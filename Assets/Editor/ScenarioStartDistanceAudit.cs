#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ScenarioStartDistanceAudit
{
    private sealed class Row
    {
        public ParkingScenario scenario;
        public int scenarioListIndex;
        public RoadNode startNode;
        public float straightLineMeters;
        public float roadNetworkMeters;
        public bool routeFound;
    }

    [MenuItem("Tools/Scenarios/Audit Start Distances")]
    public static void AuditStartDistances()
    {
        ScenarioManager manager =
            UnityEngine.Object.FindFirstObjectByType<ScenarioManager>();

        if (manager == null)
        {
            Debug.LogError(
                "[START DISTANCE AUDIT] No ScenarioManager found in the loaded scene."
            );
            return;
        }

        if (manager.destinationGate == null)
        {
            Debug.LogError(
                "[START DISTANCE AUDIT] ScenarioManager.destinationGate is not assigned.",
                manager.gameObject
            );
            return;
        }

        if (manager.scenarios == null || manager.scenarios.Count == 0)
        {
            Debug.LogError(
                "[START DISTANCE AUDIT] ScenarioManager has no scenarios.",
                manager.gameObject
            );
            return;
        }

        if (manager.scenarioStartNodes == null ||
            manager.scenarioStartNodes.Length == 0)
        {
            Debug.LogError(
                "[START DISTANCE AUDIT] ScenarioManager has no Scenario Start Nodes.",
                manager.gameObject
            );
            return;
        }

        RoadNode[] allNodes =
            UnityEngine.Object.FindObjectsByType<RoadNode>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        if (allNodes == null || allNodes.Length == 0)
        {
            Debug.LogError(
                "[START DISTANCE AUDIT] No RoadNodes found in the loaded scene."
            );
            return;
        }

        RoadNode gateRoadNode =
            FindNearestRoadNode(
                manager.destinationGate.position,
                allNodes
            );

        if (gateRoadNode == null)
        {
            Debug.LogError(
                "[START DISTANCE AUDIT] Could not resolve a RoadNode near Destination Gate."
            );
            return;
        }

        float scale = Mathf.Max(
            0f,
            manager.metersPerUnityUnit
        );

        List<Row> rows = new List<Row>();

        for (int i = 0; i < manager.scenarios.Count; i++)
        {
            ParkingScenario scenario =
                manager.scenarios[i];

            if (scenario == null)
                continue;

            int startIndex =
                scenario.startPointIndex;

            RoadNode startNode = null;

            if (startIndex >= 0 &&
                startIndex < manager.scenarioStartNodes.Length)
            {
                startNode =
                    manager.scenarioStartNodes[startIndex];
            }

            Row row = new Row
            {
                scenario = scenario,
                scenarioListIndex = i,
                startNode = startNode,
                straightLineMeters = 0f,
                roadNetworkMeters = 0f,
                routeFound = false
            };

            if (startNode != null)
            {
                row.straightLineMeters =
                    Vector2.Distance(
                        startNode.transform.position,
                        manager.destinationGate.position
                    ) * scale;

                if (TryShortestDirectedDistance(
                        startNode,
                        gateRoadNode,
                        scale,
                        out float routeMeters))
                {
                    row.roadNetworkMeters = routeMeters;
                    row.routeFound = true;
                }
            }

            rows.Add(row);
        }

        List<float> validDistances =
            new List<float>();

        foreach (Row row in rows)
        {
            if (row.routeFound)
                validDistances.Add(row.roadNetworkMeters);
        }

        validDistances.Sort();

        float nearCut = Percentile(validDistances, 1f / 3f);
        float farCut = Percentile(validDistances, 2f / 3f);

        Debug.Log(
            "========== SCENARIO START DISTANCE AUDIT ==========\n" +
            $"Destination Gate: {manager.destinationGate.name}\n" +
            $"Nearest RoadNode to gate: {gateRoadNode.name} ({gateRoadNode.nodeId})\n" +
            $"Scale: {scale:0.####} m / Unity unit\n" +
            $"Near/Mid cut: {nearCut:0.0} m\n" +
            $"Mid/Far cut: {farCut:0.0} m"
        );

        foreach (Row row in rows)
        {
            string category;

            if (!row.routeFound)
            {
                category = "NO ROUTE";
            }
            else if (row.roadNetworkMeters <= nearCut)
            {
                category = "NEAR";
            }
            else if (row.roadNetworkMeters <= farCut)
            {
                category = "MID";
            }
            else
            {
                category = "FAR";
            }

            string startName =
                row.startNode != null
                    ? row.startNode.name
                    : "NULL";

            string startId =
                row.startNode != null
                    ? row.startNode.nodeId
                    : "";

            string routeText =
                row.routeFound
                    ? $"{row.roadNetworkMeters:0.0} m"
                    : "NO DIRECTED ROUTE";

            Debug.Log(
                $"[START AUDIT] {row.scenario.scenarioId} | " +
                $"SearchLimit={row.scenario.timeLimitMinutes:0.#} min | " +
                $"Dwell={row.scenario.dwellTimeMinutes:0.#} min | " +
                $"StartIndex={row.scenario.startPointIndex} | " +
                $"Start={startName} ({startId}) | " +
                $"Straight={row.straightLineMeters:0.0} m | " +
                $"RoadNetwork={routeText} | " +
                $"Class={category}",
                row.startNode != null
                    ? row.startNode.gameObject
                    : manager.gameObject
            );
        }

        Debug.Log(
            "========== START DISTANCE AUDIT FINISHED ==========\n" +
            "Use RoadNetwork distance for experimental balancing; " +
            "Straight-line distance is only a sanity check."
        );
    }

    private static RoadNode FindNearestRoadNode(
        Vector3 worldPosition,
        RoadNode[] nodes)
    {
        RoadNode best = null;
        float bestSqr = float.PositiveInfinity;

        foreach (RoadNode node in nodes)
        {
            if (node == null)
                continue;

            float sqr =
                ((Vector2)node.transform.position -
                 (Vector2)worldPosition).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = node;
            }
        }

        return best;
    }

    private static bool TryShortestDirectedDistance(
        RoadNode start,
        RoadNode goal,
        float metersPerUnityUnit,
        out float distanceMeters)
    {
        distanceMeters = 0f;

        if (start == null || goal == null)
            return false;

        if (start == goal)
        {
            distanceMeters = 0f;
            return true;
        }

        Dictionary<RoadNode, float> distance =
            new Dictionary<RoadNode, float>();

        HashSet<RoadNode> visited =
            new HashSet<RoadNode>();

        List<RoadNode> open =
            new List<RoadNode>();

        distance[start] = 0f;
        open.Add(start);

        while (open.Count > 0)
        {
            RoadNode current =
                GetClosestOpenNode(
                    open,
                    distance
                );

            open.Remove(current);

            if (current == null ||
                visited.Contains(current))
            {
                continue;
            }

            if (current == goal)
            {
                distanceMeters =
                    distance[current] *
                    metersPerUnityUnit;

                return true;
            }

            visited.Add(current);

            if (current.connectedNodes == null)
                continue;

            foreach (RoadNode neighbour in current.connectedNodes)
            {
                if (neighbour == null)
                    continue;

                if (visited.Contains(neighbour))
                    continue;

                float segment =
                    Vector2.Distance(
                        current.transform.position,
                        neighbour.transform.position
                    );

                float newDistance =
                    distance[current] + segment;

                if (!distance.TryGetValue(
                        neighbour,
                        out float oldDistance) ||
                    newDistance < oldDistance)
                {
                    distance[neighbour] =
                        newDistance;

                    if (!open.Contains(neighbour))
                        open.Add(neighbour);
                }
            }
        }

        return false;
    }

    private static RoadNode GetClosestOpenNode(
        List<RoadNode> open,
        Dictionary<RoadNode, float> distance)
    {
        RoadNode best = null;
        float bestDistance = float.PositiveInfinity;

        foreach (RoadNode node in open)
        {
            if (node == null)
                continue;

            float d =
                distance.TryGetValue(
                    node,
                    out float value)
                    ? value
                    : float.PositiveInfinity;

            if (d < bestDistance)
            {
                bestDistance = d;
                best = node;
            }
        }

        return best;
    }

    private static float Percentile(
        List<float> sorted,
        float p)
    {
        if (sorted == null ||
            sorted.Count == 0)
        {
            return 0f;
        }

        if (sorted.Count == 1)
            return sorted[0];

        float index =
            Mathf.Clamp01(p) *
            (sorted.Count - 1);

        int lower =
            Mathf.FloorToInt(index);

        int upper =
            Mathf.CeilToInt(index);

        if (lower == upper)
            return sorted[lower];

        float t = index - lower;

        return Mathf.Lerp(
            sorted[lower],
            sorted[upper],
            t
        );
    }
}

#endif
