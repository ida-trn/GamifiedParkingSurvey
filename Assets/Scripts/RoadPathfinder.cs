using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Bakes an internal walkability grid by sampling the world against the Road layer,
/// then runs A* over that grid to produce road-following paths between any two points.
/// This lets a realistic, non-grid map (real road prefabs) still support
/// "click anywhere -> car drives along roads to get there".
///
/// The grid is only used internally for routing — the car's actual movement
/// (in CarPathMover) is smooth and free-form, not snapped to cells.
/// </summary>
public class RoadPathfinder : MonoBehaviour
{
    public static RoadPathfinder Instance { get; private set; }

    [Header("Road detection")]
    [Tooltip("Layer your road colliders live on (same one used by ClickToMove before).")]
    public LayerMask roadLayer;

    [Header("Bake area")]
    [Tooltip("Recommended: assign your DriveBoundary's Collider2D here so the bake only covers the playable area.")]
    public Collider2D boundsCollider;
    [Tooltip("Used only if boundsCollider is not assigned.")]
    public Vector2 worldMin = new Vector2(-50, -50);
    public Vector2 worldMax = new Vector2(50, 50);

    [Header("Grid resolution")]
    [Tooltip("Size of one pathfinding cell, in world units. Smaller = more accurate but slower bake. " +
             "Pick this relative to your road width (e.g. ~1/3 to 1/2 of road width).")]
    public float cellSize = 0.5f;
    [Tooltip("Radius used to test whether a cell center counts as 'on a road'.")]
    public float roadCheckRadius = 0.25f;
    public bool allowDiagonalMovement = true;
    public bool bakeOnStart = true;

    private bool[,] walkable;
    private int cols, rows;
    private Vector2 origin;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (bakeOnStart) Bake();
    }

    [ContextMenu("Bake Walkable Grid")]
    public void Bake()
    {
        Vector2 min = worldMin, max = worldMax;
        if (boundsCollider != null)
        {
            min = boundsCollider.bounds.min;
            max = boundsCollider.bounds.max;
        }

        origin = min;
        cols = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / cellSize));
        rows = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / cellSize));

        long totalCells = (long)cols * rows;
        if (totalCells > 250000)
        {
            Debug.LogWarning($"[RoadPathfinder] Grid would be {cols}x{rows} = {totalCells} cells. " +
                              "That's likely too large/slow. Increase cellSize or shrink boundsCollider.");
        }

        walkable = new bool[cols, rows];

        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Vector2 worldPos = CellToWorld(x, y);
                walkable[x, y] = Physics2D.OverlapCircle(worldPos, roadCheckRadius, roadLayer) != null;
            }
        }

        Debug.Log($"[RoadPathfinder] Baked {cols}x{rows} grid over Road layer.");
    }

    Vector2 CellToWorld(int x, int y) => origin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);

    Vector2Int WorldToCell(Vector2 worldPos) => new Vector2Int(
        Mathf.FloorToInt((worldPos.x - origin.x) / cellSize),
        Mathf.FloorToInt((worldPos.y - origin.y) / cellSize));

    bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < cols && c.y < rows;

    bool IsWalkable(Vector2Int c) => InBounds(c) && walkable[c.x, c.y];

    /// <summary>
    /// If a clicked point lands slightly off the road, snap it to the nearest walkable cell.
    /// </summary>
    public Vector2 SnapToNearestRoad(Vector2 worldPos, int maxSearchRadius = 8)
    {
        if (walkable == null) return worldPos;

        Vector2Int center = WorldToCell(worldPos);
        if (IsWalkable(center)) return worldPos;

        for (int r = 1; r <= maxSearchRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var c = new Vector2Int(center.x + dx, center.y + dy);
                    if (IsWalkable(c)) return CellToWorld(c.x, c.y);
                }
            }
        }
        return worldPos; // give up — caller should handle "no road found nearby"
    }

    /// <summary>
    /// A* search over the baked grid. Returns world-space waypoints (start excluded), or null if unreachable.
    /// </summary>
    public List<Vector2> FindPath(Vector2 startWorld, Vector2 endWorld)
    {
        if (walkable == null)
        {
            Debug.LogWarning("[RoadPathfinder] Grid not baked yet.");
            return null;
        }

        Vector2Int start = WorldToCell(startWorld);
        Vector2Int end = WorldToCell(endWorld);

        if (!IsWalkable(start)) start = WorldToCell(SnapToNearestRoad(startWorld));
        if (!IsWalkable(end)) end = WorldToCell(SnapToNearestRoad(endWorld));

        if (!IsWalkable(start) || !IsWalkable(end)) return null;
        if (start == end) return new List<Vector2> { CellToWorld(end.x, end.y) };

        var open = new List<Node>();
        var allNodes = new Dictionary<Vector2Int, Node>();
        var closed = new HashSet<Vector2Int>();

        Node startNode = new Node(start, 0, Heuristic(start, end), null);
        open.Add(startNode);
        allNodes[start] = startNode;

        Vector2Int[] dirs4 = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        Vector2Int[] dirsDiag = { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) };

        int safety = cols * rows + 10;

        while (open.Count > 0 && safety-- > 0)
        {
            open.Sort((a, b) => a.F.CompareTo(b.F));
            Node current = open[0];
            open.RemoveAt(0);

            if (current.Pos == end)
                return ReconstructPath(current);

            closed.Add(current.Pos);

            foreach (var d in dirs4)
                TryNeighbor(current, current.Pos + d, 1f, end, open, closed, allNodes);

            if (allowDiagonalMovement)
                foreach (var d in dirsDiag)
                    TryNeighbor(current, current.Pos + d, 1.4142f, end, open, closed, allNodes);
        }

        return null; // no path found
    }

    void TryNeighbor(Node current, Vector2Int neighborPos, float stepCost, Vector2Int end,
                      List<Node> open, HashSet<Vector2Int> closed, Dictionary<Vector2Int, Node> allNodes)
    {
        if (!IsWalkable(neighborPos) || closed.Contains(neighborPos)) return;

        float g = current.G + stepCost;

        if (allNodes.TryGetValue(neighborPos, out Node existing))
        {
            if (g < existing.G)
            {
                existing.G = g;
                existing.F = g + existing.H;
                existing.Parent = current;
            }
        }
        else
        {
            Node n = new Node(neighborPos, g, Heuristic(neighborPos, end), current);
            allNodes[neighborPos] = n;
            open.Add(n);
        }
    }

    float Heuristic(Vector2Int a, Vector2Int b) => Vector2.Distance(a, b);

    List<Vector2> ReconstructPath(Node end)
    {
        var raw = new List<Vector2>();
        Node n = end;
        while (n != null)
        {
            raw.Add(CellToWorld(n.Pos.x, n.Pos.y));
            n = n.Parent;
        }
        raw.Reverse();
        return SimplifyPath(raw);
    }

    // Collapses collinear runs of waypoints so the car glides instead of
    // stuttering at every single grid cell.
    List<Vector2> SimplifyPath(List<Vector2> path)
    {
        if (path.Count <= 2) return path;
        var simplified = new List<Vector2> { path[0] };

        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector2 prevDir = (path[i] - path[i - 1]).normalized;
            Vector2 nextDir = (path[i + 1] - path[i]).normalized;
            if (Vector2.Distance(prevDir, nextDir) > 0.01f)
                simplified.Add(path[i]);
        }
        simplified.Add(path[path.Count - 1]);
        return simplified;
    }

    class Node
    {
        public Vector2Int Pos;
        public float G, H, F;
        public Node Parent;
        public Node(Vector2Int pos, float g, float h, Node parent)
        {
            Pos = pos; G = g; H = h; F = g + h; Parent = parent;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (walkable == null) return;
        Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
                if (walkable[x, y])
                    Gizmos.DrawCube(CellToWorld(x, y), Vector3.one * cellSize * 0.9f);
    }
#endif
}
