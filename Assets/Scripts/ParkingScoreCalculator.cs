using System;
using UnityEngine;

/// <summary>
/// Pure scoring logic for one parking round.
/// Money is stored in Iranian tomans, while the game score is stored in points.
/// </summary>
public static class ParkingScoreCalculator
{
    public struct Result
    {
        public bool isLegal;
        public bool citationOccurred;
        public int randomSeed;
        public float randomDraw;

        public float dwellHours;
        public float adjustedCitationProbability;
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
        float tomanPerPoint)
    {
        if (scenario == null)
            throw new ArgumentNullException(nameof(scenario));

        if (spot == null)
            throw new ArgumentNullException(nameof(spot));

        float safeTomanPerPoint = Mathf.Max(1f, tomanPerPoint);
        float dwellHours = Mathf.Max(0f, scenario.dwellTimeMinutes / 60f);
        bool isLegal = spot.parkingType == ParkingType.Legal;

        float hourlyProbability = Mathf.Clamp01(scenario.citationProbability);

        // Converts an hourly citation probability into the probability over the
        // actual parking duration: 1 - (1 - p)^hours.
        float adjustedProbability = dwellHours <= 0f
            ? 0f
            : 1f - Mathf.Pow(1f - hourlyProbability, dwellHours);

        adjustedProbability = Mathf.Clamp01(adjustedProbability);

        float legalCostToman = Mathf.Max(0f, scenario.legalFeePerHour) * dwellHours;
        float expectedIllegalCostToman =
            Mathf.Max(0f, scenario.illegalFine) * adjustedProbability;

        int randomSeed = BuildStableSeed(
            participantId,
            scenario.scenarioId,
            roundIndex,
            assistedMode
        );

        System.Random random = new System.Random(randomSeed);
        float randomDraw = (float)random.NextDouble();

        bool citationOccurred = !isLegal && randomDraw < adjustedProbability;

        float realizedCostToman = isLegal
            ? legalCostToman
            : citationOccurred
                ? Mathf.Max(0f, scenario.illegalFine)
                : 0f;

        int basePoints = arrivedOnTime
            ? Mathf.Max(0, onTimeBasePoints)
            : Mathf.Max(0, lateBasePoints);

        int deduction = Mathf.Max(
            0,
            Mathf.RoundToInt(realizedCostToman / safeTomanPerPoint)
        );

        int roundPoints = Mathf.Max(0, basePoints - deduction);

        return new Result
        {
            isLegal = isLegal,
            citationOccurred = citationOccurred,
            randomSeed = randomSeed,
            randomDraw = randomDraw,
            dwellHours = dwellHours,
            adjustedCitationProbability = adjustedProbability,
            legalCostToman = legalCostToman,
            expectedIllegalCostToman = expectedIllegalCostToman,
            realizedCostToman = realizedCostToman,
            basePoints = basePoints,
            pointDeduction = deduction,
            roundPoints = roundPoints
        };
    }

    /// <summary>
    /// Stable FNV-1a hash. Unlike string.GetHashCode(), this remains reproducible
    /// across devices and application launches.
    /// </summary>
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
