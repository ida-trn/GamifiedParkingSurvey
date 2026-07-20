using UnityEngine;

[CreateAssetMenu(fileName = "ParkingScenario", menuName = "Parking/Scenario")]
public class ParkingScenario : ScriptableObject
{
    [Header("ID")]
    public string scenarioId = "S01";

    [Header("Timing")]
    [Min(0f)] public float dwellTimeMinutes = 30f;
    [Min(0f)] public float timeLimitMinutes = 5f;

    [Header("Iranian Prices / Risk")]
    [Tooltip("Legal on-street parking charge in TOMANS per hour.")]
    [Min(0f)] public float legalFeePerHour = 12000f;

    [Tooltip("Hourly probability of receiving a citation while parked illegally. Use 0.10 for 10%.")]
    [Range(0f, 1f)] public float citationProbability = 0.10f;

    [Tooltip("Full illegal-parking fine in TOMANS. This is charged only when the citation draw succeeds.")]
    [Min(0f)] public float illegalFine = 100000f;

    [Header("Legacy logger compatibility")]
    [Tooltip("ScenarioManager sets this to the on-time base score at runtime. Do not use it as money.")]
    public float onTimeReward = 100f;

    [Header("Occupancy")]
    [Range(0f, 1f)] public float legalOccupancyRate = 0.7f;
    [Range(0f, 1f)] public float illegalOccupancyRate = 0.1f;

    [Header("Mode")]
    public bool assistedMode = false;

    [Header("Start Node")]
    [Tooltip("Index of the RoadNode used as the car start node for this scenario.")]
    public int startPointIndex = 0;
}
