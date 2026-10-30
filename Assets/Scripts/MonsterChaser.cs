using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Монстр: преследует игрока по лабиринту с упреждением (pursuit/интерсепт).
/// Двигается по клеткам сетки так же, как игрок. Поиск пути: BFS.
/// </summary>
public class MonsterChaser : MonoBehaviour
{
    [Header("Ссылки")]
    public MazeGenerator maze;
    public PlayerMazeController player;

    [Header("Движение")]
    public float speed = 5.5f;          // клеток в секунду (у игрока 6 - монстр чуть медленнее)
    public float spawnDelay = 2f;       // пауза после генерации, пока монстр стоит
    public float catchDistance = 0.5f;  // дистанция поимки в мировых единицах

    [Header("Спавн")]
    public int minSpawnPathDist = 20;    // минимальная дистанция по реальному пути от игрока (в клетках)
    public bool avoidLastSpawn = true;   // не появляться дважды подряд на той же точке

    Vector2Int lastSpawn = new Vector2Int(-1, -1);

    [Header("Упреждение")]
    public int maxLeadCells = 14;       // на сколько клеток вперёд предсказываем игрока
    public float headingMemory = 0.4f;  // сек: сколько после последнего шага считаем, что игрок "бежит"
    [Range(0f, 1f)]
    public float leadStrength = 1f;     // 0 = простая погоня, 1 = полное упреждение

    [Header("Отладка")]
    public bool drawGizmos = true;

    public event Action OnCaught;

    static readonly Vector2Int[] DIRS =
    {
        new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0)
    };

    // состояние
    bool pendingSpawn, active, moving, caught;
    float activateTime;
    Vector2Int cell;
    Vector3 moveTarget;

    // BFS-буферы
    int[,] dist;
    Vector2Int[,] prev;
    readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();

    // для отладки
    readonly List<Vector2Int> trajectory = new List<Vector2Int>();
    Vector2Int interceptCell;

    // ------------------------------------------------------------------
    void OnEnable() { if (maze != null) maze.OnMazeGenerated += HandleGenerated; }
    void OnDisable() { if (maze != null) maze.OnMazeGenerated -= HandleGenerated; }

    void Start()
    {
        // если лабиринт уже построен до включения скрипта
        if (maze != null && maze.Builder != null && maze.Builder.Grid != null) pendingSpawn = true;
    }
    void HandleGenerated()
    {
        active = false;
        moving = false;
        caught = false;
        trajectory.Clear();
        pendingSpawn = true; // спавним в следующем кадре, когда игрок уже стоит на входе
    }

    /// Можно вызвать вручную из кода "перегенерации карты", если не хочешь ждать событие.
    public void Respawn()
    {
        HandleGenerated();
    }

    // Спавн: случайная опорная точка, достаточно далёкая от игрока по реальному пути
    void Spawn()
    {
        var b = maze.Builder;
        if (b.Points == null || b.Points.Count == 0) return;

        // игрок ещё не стоит на проходе (например, не успел телепортироваться) - пробуем в следующем кадре
        if (!IsFree(player.CurrentCell)) { pendingSpawn = true; return; }

        EnsureBuffers();
        Bfs(player.CurrentCell);

        var candidates = new List<Vector2Int>();
        Vector2Int farthest = b.Points[0];
        int farthestD = -1;

        foreach (var p in b.Points)
        {
            int d = dist[p.x, p.y];
            if (d < 0) continue; // недостижима
            if (d > farthestD) { farthestD = d; farthest = p; }
            if (d >= minSpawnPathDist) candidates.Add(p);
        }

        // не появляемся на той же точке подряд, если есть выбор
        if (avoidLastSpawn && candidates.Count > 1) candidates.Remove(lastSpawn);

        // если все точки ближе порога (маленькая карта) - берём самую дальнюю
        Vector2Int chosen = candidates.Count > 0
            ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
            : farthest;

        lastSpawn = chosen;
        cell = chosen;

        Vector3 w = maze.CellToWorld(cell);
        transform.position = new Vector3(w.x, w.y, transform.position.z);

        active = true;
        activateTime = Time.time + spawnDelay;
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
            if (OnCaught != null) OnCaught();
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
    // Основной алгоритм: упреждение + BFS
    bool PlanNextStep(out Vector2Int next)
    {
        next = cell;
        EnsureBuffers();

        Bfs(cell); // расстояния от монстра до всех клеток + prev для восстановления пути

        Vector2Int playerCell = player.CurrentCell;
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

        // если уже стоим в точке перехвата - идём к самому игроку
        if (target == cell) target = playerCell;
        if (target == cell) return false;
        if (dist[target.x, target.y] < 0) return false; // недостижима (не должно быть: лабиринт связный)

        // восстанавливаем путь от target назад до соседа cell
        Vector2Int cur = target;
        while (prev[cur.x, cur.y] != cell)
        {
            cur = prev[cur.x, cur.y];
            if (cur.x < 0) return false;
        }
        next = cur;
        return true;
    }

    /// <summary>
    /// Предсказываем путь игрока: идёт вперёд; если упёрся в стену и есть
    /// единственный поворот (коридор), поворачивает. На развилке/тупике обрываем.
    /// trajectory[k] = где игрок будет через k шагов.
    /// </summary>
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
                // вперёд стена: ищем повороты (кроме разворота назад)
                Vector2Int turn = Vector2Int.zero;
                int options = 0;
                foreach (var nd in DIRS)
                {
                    if (nd == -d) continue;
                    if (IsFree(c + nd)) { turn = nd; options++; }
                }
                if (options == 1) { d = turn; c = c + d; }
                else break; // развилка или тупик - дальше не угадать
            }
            trajectory.Add(c);
        }
    }

    /// <summary>
    /// Выбираем точку перехвата: первая клетка траектории, куда монстр приходит
    /// не позже игрока. Если такой нет - с минимальным отставанием.
    /// </summary>
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
            float slack = tMonster - tPlayer; // <= 0: успеваем

            if (slack <= 0f) return p;
            if (slack < bestSlack) { bestSlack = slack; best = p; }
        }
        return best;
    }

    // ------------------------------------------------------------------
    // BFS по сетке (проход = true). Каждый шаг = 1 клетка сетки = 1 юнит.
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