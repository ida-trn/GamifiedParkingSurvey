using UnityEngine;

public enum IllegalFinePolicyLevel
{
    Current = 0,
    Doubled = 1
}

public enum ParkingScarcityLevel
{
    Normal = 0,
    High = 1,
    Extreme = 2
}

[CreateAssetMenu(fileName = "ParkingScenario", menuName = "Parking/Scenario")]
public class ParkingScenario : ScriptableObject
{
    [Header("ID")]
    public string scenarioId = "S01";

    [Header("Timing")]
    [Min(0f)] public float dwellTimeMinutes = 30f;
    [Min(0f)] public float timeLimitMinutes = 5f;

    [Header("Iranian Prices / Risk")]
    [Tooltip("LEGACY ONLY. Smart Parking now uses ParkingPolicy1405 and normal legal parking is free. Keep this field for old assets/logger compatibility; do not use it for new scoring.")]
    [Min(0f)] public float legalFeePerHour = 0f;

    [Tooltip("Hourly probability of receiving a citation while parked illegally. Use 0.10 for 10%.")]
    [Range(0f, 1f)] public float citationProbability = 0.10f;

    [Tooltip("Experimental illegal-fine policy for this scenario. Current = real baseline fines. Doubled = hypothetical policy with all illegal-parking fines doubled. The same level is used in both Conventional and Assisted presentations of this scenario.")]
    public IllegalFinePolicyLevel illegalFinePolicy = IllegalFinePolicyLevel.Current;

    [Tooltip("LEGACY ONLY. Old scenario assets used 60000/120000 here. It is kept hidden so the migration tool can convert those old values to Current/Doubled. It is no longer used for scoring, UI, or logging.")]
    [HideInInspector]
    [Min(0f)] public float illegalFine = 100000f;

    public float GetIllegalFineMultiplier()
    {
        return illegalFinePolicy == IllegalFinePolicyLevel.Doubled
            ? 2f
            : 1f;
    }

    [Header("Legacy logger compatibility")]
    [Tooltip("Legacy runtime/logger compatibility field. ScenarioManager overwrites this value at runtime; it is hidden from the Inspector because the current study uses a fixed late-time penalty rather than an on-time reward.")]
    [HideInInspector]
    public float onTimeReward = 100f;

    [Header("Occupancy")]
    [Range(0f, 1f)] public float legalOccupancyRate = 0.7f;
    [Range(0f, 1f)] public float illegalOccupancyRate = 0.1f;

    [Tooltip(
        "Controls spatial scarcity of LEGAL parking near Gate 2 while preserving the scenario's exact overall legal occupancy. " +
        "Normal = 10 free A1 + 10 free A3; High = 2 free A1 + 2 free A3; Extreme = 0 free A1 + 0 free A3."
    )]
    public ParkingScarcityLevel scarcityLevel = ParkingScarcityLevel.Normal;

    [Header("Mode")]
    public bool assistedMode = false;

    [Header("Start Node")]
    [Tooltip("Index of the RoadNode used as the car start node for this scenario.")]
    public int startPointIndex = 0;
}
