using UnityEngine;

/// <summary>
/// Marks a background/local-demand area on the map.
///
/// STUDY RULE:
/// - This is NOT a scenario factor.
/// - It only affects A5 (Far Legal) and A6 (Far Illegal) spaces.
/// - Spots inside the box receive a fixed x3 occupancy-selection weight.
/// - The exact overall legal/illegal occupancy totals are still controlled by
///   ParkingOccupancyManager and therefore do not increase.
///
/// Usage:
/// 1. Add this component to an empty GameObject.
/// 2. Resize its BoxCollider2D in the Scene view to cover the local-demand area.
/// 3. Keep the GameObject active.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class ParkingDemandZone : MonoBehaviour
{
    private const float StudyOccupancyWeightMultiplier = 3f;

    private BoxCollider2D zoneCollider;

    public float OccupancyWeightMultiplier =>
        StudyOccupancyWeightMultiplier;

    private void Awake()
    {
        CacheCollider();
        ConfigureCollider();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheCollider();
        ConfigureCollider();
    }
#endif

    private void Reset()
    {
        CacheCollider();
        ConfigureCollider();
    }

    private void CacheCollider()
    {
        if (zoneCollider == null)
            zoneCollider = GetComponent<BoxCollider2D>();
    }

    private void ConfigureCollider()
    {
        if (zoneCollider == null)
            return;

        // The box is only a spatial marker. It must not physically block cars,
        // clicks, or any other gameplay object.
        zoneCollider.isTrigger = true;
    }

    public float GetOccupancyWeightMultiplier(ParkingSpot spot)
    {
        if (!ContainsAffectedSpot(spot))
            return 1f;

        return StudyOccupancyWeightMultiplier;
    }

    public bool ContainsAffectedSpot(ParkingSpot spot)
    {
        if (spot == null ||
            !isActiveAndEnabled)
        {
            return false;
        }

        string alternative = spot.GetAlternativeId6();

        // Keep this environmental calibration isolated from the Gate 2 logic.
        if (alternative != "A5" &&
            alternative != "A6")
        {
            return false;
        }

        CacheCollider();

        if (zoneCollider == null ||
            !zoneCollider.enabled)
        {
            return false;
        }

        return zoneCollider.OverlapPoint(
            spot.transform.position
        );
    }
}
