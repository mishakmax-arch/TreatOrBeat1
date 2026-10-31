using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Туман войны для монстра: зона обнаружения 5 тайлов по 64 px.
/// Видимость проверяется по сетке MazeGenerator.Builder.Grid —
/// сквозь стены цель не видна.
/// Пока цель не обнаружена, все её Renderer'ы (спрайты, анимации, дочерние объекты) скрыты.
///
/// Повесить на игрока, targetTag = "Monster".
/// </summary>
public class DetectionWarFog : MonoBehaviour
{
    public enum ZoneShape
    {
        Circle,   // обычный радиус
        Square,   // квадрат 11x11 тайлов
        Diamond   // ромб: шаги по тайлам без диагоналей
    }

    [Header("Лабиринт")]
    [Tooltip("Если пусто — найдётся в сцене автоматически")]
    [SerializeField] private MazeGenerator maze;

    [Header("Размер зоны")]
    [Tooltip("Радиус зоны в тайлах (тайл = 64 px)")]
    [SerializeField] private int detectionCells = 5;
    [SerializeField] private ZoneShape shape = ZoneShape.Circle;

    [Header("Цель")]
    [SerializeField] private string targetTag = "Monster";
    [Tooltip("Если пусто — цель найдётся по тегу")]
    [SerializeField] private Transform target;

    [Header("Стены")]
    [Tooltip("Не видеть цель сквозь стены")]
    [SerializeField] private bool blockedByWalls = true;

    [Header("Туман войны")]
    [Tooltip("Прятать спрайты цели, пока она не обнаружена")]
    [SerializeField] private bool hideTargetWhenNotSeen = true;

    [Header("Проверка")]
    [Tooltip("Как часто проверять (сек). 0 — каждый кадр")]
    [SerializeField] private float checkInterval = 0.1f;

    [Header("События")]
    public UnityEvent<Transform> onTargetDetected;
    public UnityEvent<Transform> onTargetLost;

    public bool IsTargetDetected { get; private set; }
    public Vector2 LastKnownTargetPosition { get; private set; }

    /// <summary>Размер тайла в юнитах (64 px / 64 PPU = 1).</summary>
    public float TileSize => maze != null ? maze.TileSize : 1f;
    public float RadiusUnits => detectionCells * TileSize;

    private float timer;
    private Transform cachedTarget;
    private readonly List<Renderer> targetRenderers = new List<Renderer>();

    private void Awake()
    {
        if (maze == null)
        {
#if UNITY_2023_1_OR_NEWER
            maze = FindFirstObjectByType<MazeGenerator>();
#else
            maze = FindObjectOfType<MazeGenerator>();
#endif
        }
    }

    private void Start()
    {
        if (target == null) FindTarget();
        CacheTargetRenderers();
        SetTargetVisible(false); // с самого начала монстр скрыт
    }

    private void Update()
    {
        if (target == null) FindTarget();
        if (target == null) return;
        if (target != cachedTarget)
        {
            CacheTargetRenderers();
            SetTargetVisible(IsTargetDetected);
        }
        if (!MazeReady()) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = checkInterval;

        bool detected = CanSee(target.position);

        if (detected)
            LastKnownTargetPosition = target.position;

        if (detected && !IsTargetDetected)
        {
            IsTargetDetected = true;
            SetTargetVisible(true);
            onTargetDetected?.Invoke(target);
        }
        else if (!detected && IsTargetDetected)
        {
            IsTargetDetected = false;
            SetTargetVisible(false);
            onTargetLost?.Invoke(target);
        }
    }

    private void OnDisable()
    {
        // если компонент выключили — показать монстра обратно
        SetTargetVisible(true);
    }

    private void FindTarget()
    {
        if (string.IsNullOrEmpty(targetTag)) return;
        GameObject go = GameObject.FindGameObjectWithTag(targetTag);
        if (go != null) target = go.transform;
    }

    // ---------- скрытие цели ----------

    private void CacheTargetRenderers()
    {
        cachedTarget = target;
        targetRenderers.Clear();
        if (target != null)
            target.GetComponentsInChildren(true, targetRenderers);
    }

    private void SetTargetVisible(bool visible)
    {
        if (!hideTargetWhenNotSeen && !visible) return;
        foreach (Renderer r in targetRenderers)
            if (r != null) r.enabled = visible;
    }

    // ---------- проверка зоны ----------

    private bool MazeReady()
    {
        return maze != null && maze.Builder != null && maze.Builder.Grid != null;
    }

    /// <summary>Цель в зоне и (если включено) не закрыта стенами.</summary>
    public bool CanSee(Vector3 worldPos)
    {
        if (!MazeReady()) return false;

        Vector2Int from = maze.WorldToGrid(transform.position);
        Vector2Int to = maze.WorldToGrid(worldPos);

        if (!IsInZone(from, to, worldPos)) return false;
        if (!blockedByWalls) return true;
        return HasLineOfSight(from, to);
    }

    private bool IsInZone(Vector2Int from, Vector2Int to, Vector3 worldPos)
    {
        int dx = Mathf.Abs(to.x - from.x);
        int dy = Mathf.Abs(to.y - from.y);

        switch (shape)
        {
            case ZoneShape.Square: return Mathf.Max(dx, dy) <= detectionCells;
            case ZoneShape.Diamond: return dx + dy <= detectionCells;
            default:
                float r = RadiusUnits;
                return ((Vector2)(transform.position - worldPos)).sqrMagnitude <= r * r;
        }
    }

    private bool IsOpen(int x, int y)
    {
        bool[,] g = maze.Builder.Grid;
        if (x < 0 || y < 0 || x >= g.GetLength(0) || y >= g.GetLength(1)) return false;
        return g[x, y]; // true = проход, false = стена
    }

    /// <summary>
    /// Проходит все тайлы между клетками (supercover-линия).
    /// Любая стена на пути — обзор закрыт.
    /// Через угол тайла луч проходит, только если хотя бы один из двух соседних тайлов — проход.
    /// </summary>
    private bool HasLineOfSight(Vector2Int a, Vector2Int b)
    {
        if (!IsOpen(a.x, a.y) || !IsOpen(b.x, b.y)) return false;

        int x = a.x, y = a.y;
        int dx = Mathf.Abs(b.x - a.x), dy = Mathf.Abs(b.y - a.y);
        int sx = b.x > a.x ? 1 : -1;
        int sy = b.y > a.y ? 1 : -1;

        int n = dx + dy;
        int error = dx - dy;
        dx *= 2;
        dy *= 2;

        while (n > 0)
        {
            if (error > 0)
            {
                x += sx; error -= dy; n--;
            }
            else if (error < 0)
            {
                y += sy; error += dx; n--;
            }
            else
            {
                if (!IsOpen(x + sx, y) && !IsOpen(x, y + sy)) return false;
                x += sx; y += sy; error += dx - dy; n -= 2;
            }

            if (!IsOpen(x, y)) return false;
        }
        return true;
    }

    // ---------- отладка в окне Scene ----------
    private void OnDrawGizmosSelected()
    {
        Vector3 c = transform.position;
        float r = RadiusUnits;

        Gizmos.color = IsTargetDetected ? new Color(1f, 0.2f, 0.2f, 0.8f)
                                        : new Color(1f, 0.85f, 0.1f, 0.8f);
        switch (shape)
        {
            case ZoneShape.Square:
                Gizmos.DrawWireCube(c, new Vector3(r * 2f + TileSize, r * 2f + TileSize, 0f));
                break;
            case ZoneShape.Diamond:
                Vector3 up = c + Vector3.up * r, right = c + Vector3.right * r;
                Vector3 down = c + Vector3.down * r, left = c + Vector3.left * r;
                Gizmos.DrawLine(up, right); Gizmos.DrawLine(right, down);
                Gizmos.DrawLine(down, left); Gizmos.DrawLine(left, up);
                break;
            default:
                const int seg = 48;
                Vector3 prev = c + new Vector3(r, 0f, 0f);
                for (int i = 1; i <= seg; i++)
                {
                    float ang = i * Mathf.PI * 2f / seg;
                    Vector3 next = c + new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }
                break;
        }

        if (Application.isPlaying && target != null && MazeReady())
        {
            Gizmos.color = CanSee(target.position) ? Color.green : Color.gray;
            Gizmos.DrawLine(c, target.position);
        }
    }
}