#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class WalkNodeNetworkValidator
{
    [MenuItem("Tools/Walking/Validate WalkNode Network")]
    public static void ValidateNetwork()
    {
        WalkingNetworkCalculator calculator =
            FindCalculator();

        if (calculator == null)
        {
            Debug.LogError(
                "[WALK NETWORK] No WalkingNetworkCalculator was found in the loaded scene."
            );
            return;
        }

        if (calculator.destinationNode == null)
        {
            Debug.LogError(
                "[WALK NETWORK] WalkingNetworkCalculator has no Destination Node assigned.",
                calculator.gameObject
            );
            return;
        }

        List<WalkNode> nodes =
            GetCalculatorNodes(calculator);

        if (nodes.Count == 0)
        {
            Debug.LogError(
                "[WALK NETWORK] No WalkNodes were found."
            );
            return;
        }

        // Match WalkingNetworkCalculator behavior:
        // referenced neighbours outside the optional root are still used.
        ExpandReferencedNodes(nodes);

        Dictionary<WalkNode, HashSet<WalkNode>> adjacency =
            new Dictionary<WalkNode, HashSet<WalkNode>>();

        foreach (WalkNode node in nodes)
        {
            if (node != null && !adjacency.ContainsKey(node))
            {
                adjacency[node] =
                    new HashSet<WalkNode>();
            }
        }

        int nullConnections = 0;
        int selfConnections = 0;
        int duplicateConnections = 0;
        int oneWayConnections = 0;
        int emptyNodeIds = 0;

        int errorCount = 0;
        int warningCount = 0;

        Dictionary<string, List<WalkNode>> nodesById =
            new Dictionary<string, List<WalkNode>>();

        HashSet<string> checkedOneWayPairs =
            new HashSet<string>();

        HashSet<string> uniqueEdges =
            new HashSet<string>();

        float longestEdgeMeters = 0f;
        WalkNode longestEdgeA = null;
        WalkNode longestEdgeB = null;

        float scale =
            Mathf.Max(0f, calculator.metersPerUnityUnit);

        Debug.Log(
            "========== WALK NODE NETWORK VALIDATION START ==========\n" +
            $"Nodes found: {nodes.Count}\n" +
            $"Destination: {calculator.destinationNode.name}\n" +
            $"Scale: {scale:0.####} metres / Unity unit"
        );

        // ------------------------------------------------------
        // CHECK INDIVIDUAL NODES AND CONNECTIONS
        // ------------------------------------------------------

        foreach (WalkNode node in nodes)
        {
            if (node == null)
                continue;

            // --------------------------
            // Node ID
            // --------------------------

            if (string.IsNullOrWhiteSpace(node.nodeId))
            {
                emptyNodeIds++;
                warningCount++;

                Debug.LogWarning(
                    $"[WALK WARNING] {node.name}\n" +
                    "Node ID is empty.",
                    node.gameObject
                );
            }
            else
            {
                if (!nodesById.ContainsKey(node.nodeId))
                {
                    nodesById[node.nodeId] =
                        new List<WalkNode>();
                }

                nodesById[node.nodeId].Add(node);
            }

            // --------------------------
            // Connections
            // --------------------------

            if (node.connectedNodes == null)
                continue;

            HashSet<WalkNode> seenNeighbours =
                new HashSet<WalkNode>();

            foreach (WalkNode neighbour in node.connectedNodes)
            {
                if (neighbour == null)
                {
                    nullConnections++;
                    errorCount++;

                    Debug.LogError(
                        $"[WALK ERROR] {node.name}\n" +
                        "Contains a missing/null connection.",
                        node.gameObject
                    );

                    continue;
                }

                if (neighbour == node)
                {
                    selfConnections++;
                    errorCount++;

                    Debug.LogError(
                        $"[WALK ERROR] {node.name}\n" +
                        "Node is connected to itself.",
                        node.gameObject
                    );

                    continue;
                }

                if (!seenNeighbours.Add(neighbour))
                {
                    duplicateConnections++;
                    warningCount++;

                    Debug.LogWarning(
                        $"[WALK WARNING] {node.name}\n" +
                        $"Duplicate connection to {neighbour.name}.",
                        node.gameObject
                    );

                    continue;
                }

                // Calculator treats edges as bidirectional.
                AddUndirectedConnection(
                    adjacency,
                    node,
                    neighbour
                );

                // --------------------------
                // Check reciprocal Inspector link
                // --------------------------

                bool neighbourPointsBack =
                    neighbour.connectedNodes != null &&
                    neighbour.connectedNodes.Contains(node);

                if (!neighbourPointsBack)
                {
                    string pairKey =
                        MakePairKey(node, neighbour);

                    if (checkedOneWayPairs.Add(pairKey))
                    {
                        oneWayConnections++;
                        warningCount++;

                        Debug.LogWarning(
                            $"[WALK WARNING] One-way Inspector connection:\n" +
                            $"{node.name} -> {neighbour.name}\n" +
                            $"{neighbour.name} does NOT point back to {node.name}.\n\n" +
                            "WalkingNetworkCalculator currently treats this as " +
                            "bidirectional anyway, but the WalkNodeNetworkTester " +
                            "may behave differently.",
                            node.gameObject
                        );
                    }
                }

                // --------------------------
                // Unique edge + length
                // --------------------------

                string edgeKey =
                    MakePairKey(node, neighbour);

                if (uniqueEdges.Add(edgeKey))
                {
                    float edgeMeters =
                        Vector2.Distance(
                            node.transform.position,
                            neighbour.transform.position
                        ) * scale;

                    if (edgeMeters > longestEdgeMeters)
                    {
                        longestEdgeMeters = edgeMeters;
                        longestEdgeA = node;
                        longestEdgeB = neighbour;
                    }
                }
            }
        }

        // ------------------------------------------------------
        // DUPLICATE NODE IDs
        // ------------------------------------------------------

        int duplicateIdGroups = 0;

        foreach (
            KeyValuePair<string, List<WalkNode>> pair
            in nodesById)
        {
            if (pair.Value.Count <= 1)
                continue;

            duplicateIdGroups++;

            foreach (WalkNode node in pair.Value)
            {
                warningCount++;

                Debug.LogWarning(
                    $"[WALK WARNING] Duplicate Node ID: {pair.Key}\n" +
                    $"{pair.Value.Count} WalkNodes use this ID.",
                    node.gameObject
                );
            }
        }

        // ------------------------------------------------------
        // ISOLATED NODES
        // ------------------------------------------------------

        int isolatedNodes = 0;

        foreach (WalkNode node in nodes)
        {
            if (node == null)
                continue;

            if (!adjacency.TryGetValue(
                    node,
                    out HashSet<WalkNode> neighbours) ||
                neighbours.Count == 0)
            {
                isolatedNodes++;
                errorCount++;

                Debug.LogError(
                    $"[WALK ERROR] Isolated WalkNode: {node.name}\n" +
                    "This node has no usable pedestrian connections.",
                    node.gameObject
                );
            }
        }

        // ------------------------------------------------------
        // DESTINATION REACHABILITY
        //
        // Uses UNDIRECTED graph because this matches
        // WalkingNetworkCalculator.
        // ------------------------------------------------------

        HashSet<WalkNode> reachable =
            FindReachableNodes(
                calculator.destinationNode,
                adjacency
            );

        int unreachableNodes = 0;

        foreach (WalkNode node in nodes)
        {
            if (node == null)
                continue;

            if (!reachable.Contains(node))
            {
                unreachableNodes++;
                errorCount++;

                Debug.LogError(
                    $"[WALK ERROR] {node.name}\n" +
                    $"Cannot reach destination " +
                    $"{calculator.destinationNode.name}.",
                    node.gameObject
                );
            }
        }

        // ------------------------------------------------------
        // CHECK BASIC CALCULATOR SETTINGS
        // ------------------------------------------------------

        if (calculator.metersPerUnityUnit <= 0f)
        {
            errorCount++;

            Debug.LogError(
                "[WALK ERROR] metersPerUnityUnit must be greater than 0.",
                calculator.gameObject
            );
        }

        if (calculator.walkingSpeedMetersPerSecond <= 0f)
        {
            errorCount++;

            Debug.LogError(
                "[WALK ERROR] Walking Speed must be greater than 0.",
                calculator.gameObject
            );
        }

        if (calculator.innerThresholdMeters >
            calculator.outerThresholdMeters)
        {
            errorCount++;

            Debug.LogError(
                "[WALK ERROR] Inner distance threshold is greater than " +
                "Outer threshold.",
                calculator.gameObject
            );
        }

        // ------------------------------------------------------
        // SUMMARY
        // ------------------------------------------------------

        string longestEdgeText = "N/A";

        if (longestEdgeA != null &&
            longestEdgeB != null)
        {
            longestEdgeText =
                $"{longestEdgeMeters:0.00} m " +
                $"({longestEdgeA.name} <-> {longestEdgeB.name})";
        }

        Debug.Log(
            "========== WALK NODE NETWORK VALIDATION FINISHED ==========\n" +
            $"WalkNodes checked: {nodes.Count}\n" +
            $"Unique pedestrian segments: {uniqueEdges.Count}\n" +
            $"Destination: {calculator.destinationNode.name}\n\n" +

            $"Isolated nodes: {isolatedNodes}\n" +
            $"Unreachable from destination: {unreachableNodes}\n" +
            $"Null connections: {nullConnections}\n" +
            $"Self connections: {selfConnections}\n\n" +

            $"One-way Inspector connections: {oneWayConnections}\n" +
            $"Duplicate connections: {duplicateConnections}\n" +
            $"Duplicate Node ID groups: {duplicateIdGroups}\n" +
            $"Empty Node IDs: {emptyNodeIds}\n\n" +

            $"Longest pedestrian segment: {longestEdgeText}\n\n" +

            $"Errors: {errorCount}\n" +
            $"Warnings: {warningCount}\n\n" +

            (errorCount == 0
                ? "✓ No critical WalkNode network errors found."
                : "✗ Fix the WALK ERROR entries before final WebGL build.")
        );
    }

    // ==========================================================
    // FIND CALCULATOR
    // ==========================================================

    private static WalkingNetworkCalculator FindCalculator()
    {
        WalkingNetworkCalculator[] calculators =
            Resources.FindObjectsOfTypeAll
                <WalkingNetworkCalculator>();

        WalkingNetworkCalculator firstValid = null;

        foreach (
            WalkingNetworkCalculator calculator
            in calculators)
        {
            if (calculator == null)
                continue;

            if (EditorUtility.IsPersistent(calculator))
                continue;

            if (!calculator.gameObject.scene.IsValid())
                continue;

            if (firstValid == null)
                firstValid = calculator;

            if (calculator.destinationNode != null)
                return calculator;
        }

        return firstValid;
    }

    // ==========================================================
    // FIND WALK NODES
    // ==========================================================

    private static List<WalkNode> GetCalculatorNodes(
        WalkingNetworkCalculator calculator)
    {
        List<WalkNode> result =
            new List<WalkNode>();

        if (calculator.walkNodesRoot != null)
        {
            WalkNode[] rootNodes =
                calculator.walkNodesRoot
                    .GetComponentsInChildren<WalkNode>(
                        calculator.includeInactiveObjects
                    );

            foreach (WalkNode node in rootNodes)
            {
                if (node != null &&
                    !result.Contains(node))
                {
                    result.Add(node);
                }
            }
        }
        else
        {
            WalkNode[] allNodes =
                Resources.FindObjectsOfTypeAll<WalkNode>();

            foreach (WalkNode node in allNodes)
            {
                if (node == null)
                    continue;

                if (EditorUtility.IsPersistent(node))
                    continue;

                if (!node.gameObject.scene.IsValid())
                    continue;

                if (!calculator.includeInactiveObjects &&
                    !node.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!result.Contains(node))
                    result.Add(node);
            }
        }

        if (calculator.destinationNode != null &&
            !result.Contains(calculator.destinationNode))
        {
            result.Add(calculator.destinationNode);
        }

        return result;
    }

    // ==========================================================
    // MATCH CALCULATOR BEHAVIOUR
    // Add referenced nodes outside optional root.
    // ==========================================================

    private static void ExpandReferencedNodes(
        List<WalkNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            WalkNode node = nodes[i];

            if (node == null ||
                node.connectedNodes == null)
            {
                continue;
            }

            foreach (
                WalkNode neighbour
                in node.connectedNodes)
            {
                if (neighbour != null &&
                    !nodes.Contains(neighbour))
                {
                    nodes.Add(neighbour);
                }
            }
        }
    }

    // ==========================================================
    // GRAPH HELPERS
    // ==========================================================

    private static void AddUndirectedConnection(
        Dictionary<WalkNode, HashSet<WalkNode>> adjacency,
        WalkNode a,
        WalkNode b)
    {
        if (!adjacency.ContainsKey(a))
            adjacency[a] = new HashSet<WalkNode>();

        if (!adjacency.ContainsKey(b))
            adjacency[b] = new HashSet<WalkNode>();

        adjacency[a].Add(b);
        adjacency[b].Add(a);
    }

    private static HashSet<WalkNode> FindReachableNodes(
        WalkNode destination,
        Dictionary<WalkNode, HashSet<WalkNode>> adjacency)
    {
        HashSet<WalkNode> visited =
            new HashSet<WalkNode>();

        if (destination == null)
            return visited;

        Queue<WalkNode> queue =
            new Queue<WalkNode>();

        visited.Add(destination);
        queue.Enqueue(destination);

        while (queue.Count > 0)
        {
            WalkNode current = queue.Dequeue();

            if (!adjacency.TryGetValue(
                    current,
                    out HashSet<WalkNode> neighbours))
            {
                continue;
            }

            foreach (WalkNode neighbour in neighbours)
            {
                if (neighbour == null)
                    continue;

                if (visited.Add(neighbour))
                    queue.Enqueue(neighbour);
            }
        }

        return visited;
    }

    private static string MakePairKey(
        WalkNode a,
        WalkNode b)
    {
        int idA = a.GetInstanceID();
        int idB = b.GetInstanceID();

        if (idA < idB)
            return idA + ":" + idB;

        return idB + ":" + idA;
    }
}

#endif