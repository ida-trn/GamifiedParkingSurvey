using System;
using UnityEngine;

/// <summary>
/// Pure scoring logic for one parking round.
/// Money is stored in Iranian tomans, while the game score is stored in points.
///
/// Pilot scoring philosophy:
/// - Every research round starts from ScenarioManager's per-scenario score.
/// - Normal legal parking is free.
/// - Smart legal parking uses the 1405 stepped smart-parking tariff.
/// - Illegal parking uses the real baseline fine for the selected violation type,
///   multiplied by the scenario's fine-policy level (Current = 1x, Doubled = 2x).
/// - Illegal parking still has a score consequence even when no citation occurs.
///   The score deduction represents the undesirability of choosing an illegal space,
///   while the citation draw separately determines whether a monetary fine is realized.
/// - Citation probability is interpreted as an independent hourly citation opportunity.
///   The probability of at least one citation during the full parking duration is
///   P = 1 - (1 - p)^t, where p is hourly probability and t is dwell hours.
/// - Legal parking costs use tomanPerPoint. Fine-bearing illegal parking uses the
///   separate illegalFineTomanPerPoint scale, and no-fine illegal parking uses a
///   fixed point deduction.
/// - Tow/impound risk is informational only and never changes the score.
/// </summary>
public static class ParkingScoreCalculator
{
    public struct Result
    {
        public bool isLegal;
        public bool isSmartParking;
        public bool towEligible;
        public bool citationOccurred;
        public int randomSeed;
        public float randomDraw;

        public float dwellHours;
        public float adjustedCitationProbability;

        public float fineMultiplier;
        public int baseIllegalFineToman;
        public int appliedIllegalFineToman;

        public float legalCostToman;
        public float expectedIllegalCostToman;
        public float realizedCostToman;

        public int basePoints;
        public int pointDeduction;
        public int roundPoints;
    }

    public static Result Calculate(
        ParkingScenario scenario,
        ParkingSpot spot,
        bool arrivedOnTime,
        string participantId,
        int roundIndex,
        bool assistedMode,
        int onTimeBasePoints,
        int lateBasePoints,
        float tomanPerPoint,
        float illegalFineTomanPerPoint = 4000f,
        int noFineIllegalPenaltyPoints = 10)
    {
        if (scenario == null)
            throw new ArgumentNullException(nameof(scenario));

        if (spot == null)
            throw new ArgumentNullException(nameof(spot));

        float safeTomanPerPoint = Mathf.Max(1f, tomanPerPoint);
        float safeIllegalFineTomanPerPoint = Mathf.Max(1f, illegalFineTomanPerPoint);
        int safeNoFineIllegalPenaltyPoints = Mathf.Max(0, noFineIllegalPenaltyPoints);
        float dwellHours = Mathf.Max(0f, scenario.dwellTimeMinutes / 60f);

        bool isLegal = spot.parkingType == ParkingType.Legal;
        bool isSmartParking = ParkingPolicy1405.IsSmartParking(spot);
        bool towEligible = ParkingPolicy1405.IsTowWarningEligible(spot);
        bool noFinancialFine =
            ParkingPolicy1405.IsNoFinancialFineIllegalSpot(spot);

        float hourlyProbability = Mathf.Clamp01(scenario.citationProbability);

        // Convert the hourly treatment to the probability of at least one
        // citation during the full dwell using independent hourly opportunities.
        // Intersection corners and pedestrian crossings remain no-financial-fine choices.
        float adjustedProbability =
            !isLegal && !noFinancialFine
                ? CalculateAdjustedCitationProbability(
                    hourlyProbability,
                    scenario.dwellTimeMinutes
                )
                : 0f;

        float fineMultiplier = scenario.GetIllegalFineMultiplier();

        int baseIllegalFineToman =
            ParkingPolicy1405.GetBaseIllegalFineToman(spot);

        int appliedIllegalFineToman =
            ParkingPolicy1405.GetAppliedIllegalFineToman(
                spot,
                fineMultiplier
            );

        float legalCostToman = isSmartParking
            ? ParkingPolicy1405.CalculateSmartParkingFeeToman(
                scenario.dwellTimeMinutes
            )
            : 0f;

        float expectedIllegalCostToman =
            !isLegal && !noFinancialFine
                ? appliedIllegalFineToman * adjustedProbability
                : 0f;

        int randomSeed = BuildStableSeed(
            participantId,
            scenario.scenarioId,
            roundIndex,
            assistedMode
        );

        System.Random random = new System.Random(randomSeed);
        float randomDraw = (float)random.NextDouble();

        bool citationOccurred =
            !isLegal &&
            !noFinancialFine &&
            appliedIllegalFineToman > 0 &&
            randomDraw < adjustedProbability;

        float realizedCostToman = isLegal
            ? legalCostToman
            : citationOccurred
                ? appliedIllegalFineToman
                : 0f;

        int basePoints = arrivedOnTime
            ? Mathf.Max(0, onTimeBasePoints)
            : Mathf.Max(0, lateBasePoints);

        int deduction;

        if (isLegal)
        {
            // Legal parking scoring continues to reflect the realized parking cost.
            deduction = Mathf.Max(
                0,
                Mathf.RoundToInt(legalCostToman / safeTomanPerPoint)
            );
        }
        else if (noFinancialFine)
        {
            // These spaces are illegal even though the study assigns no monetary
            // citation. They therefore receive the fixed visual score deduction.
            deduction = safeNoFineIllegalPenaltyPoints;
        }
        else if (appliedIllegalFineToman > 0)
        {
            // IMPORTANT: the illegal-parking score consequence is based on the
            // applicable fine amount whether or not the random citation occurs.
            // Citation still determines realizedCostToman separately.
            deduction = Mathf.Max(
                1,
                Mathf.FloorToInt(
                    appliedIllegalFineToman / safeIllegalFineTomanPerPoint
                )
            );
        }
        else
        {
            deduction = 0;
        }

        int roundPoints = basePoints - deduction;

        return new Result
        {
            isLegal = isLegal,
            isSmartParking = isSmartParking,
            towEligible = towEligible,
            citationOccurred = citationOccurred,
            randomSeed = randomSeed,
            randomDraw = randomDraw,
            dwellHours = dwellHours,
            adjustedCitationProbability = adjustedProbability,
            fineMultiplier = fineMultiplier,
            baseIllegalFineToman = baseIllegalFineToman,
            appliedIllegalFineToman = appliedIllegalFineToman,
            legalCostToman = legalCostToman,
            expectedIllegalCostToman = expectedIllegalCostToman,
            realizedCostToman = realizedCostToman,
            basePoints = basePoints,
            pointDeduction = deduction,
            roundPoints = roundPoints
        };
    }

    /// <summary>
    /// Probability of at least one citation during the full parking duration,
    /// assuming independent hourly citation opportunities.
    /// </summary>
    public static float CalculateAdjustedCitationProbability(
        float hourlyProbability,
        float dwellTimeMinutes)
    {
        hourlyProbability = Mathf.Clamp01(hourlyProbability);
        float dwellHours = Mathf.Max(0f, dwellTimeMinutes) / 60f;

        return Mathf.Clamp01(
            1f - Mathf.Pow(1f - hourlyProbability, dwellHours)
        );
    }

    private static int BuildStableSeed(
        string participantId,
        string scenarioId,
        int roundIndex,
        bool assistedMode)
    {
        string key =
            (participantId ?? "UNKNOWN") + "|" +
            (scenarioId ?? "UNKNOWN") + "|" +
            roundIndex + "|" +
            (assistedMode ? "A" : "C");

        unchecked
        {
            uint hash = 2166136261u;

            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
