using System;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CarParkingInteraction : MonoBehaviour
{
    public static CarParkingInteraction Instance { get; private set; }

    [Header("Mode")]
    public bool assistedMode = false;

    [Header("Parking By Click/Tap - Conventional Mode")]
    public bool enableClickParking = true;

    [Tooltip("Clicking a parking spot calculates a shortest RoadNode route and drives the car to it.")]
    public bool routeToClickedParkingSpot = true;

    [Tooltip("Only used if routeToClickedParkingSpot is false.")]
    public float maxParkDistance = 25f;

    [Tooltip("If true, direct/local parking jumps visually onto the selected spot. Automatic RoadNode parking does not use this snap.")]
    public bool snapCarToParkingSpot = true;

    [Header("Observed Choice Set - Conventional Mode")]
    [Tooltip("Keep ON for data collection. Conventional participants may only select spots that ParkingViewTracker has recorded as viewed.")]
    public bool requireViewedSpotInConventional = true;

    [Tooltip("If ON, a missing ParkingViewTracker blocks conventional parking instead of silently collecting an invalid choice set.")]
    public bool blockConventionalSelectionIfTrackerMissing = true;

    [Header("Conventional Parking Confirmation")]
    [Tooltip("Recommended: ask for confirmation before the automatic route begins.")]
    public bool requireConfirmationInConventional = true;

    [Tooltip("Recommended: participant must brake, be idle, or be waiting at a node before selecting a parking spot.")]
    public bool requireCarStoppedForParkingSelection = true;

    [Tooltip("Panel shown after a parking spot is clicked.")]
    public GameObject parkingConfirmationPanel;

    [Tooltip("Main text inside ParkingConfirmationPanel.")]
    public TMP_Text parkingConfirmationText;

    [Tooltip("Find ParkingConfirmationPanel and ParkingConfirmationText automatically by GameObject name.")]
    public bool autoFindConfirmationUI = true;

    [Tooltip("Optional. Auto-bound from the button whose OnClick calls ConfirmPendingParkingSelection.")]
    public Button parkingConfirmButton;

    [Tooltip("Optional. Auto-bound from the button whose OnClick calls CancelPendingParkingSelection.")]
    public Button parkingCancelButton;

    [Tooltip("Optional TMP/RTL label inside the confirm button.")]
    public TMP_Text parkingConfirmButtonText;

    [Tooltip("Optional TMP/RTL label inside the cancel button.")]
    public TMP_Text parkingCancelButtonText;

    [Header("Confirmation Button Labels")]
    public string normalConfirmButtonLabel = "تأیید و حرکت";
    public string normalCancelButtonLabel = "انصراف";
    public string blockedContinueButtonLabel = "ادامه رانندگی";

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

    private ParkingSpot pendingConfirmationSpot;
    private ParkingSpot activeParkingTarget;
    private float pendingPreviewDrivingDistanceMeters;

    // True while the normal parking-confirmation panel is temporarily used to
    // explain that a visible map location cannot physically be parked in.
    private bool blockedParkingInfoOpen = false;
    private bool resumeDrivingAfterBlockedInfo = false;

    private CarNodeFollower carNodeFollower;
    private SpriteRenderer[] carRenderers;
    private Collider2D[] carColliders;
    private Rigidbody2D carRb;

    public bool IsParkingConfirmationOpen
    {
        get
        {
            return parkingConfirmationPanel != null &&
                   parkingConfirmationPanel.activeSelf;
        }
    }

    public ParkingSpot ActiveParkingTarget
    {
        get { return activeParkingTarget; }
    }

    private void Awake()
    {
        Instance = this;

        carNodeFollower = GetComponent<CarNodeFollower>();

        if (carNodeFollower == null)
            carNodeFollower = FindFirstObjectByType<CarNodeFollower>();

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (autoFindConfirmationUI)
            AutoBindConfirmationUI();

        carRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        carColliders = GetComponentsInChildren<Collider2D>(true);
        carRb = GetComponent<Rigidbody2D>();

        HideParkingConfirmation();
        ResetForNewScenario();

        if (scenarioManager == null)
        {
            Debug.LogError(
                "[CarParkingInteraction] ScenarioManager was not assigned and could not be found."
            );
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && autoFindConfirmationUI)
            AutoBindConfirmationUI();
    }
#endif

    public void SetAssistedMode(bool assisted)
    {
        assistedMode = assisted;

        CancelPendingParkingSelection();
        activeParkingTarget = null;

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
            foreach (SpriteRenderer renderer in carRenderers)
            {
                if (renderer != null)
                    renderer.enabled = !assistedMode;
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

        foreach (Collider2D collider in carColliders)
        {
            if (collider != null)
                collider.enabled = !assistedMode;
        }
    }

    public void ResetForNewScenario()
    {
        travelStartTime = Time.time;
        parkedThisScenario = false;
        pendingConfirmationSpot = null;
        activeParkingTarget = null;
        pendingPreviewDrivingDistanceMeters = 0f;
        blockedParkingInfoOpen = false;
        resumeDrivingAfterBlockedInfo = false;

        HideParkingConfirmation();

        if (carNodeFollower != null)
            carNodeFollower.ResetDrivingMetrics();

        if (debugLogs)
            Debug.Log("[CarParkingInteraction] New scenario started. Driving metrics reset.");
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

        if (IsParkingConfirmationOpen)
        {
            // A previous spot is still pending confirmation. Rather than
            // dropping this click, let the newest selection supersede it so
            // the car always ends up targeting the latest spot the
            // participant tapped.
            if (debugLogs)
            {
                Debug.Log(
                    "[CarParkingInteraction] New spot clicked while a confirmation " +
                    "was open. Cancelling the previous pending selection in favor " +
                    "of the latest one."
                );
            }

            CancelPendingParkingSelection();
        }

        // Physical obstructions (trash bins, construction, barriers, etc.)
        // remain visible and clickable for explanation, but can never become a
        // parking choice.
        if (spot.IsPhysicallyBlockedParking())
        {
            ShowPhysicallyBlockedParkingInfo(spot);
            return;
        }

        if (spot.isOccupied)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[CarParkingInteraction] Clicked spot {spot.spotId}, but it is occupied."
                );
            }

            return;
        }

        if (!allowFarSpotsForTesting && !spot.IsModeledAlternative())
        {
            if (debugLogs)
            {
                Debug.LogWarning(
                    $"[CarParkingInteraction] Spot {spot.spotId} is outside the modeled alternatives."
                );
            }

            return;
        }

        // In the conventional condition, the behavioral choice set must match
        // the experienced/observed choice set. Assisted mode is full-information
        // and therefore does not use this restriction.
        bool isPractice =
            scenarioManager != null &&
            scenarioManager.isPracticeRound;

        if (!assistedMode &&
            requireViewedSpotInConventional &&
            !isPractice)
        {
            ParkingViewTracker tracker = ParkingViewTracker.Instance;

            if (tracker == null)
            {
                Debug.LogError(
                    "[CarParkingInteraction] ParkingViewTracker is missing. " +
                    "The conventional observed choice set cannot be enforced."
                );

                if (blockConventionalSelectionIfTrackerMissing)
                    return;
            }
            else
            {
                // A click/tap on a physical ParkingSpot is direct evidence that
                // the participant could see it on screen. Mark it Viewed
                // immediately before enforcing the main-study choice-set rule.
                bool registeredFromClick =
                    tracker.MarkViewedFromClick(spot);

                if (!registeredFromClick && !tracker.HasViewed(spot))
                {
                    if (debugLogs)
                    {
                        Debug.Log(
                            $"[CarParkingInteraction] Conventional spot {spot.spotId} " +
                            "cannot be selected because the view-tracking round is not active."
                        );
                    }

                    return;
                }
            }
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

        if (!routeToClickedParkingSpot)
        {
            float distance = Vector2.Distance(
                transform.position,
                spot.transform.position
            );

            if (distance > maxParkDistance)
            {
                if (debugLogs)
                {
                    Debug.Log(
                        $"[CarParkingInteraction] Spot {spot.spotId} is too far for direct parking. " +
                        $"Distance={distance:0.0}, max={maxParkDistance}"
                    );
                }

                return;
            }

            FinalizePark(spot, carAlreadyAtParkingStop: false);
            return;
        }

        if (carNodeFollower == null)
        {
            Debug.LogWarning(
                "[CarParkingInteraction] No CarNodeFollower found. Parking cannot be routed."
            );

            return;
        }

        if (requireCarStoppedForParkingSelection &&
            !carNodeFollower.CanReselectMidDrive())
        {
            // Ordinary mid-edge driving can still be rerouted to a newly
            // selected parking spot. Selection is blocked only during an undo
            // or during the short manual U-turn curve, where changing routes
            // would leave the car between the two directional paths.
            Debug.LogWarning(
                "[CarParkingInteraction] Cannot select a parking spot while the car is undoing or performing a manual U-turn."
            );

            return;
        }

        if (!carNodeFollower.TryGetParkingRoutePreview(
                spot,
                out float previewDistanceMeters,
                out string failureReason))
        {
            Debug.LogWarning(
                $"[CarParkingInteraction] No valid driving route to {spot.spotId}. " +
                failureReason
            );

            return;
        }

        if (requireConfirmationInConventional)
        {
            ShowParkingConfirmation(
                spot,
                previewDistanceMeters
            );
        }
        else
        {
            StartConfirmedParkingRoute(spot);
        }
    }

    private void ShowPhysicallyBlockedParkingInfo(ParkingSpot spot)
    {
        if (spot == null)
            return;

        pendingConfirmationSpot = null;
        pendingPreviewDrivingDistanceMeters = 0f;

        // Pause an actively moving conventional car while the explanation is
        // open. If it was already stopped, do not force it to move afterwards.
        resumeDrivingAfterBlockedInfo =
            !assistedMode &&
            carNodeFollower != null &&
            !carNodeFollower.IsBraking;

        if (!assistedMode && carNodeFollower != null)
            carNodeFollower.UI_StopButton();

        if (autoFindConfirmationUI)
            AutoBindConfirmationUI();

        if (parkingConfirmationPanel == null)
        {
            Debug.LogWarning(
                "[CarParkingInteraction] ParkingConfirmationPanel is missing. " +
                "Cannot show the physically-blocked parking explanation."
            );
            return;
        }

        blockedParkingInfoOpen = true;

        parkingConfirmationPanel.SetActive(true);
        parkingConfirmationPanel.transform.SetAsLastSibling();

        ConfigureBlockedParkingButtons();

        if (parkingConfirmationText != null)
        {
            StringBuilder message = new StringBuilder();
            message.AppendLine(
                spot.GetPhysicallyBlockedParkingMessagePersian()
            );
            message.AppendLine();
            message.AppendLine(
                "برای ادامه مسیر، «ادامه رانندگی» را انتخاب کنید."
            );

            parkingConfirmationText.text =
                message.ToString().TrimEnd();

            parkingConfirmationText.SetVerticesDirty();
            parkingConfirmationText.SetLayoutDirty();

            if (parkingConfirmationText.gameObject.activeInHierarchy)
                parkingConfirmationText.ForceMeshUpdate(true, true);
        }

        Canvas.ForceUpdateCanvases();

        if (debugLogs)
        {
            Debug.Log(
                $"[CarParkingInteraction] Physically blocked parking clicked | " +
                $"spot={spot.spotId}"
            );
        }
    }

    private void ShowParkingConfirmation(
        ParkingSpot spot,
        float previewDistanceMeters)
    {
        blockedParkingInfoOpen = false;
        resumeDrivingAfterBlockedInfo = false;
        ConfigureNormalConfirmationButtons();

        pendingConfirmationSpot = spot;
        pendingPreviewDrivingDistanceMeters = previewDistanceMeters;

        if (carNodeFollower != null)
            carNodeFollower.UI_StopButton();

        if (autoFindConfirmationUI)
            AutoBindConfirmationUI();

        if (parkingConfirmationPanel == null)
        {
            Debug.LogWarning(
                "[CarParkingInteraction] ParkingConfirmationPanel is missing. " +
                "The confirmed route will start immediately."
            );

            StartConfirmedParkingRoute(spot);
            return;
        }

        parkingConfirmationPanel.SetActive(true);
        parkingConfirmationPanel.transform.SetAsLastSibling();

        string parkingTypeText =
            spot.parkingType == ParkingType.Legal
                ? "قانونی"
                : "غیرمجاز";

        StringBuilder message = new StringBuilder();
        message.AppendLine("آیا می‌خواهید این جایگاه را انتخاب کنید؟");
        message.AppendLine();
        message.AppendLine("نوع پارک: " + parkingTypeText);
        message.AppendLine(
            "مسافت رانندگی تا جایگاه: " +
            FormatNumber(previewDistanceMeters) +
            " متر"
        );
        message.AppendLine(
            "مسافت پیاده‌روی تا درب شماره ۲ (درب ملت): " +
            FormatNumber(spot.walkingDistanceMeters) +
            " متر"
        );

        ParkingScenario activeScenario = scenarioManager != null
            ? scenarioManager.GetCurrentScenario()
            : null;

        if (spot.parkingType == ParkingType.Legal)
        {
            if (ParkingPolicy1405.IsSmartParking(spot))
            {
                int feeToman = activeScenario != null
                    ? ParkingPolicy1405.CalculateSmartParkingFeeToman(activeScenario.dwellTimeMinutes)
                    : 0;

                message.AppendLine("ویژگی: پارکینگ هوشمند");
                message.AppendLine("هزینه پارک برای این مدت: " + FormatToman(feeToman));
            }
            else
            {
                message.AppendLine("هزینه پارک: رایگان");
            }
        }
        else if (activeScenario != null)
        {
            // Intersection-corner and pedestrian-crossing parking are illegal
            // but intentionally have no monetary fine in this study.
            bool noFinancialFine =
                ParkingPolicy1405.IsNoFinancialFineIllegalSpot(spot);

            if (noFinancialFine)
            {
                if (spot.illegalReason == IllegalReason.PedestrianCrossing)
                {
                    message.AppendLine(
                        "جریمه مالی: ندارد اما باعث سلب آرامش عابران پیاده می‌شود."
                    );
                }
                else if (spot.illegalReason == IllegalReason.IntersectionCorner)
                {
                    message.AppendLine(
                        "جریمه مالی: ندارد اما باعث سلب آرامش رانندگان می‌شود و ریسک تصادف بالایی دارد."
                    );
                }
            }
            else
            {
                float adjustedCitationProbability =
                    ParkingScoreCalculator.CalculateAdjustedCitationProbability(
                        activeScenario.citationProbability,
                        activeScenario.dwellTimeMinutes
                    );

                message.AppendLine(
                    "احتمال جریمه طی مدت پارک: " +
                    FormatPercent(adjustedCitationProbability)
                );

                // Use the same centralized policy method as the scoring system.
                // This handles NoParkingSign, NoStoppingSign, DrivewayEntrance,
                // BusStop and any other fined illegal reason without duplicating
                // IllegalReason enum names here.
                int applicableFineToman =
                    ParkingPolicy1405.GetAppliedIllegalFineToman(
                        spot,
                        activeScenario.GetIllegalFineMultiplier()
                    );

                message.AppendLine(
                    "مبلغ جریمه: " + FormatToman(applicableFineToman)
                );

                if (spot.illegalReason == IllegalReason.DrivewayEntrance)
                {
                    message.AppendLine(
                        "مالک یا استفاده‌کننده پارکینگ می‌تواند برای این تخلف شکایت و پیگیری کند."
                    );
                }
            }
        }

        if (parkingConfirmationText != null)
        {
            parkingConfirmationText.text =
                message.ToString().TrimEnd();

            parkingConfirmationText.SetVerticesDirty();
            parkingConfirmationText.SetLayoutDirty();

            if (parkingConfirmationText.gameObject.activeInHierarchy)
                parkingConfirmationText.ForceMeshUpdate(true, true);
        }

        Canvas.ForceUpdateCanvases();

        if (debugLogs)
        {
            Debug.Log(
                $"[CarParkingInteraction] Confirmation opened | " +
                $"spot={spot.spotId}, driving={previewDistanceMeters:0.0} m, " +
                $"walking={spot.walkingDistanceMeters:0.0} m"
            );
        }
    }

    public void ConfirmPendingParkingSelection()
    {
        // The confirm button is hidden for a physically blocked location, but
        // this guard also prevents stale/manual UI events from confirming it.
        if (blockedParkingInfoOpen)
            return;

        if (pendingConfirmationSpot == null)
        {
            HideParkingConfirmation();
            return;
        }

        ParkingSpot confirmedSpot = pendingConfirmationSpot;

        pendingConfirmationSpot = null;
        pendingPreviewDrivingDistanceMeters = 0f;
        HideParkingConfirmation();

        StartConfirmedParkingRoute(confirmedSpot);
    }

    public void CancelPendingParkingSelection()
    {
        bool wasBlockedInfo = blockedParkingInfoOpen;
        bool shouldResumeDriving =
            blockedParkingInfoOpen &&
            resumeDrivingAfterBlockedInfo;

        pendingConfirmationSpot = null;
        pendingPreviewDrivingDistanceMeters = 0f;
        blockedParkingInfoOpen = false;
        resumeDrivingAfterBlockedInfo = false;
        HideParkingConfirmation();

        if (shouldResumeDriving && carNodeFollower != null)
            carNodeFollower.UI_MoveButton();

        if (debugLogs)
        {
            Debug.Log(
                wasBlockedInfo
                    ? "[CarParkingInteraction] Blocked-place information closed. Continuing driving state."
                    : "[CarParkingInteraction] Parking selection cancelled."
            );
        }
    }

    private void StartConfirmedParkingRoute(ParkingSpot spot)
    {
        if (spot == null || carNodeFollower == null)
            return;

        activeParkingTarget = spot;

        bool started = carNodeFollower.RouteToParkingSpot(spot);

        if (!started)
        {
            activeParkingTarget = null;

            Debug.LogWarning(
                $"[CarParkingInteraction] The route to {spot.spotId} could not be started."
            );
        }
    }

    public void OnConfirmedParkingRouteArrived(ParkingSpot spot)
    {
        if (spot == null)
            return;

        if (activeParkingTarget != null &&
            activeParkingTarget != spot)
        {
            Debug.LogWarning(
                "[CarParkingInteraction] Arrived parking spot does not match the active target."
            );
        }

        activeParkingTarget = spot;
        FinalizePark(spot, carAlreadyAtParkingStop: true);
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

        activeParkingTarget = spot;
        FinalizePark(spot, carAlreadyAtParkingStop: false);
    }

    public void FinalizePark(
        ParkingSpot spot,
        bool carAlreadyAtParkingStop = false)
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

        if (spot.IsPhysicallyBlockedParking())
        {
            if (debugLogs)
            {
                Debug.LogWarning(
                    $"[CarParkingInteraction] FinalizePark rejected physically blocked spot {spot.spotId}."
                );
            }

            activeParkingTarget = null;
            return;
        }

        if (spot.isOccupied)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[CarParkingInteraction] Spot {spot.spotId} became occupied before parking."
                );
            }

            activeParkingTarget = null;
            return;
        }

        if (!allowFarSpotsForTesting && !spot.IsModeledAlternative())
        {
            if (debugLogs)
            {
                Debug.LogWarning(
                    $"[CarParkingInteraction] Spot {spot.spotId} is outside the modeled alternatives."
                );
            }

            activeParkingTarget = null;
            return;
        }

        spot.MarkParkedByPlayer();
        parkedThisScenario = true;

        if (!assistedMode &&
            !carAlreadyAtParkingStop &&
            snapCarToParkingSpot)
        {
            if (carNodeFollower != null)
                carNodeFollower.SuppressNextDistanceSample();

            transform.position = new Vector3(
                spot.transform.position.x,
                spot.transform.position.y,
                transform.position.z
            );

            if (carNodeFollower != null)
                carNodeFollower.SuppressNextDistanceSample();
        }

        float assistedSelectionTimeSeconds =
            Mathf.Max(0f, Time.time - travelStartTime);

        float drivingDistanceMeters =
            !assistedMode && carNodeFollower != null
                ? carNodeFollower.DrivingDistanceMeters
                : 0f;

        float drivingTimeSeconds =
            !assistedMode && carNodeFollower != null
                ? carNodeFollower.DrivingTimeSeconds
                : 0f;

        float movementOrSelectionTimeSeconds =
            assistedMode
                ? assistedSelectionTimeSeconds
                : drivingTimeSeconds;

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (scenarioManager != null)
        {
            scenarioManager.OnCarParked(
                spot,
                movementOrSelectionTimeSeconds,
                drivingDistanceMeters,
                drivingTimeSeconds
            );
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
                $"drivingDistance={drivingDistanceMeters:0.0}m, " +
                $"drivingTime={drivingTimeSeconds:0.0}s, " +
                $"selectionTime={movementOrSelectionTimeSeconds:0.0}s"
            );
        }

        activeParkingTarget = null;
        pendingConfirmationSpot = null;
        HideParkingConfirmation();
    }

    public float GetDrivingDistanceMeters()
    {
        return carNodeFollower != null
            ? carNodeFollower.DrivingDistanceMeters
            : 0f;
    }

    public float GetDrivingTimeSeconds()
    {
        return carNodeFollower != null
            ? carNodeFollower.DrivingTimeSeconds
            : 0f;
    }

    public float GetRemainingDrivingDistanceMeters()
    {
        return carNodeFollower != null
            ? carNodeFollower.GetRemainingRouteDistanceMeters()
            : 0f;
    }

    private void HideParkingConfirmation()
    {
        ConfigureNormalConfirmationButtons();

        if (parkingConfirmationPanel != null)
            parkingConfirmationPanel.SetActive(false);
    }

    private void ConfigureNormalConfirmationButtons()
    {
        if (parkingConfirmButton != null)
        {
            parkingConfirmButton.gameObject.SetActive(true);
            parkingConfirmButton.interactable = true;
        }

        if (parkingCancelButton != null)
        {
            parkingCancelButton.gameObject.SetActive(true);
            parkingCancelButton.interactable = true;
        }

        SetButtonLabel(
            parkingConfirmButton,
            ref parkingConfirmButtonText,
            normalConfirmButtonLabel
        );

        SetButtonLabel(
            parkingCancelButton,
            ref parkingCancelButtonText,
            normalCancelButtonLabel
        );
    }

    private void ConfigureBlockedParkingButtons()
    {
        // For a physically impossible parking location there is no selectable
        // parking action. Only the existing cancel button remains, relabelled
        // as "continue driving".
        if (parkingConfirmButton != null)
        {
            parkingConfirmButton.interactable = false;
            parkingConfirmButton.gameObject.SetActive(false);
        }

        if (parkingCancelButton != null)
        {
            parkingCancelButton.gameObject.SetActive(true);
            parkingCancelButton.interactable = true;
        }

        SetButtonLabel(
            parkingCancelButton,
            ref parkingCancelButtonText,
            blockedContinueButtonLabel
        );
    }

    private static void SetButtonLabel(
        Button button,
        ref TMP_Text cachedLabel,
        string value)
    {
        if (button == null)
            return;

        if (cachedLabel == null)
            cachedLabel = button.GetComponentInChildren<TMP_Text>(true);

        if (cachedLabel == null)
            return;

        cachedLabel.text = value ?? string.Empty;
        cachedLabel.SetVerticesDirty();
        cachedLabel.SetLayoutDirty();

        if (cachedLabel.gameObject.activeInHierarchy)
            cachedLabel.ForceMeshUpdate(true, true);
    }

    [ContextMenu("Auto Bind Confirmation UI")]
    public void AutoBindConfirmationUI()
    {
        if (parkingConfirmationPanel == null)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (Transform candidate in allTransforms)
            {
                if (candidate == null)
                    continue;

                if (string.Equals(
                    candidate.gameObject.name,
                    "ParkingConfirmationPanel",
                    StringComparison.OrdinalIgnoreCase))
                {
                    parkingConfirmationPanel = candidate.gameObject;
                    break;
                }
            }
        }

        if (parkingConfirmationText == null)
        {
            TMP_Text[] allTexts = FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (TMP_Text candidate in allTexts)
            {
                if (candidate == null)
                    continue;

                if (string.Equals(
                    candidate.gameObject.name,
                    "ParkingConfirmationText",
                    StringComparison.OrdinalIgnoreCase))
                {
                    parkingConfirmationText = candidate;
                    break;
                }
            }
        }

        if (parkingConfirmationPanel != null &&
            (parkingConfirmButton == null || parkingCancelButton == null))
        {
            Button[] buttons =
                parkingConfirmationPanel.GetComponentsInChildren<Button>(true);

            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;

                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    string method =
                        button.onClick.GetPersistentMethodName(i);

                    if (parkingConfirmButton == null &&
                        string.Equals(
                            method,
                            "ConfirmPendingParkingSelection",
                            StringComparison.Ordinal))
                    {
                        parkingConfirmButton = button;
                    }

                    if (parkingCancelButton == null &&
                        string.Equals(
                            method,
                            "CancelPendingParkingSelection",
                            StringComparison.Ordinal))
                    {
                        parkingCancelButton = button;
                    }
                }
            }

            // Fallback to common GameObject names if persistent UnityEvent
            // method names are not available.
            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;

                string objectName = button.gameObject.name ?? string.Empty;

                if (parkingConfirmButton == null &&
                    objectName.IndexOf(
                        "confirm",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    parkingConfirmButton = button;
                }

                if (parkingCancelButton == null &&
                    (objectName.IndexOf(
                         "cancel",
                         StringComparison.OrdinalIgnoreCase) >= 0 ||
                     objectName.IndexOf(
                         "back",
                         StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    parkingCancelButton = button;
                }
            }
        }

        if (parkingConfirmButton != null &&
            parkingConfirmButtonText == null)
        {
            parkingConfirmButtonText =
                parkingConfirmButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (parkingCancelButton != null &&
            parkingCancelButtonText == null)
        {
            parkingCancelButtonText =
                parkingCancelButton.GetComponentInChildren<TMP_Text>(true);
        }
    }

    private static string FormatToman(float value)
    {
        return ParkingPolicy1405.FormatToman(value);
    }

    private static string FormatPercent(float zeroToOneValue)
    {
        return ParkingPolicy1405.FormatPercent(zeroToOneValue);
    }

    private static string FormatNumber(float value)
    {
        return ParkingPolicy1405.FormatDecimal(value, "0.#");
    }

    private static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        char[] western =
        {
            '0', '1', '2', '3', '4',
            '5', '6', '7', '8', '9'
        };

        char[] persian =
        {
            '۰', '۱', '۲', '۳', '۴',
            '۵', '۶', '۷', '۸', '۹'
        };

        StringBuilder builder = new StringBuilder(input.Length);

        foreach (char character in input)
        {
            int index = Array.IndexOf(western, character);
            builder.Append(index >= 0 ? persian[index] : character);
        }

        return builder.ToString();
    }
}