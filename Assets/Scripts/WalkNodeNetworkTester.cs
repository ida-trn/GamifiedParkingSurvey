using System.Collections.Generic;
using UnityEngine;

public class WalkNodeNetworkTester : MonoBehaviour
{
    [Header("Path Test")]
    [Tooltip("The node where the test route begins.")]
    public WalkNode startNode;

    [Tooltip("Normally assign WN_GATE2 here.")]
    public WalkNode destinationNode;

    [Header("Map Scale")]
    [Tooltip("80 real metres / 330 Unity units = 0.2424.")]
    public float metersPerUnityUnit = 0.2424f;

    [Header("Testing")]
    public bool testOnStart = false;
    public bool drawFoundPath = true;

    [Tooltip("Only changes the Z position of the debug route.")]
    public float lineHeightOffset = -1f;

    [Header("Last Test Result")]
    public float foundDistanceMeters;
    public int foundNodeCount;

    private readonly List<WalkNode> foundPath =
        new List<WalkNode>();

    private void Start()
    {
        if (testOnStart)
            TestWalkNodePath();
    }

    [ContextMenu("Test WalkNode Path")]
    public void TestWalkNodePath()
    {
        foundPath.Clear();
        foundDistanceMeters = 0f;
        foundNodeCount = 0;

        if (startNode == null)
        {
            Debug.LogError(
                "[WalkNodeNetworkTester] Start Node is not assigned."
            );
            return;
        }

        if (destinationNode == null)
        {
            Debug.LogError(
                "[WalkNodeNetworkTester] Destination Node is not assigned."
            );
            return;
        }

        if (startNode == destinationNode)
        {
            foundPath.Add(startNode);
            foundNodeCount = 1;

            Debug.Log(
                "[WalkNodeNetworkTester] Start and destination are the same. " +
                "Distance = 0.0 m"
            );

            return;
        }

        float safeScale = Mathf.Max(0f, metersPerUnityUnit);

        Dictionary<WalkNode, float> distances =
            new Dictionary<WalkNode, float>();

        Dictionary<WalkNode, WalkNode> previousNode =
            new Dictionary<WalkNode, WalkNode>();

        List<WalkNode> openNodes = new List<WalkNode>();
        HashSet<WalkNode> visitedNodes = new HashSet<WalkNode>();

        distances[startNode] = 0f;
        openNodes.Add(startNode);

        bool destinationReached = false;

        while (openNodes.Count > 0)
        {
            WalkNode currentNode = GetClosestOpenNode(
                openNodes,
                distances
            );

            openNodes.Remove(currentNode);

            if (visitedNodes.Contains(currentNode))
                continue;

            visitedNodes.Add(currentNode);

            if (currentNode == destinationNode)
            {
                destinationReached = true;
                break;
            }

            if (currentNode.connectedNodes == null)
                continue;

            foreach (WalkNode neighbour in currentNode.connectedNodes)
            {
                if (neighbour == null)
                    continue;

                if (visitedNodes.Contains(neighbour))
                    continue;

                float segmentDistanceMeters =
                    Vector2.Distance(
                        currentNode.transform.position,
                        neighbour.transform.position
                    ) * safeScale;

                float newDistance =
                    distances[currentNode] +
                    segmentDistanceMeters;

                bool hasOldDistance =
                    distances.TryGetValue(
                        neighbour,
                        out float oldDistance
                    );

                if (!hasOldDistance || newDistance < oldDistance)
                {
                    distances[neighbour] = newDistance;
                    previousNode[neighbour] = currentNode;

                    if (!openNodes.Contains(neighbour))
                        openNodes.Add(neighbour);
                }
            }
        }

        if (!destinationReached ||
            !distances.ContainsKey(destinationNode))
        {
            Debug.LogError(
                $"[WalkNodeNetworkTester] No pedestrian path exists from " +
                $"{startNode.name} to {destinationNode.name}."
            );

            return;
        }

        ReconstructPath(previousNode);

        foundDistanceMeters = distances[destinationNode];
        foundNodeCount = foundPath.Count;

        string pathText = BuildPathText();

        Debug.Log(
            $"[WalkNodeNetworkTester] PATH FOUND!\n" +
            $"Start: {startNode.name}\n" +
            $"Destination: {destinationNode.name}\n" +
            $"Nodes: {foundNodeCount}\n" +
            $"Distance: {foundDistanceMeters:0.0} m\n" +
            $"Path: {pathText}"
        );
    }

    private WalkNode GetClosestOpenNode(
        List<WalkNode> openNodes,
        Dictionary<WalkNode, float> distances)
    {
        WalkNode closestNode = openNodes[0];
        float closestDistance = GetDistance(
            closestNode,
            distances
        );

        for (int i = 1; i < openNodes.Count; i++)
        {
            WalkNode candidate = openNodes[i];

            float candidateDistance = GetDistance(
                candidate,
                distances
            );

            if (candidateDistance < closestDistance)
            {
                closestNode = candidate;
                closestDistance = candidateDistance;
            }
        }

        return closestNode;
    }

    private float GetDistance(
        WalkNode node,
        Dictionary<WalkNode, float> distances)
    {
        if (distances.TryGetValue(node, out float distance))
            return distance;

        return float.PositiveInfinity;
    }

    private void ReconstructPath(
        Dictionary<WalkNode, WalkNode> previousNode)
    {
        foundPath.Clear();

        WalkNode currentNode = destinationNode;
        foundPath.Add(currentNode);

        while (currentNode != startNode)
        {
            if (!previousNode.TryGetValue(
                    currentNode,
                    out WalkNode previous))
            {
                Debug.LogError(
                    "[WalkNodeNetworkTester] Path reconstruction failed."
                );

                foundPath.Clear();
                return;
            }

            currentNode = previous;
            foundPath.Add(currentNode);
        }

        foundPath.Reverse();
    }

    private string BuildPathText()
    {
        string pathText = "";

        for (int i = 0; i < foundPath.Count; i++)
        {
            if (i > 0)
                pathText += " -> ";

            pathText += foundPath[i].name;
        }

        return pathText;
    }

    private void OnDrawGizmos()
    {
        if (!drawFoundPath ||
            foundPath == null ||
            foundPath.Count < 2)
        {
            return;
        }

        for (int i = 0; i < foundPath.Count - 1; i++)
        {
            WalkNode current = foundPath[i];
            WalkNode next = foundPath[i + 1];

            if (current == null || next == null)
                continue;

            Vector3 start = current.transform.position;
            Vector3 end = next.transform.position;

            start.z += lineHeightOffset;
            end.z += lineHeightOffset;

            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, 1.8f);
        }

        Vector3 last =
            foundPath[foundPath.Count - 1].transform.position;

        last.z += lineHeightOffset;
        Gizmos.DrawWireSphere(last, 1.8f);
    }
}