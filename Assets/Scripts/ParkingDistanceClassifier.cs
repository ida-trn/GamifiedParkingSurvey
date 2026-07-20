using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

[ExecuteAlways]
public class ParkingDistanceClassifier : MonoBehaviour
{
    public enum ClassificationRule
    {
        ClosestPercentageIsInner,
        FixedDistanceThreshold
    }

    public enum RoadSearchMode
    {
        DirectedRoadConnections,
        UndirectedApproximation
    }

    public enum NumberingOrder
    {
        SceneHierarchyOrder,
        DistanceFromGate
    }

    public enum AutoRegion
    {
        Inner,
        Outer,
        Unreachable
    }

    [Header("Main References")]
    [Tooltip("The RoadNode closest to the destination / entrance gate.")]
    public RoadNode destinationGateNode;

    [Tooltip("Optional. If assigned, only ParkingSpot components under this root are used.")]
    public Transform parkingSpotsRoot;

    [Tooltip("Optional. If assigned, only RoadNode components under this root are used.")]
    public Transform roadNodesRoot;

    [Header("Auto Find")]
    public bool autoFindParkingSpots = true;
    public bool autoFindRoadNodes = true;

    public List<ParkingSpot> parkingSpots = new List<ParkingSpot>();
    public List<RoadNode> roadNodes = new List<RoadNode>();

    [Header("Road Graph Settings")]
    [Tooltip("If your RoadNode connection list has a specific variable name, write it here. Common examples: connectedNodes, nextNodes, neighbors.")]
    public string roadNodeConnectionFieldName = "connectedNodes";

    [Tooltip("Use Directed if your car movement follows one-way node connections. Use Undirected if you only want closeness by road geometry.")]
    public RoadSearchMode roadSearchMode = RoadSearchMode.DirectedRoadConnections;

    [Tooltip("If a directed path cannot be found, try the undirected version.")]
    public bool fallbackToUndirectedIfUnreachable = true;

    [Tooltip("Use this to convert Unity units to meters. Keep 1 for now if you only use relative inner/outer ranking.")]
    public float metersPerUnityUnit = 1f;

    [Header("Inner / Outer Classification")]
    public ClassificationRule classificationRule = ClassificationRule.ClosestPercentageIsInner;

    [Range(0.1f, 0.9f)]
    [Tooltip("Example: 0.5 means closest 50% of spots become Inner.")]
    public float innerPercentage = 0.5f;

    [Tooltip("Used only if Classification Rule = Fixed Distance Threshold.")]
    public float innerDistanceThresholdMeters = 150f;

    [Tooltip("Recommended ON. It ranks legal and illegal spots separately, so you can still get inner legal, inner illegal, outer legal, outer illegal.")]
    public bool classifySeparatelyByParkingType = true;

    [Tooltip("If no path is found, classify the spot as Outer instead of leaving it unchanged.")]
    public bool unreachableSpotsBecomeOuter = true;

    [Header("Write Back To ParkingSpot")]
    public bool writeRegionToParkingSpot = true;
    public bool writeUniqueSpotNumbers = true;
    public bool renameGameObjectsToSpotId = true;

    [Header("Unique Spot Numbering")]
    public NumberingOrder numberingOrder = NumberingOrder.SceneHierarchyOrder;
    public int firstSpotNumber = 1;

    [Tooltip("This is the variable name in ParkingSpot.cs. In your inspector it appears as Spot Number.")]
    public string parkingSpotNumberFieldName = "spotNumber";

    [Tooltip("This is the variable name in ParkingSpot.cs. In your inspector it appears as Region.")]
    public string parkingRegionFieldName = "region";

    [Tooltip("This is the variable name in ParkingSpot.cs. In your inspector it appears as Spot Id.")]
    public string parkingSpotIdFieldName = "spotId";

    [Header("Debug")]
    public bool logResults = true;

    [Serializable]
    public class ClassificationResult
    {
        public ParkingSpot parkingSpot;
        public RoadNode nearestRoadNode;
        public float roadDistanceMeters;
        public AutoRegion autoRegion;
        public string note;
    }

    public List<ClassificationResult> lastResults = new List<ClassificationResult>();

    [ContextMenu("1. Classify + Renumber Parking Spots")]
    public void ClassifyAndRenumberParkingSpots()
    {
        RefreshLists();

        if (destinationGateNode == null)
        {
            Debug.LogError("ParkingDistanceClassifier: Destination Gate Node is missing.");
            return;
        }

        if (parkingSpots.Count == 0)
        {
            Debug.LogError("ParkingDistanceClassifier: No ParkingSpot objects found.");
            return;
        }

        if (roadNodes.Count == 0)
        {
            Debug.LogError("ParkingDistanceClassifier: No RoadNode objects found.");
            return;
        }

        CalculateDistances();
        AssignRegions();

        if (writeRegionToParkingSpot)
            WriteRegionsToParkingSpots();

        if (writeUniqueSpotNumbers)
            AssignUniqueSpotNumbers();

        if (renameGameObjectsToSpotId)
            RenameParkingSpotObjects();

        MarkDirty(this);

        if (logResults)
            PrintResults();
    }

    [ContextMenu("2. Renumber Only")]
    public void RenumberOnly()
    {
        RefreshLists();

        if (parkingSpots.Count == 0)
        {
            Debug.LogError("ParkingDistanceClassifier: No ParkingSpot objects found.");
            return;
        }

        if (lastResults == null || lastResults.Count == 0)
            CalculateDistances();

        AssignUniqueSpotNumbers();

        if (renameGameObjectsToSpotId)
            RenameParkingSpotObjects();

        MarkDirty(this);

        Debug.Log("ParkingDistanceClassifier: Spot numbers updated.");
    }

    [ContextMenu("3. Calculate Distances Only")]
    public void CalculateDistancesOnly()
    {
        RefreshLists();
        CalculateDistances();
        MarkDirty(this);
        PrintResults();
    }

    private void RefreshLists()
    {
        if (autoFindParkingSpots)
        {
            if (parkingSpotsRoot != null)
                parkingSpots = parkingSpotsRoot.GetComponentsInChildren<ParkingSpot>(true).ToList();
            else
                parkingSpots = FindObjectsByType<ParkingSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();

            parkingSpots = parkingSpots
                .Where(s => s != null)
                .OrderBy(s => GetHierarchyPath(s.transform))
                .ToList();
        }

        if (autoFindRoadNodes)
        {
            if (roadNodesRoot != null)
                roadNodes = roadNodesRoot.GetComponentsInChildren<RoadNode>(true).ToList();
            else
                roadNodes = FindObjectsByType<RoadNode>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();

            roadNodes = roadNodes
                .Where(n => n != null)
                .OrderBy(n => GetHierarchyPath(n.transform))
                .ToList();
        }
    }

    private void CalculateDistances()
    {
        lastResults.Clear();

        Dictionary<RoadNode, List<RoadNode>> reverseMap = BuildReverseConnectionMap();

        foreach (ParkingSpot spot in parkingSpots)
        {
            if (spot == null) continue;

            RoadNode nearest = FindNearestRoadNode(spot.transform.position);

            float distance = float.PositiveInfinity;
            string note = "";

            if (nearest != null)
            {
                distance = ShortestPathDistance(nearest, destinationGateNode, roadSearchMode, reverseMap);

                if (float.IsInfinity(distance) && fallbackToUndirectedIfUnreachable)
                {
                    float fallbackDistance = ShortestPathDistance(nearest, destinationGateNode, RoadSearchMode.UndirectedApproximation, reverseMap);

                    if (!float.IsInfinity(fallbackDistance))
                    {
                        distance = fallbackDistance;
                        note = "Used undirected fallback.";
                    }
                    else
                    {
                        note = "No path found.";
                    }
                }
            }
            else
            {
                note = "No nearest RoadNode found.";
            }

            lastResults.Add(new ClassificationResult
            {
                parkingSpot = spot,
                nearestRoadNode = nearest,
                roadDistanceMeters = distance,
                autoRegion = AutoRegion.Unreachable,
                note = note
            });
        }
    }

    private void AssignRegions()
    {
        if (lastResults.Count == 0)
            return;

        if (classificationRule == ClassificationRule.FixedDistanceThreshold)
        {
            foreach (ClassificationResult r in lastResults)
            {
                if (float.IsInfinity(r.roadDistanceMeters))
                {
                    r.autoRegion = unreachableSpotsBecomeOuter ? AutoRegion.Outer : AutoRegion.Unreachable;
                }
                else
                {
                    r.autoRegion = r.roadDistanceMeters <= innerDistanceThresholdMeters ? AutoRegion.Inner : AutoRegion.Outer;
                }
            }

            return;
        }

        IEnumerable<IGrouping<string, ClassificationResult>> groups;

        if (classifySeparatelyByParkingType)
        {
            groups = lastResults.GroupBy(r => GetParkingTypeLabel(r.parkingSpot));
        }
        else
        {
            groups = lastResults.GroupBy(r => "ALL");
        }

        foreach (IGrouping<string, ClassificationResult> group in groups)
        {
            List<ClassificationResult> reachable = group
                .Where(r => !float.IsInfinity(r.roadDistanceMeters))
                .OrderBy(r => r.roadDistanceMeters)
                .ToList();

            int innerCount = Mathf.CeilToInt(reachable.Count * innerPercentage);
            innerCount = Mathf.Clamp(innerCount, 1, Mathf.Max(1, reachable.Count));

            for (int i = 0; i < reachable.Count; i++)
            {
                reachable[i].autoRegion = i < innerCount ? AutoRegion.Inner : AutoRegion.Outer;
            }

            foreach (ClassificationResult unreachable in group.Where(r => float.IsInfinity(r.roadDistanceMeters)))
            {
                unreachable.autoRegion = unreachableSpotsBecomeOuter ? AutoRegion.Outer : AutoRegion.Unreachable;
            }
        }
    }

    private void WriteRegionsToParkingSpots()
    {
        foreach (ClassificationResult r in lastResults)
        {
            if (r.parkingSpot == null) continue;
            if (r.autoRegion == AutoRegion.Unreachable) continue;

            RecordUndo(r.parkingSpot, "Auto Parking Region");

            string regionName = r.autoRegion == AutoRegion.Inner ? "Inner" : "Outer";

            bool success = TrySetMemberValue(
                r.parkingSpot,
                new string[]
                {
                    parkingRegionFieldName,
                    "region",
                    "Region",
                    "parkingRegion",
                    "choiceRegion"
                },
                regionName
            );

            if (!success)
            {
                Debug.LogWarning("Could not write Region to " + r.parkingSpot.name + ". Check the region field name in ParkingSpot.cs.");
            }

            InvokeRefreshMethods(r.parkingSpot);
            MarkDirty(r.parkingSpot);
        }
    }

    private void AssignUniqueSpotNumbers()
    {
        List<ParkingSpot> orderedSpots;

        if (numberingOrder == NumberingOrder.DistanceFromGate && lastResults.Count > 0)
        {
            orderedSpots = lastResults
                .OrderBy(r => float.IsInfinity(r.roadDistanceMeters) ? float.MaxValue : r.roadDistanceMeters)
                .ThenBy(r => GetHierarchyPath(r.parkingSpot.transform))
                .Select(r => r.parkingSpot)
                .Where(s => s != null)
                .Distinct()
                .ToList();
        }
        else
        {
            orderedSpots = parkingSpots
                .Where(s => s != null)
                .OrderBy(s => GetHierarchyPath(s.transform))
                .ToList();
        }

        int number = firstSpotNumber;

        foreach (ParkingSpot spot in orderedSpots)
        {
            RecordUndo(spot, "Assign Unique Parking Spot Number");

            bool success = TrySetMemberValue(
                spot,
                new string[]
                {
                    parkingSpotNumberFieldName,
                    "spotNumber",
                    "SpotNumber",
                    "spotNo",
                    "number",
                    "parkingSpotNumber"
                },
                number
            );

            if (!success)
            {
                Debug.LogWarning("Could not write Spot Number to " + spot.name + ". Check the spotNumber field name in ParkingSpot.cs.");
            }

            InvokeRefreshMethods(spot);
            MarkDirty(spot);

            number++;
        }
    }

    private void RenameParkingSpotObjects()
    {
        foreach (ParkingSpot spot in parkingSpots)
        {
            if (spot == null) continue;

            string id = ReadMemberAsString(
                spot,
                new string[]
                {
                    parkingSpotIdFieldName,
                    "spotId",
                    "SpotId",
                    "id",
                    "parkingSpotId"
                }
            );

            if (string.IsNullOrWhiteSpace(id))
                continue;

            RecordUndo(spot.gameObject, "Rename Parking Spot GameObject");
            spot.gameObject.name = id;
            MarkDirty(spot.gameObject);
        }
    }

    private RoadNode FindNearestRoadNode(Vector3 position)
    {
        RoadNode nearest = null;
        float bestDistance = float.PositiveInfinity;

        foreach (RoadNode node in roadNodes)
        {
            if (node == null) continue;

            float sqrDistance = (node.transform.position - position).sqrMagnitude;

            if (sqrDistance < bestDistance)
            {
                bestDistance = sqrDistance;
                nearest = node;
            }
        }

        return nearest;
    }

    private float ShortestPathDistance(
        RoadNode start,
        RoadNode target,
        RoadSearchMode mode,
        Dictionary<RoadNode, List<RoadNode>> reverseMap)
    {
        if (start == null || target == null)
            return float.PositiveInfinity;

        if (start == target)
            return 0f;

        Dictionary<RoadNode, float> distance = new Dictionary<RoadNode, float>();
        HashSet<RoadNode> unvisited = new HashSet<RoadNode>();

        foreach (RoadNode node in roadNodes)
        {
            if (node == null) continue;
            distance[node] = float.PositiveInfinity;
            unvisited.Add(node);
        }

        if (!distance.ContainsKey(start))
            return float.PositiveInfinity;

        distance[start] = 0f;

        while (unvisited.Count > 0)
        {
            RoadNode current = null;
            float best = float.PositiveInfinity;

            foreach (RoadNode node in unvisited)
            {
                float d = distance.ContainsKey(node) ? distance[node] : float.PositiveInfinity;

                if (d < best)
                {
                    best = d;
                    current = node;
                }
            }

            if (current == null)
                break;

            if (current == target)
                return distance[current];

            unvisited.Remove(current);

            foreach (RoadNode neighbor in GetNeighbors(current, mode, reverseMap))
            {
                if (neighbor == null) continue;
                if (!distance.ContainsKey(neighbor)) continue;
                if (!unvisited.Contains(neighbor)) continue;

                float edge = Vector3.Distance(current.transform.position, neighbor.transform.position) * metersPerUnityUnit;
                float newDistance = distance[current] + edge;

                if (newDistance < distance[neighbor])
                    distance[neighbor] = newDistance;
            }
        }

        return float.PositiveInfinity;
    }

    private IEnumerable<RoadNode> GetNeighbors(
        RoadNode node,
        RoadSearchMode mode,
        Dictionary<RoadNode, List<RoadNode>> reverseMap)
    {
        HashSet<RoadNode> result = new HashSet<RoadNode>();

        foreach (RoadNode outgoing in GetOutgoingRoadNodes(node))
        {
            if (outgoing != null)
                result.Add(outgoing);
        }

        if (mode == RoadSearchMode.UndirectedApproximation)
        {
            if (reverseMap != null && reverseMap.TryGetValue(node, out List<RoadNode> incoming))
            {
                foreach (RoadNode n in incoming)
                {
                    if (n != null)
                        result.Add(n);
                }
            }
        }

        return result;
    }

    private Dictionary<RoadNode, List<RoadNode>> BuildReverseConnectionMap()
    {
        Dictionary<RoadNode, List<RoadNode>> reverse = new Dictionary<RoadNode, List<RoadNode>>();

        foreach (RoadNode node in roadNodes)
        {
            if (node == null) continue;

            if (!reverse.ContainsKey(node))
                reverse[node] = new List<RoadNode>();

            foreach (RoadNode outgoing in GetOutgoingRoadNodes(node))
            {
                if (outgoing == null) continue;

                if (!reverse.ContainsKey(outgoing))
                    reverse[outgoing] = new List<RoadNode>();

                if (!reverse[outgoing].Contains(node))
                    reverse[outgoing].Add(node);
            }
        }

        return reverse;
    }

    private IEnumerable<RoadNode> GetOutgoingRoadNodes(RoadNode node)
    {
        if (node == null)
            yield break;

        List<string> candidateNames = new List<string>();

        if (!string.IsNullOrWhiteSpace(roadNodeConnectionFieldName))
            candidateNames.Add(roadNodeConnectionFieldName);

        candidateNames.AddRange(new string[]
        {
            "connectedNodes",
            "nextNodes",
            "neighbors",
            "neighbours",
            "connections",
            "allowedNextNodes",
            "possibleNextNodes",
            "outgoingNodes",
            "linkedNodes",
            "roadConnections"
        });

        Type type = node.GetType();
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string fieldName in candidateNames.Distinct())
        {
            FieldInfo field = type.GetField(fieldName, flags);

            if (field != null)
            {
                object value = field.GetValue(node);

                foreach (RoadNode roadNode in ExtractRoadNodes(value))
                    yield return roadNode;
            }

            PropertyInfo property = type.GetProperty(fieldName, flags);

            if (property != null && property.CanRead)
            {
                object value = property.GetValue(node, null);

                foreach (RoadNode roadNode in ExtractRoadNodes(value))
                    yield return roadNode;
            }
        }
    }

    private IEnumerable<RoadNode> ExtractRoadNodes(object value)
    {
        if (value == null)
            yield break;

        if (value is RoadNode singleNode)
        {
            yield return singleNode;
            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (object item in enumerable)
            {
                if (item is RoadNode roadNode)
                    yield return roadNode;
            }
        }
    }

    private bool TrySetMemberValue(object target, string[] names, object value)
    {
        if (target == null)
            return false;

        Type type = target.GetType();
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            FieldInfo field = type.GetField(name, flags);

            if (field != null)
            {
                object converted = ConvertValue(value, field.FieldType);

                if (converted != null || !field.FieldType.IsValueType)
                {
                    field.SetValue(target, converted);
                    return true;
                }
            }

            PropertyInfo property = type.GetProperty(name, flags);

            if (property != null && property.CanWrite)
            {
                object converted = ConvertValue(value, property.PropertyType);

                if (converted != null || !property.PropertyType.IsValueType)
                {
                    property.SetValue(target, converted, null);
                    return true;
                }
            }
        }

        return false;
    }

    private string ReadMemberAsString(object target, string[] names)
    {
        if (target == null)
            return "";

        Type type = target.GetType();
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (string name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            FieldInfo field = type.GetField(name, flags);

            if (field != null)
            {
                object value = field.GetValue(target);
                return value == null ? "" : value.ToString();
            }

            PropertyInfo property = type.GetProperty(name, flags);

            if (property != null && property.CanRead)
            {
                object value = property.GetValue(target, null);
                return value == null ? "" : value.ToString();
            }
        }

        return "";
    }

    private object ConvertValue(object value, Type targetType)
    {
        if (targetType == typeof(string))
            return value == null ? "" : value.ToString();

        if (targetType == typeof(int))
        {
            if (value is int i) return i;
            if (int.TryParse(value.ToString(), out int parsed)) return parsed;
        }

        if (targetType == typeof(float))
        {
            if (value is float f) return f;
            if (float.TryParse(value.ToString(), out float parsed)) return parsed;
        }

        if (targetType == typeof(double))
        {
            if (value is double d) return d;
            if (double.TryParse(value.ToString(), out double parsed)) return parsed;
        }

        if (targetType == typeof(bool))
        {
            if (value is bool b) return b;
            if (bool.TryParse(value.ToString(), out bool parsed)) return parsed;
        }

        if (targetType.IsEnum)
        {
            string text = value.ToString();

            foreach (string enumName in Enum.GetNames(targetType))
            {
                if (string.Equals(enumName, text, StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(targetType, enumName);
            }

            string cleanedText = text.Replace(" ", "").Replace("_", "").Replace("-", "");

            foreach (string enumName in Enum.GetNames(targetType))
            {
                string cleanedEnum = enumName.Replace(" ", "").Replace("_", "").Replace("-", "");

                if (string.Equals(cleanedEnum, cleanedText, StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(targetType, enumName);
            }
        }

        if (targetType.IsAssignableFrom(value.GetType()))
            return value;

        return null;
    }

    private void InvokeRefreshMethods(ParkingSpot spot)
    {
        if (spot == null)
            return;

        Type type = spot.GetType();
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        string[] methodNames =
        {
            "RefreshSpotId",
            "UpdateSpotId",
            "UpdateSpotIdInInspector",
            "AutoUpdateSpotId",
            "SyncSpotIdWithStreetData",
            "BuildSpotId",
            "OnValidate"
        };

        foreach (string methodName in methodNames)
        {
            MethodInfo method = type.GetMethod(methodName, flags);

            if (method == null)
                continue;

            if (method.GetParameters().Length != 0)
                continue;

            try
            {
                method.Invoke(spot, null);
            }
            catch
            {
                // Ignore methods that are not safe to call from here.
            }
        }
    }

    private string GetParkingTypeLabel(ParkingSpot spot)
    {
        string parkingType = ReadMemberAsString(
            spot,
            new string[]
            {
                "parkingType",
                "ParkingType",
                "type",
                "legalStatus"
            }
        );

        if (string.IsNullOrWhiteSpace(parkingType))
            return "Unknown";

        if (parkingType.ToLower().Contains("illegal"))
            return "Illegal";

        if (parkingType.ToLower().Contains("legal"))
            return "Legal";

        return parkingType;
    }

    private string GetHierarchyPath(Transform t)
    {
        if (t == null)
            return "";

        string path = t.name;

        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }

    private void PrintResults()
    {
        foreach (ClassificationResult r in lastResults)
        {
            if (r.parkingSpot == null) continue;

            string distanceText = float.IsInfinity(r.roadDistanceMeters)
                ? "Unreachable"
                : r.roadDistanceMeters.ToString("0.00") + " m";

            Debug.Log(
                r.parkingSpot.name +
                " | nearest node: " + (r.nearestRoadNode != null ? r.nearestRoadNode.name : "None") +
                " | road distance to gate: " + distanceText +
                " | auto region: " + r.autoRegion +
                " | " + r.note
            );
        }
    }

    private void RecordUndo(UnityEngine.Object obj, string actionName)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && obj != null)
            UnityEditor.Undo.RecordObject(obj, actionName);
#endif
    }

    private void MarkDirty(UnityEngine.Object obj)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && obj != null)
            UnityEditor.EditorUtility.SetDirty(obj);
#endif
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(ParkingDistanceClassifier))]
public class ParkingDistanceClassifierEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ParkingDistanceClassifier classifier = (ParkingDistanceClassifier)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Classify + Renumber Parking Spots"))
        {
            classifier.ClassifyAndRenumberParkingSpots();
        }

        if (GUILayout.Button("Renumber Only"))
        {
            classifier.RenumberOnly();
        }

        if (GUILayout.Button("Calculate Distances Only"))
        {
            classifier.CalculateDistancesOnly();
        }
    }
}
#endif