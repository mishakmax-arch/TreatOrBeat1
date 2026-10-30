using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 2D-версия для Unity: лабиринт 50x50 клеток на Tilemap, каждый тайл 64x64 пикселя.
/// Вешается на пустой GameObject (рядом с MazeBuilder.cs, который на объект вешать НЕ нужно).
///
/// Размер сетки: клетка лабиринта = проход (1 тайл) + стена (1 тайл), поэтому
/// 50x50 клеток -> сетка тайлов 101x101 (6464x6464 пикселей при тайле 64 px).
/// </summary>
public class MazeGenerator : MonoBehaviour
{
    [Header("Размер лабиринта (в клетках)")]
    public int cellsX = 50;
    public int cellsY = 50;

    [Header("Тайлы")]
    public int tilePixels = 64;         // размер одного тайла в пикселях
    public int pixelsPerUnit = 64;      // 64 px = 1 юнит Unity (тайл = 1x1 юнит)
    public Sprite wallSprite;           // если пусто - генерируется 64x64 спрайт
    public Sprite floorSprite;
    public Sprite waypointSprite;
    public Color wallColor = new Color(0.15f, 0.16f, 0.2f);
    public Color floorColor = new Color(0.85f, 0.82f, 0.72f);
    public Color waypointColor = new Color(0.95f, 0.3f, 0.25f);
    public bool addColliders = true;    // TilemapCollider2D на стенах

    [Header("Параметры генерации")]
    [Range(1, 12)] public int variants = 5;
    [Range(2, 9)] public int waypointsCount = 5;
    public int minPointDist = 25;       // между waypoint'ами (в тайлах); на малых картах снижается само
    public float detourMin = 1.3f;
    public float detourMax = 1.8f;

    [Header("Случайность")]
    public bool useRandomSeed = true;   // каждый запуск - новый лабиринт и новые waypoint'ы
    public int seed = 1;
    public bool shuffleWaypointOrder = true; // порядок номеров waypoint'ов тоже случайный

    [Header("Камера и игрок")]
    public bool fitCamera = true;
    public Transform player;            // ставится на случайный waypoint

    [Header("Пошаговый показ")]
    public bool animateStages = false;
    public float stageDelay = 0.4f;

    [Header("Запуск")]
    public bool generateOnStart = true;

    public MazeBuilder Builder { get; private set; }
    public List<Transform> Waypoints { get; private set; }
    public List<Vector2Int> WaypointCells { get; private set; } // клетки сетки, тот же порядок, что и Waypoints

    /// <summary>Вызывается, когда лабиринт и waypoint'ы полностью готовы.</summary>
    public event System.Action OnMazeGenerated;

    // размер тайла в мировых единицах: 64 / 64 = 1
    public float TileSize { get { return (float)tilePixels / pixelsPerUnit; } }

    Transform root;
    Tilemap tilemap;
    Tile wallTile, floorTile;
    Sprite waypointSpriteRuntime;

    void Start()
    {
        if (generateOnStart) Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        StopAllCoroutines();
        if (useRandomSeed) seed = Random.Range(0, int.MaxValue);

        Builder = new MazeBuilder
        {
            cellsX = cellsX,
            cellsY = cellsY,
            variants = variants,
            pointsCount = waypointsCount,
            minPointDist = minPointDist,
            detourMin = detourMin,
            detourMax = detourMax,
            seed = seed
        };

        PrepareScene();

        if (animateStages)
        {
            var frames = new List<KeyValuePair<string, bool[,]>>();
            Builder.OnStage = (title, grid) => frames.Add(new KeyValuePair<string, bool[,]>(title, grid));
            Builder.Build();
            StartCoroutine(PlayFrames(frames));
        }
        else
        {
            Builder.Build();
            DrawGrid(Builder.Grid);
            SpawnWaypoints();
            PlacePlayer();
            ReportResult();
        }
    }

    IEnumerator PlayFrames(List<KeyValuePair<string, bool[,]>> frames)
    {
        foreach (var f in frames)
        {
            Debug.Log(f.Key);
            DrawGrid(f.Value);
            yield return new WaitForSeconds(stageDelay);
        }
        SpawnWaypoints();
        PlacePlayer();
        ReportResult();
    }

    void ReportResult()
    {
        if (Builder.Warning != null) Debug.LogWarning(Builder.Warning);
        var r = Builder.Verify();
        Debug.Log("Maze seed " + seed + " (" + cellsX + "x" + cellsY + "): связно=" + r.connected +
                  ", все waypoint'ы достижимы=" + r.allPointsReachable + ", тупиков=" + r.deadEnds);
        if (OnMazeGenerated != null) OnMazeGenerated();
    }

    // ---------- подготовка Grid + Tilemap ----------
    void PrepareScene()
    {
        if (root != null) Destroy(root.gameObject);

        var rootGo = new GameObject("Maze");
        root = rootGo.transform;
        root.SetParent(transform, false);

        var grid = rootGo.AddComponent<Grid>();
        grid.cellSize = new Vector3(TileSize, TileSize, 1f);

        var tmGo = new GameObject("Tiles");
        tmGo.transform.SetParent(root, false);
        tilemap = tmGo.AddComponent<Tilemap>();
        var tr = tmGo.AddComponent<TilemapRenderer>();
        tr.sortingOrder = 0;

        if (addColliders) tmGo.AddComponent<TilemapCollider2D>();

        wallTile = MakeTile(wallSprite != null ? wallSprite : MakeSprite(wallColor, true), true);
        floorTile = MakeTile(floorSprite != null ? floorSprite : MakeSprite(floorColor, false), false);
        waypointSpriteRuntime = waypointSprite != null ? waypointSprite : MakeCircleSprite(waypointColor);
    }

    Tile MakeTile(Sprite s, bool solid)
    {
        var t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = s;
        t.colliderType = solid ? Tile.ColliderType.Grid : Tile.ColliderType.None;
        return t;
    }

    // Генерация 64x64 спрайта, если свой не задан
    Sprite MakeSprite(Color c, bool withBorder)
    {
        int n = tilePixels;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color[n * n];
        Color edge = Color.Lerp(c, Color.black, 0.25f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool border = x < 2 || y < 2 || x >= n - 2 || y >= n - 2;
                px[y * n + x] = (withBorder && border) ? edge : c;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), pixelsPerUnit);
    }

    Sprite MakeCircleSprite(Color c)
    {
        int n = tilePixels;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color[n * n];
        float r = n * 0.32f, cx = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cx) * (y - cx));
                px[y * n + x] = d <= r ? c : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), pixelsPerUnit);
    }

    // ---------- отрисовка ----------
    // y инвертируем, чтобы лабиринт на экране выглядел так же, как в консоли (y=0 сверху)
    Vector3Int ToCell(int x, int y)
    {
        return new Vector3Int(x, Builder.Height - 1 - y, 0);
    }

    void DrawGrid(bool[,] g)
    {
        int W = g.GetLength(0), H = g.GetLength(1);
        var pos = new Vector3Int[W * H];
        var tiles = new TileBase[W * H];
        int i = 0;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                pos[i] = ToCell(x, y);
                tiles[i] = g[x, y] ? (TileBase)floorTile : wallTile;
                i++;
            }
        tilemap.ClearAllTiles();
        tilemap.SetTiles(pos, tiles); // одним вызовом - быстро даже для 101x101 = 10201 тайла

        if (fitCamera) FitCamera(W, H);
    }

    void FitCamera(int W, int H)
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return;

        float ww = W * TileSize, hh = H * TileSize;
        Vector3 center = tilemap.GetCellCenterWorld(new Vector3Int(W / 2, H / 2, 0));
        cam.transform.position = new Vector3(center.x, center.y, cam.transform.position.z);
        cam.orthographicSize = Mathf.Max(hh * 0.5f, ww * 0.5f / cam.aspect);
    }

    // ---------- waypoint'ы (случайные) ----------
    void SpawnWaypoints()
    {
        Waypoints = new List<Transform>();
        WaypointCells = new List<Vector2Int>();
        var pts = new List<Vector2Int>(Builder.Points);

        // Builder уже выбирает их случайно (зависит от seed); дополнительно перемешиваем нумерацию
        if (shuffleWaypointOrder)
            for (int i = pts.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var t = pts[i]; pts[i] = pts[j]; pts[j] = t;
            }

        for (int i = 0; i < pts.Count; i++)
        {
            var go = new GameObject("Waypoint_" + (i + 1));
            go.transform.SetParent(root, false);
            go.transform.position = CellToWorld(pts[i]);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = waypointSpriteRuntime;
            sr.sortingOrder = 10;
            Waypoints.Add(go.transform);
            WaypointCells.Add(pts[i]);
        }
    }

    public Transform GetRandomWaypoint(Transform exclude = null)
    {
        if (Waypoints == null || Waypoints.Count == 0) return null;
        if (Waypoints.Count == 1) return Waypoints[0];
        Transform w;
        do { w = Waypoints[Random.Range(0, Waypoints.Count)]; } while (w == exclude);
        return w;
    }

    void PlacePlayer()
    {
        if (player == null || Waypoints == null || Waypoints.Count == 0) return;
        player.position = GetRandomWaypoint().position;
    }

    // ---------- утилиты ----------
    /// <summary>Мировая позиция -> клетка сетки лабиринта (обратная к CellToWorld).</summary>
    public Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3Int c = tilemap.WorldToCell(world);
        return new Vector2Int(c.x, Builder.Height - 1 - c.y);
    }

    /// <summary>Кратчайший путь по проходам лабиринта (BFS). Возвращает центры тайлов в мировых координатах.</summary>
    public List<Vector3> FindPathWorld(Vector2Int from, Vector2Int to)
    {
        var g = Builder.Grid;
        int W = Builder.Width, H = Builder.Height;
        var prev = new Vector2Int[W, H];
        var seen = new bool[W, H];
        var q = new Queue<Vector2Int>();
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        q.Enqueue(from);
        seen[from.x, from.y] = true;
        while (q.Count > 0 && !seen[to.x, to.y])
        {
            var c = q.Dequeue();
            foreach (var d in dirs)
            {
                int nx = c.x + d.x, ny = c.y + d.y;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                if (!g[nx, ny] || seen[nx, ny]) continue;
                seen[nx, ny] = true;
                prev[nx, ny] = c;
                q.Enqueue(new Vector2Int(nx, ny));
            }
        }

        var path = new List<Vector3>();
        if (!seen[to.x, to.y]) return path; // пути нет
        for (var cur = to; cur != from; cur = prev[cur.x, cur.y])
            path.Add(CellToWorld(cur));
        path.Reverse();
        return path;
    }

    public Vector3 CellToWorld(Vector2Int c)
    {
        return tilemap.GetCellCenterWorld(ToCell(c.x, c.y));
    }
}