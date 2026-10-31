using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Монстр: преследует игрока по лабиринту с упреждением (перехват).
/// Ходит по клеткам сетки, поиск пути - BFS.
/// Появляется заново:
///   - после полной генерации (OnMazeGenerated),
///   - после активации алтаря и пересоздания стен (OnMazeRebuilt).
/// Точка появления: случайный алтарь (waypoint), далёкий от игрока по реальному пути.
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

    [Header("Спавн")]
    public int minSpawnPathDist = 30;       // минимум клеток по пути от игрока
    public bool avoidLastSpawn = true;      // не появляться подряд на одной точке
    public bool spawnOnEntranceExit = true; // разрешить появление на входе/выходе

    [Header("Упреждение")]
    public int maxLeadCells = 14;
    public float headingMemory = 0.4f;
    [Range(0f, 1f)]
    public float leadStrength = 1f;     // 0 = простая погоня

    [Header("Отладка")]
    public bool drawGizmos = true;

    public event Action OnCaught;
    public event Action OnSpawned;

    static readonly Vector2Int[] DIRS =
    {
        new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0)
    };

    // состояние
    bool pendingSpawn, active, moving, caught;
    float activateTime;
    Vector2Int cell;
    Vector2Int lastSpawn = new Vector2Int(-1, -1);
    Vector3 moveTarget;
    Renderer[] renderers;

    // BFS-буферы
    int[,] dist;
    Vector2Int[,] prev;
    readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();

    // отладка
    readonly List<Vector2Int> trajectory = new List<Vector2Int>();
    Vector2Int interceptCell;

    // ------------------------------------------------------------------
    void Awake()
    {
        if (gm == null) gm = GetComponent<GameManager>();

        renderers = GetComponentsInChildren<Renderer>(true);
        SetVisible(false); // пока не появился - скрыт
    }

    void OnEnable()
    {
        if (maze == null) return;
        maze.OnMazeGenerated += HandleGenerated; // полная генерация: новые алтари
        maze.OnMazeRebuilt += HandleRebuilt;     // алтарь активирован: новые стены, те же алтари
    }

    void OnDisable()
    {
        if (maze == null) return;
        maze.OnMazeGenerated -= HandleGenerated;
        maze.OnMazeRebuilt -= HandleRebuilt;
    }

    void Start()
    {
        // карта могла быть построена раньше, чем включился скрипт
        if (maze != null && maze.Builder != null && maze.Builder.Grid != null) ScheduleSpawn();
    }

    void HandleGenerated()
    {
        lastSpawn = new Vector2Int(-1, -1); // точки новые, старую запоминать нет смысла
        ScheduleSpawn();
    }

    void HandleRebuilt()
    {
        ScheduleSpawn();
    }

    /// Можно вызвать вручную (например, после поимки игрока).
    public void Respawn()
    {
        ScheduleSpawn();
    }

    // Сбрасываем всё старое (оно строилось по старым стенам), спавн произойдёт в Update
    void ScheduleSpawn()
    {
        active = false;
        moving = false;
        caught = false;
        trajectory.Clear();
        SetVisible(false);
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

        if (pendingSpawn) { pendingSpawn = false; Spawn(); return; }
        if (!active || caught) return;
        if (Time.time < activateTime) return;

        // поимка
        Vector3 pp = player.transform.position;
        Vector2 d = new Vector2(pp.x - transform.position.x, pp.y - transform.position.y);
        if (d.magnitude <= catchDistance)
        {
            caught = true;
            gm.GameOver();
            OnCaught?.Invoke();
            return;
        }

        // движение к следующей клетке
        if (moving)
        {
            transform.position = Vector3.MoveTowards(transform.position, moveTarget, speed * Time.deltaTime);
            if ((transform.position - moveTarget).sqrMagnitude > 0.0001f) return;
            transform.position = moveTarget;
            moving = false;
        }

        // пришли в клетку - планируем следующий шаг
        Vector2Int next;
        if (!PlanNextStep(out next)) return;

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
        // игрок ещё не стоит на проходе (не успел встать на место) - повторим в следующем кадре
        if (!IsFree(playerCell)) { pendingSpawn = true; return; }

        EnsureBuffers();
        Bfs(playerCell);

        var candidates = new List<Vector2Int>();
        Vector2Int farthest = playerCell;
        int farthestD = -1;

        for (int i = 0; i < points.Count; i++)
        {
            if (!spawnOnEntranceExit && (i == maze.EntranceIndex || i == maze.ExitIndex)) continue;

            Vector2Int p = points[i];
            if (!IsFree(p)) continue;
            int dd = dist[p.x, p.y];
            if (dd < 0) continue; // недостижима

            if (dd > farthestD) { farthestD = dd; farthest = p; }
            if (dd >= minSpawnPathDist) candidates.Add(p);
        }

        if (farthestD < 0) return; // нет ни одной подходящей точки

        if (avoidLastSpawn && candidates.Count > 1) candidates.Remove(lastSpawn);

        // если все точки ближе порога (маленькая карта) - берём самую дальнюю
        Vector2Int chosen = candidates.Count > 0
            ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
            : farthest;

        lastSpawn = chosen;
        cell = chosen;

        Vector3 w = maze.CellToWorld(cell);
        transform.position = new Vector3(w.x, w.y, transform.position.z);

        SetVisible(true);
        active = true;
        activateTime = Time.time + spawnDelay;
        if (OnSpawned != null) OnSpawned();
    }

    // ------------------------------------------------------------------
    // Упреждение + BFS
    bool PlanNextStep(out Vector2Int next)
    {
        next = cell;
        EnsureBuffers();

        Bfs(cell);

        Vector2Int playerCell = player.CurrentCell;
        if (!IsFree(playerCell)) return false;
        Vector2Int target = playerCell;

        if (leadStrength > 0f && Time.time - player.LastMoveTime < headingMemory)
        {
            BuildTrajectory(playerCell, player.LastDir, maxLeadCells);
            target = ChooseIntercept(playerCell);
        }
        else
        {
            trajectory.Clear();
        }
        interceptCell = target;

        if (target == cell) target = playerCell;
        if (target == cell) return false;
        if (dist[target.x, target.y] < 0) return false;

        Vector2Int cur = target;
        while (prev[cur.x, cur.y] != cell)
        {
            cur = prev[cur.x, cur.y];
            if (cur.x < 0) return false;
        }
        next = cur;
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
                foreach (var nd in DIRS)
                {
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
        float monsterSpeed = Mathf.Max(0.01f, speed);

        Vector2Int best = playerCell;
        float bestSlack = float.MaxValue;

        for (int k = 0; k < trajectory.Count; k++)
        {
            Vector2Int p = trajectory[k];
            int dm = dist[p.x, p.y];
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
    void Bfs(Vector2Int from)
    {
        int W = maze.Builder.Width, H = maze.Builder.Height;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++) { dist[x, y] = -1; prev[x, y] = new Vector2Int(-1, -1); }

        queue.Clear();
        dist[from.x, from.y] = 0;
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            foreach (var d in DIRS)
            {
                Vector2Int n = c + d;
                if (!IsFree(n) || dist[n.x, n.y] >= 0) continue;
                dist[n.x, n.y] = dist[c.x, c.y] + 1;
                prev[n.x, n.y] = c;
                queue.Enqueue(n);
            }
        }
    }

    // Builder пересоздаётся при каждой перестройке, поэтому размер проверяем каждый раз
    void EnsureBuffers()
    {
        int W = maze.Builder.Width, H = maze.Builder.Height;
        if (dist == null || dist.GetLength(0) != W || dist.GetLength(1) != H)
        {
            dist = new int[W, H];
            prev = new Vector2Int[W, H];
        }
    }

    bool IsFree(Vector2Int c)
    {
        var b = maze.Builder;
        if (c.x < 0 || c.y < 0 || c.x >= b.Width || c.y >= b.Height) return false;
        return b.Grid[c.x, c.y];
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