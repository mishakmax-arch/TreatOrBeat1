using UnityEngine;

/// <summary>
/// Вешается на игрока (рядом с PlayerWaypointMover). Управление: WASD / стрелки.
/// Игрок движется по проходам лабиринта и упирается в стены.
/// </summary>
public class PlayerMazeController : MonoBehaviour
{
    public MazeGenerator maze;
    public float speed = 6f;        // юнитов в секунду (1 юнит = 1 тайл)

    bool moving;
    Vector3 target;

    void Update()
    {
        // лабиринт ещё не сгенерирован
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

        // читаем ввод (горизонталь в приоритете)
        int h = (int)Input.GetAxisRaw("Horizontal");
        int v = (int)Input.GetAxisRaw("Vertical");
        if (h == 0 && v == 0) return;

        // в сетке лабиринта y=0 - верх экрана, поэтому "вверх" = y-1
        Vector2Int dir = h != 0 ? new Vector2Int(h, 0) : new Vector2Int(0, -v);

        // текущая клетка берётся из позиции, поэтому телепорт на waypoint учитывается сам
        Vector2Int cell = maze.WorldToGrid(transform.position);
        Vector2Int next = cell + dir;

        if (!IsFree(next)) return; // стена или край карты - стоим на месте

        Vector3 w = maze.CellToWorld(next);
        target = new Vector3(w.x, w.y, transform.position.z);
        moving = true;
    }

    bool IsFree(Vector2Int c)
    {
        var g = maze.Builder.Grid;
        if (c.x < 0 || c.y < 0 || c.x >= maze.Builder.Width || c.y >= maze.Builder.Height) return false;
        return g[c.x, c.y]; // true = проход, false = стена
    }
}