using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure maze generation logic (port from the console C++ version).
/// Independent from the scene.
/// Grid: W = cellsX*2+1, H = cellsY*2+1. Cells are on odd coordinates, walls/passages between them on even ones.
/// Grid[x, y] == true  -> passage, false -> wall.
/// </summary>
public class MazeBuilder
{
    // ---------- parameters ----------
    public int cellsX = 25, cellsY = 15;
    public int variants = 5;           // number of layers
    public int pointsCount = 5;        // anchor points (2..9)
    public int minPointDist = 25;      // manhattan distance in grid cells
    public float detourMin = 1.3f, detourMax = 1.8f;
    public int seed = 1;

    /// <summary>If set (2+ points), these points are used instead of picking random ones.</summary>
    public List<Vector2Int> presetPoints;

    // ---------- result ----------
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool[,] Grid { get; private set; }
    public List<Vector2Int> Points { get; private set; }
    public string Warning { get; private set; }

    /// <summary>Called on each stage (for step-by-step display). Receives a copy of the grid.</summary>
    public Action<string, bool[,]> OnStage;

    public struct Report
    {
        public int minPointDist, openCells, totalCells, reached, deadEnds;
        public bool connected, allPointsReachable;
    }

    // ---------- internal ----------
    static readonly Vector2Int[] DIRS =
    {
        new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0)
    };

    System.Random rng;
    int W, H;
    float[,] costBuf;
    Vector2Int[,] prevBuf;

    int Rnd(int n) { return rng.Next(n); }
    float Rndf() { return (float)rng.NextDouble(); }
    bool[,] Empty() { return new bool[W, H]; }
    bool Inner(int x, int y) { return x >= 1 && y >= 1 && x < W - 1 && y < H - 1; }

    void Stage(string title, bool[,] g)
    {
        if (OnStage != null) OnStage(title, (bool[,])g.Clone());
    }

    // =====================================================
    public bool[,] Build()
    {
        rng = new System.Random(seed);
        cellsX = Mathf.Max(3, cellsX);
        cellsY = Mathf.Max(3, cellsY);
        variants = Mathf.Max(1, variants);
        pointsCount = Mathf.Clamp(pointsCount, 2, 9);
        minPointDist = Mathf.Max(2, minPointDist);
        detourMin = Mathf.Max(1f, detourMin);
        detourMax = Mathf.Max(detourMin, detourMax);

        W = Width = cellsX * 2 + 1;
        H = Height = cellsY * 2 + 1;
        Warning = null;

        // CHANGED: use preset points if given
        Points = (presetPoints != null && presetPoints.Count >= 2)
            ? new List<Vector2Int>(presetPoints)
            : PickPoints(pointsCount);

        bool[,] map = Empty();
        Stage("Step 0: anchor points", map);

        for (int v = 0; v < variants; v++)
        {
            bool[,] layer = BuildVariant();
            Stage("Layer " + (v + 1) + "/" + variants, layer);

            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (layer[x, y]) map[x, y] = true;
            Stage("Merge after layer " + (v + 1), map);
        }

        RemoveDeadEnds(map);
        Grid = map;
        Stage("FINAL MAZE", map);
        return map;
    }

    // ---------- 1. anchor points ----------
    List<Vector2Int> PickPoints(int count)
    {
        int minDist = minPointDist;
        var res = new List<Vector2Int>();
        while (minDist >= 2)
        {
            for (int restart = 0; restart < 200; restart++)
            {
                res.Clear();
                for (int t = 0; t < 2000 && res.Count < count; t++)
                {
                    var p = new Vector2Int(Rnd(cellsX) * 2 + 1, Rnd(cellsY) * 2 + 1);
                    bool ok = true;
                    foreach (var q in res)
                        if (Mathf.Abs(p.x - q.x) + Mathf.Abs(p.y - q.y) < minDist) { ok = false; break; }
                    if (ok) res.Add(p);
                }
                if (res.Count == count)
                {
                    if (minDist < minPointDist)
                        Warning = "On a " + cellsX + "x" + cellsY + " map points can't be spaced by " +
                                  minPointDist + ", used " + minDist + ".";
                    return res;
                }
            }
            minDist--;
        }
        return res;
    }

    // ---------- 2. one layer ----------
    bool[,] BuildVariant()
    {
        var layer = Empty();
        var order = new List<int>();
        for (int i = 0; i < Points.Count; i++) order.Add(i);
        // Fisher-Yates
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = Rnd(i + 1);
            int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
        }

        for (int i = 0; i + 1 < order.Count; i++)
            foreach (var c in RoutedPath(Points[order[i]], Points[order[i + 1]]))
                layer[c.x, c.y] = true;
        return layer;
    }

    // Path longer than the shortest by detourMin..detourMax times
    List<Vector2Int> RoutedPath(Vector2Int from, Vector2Int to)
    {
        int shortest = Mathf.Max(1, (Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y)) / 2);
        float target = detourMin + Rndf() * (detourMax - detourMin);

        List<Vector2Int> best = null;
        float bestErr = float.MaxValue;

        for (int t = 0; t < 40; t++)
        {
            float noise = 1f + Rndf() * 3f;
            List<Vector2Int> path;
            if (t % 2 == 0)
            {
                path = DijkstraPath(from, to, noise);
            }
            else
            {
                var w = new Vector2Int(Rnd(cellsX) * 2 + 1, Rnd(cellsY) * 2 + 1);
                if (w == from || w == to) continue;
                path = DijkstraPath(from, w, noise);
                path.AddRange(DijkstraPath(w, to, noise));
            }

            // real length = number of unique cells - 1
            var mark = new bool[W, H];
            int uniq = 0;
            foreach (var c in path)
                if ((c.x & 1) == 1 && (c.y & 1) == 1 && !mark[c.x, c.y]) { mark[c.x, c.y] = true; uniq++; }
            int steps = uniq - 1;

            float err = Mathf.Abs((float)steps / shortest - target);
            if (err < bestErr) { bestErr = err; best = path; }
        }
        // safety: if all candidates were skipped
        return best ?? DijkstraPath(from, to, 1f);
    }

    // ---------- Dijkstra with random cost ----------
    struct Node { public float c; public int x, y; }

    // Minimal binary heap (Unity has no built-in PriorityQueue)
    class MinHeap
    {
        readonly List<Node> a = new List<Node>();
        public int Count { get { return a.Count; } }

        public void Push(Node n)
        {
            a.Add(n);
            int i = a.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (a[p].c <= a[i].c) break;
                var t = a[p]; a[p] = a[i]; a[i] = t;
                i = p;
            }
        }

        public Node Pop()
        {
            Node top = a[0];
            Node last = a[a.Count - 1];
            a.RemoveAt(a.Count - 1);
            if (a.Count > 0)
            {
                a[0] = last;
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < a.Count && a[l].c < a[m].c) m = l;
                    if (r < a.Count && a[r].c < a[m].c) m = r;
                    if (m == i) break;
                    var t = a[m]; a[m] = a[i]; a[i] = t;
                    i = m;
                }
            }
            return top;
        }
    }

    List<Vector2Int> DijkstraPath(Vector2Int from, Vector2Int to, float noise)
    {
        const float INF = 1e30f;
        // buffers are reused: on 50x50 (grid 101x101) Dijkstra is called hundreds of times
        if (costBuf == null || costBuf.GetLength(0) != W || costBuf.GetLength(1) != H)
        {
            costBuf = new float[W, H];
            prevBuf = new Vector2Int[W, H];
        }
        var cost = costBuf;
        var prev = prevBuf;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++) { cost[x, y] = INF; prev[x, y] = new Vector2Int(-1, -1); }

        var open = new MinHeap();
        cost[from.x, from.y] = 0;
        open.Push(new Node { c = 0, x = from.x, y = from.y });

        while (open.Count > 0)
        {
            Node n = open.Pop();
            if (n.x == to.x && n.y == to.y) break;
            if (n.c > cost[n.x, n.y]) continue;
            foreach (var d in DIRS)
            {
                int nx = n.x + d.x * 2, ny = n.y + d.y * 2;
                if (!Inner(nx, ny)) continue;
                float nc = n.c + 1f + Rndf() * noise;
                if (nc < cost[nx, ny])
                {
                    cost[nx, ny] = nc;
                    prev[nx, ny] = new Vector2Int(n.x, n.y);
                    open.Push(new Node { c = nc, x = nx, y = ny });
                }
            }
        }

        var path = new List<Vector2Int>();
        Vector2Int cur = to;
        while (cur != from)
        {
            Vector2Int p = prev[cur.x, cur.y];
            path.Add(cur);
            path.Add(new Vector2Int((cur.x + p.x) / 2, (cur.y + p.y) / 2)); // passage between cells
            cur = p;
        }
        path.Add(from);
        return path;
    }

    // ---------- 3. dead end removal ----------
    int CountOpen(bool[,] g, int x, int y)
    {
        int n = 0;
        foreach (var d in DIRS)
            if (g[x + d.x, y + d.y]) n++;
        return n;
    }

    List<Vector2Int> FindDeadEnds(bool[,] g)
    {
        var res = new List<Vector2Int>();
        for (int x = 1; x < W; x += 2)
            for (int y = 1; y < H; y += 2)
                if (g[x, y] && CountOpen(g, x, y) == 1) res.Add(new Vector2Int(x, y));
        return res;
    }

    void RemoveDeadEnds(bool[,] g)
    {
        Stage("Dead ends before removal: " + FindDeadEnds(g).Count, g);

        for (int pass = 1; pass <= 500; pass++)
        {
            var dead = FindDeadEnds(g);
            if (dead.Count == 0) break;
            int fixedCount = 0;

            foreach (var c in dead)
            {
                if (CountOpen(g, c.x, c.y) != 1) continue; // may have been fixed earlier in this pass

                var loops = new List<Vector2Int>();   // a) loop
                var extend = new List<Vector2Int>();  // b) extension
                foreach (var d in DIRS)
                {
                    int wx = c.x + d.x, wy = c.y + d.y;
                    int nx = c.x + d.x * 2, ny = c.y + d.y * 2;
                    if (!Inner(nx, ny)) continue;
                    if (g[wx, wy]) continue;
                    (g[nx, ny] ? loops : extend).Add(d);
                }
                var pool = loops.Count > 0 ? loops : extend;
                if (pool.Count == 0) continue;

                var dir = pool[Rnd(pool.Count)];
                g[c.x + dir.x, c.y + dir.y] = true;
                g[c.x + dir.x * 2, c.y + dir.y * 2] = true;
                fixedCount++;
            }

            Stage("Dead end removal, pass " + pass + ": fixed " + fixedCount, g);
            if (fixedCount == 0) break;
        }
    }

    // ---------- verification ----------
    public Report Verify()
    {
        var seen = new bool[W, H];
        var st = new Stack<Vector2Int>();
        st.Push(Points[0]);
        seen[Points[0].x, Points[0].y] = true;
        int reached = 0;
        while (st.Count > 0)
        {
            var c = st.Pop();
            reached++;
            foreach (var d in DIRS)
            {
                int nx = c.x + d.x, ny = c.y + d.y;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                if (Grid[nx, ny] && !seen[nx, ny]) { seen[nx, ny] = true; st.Push(new Vector2Int(nx, ny)); }
            }
        }

        int open = 0, dead = 0;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (Grid[x, y]) open++;
        dead = FindDeadEnds(Grid).Count;

        bool all = true;
        foreach (var p in Points) if (!seen[p.x, p.y]) all = false;

        int minD = int.MaxValue;
        for (int i = 0; i < Points.Count; i++)
            for (int j = i + 1; j < Points.Count; j++)
                minD = Mathf.Min(minD, Mathf.Abs(Points[i].x - Points[j].x) + Mathf.Abs(Points[i].y - Points[j].y));

        return new Report
        {
            minPointDist = minD,
            openCells = open,
            totalCells = W * H,
            reached = reached,
            deadEnds = dead,
            connected = reached == open,
            allPointsReachable = all
        };
    }
}