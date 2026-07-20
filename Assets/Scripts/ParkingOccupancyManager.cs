using UnityEngine;

public class ParkingOccupancyManager : MonoBehaviour
{
    [Header("Occupancy rates (0 = empty, 1 = full)")]
    [Range(0f, 1f)] public float legalOccupancyRate = 0.7f;
    [Range(0f, 1f)] public float illegalOccupancyRate = 0.1f;

    private ParkingSpot[] spots;

    void Awake()
    {
        // Find all parking spots once
        spots = FindObjectsOfType<ParkingSpot>();
    }

    // Called by ScenarioManager for each scenario
    public void ApplyOccupancy()
    {
        if (spots == null || spots.Length == 0)
        {
            spots = FindObjectsOfType<ParkingSpot>();
        }

        foreach (var spot in spots)
        {
            if (spot == null) continue;

            // Choose which occupancy rate to use based on legal/illegal
            bool isLegal = (spot.parkingType == ParkingType.Legal);
            float rate = isLegal ? legalOccupancyRate : illegalOccupancyRate;

            // Randomly set occupied/free
            spot.isOccupied = (Random.value < rate);
            spot.UpdateVisual();
        }
    }
}
