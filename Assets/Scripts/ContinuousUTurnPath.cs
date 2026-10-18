using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines one directional road path that is paired with an opposite-direction
/// path for mid-edge U-turns.
///
/// This component never changes RoadNode.connectedNodes. CarNodeFollower can use
/// the pair for two separate behaviors:
/// 1) parking-triggered U-turns when a spot on the opposite lane is selected;
/// 2) a manual U-turn button that turns the car at its exact current position.
/// </summary>
public class ContinuousUTurnPath : MonoBehaviour
{
    [System.Serializable]
    public struct PathProjection
    {
        public RoadNode fromNode;
        public RoadNode toNode;
        public Vector3 point;
        [Range(0f, 1f)] public float t;
        public float distance;

        public bool IsValid => fromNode != null && toNode != null;
    }

    [Header("Identity")]
    [Tooltip("Optional label used only for debugging, e.g. Malekloo A, Heydarkhani A, or Abgoon B.")]
    public string pathLabel = "Configured street";

    [Header("Opposite-Lane Parking U-Turn")]
    [Tooltip("Enable parking-triggered mid-edge U-turns from this directional path.")]
    public bool allowParkingUTurns = true;

    [Tooltip("Enable the manual U-turn button while the car is travelling on this directional path.")]
    public bool allowManualUTurns = true;

    [Tooltip("The paired opposite-direction path on the same street.")]
    public ContinuousUTurnPath oppositePath;

    [Tooltip("Maximum lane-to-lane crossing distance in Unity units. Set 0 to disable the limit.")]
    [Min(0f)]
    public float maxUTurnCrossingDistance = 20f;

    [Header("Path Nodes")]
    [Tooltip("If enabled, RoadNode components below this GameObject are collected automatically.")]
    public bool autoCollectChildNodes = true;

    [Tooltip("RoadNodes belonging to this ONE driving direction. Their existing connectedNodes define the direction/order.")]
    public List<RoadNode> nodes = new List<RoadNode>();

    private static readonly List<ContinuousUTurnPath> activePaths =
        new List<ContinuousUTurnPath>();

    private void OnEnable()
    {
        if (!activePaths.Contains(this))
            activePaths.Add(this);

        if (autoCollectChildNodes)
            RefreshNodesFromChildren();
    }

    private void OnDisable()
    {
        activePaths.Remove(this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && autoCollectChildNodes)
            RefreshNodesFromChildren();
    }
#endif

    [ContextMenu("Refresh Nodes From Children")]
    public void RefreshNodesFromChildren()
    {
        if (!autoCollectChildNodes)
            return;

        RoadNode[] childNodes = GetComponentsInChildren<RoadNode>(true);

        nodes.Clear();

        foreach (RoadNode node in childNodes)
        {
            if (node != null && !nodes.Contains(node))
                nodes.Add(node);
        }
    }

    public bool ContainsNode(RoadNode node)
    {
        return node != null && nodes != null && nodes.Contains(node);
    }

    public bool ContainsDirectedSegment(RoadNode fromNode, RoadNode toNode)
    {
        if (!ContainsNode(fromNode) || !ContainsNode(toNode))
            return false;

        return fromNode.connectedNodes != null &&
               fromNode.connectedNodes.Contains(toNode);
    }

    public bool TryProjectOntoDirectedSegment(
        RoadNode fromNode,
        RoadNode toNode,
        Vector2 worldPosition,
        out PathProjection projection)
    {
        projection = default;

        if (!ContainsDirectedSegment(fromNode, toNode))
            return false;

        Vector2 a = fromNode.transform.position;
        Vector2 b = toNode.transform.position;
        Vector2 ab = b - a;

        float lengthSquared = ab.sqrMagnitude;

        if (lengthSquared <= 0.0001f)
            return false;

        float t = Vector2.Dot(worldPosition - a, ab) / lengthSquared;
        t = Mathf.Clamp01(t);

        Vector2 point = a + ab * t;

        projection = new PathProjection
        {
            fromNode = fromNode,
            toNode = toNode,
            point = new Vector3(point.x, point.y, fromNode.transform.position.z),
            t = t,
            distance = Vector2.Distance(worldPosition, point)
        };

        return true;
    }

    public bool TryGetNearestDirectedSegment(
        Vector2 worldPosition,
        out PathProjection bestProjection)
    {
        bestProjection = default;
        float bestDistance = Mathf.Infinity;
        bool found = false;

        if (nodes == null)
            return false;

        foreach (RoadNode fromNode in nodes)
        {
            if (fromNode == null || fromNode.connectedNodes == null)
                continue;

            foreach (RoadNode toNode in fromNode.connectedNodes)
            {
                if (!ContainsNode(toNode))
                    continue;

                if (!TryProjectOntoDirectedSegment(
                        fromNode,
                        toNode,
                        worldPosition,
                        out PathProjection candidate))
                {
                    continue;
                }

                if (candidate.distance < bestDistance)
                {
                    bestDistance = candidate.distance;
                    bestProjection = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Finds a directed node path while staying entirely inside this path.
    /// Existing RoadNode.connectedNodes remain authoritative.
    /// </summary>
    public bool TryFindDirectedNodePath(
        RoadNode start,
        RoadNode goal,
        out List<RoadNode> path)
    {
        path = null;

        if (!ContainsNode(start) || !ContainsNode(goal))
            return false;

        if (start == goal)
        {
            path = new List<RoadNode> { start };
            return true;
        }

        Queue<RoadNode> open = new Queue<RoadNode>();
        HashSet<RoadNode> visited = new HashSet<RoadNode>();
        Dictionary<RoadNode, RoadNode> cameFrom =
            new Dictionary<RoadNode, RoadNode>();

        open.Enqueue(start);
        visited.Add(start);

        while (open.Count > 0)
        {
            RoadNode current = open.Dequeue();

            if (current == goal)
                break;

            if (current.connectedNodes == null)
                continue;

            foreach (RoadNode next in current.connectedNodes)
            {
                if (!ContainsNode(next) || visited.Contains(next))
                    continue;

                visited.Add(next);
                cameFrom[next] = current;
                open.Enqueue(next);
            }
        }

        if (!visited.Contains(goal))
            return false;

        List<RoadNode> reversed = new List<RoadNode>();
        RoadNode cursor = goal;
        reversed.Add(cursor);

        while (cursor != start)
        {
            if (!cameFrom.TryGetValue(cursor, out RoadNode previous))
                return false;

            cursor = previous;
            reversed.Add(cursor);
        }

        reversed.Reverse();
        path = reversed;
        return true;
    }

    public static ContinuousUTurnPath FindManualUTurnPathContainingDirectedSegment(
        RoadNode fromNode,
        RoadNode toNode)
    {
        for (int i = 0; i < activePaths.Count; i++)
        {
            ContinuousUTurnPath path = activePaths[i];

            if (path == null ||
                !path.enabled ||
                !path.gameObject.activeInHierarchy ||
                !path.allowManualUTurns ||
                path.oppositePath == null ||
                !path.oppositePath.enabled ||
                !path.oppositePath.gameObject.activeInHierarchy ||
                !path.oppositePath.allowManualUTurns)
            {
                continue;
            }

            if (path.ContainsDirectedSegment(fromNode, toNode))
                return path;
        }

        return null;
    }

    public static ContinuousUTurnPath FindPathContainingDirectedSegment(
        RoadNode fromNode,
        RoadNode toNode)
    {
        for (int i = 0; i < activePaths.Count; i++)
        {
            ContinuousUTurnPath path = activePaths[i];

            if (path == null || !path.enabled || !path.gameObject.activeInHierarchy)
                continue;

            if (path.ContainsDirectedSegment(fromNode, toNode))
                return path;
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (nodes == null)
            return;

        Gizmos.color = Color.cyan;

        foreach (RoadNode fromNode in nodes)
        {
            if (fromNode == null || fromNode.connectedNodes == null)
                continue;

            foreach (RoadNode toNode in fromNode.connectedNodes)
            {
                if (!ContainsNode(toNode))
                    continue;

                Gizmos.DrawLine(
                    fromNode.transform.position,
                    toNode.transform.position
                );
            }
        }
    }
}
