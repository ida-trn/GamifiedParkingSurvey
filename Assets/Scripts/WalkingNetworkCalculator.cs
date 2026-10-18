using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Calculates a sidewalk-route distance from every ParkingSpot to a destination WalkNode.
///
/// For each parking spot, the origin is projected onto the nearest reachable WalkNode segment.
/// The total distance is:
/// parking spot -> projected point on sidewalk -> shortest WalkNode route -> destination.
///
/// Results are written into ParkingSpot.walkingDistanceMeters and
/// ParkingSpot.walkingTimeSeconds so ScenarioManager and ParkingLogger can reuse them.
/// </summary>
public class WalkingNetworkCalculator : MonoBehaviour
{
    [Serializable]
    public class SpotWalkingResult
    {
        public ParkingSpot parkingSpot;
        public bool reachable;
        public WalkNode segmentStart;
        public WalkNode segmentEnd;
        public WalkNode routeStartsToward;
        public Vector3 networkEntryPoint;
        public float parkingToNetworkDistanceMeters;
        public float networkDistanceMeters;
        public float totalWalkingDistanceMeters;
        public float walkingTimeSeconds;
        public string note;
    }

    private class Segment
    {
        public WalkNode a;
        public WalkNode b;
    }

    [Header("Destination")]
    [Tooltip("Assign the WalkNode placed at Gate 2 (Mellat Gate), normally WN_GATE2.")]
    public WalkNode destinationNode;

    [Header("Optional Roots")]
    [Tooltip("If assigned, only WalkNodes below this object are used.")]
    public Transform walkNodesRoot;

    [Tooltip("If assigned, only ParkingSpots below this object are used.")]
    public Transform parkingSpotsRoot;

    [Header("Scale and Walking Speed")]
    [Tooltip("80 real metres / 330 Unity units = 0.2424.")]
    [Min(0f)] public float metersPerUnityUnit = 0.2424f;

    [Tooltip("Normal adult walking speed is approximately 1.4 m/s.")]
    [Min(0.01f)] public float walkingSpeedMetersPerSecond = 1.4f;

    [Header("Parking-to-Sidewalk Link")]
    [Tooltip("A warning is produced when the nearest reachable sidewalk segment is farther than this value. Set 0 to disable the limit.")]
    [Min(0f)] public float maximumParkingLinkDistanceMeters = 30f;

    [Header("Distance Regions")]
    [Tooltip("When enabled, Inner/Outer/Far are assigned using the calculated sidewalk-route distance.")]
    public bool updateRegionsFromWalkingDistance = true;

    [Min(0f)] public float innerThresholdMeters = 100f;
    [Min(0f)] public float outerThresholdMeters = 200f;

    [Tooltip("Regenerate SpotId after a region changes, so A1-A5 in the ID remains consistent.")]
    public bool updateSpotIdsAfterRegionChange = true;

    [Header("Automatic Calculation")]
    public bool calculateOnStart = true;
    public bool includeInactiveObjects = true;

    [Header("Debug")]
    public bool debugLogs = false;
    public bool drawParkingToNetworkLinks = false;
    public List<SpotWalkingResult> lastResults = new List<SpotWalkingResult>();

    private readonly Dictionary<WalkNode, List<WalkNode>> adjacency =
        new Dictionary<WalkNode, List<WalkNode>>();

    private readonly Dictionary<WalkNode, float> distanceToDestination =
        new Dictionary<WalkNode, float>();

    private readonly Dictionary<ParkingSpot, SpotWalkingResult> resultBySpot =
        new Dictionary<ParkingSpot, SpotWalkingResult>();

    private readonly List<WalkNode> nodes = new List<WalkNode>();
    private readonly List<Segment> segments = new List<Segment>();

    private bool cacheReady;

    private void Start()
    {
        if (calculateOnStart)
            RecalculateAllParkingSpots();
    }

    [ContextMenu("Calculate All Parking Walking Distances")]
    public void RecalculateAllParkingSpots()
    {
        cacheReady = false;
        lastResults.Clear();
        resultBySpot.Clear();

        if (!BuildNetworkCache())
            return;

        ParkingSpot[] spots = FindParkingSpots();

        if (spots == null || spots.Length == 0)
        {
            Debug.LogError("[WalkingNetworkCalculator] No ParkingSpot objects were found.");
            return;
        }

        int reachableCount = 0;

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            SpotWalkingResult result = CalculateSpotResult(spot);
            lastResults.Add(result);
            resultBySpot[spot] = result;

            if (result.reachable)
            {
                reachableCount++;
                WriteResultToParkingSpot(result);
            }
            else
            {
                spot.walkingDistanceMeters = 0f;
                spot.walkingTimeSeconds = 0f;

                Debug.LogWarning(
                    $"[WalkingNetworkCalculator] No pedestrian route was found for {spot.name}. " +
                    result.note
                );
            }
        }

        cacheReady = true;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif

        Debug.Log(
            $"[WalkingNetworkCalculator] Walking distances calculated. " +
            $"Reachable parking spots: {reachableCount}/{spots.Length}, " +
            $"WalkNodes: {nodes.Count}, segments: {segments.Count}, " +
            $"destination: {destinationNode.name}."
        );
    }

    public bool TryCalculateSpotDistance(
        ParkingSpot spot,
        out float distanceMeters,
        out float walkingTimeSeconds)
    {
        distanceMeters = 0f;
        walkingTimeSeconds = 0f;

        if (spot == null)
            return false;

        if (!cacheReady)
        {
            RecalculateAllParkingSpots();
        }

        if (resultBySpot.TryGetValue(spot, out SpotWalkingResult cached) &&
            cached != null && cached.reachable)
        {
            distanceMeters = cached.totalWalkingDistanceMeters;
            walkingTimeSeconds = cached.walkingTimeSeconds;
            return true;
        }

        if (!BuildNetworkCache())
            return false;

        SpotWalkingResult result = CalculateSpotResult(spot);
        resultBySpot[spot] = result;

        if (!result.reachable)
            return false;

        WriteResultToParkingSpot(result);
        distanceMeters = result.totalWalkingDistanceMeters;
        walkingTimeSeconds = result.walkingTimeSeconds;
        return true;
    }

    public WalkNode FindNearestWalkNode(Vector3 position)
    {
        WalkNode[] availableNodes = FindWalkNodes();
        WalkNode nearest = null;
        float bestSquaredDistance = float.PositiveInfinity;

        foreach (WalkNode node in availableNodes)
        {
            if (node == null)
                continue;

            float squaredDistance =
                ((Vector2)node.transform.position - (Vector2)position).sqrMagnitude;

            if (squaredDistance < bestSquaredDistance)
            {
                bestSquaredDistance = squaredDistance;
                nearest = node;
            }
        }

        return nearest;
    }

    private bool BuildNetworkCache()
    {
        nodes.Clear();
        segments.Clear();
        adjacency.Clear();
        distanceToDestination.Clear();

        if (destinationNode == null)
        {
            Debug.LogError(
                "[WalkingNetworkCalculator] Destination Node is not assigned. " +
                "Assign WN_GATE2."
            );
            return false;
        }

        WalkNode[] foundNodes = FindWalkNodes();

        foreach (WalkNode node in foundNodes)
        {
            if (node == null || nodes.Contains(node))
                continue;

            nodes.Add(node);
            adjacency[node] = new List<WalkNode>();
        }

        if (!nodes.Contains(destinationNode))
        {
            nodes.Add(destinationNode);
            adjacency[destinationNode] = new List<WalkNode>();
        }

        if (nodes.Count == 0)
        {
            Debug.LogError("[WalkingNetworkCalculator] No WalkNode objects were found.");
            return false;
        }

        // Use an index loop because a referenced neighbour may not have been
        // found under the optional root and must be added to the working list.
        for (int i = 0; i < nodes.Count; i++)
        {
            WalkNode node = nodes[i];

            if (node == null || node.connectedNodes == null)
                continue;

            foreach (WalkNode neighbour in node.connectedNodes)
            {
                if (neighbour == null || neighbour == node)
                    continue;

                if (!adjacency.ContainsKey(neighbour))
                {
                    nodes.Add(neighbour);
                    adjacency[neighbour] = new List<WalkNode>();
                }

                AddUnique(adjacency[node], neighbour);
                AddUnique(adjacency[neighbour], node);
            }
        }

        BuildUniqueSegments();
        CalculateNodeDistancesToDestination();
        cacheReady = true;
        return true;
    }

    private void BuildUniqueSegments()
    {
        HashSet<string> segmentKeys = new HashSet<string>();

        foreach (KeyValuePair<WalkNode, List<WalkNode>> pair in adjacency)
        {
            WalkNode a = pair.Key;

            foreach (WalkNode b in pair.Value)
            {
                if (a == null || b == null || a == b)
                    continue;

                int idA = a.GetInstanceID();
                int idB = b.GetInstanceID();
                int low = Mathf.Min(idA, idB);
                int high = Mathf.Max(idA, idB);
                string key = low + ":" + high;

                if (!segmentKeys.Add(key))
                    continue;

                segments.Add(new Segment { a = a, b = b });
            }
        }
    }

    private void CalculateNodeDistancesToDestination()
    {
        foreach (WalkNode node in nodes)
            distanceToDestination[node] = float.PositiveInfinity;

        distanceToDestination[destinationNode] = 0f;

        List<WalkNode> openNodes = new List<WalkNode> { destinationNode };
        HashSet<WalkNode> visited = new HashSet<WalkNode>();
        float scale = Mathf.Max(0f, metersPerUnityUnit);

        while (openNodes.Count > 0)
        {
            WalkNode current = GetClosestOpenNode(openNodes);
            openNodes.Remove(current);

            if (current == null || visited.Contains(current))
                continue;

            visited.Add(current);

            if (!adjacency.TryGetValue(current, out List<WalkNode> neighbours))
                continue;

            foreach (WalkNode neighbour in neighbours)
            {
                if (neighbour == null || visited.Contains(neighbour))
                    continue;

                float edgeDistance =
                    Vector2.Distance(current.transform.position, neighbour.transform.position) * scale;

                float candidate = distanceToDestination[current] + edgeDistance;

                if (!distanceToDestination.TryGetValue(neighbour, out float existing) ||
                    candidate < existing)
                {
                    distanceToDestination[neighbour] = candidate;

                    if (!openNodes.Contains(neighbour))
                        openNodes.Add(neighbour);
                }
            }
        }
    }

    private SpotWalkingResult CalculateSpotResult(ParkingSpot spot)
    {
        SpotWalkingResult result = new SpotWalkingResult
        {
            parkingSpot = spot,
            reachable = false,
            note = string.Empty
        };

        if (spot == null)
        {
            result.note = "ParkingSpot is null.";
            return result;
        }

        float scale = Mathf.Max(0f, metersPerUnityUnit);
        Vector2 spotPosition = spot.transform.position;

        Segment bestSegment = null;
        Vector2 bestEntryPoint = Vector2.zero;
        float bestParkingLinkMeters = float.PositiveInfinity;

        // Select the nearest segment that is connected to the destination.
        foreach (Segment segment in segments)
        {
            if (segment == null || segment.a == null || segment.b == null)
                continue;

            bool aReachable = IsNodeReachable(segment.a);
            bool bReachable = IsNodeReachable(segment.b);

            if (!aReachable && !bReachable)
                continue;

            Vector2 a = segment.a.transform.position;
            Vector2 b = segment.b.transform.position;
            Vector2 entryPoint = ClosestPointOnSegment(spotPosition, a, b);
            float parkingLinkMeters = Vector2.Distance(spotPosition, entryPoint) * scale;

            if (parkingLinkMeters < bestParkingLinkMeters)
            {
                bestParkingLinkMeters = parkingLinkMeters;
                bestEntryPoint = entryPoint;
                bestSegment = segment;
            }
        }

        if (bestSegment == null)
        {
            // A one-node network has no segment. Fall back to the nearest reachable node.
            WalkNode nearestReachableNode = null;
            float nearestNodeDistanceMeters = float.PositiveInfinity;

            foreach (WalkNode node in nodes)
            {
                if (!IsNodeReachable(node))
                    continue;

                float linkDistance = Vector2.Distance(spotPosition, node.transform.position) * scale;

                if (linkDistance < nearestNodeDistanceMeters)
                {
                    nearestNodeDistanceMeters = linkDistance;
                    nearestReachableNode = node;
                }
            }

            if (nearestReachableNode == null)
            {
                result.note = "No reachable WalkNode or WalkNode segment exists.";
                return result;
            }

            result.reachable = true;
            result.segmentStart = nearestReachableNode;
            result.segmentEnd = nearestReachableNode;
            result.routeStartsToward = nearestReachableNode;
            result.networkEntryPoint = nearestReachableNode.transform.position;
            result.parkingToNetworkDistanceMeters = nearestNodeDistanceMeters;
            result.networkDistanceMeters = distanceToDestination[nearestReachableNode];
            result.totalWalkingDistanceMeters =
                nearestNodeDistanceMeters + distanceToDestination[nearestReachableNode];
            result.walkingTimeSeconds = CalculateWalkingTime(result.totalWalkingDistanceMeters);
            result.note = "Used nearest reachable WalkNode because no connected segment was available.";
            return result;
        }

        Vector2 segmentA = bestSegment.a.transform.position;
        Vector2 segmentB = bestSegment.b.transform.position;

        float entryToAMeters = Vector2.Distance(bestEntryPoint, segmentA) * scale;
        float entryToBMeters = Vector2.Distance(bestEntryPoint, segmentB) * scale;

        float viaA = IsNodeReachable(bestSegment.a)
            ? entryToAMeters + distanceToDestination[bestSegment.a]
            : float.PositiveInfinity;

        float viaB = IsNodeReachable(bestSegment.b)
            ? entryToBMeters + distanceToDestination[bestSegment.b]
            : float.PositiveInfinity;

        float networkRouteMeters;
        WalkNode routeStartsToward;

        if (viaA <= viaB)
        {
            networkRouteMeters = viaA;
            routeStartsToward = bestSegment.a;
        }
        else
        {
            networkRouteMeters = viaB;
            routeStartsToward = bestSegment.b;
        }

        if (float.IsInfinity(networkRouteMeters))
        {
            result.note = "The nearest sidewalk segment is not connected to the destination.";
            return result;
        }

        result.reachable = true;
        result.segmentStart = bestSegment.a;
        result.segmentEnd = bestSegment.b;
        result.routeStartsToward = routeStartsToward;
        result.networkEntryPoint = new Vector3(
            bestEntryPoint.x,
            bestEntryPoint.y,
            spot.transform.position.z
        );
        result.parkingToNetworkDistanceMeters = bestParkingLinkMeters;
        result.networkDistanceMeters = networkRouteMeters;
        result.totalWalkingDistanceMeters = bestParkingLinkMeters + networkRouteMeters;
        result.walkingTimeSeconds = CalculateWalkingTime(result.totalWalkingDistanceMeters);

        if (maximumParkingLinkDistanceMeters > 0f &&
            bestParkingLinkMeters > maximumParkingLinkDistanceMeters)
        {
            result.note =
                $"Nearest sidewalk link is {bestParkingLinkMeters:0.0} m away, " +
                $"which exceeds the recommended {maximumParkingLinkDistanceMeters:0.0} m.";

            Debug.LogWarning(
                $"[WalkingNetworkCalculator] {spot.name}: {result.note}"
            );
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[WalkingNetworkCalculator] {spot.name}: " +
                $"parking-to-sidewalk={result.parkingToNetworkDistanceMeters:0.0} m, " +
                $"sidewalk-route={result.networkDistanceMeters:0.0} m, " +
                $"total={result.totalWalkingDistanceMeters:0.0} m, " +
                $"time={result.walkingTimeSeconds:0.0} s, " +
                $"segment={bestSegment.a.name}<->{bestSegment.b.name}, " +
                $"toward={routeStartsToward.name}."
            );
        }

        return result;
    }

    private void WriteResultToParkingSpot(SpotWalkingResult result)
    {
        ParkingSpot spot = result.parkingSpot;

        if (spot == null || !result.reachable)
            return;

        spot.walkingDistanceMeters = Mathf.Max(0f, result.totalWalkingDistanceMeters);
        spot.walkingTimeSeconds = Mathf.Max(0f, result.walkingTimeSeconds);

        if (updateRegionsFromWalkingDistance)
        {
            RegionType newRegion;

            if (spot.walkingDistanceMeters < innerThresholdMeters)
                newRegion = RegionType.Inner;
            else if (spot.walkingDistanceMeters <= outerThresholdMeters)
                newRegion = RegionType.Outer;
            else
                newRegion = RegionType.Far;

            bool regionChanged = spot.region != newRegion;
            spot.region = newRegion;

            if (regionChanged && updateSpotIdsAfterRegionChange)
                spot.GenerateSpotId();
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(spot);
#endif
    }

    private float CalculateWalkingTime(float distanceMeters)
    {
        float speed = Mathf.Max(0.01f, walkingSpeedMetersPerSecond);
        return Mathf.Max(0f, distanceMeters) / speed;
    }

    private bool IsNodeReachable(WalkNode node)
    {
        return node != null &&
               distanceToDestination.TryGetValue(node, out float distance) &&
               !float.IsInfinity(distance);
    }

    private WalkNode GetClosestOpenNode(List<WalkNode> openNodes)
    {
        WalkNode closest = null;
        float bestDistance = float.PositiveInfinity;

        foreach (WalkNode node in openNodes)
        {
            if (node == null)
                continue;

            float distance = distanceToDestination.TryGetValue(node, out float value)
                ? value
                : float.PositiveInfinity;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                closest = node;
            }
        }

        return closest;
    }

    private WalkNode[] FindWalkNodes()
    {
        if (walkNodesRoot != null)
            return walkNodesRoot.GetComponentsInChildren<WalkNode>(includeInactiveObjects);

        return FindObjectsByType<WalkNode>(
            includeInactiveObjects ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
    }

    private ParkingSpot[] FindParkingSpots()
    {
        if (parkingSpotsRoot != null)
            return parkingSpotsRoot.GetComponentsInChildren<ParkingSpot>(includeInactiveObjects);

        return FindObjectsByType<ParkingSpot>(
            includeInactiveObjects ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;

        if (ab.sqrMagnitude < 0.000001f)
            return a;

        float t = Vector2.Dot(point - a, ab) / ab.sqrMagnitude;
        return a + Mathf.Clamp01(t) * ab;
    }

    private static void AddUnique(List<WalkNode> list, WalkNode node)
    {
        if (list != null && node != null && !list.Contains(node))
            list.Add(node);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawParkingToNetworkLinks || lastResults == null)
            return;

        foreach (SpotWalkingResult result in lastResults)
        {
            if (result == null || !result.reachable || result.parkingSpot == null)
                continue;

            Gizmos.DrawLine(
                result.parkingSpot.transform.position,
                result.networkEntryPoint
            );
        }
    }
}
