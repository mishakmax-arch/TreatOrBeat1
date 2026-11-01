using UnityEngine;

/// <summary>
/// Grid-based player movement for RoomGenerator: the player moves one square at a time
/// and slides smoothly between square centers. Walls and blocking objects are checked with RoomGenerator.IsWalkable,
/// so no physics/colliders are needed for movement.
/// Put it on the player object.
/// </summary>
public class RoomPlayerController : MonoBehaviour
{
    public RoomGenerator room;                 // if empty - found in the scene

    [Header("Movement")]
    public float moveSpeed = 6f;              // squares per second
    public bool holdToRepeat = true;          // keep walking while the key is held
    public bool allowDiagonal = false;        // false - only up/down/left/right

    [Header("Optional")]
    public SpriteRenderer spriteRenderer;     // flipped when walking left/right
    public bool flipSpriteX = true;

    /// <summary>True while the player is sliding between two squares.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Other scripts (altars, menus, cutscenes) can block input with this.</summary>
    public bool InputLocked { get; set; }

    /// <summary>Current square in room grid coordinates.</summary>
    public Vector2Int Cell { get; private set; }

    /// <summary>Last movement direction (grid coordinates: y grows DOWN, like in the generator).</summary>
    public Vector2Int Facing { get; private set; } = new Vector2Int(0, 1);

    /// <summary>Fired when the player arrives at a new square.</summary>
    public event System.Action<Vector2Int> OnCellReached;

    Vector3 targetPos;

    void Awake()
    {
        if (room == null)
        {
#if UNITY_2023_1_OR_NEWER
            room = FindFirstObjectByType<RoomGenerator>();
#else
            room = FindObjectOfType<RoomGenerator>();
#endif
        }
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (room == null) Debug.LogWarning("[RoomPlayerController] RoomGenerator not found in the scene.");
    }

    void OnEnable()
    {
        if (room != null) room.OnRoomGenerated += SnapToGrid;
    }

    void OnDisable()
    {
        if (room != null) room.OnRoomGenerated -= SnapToGrid;
    }

    void Start()
    {
        // if the room was already generated before this script started
        if (room != null && room.Grid != null) SnapToGrid();
    }

    /// <summary>Snaps the player to the center of the square it is standing on and stops any movement.</summary>
    public void SnapToGrid()
    {
        if (room == null || room.Grid == null) return;
        Cell = room.WorldToGrid(transform.position);
        targetPos = room.CellToWorld(Cell);
        transform.position = targetPos;
        IsMoving = false;
    }

    /// <summary>Teleports the player to a square (ignored if it is a wall).</summary>
    public void TeleportTo(Vector2Int cell)
    {
        if (room == null || !room.IsWalkable(cell)) return;
        Cell = cell;
        targetPos = room.CellToWorld(cell);
        transform.position = targetPos;
        IsMoving = false;
    }

    void Update()
    {
        if (room == null || room.Grid == null) return;

        if (IsMoving)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos,
                                                     moveSpeed * room.TileSize * Time.deltaTime);
            if ((transform.position - targetPos).sqrMagnitude > 0.0001f) return;

            transform.position = targetPos;
            IsMoving = false;
            if (OnCellReached != null) OnCellReached(Cell);
        }

        if (InputLocked) return;

        Vector2Int dir = ReadInput();
        if (dir != Vector2Int.zero) TryMove(dir);
    }

    Vector2Int ReadInput()
    {
        bool up, down, left, right;
        if (holdToRepeat)
        {
            up = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            down = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        }
        else
        {
            up = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            down = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
            left = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
            right = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
        }

        // grid y grows DOWN (row 0 is the top), so "up" is y - 1
        int x = (right ? 1 : 0) - (left ? 1 : 0);
        int y = (down ? 1 : 0) - (up ? 1 : 0);

        if (!allowDiagonal && x != 0 && y != 0)
        {
            // keep walking in the current direction, turn with the newly pressed key
            if (Facing.x != 0) x = 0; else y = 0;
        }
        return new Vector2Int(x, y);
    }

    /// <summary>Tries to step one square in a direction. Returns false if blocked.</summary>
    public bool TryMove(Vector2Int dir)
    {
        if (IsMoving || dir == Vector2Int.zero) return false;

        Facing = dir;
        if (flipSpriteX && spriteRenderer != null && dir.x != 0) spriteRenderer.flipX = dir.x < 0;

        Vector2Int next = Cell + dir;
        if (!room.IsWalkable(next)) return false;

        // diagonal: don't cut through wall corners
        if (dir.x != 0 && dir.y != 0)
        {
            if (!room.IsWalkable(new Vector2Int(Cell.x + dir.x, Cell.y)) ||
                !room.IsWalkable(new Vector2Int(Cell.x, Cell.y + dir.y)))
                return false;
        }

        Cell = next;
        targetPos = room.CellToWorld(next);
        IsMoving = true;
        return true;
    }
}