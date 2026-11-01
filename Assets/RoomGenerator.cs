using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;

/// <summary>
/// Генератор помещения по схеме: несколько прямоугольных комнат (пол) + проходы между ними.
/// Стены строятся автоматически вокруг пола (толщина 1 клетка), клетки дальше от пола - пустота.
/// Использует те же тайлы и HotelAutotile, что и MazeGenerator, но не зависит от него.
///
/// Координаты сетки: x вправо, y ВНИЗ (y = 0 - верхний ряд), как в MazeGenerator.
/// Grid[x, y] == true -> пол, false -> стена или пустота.
///
/// Схема по умолчанию (вся сетка 21 x 12, вместе со стенами):
///   левая комната  8x8   - пол x 1..8,  y 3..10
///   правая комната - x 10..18, y 1..9: мини-лабиринт из коридоров шириной 1 клетку,
///                    всё остальное внутри неё - стена (wallAreas); зелёная точка - клетка (18, 6)
///   общая стена x = 9, проход в ней на клетке (9, 7)
///   спавн в левом верхнем углу левой комнаты - (1, 3)
/// Чтобы применить схему к уже настроенному компоненту: меню компонента -> "Load Default Layout".
/// </summary>
public class RoomGenerator : MonoBehaviour
{
    [Header("Layout (grid cells, y goes DOWN)")]
    [Tooltip("Прямоугольники пола: x, y - левый верхний угол пола, width/height - размер в клетках.")]
    public List<RectInt> rooms = DefaultRooms();

    [Tooltip("Клетки стен, которые становятся проходами.")]
    public List<Vector2Int> doorways = DefaultDoorways();

    [Tooltip("Области, где всё, что не пол, - сплошная стена (а не пустота). Так рисуется мини-лабиринт.")]
    public List<RectInt> wallAreas = DefaultWallAreas();

    [Tooltip("Клетка, где появляется игрок.")]
    public Vector2Int spawnCell = new Vector2Int(1, 3);

    // ---------- схема по умолчанию ----------
    // Правая комната - мини-лабиринт. Его левый коридор (x = 10) стоит вплотную к общей стене (x = 9),
    // поэтому между комнатами одна стена, а не двойная.
    static List<RectInt> DefaultRooms()
    {
        return new List<RectInt>
        {
            new RectInt(1, 3, 8, 8),     // левая комната 8x8

            // правая комната - мини-лабиринт (коридоры шириной 1 клетку)
            new RectInt(10, 3, 1, 7),    // большое кольцо: левая сторона (проход (9, 7) ведёт прямо в неё)
            new RectInt(16, 3, 1, 7),    //                 правая сторона
            new RectInt(10, 3, 7, 1),    //                 верх
            new RectInt(10, 9, 7, 1),    //                 низ
            new RectInt(10, 1, 5, 1),    // малое кольцо сверху: верх
            new RectInt(10, 1, 1, 3),    //                      левая сторона
            new RectInt(14, 1, 1, 3),    //                      правая сторона
            new RectInt(16, 6, 3, 1),    // ответвление вправо (к зелёной точке, клетка 18, 6)
        };
    }

    static List<Vector2Int> DefaultDoorways()
    {
        return new List<Vector2Int> { new Vector2Int(9, 7) };   // проход в общей стене
    }

    static List<RectInt> DefaultWallAreas()
    {
        return new List<RectInt> { new RectInt(9, 0, 11, 12) }; // вся правая комната вместе с её стенами
    }

    // Версия схемы. Если в сцене сохранена более старая - при запуске схема обновится сама.
    const int LAYOUT_VERSION = 3;
    [SerializeField, HideInInspector] int layoutVersion = 0;

    /// <summary>Возвращает в компонент схему по умолчанию (комнаты, проходы, стены, спавн). Объекты не трогает.</summary>
    [ContextMenu("Load Default Layout")]
    public void LoadDefaultLayout()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Load Default Layout");
#endif
        rooms = DefaultRooms();
        doorways = DefaultDoorways();
        wallAreas = DefaultWallAreas();
        spawnCell = new Vector2Int(1, 3);
        layoutVersion = LAYOUT_VERSION;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        // в Play Mode сразу перестраиваем комнату, чтобы изменения было видно
        if (Application.isPlaying && Grid != null) Generate();
    }

    void UpgradeLayoutIfOld()
    {
        if (layoutVersion >= LAYOUT_VERSION) return;

        // в версии 3 лабиринт сдвинут на 1 клетку влево - сдвигаем и объекты, стоящие в нём
        if (layoutVersion == 2 && objects != null)
            foreach (var o in objects)
                if (o != null && o.cell.x >= 10) o.cell = new Vector2Int(o.cell.x - 1, o.cell.y);

        Debug.Log("[RoomGenerator] Схема в сцене устарела - загружена новая (мини-лабиринт в правой комнате).");
        LoadDefaultLayout();
    }

    void Awake()
    {
        UpgradeLayoutIfOld();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // обновить схему и в редакторе, не дожидаясь Play
        if (!Application.isPlaying && layoutVersion < LAYOUT_VERSION)
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) UpgradeLayoutIfOld(); };
    }
#endif

    [System.Serializable]
    public class RoomObject
    {
        [Tooltip("Префаб (будет создан) или объект со сцены (будет перемещён).")]
        public GameObject obj;
        public Vector2Int cell;
        [Tooltip("true - игрок не может наступить на клетки объекта.")]
        public bool blocking = true;
        [Tooltip("Сколько клеток занимает объект: от cell вправо (x) и вниз (y).")]
        public Vector2Int size = Vector2Int.one;

        [Tooltip("true - игрок может взаимодействовать с объектом, стоя в соседней клетке (в т.ч. по диагонали).")]
        public bool interactable = false;
        [Tooltip("Игрок подошёл к объекту (например, показать подсказку \"E\").")]
        public UnityEvent onPlayerNear;
        [Tooltip("Игрок отошёл от объекта.")]
        public UnityEvent onPlayerLeave;
        [Tooltip("Игрок нажал клавишу взаимодействия рядом с объектом.")]
        public UnityEvent onInteract;
    }

    bool[,] blocked;   // клетки, занятые блокирующими объектами

    [Header("Objects")]
    [Tooltip("Объекты, которые ставятся в центр указанной клетки.")]
    public List<RoomObject> objects = new List<RoomObject>
    {
        new RoomObject { cell = new Vector2Int(5, 3) },   // прямоугольник на схеме
    };

    /// <summary>Созданные или перемещённые объекты, в том же порядке, что и в списке objects.</summary>
    public List<GameObject> SpawnedObjects { get; private set; } = new List<GameObject>();

    [Header("Tiles")]
    public int tilePixels = 64;
    public int pixelsPerUnit = 64;
    public Sprite wallSprite;           // если пусто - генерируется
    public Sprite floorSprite;
    public Color wallColor = new Color(0.15f, 0.16f, 0.2f);
    public Color floorColor = new Color(0.85f, 0.82f, 0.72f);
    public bool addColliders = true;
    [Tooltip("true - пустота вне комнат тоже заливается стеной; false - там ничего не рисуется.")]
    public bool fillOutside = false;

    [Header("Wall autotile (see HotelAutotile.cs)")]
    public bool useWallAutotile = true;
    public string wallResourcesFolder = "HotelWall";
    [Tooltip("false - внешняя сторона стен тоже обводится контуром (как по краю комнаты).")]
    public bool outsideIsWall = false;
    public Sprite doorSprite;                        // декоративные двери на стенах, необязательно
    public bool noAdjacentDoors = true;
    public bool doorNeedsWallBehind = true;
    [Range(0f, 1f)] public float doorChance = 0.15f;

    [Header("Camera and player")]
    public bool fitCamera = true;
    public Transform player;            // ставится на spawnCell

    [Header("Start")]
    public bool generateOnStart = true;

    // ---------- результат ----------
    public bool[,] Grid { get; private set; }      // пол
    public bool[,] WallMask { get; private set; }  // стены (без пустоты)
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float TileSize { get { return (float)tilePixels / pixelsPerUnit; } }

    /// <summary>Вызывается после каждой Generate().</summary>
    public event System.Action OnRoomGenerated;

    Transform root;
    Tilemap tilemap, overlayMap;
    Tile wallTile, floorTile, doorTile;
    HotelAutotile autotile;

    void Start()
    {
        if (generateOnStart) Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        BuildLayout();
        PrepareScene();
        DrawGrid();
        PlacePlayer();
        PlaceObjects();

        if (OnRoomGenerated != null) OnRoomGenerated();
    }

    // ---------- логика планировки ----------
    void BuildLayout()
    {
        // размер сетки = самый дальний край пола + 1 клетка стены
        int W = 1, H = 1;
        foreach (var r in rooms)
        {
            W = Mathf.Max(W, r.xMax + 1);
            H = Mathf.Max(H, r.yMax + 1);
        }
        if (wallAreas != null)
            foreach (var r in wallAreas)
            {
                W = Mathf.Max(W, r.xMax);
                H = Mathf.Max(H, r.yMax);
            }
        Width = W;
        Height = H;

        var floor = new bool[W, H];
        foreach (var r in rooms)
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                    if (x >= 1 && y >= 1 && x < W - 1 && y < H - 1) floor[x, y] = true;

        foreach (var d in doorways)
            if (d.x >= 1 && d.y >= 1 && d.x < W - 1 && d.y < H - 1) floor[d.x, d.y] = true;

        // стена = не пол, но рядом (в т.ч. по диагонали) есть пол
        var walls = new bool[W, H];
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                if (floor[x, y]) continue;
                if (fillOutside || InWallArea(x, y)) { walls[x, y] = true; continue; }
                for (int dx = -1; dx <= 1 && !walls[x, y]; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < W && ny < H && floor[nx, ny]) { walls[x, y] = true; break; }
                    }
            }

        Grid = floor;
        WallMask = walls;

        if (!IsFloor(spawnCell))
            Debug.LogWarning("[RoomGenerator] spawnCell " + spawnCell + " не на полу, игрок может появиться в стене.");
    }

    bool InWallArea(int x, int y)
    {
        if (wallAreas == null) return false;
        foreach (var r in wallAreas)
            if (x >= r.xMin && y >= r.yMin && x < r.xMax && y < r.yMax) return true;
        return false;
    }

    // ---------- сцена ----------
    void PrepareScene()
    {
        if (root != null) Destroy(root.gameObject);

        var rootGo = new GameObject("Room");
        root = rootGo.transform;
        root.SetParent(transform, false);

        var grid = rootGo.AddComponent<Grid>();
        grid.cellSize = new Vector3(TileSize, TileSize, 1f);

        var tmGo = new GameObject("Tiles");
        tmGo.transform.SetParent(root, false);
        tilemap = tmGo.AddComponent<Tilemap>();
        tmGo.AddComponent<TilemapRenderer>().sortingOrder = 0;
        if (addColliders) tmGo.AddComponent<TilemapCollider2D>();

        var ovGo = new GameObject("Overlay");
        ovGo.transform.SetParent(root, false);
        overlayMap = ovGo.AddComponent<Tilemap>();
        ovGo.AddComponent<TilemapRenderer>().sortingOrder = 1;   // двери поверх стен

        wallTile = MakeTile(wallSprite != null ? wallSprite : MakeSprite(wallColor, true), true);
        floorTile = MakeTile(floorSprite != null ? floorSprite : MakeSprite(floorColor, false), false);
        doorTile = doorSprite != null ? MakeTile(doorSprite, false) : null;

        autotile = useWallAutotile
            ? new HotelAutotile(wallResourcesFolder, outsideIsWall, wallTile)
            { noAdjacentDoors = noAdjacentDoors, doorNeedsWallBehind = doorNeedsWallBehind }
            : null;
    }

    TileBase TileAt(int x, int y)
    {
        if (Grid[x, y]) return floorTile;
        if (!WallMask[x, y]) return null;                       // пустота
        return autotile != null ? autotile.GetTile(autoGrid, x, y) : wallTile;
    }

    // Сетка для автотайла: пустота считается "открытой", как край карты.
    // Тогда стены, граничащие с пустотой, рисуют внешний контур, и он замыкается в углах.
    bool[,] autoGrid;

    void BuildAutoGrid()
    {
        autoGrid = (bool[,])Grid.Clone();
        if (fillOutside) return;
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                if (!Grid[x, y] && !WallMask[x, y]) autoGrid[x, y] = true;
    }

    TileBase OverlayAt(int x, int y)
    {
        if (doorTile == null || autotile == null || !WallMask[x, y]) return null;
        return autotile.HasDoor(Grid, x, y, doorChance) ? doorTile : null;
    }

    void DrawGrid()
    {
        BuildAutoGrid();

        var pos = new List<Vector3Int>();
        var tiles = new List<TileBase>();
        var ovPos = new List<Vector3Int>();
        var ovTiles = new List<TileBase>();

        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                var t = TileAt(x, y);
                if (t != null) { pos.Add(ToCell(x, y)); tiles.Add(t); }

                var ov = OverlayAt(x, y);
                if (ov != null) { ovPos.Add(ToCell(x, y)); ovTiles.Add(ov); }
            }

        tilemap.ClearAllTiles();
        tilemap.SetTiles(pos.ToArray(), tiles.ToArray());
        overlayMap.ClearAllTiles();
        overlayMap.SetTiles(ovPos.ToArray(), ovTiles.ToArray());

        if (fitCamera) FitCamera();
    }

    void FitCamera()
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return;

        float ww = Width * TileSize, hh = Height * TileSize;
        Vector3 a = tilemap.GetCellCenterWorld(ToCell(0, 0));
        Vector3 b = tilemap.GetCellCenterWorld(ToCell(Width - 1, Height - 1));
        Vector3 center = (a + b) * 0.5f;
        cam.transform.position = new Vector3(center.x, center.y, cam.transform.position.z);
        cam.orthographicSize = Mathf.Max(hh * 0.5f, ww * 0.5f / cam.aspect);
    }

    void PlacePlayer()
    {
        if (player == null) return;
        player.position = CellToWorld(spawnCell);
    }

    // Префаб создаётся внутри комнаты (удаляется при перегенерации),
    // объект со сцены просто переносится в центр клетки.
    void PlaceObjects()
    {
        SpawnedObjects = new List<GameObject>();
        blocked = new bool[Width, Height];
        foreach (var o in objects)
        {
            if (o == null || o.obj == null) { SpawnedObjects.Add(null); continue; }

            if (o.blocking)
            {
                for (int dx = 0; dx < Mathf.Max(1, o.size.x); dx++)
                    for (int dy = 0; dy < Mathf.Max(1, o.size.y); dy++)
                    {
                        int x = o.cell.x + dx, y = o.cell.y + dy;
                        if (x >= 0 && y >= 0 && x < Width && y < Height) blocked[x, y] = true;
                    }

                var sc = spawnCell;
                if (sc.x >= o.cell.x && sc.y >= o.cell.y &&
                    sc.x < o.cell.x + Mathf.Max(1, o.size.x) && sc.y < o.cell.y + Mathf.Max(1, o.size.y))
                    Debug.LogWarning("[RoomGenerator] " + o.obj.name + " стоит на клетке спавна игрока.");
            }

            if (!IsFloor(o.cell))
                Debug.LogWarning("[RoomGenerator] Клетка " + o.cell + " для " + o.obj.name + " не на полу.");

            Vector3 p = CellToWorld(o.cell);
            GameObject go;
            if (o.obj.scene.IsValid())   // объект уже на сцене
            {
                go = o.obj;
                go.transform.position = new Vector3(p.x, p.y, go.transform.position.z);
            }
            else                          // префаб из Assets
            {
                go = Instantiate(o.obj, new Vector3(p.x, p.y, o.obj.transform.position.z), o.obj.transform.rotation, root);
            }
            SpawnedObjects.Add(go);
        }
    }

    // ---------- создание тайлов и спрайтов ----------
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

    // ---------- утилиты (как в MazeGenerator) ----------
    Vector3Int ToCell(int x, int y)
    {
        return new Vector3Int(x, Height - 1 - y, 0);
    }

    public Vector3 CellToWorld(Vector2Int c)
    {
        return tilemap.GetCellCenterWorld(ToCell(c.x, c.y));
    }

    public Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3Int c = tilemap.WorldToCell(world);
        return new Vector2Int(c.x, Height - 1 - c.y);
    }

    public bool IsFloor(Vector2Int c)
    {
        return Grid != null && c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height && Grid[c.x, c.y];
    }

    /// <summary>
    /// Стоит ли клетка c рядом с объектом: в любой из 8 соседних клеток вокруг его площади
    /// (для неблокирующего объекта - ещё и на нём самом).
    /// </summary>
    public bool IsNextTo(RoomObject o, Vector2Int c)
    {
        if (o == null) return false;
        int w = Mathf.Max(1, o.size.x), h = Mathf.Max(1, o.size.y);
        bool inArea = c.x >= o.cell.x - 1 && c.y >= o.cell.y - 1 && c.x <= o.cell.x + w && c.y <= o.cell.y + h;
        if (!inArea) return false;
        bool onObject = c.x >= o.cell.x && c.y >= o.cell.y && c.x < o.cell.x + w && c.y < o.cell.y + h;
        return !onObject || !o.blocking;
    }

    /// <summary>
    /// Меняет blocking у объекта из списка objects во время игры (без перегенерации комнаты).
    /// index - номер в списке, начиная с 0.
    /// </summary>
    public void SetBlocking(int index, bool value)
    {
        if (index < 0 || index >= objects.Count || objects[index] == null)
        {
            Debug.LogWarning("[RoomGenerator] SetBlocking: объекта с номером " + index + " нет в списке objects.");
            return;
        }
        objects[index].blocking = value;
        RebuildBlocked();
    }

    // пересчитывает занятые клетки по текущим настройкам объектов
    void RebuildBlocked()
    {
        if (Grid == null) return;
        blocked = new bool[Width, Height];
        for (int i = 0; i < objects.Count; i++)
        {
            var o = objects[i];
            if (o == null || o.obj == null || !o.blocking) continue;
            for (int dx = 0; dx < Mathf.Max(1, o.size.x); dx++)
                for (int dy = 0; dy < Mathf.Max(1, o.size.y); dy++)
                {
                    int x = o.cell.x + dx, y = o.cell.y + dy;
                    if (x >= 0 && y >= 0 && x < Width && y < Height) blocked[x, y] = true;
                }
        }
    }

    /// <summary>Пол, на котором нет блокирующего объекта - сюда игрок может наступить.</summary>
    public bool IsWalkable(Vector2Int c)
    {
        return IsFloor(c) && (blocked == null || !blocked[c.x, c.y]);
    }
}