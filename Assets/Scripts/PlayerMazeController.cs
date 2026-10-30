using UnityEngine;

/// <summary>
/// Put on the player. Controls: WASD / arrows.
/// The player moves along the maze passages and stops at walls.
/// </summary>
public class PlayerMazeController : MonoBehaviour
{
    public MazeGenerator maze;
    public float speed = 6f;        // units per second (1 unit = 1 tile)

    // --- добавить в класс ---
    public float Speed { get { return speed; } }
    public Vector2Int LastDir { get; private set; }
    public float LastMoveTime { get; private set; } = -999f;

    /// Клетка, в которой игрок находится, а если идёт, то в которую идёт.
    public Vector2Int CurrentCell
    {
        get { return maze.WorldToGrid(moving ? target : transform.position); }
    }

    bool moving;
    Vector3 target;

    void OnEnable() { if (maze != null) maze.OnMazeGenerated += ResetMovement; }
    void OnDisable() { if (maze != null) maze.OnMazeGenerated -= ResetMovement; }

    void ResetMovement()
    {
        moving = false;
        LastDir = Vector2Int.zero;
        LastMoveTime = -999f;
    }

    // the old target tile may be a wall in the new layout
    void CancelMove()
    {
        moving = false;
    }

    void Update()
    {
        // maze not generated yet
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

        int h = (int)Input.GetAxisRaw("Horizontal");
        int v = (int)Input.GetAxisRaw("Vertical");
        if (h == 0 && v == 0) return;

        // in the maze grid y=0 is the top of the screen, so "up" = y-1
        Vector2Int dir = h != 0 ? new Vector2Int(h, 0) : new Vector2Int(0, -v);

        Vector2Int cell = maze.WorldToGrid(transform.position);
        Vector2Int next = cell + dir;

        if (!IsFree(next)) return; // wall or map edge - stay in place

        Vector3 w = maze.CellToWorld(next);
        target = new Vector3(w.x, w.y, transform.position.z);
        moving = true;

        LastDir = dir;
        LastMoveTime = Time.time;
    }

    bool IsFree(Vector2Int c)
    {
        var g = maze.Builder.Grid;
        if (c.x < 0 || c.y < 0 || c.x >= maze.Builder.Width || c.y >= maze.Builder.Height) return false;
        return g[c.x, c.y]; // true = passage, false = wall
    }
}