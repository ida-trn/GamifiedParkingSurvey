#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ParkingSpotNodeScanner
{
    [MenuItem("Tools/Parking/Scan Parking Start-End Nodes")]
    public static void ScanParkingNodes()
    {
        ParkingSpot[] allSpots = Object.FindObjectsByType<ParkingSpot>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        int total = 0;
        int occupiedIgnored = 0;
        int valid = 0;
        int missingStart = 0;
        int missingEnd = 0;
        int missingBoth = 0;

        List<GameObject> problematicObjects = new List<GameObject>();

        Debug.Log("========== PARKING NODE SCAN START ==========");

        foreach (ParkingSpot spot in allSpots)
        {
            if (spot == null)
                continue;

            total++;

            // Ignore spots intentionally marked occupied
            if (spot.isOccupied)
            {
                occupiedIgnored++;
                continue;
            }

            bool hasStart = spot.parkingSegmentStartNode != null;
            bool hasEnd = spot.parkingSegmentEndNode != null;

            if (hasStart && hasEnd)
            {
                valid++;
                continue;
            }

            problematicObjects.Add(spot.gameObject);

            string problem;

            if (!hasStart && !hasEnd)
            {
                problem = "MISSING START + END";
                missingBoth++;
            }
            else if (!hasStart)
            {
                problem = "MISSING START";
                missingStart++;
            }
            else
            {
                problem = "MISSING END";
                missingEnd++;
            }

            Debug.LogError(
                $"[Parking Node Scan] {problem} | " +
                $"Spot: {spot.name} | " +
                $"Spot ID: {spot.spotId} | " +
                $"Street: {spot.streetName} | " +
                $"Side: {spot.streetSide} | " +
                $"Hierarchy: {GetHierarchyPath(spot.transform)}",
                spot.gameObject
            );
        }

        Debug.Log(
            "========== PARKING NODE SCAN RESULT ==========\n" +
            $"Total Parking Spots: {total}\n" +
            $"Occupied / Ignored: {occupiedIgnored}\n" +
            $"Valid Selectable Spots: {valid}\n" +
            $"Missing Start Only: {missingStart}\n" +
            $"Missing End Only: {missingEnd}\n" +
            $"Missing Both: {missingBoth}\n" +
            $"TOTAL PROBLEMATIC: {problematicObjects.Count}\n" +
            "=============================================="
        );

        // Automatically select every problematic parking spot
        Selection.objects = problematicObjects.ToArray();

        if (problematicObjects.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Parking Node Scan",
                "All non-occupied parking spots have both a Start Node and End Node.",
                "OK"
            );
        }
        else
        {
            EditorUtility.DisplayDialog(
                "Parking Node Scan",
                $"{problematicObjects.Count} non-occupied parking spot(s) are missing nodes.\n\n" +
                "They have been selected in the Hierarchy.\n" +
                "Check the Console for details.",
                "OK"
            );
        }
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;

        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}

#endif