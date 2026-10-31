using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 2D maze on a Tilemap, each tile is 64x64 pixels.
/// Put it on an empty GameObject (MazeBuilder.cs must NOT be attached to anything).
/// </summary>
public class MazeGenerator : MonoBehaviour
{
    [Header("Maze size (cells)")]
    public int cellsX = 50;
    public int cellsY = 50;

    [Header("Tiles")]
    public int tilePixels = 64;
    public int pixelsPerUnit = 64;
    public Sprite wallSprite;           // if empty - generated
    public Sprite floorSprite;
    public Color wallColor = new Color(0.15f, 0.16f, 0.2f);
    public Color floorColor = new Color(0.85f, 0.82f, 0.72f);
    public bool addColliders = true;

    [Header("Altar prefabs (optional). Each can have its own animation.")]
    public GameObject altarPrefab;            // NOT activated altar
    public GameObject altarActivatedPrefab;   // ACTIVATED altar

    [Header("Waypoint sprites (used when the prefab slot is empty, and for entrance/exit)")]
    public Sprite waypointSprite;           // normal altar, if no prefab
    public Sprite waypointActivatedSprite;  // activated altar, if no prefab
    public Sprite entranceSprite;           // where the player spawns
    public Sprite exitSprite;               // random one of the rest
    public Color waypointColor = new Color(0.95f, 0.3f, 0.25f);
    public Color waypointActivatedColor = new Color(0.3f, 0.85f, 0.35f);
    public Color entranceColor = new Color(0.25f, 0.55f, 0.95f);
    public Color exitColor = new Color(0.95f, 0.85f, 0.2f);

    [Header("Waypoint scale")]
    public Vector2 waypointScale = new Vector2(1.5f, 1.5f);

    [Header("Generation")]
    [Range(1, 12)] public int variants = 5;
    [Range(2, 9)] public int waypointsCount = 5;
    public int minPointDist = 25;
    public float detourMin = 1.3f;
    public float detourMax = 1.8f;

    [Header("Randomness")]
    public bool useRandomSeed = true;
    public int seed = 1;
    public bool shuffleWaypointOrder = true;

    [Header("Camera and player")]
    public bool fitCamera = true;
    public Transform player;            // spawns on the entrance

    [Header("Step-by-step display")]
    public bool animateStages = false;
    public float stageDelay = 0.4f;

    [Header("Start")]
    public bool generateOnStart = true;

    public MazeBuilder Builder { get; private set; }
    public List<Transform> Waypoints { get; private set; }
    public List<Vector2Int> WaypointCells { get; private set; }
    public List<SpriteRenderer> WaypointRenderers { get; private set; }

    /// <summary>Index of the entrance waypoint (where the player spawned), -1 if none.</summary>
    public int EntranceIndex { get; private set; }
    /// <summary>Index of the exit waypoint, -1 if none.</summary>
    public int ExitIndex { get; private set; }

    public Transform Entrance { get { return IsValid(EntranceIndex) ? Waypoints[EntranceIndex] : null; } }
    public Transform Exit { get { return IsValid(ExitIndex) ? Waypoints[ExitIndex] : null; } }

    /// <summary>Fired after a full Generate() (new altars, entrance, exit).</summary>
    public event System.Action OnMazeGenerated;

    /// <summary>Fired after RebuildMazeKeepWaypoints() (new walls, same altars and player).</summary>
    public event System.Action OnMazeRebuilt;

    public float TileSize { get { return (float)tilePixels / pixelsPerUnit; } }

    Transform root;
    Tilemap tilemap;
    Tile wallTile, floorTile;
    Sprite normalSpriteRuntime, activatedSpriteRuntime, entranceSpriteRuntime, exitSpriteRuntime;
    List<GameObject> waypointVisuals;   // current prefab instance of each waypoint (null = sprite mode)

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

    void DrawGridDiff(bool[,] old, bool[,] g)
    {
        var pos = new List<Vector3Int>();
        var tiles = new List<TileBase>();
        for (int x = 0; x < g.GetLength(0); x++)
            for (int y = 0; y < g.GetLength(1); y++)
                if (old[x, y] != g[x, y])
                {
                    pos.Add(ToCell(x, y));
                    tiles.Add(g[x, y] ? (TileBase)floorTile : wallTile);
                }
        tilemap.SetTiles(pos.ToArray(), tiles.ToArray());
    }
    /// <summary>Builds a new maze layout but keeps all waypoints, their states and the player where they are.</summary>
    public void RebuildMazeKeepWaypoints()
    {
        if (Builder == null || WaypointCells == null || tilemap == null) return;

        if (useRandomSeed) seed = Random.Range(0, int.MaxValue);

        var nb = new MazeBuilder
        {
            cellsX = cellsX,
            cellsY = cellsY,
            variants = variants,
            pointsCount = waypointsCount,
            minPointDist = minPointDist,
            detourMin = detourMin,
            detourMax = detourMax,
            seed = seed,
            presetPoints = new List<Vector2Int>(WaypointCells)   // same altar cells
        };
        nb.Build();
        var oldGrid = Builder.Grid; 
        Builder = nb;

        DrawGridDiff(oldGrid, Builder.Grid);    // only the tiles are redrawn; waypoints and player are not touched

        if (OnMazeRebuilt != null) OnMazeRebuilt();
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
        Debug.Log("Maze seed " + seed + " (" + cellsX + "x" + cellsY + "): connected=" + r.connected +
                  ", all waypoints reachable=" + r.allPointsReachable + ", dead ends=" + r.deadEnds);
        if (OnMazeGenerated != null) OnMazeGenerated();
    }

    // ---------- Grid + Tilemap ----------
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

        normalSpriteRuntime = waypointSprite != null ? waypointSprite : MakeCircleSprite(waypointColor);
        activatedSpriteRuntime = waypointActivatedSprite != null ? waypointActivatedSprite : MakeCircleSprite(waypointActivatedColor);
        entranceSpriteRuntime = entranceSprite != null ? entranceSprite : MakeCircleSprite(entranceColor);
        exitSpriteRuntime = exitSprite != null ? exitSprite : MakeCircleSprite(exitColor);
    }

    Tile MakeTile(Sprite s, bool solid)
    {
        var t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = s;
        t.colliderType = solid ? Tile.ColliderType.Grid : Tile.ColliderType.None;
        return t;
    }

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

    // ---------- drawing ----------
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
        tilemap.SetTiles(pos, tiles);

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

    // ---------- waypoints ----------
    void SpawnWaypoints()
    {
        Waypoints = new List<Transform>();
        WaypointCells = new List<Vector2Int>();
        WaypointRenderers = new List<SpriteRenderer>();
        waypointVisuals = new List<GameObject>();
        EntranceIndex = -1;
        ExitIndex = -1;
        var pts = new List<Vector2Int>(Builder.Points);

        if (shuffleWaypointOrder)
            for (int i = pts.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var t = pts[i]; pts[i] = pts[j]; pts[j] = t;
            }

        for (int i = 0; i < pts.Count; i++)
        {
            // the waypoint root stays the same forever (position + scale + its own SpriteRenderer for sprite mode)
            var go = new GameObject("Waypoint_" + (i + 1));
            go.transform.SetParent(root, false);
            go.transform.position = CellToWorld(pts[i]);
            go.transform.localScale = new Vector3(waypointScale.x, waypointScale.y, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 10;

            Waypoints.Add(go.transform);
            WaypointCells.Add(pts[i]);
            WaypointRenderers.Add(sr);
            waypointVisuals.Add(null);

            ShowAltar(i, false); // start as NOT activated
        }
    }

    /// <summary>Picks the entrance (player spawn) and a random exit among the rest.</summary>
    void PlacePlayer()
    {
        if (Waypoints == null || Waypoints.Count == 0) return;

        EntranceIndex = Random.Range(0, Waypoints.Count);

        if (Waypoints.Count > 1)
        {
            do { ExitIndex = Random.Range(0, Waypoints.Count); } while (ExitIndex == EntranceIndex);
        }

        ShowSprite(EntranceIndex, entranceSpriteRuntime);
        Waypoints[EntranceIndex].name = "Waypoint_" + (EntranceIndex + 1) + "_Entrance";

        if (ExitIndex >= 0)
        {
            ShowSprite(ExitIndex, exitSpriteRuntime);
            Waypoints[ExitIndex].name = "Waypoint_" + (ExitIndex + 1) + "_Exit";
        }

        if (player != null) player.position = Waypoints[EntranceIndex].position;
    }

    public Transform GetRandomWaypoint(Transform exclude = null)
    {
        if (Waypoints == null || Waypoints.Count == 0) return null;
        if (Waypoints.Count == 1) return Waypoints[0];
        Transform w;
        do { w = Waypoints[Random.Range(0, Waypoints.Count)]; } while (w == exclude);
        return w;
    }

    // ---------- waypoint visuals ----------
    bool IsValid(int index)
    {
        return Waypoints != null && index >= 0 && index < Waypoints.Count;
    }

    void ClearVisual(int index)
    {
        if (waypointVisuals[index] != null)
        {
            Destroy(waypointVisuals[index]);
            waypointVisuals[index] = null;
        }
    }

    /// <summary>Shows the altar in the given state: uses the matching prefab, or the sprite if the prefab slot is empty.</summary>
    void ShowAltar(int index, bool activated)
    {
        ClearVisual(index);

        GameObject prefab = activated ? altarActivatedPrefab : altarPrefab;
        if (prefab != null)
        {
            var visual = Instantiate(prefab, Waypoints[index]);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            waypointVisuals[index] = visual;
            WaypointRenderers[index].enabled = false; // the prefab draws itself
        }
        else
        {
            WaypointRenderers[index].enabled = true;
            WaypointRenderers[index].sprite = activated ? activatedSpriteRuntime : normalSpriteRuntime;
        }
    }

    /// <summary>Removes any prefab visual and shows a plain sprite.</summary>
    void ShowSprite(int index, Sprite sprite)
    {
        ClearVisual(index);
        WaypointRenderers[index].enabled = true;
        WaypointRenderers[index].sprite = sprite;
    }

    // ---------- changing waypoints from other scripts ----------
    /// <summary>Switches an altar between NOT activated and ACTIVATED (swaps the prefab). Ignores entrance and exit.</summary>
    public void SetWaypointActivated(int index, bool activated)
    {
        if (!IsValid(index)) return;
        if (index == EntranceIndex || index == ExitIndex) return;
        ShowAltar(index, activated);
    }

    public void ActivateWaypoint(int index) { SetWaypointActivated(index, true); }

    public void DeactivateWaypoint(int index) { SetWaypointActivated(index, false); }

    /// <summary>Sets any custom sprite on a waypoint (removes its prefab visual).</summary>
    public void SetWaypointSprite(int index, Sprite sprite)
    {
        if (!IsValid(index) || sprite == null) return;
        ShowSprite(index, sprite);
    }

    /// <summary>Changes the sprite of the exit (for example, to show it is now open).</summary>
    public void SetExitSprite(Sprite sprite) { SetWaypointSprite(ExitIndex, sprite); }

    // ---------- utilities ----------
    public Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3Int c = tilemap.WorldToCell(world);
        return new Vector2Int(c.x, Builder.Height - 1 - c.y);
    }

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
        if (!seen[to.x, to.y]) return path;
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