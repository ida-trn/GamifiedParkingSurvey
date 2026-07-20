using UnityEngine;

public class CarParkingInteraction : MonoBehaviour
{
    public static CarParkingInteraction Instance { get; private set; }

    [Header("Mode")]
    public bool assistedMode = false;

    [Header("Parking By Click/Tap - Conventional Mode")]
    public bool enableClickParking = true;

    [Tooltip("Conventional mode keeps your old behavior: clicking a parking spot makes the car drive to it and park there.")]
    public bool routeToClickedParkingSpot = true;

    [Tooltip("Only used if routeToClickedParkingSpot is false.")]
    public float maxParkDistance = 25f;

    [Tooltip("If true, the car jumps visually onto the selected parking spot after arrival.")]
    public bool snapCarToParkingSpot = true;

    [Header("Assisted Mode")]
    [Tooltip("In assisted mode, user selects any free spot directly from the map.")]
    public bool assistedDirectSelection = true;

    [Tooltip("Hide the car during assisted map-selection mode.")]
    public bool hideCarInAssistedMode = true;

    [Tooltip("Disable the node follower while assisted map mode is active.")]
    public bool disableCarMovementInAssistedMode = true;

    [Header("Testing")]
    [Tooltip("Turn on temporarily if your spots are still classified as Far.")]
    public bool allowFarSpotsForTesting = true;

    [Header("Scenario hookup")]
    [Tooltip("Assign ScenarioManager here. If left empty, the script finds it automatically.")]
    public ScenarioManager scenarioManager;

    [Header("Debug")]
    public bool debugLogs = true;

    private float travelStartTime;
    private bool parkedThisScenario = false;

    private CarNodeFollower carNodeFollower;
    private SpriteRenderer[] carRenderers;
    private Collider2D[] carColliders;
    private Rigidbody2D carRb;

    private void Awake()
    {
        Instance = this;

        carNodeFollower = GetComponent<CarNodeFollower>();

        if (carNodeFollower == null)
            carNodeFollower = FindFirstObjectByType<CarNodeFollower>();

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        carRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        carColliders = GetComponentsInChildren<Collider2D>(true);
        carRb = GetComponent<Rigidbody2D>();

        ResetForNewScenario();

        if (scenarioManager == null)
        {
            Debug.LogError(
                "[CarParkingInteraction] ScenarioManager was not assigned and could not be found."
            );
        }
    }

    public void SetAssistedMode(bool assisted)
    {
        assistedMode = assisted;
        ApplyAssistedModeVisuals();

        if (debugLogs)
        {
            Debug.Log(
                "[CarParkingInteraction] Mode set to: " +
                (assistedMode ? "ASSISTED MAP MODE" : "CONVENTIONAL CAR MODE")
            );
        }
    }

    private void ApplyAssistedModeVisuals()
    {
        if (hideCarInAssistedMode)
        {
            foreach (SpriteRenderer r in carRenderers)
            {
                if (r != null)
                    r.enabled = !assistedMode;
            }
        }

        if (disableCarMovementInAssistedMode && carNodeFollower != null)
            carNodeFollower.enabled = !assistedMode;

        if (carRb != null)
        {
            carRb.linearVelocity = Vector2.zero;
            carRb.angularVelocity = 0f;
            carRb.simulated = !assistedMode;
        }

        foreach (Collider2D c in carColliders)
        {
            if (c != null)
                c.enabled = !assistedMode;
        }
    }

    public void ResetForNewScenario()
    {
        travelStartTime = Time.time;
        parkedThisScenario = false;

        if (debugLogs)
            Debug.Log("[CarParkingInteraction] New scenario started. Travel timer reset.");
    }

    public void ResetTravelTimer()
    {
        travelStartTime = Time.time;

        if (debugLogs)
            Debug.Log("[CarParkingInteraction] Travel timer reset.");
    }

    public void OnParkingSpotClicked(ParkingSpot spot)
    {
        if (!enableClickParking || spot == null)
            return;

        if (parkedThisScenario)
        {
            if (debugLogs)
                Debug.Log("[CarParkingInteraction] Already parked in this scenario.");
            return;
        }

        if (spot.isOccupied)
        {
            if (debugLogs)
                Debug.Log($"[CarParkingInteraction] Clicked spot {spot.spotId}, but it is occupied.");
            return;
        }

        if (!allowFarSpotsForTesting && !spot.IsModeledAlternative())
        {
            if (debugLogs)
                Debug.LogWarning(
                    $"[CarParkingInteraction] Spot {spot.spotId} is Far/Not Modeled. Ignoring."
                );
            return;
        }

        if (assistedMode)
            HandleAssistedSpotSelection(spot);
        else
            HandleConventionalSpotSelection(spot);
    }

    private void HandleConventionalSpotSelection(ParkingSpot spot)
    {
        if (debugLogs)
        {
            Debug.Log(
                $"[CarParkingInteraction] CONVENTIONAL parking spot clicked: {spot.spotId}, " +
                $"alt={spot.GetAlternativeId()}, region={spot.GetRegionName()}, type={spot.parkingType}"
            );
        }

        if (routeToClickedParkingSpot)
        {
            if (carNodeFollower == null)
            {
                Debug.LogWarning(
                    "[CarParkingInteraction] No CarNodeFollower found. Parking immediately."
                );
                FinalizePark(spot);
                return;
            }

            carNodeFollower.RouteToParkingSpot(spot);
        }
        else
        {
            float distance = Vector2.Distance(transform.position, spot.transform.position);

            if (distance > maxParkDistance)
            {
                if (debugLogs)
                {
                    Debug.Log(
                        $"[CarParkingInteraction] Spot {spot.spotId} is too far. " +
                        $"Distance={distance:0.0}, max={maxParkDistance}"
                    );
                }
                return;
            }

            FinalizePark(spot);
        }
    }

    private void HandleAssistedSpotSelection(ParkingSpot spot)
    {
        if (!assistedDirectSelection)
            return;

        if (debugLogs)
        {
            Debug.Log(
                $"[CarParkingInteraction] ASSISTED selected spot directly from map: {spot.spotId}, " +
                $"alt={spot.GetAlternativeId()}, region={spot.GetRegionName()}, type={spot.parkingType}"
            );
        }

        FinalizePark(spot);
    }

    public void FinalizePark(ParkingSpot spot)
    {
        if (parkedThisScenario)
        {
            if (debugLogs)
                Debug.Log("[CarParkingInteraction] Already parked in this scenario.");
            return;
        }

        if (spot == null)
        {
            if (debugLogs)
                Debug.LogWarning("[CarParkingInteraction] FinalizePark called with null spot.");
            return;
        }

        if (spot.isOccupied)
        {
            if (debugLogs)
                Debug.Log($"[CarParkingInteraction] Spot {spot.spotId} is already occupied.");
            return;
        }

        if (!allowFarSpotsForTesting && !spot.IsModeledAlternative())
        {
            if (debugLogs)
                Debug.LogWarning(
                    $"[CarParkingInteraction] Spot {spot.spotId} is Far/Not Modeled. Ignoring."
                );
            return;
        }

        spot.MarkParkedByPlayer();
        parkedThisScenario = true;

        if (!assistedMode && snapCarToParkingSpot)
        {
            transform.position = new Vector3(
                spot.transform.position.x,
                spot.transform.position.y,
                transform.position.z
            );
        }

        float travelTime = Time.time - travelStartTime;

        // Reconnect automatically if the Inspector reference was lost.
        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (scenarioManager != null)
        {
            Debug.Log(
                "[CarParkingInteraction] Sending parked result to ScenarioManager."
            );

            scenarioManager.OnCarParked(spot, travelTime);
        }
        else
        {
            Debug.LogError(
                "[CarParkingInteraction] No ScenarioManager assigned or found. " +
                "The result panel cannot open and the survey cannot advance."
            );
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[CarParkingInteraction] PARKING CHOICE CONFIRMED! " +
                $"mode={(assistedMode ? "Assisted" : "Conventional")}, " +
                $"spot={spot.spotId}, alt={spot.GetAlternativeId()}, " +
                $"region={spot.GetRegionName()}, type={spot.parkingType}, " +
                $"selectionTime={travelTime:0.0}s"
            );
        }
    }
}