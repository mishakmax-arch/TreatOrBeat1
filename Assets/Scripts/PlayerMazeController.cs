using UnityEngine;

/// <summary>
/// Движение игрока по сетке лабиринта: WASD / стрелки.
/// Игрок ходит строго по клеткам сетки (1 клетка = 1 юнит).
/// Умеет переживать пересоздание стен (активация алтаря).
/// </summary>
public class PlayerMazeController : MonoBehaviour
{
    public MazeGenerator maze;
    public float speed = 6f;

    bool moving;
    Vector3 target;

    /// true - игрок не реагирует на ввод (например, пока активируется алтарь).
    public bool InputLocked { get; set; }

    /// true - игрок сейчас идёт между клетками (стоит не точно в клетке).
    public bool IsMoving { get { return moving; } }

    // ---------- данные для монстра ----------
    public float Speed { get { return speed; } }
    public Vector2Int LastDir { get; private set; }
    public float LastMoveTime { get; private set; } = -999f;

    /// Клетка, в которой игрок находится, а если идёт, то в которую идёт.
    public Vector2Int CurrentCell
    {
        get { return maze.WorldToGrid(moving ? target : transform.position); }
    }

    void OnEnable()
    {
        if (maze == null) return;
        maze.OnMazeGenerated += ResetMovement;
        maze.OnMazeRebuilt += HandleRebuilt;
    }

    void OnDisable()
    {
        if (maze == null) return;
        maze.OnMazeGenerated -= ResetMovement;
        maze.OnMazeRebuilt -= HandleRebuilt;
    }

    // новая карта целиком: игрок телепортирован на вход
    void ResetMovement()
    {
        moving = false;
        LastDir = Vector2Int.zero;
        LastMoveTime = -999f;
    }

    // стены поменялись, игрок остался на месте: если шёл в клетку, которая стала стеной - останавливаем
    void HandleRebuilt()
    {
        if (!moving) return;
        if (IsFree(maze.WorldToGrid(target))) return; // цель всё ещё проходима

        Vector2Int c = maze.WorldToGrid(transform.position);
        Vector3 w = maze.CellToWorld(c);
        transform.position = new Vector3(w.x, w.y, transform.position.z);
        moving = false;
    }

    void Update()
    {
        if (maze == null || maze.Builder == null || maze.Builder.Grid == null) return;

        if (moving)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            if ((transform.position - target).sqrMagnitude < 0.0001f)
            {
                transform.position = target;
                moving = false;
            }
            else return;
        }

        if (InputLocked) return; // начатый шаг доходит до конца, новый не начинается

        int h = (int)Input.GetAxisRaw("Horizontal");
        int v = (int)Input.GetAxisRaw("Vertical");
        if (h == 0 && v == 0) return;

        // в сетке y=0 - верх экрана, поэтому "вверх" = y-1
        Vector2Int dir = h != 0 ? new Vector2Int(h, 0) : new Vector2Int(0, -v);

        Vector2Int cell = maze.WorldToGrid(transform.position);
        Vector2Int next = cell + dir;

        if (!IsFree(next)) return;

        Vector3 w2 = maze.CellToWorld(next);
        target = new Vector3(w2.x, w2.y, transform.position.z);
        moving = true;

        LastDir = dir;
        LastMoveTime = Time.time;
    }

    bool IsFree(Vector2Int c)
    {
        var g = maze.Builder.Grid;
        if (c.x < 0 || c.y < 0 || c.x >= maze.Builder.Width || c.y >= maze.Builder.Height) return false;
        return g[c.x, c.y];
    }
}