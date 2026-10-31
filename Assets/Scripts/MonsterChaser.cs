using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Монстр: преследует игрока по лабиринту с упреждением (перехват).
/// Ходит по клеткам сетки, поиск пути - BFS (плоские массивы, без очистки, с ранним выходом).
/// Появляется заново:
///   - после полной генерации (OnMazeGenerated),
///   - после активации алтаря и пересоздания стен (OnMazeRebuilt).
/// </summary>
public class MonsterChaser : MonoBehaviour
{
    public GameManager gm;

    [Header("Ссылки")]
    public MazeGenerator maze;
    public PlayerMazeController player;

    [Header("Движение")]
    public float speed = 5.5f;          // у игрока 6
    public float spawnDelay = 2f;       // сколько монстр стоит после появления
    public float catchDistance = 0.5f;  // дистанция поимки в мировых единицах

    [Header("Ускорение от алтарей")]
    public float speedPerAltar = 0.5f;  // прибавка к скорости за каждый активированный алтарь
    public float maxSpeed = 0f;         // потолок скорости; 0 = без ограничения

    [Header("Спавн")]
    public int minSpawnPathDist = 30;
    public bool avoidLastSpawn = true;
    public bool spawnOnEntranceExit = true;

    [Header("Упреждение")]
    public int maxLeadCells = 14;
    public float headingMemory = 0.4f;
    [Range(0f, 1f)]
    public float leadStrength = 1f;     // 0 = простая погоня

    [Header("Отладка")]
    public bool drawGizmos = false;     // выключено: Gizmos в редакторе тоже стоят кадров

    public event Action OnCaught;
    public event Action OnSpawned;

    int altarsActivated;

    /// Текущая скорость: базовая (speed) + speedPerAltar за каждый активированный алтарь.
    public float CurrentSpeed
    {
        get
        {
            float s = speed + speedPerAltar * altarsActivated;
            return maxSpeed > 0f ? Mathf.Min(s, maxSpeed) : s;
        }
    }

    /// Монстр появился и ещё не поймал игрока.
    public bool IsActive { get { return active && !caught; } }

    /// Длина кратчайшего пути до игрока в клетках (обновляется на каждом шаге), -1 если неизвестна.
    public int PathDistanceToPlayer { get; private set; } = -1;

    static readonly int[] DX = { 0, 0, -1, 1 };
    static readonly int[] DY = { -1, 1, 0, 0 };
    static readonly Vector2Int[] DIRS =
    {
        new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0)
    };

    // состояние
    bool pendingSpawn, active, moving, caught;
    float activateTime, nextPlanTime;
    Vector2Int cell;
    Vector2Int lastSpawn = new Vector2Int(-1, -1);
    Vector3 moveTarget;
    Renderer[] renderers;

    // плоская копия карты и буферы BFS (индекс = x * H + y)
    bool[,] walkSource;
    int W, H;
    bool[] walk;
    int[] dist, prevIdx, visit, targetMark, queueArr;
    int stamp;

    readonly List<Vector2Int> targets = new List<Vector2Int>();

    // отладка
    readonly List<Vector2Int> trajectory = new List<Vector2Int>();
    Vector2Int interceptCell;

    // ------------------------------------------------------------------
    void Awake()
    {
        if (gm == null) gm = GetComponent<GameManager>();

        renderers = GetComponentsInChildren<Renderer>(true);
        SetVisible(false);
    }

    void OnEnable()
    {
        if (maze == null) return;
        maze.OnMazeGenerated += HandleGenerated;
        maze.OnMazeRebuilt += HandleRebuilt;
    }

    void OnDisable()
    {
        if (maze == null) return;
        maze.OnMazeGenerated -= HandleGenerated;
        maze.OnMazeRebuilt -= HandleRebuilt;
    }

    void Start()
    {
        if (maze != null && maze.Builder != null && maze.Builder.Grid != null) ScheduleSpawn();
    }

    void HandleGenerated()
    {
        lastSpawn = new Vector2Int(-1, -1);
        altarsActivated = 0;   // новая игра/новые алтари - скорость сбрасывается
        ScheduleSpawn();
    }

    void HandleRebuilt()
    {
        altarsActivated++;     // один алтарь = одна перестройка
        ScheduleSpawn();
    }

    public void Respawn() { ScheduleSpawn(); }

    void ScheduleSpawn()
    {
        active = false;
        moving = false;
        caught = false;
        trajectory.Clear();
        SetVisible(false);
        PathDistanceToPlayer = -1;
        pendingSpawn = true;
    }

    void SetVisible(bool v)
    {
        if (renderers == null) return;
        foreach (var r in renderers) if (r != null) r.enabled = v;
    }

    // ------------------------------------------------------------------
    void Update()
    {
        if (maze == null || player == null || maze.Builder == null || maze.Builder.Grid == null) return;

        SyncGrid();

        if (pendingSpawn) { pendingSpawn = false; Spawn(); return; }
        if (!active || caught) return;
        if (Time.time < activateTime) return;

        // поимка
        Vector3 pp = player.transform.position;
        float ddx = pp.x - transform.position.x, ddy = pp.y - transform.position.y;
        if (ddx * ddx + ddy * ddy <= catchDistance * catchDistance)
        {
            caught = true;
            if (gm != null) gm.GameOver();
            OnCaught?.Invoke();
            return;
        }

        // движение к следующей клетке
        if (moving)
        {
            transform.position = Vector3.MoveTowards(transform.position, moveTarget, CurrentSpeed * Time.deltaTime);
            if ((transform.position - moveTarget).sqrMagnitude > 0.0001f) return;
            transform.position = moveTarget;
            moving = false;
        }

        // не планируем чаще, чем раз в 0.1 с после неудачи (иначе BFS каждый кадр)
        if (Time.time < nextPlanTime) return;

        Vector2Int next;
        if (!PlanNextStep(out next)) { nextPlanTime = Time.time + 0.1f; return; }

        cell = next;
        Vector3 w = maze.CellToWorld(next);
        moveTarget = new Vector3(w.x, w.y, transform.position.z);
        moving = true;
    }

    // ------------------------------------------------------------------
    // Спавн: случайный алтарь, далёкий от игрока по пути в НОВОМ лабиринте
    void Spawn()
    {
        var points = maze.WaypointCells;
        if (points == null || points.Count == 0) return;

        Vector2Int playerCell = player.CurrentCell;
        if (!IsFree(playerCell)) { pendingSpawn = true; return; }

        Bfs(playerCell, null); // один раз за спавн - полный обход

        var candidates = new List<Vector2Int>();
        Vector2Int farthest = playerCell;
        int farthestD = -1;

        for (int i = 0; i < points.Count; i++)
        {
            if (!spawnOnEntranceExit && (i == maze.EntranceIndex || i == maze.ExitIndex)) continue;

            Vector2Int p = points[i];
            if (!IsFree(p)) continue;
            int dd = DistTo(p);
            if (dd < 0) continue;

            if (dd > farthestD) { farthestD = dd; farthest = p; }
            if (dd >= minSpawnPathDist) candidates.Add(p);
        }

        if (farthestD < 0) return;

        if (avoidLastSpawn && candidates.Count > 1) candidates.Remove(lastSpawn);

        Vector2Int chosen = candidates.Count > 0
            ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
            : farthest;

        lastSpawn = chosen;
        cell = chosen;

        Vector3 w = maze.CellToWorld(cell);
        transform.position = new Vector3(w.x, w.y, transform.position.z);

        SetVisible(true);
        active = true;
        nextPlanTime = 0f;
        activateTime = Time.time + spawnDelay;
        if (OnSpawned != null) OnSpawned();
    }

    // ------------------------------------------------------------------
    // Упреждение + BFS с ранним выходом
    bool PlanNextStep(out Vector2Int next)
    {
        next = cell;

        Vector2Int playerCell = player.CurrentCell;
        if (!IsFree(playerCell)) return false;

        bool lead = leadStrength > 0f && Time.time - player.LastMoveTime < headingMemory;
        if (lead) BuildTrajectory(playerCell, player.LastDir, maxLeadCells);
        else trajectory.Clear();

        // BFS нужен только до игрока и клеток его предсказанной траектории
        targets.Clear();
        targets.Add(playerCell);
        for (int i = 0; i < trajectory.Count; i++) targets.Add(trajectory[i]);

        Bfs(cell, targets);

        PathDistanceToPlayer = DistTo(playerCell);

        Vector2Int target = lead ? ChooseIntercept(playerCell) : playerCell;
        interceptCell = target;

        if (target == cell) target = playerCell;
        if (target == cell) return false;
        if (DistTo(target) < 0) return false;

        // идём назад от цели до соседа текущей клетки
        int startIdx = cell.x * H + cell.y;
        int cur = target.x * H + target.y;
        while (prevIdx[cur] != startIdx)
        {
            cur = prevIdx[cur];
            if (cur < 0) return false;
        }
        next = new Vector2Int(cur / H, cur % H);
        return true;
    }

    void BuildTrajectory(Vector2Int start, Vector2Int dir, int steps)
    {
        trajectory.Clear();
        trajectory.Add(start);

        Vector2Int c = start;
        Vector2Int d = dir;

        for (int i = 0; i < steps; i++)
        {
            Vector2Int fwd = c + d;
            if (IsFree(fwd))
            {
                c = fwd;
            }
            else
            {
                Vector2Int turn = Vector2Int.zero;
                int options = 0;
                for (int k = 0; k < 4; k++)
                {
                    Vector2Int nd = DIRS[k];
                    if (nd == -d) continue;
                    if (IsFree(c + nd)) { turn = nd; options++; }
                }
                if (options == 1) { d = turn; c = c + d; }
                else break;
            }
            trajectory.Add(c);
        }
    }

    Vector2Int ChooseIntercept(Vector2Int playerCell)
    {
        float playerSpeed = Mathf.Max(0.01f, player.Speed);
        float monsterSpeed = Mathf.Max(0.01f, CurrentSpeed);

        Vector2Int best = playerCell;
        float bestSlack = float.MaxValue;

        for (int k = 0; k < trajectory.Count; k++)
        {
            Vector2Int p = trajectory[k];
            int dm = DistTo(p);
            if (dm < 0) continue;

            float tMonster = dm / monsterSpeed;
            float tPlayer = k * leadStrength / playerSpeed;
            float slack = tMonster - tPlayer;

            if (slack <= 0f) return p;
            if (slack < bestSlack) { bestSlack = slack; best = p; }
        }
        return best;
    }

    // ------------------------------------------------------------------
    // Плоская копия карты: обновляется только когда Builder.Grid сменился (перестройка/генерация)
    void SyncGrid()
    {
        var b = maze.Builder;
        var g = b.Grid;
        if (g == walkSource) return;
        walkSource = g;

        W = b.Width;
        H = b.Height;
        int n = W * H;
        if (walk == null || walk.Length != n)
        {
            walk = new bool[n];
            dist = new int[n];
            prevIdx = new int[n];
            visit = new int[n];
            targetMark = new int[n];
            queueArr = new int[n];
            stamp = 0;
        }
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                walk[x * H + y] = g[x, y];
    }

    int DistTo(Vector2Int c)
    {
        if (c.x < 0 || c.y < 0 || c.x >= W || c.y >= H) return -1;
        int i = c.x * H + c.y;
        return visit[i] == stamp ? dist[i] : -1;
    }

    bool IsFree(Vector2Int c)
    {
        if (c.x < 0 || c.y < 0 || c.x >= W || c.y >= H) return false;
        return walk[c.x * H + c.y];
    }

    /// BFS без очистки буферов (посещено = visit[i] == stamp).
    /// Если задан список целей, обход останавливается, как только найдены все.
    void Bfs(Vector2Int from, List<Vector2Int> goals)
    {
        stamp++;

        int remaining = 0;
        if (goals != null)
        {
            for (int i = 0; i < goals.Count; i++)
            {
                Vector2Int g = goals[i];
                if (g.x < 0 || g.y < 0 || g.x >= W || g.y >= H) continue;
                int gi = g.x * H + g.y;
                if (!walk[gi] || targetMark[gi] == stamp) continue;
                targetMark[gi] = stamp;
                remaining++;
            }
        }

        int start = from.x * H + from.y;
        int head = 0, tail = 0;
        visit[start] = stamp;
        dist[start] = 0;
        prevIdx[start] = -1;
        queueArr[tail++] = start;
        if (goals != null && targetMark[start] == stamp) remaining--;

        while (head < tail)
        {
            if (goals != null && remaining <= 0) break;

            int c = queueArr[head++];
            int cx = c / H, cy = c - cx * H;
            int nd = dist[c] + 1;

            for (int k = 0; k < 4; k++)
            {
                int nx = cx + DX[k], ny = cy + DY[k];
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                int n = nx * H + ny;
                if (!walk[n] || visit[n] == stamp) continue;

                visit[n] = stamp;
                dist[n] = nd;
                prevIdx[n] = c;
                queueArr[tail++] = n;
                if (goals != null && targetMark[n] == stamp) remaining--;
            }
        }
    }

    // ------------------------------------------------------------------
    void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying || maze == null || trajectory.Count == 0) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < trajectory.Count; i++)
            Gizmos.DrawSphere(maze.CellToWorld(trajectory[i]), 0.12f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(maze.CellToWorld(interceptCell), 0.35f);
    }
}