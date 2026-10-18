using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates scenario-controlled parking occupancy.
///
/// STUDY LOGIC:
/// 1. ScenarioManager supplies the scenario's exact overall legal/illegal
///    occupancy rates and its ParkingScarcityLevel.
/// 2. Legal occupancy is spatially calibrated by fixing the number of FREE
///    legal spaces within 300 m of Gate 2 using two experimental levels:
///       High    =  1 free A1 +  3 free A3
///       Extreme =  0 free A1 +  1 free A3
/// 3. The remaining occupied legal spaces are selected from A5 so the exact
///    overall legal occupancy target (for example 70% or 90%) is preserved.
/// 4. A2 no-fine illegal spaces (IntersectionCorner/PedestrianCrossing)
///    use a fixed calibration: exactly 10 are occupied and 4 are free,
///    randomly selected each scenario. This is NOT an experimental factor.
/// 5. The remaining illegal occupied spaces are selected with the existing
///    weighted exact-total method, preserving the scenario's exact overall
///    illegal occupancy rate.
/// 6. A5/A6 spaces inside a ParkingDemandZone receive a fixed local-demand
///    weight boost. This changes only WHERE occupancy is concentrated; it does
///    not change the exact scenario-wide legal/illegal occupied totals.
/// 7. Conventional and Assisted pairing is handled by ScenarioManager, which
///    snapshots and restores the exact same physical occupied/free spots.
///
/// A1-A6 are assigned by ParkingSpot.GetAlternativeId6():
/// A1 = Inner Legal      (<200 m)
/// A2 = Inner Illegal
/// A3 = Outer Legal      (200-300 m)
/// A4 = Outer Illegal
/// A5 = Far Legal        (>300 m)
/// A6 = Far Illegal
/// </summary>
public class ParkingOccupancyManager : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // LOCKED STUDY SCARCITY VALUES
    // These are enforced in both the Unity Editor and at runtime so old
    // serialized Inspector values can never silently change the experiment.
    // ---------------------------------------------------------------------
    private const int StudyHighA1Free = 1;
    private const int StudyHighA3Free = 3;
    private const int StudyExtremeA1Free = 0;
    private const int StudyExtremeA3Free = 1;

    // Fixed calibration for the 14 A2 no-fine illegal spaces.
    // This is deliberately NOT exposed as a scenario parameter.
    private const int StudyA2NoFineOccupied = 10;

    [Header("Scenario Occupancy Targets (set by ScenarioManager)")]
    [Tooltip("Overall proportion of LEGAL spaces occupied in the current scenario.")]
    [Range(0f, 1f)]
    public float legalOccupancyRate = 0.70f;

    [Tooltip("Overall proportion of ILLEGAL spaces occupied in the current scenario.")]
    [Range(0f, 1f)]
    public float illegalOccupancyRate = 0.10f;

    [Tooltip("Spatial scarcity treatment for legal parking near Gate 2.")]
    public ParkingScarcityLevel scarcityLevel = ParkingScarcityLevel.High;

    [Header("Use Exact-Total Occupancy")]
    [Tooltip("Keep ON for the study. The scenario controls the exact total number of occupied legal/illegal spaces.")]
    public bool useWeightedExactTotals = true;

    [Header("LEGAL Nearby-Free Targets")]
    [Tooltip("High scarcity: number of FREE A1 legal spaces (<200 m).")]
    [Min(0)] public int highA1Free = StudyHighA1Free;

    [Tooltip("High scarcity: number of FREE A3 legal spaces (200-300 m).")]
    [Min(0)] public int highA3Free = StudyHighA3Free;

    [Tooltip("Extreme scarcity: number of FREE A1 legal spaces (<200 m).")]
    [Min(0)] public int extremeA1Free = StudyExtremeA1Free;

    [Tooltip("Extreme scarcity: number of FREE A3 legal spaces (200-300 m).")]
    [Min(0)] public int extremeA3Free = StudyExtremeA3Free;

    [Header("Legacy Logger Compatibility")]
    [Tooltip("No longer used to generate LEGAL occupancy. Kept only so older ParkingLogger code compiles. A negative value is exported as blank by the current logger.")]
    [HideInInspector] public float a1InnerLegalWeight = -1f;

    [Tooltip("No longer used to generate LEGAL occupancy. Kept only so older ParkingLogger code compiles.")]
    [HideInInspector] public float a3OuterLegalWeight = -1f;

    [Tooltip("No longer used to generate LEGAL occupancy. Kept only so older ParkingLogger code compiles.")]
    [HideInInspector] public float a5FarLegalWeight = -1f;

    [Header("ILLEGAL Spatial Weights")]
    [Min(0.01f)] public float a2InnerIllegalWeight = 20.0f;
    [Min(0.01f)] public float a4OuterIllegalWeight = 4.0f;
    [Min(0.01f)] public float a6FarIllegalWeight = 1.0f;

    [Header("Diagnostics")]
    public bool logOccupancyResults = true;
    public bool verifyExactTotals = true;

    private ParkingSpot[] spots;
    private ParkingDemandZone[] demandZones;

    private sealed class WeightedCandidate
    {
        public ParkingSpot spot;
        public float priority;
    }

    private void Awake()
    {
        EnforceStudyScarcityValues();
        CacheParkingSpots();
        CacheDemandZones();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Also force the values while editing the scene/prefab so the Inspector
        // immediately reflects the study configuration.
        EnforceStudyScarcityValues();
    }
#endif

    private void EnforceStudyScarcityValues()
    {
        bool changed =
            highA1Free != StudyHighA1Free ||
            highA3Free != StudyHighA3Free ||
            extremeA1Free != StudyExtremeA1Free ||
            extremeA3Free != StudyExtremeA3Free;

        highA1Free = StudyHighA1Free;
        highA3Free = StudyHighA3Free;
        extremeA1Free = StudyExtremeA1Free;
        extremeA3Free = StudyExtremeA3Free;

        // Exact-total occupancy is part of the study design and should remain ON.
        useWeightedExactTotals = true;

        if (changed && Application.isPlaying)
        {
            Debug.LogWarning(
                "[ParkingOccupancyManager] Old serialized scarcity values were overridden. " +
                "Study values enforced: High A1/A3 = 1/3, Extreme A1/A3 = 0/1."
            );
        }
    }

    private void CacheParkingSpots()
    {
        spots = FindObjectsByType<ParkingSpot>(FindObjectsSortMode.None);
    }

    private void CacheDemandZones()
    {
        demandZones =
            FindObjectsByType<ParkingDemandZone>(
                FindObjectsSortMode.None
            );
    }

    public void ApplyOccupancy()
    {
        if (spots == null || spots.Length == 0)
            CacheParkingSpots();

        if (spots == null || spots.Length == 0)
        {
            Debug.LogError("[ParkingOccupancyManager] No ParkingSpot objects were found.");
            return;
        }

        if (demandZones == null)
            CacheDemandZones();

        if (!useWeightedExactTotals)
        {
            ApplyLegacyIndependentOccupancy();
            return;
        }

        List<ParkingSpot> legalSpots = new List<ParkingSpot>();
        List<ParkingSpot> illegalSpots = new List<ParkingSpot>();

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            // Static physical obstructions are not part of parking supply or
            // scenario occupancy. They remain visible in their normal color.
            if (spot.IsPhysicallyBlockedParking())
            {
                spot.SetOccupied(false);
                continue;
            }

            spot.SetOccupied(false);

            if (spot.parkingType == ParkingType.Legal)
                legalSpots.Add(spot);
            else
                illegalSpots.Add(spot);
        }

        int targetLegalOccupied = Mathf.Clamp(
            Mathf.RoundToInt(legalSpots.Count * legalOccupancyRate),
            0,
            legalSpots.Count
        );

        int targetIllegalOccupied = Mathf.Clamp(
            Mathf.RoundToInt(illegalSpots.Count * illegalOccupancyRate),
            0,
            illegalSpots.Count
        );

        ApplyLegalScarcityOccupancy(legalSpots, targetLegalOccupied);
        ApplyIllegalOccupancyWithA2NoFineCalibration(
            illegalSpots,
            targetIllegalOccupied
        );

        if (logOccupancyResults || verifyExactTotals)
        {
            LogAndVerifyResults(
                legalSpots.Count,
                illegalSpots.Count,
                targetLegalOccupied,
                targetIllegalOccupied
            );
        }
    }

    private void ApplyLegalScarcityOccupancy(
        List<ParkingSpot> legalSpots,
        int targetLegalOccupied)
    {
        List<ParkingSpot> a1Spots = new List<ParkingSpot>();
        List<ParkingSpot> a3Spots = new List<ParkingSpot>();
        List<ParkingSpot> a5Spots = new List<ParkingSpot>();
        List<ParkingSpot> unknownLegalSpots = new List<ParkingSpot>();

        foreach (ParkingSpot spot in legalSpots)
        {
            if (spot == null)
                continue;

            switch (spot.GetAlternativeId6())
            {
                case "A1": a1Spots.Add(spot); break;
                case "A3": a3Spots.Add(spot); break;
                case "A5": a5Spots.Add(spot); break;
                default: unknownLegalSpots.Add(spot); break;
            }
        }

        GetNearbyFreeTargets(out int desiredA1Free, out int desiredA3Free);

        desiredA1Free = Mathf.Clamp(desiredA1Free, 0, a1Spots.Count);
        desiredA3Free = Mathf.Clamp(desiredA3Free, 0, a3Spots.Count);

        int targetA1Occupied = a1Spots.Count - desiredA1Free;
        int targetA3Occupied = a3Spots.Count - desiredA3Free;
        int nearbyTargetOccupied = targetA1Occupied + targetA3Occupied;
        int nonNearbyCapacity = a5Spots.Count + unknownLegalSpots.Count;

        if (nearbyTargetOccupied > targetLegalOccupied)
        {
            Debug.LogError(
                "[ParkingOccupancyManager] Scarcity target is incompatible with the overall legal occupancy. " +
                $"scarcity={scarcityLevel}, nearbyTargetOccupied={nearbyTargetOccupied}, overallTarget={targetLegalOccupied}. " +
                "Falling back to exact-total weighted selection for this scenario."
            );
            SelectWeightedWithoutReplacement(legalSpots, targetLegalOccupied);
            return;
        }

        int remainingAfterNearby = targetLegalOccupied - nearbyTargetOccupied;

        if (remainingAfterNearby > nonNearbyCapacity)
        {
            Debug.LogError(
                "[ParkingOccupancyManager] Not enough A5/other legal capacity to preserve the requested scarcity target. " +
                $"scarcity={scarcityLevel}, remainingNeeded={remainingAfterNearby}, nonNearbyCapacity={nonNearbyCapacity}. " +
                "Falling back to exact-total weighted selection for this scenario."
            );
            SelectWeightedWithoutReplacement(legalSpots, targetLegalOccupied);
            return;
        }

        SelectWeightedWithoutReplacement(a1Spots, targetA1Occupied);
        SelectWeightedWithoutReplacement(a3Spots, targetA3Occupied);

        int occupyFromA5 = Mathf.Min(remainingAfterNearby, a5Spots.Count);
        SelectWeightedWithoutReplacement(a5Spots, occupyFromA5);

        int stillNeeded = remainingAfterNearby - occupyFromA5;
        if (stillNeeded > 0)
            SelectWeightedWithoutReplacement(unknownLegalSpots, stillNeeded);

        if (logOccupancyResults)
        {
            Debug.Log(
                "[ParkingOccupancyManager] LEGAL SCARCITY APPLIED | " +
                $"level={scarcityLevel} | A1 free target={desiredA1Free}, A3 free target={desiredA3Free} | " +
                $"overall legal target={targetLegalOccupied}/{legalSpots.Count}"
            );
        }
    }

    private void GetNearbyFreeTargets(out int targetA1Free, out int targetA3Free)
    {
        switch (scarcityLevel)
        {
            case ParkingScarcityLevel.Extreme:
                targetA1Free = extremeA1Free;
                targetA3Free = extremeA3Free;
                return;

            case ParkingScarcityLevel.High:
            default:
                // Default also protects legacy assets that may still contain
                // the removed numeric Normal value (0): they behave as High
                // until the asset is replaced or resaved.
                targetA1Free = highA1Free;
                targetA3Free = highA3Free;
                return;
        }
    }

    private void ApplyIllegalOccupancyWithA2NoFineCalibration(
        List<ParkingSpot> illegalSpots,
        int targetIllegalOccupied)
    {
        List<ParkingSpot> a2NoFineSpots = new List<ParkingSpot>();
        List<ParkingSpot> otherIllegalSpots = new List<ParkingSpot>();

        foreach (ParkingSpot spot in illegalSpots)
        {
            if (spot == null)
                continue;

            if (IsA2NoFineSpot(spot))
                a2NoFineSpots.Add(spot);
            else
                otherIllegalSpots.Add(spot);
        }

        int calibratedA2NoFineOccupied = Mathf.Min(
            StudyA2NoFineOccupied,
            a2NoFineSpots.Count
        );

        // The fixed A2 no-fine calibration must fit inside the scenario's
        // overall illegal occupancy target. If it does not, the scenario
        // configuration is incompatible with the study rule.
        if (calibratedA2NoFineOccupied > targetIllegalOccupied)
        {
            Debug.LogError(
                "[ParkingOccupancyManager] Illegal occupancy target is too low " +
                "for the fixed A2 no-fine calibration. " +
                $"A2 no-fine required occupied={calibratedA2NoFineOccupied}, " +
                $"overall illegal target={targetIllegalOccupied}. " +
                "Falling back to the overall exact-total weighted selection."
            );

            SelectWeightedWithoutReplacement(
                illegalSpots,
                targetIllegalOccupied
            );
            return;
        }

        int remainingIllegalToOccupy =
            targetIllegalOccupied - calibratedA2NoFineOccupied;

        if (remainingIllegalToOccupy > otherIllegalSpots.Count)
        {
            Debug.LogError(
                "[ParkingOccupancyManager] Not enough other illegal spaces to " +
                "preserve the overall illegal occupancy target after applying " +
                "the fixed A2 no-fine calibration. " +
                $"remainingNeeded={remainingIllegalToOccupy}, " +
                $"otherIllegalCapacity={otherIllegalSpots.Count}. " +
                "Falling back to the overall exact-total weighted selection."
            );

            SelectWeightedWithoutReplacement(
                illegalSpots,
                targetIllegalOccupied
            );
            return;
        }

        // All A2 no-fine spots have the same A2 spatial weight, so this is
        // effectively a random exact-size draw: exactly 10 occupied, with
        // which individual spaces are occupied changing randomly by scenario.
        SelectWeightedWithoutReplacement(
            a2NoFineSpots,
            calibratedA2NoFineOccupied
        );

        // Fill the rest of the scenario's illegal occupied target from every
        // other illegal space using the existing A2/A4/A6 spatial weights.
        SelectWeightedWithoutReplacement(
            otherIllegalSpots,
            remainingIllegalToOccupy
        );

        if (logOccupancyResults)
        {
            int a2NoFineFree =
                a2NoFineSpots.Count - calibratedA2NoFineOccupied;

            Debug.Log(
                "[ParkingOccupancyManager] A2 NO-FINE CALIBRATION APPLIED | " +
                $"occupied={calibratedA2NoFineOccupied}/{a2NoFineSpots.Count} | " +
                $"free={a2NoFineFree} | " +
                $"remainingIllegalOccupied={remainingIllegalToOccupy} | " +
                $"overallIllegalTarget={targetIllegalOccupied}/{illegalSpots.Count}"
            );
        }
    }

    private static bool IsA2NoFineSpot(ParkingSpot spot)
    {
        if (spot == null)
            return false;

        if (spot.parkingType != ParkingType.Illegal)
            return false;

        if (spot.GetAlternativeId6() != "A2")
            return false;

        return
            spot.illegalReason == IllegalReason.IntersectionCorner ||
            spot.illegalReason == IllegalReason.PedestrianCrossing;
    }

    private void SelectWeightedWithoutReplacement(
        List<ParkingSpot> pool,
        int targetOccupied)
    {
        if (pool == null || pool.Count == 0 || targetOccupied <= 0)
            return;

        targetOccupied = Mathf.Clamp(targetOccupied, 0, pool.Count);

        List<WeightedCandidate> candidates =
            new List<WeightedCandidate>(pool.Count);

        foreach (ParkingSpot spot in pool)
        {
            if (spot == null)
                continue;

            float weight = Mathf.Max(0.01f, GetSpatialWeight(spot));
            float u = Mathf.Clamp(UnityEngine.Random.value, 0.000001f, 0.999999f);

            candidates.Add(
                new WeightedCandidate
                {
                    spot = spot,
                    priority = -Mathf.Log(u) / weight
                }
            );
        }

        candidates.Sort((a, b) => a.priority.CompareTo(b.priority));

        int countToOccupy = Mathf.Min(targetOccupied, candidates.Count);

        for (int i = 0; i < countToOccupy; i++)
        {
            if (candidates[i].spot != null)
                candidates[i].spot.SetOccupied(true);
        }
    }

    private float GetSpatialWeight(ParkingSpot spot)
    {
        if (spot == null)
            return 1f;

        string alternative = spot.GetAlternativeId6();

        float baseWeight;

        switch (alternative)
        {
            case "A2":
                baseWeight = a2InnerIllegalWeight;
                break;

            case "A4":
                baseWeight = a4OuterIllegalWeight;
                break;

            case "A6":
                baseWeight = a6FarIllegalWeight;
                break;

            default:
                // A5 legal spaces intentionally start at 1.0. Their weight is
                // raised only when they lie inside a ParkingDemandZone.
                baseWeight = 1f;
                break;
        }

        // Secondary-gate/background demand applies ONLY to A5 and A6.
        // A1-A4, including the fixed A2 no-fine calibration, are untouched.
        if (alternative == "A5" || alternative == "A6")
        {
            baseWeight *= GetDemandZoneMultiplier(spot);
        }

        return Mathf.Max(0.01f, baseWeight);
    }

    private float GetDemandZoneMultiplier(ParkingSpot spot)
    {
        if (spot == null ||
            demandZones == null ||
            demandZones.Length == 0)
        {
            return 1f;
        }

        // Use the strongest matching zone rather than multiplying overlapping
        // zones together. This prevents accidental extreme weights if two
        // calibration boxes overlap.
        float multiplier = 1f;

        foreach (ParkingDemandZone zone in demandZones)
        {
            if (zone == null || !zone.isActiveAndEnabled)
                continue;

            multiplier = Mathf.Max(
                multiplier,
                zone.GetOccupancyWeightMultiplier(spot)
            );
        }

        return multiplier;
    }

    private bool IsInsideAnyDemandZone(ParkingSpot spot)
    {
        return GetDemandZoneMultiplier(spot) > 1.0001f;
    }

    private void ApplyLegacyIndependentOccupancy()
    {
        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            if (spot.IsPhysicallyBlockedParking())
            {
                spot.SetOccupied(false);
                continue;
            }

            bool isLegal = spot.parkingType == ParkingType.Legal;
            float rate = isLegal ? legalOccupancyRate : illegalOccupancyRate;

            spot.SetOccupied(UnityEngine.Random.value < rate);
        }

        if (logOccupancyResults)
        {
            LogAndVerifyResults(
                CountParkingType(ParkingType.Legal),
                CountParkingType(ParkingType.Illegal),
                -1,
                -1
            );
        }
    }

    private int CountParkingType(ParkingType type)
    {
        int count = 0;

        foreach (ParkingSpot spot in spots)
        {
            if (spot != null &&
                !spot.IsPhysicallyBlockedParking() &&
                spot.parkingType == type)
            {
                count++;
            }
        }

        return count;
    }

    private void LogAndVerifyResults(
        int legalTotal,
        int illegalTotal,
        int targetLegalOccupied,
        int targetIllegalOccupied)
    {
        int legalOccupied = 0;
        int illegalOccupied = 0;

        int a2NoFineTotal = 0;
        int a2NoFineOccupied = 0;

        int[] groupTotal = new int[6];
        int[] groupOccupied = new int[6];

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null || spot.IsPhysicallyBlockedParking())
                continue;

            if (spot.parkingType == ParkingType.Legal)
            {
                if (spot.isOccupied) legalOccupied++;
            }
            else
            {
                if (spot.isOccupied) illegalOccupied++;

                if (IsA2NoFineSpot(spot))
                {
                    a2NoFineTotal++;
                    if (spot.isOccupied)
                        a2NoFineOccupied++;
                }
            }

            int index = AlternativeToIndex(spot.GetAlternativeId6());

            if (index >= 0)
            {
                groupTotal[index]++;
                if (spot.isOccupied) groupOccupied[index]++;
            }
        }

        int actualA1Free = groupTotal[0] - groupOccupied[0];
        int actualA3Free = groupTotal[2] - groupOccupied[2];

        GetNearbyFreeTargets(out int targetA1Free, out int targetA3Free);
        targetA1Free = Mathf.Clamp(targetA1Free, 0, groupTotal[0]);
        targetA3Free = Mathf.Clamp(targetA3Free, 0, groupTotal[2]);

        if (logOccupancyResults)
        {
            Debug.Log("========== SCARCITY-CALIBRATED OCCUPANCY RESULTS ==========");

            Debug.Log(
                $"SCARCITY | level={scarcityLevel} | " +
                $"targetFreeWithin300m={targetA1Free + targetA3Free} (A1={targetA1Free}, A3={targetA3Free}) | " +
                $"actualFreeWithin300m={actualA1Free + actualA3Free} (A1={actualA1Free}, A3={actualA3Free})"
            );

            Debug.Log(
                $"LEGAL overall | targetRate={legalOccupancyRate:P1} | " +
                $"targetOccupied={(targetLegalOccupied >= 0 ? targetLegalOccupied.ToString() : "legacy-random")} | " +
                $"actual={legalOccupied}/{legalTotal} ({Percent(legalOccupied, legalTotal):0.0}%) | free={legalTotal - legalOccupied}"
            );

            Debug.Log(
                $"ILLEGAL overall | targetRate={illegalOccupancyRate:P1} | " +
                $"targetOccupied={(targetIllegalOccupied >= 0 ? targetIllegalOccupied.ToString() : "legacy-random")} | " +
                $"actual={illegalOccupied}/{illegalTotal} ({Percent(illegalOccupied, illegalTotal):0.0}%) | free={illegalTotal - illegalOccupied}"
            );

            Debug.Log(
                $"A2 NO-FINE | occupied={a2NoFineOccupied}/{a2NoFineTotal} " +
                $"({Percent(a2NoFineOccupied, a2NoFineTotal):0.0}%) | " +
                $"FREE={a2NoFineTotal - a2NoFineOccupied} | " +
                $"studyTargetOccupied={Mathf.Min(StudyA2NoFineOccupied, a2NoFineTotal)}"
            );

            Debug.Log("------------------------------------------------");

            for (int i = 0; i < 6; i++)
            {
                int free = groupTotal[i] - groupOccupied[i];

                Debug.Log(
                    $"A{i + 1} | occupied={groupOccupied[i]}/{groupTotal[i]} " +
                    $"({Percent(groupOccupied[i], groupTotal[i]):0.0}%) | FREE={free}"
                );
            }

            int demandA5Total = 0;
            int demandA5Occupied = 0;
            int demandA6Total = 0;
            int demandA6Occupied = 0;

            foreach (ParkingSpot spot in spots)
            {
                if (spot == null ||
                    spot.IsPhysicallyBlockedParking() ||
                    !IsInsideAnyDemandZone(spot))
                {
                    continue;
                }

                string alternative = spot.GetAlternativeId6();

                if (alternative == "A5")
                {
                    demandA5Total++;
                    if (spot.isOccupied)
                        demandA5Occupied++;
                }
                else if (alternative == "A6")
                {
                    demandA6Total++;
                    if (spot.isOccupied)
                        demandA6Occupied++;
                }
            }

            if (demandZones != null && demandZones.Length > 0)
            {
                Debug.Log(
                    $"SECONDARY DEMAND ZONE | zones={demandZones.Length} | " +
                    $"A5 occupied={demandA5Occupied}/{demandA5Total} " +
                    $"({Percent(demandA5Occupied, demandA5Total):0.0}%) | " +
                    $"A6 occupied={demandA6Occupied}/{demandA6Total} " +
                    $"({Percent(demandA6Occupied, demandA6Total):0.0}%)"
                );
            }

            Debug.Log("===========================================================");
        }

        if (verifyExactTotals && useWeightedExactTotals)
        {
            bool legalPassed = legalOccupied == targetLegalOccupied;
            bool illegalPassed = illegalOccupied == targetIllegalOccupied;
            bool scarcityPassed =
                actualA1Free == targetA1Free &&
                actualA3Free == targetA3Free;

            int expectedA2NoFineOccupied = Mathf.Min(
                StudyA2NoFineOccupied,
                a2NoFineTotal
            );

            bool a2NoFinePassed =
                a2NoFineOccupied == expectedA2NoFineOccupied;

            if (!legalPassed || !illegalPassed || !scarcityPassed || !a2NoFinePassed)
            {
                Debug.LogError(
                    "[ParkingOccupancyManager] OCCUPANCY CHECK FAILED | " +
                    $"legal={legalOccupied}/{targetLegalOccupied}, illegal={illegalOccupied}/{targetIllegalOccupied}, " +
                    $"scarcity={scarcityLevel}, A1free={actualA1Free}/{targetA1Free}, A3free={actualA3Free}/{targetA3Free}, " +
                    $"A2NoFineOccupied={a2NoFineOccupied}/{expectedA2NoFineOccupied}. " +
                    "Do not use this scenario as study data."
                );
            }
            else if (logOccupancyResults)
            {
                Debug.Log(
                    "[ParkingOccupancyManager] OCCUPANCY CHECK PASSED | " +
                    $"legal={legalOccupied}/{legalTotal}, illegal={illegalOccupied}/{illegalTotal}, " +
                    $"scarcity={scarcityLevel}, A1free={actualA1Free}, A3free={actualA3Free}, " +
                    $"A2NoFineOccupied={a2NoFineOccupied}/{a2NoFineTotal}."
                );
            }
        }
    }

    private static int AlternativeToIndex(string alternative)
    {
        switch (alternative)
        {
            case "A1": return 0;
            case "A2": return 1;
            case "A3": return 2;
            case "A4": return 3;
            case "A5": return 4;
            case "A6": return 5;
            default: return -1;
        }
    }

    private static float Percent(int part, int total)
    {
        if (total <= 0)
            return 0f;

        return 100f * part / total;
    }
}
