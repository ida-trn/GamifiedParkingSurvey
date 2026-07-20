using UnityEngine;

public class ParkingRegionAssigner : MonoBehaviour
{
    [Header("Destination")]
    public Transform destination;

    [Header("Scale")]
    [Tooltip("How many real meters equal 1 Unity unit.")]
    public float metersPerUnityUnit = 10f;

    [Header("Thresholds in meters")]
    public float innerThresholdMeters = 100f;
    public float outerThresholdMeters = 200f;

    [Header("Debug")]
    public bool assignOnStart = true;
    public bool debugLogs = true;

    private void Start()
    {
        if (assignOnStart)
        {
            AssignRegions();
        }
    }

    [ContextMenu("Assign Parking Regions")]
    public void AssignRegions()
    {
        if (destination == null)
        {
            Debug.LogError("[ParkingRegionAssigner] Destination is not assigned.");
            return;
        }

        ParkingSpot[] spots = FindObjectsByType<ParkingSpot>(FindObjectsSortMode.None);

        foreach (ParkingSpot spot in spots)
        {
            float unityDistance = Vector2.Distance(
                spot.transform.position,
                destination.position
            );

            float distanceMeters = unityDistance * metersPerUnityUnit;

            spot.walkingDistanceMeters = distanceMeters;

            // Assume average walking speed ≈ 1.4 m/s
            spot.walkingTimeSeconds = distanceMeters / 1.4f;

            if (distanceMeters < innerThresholdMeters)
            {
                spot.region = RegionType.Inner;
            }
            else if (distanceMeters <= outerThresholdMeters)
            {
                spot.region = RegionType.Outer;
            }
            else
            {
                spot.region = RegionType.Far;
            }

            spot.UpdateVisual();

            if (debugLogs)
            {
                Debug.Log(
                    $"[ParkingRegionAssigner] {spot.spotId}: " +
                    $"{distanceMeters:0.0} m → {spot.region}"
                );
            }
        }
    }
}