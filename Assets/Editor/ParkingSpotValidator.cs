#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ParkingSpotValidator
{
    [MenuItem("Tools/Parking/Validate All Parking Spots")]
    public static void ValidateAllParkingSpots()
    {
        ParkingSpot[] allLoadedSpots =
            Resources.FindObjectsOfTypeAll<ParkingSpot>();

        List<ParkingSpot> spots = new List<ParkingSpot>();

        // Only include actual scene objects.
        // Ignore prefab assets sitting in the Project window.
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

        if (spots.Count == 0)
        {
            Debug.LogWarning(
                "[Parking Validator] No ParkingSpot objects were found in the loaded scene(s)."
            );
            return;
        }

        int errorCount = 0;
        int warningCount = 0;

        Dictionary<string, List<ParkingSpot>> spotsById =
            new Dictionary<string, List<ParkingSpot>>();

        Dictionary<int, List<ParkingSpot>> spotsByNumber =
            new Dictionary<int, List<ParkingSpot>>();

        Debug.Log(
            $"========== PARKING SPOT VALIDATION START ==========\n" +
            $"Found {spots.Count} parking spots."
        );

        // ---------------------------------------------------------
        // FIRST PASS:
        // Individual parking-spot checks
        // ---------------------------------------------------------

        foreach (ParkingSpot spot in spots)
        {
            // =====================================================
            // 1. SPOT NUMBER
            // =====================================================

            if (spot.spotNumber <= 0)
            {
                LogError(
                    spot,
                    "Spot Number is 0 or unassigned. " +
                    "Run the ParkingSpotIdAssigner for this spot.",
                    ref errorCount
                );
            }
            else
            {
                if (!spotsByNumber.ContainsKey(spot.spotNumber))
                {
                    spotsByNumber[spot.spotNumber] =
                        new List<ParkingSpot>();
                }

                spotsByNumber[spot.spotNumber].Add(spot);
            }

            // =====================================================
            // 2. SPOT ID
            // =====================================================

            if (string.IsNullOrWhiteSpace(spot.spotId))
            {
                LogError(
                    spot,
                    "Spot ID is empty.",
                    ref errorCount
                );
            }
            else
            {
                if (spot.spotId.Contains("UNASSIGNED"))
                {
                    LogError(
                        spot,
                        $"Spot ID is still UNASSIGNED: {spot.spotId}",
                        ref errorCount
                    );
                }

                if (!spotsById.ContainsKey(spot.spotId))
                {
                    spotsById[spot.spotId] =
                        new List<ParkingSpot>();
                }

                spotsById[spot.spotId].Add(spot);
            }

            // =====================================================
            // 3. CHECK EXPECTED SPOT ID
            // =====================================================

            if (spot.spotNumber > 0)
            {
                string expectedId =
                    $"{spot.streetCode}_" +
                    $"{spot.GetStreetSideCode()}_" +
                    $"{spot.GetAlternativeId6()}_" +
                    $"{spot.spotNumber:000}";

                if (spot.spotId != expectedId)
                {
                    LogError(
                        spot,
                        $"Spot ID does not match its current metadata.\n" +
                        $"Current:  {spot.spotId}\n" +
                        $"Expected: {expectedId}",
                        ref errorCount
                    );
                }
            }

            // =====================================================
            // 4. GAMEOBJECT NAME
            // =====================================================

            if (!string.IsNullOrWhiteSpace(spot.spotId) &&
                spot.gameObject.name != spot.spotId)
            {
                LogWarning(
                    spot,
                    $"GameObject name does not match Spot ID.\n" +
                    $"Object name: {spot.gameObject.name}\n" +
                    $"Spot ID: {spot.spotId}",
                    ref warningCount
                );
            }

            // =====================================================
            // 5. STREET SIDE
            // =====================================================

            if (spot.streetSide == StreetSide.Unknown)
            {
                LogError(
                    spot,
                    "Street Side is Unknown.",
                    ref errorCount
                );
            }

            // =====================================================
            // 6. A1-A6 ALTERNATIVE
            // =====================================================

            string alternative = spot.GetAlternativeId6();

            if (alternative == "UNKNOWN")
            {
                LogError(
                    spot,
                    "Parking alternative classification is UNKNOWN.",
                    ref errorCount
                );
            }

            // =====================================================
            // 7. NO-PARKING ZONE LOGIC
            // =====================================================

            if (spot.parkingAreaKind ==
                    ParkingAreaKind.NoParkingZone &&
                spot.parkingType != ParkingType.Illegal)
            {
                LogError(
                    spot,
                    "NoParkingZone is not marked Illegal.",
                    ref errorCount
                );
            }

            // =====================================================
            // 8. LEGAL PARKING LOGIC
            // =====================================================

            if (spot.parkingType == ParkingType.Legal)
            {
                if (spot.illegalReason != IllegalReason.None)
                {
                    LogError(
                        spot,
                        "Legal parking spot has an Illegal Reason assigned.",
                        ref errorCount
                    );
                }
            }

            // =====================================================
            // 9. ILLEGAL PARKING LOGIC
            // =====================================================

            if (spot.parkingType == ParkingType.Illegal)
            {
                if (spot.legalParkingFeature !=
                    LegalParkingFeature.None)
                {
                    LogError(
                        spot,
                        "Illegal parking spot has a Legal Parking Feature assigned.",
                        ref errorCount
                    );
                }

                if (spot.illegalReason == IllegalReason.None)
                {
                    LogWarning(
                        spot,
                        "Illegal parking spot has Illegal Reason = None.",
                        ref warningCount
                    );
                }
            }

            // =====================================================
            // 10. ROAD NODE
            // =====================================================

            if (spot.nearestRoadNode == null)
            {
                LogWarning(
                    spot,
                    "Nearest Road Node is missing.",
                    ref warningCount
                );
            }

            // =====================================================
            // 11. PARKING SEGMENT
            // =====================================================

            bool hasStart =
                spot.parkingSegmentStartNode != null;

            bool hasEnd =
                spot.parkingSegmentEndNode != null;

            // One assigned and one missing = definitely suspicious.
            if (hasStart != hasEnd)
            {
                LogError(
                    spot,
                    "Parking Segment is only partially assigned. " +
                    "Start Node and End Node should either both be assigned " +
                    "or both be empty.",
                    ref errorCount
                );
            }

            // Both missing on a selectable spot = warning only.
            // Some of your intersection/end spots can work without them.
            if (!hasStart &&
                !hasEnd &&
                !spot.isOccupied)
            {
                LogWarning(
                    spot,
                    "Selectable parking spot has no Parking Segment " +
                    "Start/End nodes. Test this spot manually before changing it.",
                    ref warningCount
                );
            }

            // =====================================================
            // 12. WALKING DATA
            // =====================================================

            if (spot.walkingDistanceMeters <= 0f)
            {
                LogWarning(
                    spot,
                    $"Walking Distance is {spot.walkingDistanceMeters}. " +
                    "It may not have been calculated.",
                    ref warningCount
                );
            }

            if (spot.walkingTimeSeconds <= 0f)
            {
                LogWarning(
                    spot,
                    $"Walking Time is {spot.walkingTimeSeconds}. " +
                    "It may not have been calculated.",
                    ref warningCount
                );
            }

            // =====================================================
            // 13. COLLIDER
            // =====================================================

            Collider2D collider =
                spot.GetComponent<Collider2D>();

            if (collider == null)
            {
                LogError(
                    spot,
                    "Collider2D is missing.",
                    ref errorCount
                );
            }

            // =====================================================
            // 14. SPRITE RENDERER
            // =====================================================

            SpriteRenderer renderer =
                spot.GetComponent<SpriteRenderer>();

            if (renderer == null)
            {
                LogError(
                    spot,
                    "SpriteRenderer is missing.",
                    ref errorCount
                );
            }
        }

        // ---------------------------------------------------------
        // SECOND PASS:
        // DUPLICATE SPOT NUMBERS
        // ---------------------------------------------------------

        foreach (KeyValuePair<int, List<ParkingSpot>> pair
                 in spotsByNumber)
        {
            if (pair.Value.Count <= 1)
                continue;

            foreach (ParkingSpot spot in pair.Value)
            {
                LogError(
                    spot,
                    $"DUPLICATE Spot Number: {pair.Key}. " +
                    $"{pair.Value.Count} parking spots use this number.",
                    ref errorCount
                );
            }
        }

        // ---------------------------------------------------------
        // THIRD PASS:
        // DUPLICATE SPOT IDs
        // ---------------------------------------------------------

        foreach (KeyValuePair<string, List<ParkingSpot>> pair
                 in spotsById)
        {
            if (pair.Value.Count <= 1)
                continue;

            foreach (ParkingSpot spot in pair.Value)
            {
                LogError(
                    spot,
                    $"DUPLICATE Spot ID: {pair.Key}. " +
                    $"{pair.Value.Count} parking spots use this ID.",
                    ref errorCount
                );
            }
        }

        // ---------------------------------------------------------
        // SUMMARY
        // ---------------------------------------------------------

        Debug.Log(
            "========== PARKING SPOT VALIDATION FINISHED ==========\n" +
            $"Parking spots checked: {spots.Count}\n" +
            $"Errors: {errorCount}\n" +
            $"Warnings: {warningCount}\n\n" +
            (errorCount == 0
                ? "✓ No critical parking-data errors found."
                : "✗ Fix the ERROR messages before the WebGL pilot.")
        );
    }

    private static void LogError(
        ParkingSpot spot,
        string message,
        ref int count)
    {
        count++;

        Debug.LogError(
            $"[PARKING ERROR] {spot.gameObject.name}\n" +
            $"Spot ID: {spot.spotId}\n" +
            message,
            spot.gameObject
        );
    }

    private static void LogWarning(
        ParkingSpot spot,
        string message,
        ref int count)
    {
        count++;

        Debug.LogWarning(
            $"[PARKING WARNING] {spot.gameObject.name}\n" +
            $"Spot ID: {spot.spotId}\n" +
            message,
            spot.gameObject
        );
    }
}

#endif