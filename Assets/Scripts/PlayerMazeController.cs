using UnityEngine;

/// <summary>
/// Движение игрока по сетке лабиринта: WASD / стрелки.
/// Игрок ходит строго по клеткам сетки (1 клетка = 1 юнит).
/// Умеет переживать пересоздание стен (активация алтаря).
///
/// Буфер ввода: нажатие клавиши направления запоминается, даже если игрок ещё идёт.
/// Как только игрок дойдёт до клетки, где этот поворот возможен, он повернёт.
/// Запомненное нажатие живёт Input Buffer Seconds секунд.
/// </summary>
public class PlayerMazeController : MonoBehaviour
{
    public MazeGenerator maze;
    public float speed = 6f;

    [Header("Буфер ввода")]
    [Tooltip("Сколько секунд помнится нажатие, если поворот пока невозможен (стена). 0 = буфер выключен.")]
    public float inputBufferSeconds = 0.4f;

    bool moving;
    Vector3 target;

    // буфер ввода
    bool hasBuffer;
    Vector2Int bufferedDir;
    float bufferedTime;
    Vector2Int pressDir;       // последнее нажатое направление
    int prevH, prevV;          // оси на прошлом кадре (для определения "новое нажатие")

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
        hasBuffer = false;
        pressDir = Vector2Int.zero;
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

        // нажатия читаем каждый кадр, в том числе пока игрок идёт между клетками
        ReadInput();

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

        if (InputLocked)   // начатый шаг доходит до конца, новый не начинается
        {
            hasBuffer = false;
            return;
        }

        TryStartStep();
    }

    // Запоминает новое нажатие направления (ось только что стала ненулевой или сменила знак).
    void ReadInput()
    {
        int h = (int)Input.GetAxisRaw("Horizontal");
        int v = (int)Input.GetAxisRaw("Vertical");

        bool newH = h != 0 && h != prevH;
        bool newV = v != 0 && v != prevV;
        prevH = h;
        prevV = v;

        if (InputLocked) return;   // во время блокировки нажатия не копим

        // в сетке y=0 - верх экрана, поэтому "вверх" = y-1
        if (newV) Buffer(new Vector2Int(0, -v));
        if (newH) Buffer(new Vector2Int(h, 0));   // если нажаты обе сразу, побеждает горизонталь (как раньше)
    }

    void Buffer(Vector2Int dir)
    {
        pressDir = dir;
        bufferedDir = dir;
        bufferedTime = Time.time;
        hasBuffer = true;
    }

    // Игрок стоит в клетке: выбираем следующий шаг.
    void TryStartStep()
    {
        Vector2Int cell = maze.WorldToGrid(transform.position);
        Vector2Int dir = Vector2Int.zero;
        bool found = false;

        // 1) запомненное нажатие (поворот "заранее")
        if (hasBuffer)
        {
            if (Time.time - bufferedTime > inputBufferSeconds)
            {
                hasBuffer = false;   // протухло
            }
            else if (IsFree(cell + bufferedDir))
            {
                dir = bufferedDir;
                found = true;
                hasBuffer = false;
            }
            // иначе поворот пока невозможен: оставляем в буфере до следующей клетки
        }

        // 2) если ничего не запомнено/не вышло - продолжаем идти туда, куда зажата клавиша
        if (!found)
        {
            Vector2Int hd = prevH != 0 ? new Vector2Int(prevH, 0) : Vector2Int.zero;
            Vector2Int vd = prevV != 0 ? new Vector2Int(0, -prevV) : Vector2Int.zero;

            // сначала то направление, которое нажато последним (если ещё зажато)
            if (pressDir != Vector2Int.zero && (pressDir == hd || pressDir == vd) && IsFree(cell + pressDir))
            {
                dir = pressDir;
                found = true;
            }
            else if (hd != Vector2Int.zero && IsFree(cell + hd))
            {
                dir = hd;
                found = true;
            }
            else if (vd != Vector2Int.zero && IsFree(cell + vd))
            {
                dir = vd;
                found = true;
            }
        }

        if (!found) return;

        Vector3 w2 = maze.CellToWorld(cell + dir);
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