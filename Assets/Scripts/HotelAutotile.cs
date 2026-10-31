using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Автотайлинг стен лабиринта в стиле "отель" (47 вариантов, "blob").
/// Алгоритм тот же, что в WallAutotile, но тайлы нарисованы под текстуру стены-полоски.
///
/// Для каждой клетки-стены смотрим 8 соседей и считаем ключ:
///   биты сторон   N=1 E=2 S=4 W=8   - сосед на этой стороне тоже СТЕНА (север = вверх на экране)
///   биты вырезов  NE=16 SE=32 SW=64 NW=128 - вогнутый угол: обе смежные стороны стены,
///                                            а диагональная клетка - пол.
/// Ключу соответствует спрайт Resources/&lt;папка&gt;/hotel_&lt;ключ&gt;.png.
/// Полоска стены (с повёрнутыми линиями) рисуется только на сторонах, где сосед НЕ стена,
/// поэтому повороты и стыки получаются сами, а всё остальное место остаётся чёрным.
///
/// Двери: дверь (wall_door.png) ставится на участок стены, обращённый вниз:
/// слева и справа стена, сверху стена (есть куда "вырасти"), снизу пол.
/// </summary>
public class HotelAutotile
{
    public const int N = 1, E = 2, S = 4, W = 8, NE = 16, SE = 32, SW = 64, NW = 128;

    readonly Dictionary<int, Tile> cache = new Dictionary<int, Tile>();
    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    readonly Tile fallback;
    readonly bool outsideIsWall;

    /// Двери не стоят в соседних клетках подряд.
    public bool noAdjacentDoors = true;
    /// Дверь только там, где сверху тоже стена (иначе дверь налезет на полоску с другой стороны).
    public bool doorNeedsWallBehind = true;

    public HotelAutotile(string resourcesFolder, bool outsideIsWall, Tile fallback)
    {
        this.outsideIsWall = outsideIsWall;
        this.fallback = fallback;

        foreach (var s in Resources.LoadAll<Sprite>(resourcesFolder))
            sprites[s.name] = s;

        if (sprites.Count == 0)
            Debug.LogWarning("[HotelAutotile] В Resources/" + resourcesFolder + " нет спрайтов hotel_<ключ>. " +
                             "Используется обычная стена.");
    }

    bool IsWall(bool[,] g, int x, int y)
    {
        if (x < 0 || y < 0 || x >= g.GetLength(0) || y >= g.GetLength(1)) return outsideIsWall;
        return !g[x, y]; // в сетке true = проход, false = стена
    }

    public int ComputeKey(bool[,] g, int x, int y)
    {
        bool n = IsWall(g, x, y - 1);   // grid y-1 = вверх на экране
        bool e = IsWall(g, x + 1, y);
        bool s = IsWall(g, x, y + 1);
        bool w = IsWall(g, x - 1, y);

        int k = 0;
        if (n) k |= N;
        if (e) k |= E;
        if (s) k |= S;
        if (w) k |= W;

        // вогнутые углы учитываем, только если обе смежные стороны - стены
        if (n && e && !IsWall(g, x + 1, y - 1)) k |= NE;
        if (s && e && !IsWall(g, x + 1, y + 1)) k |= SE;
        if (s && w && !IsWall(g, x - 1, y + 1)) k |= SW;
        if (n && w && !IsWall(g, x - 1, y - 1)) k |= NW;
        return k;
    }

    public TileBase GetTile(bool[,] g, int x, int y)
    {
        int key = ComputeKey(g, x, y);

        Tile t;
        if (cache.TryGetValue(key, out t)) return t;

        Sprite sp;
        if (sprites.TryGetValue("hotel_" + key, out sp))
        {
            t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sp;
            t.colliderType = Tile.ColliderType.Grid; // коллайдер - вся клетка, как у обычной стены
        }
        else
        {
            t = fallback;
        }
        cache[key] = t;
        return t;
    }

    /// Подходит ли клетка для двери: слева и справа стена, снизу пол (и сверху стена, если включено).
    public bool CanHaveDoor(bool[,] g, int x, int y)
    {
        if (x < 0 || y < 0 || x >= g.GetLength(0) || y >= g.GetLength(1)) return false;
        if (g[x, y]) return false;
        int k = ComputeKey(g, x, y);
        if ((k & W) == 0 || (k & E) == 0 || (k & S) != 0) return false;
        if (doorNeedsWallBehind && (k & N) == 0) return false;
        return true;
    }

    bool Candidate(bool[,] g, int x, int y, float chance)
    {
        return CanHaveDoor(g, x, y) && Hash01(x, y) < chance;
    }

    /// Есть ли дверь в этой клетке. Решение зависит только от самой клетки и её соседей слева/справа,
    /// поэтому дверь не мигает при перерисовке. Из двух соседних кандидатов остаётся тот, у кого хэш меньше.
    public bool HasDoor(bool[,] g, int x, int y, float chance)
    {
        if (!Candidate(g, x, y, chance)) return false;
        if (noAdjacentDoors)
        {
            float h = Hash01(x, y);
            if (Candidate(g, x - 1, y, chance) && Hash01(x - 1, y) < h) return false;
            if (Candidate(g, x + 1, y, chance) && Hash01(x + 1, y) < h) return false;
        }
        return true;
    }

    /// Стабильное "случайное" число 0..1 для клетки.
    public static float Hash01(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
