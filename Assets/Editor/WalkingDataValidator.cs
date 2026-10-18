#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class WalkingDataValidator
{
    [MenuItem("Tools/Parking/Validate Walking Data")]
    public static void ValidateWalkingData()
    {
        ParkingSpot[] allLoadedSpots =
            Resources.FindObjectsOfTypeAll<ParkingSpot>();

        List<ParkingSpot> spots = new List<ParkingSpot>();

        foreach (ParkingSpot spot in allLoadedSpots)
        {
            if (spot == null)
                continue;

            if (EditorUtility.IsPersistent(spot))
                continue;

            if (!spot.gameObject.scene.IsValid())
                continue;

            spots.Add(spot);
        }

        int zeroDistance = 0;
        int zeroTime = 0;
        int invalidDistance = 0;
        int invalidTime = 0;

        float minDistance = float.MaxValue;
        float maxDistance = float.MinValue;

        ParkingSpot closestSpot = null;
        ParkingSpot farthestSpot = null;

        Debug.Log(
            "========== WALKING DATA VALIDATION START ==========\n" +
            $"Parking spots found: {spots.Count}"
        );

        foreach (ParkingSpot spot in spots)
        {
            float distance = spot.walkingDistanceMeters;
            float time = spot.walkingTimeSeconds;

            // -----------------------------
            // Invalid numerical values
            // -----------------------------

            if (float.IsNaN(distance) || float.IsInfinity(distance))
            {
                invalidDistance++;

                Debug.LogError(
                    $"[WALKING ERROR] {spot.name}\n" +
                    $"Invalid Walking Distance: {distance}",
                    spot.gameObject
                );

                continue;
            }

            if (float.IsNaN(time) || float.IsInfinity(time))
            {
                invalidTime++;

                Debug.LogError(
                    $"[WALKING ERROR] {spot.name}\n" +
                    $"Invalid Walking Time: {time}",
                    spot.gameObject
                );
            }

            // -----------------------------
            // Missing values
            // -----------------------------

            if (distance <= 0f)
            {
                zeroDistance++;

                Debug.LogWarning(
                    $"[WALKING WARNING] {spot.name}\n" +
                    $"Walking Distance = {distance}",
                    spot.gameObject
                );
            }

            if (time <= 0f)
            {
                zeroTime++;

                Debug.LogWarning(
                    $"[WALKING WARNING] {spot.name}\n" +
                    $"Walking Time = {time}",
                    spot.gameObject
                );
            }

            // -----------------------------
            // Minimum / maximum
            // -----------------------------

            if (distance > 0f)
            {
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestSpot = spot;
                }

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthestSpot = spot;
                }
            }
        }

        Debug.Log(
            "========== WALKING DATA VALIDATION FINISHED ==========\n" +
            $"Parking spots checked: {spots.Count}\n" +
            $"Distance <= 0: {zeroDistance}\n" +
            $"Time <= 0: {zeroTime}\n" +
            $"Invalid distances: {invalidDistance}\n" +
            $"Invalid times: {invalidTime}\n\n" +
            $"Minimum walking distance: " +
            $"{(closestSpot != null ? minDistance.ToString("F2") + " m (" + closestSpot.name + ")" : "N/A")}\n" +
            $"Maximum walking distance: " +
            $"{(farthestSpot != null ? maxDistance.ToString("F2") + " m (" + farthestSpot.name + ")" : "N/A")}"
        );

        if (zeroDistance == 0 &&
            zeroTime == 0 &&
            invalidDistance == 0 &&
            invalidTime == 0)
        {
            Debug.Log(
                "✓ All parking spots contain walking-distance and walking-time data."
            );
        }
        else
        {
            Debug.LogWarning(
                "Some walking data needs inspection before WebGL."
            );
        }
    }
}

#endif