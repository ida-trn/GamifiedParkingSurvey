using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Tracks the experienced/observed parking choice set for CONVENTIONAL rounds only.
///
/// CONVENTIONAL:
/// A modeled ParkingSpot becomes Viewed only when it is BOTH:
/// 1) inside the gameplay camera viewport, and
/// 2) within the configured car-to-spot radius (default 100 m).
///
/// Viewed state is now kept IN MEMORY only because it is still needed to
/// enforce Conventional-mode parking selection.
///
/// No spot-by-spot parking_spot_views rows are written anymore. ParkingLogger
/// stores one compact RoadNode route summary per Conventional round instead.
/// That route + the one-time static parking master can reconstruct exposure
/// later using any chosen radius (50 m, 75 m, 100 m, etc.).
///
/// ASSISTED:
/// View tracking remains disabled because the assisted condition is treated as
/// full-information.
/// </summary>
public class ParkingViewTracker : MonoBehaviour
{
    public static ParkingViewTracker Instance { get; private set; }

    [Header("References")]
    [Tooltip("Assign the participant car. ScenarioManager also passes this automatically.")]
    public Transform carTransform;

    [Tooltip("Camera used for conventional driving. If empty, Camera.main is used.")]
    public Camera worldCamera;

    [Tooltip("Optional. Used to record distance already driven when a spot is first viewed.")]
    public CarParkingInteraction carParking;

    [Header("Conventional observation rule")]
    [Tooltip("Measured project scale. Used to convert Unity-space car-to-spot distance into meters.")]
    [Min(0.000001f)]
    public float metersPerUnityUnit = 0.2424f;

    [Tooltip("A conventional spot must be inside the active gameplay camera viewport to count as Viewed.")]
    public bool useCameraViewportForViewed = true;

    [Tooltip("A conventional spot must also be within this car-to-spot distance to count as Viewed.")]
    public bool useMaxViewDistanceForViewed = true;

    [Tooltip("Maximum car-to-spot distance, in meters, for a conventional spot to count as Viewed. Recommended value for this study: 100 m.")]
    [Min(0f)]
    public float maxViewDistanceMeters = 100f;

    [Tooltip("Extra viewport margin. 0 means strictly inside the camera image; 0.02 allows a small tolerance at the edges.")]
    [Range(0f, 0.25f)]
    public float viewportMargin = 0.02f;

    [Header("Output")]
    [Tooltip("Kept only for backwards Inspector compatibility. Spot-view rows are no longer written; route summaries are stored by ParkingLogger.")]
    [HideInInspector]
    public bool writeSpotViewCsv = false;

    [HideInInspector]
    public string outputSubfolder = "ParkingSurveyData";

    [Header("Debug")]
    public bool debugLogs = true;

    public bool RoundActive => roundActive;
    public bool CurrentAssistedMode => currentAssistedMode;
    public string ViewFilePath => string.Empty;

    private readonly CultureInfo invariant = CultureInfo.InvariantCulture;

    private bool roundActive;
    private bool currentAssistedMode;
    private float roundStartTime;

    private string participantId;
    private string studyVersion;
    private string buildVersion;
    private string sessionId;
    private string roundStartUtc;

    private int taskIndex;
    private int scenarioIndex;
    private string scenarioId;

    private string viewFilePath;

    private readonly Dictionary<ParkingSpot, SpotViewState> states =
        new Dictionary<ParkingSpot, SpotViewState>();

    private const string Header =
        "ParticipantId,SessionId,StudyVersion,BuildVersion,RoundStartUtc,RoundEndUtc," +
        "TaskIndex,ScenarioIndex,ScenarioId,Mode," +
        "SpotId,Alternative6,Alternative5,Region,ParkingType,Street,StreetSide," +
        "OccupiedAtStart,ObjectiveAvailable,Viewed,ViewedAvailable," +
        "FirstViewedElapsedSec,FirstViewedCarDistanceM,DrivingDistanceAtFirstViewM," +
        "Chosen,ViewedRule";

    private sealed class SpotViewState
    {
        public ParkingSpot spot;
        public string spotId;
        public string alternative6;
        public string alternative5;
        public string region;
        public string parkingType;
        public string street;
        public string streetSide;

        public bool occupiedAtStart;

        public bool viewed;
        public float firstViewedElapsedSec = -1f;
        public float firstViewedCarDistanceM = -1f;
        public float drivingDistanceAtFirstViewM = -1f;

    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Storage-optimized study design: keep view state only in memory.
        // SupabaseSurveyBridge sees an empty ViewFilePath and therefore has
        // no spot-view CSV to upload.
        writeSpotViewCsv = false;
        viewFilePath = string.Empty;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!roundActive || currentAssistedMode)
            return;

        ScanVisibleSpotsNow();
    }

    private void ScanVisibleSpotsNow()
    {
        Camera cameraToUse = worldCamera != null ? worldCamera : Camera.main;

        foreach (KeyValuePair<ParkingSpot, SpotViewState> pair in states)
        {
            ParkingSpot spot = pair.Key;
            SpotViewState state = pair.Value;

            if (spot == null || state == null || state.viewed)
                continue;

            float distanceMeters = GetCarToSpotDistanceMeters(spot);

            // Distance is checked first so far-away spots do not need the more
            // expensive camera-bounds visibility test.
            if (!IsWithinConfiguredViewRadius(distanceMeters))
                continue;

            if (useCameraViewportForViewed &&
                !IsSpotVisibleInCamera(cameraToUse, spot))
            {
                continue;
            }

            MarkViewedInternal(
                state,
                distanceMeters,
                GetDrivingDistanceMeters()
            );
        }
    }

    /// <summary>
    /// Called once after ParkingLogger.BeginRound so the main SessionId already exists.
    /// </summary>
    public void BeginRound(
        string participantIdValue,
        string studyVersionValue,
        string buildVersionValue,
        int taskIndexValue,
        int scenarioIndexValue,
        ParkingScenario scenario,
        bool assistedMode,
        Transform car,
        float metersPerUnityUnitValue,
        CarParkingInteraction carParkingInteraction)
    {
        if (scenario == null)
        {
            Debug.LogWarning("[ParkingViewTracker] BeginRound ignored because scenario is null.");
            return;
        }

        // Protect against an accidentally unfinished previous round.
        if (roundActive)
        {
            Debug.LogWarning(
                "[ParkingViewTracker] A new round started before the previous view round was completed. " +
                "The previous in-memory view state is being discarded."
            );
        }

        states.Clear();

        participantId = string.IsNullOrWhiteSpace(participantIdValue)
            ? "UNKNOWN"
            : participantIdValue.Trim();

        studyVersion = string.IsNullOrWhiteSpace(studyVersionValue)
            ? string.Empty
            : studyVersionValue.Trim();

        buildVersion = string.IsNullOrWhiteSpace(buildVersionValue)
            ? Application.version
            : buildVersionValue.Trim();

        taskIndex = taskIndexValue;
        scenarioIndex = scenarioIndexValue;
        scenarioId = scenario.scenarioId ?? string.Empty;
        currentAssistedMode = assistedMode;

        carTransform = car != null ? car : carTransform;
        carParking = carParkingInteraction != null ? carParkingInteraction : carParking;
        metersPerUnityUnit = Mathf.Max(0.000001f, metersPerUnityUnitValue);

        roundStartTime = Time.time;
        roundStartUtc = DateTime.UtcNow.ToString("o", invariant);

        sessionId =
            ParkingLogger.Instance != null &&
            !string.IsNullOrWhiteSpace(ParkingLogger.Instance.SessionId)
                ? ParkingLogger.Instance.SessionId
                : "NO_MAIN_LOGGER_SESSION";

        // Assisted mode is intentionally not view-tracked. The assisted map is
        // treated as full-information, so no parking_spot_views rows are needed.
        if (currentAssistedMode)
        {
            roundActive = false;
            states.Clear();

            if (debugLogs)
            {
                Debug.Log(
                    $"[ParkingViewTracker] Assisted round ignored for view logging | " +
                    $"scenario={scenarioId}, taskIndex={taskIndex}"
                );
            }

            return;
        }

        ParkingSpot[] allSpots =
            FindObjectsByType<ParkingSpot>(FindObjectsSortMode.None);

        foreach (ParkingSpot spot in allSpots)
        {
            if (spot == null)
                continue;

            string alternative6 = spot.GetAlternativeId();

            if (!IsModeledAlternative(alternative6))
                continue;

            SpotViewState state = CreateStateForSpot(spot, alternative6);
            states[spot] = state;
        }

        roundActive = true;

        // Capture any valid spots already visible at the exact start of the
        // conventional round.
        ScanVisibleSpotsNow();

        if (debugLogs)
        {
            Debug.Log(
                $"[ParkingViewTracker] Round started | scenario={scenarioId}, " +
                $"mode=Conventional, modeledSpots={states.Count}, " +
                $"viewedRule={GetViewedRuleLabel()}"
            );
        }
    }

    public bool HasViewed(ParkingSpot spot)
    {
        if (spot == null)
            return false;

        // Assisted mode is full-information and is not logged by this tracker.
        if (currentAssistedMode)
            return true;

        if (!states.TryGetValue(spot, out SpotViewState state) ||
            state == null)
        {
            return false;
        }

        if (state.viewed)
            return true;

        // Important: check the camera immediately when the participant clicks.
        // This prevents a visible spot from being rejected just because Update()
        // has not marked it yet in the current frame.
        Camera cameraToUse = worldCamera != null ? worldCamera : Camera.main;

        float distanceMeters = GetCarToSpotDistanceMeters(spot);

        if (!IsWithinConfiguredViewRadius(distanceMeters))
            return false;

        if (useCameraViewportForViewed &&
            !IsSpotVisibleInCamera(cameraToUse, spot))
        {
            return false;
        }

        MarkViewedInternal(
            state,
            distanceMeters,
            GetDrivingDistanceMeters()
        );

        return true;
    }

    public bool IsTracked(ParkingSpot spot)
    {
        return spot != null && states.ContainsKey(spot);
    }

    /// <summary>
    /// A click/tap on a ParkingSpot is direct evidence that the participant
    /// could see that spot on screen, but the spot still has to be within the
    /// configured conventional view radius before it can count as Viewed.
    ///
    /// It also repairs a missing dictionary entry if a modeled spot was added
    /// after BeginRound or was skipped unexpectedly.
    /// </summary>
    public bool MarkViewedFromClick(ParkingSpot spot)
    {
        if (!roundActive || spot == null)
            return false;

        if (currentAssistedMode)
            return true;

        if (!states.TryGetValue(spot, out SpotViewState state) || state == null)
        {
            string alternative6 = spot.GetAlternativeId();

            if (!IsModeledAlternative(alternative6))
                return false;

            state = CreateStateForSpot(spot, alternative6);
            states[spot] = state;

            if (debugLogs)
            {
                Debug.LogWarning(
                    $"[ParkingViewTracker] Spot {spot.spotId} was not in the " +
                    "round dictionary, so it was registered dynamically from a click."
                );
            }
        }

        if (state.viewed)
            return true;

        float distanceMeters = GetCarToSpotDistanceMeters(spot);

        // A click proves on-screen exposure, but it does NOT override the
        // study's 100 m conventional observation radius.
        if (!IsWithinConfiguredViewRadius(distanceMeters))
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[ParkingViewTracker] Clicked spot not counted as Viewed because " +
                    $"it is outside the view radius | spot={spot.spotId}, " +
                    $"carDist={distanceMeters:0.0}m, max={maxViewDistanceMeters:0.0}m"
                );
            }

            return false;
        }

        Camera cameraToUse = worldCamera != null ? worldCamera : Camera.main;

        if (useCameraViewportForViewed &&
            !IsSpotVisibleInCamera(cameraToUse, spot))
        {
            return false;
        }

        MarkViewedInternal(
            state,
            distanceMeters,
            GetDrivingDistanceMeters()
        );

        return true;
    }

    /// <summary>
    /// Optional explicit mark. Useful if another visibility system knows with
    /// certainty that a spot has been revealed to the participant.
    /// </summary>
    public void MarkViewed(ParkingSpot spot)
    {
        if (!roundActive || spot == null)
            return;

        if (!states.TryGetValue(spot, out SpotViewState state) || state == null)
            return;

        if (state.viewed)
            return;

        if (currentAssistedMode)
            return;

        float distanceMeters = GetCarToSpotDistanceMeters(spot);

        if (!IsWithinConfiguredViewRadius(distanceMeters))
            return;

        Camera cameraToUse = worldCamera != null ? worldCamera : Camera.main;

        if (useCameraViewportForViewed &&
            !IsSpotVisibleInCamera(cameraToUse, spot))
        {
            return;
        }

        MarkViewedInternal(
            state,
            distanceMeters,
            GetDrivingDistanceMeters()
        );
    }

    public void CompleteRound(ParkingSpot chosenSpot, bool timedOut)
    {
        if (!roundActive)
            return;

        // No CSV / Supabase rows are written here anymore.
        // The in-memory viewed state still served its gameplay purpose during
        // the round. ParkingLogger stores the compact Conventional RoadNode
        // route, from which viewed/encountered spots can be reconstructed later.
        if (debugLogs)
        {
            int viewedCount = 0;
            int viewedAvailableCount = 0;

            foreach (SpotViewState state in states.Values)
            {
                if (state == null)
                    continue;

                if (state.viewed)
                    viewedCount++;

                if (state.viewed && !state.occupiedAtStart)
                    viewedAvailableCount++;
            }

            Debug.Log(
                $"[ParkingViewTracker] Round completed | scenario={scenarioId}, " +
                $"mode=Conventional, viewedInMemory={viewedCount}, " +
                $"viewedAvailableInMemory={viewedAvailableCount}, " +
                $"spotViewRowsSaved=0, timedOut={timedOut}, " +
                $"rule={GetViewedRuleLabel()}"
            );
        }

        roundActive = false;
        states.Clear();
    }

    private SpotViewState CreateStateForSpot(
        ParkingSpot spot,
        string alternative6)
    {
        return new SpotViewState
        {
            spot = spot,
            spotId = spot != null ? (spot.spotId ?? string.Empty) : string.Empty,
            alternative6 = alternative6 ?? string.Empty,
            alternative5 = alternative6 == "A6" ? "A5" : (alternative6 ?? string.Empty),
            region = spot != null ? spot.GetRegionName() : string.Empty,
            parkingType = spot != null ? spot.GetParkingTypeName() : string.Empty,
            street = spot != null ? (spot.streetName ?? string.Empty) : string.Empty,
            streetSide = spot != null ? spot.GetStreetSideName() : string.Empty,
            occupiedAtStart = spot != null && spot.isOccupied
        };
    }

    private void MarkViewedInternal(
        SpotViewState state,
        float carDistanceMeters,
        float drivingDistanceMeters)
    {
        if (state == null || state.viewed)
            return;

        state.viewed = true;
        state.firstViewedElapsedSec =
            Mathf.Max(0f, Time.time - roundStartTime);

        state.firstViewedCarDistanceM =
            carDistanceMeters >= 0f
                ? carDistanceMeters
                : -1f;

        state.drivingDistanceAtFirstViewM =
            drivingDistanceMeters >= 0f
                ? drivingDistanceMeters
                : -1f;

        if (debugLogs)
        {
            Debug.Log(
                $"[ParkingViewTracker] VIEWED | spot={state.spotId}, " +
                $"alt={state.alternative6}, t={state.firstViewedElapsedSec:0.00}s, " +
                $"carDist={state.firstViewedCarDistanceM:0.0}m, " +
                $"driven={state.drivingDistanceAtFirstViewM:0.0}m"
            );
        }
    }

    private bool IsSpotVisibleInCamera(
        Camera cameraToUse,
        ParkingSpot spot)
    {
        if (spot == null)
            return false;

        if (cameraToUse == null)
        {
            if (debugLogs)
            {
                Debug.LogWarning(
                    "[ParkingViewTracker] No World Camera and no Camera.main were found. " +
                    "Camera-based passive view tracking cannot be evaluated."
                );
            }

            return false;
        }

        // Check the ParkingSpot root first.
        if (IsPointInsideCameraViewport(cameraToUse, spot.transform.position))
            return true;

        // Then check visible/rendered bounds. This is more reliable when the
        // ParkingSpot root/pivot is not centered on the displayed parking space.
        Renderer[] renderers = spot.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Bounds bounds = renderer.bounds;

            if (BoundsTouchesViewport(cameraToUse, bounds))
                return true;
        }

        // Collider bounds are a useful fallback for clickable spots whose
        // rendered child object has an unusual hierarchy.
        Collider2D[] colliders =
            spot.GetComponentsInChildren<Collider2D>(true);

        foreach (Collider2D collider in colliders)
        {
            if (collider == null)
                continue;

            if (BoundsTouchesViewport(cameraToUse, collider.bounds))
                return true;
        }

        return false;
    }

    private bool BoundsTouchesViewport(
        Camera cameraToUse,
        Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3[] points =
        {
            center,
            center + new Vector3(extents.x, extents.y, 0f),
            center + new Vector3(extents.x, -extents.y, 0f),
            center + new Vector3(-extents.x, extents.y, 0f),
            center + new Vector3(-extents.x, -extents.y, 0f)
        };

        foreach (Vector3 point in points)
        {
            if (IsPointInsideCameraViewport(cameraToUse, point))
                return true;
        }

        return false;
    }

    private bool IsPointInsideCameraViewport(
        Camera cameraToUse,
        Vector3 worldPosition)
    {
        if (cameraToUse == null)
            return false;

        Vector3 viewport =
            cameraToUse.WorldToViewportPoint(worldPosition);

        if (viewport.z < 0f)
            return false;

        float margin =
            Mathf.Clamp(viewportMargin, 0f, 0.25f);

        return viewport.x >= -margin &&
               viewport.x <= 1f + margin &&
               viewport.y >= -margin &&
               viewport.y <= 1f + margin;
    }

    private float GetCarToSpotDistanceMeters(ParkingSpot spot)
    {
        if (spot == null || carTransform == null)
            return -1f;

        return Vector2.Distance(
                   carTransform.position,
                   spot.transform.position
               ) * Mathf.Max(0.000001f, metersPerUnityUnit);
    }

    private bool IsWithinConfiguredViewRadius(float distanceMeters)
    {
        if (!useMaxViewDistanceForViewed)
            return true;

        if (distanceMeters < 0f)
            return false;

        return distanceMeters <= Mathf.Max(0f, maxViewDistanceMeters);
    }

    private string GetViewedRuleLabel()
    {
        string cameraPart = useCameraViewportForViewed
            ? "CameraViewport"
            : "NoCameraLimit";

        string radiusPart = useMaxViewDistanceForViewed
            ? "+Radius" + Mathf.Max(0f, maxViewDistanceMeters).ToString("0.#", invariant) + "m"
            : "+NoRadiusLimit";

        return cameraPart + radiusPart;
    }

    private float GetDrivingDistanceMeters()
    {
        if (carParking == null)
            carParking = CarParkingInteraction.Instance;

        return carParking != null
            ? Mathf.Max(0f, carParking.GetDrivingDistanceMeters())
            : 0f;
    }

    private void EnsureViewFile()
    {
        if (!writeSpotViewCsv)
            return;

        string outputDirectory =
            Path.Combine(Application.persistentDataPath, outputSubfolder);

        Directory.CreateDirectory(outputDirectory);

        string safeParticipant = MakeSafeFileName(participantId);
        string safeSession = MakeSafeFileName(sessionId);

        string expectedPath = Path.Combine(
            outputDirectory,
            "parking_spot_views_" +
            safeParticipant + "_" +
            safeSession + ".csv"
        );

        if (!string.Equals(
                viewFilePath,
                expectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            viewFilePath = expectedPath;
        }

        if (!File.Exists(viewFilePath))
        {
            File.WriteAllText(
                viewFilePath,
                Header + Environment.NewLine,
                new UTF8Encoding(true)
            );

            if (debugLogs)
                Debug.Log("[ParkingViewTracker] View CSV created: " + viewFilePath);
        }
    }

    private static bool IsModeledAlternative(string alternativeId)
    {
        return alternativeId == "A1" ||
               alternativeId == "A2" ||
               alternativeId == "A3" ||
               alternativeId == "A4" ||
               alternativeId == "A5" ||
               alternativeId == "A6";
    }

    private void AppendCsvRow(string path, List<string> values)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        StringBuilder line = new StringBuilder();

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                line.Append(',');

            line.Append(EscapeCsv(values[i]));
        }

        line.Append(Environment.NewLine);

        File.AppendAllText(
            path,
            line.ToString(),
            new UTF8Encoding(false)
        );
    }

    private static string EscapeCsv(string value)
    {
        if (value == null)
            return string.Empty;

        bool needsQuotes =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        if (!needsQuotes)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private string F(float value)
    {
        return value.ToString("0.######", invariant);
    }

    private string OptionalF(float value)
    {
        return value < 0f
            ? string.Empty
            : F(value);
    }

    private string I(int value)
    {
        return value.ToString(invariant);
    }

    private static string B(bool value)
    {
        return value ? "1" : "0";
    }

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "UNKNOWN";

        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');

        return value.Trim();
    }
}
