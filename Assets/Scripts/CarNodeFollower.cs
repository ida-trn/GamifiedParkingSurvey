using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class CarNodeFollower : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Fallback movement speed in Unity units/second. Used only when ScenarioManager speed selection is disabled or unavailable.")]
    public float speed = 4f;
    public float arriveDistance = 0.1f;
    public RoadNode startNode;

    [Header("Scenario Speed Control")]
    [Tooltip("When enabled, the car uses the 30/40/50 km/h value selected by SpeedSelectorUI through ScenarioManager.")]
    public bool useScenarioSelectedSpeed = true;

    [Tooltip("Optional. Assign ScenarioManager here, or leave empty and it will be found automatically.")]
    public ScenarioManager scenarioManager;

    [Tooltip("Multiplier applied after converting km/h to Unity units/second. Keep at 1 for physically calibrated speed. Lower only if you intentionally want slower visual simulation.")]
    [Min(0.01f)]
    public float selectedSpeedSimulationMultiplier = 1f;

    [Header("Distance Calibration")]
    [Tooltip("80 real metres / 330 Unity units = 0.2424.")]
    public float metersPerUnityUnit = 0.2424f;

    [Header("Driving Metrics")]
    [Tooltip("Minimum movement in Unity units before a frame counts as driving.")]
    public float movementTrackingThreshold = 0.0001f;

    [Header("Car Rotation")]
    [Tooltip("If car nose points RIGHT in the sprite, use 0. If UP, use -90. If LEFT, use 180. If DOWN, use 90.")]
    public float spriteRotationOffset = 0f;

    public bool smoothRotation = true;
    public float rotationSpeed = 10f;

    [Header("Camera")]
    public Camera worldCamera;

    [Header("Click / Touch Road Movement")]
    public bool enableClickToSelectMoveTarget = true;
    public bool startMovingImmediatelyOnMapClick = false;
    public bool blockMapClicksOnUI = false;

    [Tooltip("Small radius around a road click that is also accepted as being on the Road layer. This makes clicks less fragile than an exact OverlapPoint test.")]
    public float roadClickDetectionRadius = 0.75f;

    [Header("Layers")]
    public LayerMask roadLayer;
    public LayerMask parkingLayer;

    [Header("Target Selection")]
    public bool useMaxNodeClickDistance = false;
    public float maxNodeClickDistance = 150f;

    [Header("Move Target Marker Optional")]
    public Transform selectedTargetMarker;

    [Header("One-way Rules")]
    public bool preventImmediateBacktracking = true;

    [Header("Parking Click Override")]
    [Tooltip("If true, clicking a parking spot cancels the current node route and starts parking immediately.")]
    public bool allowParkingClickToInterruptRoute = true;

    [Header("Direct Parking While Mid-Edge")]
    [Tooltip("If the selected parking stop is ahead of the car on the road edge it is currently travelling along, drive directly into the spot instead of continuing to the next node and looping around the network.")]
    public bool allowDirectParkingAheadOnCurrentEdge = true;

    [Tooltip("Maximum sideways distance, in Unity units, that the parking stop may be from the current road edge and still count as being on that edge. Increase this slightly if roadside parking spots are not detected.")]
    public float directParkingEdgeTolerance = 2.5f;

    [Tooltip("Small forward margin used to make sure the selected parking spot is actually ahead of the car, not behind it.")]
    public float directParkingMinimumAheadDistance = 0.05f;

    [Header("Reverse Parking (R / دنده عقب)")]
    [Tooltip("If a selected parking stop is behind the car on the current road edge and close enough, reverse directly into it instead of driving around the RoadNode network.")]
    public bool allowDirectReverseParkingBehind = true;

    [Tooltip("Maximum REAL distance in metres that the car may reverse directly to a parking spot behind it.")]
    public float maxDirectReverseParkingDistanceMeters = 10f;

    [Tooltip("Reverse speed as a fraction of normal forward speed. 0.7 means reverse at 70% of normal speed.")]
    [Range(0.1f, 1f)]
    public float reverseParkingSpeedMultiplier = 0.7f;

    [Header("Undo Move")]
    public bool enableUndoMove = true;
    public bool animateUndoMove = true;
    public float undoSpeedMultiplier = 1f;

    [Header("Debug")]
    public bool debugLogs = true;

    [Header("Node Stop Movement Arrows")]
    [Tooltip("When true, the car pauses after reaching each intermediate node and shows arrows for possible next movements.")]
    public bool stopAtEveryNodeAndShowArrows = true;

    [Tooltip("When true, a node with exactly one legal outgoing direction is treated as a pass-through node: the car continues automatically without stopping or showing an arrow.")]
    public bool autoContinueThroughSingleDirectionNodes = true;

    [Tooltip("Keep this enabled so the scenario start node still shows its movement arrow even when it has only one legal outgoing direction.")]
    public bool alwaysShowArrowAtStartNode = true;

    [Tooltip("Arrow prefab shown around the car. It should have a SpriteRenderer. A BoxCollider2D will be added automatically if missing.")]
    public GameObject movementArrowPrefab;

    [Tooltip("How far each arrow appears from the car/node.")]
    public float movementArrowDistance = 0.8f;

    [Tooltip("Scale applied to spawned movement arrows.")]
    public float movementArrowScale = 0.42f;

    [Tooltip("If your arrow sprite points RIGHT, use 0. If it points UP, use -90.")]
    public float movementArrowRotationOffset = -90f;

    [Tooltip("Sorting order for arrows so they appear above the map.")]
    public int movementArrowSortingOrder = 200;

    [Tooltip("If true, arrows also appear when the route finishes and the car is idle at a node.")]
    public bool showArrowsWhenIdleAtNode = false;

    [Header("Configured-Street Mid-Edge U-Turns")]
    [Tooltip("When a parking spot on the paired opposite lane of any configured U-turn street is selected, allow a virtual U-turn at any point between RoadNodes instead of driving to the end of the one-way path.")]
    public bool enableOppositeLaneParkingUTurn = true;

    [Tooltip("Maximum sideways distance from the car to a configured U-turn path. This prevents U-turn logic from activating on nearby streets/intersections.")]
    [Min(0f)]
    public float maxDistanceFromConfiguredUTurnPath = 3f;

    [Tooltip("Number of points used to draw the curved U-turn. Higher values look smoother. 12-20 is usually enough.")]
    [Range(4, 40)]
    public int uTurnCurveSamples = 14;

    [Tooltip("How far the U-turn curve initially continues forward before bending onto the opposite lane, in Unity units.")]
    [Min(0f)]
    public float uTurnCurveForwardDistance = 1.5f;

    [Header("Manual U-Turn Button")]
    [Tooltip("Allows the participant to press a UI button and make an immediate mid-edge U-turn on paths where ContinuousUTurnPath.Allow Manual U Turns is enabled.")]
    public bool enableManualUTurn = true;

    [Tooltip("Assign the UTurnButton UI Button here. Put it under ConventionalDrivingButtonsRoot so ScenarioManager automatically hides it in Assisted mode.")]
    public Button uTurnButton;

    [Tooltip("If true, the assigned U-turn button becomes non-interactable whenever the car is not on a configured U-turn street or a U-turn is temporarily blocked.")]
    public bool automaticallySetUTurnButtonInteractable = true;

    [Tooltip("After a manual U-turn reaches the opposite lane, drive this many REAL metres forward on that lane, then stop and show the normal forward movement arrow. Recommended: 1 metre.")]
    [Min(0f)]
    public float manualUTurnPostTurnStopDistanceMeters = 1f;

    [Header("Scenario Start Direction")]
    [Tooltip("Immediately show the available movement arrows when a conventional scenario begins.")]
    public bool showArrowsAtScenarioStart = true;

    [Tooltip("At scenario reset, point the car toward the first valid outgoing connected node instead of trusting the RoadNode object's transform rotation.")]
    public bool faceFirstOutgoingRoadAtScenarioStart = true;

    [Header("Brake Resume Arrow")]
    [Tooltip("When the participant presses the brake while the car is moving, stop the car and show one forward arrow. Clicking that arrow resumes the same route.")]
    public bool showForwardArrowWhenBraked = true;

    private RoadNode previousNode;
    private RoadNode currentNode;
    private RoadNode selectedTargetNode;

    private Queue<RoadNode> pathQueue = new Queue<RoadNode>();

    private ParkingSpot pendingParkingSpot;
    private CarParkingInteraction parkingInteraction;

    private bool isBraking = false;
    private bool automaticParkingRouteActive = false;

    private float drivingDistanceMeters = 0f;
    private float drivingTimeSeconds = 0f;
    private float plannedParkingRouteDistanceMeters = 0f;

    // Compact research metrics for the three study speeds. These accumulate
    // only while the car actually moves, so time spent stopped at a node or
    // reading a confirmation panel is not counted as "speed use".
    private float drivingTimeAt30Seconds = 0f;
    private float drivingTimeAt40Seconds = 0f;
    private float drivingTimeAt50Seconds = 0f;
    private float drivingDistanceAt30Meters = 0f;
    private float drivingDistanceAt40Meters = 0f;
    private float drivingDistanceAt50Meters = 0f;

    private Vector3 previousMetricPosition;
    private bool hasPreviousMetricPosition = false;
    private bool suppressNextMetricSample = false;

    private bool movingToVirtualParkingStop = false;
    private bool useVirtualParkingStopAfterNodeRoute = false;
    private Vector3 virtualParkingStopPosition;

    // True only when the current direct parking manoeuvre is using reverse gear (R / دنده عقب).
    // While this is true, FollowVirtualParkingStop moves backward without turning the car around.
    private bool reversingToVirtualParkingStop = false;

    // Special parking route used only when a chosen parking spot is on the
    // paired opposite configured lane. It is a world-space route, so the U-turn
    // can happen at any point between RoadNodes rather than only at a node.
    private readonly Queue<Vector3> oppositeLaneParkingRoutePoints =
        new Queue<Vector3>();

    private bool oppositeLaneParkingUTurnActive = false;

    private sealed class OppositeLaneParkingPlan
    {
        public readonly List<Vector3> routePoints = new List<Vector3>();
        public float totalDistanceUnity;
        public Vector3 uTurnDeparturePoint;
        public Vector3 uTurnLandingPoint;
        public ContinuousUTurnPath sourcePath;
        public ContinuousUTurnPath targetPath;
    }

    // Manual U-turns are also world-space routes so they can begin at the
    // car's exact position between RoadNodes. After the curve reaches the
    // opposite lane, the normal RoadNode system resumes from that lane.
    private readonly Queue<Vector3> manualUTurnRoutePoints =
        new Queue<Vector3>();

    private bool manualUTurnActive = false;
    private ContinuousUTurnPath.PathProjection manualUTurnLandingProjection;
    private ContinuousUTurnPath.PathProjection manualUTurnStopProjection;

    private sealed class ManualUTurnPlan
    {
        public readonly List<Vector3> routePoints = new List<Vector3>();
        public ContinuousUTurnPath sourcePath;
        public ContinuousUTurnPath targetPath;
        public ContinuousUTurnPath.PathProjection sourceProjection;
        public ContinuousUTurnPath.PathProjection landingProjection;
        public ContinuousUTurnPath.PathProjection stopProjection;
        public Vector3 departurePoint;
        public Vector3 landingPoint;
        public Vector3 stopPoint;
        public float totalDistanceUnity;
    }

    private bool isUndoing = false;

    private bool waitingAtNodeChoice = false;
    private bool manualArrowNavigationActive = false;
    private RoadNode currentStoppedChoiceNode;

    private readonly List<GameObject> spawnedMovementArrows = new List<GameObject>();
    private readonly Dictionary<GameObject, RoadNode> movementArrowTargets = new Dictionary<GameObject, RoadNode>();

    // A brake-resume arrow is visually the same as the normal movement arrow,
    // but it does not choose a new RoadNode. It simply releases the brake and
    // continues whatever route was already active.
    private readonly HashSet<GameObject> brakeResumeArrows = new HashSet<GameObject>();
    private bool waitingForBrakeResumeArrow = false;

    private Stack<UndoMoveState> undoHistory = new Stack<UndoMoveState>();
    private RoadNodeUndoButton[] undoButtons;

    private struct UndoMoveState
    {
        public Vector3 carPosition;
        public Quaternion carRotation;
        public RoadNode currentNode;
        public RoadNode previousNode;

        public UndoMoveState(Vector3 carPosition, Quaternion carRotation, RoadNode currentNode, RoadNode previousNode)
        {
            this.carPosition = carPosition;
            this.carRotation = carRotation;
            this.currentNode = currentNode;
            this.previousNode = previousNode;
        }
    }

    public RoadNode CurrentNode
    {
        get { return currentNode; }
    }

    public float DrivingDistanceMeters
    {
        get { return drivingDistanceMeters; }
    }

    public float DrivingTimeSeconds
    {
        get { return drivingTimeSeconds; }
    }

    public float PlannedParkingRouteDistanceMeters
    {
        get { return plannedParkingRouteDistanceMeters; }
    }

    public float DrivingTimeAt30Seconds => drivingTimeAt30Seconds;
    public float DrivingTimeAt40Seconds => drivingTimeAt40Seconds;
    public float DrivingTimeAt50Seconds => drivingTimeAt50Seconds;

    public float DrivingDistanceAt30Meters => drivingDistanceAt30Meters;
    public float DrivingDistanceAt40Meters => drivingDistanceAt40Meters;
    public float DrivingDistanceAt50Meters => drivingDistanceAt50Meters;

    /// <summary>
    /// Study speed used for the largest amount of actual moving time.
    /// Returns 0 if no driving movement has occurred yet.
    /// </summary>
    public int DominantDrivingSpeedByTimeKmh
    {
        get
        {
            return GetDominantStudySpeed(
                drivingTimeAt30Seconds,
                drivingTimeAt40Seconds,
                drivingTimeAt50Seconds
            );
        }
    }

    /// <summary>
    /// Study speed used for the largest amount of actual travelled distance.
    /// Returns 0 if no driving movement has occurred yet.
    /// </summary>
    public int DominantDrivingSpeedByDistanceKmh
    {
        get
        {
            return GetDominantStudySpeed(
                drivingDistanceAt30Meters,
                drivingDistanceAt40Meters,
                drivingDistanceAt50Meters
            );
        }
    }

    public bool AutomaticParkingRouteActive
    {
        get { return automaticParkingRouteActive; }
    }

    public bool IsBraking
    {
        get { return isBraking; }
    }

    /// <summary>
    /// Current participant-selected road speed in km/h. Falls back to an
    /// equivalent value derived from the legacy Unity-units speed if the
    /// ScenarioManager speed selector is unavailable.
    /// </summary>
    public float CurrentDrivingSpeedKmh
    {
        get
        {
            if (useScenarioSelectedSpeed)
            {
                ScenarioManager manager = GetScenarioManager();
                if (manager != null)
                    return manager.GetSelectedDrivingSpeedKmh();
            }

            return GetFallbackSpeedKmh();
        }
    }

    /// <summary>
    /// Actual world movement speed currently used by the car, in Unity units/s.
    /// </summary>
    public float CurrentMovementSpeedUnitsPerSecond
    {
        get { return GetCurrentMovementSpeedUnitsPerSecond(); }
    }

    public bool CanSelectParkingNow()
    {
        return !isUndoing &&
               (isBraking || waitingAtNodeChoice || !IsRouteActive());
    }

    /// <summary>
    /// Looser gate than CanSelectParkingNow(): allows a new parking spot to be
    /// selected/re-selected while the car is braked, waiting at a node choice,
    /// idle, OR actively driving mid-edge between two RoadNodes. Only blocks
    /// while an undo is in progress. Use this when the desired behavior is
    /// "the car always parks at whichever spot was clicked most recently,"
    /// since RouteToParkingSpot already rebuilds the route from the car's
    /// current forward position (see GetForwardRoutingAnchor) instead of
    /// reversing, so it's safe to reroute mid-edge.
    /// </summary>
    public bool CanReselectMidDrive()
    {
        // A parking target may be changed during ordinary node driving, but not
        // while an undo or the short manual U-turn curve itself is in progress.
        return !isUndoing && !manualUTurnActive;
    }

    public void ResetDrivingMetrics()
    {
        drivingDistanceMeters = 0f;
        drivingTimeSeconds = 0f;
        plannedParkingRouteDistanceMeters = 0f;

        drivingTimeAt30Seconds = 0f;
        drivingTimeAt40Seconds = 0f;
        drivingTimeAt50Seconds = 0f;
        drivingDistanceAt30Meters = 0f;
        drivingDistanceAt40Meters = 0f;
        drivingDistanceAt50Meters = 0f;

        previousMetricPosition = transform.position;
        hasPreviousMetricPosition = true;
        suppressNextMetricSample = true;
    }

    public void SuppressNextDistanceSample()
    {
        previousMetricPosition = transform.position;
        hasPreviousMetricPosition = true;
        suppressNextMetricSample = true;
    }

    public float GetRemainingRouteDistanceMeters()
    {
        if (!automaticParkingRouteActive)
            return 0f;

        float distanceUnity = 0f;
        Vector2 cursor = transform.position;

        if (oppositeLaneParkingUTurnActive)
        {
            foreach (Vector3 point in oppositeLaneParkingRoutePoints)
            {
                Vector2 next = point;
                distanceUnity += Vector2.Distance(cursor, next);
                cursor = next;
            }

            return distanceUnity * Mathf.Max(0f, metersPerUnityUnit);
        }

        foreach (RoadNode queuedNode in pathQueue)
        {
            if (queuedNode == null)
                continue;

            Vector2 next = queuedNode.transform.position;
            distanceUnity += Vector2.Distance(cursor, next);
            cursor = next;
        }

        if (movingToVirtualParkingStop || useVirtualParkingStopAfterNodeRoute)
        {
            distanceUnity += Vector2.Distance(
                cursor,
                (Vector2)virtualParkingStopPosition
            );
        }

        return distanceUnity * Mathf.Max(0f, metersPerUnityUnit);
    }

    public void ResetToSpecificNode(RoadNode node)
    {
        if (node == null)
        {
            Debug.LogWarning("[CarNodeFollower] ResetToSpecificNode called with null node.");
            return;
        }

        ClearCurrentRouteWithoutClearingUndo();

        startNode = node;
        currentNode = node;
        previousNode = null;

        transform.position = new Vector3(
            node.transform.position.x,
            node.transform.position.y,
            transform.position.z
        );

        transform.rotation = node.transform.rotation;

        if (faceFirstOutgoingRoadAtScenarioStart)
            FaceCarAlongFirstOutgoingRoad(node);

        ResetDrivingMetrics();

        isBraking = false;
        isUndoing = false;
        waitingForBrakeResumeArrow = false;
        waitingAtNodeChoice = false;
        manualArrowNavigationActive = false;
        currentStoppedChoiceNode = null;

        undoHistory.Clear();

        if (selectedTargetMarker != null)
            selectedTargetMarker.gameObject.SetActive(false);

        RefreshUndoButtons();

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Reset to scenario start node: " + node.nodeId);
    }

    /// <summary>
    /// Called by ScenarioManager exactly when gameplay becomes interactive.
    /// This gives the player valid movement choices immediately, without first
    /// clicking a road area and pressing Move.
    /// </summary>
    public void PrepareForScenarioGameplay()
    {
        if (currentNode == null || isUndoing || automaticParkingRouteActive)
            return;

        ClearMovementArrows();

        selectedTargetNode = null;
        waitingAtNodeChoice = false;
        currentStoppedChoiceNode = null;
        manualArrowNavigationActive = false;
        isBraking = false;

        if (selectedTargetMarker != null)
            selectedTargetMarker.gameObject.SetActive(false);

        if (faceFirstOutgoingRoadAtScenarioStart)
            FaceCarAlongFirstOutgoingRoad(currentNode);

        if (showArrowsAtScenarioStart &&
            stopAtEveryNodeAndShowArrows)
        {
            manualArrowNavigationActive = true;
            StopAtNodeAndShowMovementArrows(currentNode);
        }
        else
        {
            RefreshUndoButtons();
        }
    }

    private void Awake()
    {
        parkingInteraction = GetComponent<CarParkingInteraction>();

        if (scenarioManager == null && parkingInteraction != null)
            scenarioManager = parkingInteraction.scenarioManager;

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (worldCamera == null)
            worldCamera = Camera.main;
    }

    private void Start()
    {
        currentNode = startNode;
        previousNode = null;

        if (currentNode != null)
        {
            transform.position = currentNode.transform.position;

            if (debugLogs)
                Debug.Log("[CarNodeFollower] Started at node: " + currentNode.nodeId);
        }
        else
        {
            Debug.LogWarning("[CarNodeFollower] StartNode is not assigned yet. ScenarioManager can assign it at round start.");
        }

        ResetDrivingMetrics();

        if (selectedTargetMarker != null)
            selectedTargetMarker.gameObject.SetActive(false);

        CacheUndoButtons();
        RefreshUndoButtons();

        if (showArrowsWhenIdleAtNode && currentNode != null)
        {
            manualArrowNavigationActive = true;
            StopAtNodeAndShowMovementArrows(currentNode);
        }
    }

    private void Update()
    {
        RefreshUTurnButtonState();

        if (waitingAtNodeChoice || waitingForBrakeResumeArrow)
        {
            HandleMovementArrowClick();
            return;
        }

        if (!isUndoing)
        {
            HandleClickOrTouchTargetSelection();
        }

        if (!isBraking && !isUndoing)
        {
            if (manualUTurnActive &&
                manualUTurnRoutePoints.Count > 0)
            {
                FollowManualUTurnRoute();
            }
            else if (oppositeLaneParkingUTurnActive &&
                     oppositeLaneParkingRoutePoints.Count > 0)
            {
                FollowOppositeLaneParkingRoute();
            }
            else if (pathQueue.Count > 0)
            {
                FollowPath();
            }
            else if (movingToVirtualParkingStop)
            {
                FollowVirtualParkingStop();
            }
        }
    }

    private void LateUpdate()
    {
        TrackDrivingMetrics();
    }

    private void TrackDrivingMetrics()
    {
        if (!hasPreviousMetricPosition)
        {
            previousMetricPosition = transform.position;
            hasPreviousMetricPosition = true;
            return;
        }

        if (suppressNextMetricSample)
        {
            previousMetricPosition = transform.position;
            suppressNextMetricSample = false;
            return;
        }

        float movedUnity = Vector2.Distance(
            previousMetricPosition,
            transform.position
        );

        if (movedUnity > Mathf.Max(0f, movementTrackingThreshold))
        {
            float movedMeters =
                movedUnity * Mathf.Max(0f, metersPerUnityUnit);

            float movedTimeSeconds = Mathf.Max(0f, Time.deltaTime);

            drivingDistanceMeters += movedMeters;
            drivingTimeSeconds += movedTimeSeconds;

            // Attribute each moving frame to the participant-selected study
            // speed. Reverse parking still belongs to the selected 30/40/50
            // choice even though its animation has a reverse-speed multiplier.
            AccumulateStudySpeedUsage(
                GetCurrentStudySpeedBucketKmh(),
                movedTimeSeconds,
                movedMeters
            );
        }

        previousMetricPosition = transform.position;
    }

    public float GetDrivingTimeAtSpeedSeconds(int speedKmh)
    {
        switch (speedKmh)
        {
            case 30: return drivingTimeAt30Seconds;
            case 40: return drivingTimeAt40Seconds;
            case 50: return drivingTimeAt50Seconds;
            default: return 0f;
        }
    }

    public float GetDrivingDistanceAtSpeedMeters(int speedKmh)
    {
        switch (speedKmh)
        {
            case 30: return drivingDistanceAt30Meters;
            case 40: return drivingDistanceAt40Meters;
            case 50: return drivingDistanceAt50Meters;
            default: return 0f;
        }
    }

    private void AccumulateStudySpeedUsage(
        int speedKmh,
        float timeSeconds,
        float distanceMeters)
    {
        switch (speedKmh)
        {
            case 30:
                drivingTimeAt30Seconds += timeSeconds;
                drivingDistanceAt30Meters += distanceMeters;
                break;

            case 40:
                drivingTimeAt40Seconds += timeSeconds;
                drivingDistanceAt40Meters += distanceMeters;
                break;

            case 50:
                drivingTimeAt50Seconds += timeSeconds;
                drivingDistanceAt50Meters += distanceMeters;
                break;
        }
    }

    private int GetCurrentStudySpeedBucketKmh()
    {
        float current = CurrentDrivingSpeedKmh;

        float d30 = Mathf.Abs(current - 30f);
        float d40 = Mathf.Abs(current - 40f);
        float d50 = Mathf.Abs(current - 50f);

        if (d30 <= d40 && d30 <= d50)
            return 30;

        if (d40 <= d30 && d40 <= d50)
            return 40;

        return 50;
    }

    private static int GetDominantStudySpeed(
        float amount30,
        float amount40,
        float amount50)
    {
        float maximum = Mathf.Max(amount30, Mathf.Max(amount40, amount50));

        if (maximum <= 0f)
            return 0;

        // On an exact tie, prefer the higher speed so the result is
        // deterministic and remains consistent with the 50 km/h start state.
        if (Mathf.Approximately(amount50, maximum))
            return 50;

        if (Mathf.Approximately(amount40, maximum))
            return 40;

        return 30;
    }

    private void HandleClickOrTouchTargetSelection()
    {
        if (!enableClickToSelectMoveTarget)
            return;

        if (parkingInteraction != null &&
            parkingInteraction.IsParkingConfirmationOpen)
        {
            return;
        }

        if (!Input.GetMouseButtonDown(0))
            return;

        if (IsRouteActive())
            return;

        if (worldCamera == null)
        {
            Debug.LogWarning("[CarNodeFollower] World Camera is null.");
            return;
        }

        if (blockMapClicksOnUI && IsPointerOverUI())
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Click ignored because pointer is over UI.");

            return;
        }

        Vector2 worldPos = worldCamera.ScreenToWorldPoint(Input.mousePosition);

        Collider2D parkingHit = Physics2D.OverlapPoint(worldPos, parkingLayer);

        if (parkingHit != null && parkingHit.GetComponentInParent<ParkingSpot>() != null)
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Parking spot clicked, road target selection skipped.");

            return;
        }

        float clickRadius = Mathf.Max(0f, roadClickDetectionRadius);

        Collider2D roadHit =
            clickRadius > 0f
                ? Physics2D.OverlapCircle(worldPos, clickRadius, roadLayer)
                : Physics2D.OverlapPoint(worldPos, roadLayer);

        if (roadHit == null)
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Click was not on Road layer.");

            return;
        }

        RoadNode nearestNode = FindBestNodeForMapClick(worldPos);

        if (nearestNode == null)
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Road clicked, but no node was found.");

            return;
        }

        ClearMovementArrows();
        waitingAtNodeChoice = false;
        currentStoppedChoiceNode = null;
        manualArrowNavigationActive = false;

        pendingParkingSpot = null;
        movingToVirtualParkingStop = false;
        useVirtualParkingStopAfterNodeRoute = false;

        selectedTargetNode = nearestNode;

        if (selectedTargetMarker != null)
        {
            selectedTargetMarker.position = nearestNode.transform.position;
            selectedTargetMarker.gameObject.SetActive(true);
        }

        if (debugLogs)
        {
            Debug.Log("[CarNodeFollower] Road clicked. Selected move target = " + selectedTargetNode.nodeId);
        }

        if (startMovingImmediatelyOnMapClick)
        {
            UI_MoveButton();
        }
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null)
            return false;

        return EventSystem.current.IsPointerOverGameObject();
    }

    public void UI_MoveButton()
    {
        if (isUndoing)
            return;

        // Backwards compatibility: if an old Move button still exists in a
        // scene, pressing it while braked performs the same action as clicking
        // the new forward resume arrow.
        if (waitingForBrakeResumeArrow)
        {
            ResumeFromBrakeArrow();
            return;
        }

        if (waitingAtNodeChoice)
        {
            ContinuePlannedRouteFromNodeChoice();
            return;
        }

        isBraking = false;

        if (manualUTurnActive ||
            oppositeLaneParkingUTurnActive ||
            pathQueue.Count > 0 ||
            movingToVirtualParkingStop)
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Move pressed. Continuing current route.");

            return;
        }

        if (selectedTargetNode == null)
        {
            Debug.Log("[CarNodeFollower] No selected target. Click/tap a road area first.");
            return;
        }

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Move pressed. Routing to: " + selectedTargetNode.nodeId);

        RouteToNode(selectedTargetNode);
    }

    public void UI_StopButton()
    {
        if (isUndoing)
            return;

        // If normal route-choice arrows are already visible at a RoadNode,
        // the car is already stopped and does not need an additional resume arrow.
        if (waitingAtNodeChoice)
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Brake ignored because the car is already waiting at a movement-choice node.");

            return;
        }

        // The Move button is no longer required, so never allow the brake to
        // leave the participant stranded without a way to continue.
        if (!showForwardArrowWhenBraked)
        {
            if (debugLogs)
                Debug.LogWarning("[CarNodeFollower] Brake resume arrow is disabled. Brake press ignored because no Move button is required anymore.");

            return;
        }

        if (movementArrowPrefab == null)
        {
            Debug.LogWarning(
                "[CarNodeFollower] Cannot brake safely because Movement Arrow Prefab is not assigned. " +
                "Assign the same yellow arrow prefab used for node choices."
            );
            return;
        }

        if (!TryGetBrakeResumeDirection(out Vector2 resumeDirection))
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Brake pressed while no resumable movement route was active.");

            return;
        }

        isBraking = true;
        ShowBrakeResumeArrow(resumeDirection);

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Brake pressed. Car paused and forward resume arrow shown.");

        RefreshUndoButtons();
    }

    /// <summary>
    /// Finds the direction the car would continue if the brake were released.
    /// This works for ordinary RoadNode driving as well as the short world-space
    /// routes used by U-turns and direct parking.
    /// </summary>
    private bool TryGetBrakeResumeDirection(out Vector2 direction)
    {
        direction = Vector2.zero;
        Vector2 carPosition = transform.position;
        Vector2 targetPosition;

        if (manualUTurnActive && manualUTurnRoutePoints.Count > 0)
        {
            targetPosition = manualUTurnRoutePoints.Peek();
        }
        else if (oppositeLaneParkingUTurnActive &&
                 oppositeLaneParkingRoutePoints.Count > 0)
        {
            targetPosition = oppositeLaneParkingRoutePoints.Peek();
        }
        else if (pathQueue.Count > 0)
        {
            RoadNode nextNode = pathQueue.Peek();

            if (nextNode == null)
                return false;

            targetPosition = nextNode.transform.position;
        }
        else if (movingToVirtualParkingStop)
        {
            targetPosition = virtualParkingStopPosition;
        }
        else
        {
            return false;
        }

        direction = targetPosition - carPosition;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            // Very close to the next route point: use the car's current facing
            // direction so the resume control still appears in front of it.
            direction = GetCarTravelFacingDirection();
        }

        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();
        return true;
    }

    /// <summary>
    /// Spawns one yellow arrow in front of the braked car. Unlike normal
    /// intersection arrows, this arrow does NOT change the route.
    /// </summary>
    private void ShowBrakeResumeArrow(Vector2 direction)
    {
        ClearMovementArrows();

        Vector3 arrowPosition =
            transform.position +
            (Vector3)(direction.normalized * movementArrowDistance);

        arrowPosition.z = transform.position.z;

        GameObject arrow = Instantiate(
            movementArrowPrefab,
            arrowPosition,
            Quaternion.identity
        );

        arrow.name = "BrakeResumeArrow";
        arrow.transform.localScale = Vector3.one * movementArrowScale;

        float angle =
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        arrow.transform.rotation = Quaternion.Euler(
            0f,
            0f,
            angle + movementArrowRotationOffset
        );

        SpriteRenderer spriteRenderer =
            arrow.GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.sortingOrder = movementArrowSortingOrder;

        if (arrow.GetComponentInChildren<Collider2D>() == null)
            arrow.AddComponent<BoxCollider2D>();

        // The prefab may contain the MovementArrow script used for RoadNode
        // choices. Disable it for this special arrow because this click means
        // "continue", not "choose another node".
        MovementArrow movementArrow =
            arrow.GetComponentInChildren<MovementArrow>();

        if (movementArrow != null)
            movementArrow.enabled = false;

        spawnedMovementArrows.Add(arrow);
        brakeResumeArrows.Add(arrow);
        waitingForBrakeResumeArrow = true;
    }

    private void ResumeFromBrakeArrow()
    {
        ClearMovementArrows();

        waitingForBrakeResumeArrow = false;
        isBraking = false;

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Forward resume arrow selected. Continuing the existing route.");

        RefreshUndoButtons();
    }

    public bool CanManualUTurnNow()
    {
        return TryBuildManualUTurnPlan(
            out ManualUTurnPlan ignoredPlan,
            out string ignoredReason
        );
    }

    /// <summary>
    /// UI hook for the new U-turn button. The turn starts at the car's exact
    /// current world position, so it works between RoadNodes. It is only valid
    /// on ContinuousUTurnPath pairs with Allow Manual U Turns enabled.
    /// </summary>
    public void UI_UTurnButton()
    {
        if (!TryBuildManualUTurnPlan(
                out ManualUTurnPlan plan,
                out string failureReason))
        {
            if (debugLogs)
            {
                Debug.Log(
                    "[CarNodeFollower] Manual U-turn unavailable: " +
                    failureReason
                );
            }

            RefreshUTurnButtonState();
            return;
        }

        // Preserve the current state in the normal undo history.
        RecordUndoState();

        ClearCurrentRouteWithoutClearingUndo();

        manualUTurnLandingProjection = plan.landingProjection;
        manualUTurnStopProjection = plan.stopProjection;
        manualUTurnActive = true;
        isBraking = false;
        manualArrowNavigationActive = false;

        foreach (Vector3 point in plan.routePoints)
        {
            Vector3 routePoint = point;
            routePoint.z = transform.position.z;
            manualUTurnRoutePoints.Enqueue(routePoint);
        }

        RefreshUndoButtons();
        RefreshUTurnButtonState();

        if (debugLogs)
        {
            Debug.Log(
                "[CarNodeFollower] Manual mid-edge U-turn started | " +
                "source=" + plan.sourcePath.pathLabel +
                ", target=" + plan.targetPath.pathLabel +
                ", departure=(" +
                plan.departurePoint.x.ToString("0.##") + "," +
                plan.departurePoint.y.ToString("0.##") + ")" +
                ", landing=(" +
                plan.landingPoint.x.ToString("0.##") + "," +
                plan.landingPoint.y.ToString("0.##") + ")" +
                ", stop=(" +
                plan.stopPoint.x.ToString("0.##") + "," +
                plan.stopPoint.y.ToString("0.##") + ")" +
                ", routeDistance=" +
                (plan.totalDistanceUnity * Mathf.Max(0f, metersPerUnityUnit))
                    .ToString("0.0") +
                " m"
            );
        }
    }

    private bool TryBuildManualUTurnPlan(
        out ManualUTurnPlan plan,
        out string failureReason)
    {
        plan = null;
        failureReason = string.Empty;

        if (!enableManualUTurn)
        {
            failureReason = "Manual U-turns are disabled on CarNodeFollower.";
            return false;
        }

        if (isUndoing)
        {
            failureReason = "Undo is in progress.";
            return false;
        }

        if (manualUTurnActive)
        {
            failureReason = "A manual U-turn is already in progress.";
            return false;
        }

        if (automaticParkingRouteActive ||
            oppositeLaneParkingUTurnActive)
        {
            failureReason = "An automatic parking route is active.";
            return false;
        }

        if (parkingInteraction != null &&
            parkingInteraction.IsParkingConfirmationOpen)
        {
            failureReason = "Parking confirmation is open.";
            return false;
        }

        if (!TryGetCurrentManualUTurnSource(
                out ContinuousUTurnPath sourcePath,
                out ContinuousUTurnPath.PathProjection sourceProjection))
        {
            failureReason =
                "The car is not on a configured street where manual U-turns are allowed.";
            return false;
        }

        ContinuousUTurnPath targetPath = sourcePath.oppositePath;

        if (targetPath == null ||
            !targetPath.allowManualUTurns)
        {
            failureReason = "The paired opposite path does not allow manual U-turns.";
            return false;
        }

        Vector3 departurePoint = transform.position;

        if (!targetPath.TryGetNearestDirectedSegment(
                departurePoint,
                out ContinuousUTurnPath.PathProjection landingProjection))
        {
            failureReason = "No opposite-lane segment was found.";
            return false;
        }

        float crossingDistance = Vector2.Distance(
            departurePoint,
            landingProjection.point
        );

        float maxCrossingDistance =
            Mathf.Max(0f, sourcePath.maxUTurnCrossingDistance);

        if (maxCrossingDistance > 0f &&
            crossingDistance > maxCrossingDistance)
        {
            failureReason =
                "The opposite lane is farther than the configured U-turn crossing limit.";
            return false;
        }

        Vector2 sourceDirection =
            (Vector2)sourceProjection.toNode.transform.position -
            (Vector2)sourceProjection.fromNode.transform.position;

        Vector2 targetDirection =
            (Vector2)landingProjection.toNode.transform.position -
            (Vector2)landingProjection.fromNode.transform.position;

        if (sourceDirection.sqrMagnitude <= 0.0001f ||
            targetDirection.sqrMagnitude <= 0.0001f)
        {
            failureReason = "A U-turn path segment has zero length.";
            return false;
        }

        ManualUTurnPlan candidate = new ManualUTurnPlan
        {
            sourcePath = sourcePath,
            targetPath = targetPath,
            sourceProjection = sourceProjection,
            landingProjection = landingProjection,
            departurePoint = departurePoint,
            landingPoint = landingProjection.point
        };

        candidate.landingPoint.z = transform.position.z;

        AppendUTurnCurvePoints(
            candidate.routePoints,
            candidate.departurePoint,
            candidate.landingPoint,
            sourceDirection.normalized,
            targetDirection.normalized
        );

        if (candidate.routePoints.Count == 0)
        {
            failureReason = "The U-turn curve could not be generated.";
            return false;
        }

        // After reaching the opposite lane, continue a short REAL distance
        // along that lane. The requested behavior is to stop 1 metre after
        // the U-turn instead of immediately resuming automatic node travel.
        float metresPerUnit = Mathf.Max(0f, metersPerUnityUnit);
        float postTurnDistanceUnity =
            metresPerUnit > 0f
                ? Mathf.Max(0f, manualUTurnPostTurnStopDistanceMeters) / metresPerUnit
                : 0f;

        if (!TryAppendForwardDistanceOnConfiguredPath(
                candidate.targetPath,
                candidate.landingProjection,
                postTurnDistanceUnity,
                candidate.routePoints,
                out ContinuousUTurnPath.PathProjection stopProjection))
        {
            failureReason = "Could not find a valid forward stop point on the opposite lane.";
            return false;
        }

        candidate.stopProjection = stopProjection;
        candidate.stopPoint = stopProjection.point;
        candidate.stopPoint.z = transform.position.z;

        candidate.totalDistanceUnity =
            CalculateWorldRouteLengthUnity(
                transform.position,
                candidate.routePoints
            );

        plan = candidate;
        return true;
    }

    private bool TryGetCurrentManualUTurnSource(
        out ContinuousUTurnPath sourcePath,
        out ContinuousUTurnPath.PathProjection sourceProjection)
    {
        sourcePath = null;
        sourceProjection = default;

        if (currentNode == null)
            return false;

        // Mid-edge: the queue head is the exact RoadNode currently ahead of
        // the car, so it unambiguously identifies the directional street path.
        if (pathQueue.Count > 0)
        {
            RoadNode forwardNode = pathQueue.Peek();

            ContinuousUTurnPath path =
                ContinuousUTurnPath.FindManualUTurnPathContainingDirectedSegment(
                    currentNode,
                    forwardNode
                );

            if (path == null)
                return false;

            if (!path.TryProjectOntoDirectedSegment(
                    currentNode,
                    forwardNode,
                    transform.position,
                    out ContinuousUTurnPath.PathProjection projection))
            {
                return false;
            }

            if (projection.distance >
                Mathf.Max(0f, maxDistanceFromConfiguredUTurnPath))
            {
                return false;
            }

            sourcePath = path;
            sourceProjection = projection;
            return true;
        }

        // At a node, identify the configured street continuation that best
        // matches the direction the car is currently facing.
        if (currentNode.connectedNodes == null)
            return false;

        Vector2 facingDirection = GetCarTravelFacingDirection();
        float bestAlignment = -Mathf.Infinity;
        float bestDistance = Mathf.Infinity;
        bool found = false;

        foreach (RoadNode nextNode in currentNode.connectedNodes)
        {
            if (nextNode == null)
                continue;

            ContinuousUTurnPath path =
                ContinuousUTurnPath.FindManualUTurnPathContainingDirectedSegment(
                    currentNode,
                    nextNode
                );

            if (path == null)
                continue;

            if (!path.TryProjectOntoDirectedSegment(
                    currentNode,
                    nextNode,
                    transform.position,
                    out ContinuousUTurnPath.PathProjection projection))
            {
                continue;
            }

            if (projection.distance >
                Mathf.Max(0f, maxDistanceFromConfiguredUTurnPath))
            {
                continue;
            }

            Vector2 candidateDirection =
                (Vector2)nextNode.transform.position -
                (Vector2)currentNode.transform.position;

            float alignment =
                candidateDirection.sqrMagnitude > 0.0001f
                    ? Vector2.Dot(
                        facingDirection,
                        candidateDirection.normalized
                    )
                    : -1f;

            if (!found ||
                alignment > bestAlignment + 0.001f ||
                (Mathf.Abs(alignment - bestAlignment) <= 0.001f &&
                 projection.distance < bestDistance))
            {
                sourcePath = path;
                sourceProjection = projection;
                bestAlignment = alignment;
                bestDistance = projection.distance;
                found = true;
            }
        }

        return found;
    }

    private Vector2 GetCarTravelFacingDirection()
    {
        float travelAngleDegrees =
            transform.eulerAngles.z - spriteRotationOffset;

        float radians =
            travelAngleDegrees * Mathf.Deg2Rad;

        return new Vector2(
            Mathf.Cos(radians),
            Mathf.Sin(radians)
        ).normalized;
    }

    private ScenarioManager GetScenarioManager()
    {
        if (scenarioManager == null && parkingInteraction != null)
            scenarioManager = parkingInteraction.scenarioManager;

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        return scenarioManager;
    }

    /// <summary>
    /// Converts the participant-selected km/h into Unity units/second using the
    /// same metres-per-Unity-unit calibration used by distance logging.
    /// Example with 0.2424 m/unit: 40 km/h = 11.11 m/s = about 45.84 units/s.
    /// </summary>
    private float GetCurrentMovementSpeedUnitsPerSecond()
    {
        float fallback = Mathf.Max(0.01f, speed);

        if (!useScenarioSelectedSpeed)
            return fallback;

        ScenarioManager manager = GetScenarioManager();
        if (manager == null)
            return fallback;

        float metresPerUnit = Mathf.Max(0.0001f, metersPerUnityUnit);
        float metresPerSecond = Mathf.Max(0f, manager.GetSelectedDrivingSpeedMetersPerSecond());
        float multiplier = Mathf.Max(0.01f, selectedSpeedSimulationMultiplier);

        return Mathf.Max(0.01f, (metresPerSecond / metresPerUnit) * multiplier);
    }

    private float GetFallbackSpeedKmh()
    {
        float metresPerSecond =
            Mathf.Max(0f, speed) * Mathf.Max(0f, metersPerUnityUnit);

        return metresPerSecond * 3.6f;
    }

    private void FollowManualUTurnRoute()
    {
        if (!manualUTurnActive)
            return;

        if (manualUTurnRoutePoints.Count == 0)
        {
            CompleteManualUTurn();
            return;
        }

        Vector3 targetPoint = manualUTurnRoutePoints.Peek();

        Vector2 direction =
            (Vector2)targetPoint - (Vector2)transform.position;

        if (direction.sqrMagnitude > 0.001f)
            RotateCar(direction);

        transform.position = Vector2.MoveTowards(
            transform.position,
            targetPoint,
            GetCurrentMovementSpeedUnitsPerSecond() * Time.deltaTime
        );

        if (Vector2.Distance(transform.position, targetPoint) <= arriveDistance)
        {
            transform.position = targetPoint;
            manualUTurnRoutePoints.Dequeue();

            if (manualUTurnRoutePoints.Count == 0)
                CompleteManualUTurn();
        }
    }

    private void CompleteManualUTurn()
    {
        manualUTurnActive = false;
        manualUTurnRoutePoints.Clear();

        ContinuousUTurnPath.PathProjection stopProjection =
            manualUTurnStopProjection.IsValid
                ? manualUTurnStopProjection
                : manualUTurnLandingProjection;

        if (!stopProjection.IsValid)
        {
            manualUTurnLandingProjection = default;
            manualUTurnStopProjection = default;
            RefreshUndoButtons();
            RefreshUTurnButtonState();
            return;
        }

        // The car is physically stopped BETWEEN RoadNodes on the opposite lane.
        // Keep the node behind the car as currentNode, but do NOT enqueue the
        // node ahead. That prevents automatic driving after the U-turn.
        currentNode = stopProjection.fromNode;
        previousNode = null;
        pathQueue.Clear();

        manualArrowNavigationActive = true;
        waitingAtNodeChoice = false;
        currentStoppedChoiceNode = null;
        selectedTargetNode = null;
        isBraking = false;

        manualUTurnLandingProjection = default;
        manualUTurnStopProjection = default;

        // Show one normal movement arrow in the legal forward direction of
        // the opposite lane. The participant must click it to continue, just
        // like the arrows shown when stopping at a RoadNode.
        StopAtVirtualPointAndShowForwardArrow(stopProjection);

        RefreshUndoButtons();
        RefreshUTurnButtonState();

        if (debugLogs)
        {
            Debug.Log(
                "[CarNodeFollower] Manual U-turn complete. Car stopped " +
                manualUTurnPostTurnStopDistanceMeters.ToString("0.#") +
                " m along the opposite lane and is waiting for the forward arrow."
            );
        }
    }

    /// <summary>
    /// Adds a requested forward distance along a configured one-way path,
    /// starting from an arbitrary mid-edge projection. The final projection
    /// describes the exact virtual stop point between nodes.
    /// </summary>
    private bool TryAppendForwardDistanceOnConfiguredPath(
        ContinuousUTurnPath path,
        ContinuousUTurnPath.PathProjection startProjection,
        float distanceUnity,
        List<Vector3> routePoints,
        out ContinuousUTurnPath.PathProjection stopProjection)
    {
        stopProjection = startProjection;

        if (path == null || !startProjection.IsValid)
            return false;

        float remaining = Mathf.Max(0f, distanceUnity);

        RoadNode fromNode = startProjection.fromNode;
        RoadNode toNode = startProjection.toNode;
        float startT = Mathf.Clamp01(startProjection.t);

        // With a zero requested distance, stop exactly at the landing point.
        if (remaining <= 0.0001f)
        {
            stopProjection.point = startProjection.point;
            stopProjection.distance = 0f;
            return true;
        }

        int safety = 0;

        while (fromNode != null && toNode != null && safety++ < 100)
        {
            Vector2 a = fromNode.transform.position;
            Vector2 b = toNode.transform.position;
            Vector2 segment = b - a;
            float segmentLength = segment.magnitude;

            if (segmentLength <= 0.0001f)
                return false;

            float distanceToSegmentEnd = (1f - startT) * segmentLength;

            if (remaining < distanceToSegmentEnd - 0.0001f)
            {
                float finalT = startT + (remaining / segmentLength);
                Vector2 finalPoint = Vector2.Lerp(a, b, finalT);

                Vector3 worldPoint = new Vector3(
                    finalPoint.x,
                    finalPoint.y,
                    transform.position.z
                );

                AppendRoutePoint(routePoints, worldPoint);

                stopProjection = new ContinuousUTurnPath.PathProjection
                {
                    fromNode = fromNode,
                    toNode = toNode,
                    point = worldPoint,
                    t = finalT,
                    distance = 0f
                };

                return true;
            }

            // Reach the next RoadNode and continue along the configured path.
            Vector3 nodePoint = toNode.transform.position;
            nodePoint.z = transform.position.z;
            AppendRoutePoint(routePoints, nodePoint);

            remaining -= Mathf.Max(0f, distanceToSegmentEnd);

            RoadNode previousFrom = fromNode;
            RoadNode reachedNode = toNode;
            RoadNode nextNode = FindBestForwardNodeOnConfiguredPath(
                path,
                reachedNode,
                previousFrom
            );

            if (nextNode == null)
            {
                // The configured U-turn path ends here. We can stop at the node,
                // but there is no forward arrow to show, so treat it as invalid
                // for the manual U-turn behavior requested by the participant.
                return false;
            }

            fromNode = reachedNode;
            toNode = nextNode;
            startT = 0f;

            if (remaining <= 0.0001f)
            {
                Vector3 stopPoint = fromNode.transform.position;
                stopPoint.z = transform.position.z;

                stopProjection = new ContinuousUTurnPath.PathProjection
                {
                    fromNode = fromNode,
                    toNode = toNode,
                    point = stopPoint,
                    t = 0f,
                    distance = 0f
                };

                return true;
            }
        }

        return false;
    }

    private RoadNode FindBestForwardNodeOnConfiguredPath(
        ContinuousUTurnPath path,
        RoadNode fromNode,
        RoadNode previousNodeOnPath)
    {
        if (path == null ||
            fromNode == null ||
            fromNode.connectedNodes == null)
        {
            return null;
        }

        Vector2 incomingDirection = Vector2.zero;

        if (previousNodeOnPath != null)
        {
            incomingDirection =
                (Vector2)fromNode.transform.position -
                (Vector2)previousNodeOnPath.transform.position;

            if (incomingDirection.sqrMagnitude > 0.0001f)
                incomingDirection.Normalize();
        }

        RoadNode bestNode = null;
        float bestAlignment = -Mathf.Infinity;

        foreach (RoadNode candidate in fromNode.connectedNodes)
        {
            if (candidate == null ||
                !path.ContainsDirectedSegment(fromNode, candidate))
            {
                continue;
            }

            Vector2 outgoingDirection =
                (Vector2)candidate.transform.position -
                (Vector2)fromNode.transform.position;

            if (outgoingDirection.sqrMagnitude <= 0.0001f)
                continue;

            outgoingDirection.Normalize();

            float alignment =
                incomingDirection.sqrMagnitude > 0.0001f
                    ? Vector2.Dot(incomingDirection, outgoingDirection)
                    : 1f;

            if (bestNode == null || alignment > bestAlignment)
            {
                bestNode = candidate;
                bestAlignment = alignment;
            }
        }

        return bestNode;
    }

    /// <summary>
    /// Shows the same movement-arrow prefab used at RoadNodes, but places it
    /// relative to the car's current virtual mid-edge stop position. Only the
    /// legal forward node of the configured opposite lane is offered.
    /// </summary>
    private void StopAtVirtualPointAndShowForwardArrow(
        ContinuousUTurnPath.PathProjection projection)
    {
        if (!projection.IsValid)
            return;

        if (movementArrowPrefab == null)
        {
            Debug.LogWarning(
                "[CarNodeFollower] Movement Arrow Prefab is not assigned. " +
                "The car will remain stopped after the manual U-turn."
            );
            return;
        }

        RoadNode nextNode = projection.toNode;

        if (nextNode == null)
            return;

        Vector3 direction =
            nextNode.transform.position - transform.position;

        direction.z = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();

        waitingAtNodeChoice = true;
        currentStoppedChoiceNode = projection.fromNode;
        manualArrowNavigationActive = true;
        isBraking = false;

        ClearMovementArrows();

        Vector3 arrowPosition =
            transform.position + direction * movementArrowDistance;
        arrowPosition.z = transform.position.z;

        GameObject arrow = Instantiate(
            movementArrowPrefab,
            arrowPosition,
            Quaternion.identity
        );

        arrow.name = "MovementArrow_To_" + nextNode.nodeId;
        arrow.transform.localScale = Vector3.one * movementArrowScale;

        float angle =
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        arrow.transform.rotation = Quaternion.Euler(
            0f,
            0f,
            angle + movementArrowRotationOffset
        );

        SpriteRenderer spriteRenderer =
            arrow.GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.sortingOrder = movementArrowSortingOrder;

        if (arrow.GetComponentInChildren<Collider2D>() == null)
            arrow.AddComponent<BoxCollider2D>();

        MovementArrow movementArrow =
            arrow.GetComponentInChildren<MovementArrow>();

        if (movementArrow != null)
            movementArrow.Setup(this, nextNode);

        spawnedMovementArrows.Add(arrow);
        movementArrowTargets[arrow] = nextNode;

        if (debugLogs)
        {
            Debug.Log(
                "[CarNodeFollower] Manual U-turn stop reached. " +
                "Forward movement arrow shown toward: " +
                nextNode.nodeId
            );
        }
    }

    private void RefreshUTurnButtonState()
    {
        if (uTurnButton == null ||
            !automaticallySetUTurnButtonInteractable)
        {
            return;
        }

        uTurnButton.interactable = CanManualUTurnNow();
    }

    public bool TryGetParkingRoutePreview(
        ParkingSpot spot,
        out float distanceMeters,
        out string failureReason)
    {
        distanceMeters = 0f;
        failureReason = string.Empty;

        if (spot == null)
        {
            failureReason = "Parking spot is null.";
            return false;
        }

        if (manualUTurnActive)
        {
            failureReason = "A manual U-turn is currently in progress.";
            return false;
        }

        if (TryBuildOppositeLaneParkingPlan(
                spot,
                out OppositeLaneParkingPlan oppositeLanePlan))
        {
            distanceMeters =
                oppositeLanePlan.totalDistanceUnity *
                Mathf.Max(0f, metersPerUnityUnit);

            return true;
        }

        if (!TryBuildParkingRoute(
                spot,
                out RoadNode routeTargetNode,
                out Vector3 parkingStopPosition,
                out List<RoadNode> queuedPath,
                out float totalDistanceMeters,
                out failureReason))
        {
            return false;
        }

        distanceMeters = totalDistanceMeters;
        return true;
    }

    public bool RouteToParkingSpot(ParkingSpot spot)
    {
        if (spot == null || isUndoing || manualUTurnActive)
            return false;

        if (IsRouteActive() && !allowParkingClickToInterruptRoute)
        {
            if (debugLogs)
            {
                Debug.Log(
                    "[CarNodeFollower] Parking click ignored because the car is already moving."
                );
            }

            return false;
        }

        if (TryBuildOppositeLaneParkingPlan(
                spot,
                out OppositeLaneParkingPlan oppositeLanePlan))
        {
            return StartOppositeLaneParkingRoute(
                spot,
                oppositeLanePlan
            );
        }

        if (!TryBuildParkingRoute(
                spot,
                out RoadNode routeTargetNode,
                out Vector3 parkingStopPosition,
                out List<RoadNode> queuedPath,
                out float totalDistanceMeters,
                out string failureReason))
        {
            Debug.LogWarning(
                $"[CarNodeFollower] Cannot route to {spot.spotId}: {failureReason}"
            );

            return false;
        }

        // TryBuildParkingRoute returns an empty queued path for both direct-forward
        // parking and direct-reverse parking. Re-check the reverse condition here
        // BEFORE clearing the current route, because pathQueue tells us which way
        // the car is currently travelling along the road edge.
        bool shouldReverseDirectly =
            queuedPath.Count == 0 &&
            allowDirectReverseParkingBehind &&
            TryGetDirectParkingBehindDistance(
                parkingStopPosition,
                out float ignoredReverseDistanceUnity);

        RecordUndoState();

        ClearCurrentRouteWithoutClearingUndo();

        pendingParkingSpot = spot;
        selectedTargetNode = routeTargetNode;
        virtualParkingStopPosition = parkingStopPosition;
        virtualParkingStopPosition.z = transform.position.z;
        useVirtualParkingStopAfterNodeRoute = true;
        movingToVirtualParkingStop = false;
        reversingToVirtualParkingStop = shouldReverseDirectly;
        automaticParkingRouteActive = true;
        plannedParkingRouteDistanceMeters = totalDistanceMeters;
        isBraking = false;

        foreach (RoadNode node in queuedPath)
        {
            if (node != null)
                pathQueue.Enqueue(node);
        }

        if (selectedTargetMarker != null)
        {
            selectedTargetMarker.position = virtualParkingStopPosition;
            selectedTargetMarker.gameObject.SetActive(true);
        }

        RefreshUndoButtons();

        if (debugLogs)
        {
            Debug.Log(
                $"[CarNodeFollower] Automatic parking route started | " +
                $"spot={spot.spotId}, targetNode={routeTargetNode.nodeId}, " +
                $"routeDistance={totalDistanceMeters:0.0} m, " +
                $"queuedNodes={pathQueue.Count}, " +
                $"gear={(reversingToVirtualParkingStop ? "R (دنده عقب)" : "D")}"
            );
        }

        if (pathQueue.Count == 0)
            OnNodeRouteFinished();

        return true;
    }

    /// <summary>
    /// Builds the special configured-street route only when the selected ParkingSpot
    /// belongs to the paired OPPOSITE directional path. The route is made from
    /// world-space points, so the U-turn can happen anywhere between RoadNodes.
    /// Same-lane parking and every other street fall through to the normal
    /// RoadNode pathfinder unchanged.
    /// </summary>
    private bool TryBuildOppositeLaneParkingPlan(
        ParkingSpot spot,
        out OppositeLaneParkingPlan bestPlan)
    {
        bestPlan = null;

        if (!enableOppositeLaneParkingUTurn ||
            spot == null ||
            !spot.HasParkingSegment())
        {
            return false;
        }

        ContinuousUTurnPath targetPath =
            ContinuousUTurnPath.FindPathContainingDirectedSegment(
                spot.parkingSegmentStartNode,
                spot.parkingSegmentEndNode
            );

        if (targetPath == null ||
            !targetPath.allowParkingUTurns ||
            targetPath.oppositePath == null)
        {
            return false;
        }

        ContinuousUTurnPath sourcePath = targetPath.oppositePath;

        if (!sourcePath.allowParkingUTurns)
            return false;

        // Critical same-lane/opposite-lane test:
        // this succeeds only when the car is currently travelling on the path
        // paired opposite to the selected parking spot's path.
        if (!TryGetCurrentProjectionOnPath(
                sourcePath,
                out ContinuousUTurnPath.PathProjection currentProjection))
        {
            return false;
        }

        Vector3 parkingStopPosition = spot.GetParkingStopPoint();
        parkingStopPosition.z = transform.position.z;

        if (!targetPath.TryProjectOntoDirectedSegment(
                spot.parkingSegmentStartNode,
                spot.parkingSegmentEndNode,
                parkingStopPosition,
                out ContinuousUTurnPath.PathProjection parkingProjection))
        {
            return false;
        }

        List<OppositeLaneParkingPlan> candidates =
            new List<OppositeLaneParkingPlan>();

        // Candidate 1: continue forward on the current lane until roughly
        // opposite the selected parking spot, then U-turn there. This is the
        // preferred behavior when the spot is still ahead of the car.
        if (sourcePath.TryGetNearestDirectedSegment(
                parkingStopPosition,
                out ContinuousUTurnPath.PathProjection alignedDeparture) &&
            TryBuildOppositeLaneUTurnCandidate(
                sourcePath,
                targetPath,
                currentProjection,
                alignedDeparture,
                false,
                parkingStopPosition,
                parkingProjection,
                out OppositeLaneParkingPlan alignedPlan))
        {
            candidates.Add(alignedPlan);
        }

        // Candidate 2: U-turn immediately at the car's CURRENT exact position.
        // This is ideal when the opposite-lane spot has already been passed.
        if (TryBuildOppositeLaneUTurnCandidate(
                sourcePath,
                targetPath,
                currentProjection,
                currentProjection,
                true,
                parkingStopPosition,
                parkingProjection,
                out OppositeLaneParkingPlan immediatePlan))
        {
            candidates.Add(immediatePlan);
        }

        if (candidates.Count == 0)
            return false;

        bestPlan = candidates[0];

        for (int i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].totalDistanceUnity < bestPlan.totalDistanceUnity)
                bestPlan = candidates[i];
        }

        if (debugLogs)
        {
            Debug.Log(
                "[CarNodeFollower] Opposite-lane parking U-turn plan selected | " +
                "spot=" + spot.spotId +
                ", source=" + sourcePath.pathLabel +
                ", target=" + targetPath.pathLabel +
                ", route=" +
                (bestPlan.totalDistanceUnity * Mathf.Max(0f, metersPerUnityUnit)).ToString("0.0") +
                " m"
            );
        }

        return true;
    }

    private bool TryGetCurrentProjectionOnPath(
        ContinuousUTurnPath path,
        out ContinuousUTurnPath.PathProjection projection)
    {
        projection = default;

        if (path == null || currentNode == null)
            return false;

        // Normal mid-edge case. currentNode is the node behind the car and the
        // queue head is the node currently being approached.
        if (pathQueue.Count > 0)
        {
            RoadNode forwardNode = pathQueue.Peek();

            if (!path.ContainsDirectedSegment(currentNode, forwardNode))
                return false;

            if (!path.TryProjectOntoDirectedSegment(
                    currentNode,
                    forwardNode,
                    transform.position,
                    out projection))
            {
                return false;
            }

            return projection.distance <=
                   Mathf.Max(0f, maxDistanceFromConfiguredUTurnPath);
        }

        // If the car is stopped exactly at a configured U-turn node, use that node's
        // outgoing segment on the configured directional path.
        if (!path.ContainsNode(currentNode) || currentNode.connectedNodes == null)
            return false;

        bool found = false;
        float bestDistance = Mathf.Infinity;

        foreach (RoadNode nextNode in currentNode.connectedNodes)
        {
            if (!path.ContainsDirectedSegment(currentNode, nextNode))
                continue;

            if (!path.TryProjectOntoDirectedSegment(
                    currentNode,
                    nextNode,
                    transform.position,
                    out ContinuousUTurnPath.PathProjection candidate))
            {
                continue;
            }

            if (candidate.distance < bestDistance)
            {
                bestDistance = candidate.distance;
                projection = candidate;
                found = true;
            }
        }

        return found &&
               bestDistance <= Mathf.Max(0f, maxDistanceFromConfiguredUTurnPath);
    }

    private bool TryBuildOppositeLaneUTurnCandidate(
        ContinuousUTurnPath sourcePath,
        ContinuousUTurnPath targetPath,
        ContinuousUTurnPath.PathProjection currentProjection,
        ContinuousUTurnPath.PathProjection departureProjection,
        bool immediateUTurn,
        Vector3 parkingStopPosition,
        ContinuousUTurnPath.PathProjection parkingProjection,
        out OppositeLaneParkingPlan plan)
    {
        plan = null;

        if (sourcePath == null ||
            targetPath == null ||
            !currentProjection.IsValid ||
            !departureProjection.IsValid ||
            !parkingProjection.IsValid)
        {
            return false;
        }

        OppositeLaneParkingPlan candidate = new OppositeLaneParkingPlan
        {
            sourcePath = sourcePath,
            targetPath = targetPath
        };

        Vector3 departurePoint;
        Vector2 sourceDirection;

        if (immediateUTurn)
        {
            // Preserve the car's precise mid-edge position. No node snapping.
            departurePoint = transform.position;
            sourceDirection =
                (Vector2)currentProjection.toNode.transform.position -
                (Vector2)currentProjection.fromNode.transform.position;
        }
        else
        {
            departurePoint = departureProjection.point;
            departurePoint.z = transform.position.z;

            sourceDirection =
                (Vector2)departureProjection.toNode.transform.position -
                (Vector2)departureProjection.fromNode.transform.position;

            bool sameSegment =
                currentProjection.fromNode == departureProjection.fromNode &&
                currentProjection.toNode == departureProjection.toNode;

            if (sameSegment)
            {
                // The aligned point must still be ahead on the current edge.
                // If it is behind, the immediate candidate handles it instead.
                if (departureProjection.t + 0.001f < currentProjection.t)
                    return false;

                AppendRoutePoint(candidate.routePoints, departurePoint);
            }
            else
            {
                // Finish the edge currently being driven, then remain on the
                // same directional configured path until the arbitrary departure
                // point is reached.
                if (!sourcePath.TryFindDirectedNodePath(
                        currentProjection.toNode,
                        departureProjection.fromNode,
                        out List<RoadNode> sourceNodePath))
                {
                    return false;
                }

                AppendRoutePoint(
                    candidate.routePoints,
                    currentProjection.toNode.transform.position
                );

                for (int i = 1; i < sourceNodePath.Count; i++)
                {
                    AppendRoutePoint(
                        candidate.routePoints,
                        sourceNodePath[i].transform.position
                    );
                }

                AppendRoutePoint(candidate.routePoints, departurePoint);
            }
        }

        if (sourceDirection.sqrMagnitude <= 0.0001f)
            return false;

        if (!targetPath.TryGetNearestDirectedSegment(
                departurePoint,
                out ContinuousUTurnPath.PathProjection landingProjection))
        {
            return false;
        }

        float crossingDistance = Vector2.Distance(
            departurePoint,
            landingProjection.point
        );

        float maxCrossingDistance =
            Mathf.Max(0f, sourcePath.maxUTurnCrossingDistance);

        if (maxCrossingDistance > 0f &&
            crossingDistance > maxCrossingDistance)
        {
            return false;
        }

        Vector3 landingPoint = landingProjection.point;
        landingPoint.z = transform.position.z;

        Vector2 targetDirection =
            (Vector2)landingProjection.toNode.transform.position -
            (Vector2)landingProjection.fromNode.transform.position;

        if (targetDirection.sqrMagnitude <= 0.0001f)
            return false;

        candidate.uTurnDeparturePoint = departurePoint;
        candidate.uTurnLandingPoint = landingPoint;

        AppendUTurnCurvePoints(
            candidate.routePoints,
            departurePoint,
            landingPoint,
            sourceDirection.normalized,
            targetDirection.normalized
        );

        bool landingOnParkingSegment =
            landingProjection.fromNode == parkingProjection.fromNode &&
            landingProjection.toNode == parkingProjection.toNode;

        if (landingOnParkingSegment &&
            parkingProjection.t + 0.001f >= landingProjection.t)
        {
            // The chosen spot is already ahead on this same opposite-lane edge.
            AppendRoutePoint(candidate.routePoints, parkingStopPosition);
        }
        else
        {
            // Continue in the opposite lane's legal driving direction until the
            // start of the selected parking segment, then drive into the spot.
            if (!targetPath.TryFindDirectedNodePath(
                    landingProjection.toNode,
                    parkingProjection.fromNode,
                    out List<RoadNode> targetNodePath))
            {
                return false;
            }

            AppendRoutePoint(
                candidate.routePoints,
                landingProjection.toNode.transform.position
            );

            for (int i = 1; i < targetNodePath.Count; i++)
            {
                AppendRoutePoint(
                    candidate.routePoints,
                    targetNodePath[i].transform.position
                );
            }

            AppendRoutePoint(candidate.routePoints, parkingStopPosition);
        }

        if (candidate.routePoints.Count == 0)
            return false;

        candidate.totalDistanceUnity = CalculateWorldRouteLengthUnity(
            transform.position,
            candidate.routePoints
        );

        plan = candidate;
        return true;
    }

    private void AppendUTurnCurvePoints(
        List<Vector3> routePoints,
        Vector3 start,
        Vector3 end,
        Vector2 sourceDirection,
        Vector2 targetDirection)
    {
        int samples = Mathf.Clamp(uTurnCurveSamples, 4, 40);
        float forward = Mathf.Max(0f, uTurnCurveForwardDistance);

        Vector3 control1 = start +
            (Vector3)(sourceDirection.normalized * forward);

        // End tangent points in targetDirection. Therefore the second control
        // point sits behind the end point relative to the target direction.
        Vector3 control2 = end -
            (Vector3)(targetDirection.normalized * forward);

        control1.z = transform.position.z;
        control2.z = transform.position.z;

        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            float u = 1f - t;

            Vector3 point =
                (u * u * u) * start +
                (3f * u * u * t) * control1 +
                (3f * u * t * t) * control2 +
                (t * t * t) * end;

            point.z = transform.position.z;
            AppendRoutePoint(routePoints, point);
        }
    }

    private static void AppendRoutePoint(
        List<Vector3> routePoints,
        Vector3 point)
    {
        if (routePoints == null)
            return;

        if (routePoints.Count > 0 &&
            Vector2.Distance(routePoints[routePoints.Count - 1], point) <= 0.001f)
        {
            return;
        }

        routePoints.Add(point);
    }

    private static float CalculateWorldRouteLengthUnity(
        Vector3 start,
        List<Vector3> routePoints)
    {
        if (routePoints == null || routePoints.Count == 0)
            return 0f;

        float total = 0f;
        Vector2 cursor = start;

        foreach (Vector3 point in routePoints)
        {
            total += Vector2.Distance(cursor, (Vector2)point);
            cursor = point;
        }

        return total;
    }

    private bool StartOppositeLaneParkingRoute(
        ParkingSpot spot,
        OppositeLaneParkingPlan plan)
    {
        if (spot == null ||
            plan == null ||
            plan.routePoints == null ||
            plan.routePoints.Count == 0)
        {
            return false;
        }

        RecordUndoState();
        ClearCurrentRouteWithoutClearingUndo();

        pendingParkingSpot = spot;
        selectedTargetNode = spot.parkingSegmentStartNode;
        automaticParkingRouteActive = true;
        plannedParkingRouteDistanceMeters =
            plan.totalDistanceUnity * Mathf.Max(0f, metersPerUnityUnit);

        movingToVirtualParkingStop = false;
        useVirtualParkingStopAfterNodeRoute = false;
        reversingToVirtualParkingStop = false;
        oppositeLaneParkingUTurnActive = true;
        isBraking = false;

        foreach (Vector3 point in plan.routePoints)
        {
            Vector3 routePoint = point;
            routePoint.z = transform.position.z;
            oppositeLaneParkingRoutePoints.Enqueue(routePoint);
        }

        if (selectedTargetMarker != null)
        {
            Vector3 parkingStop = spot.GetParkingStopPoint();
            parkingStop.z = transform.position.z;
            selectedTargetMarker.position = parkingStop;
            selectedTargetMarker.gameObject.SetActive(true);
        }

        RefreshUndoButtons();

        if (debugLogs)
        {
            Debug.Log(
                "[CarNodeFollower] Mid-edge opposite-lane parking U-turn started | " +
                "spot=" + spot.spotId +
                ", departure=(" +
                plan.uTurnDeparturePoint.x.ToString("0.##") + "," +
                plan.uTurnDeparturePoint.y.ToString("0.##") + ")" +
                ", landing=(" +
                plan.uTurnLandingPoint.x.ToString("0.##") + "," +
                plan.uTurnLandingPoint.y.ToString("0.##") + ")" +
                ", distance=" + plannedParkingRouteDistanceMeters.ToString("0.0") + " m"
            );
        }

        return true;
    }

    private void FollowOppositeLaneParkingRoute()
    {
        if (!oppositeLaneParkingUTurnActive)
            return;

        if (oppositeLaneParkingRoutePoints.Count == 0)
        {
            oppositeLaneParkingUTurnActive = false;
            FinishRouteCompletely();
            return;
        }

        Vector3 targetPoint = oppositeLaneParkingRoutePoints.Peek();
        Vector2 direction =
            (Vector2)targetPoint - (Vector2)transform.position;

        if (direction.sqrMagnitude > 0.001f)
            RotateCar(direction);

        transform.position = Vector2.MoveTowards(
            transform.position,
            targetPoint,
            GetCurrentMovementSpeedUnitsPerSecond() * Time.deltaTime
        );

        if (Vector2.Distance(transform.position, targetPoint) <= arriveDistance)
        {
            transform.position = targetPoint;
            oppositeLaneParkingRoutePoints.Dequeue();

            if (oppositeLaneParkingRoutePoints.Count == 0)
            {
                oppositeLaneParkingUTurnActive = false;

                if (debugLogs)
                {
                    Debug.Log(
                        "[CarNodeFollower] Opposite-lane U-turn parking route finished."
                    );
                }

                FinishRouteCompletely();
            }
        }
    }

    private bool TryBuildParkingRoute(
        ParkingSpot spot,
        out RoadNode routeTargetNode,
        out Vector3 parkingStopPosition,
        out List<RoadNode> queuedPath,
        out float totalDistanceMeters,
        out string failureReason)
    {
        routeTargetNode = null;
        parkingStopPosition = transform.position;
        queuedPath = new List<RoadNode>();
        totalDistanceMeters = 0f;
        failureReason = string.Empty;

        if (spot == null)
        {
            failureReason = "Parking spot is null.";
            return false;
        }

        if (currentNode == null)
        {
            failureReason = "Current RoadNode is null.";
            return false;
        }

        // First determine the actual point where the car should stop.
        // If the parking spot belongs to a parking segment, use its configured
        // stop point. Otherwise use the spot transform itself.
        parkingStopPosition = spot.HasParkingSegment()
            ? spot.GetParkingStopPoint()
            : spot.transform.position;

        // REVERSE SPECIAL CASE (R / دنده عقب):
        // If the parking stop is behind the car on the SAME road edge and is
        // within maxDirectReverseParkingDistanceMeters, allow the car to back
        // directly into the spot. This avoids making a large one-way loop just
        // to reach a spot that is only a few metres behind the car.
        if (allowDirectReverseParkingBehind &&
            TryGetDirectParkingBehindDistance(
                parkingStopPosition,
                out float reverseDistanceUnity))
        {
            routeTargetNode = currentNode;
            totalDistanceMeters =
                reverseDistanceUnity * Mathf.Max(0f, metersPerUnityUnit);

            if (debugLogs)
            {
                Debug.Log(
                    $"[CarNodeFollower] Parking spot is behind the car and within " +
                    $"{maxDirectReverseParkingDistanceMeters:0.#} m. " +
                    $"Using R (دنده عقب) and reversing directly. " +
                    $"Distance={totalDistanceMeters:0.0} m"
                );
            }

            return true;
        }

        // IMPORTANT SPECIAL CASE:
        // If the car is already travelling along an edge and the selected
        // parking stop is physically ahead of the car on that SAME edge,
        // there is no reason to continue to the next RoadNode and then ask
        // the graph pathfinder to return to parkingSegmentStartNode. Doing
        // that on a one-way network can create a large loop even though the
        // parking spot is directly in front of the car.
        //
        // Returning an empty queuedPath makes RouteToParkingSpot immediately
        // switch to FollowVirtualParkingStop(), so the car simply drives from
        // its current position into the selected parking spot.
        if (allowDirectParkingAheadOnCurrentEdge &&
            TryGetDirectParkingAheadDistance(
                parkingStopPosition,
                out float directDistanceUnity))
        {
            routeTargetNode = currentNode;
            totalDistanceMeters =
                directDistanceUnity * Mathf.Max(0f, metersPerUnityUnit);

            if (debugLogs)
            {
                Debug.Log(
                    $"[CarNodeFollower] Parking spot is directly ahead on the current edge. " +
                    $"Skipping RoadNode loop and parking directly. Distance={totalDistanceMeters:0.0} m"
                );
            }

            return true;
        }

        if (spot.HasParkingSegment())
        {
            // Normal parking routing: parkingSegmentStartNode is the node
            // before the parking row in the permitted driving direction.
            routeTargetNode = spot.parkingSegmentStartNode;
        }
        else
        {
            routeTargetNode = spot.nearestRoadNode;

            if (routeTargetNode == null)
                routeTargetNode = FindNearestNode(spot.transform.position);
        }

        if (routeTargetNode == null)
        {
            failureReason = "No RoadNode is assigned near this parking spot.";
            return false;
        }

        RoadNode routingAnchor = GetForwardRoutingAnchor(
            out bool includeAnchorInQueue,
            out float prefixDistanceUnity
        );

        if (routingAnchor == null)
        {
            failureReason = "No valid routing anchor is available.";
            return false;
        }

        RoadNode blockedFirstStep =
            includeAnchorInQueue
                ? currentNode
                : null;

        List<RoadNode> path = FindPath(
            routingAnchor,
            routeTargetNode,
            blockedFirstStep
        );

        if (path == null || path.Count == 0)
        {
            failureReason =
                $"No directed RoadNode path exists from {routingAnchor.nodeId} " +
                $"to {routeTargetNode.nodeId}.";

            return false;
        }

        if (includeAnchorInQueue)
            queuedPath.Add(routingAnchor);

        for (int i = 1; i < path.Count; i++)
            queuedPath.Add(path[i]);

        float nodePathDistanceUnity = CalculatePathLengthUnity(path);
        float finalApproachDistanceUnity = Vector2.Distance(
            routeTargetNode.transform.position,
            parkingStopPosition
        );

        float totalUnity =
            prefixDistanceUnity +
            nodePathDistanceUnity +
            finalApproachDistanceUnity;

        totalDistanceMeters =
            totalUnity * Mathf.Max(0f, metersPerUnityUnit);

        return true;
    }

    /// <summary>
    /// Detects the case where the car is currently between currentNode and
    /// the next queued RoadNode, and the selected parking position is ahead
    /// of the car on that same road edge.
    ///
    /// This prevents a one-way-network loop such as:
    /// car -> next node -> several nodes around the block -> parking segment
    /// start node -> parking spot,
    /// when the parking spot was already directly in front of the car.
    /// </summary>
    private bool TryGetDirectParkingAheadDistance(
        Vector3 parkingStopPosition,
        out float directDistanceUnity)
    {
        directDistanceUnity = 0f;

        if (currentNode == null || pathQueue.Count == 0)
            return false;

        RoadNode forwardNode = pathQueue.Peek();

        if (forwardNode == null)
            return false;

        Vector2 edgeStart = currentNode.transform.position;
        Vector2 edgeEnd = forwardNode.transform.position;
        Vector2 edge = edgeEnd - edgeStart;

        float edgeLengthSquared = edge.sqrMagnitude;

        if (edgeLengthSquared <= 0.0001f)
            return false;

        Vector2 carPosition = transform.position;
        Vector2 stopPosition = parkingStopPosition;

        // Position of the car and parking stop projected onto the directed
        // current road edge. 0 = currentNode, 1 = forwardNode.
        float carT = Vector2.Dot(carPosition - edgeStart, edge) / edgeLengthSquared;
        float stopT = Vector2.Dot(stopPosition - edgeStart, edge) / edgeLengthSquared;

        // The stop must belong to the current edge, not to a different road
        // farther ahead or behind. A small projection margin handles minor
        // placement inaccuracies in the scene.
        const float projectionMargin = 0.05f;

        if (stopT < -projectionMargin || stopT > 1f + projectionMargin)
            return false;

        Vector2 projectedStop = edgeStart + edge * Mathf.Clamp01(stopT);
        float sidewaysDistance = Vector2.Distance(stopPosition, projectedStop);

        if (sidewaysDistance > Mathf.Max(0f, directParkingEdgeTolerance))
            return false;

        // Use distance along the directed road edge to decide "ahead".
        float edgeLength = Mathf.Sqrt(edgeLengthSquared);
        float aheadDistanceAlongEdge = (stopT - carT) * edgeLength;

        if (aheadDistanceAlongEdge <= Mathf.Max(0f, directParkingMinimumAheadDistance))
            return false;

        directDistanceUnity = Vector2.Distance(carPosition, stopPosition);
        return true;
    }

    /// <summary>
    /// Detects a parking stop that lies behind the car on the same directed
    /// RoadNode edge and is close enough to reach using reverse gear.
    /// The maximum reverse distance is specified in REAL metres and converted
    /// using metersPerUnityUnit.
    /// </summary>
    private bool TryGetDirectParkingBehindDistance(
        Vector3 parkingStopPosition,
        out float directDistanceUnity)
    {
        directDistanceUnity = 0f;

        // During the normal driving/confirmation case, pathQueue.Peek() is the
        // forward RoadNode. We need it to know what "behind" means on this edge.
        if (currentNode == null || pathQueue.Count == 0)
            return false;

        RoadNode forwardNode = pathQueue.Peek();

        if (forwardNode == null)
            return false;

        Vector2 edgeStart = currentNode.transform.position;
        Vector2 edgeEnd = forwardNode.transform.position;
        Vector2 edge = edgeEnd - edgeStart;

        float edgeLengthSquared = edge.sqrMagnitude;

        if (edgeLengthSquared <= 0.0001f)
            return false;

        Vector2 carPosition = transform.position;
        Vector2 stopPosition = parkingStopPosition;

        // Project both positions onto the current directed road edge.
        // 0 = currentNode, 1 = forwardNode.
        float carT = Vector2.Dot(carPosition - edgeStart, edge) / edgeLengthSquared;
        float stopT = Vector2.Dot(stopPosition - edgeStart, edge) / edgeLengthSquared;

        const float projectionMargin = 0.05f;

        // The spot must still belong to this same edge.
        if (stopT < -projectionMargin || stopT > 1f + projectionMargin)
            return false;

        Vector2 projectedStop = edgeStart + edge * Mathf.Clamp01(stopT);
        float sidewaysDistance = Vector2.Distance(stopPosition, projectedStop);

        // Reuse the same tolerance as direct-forward parking, because roadside
        // parking spots are usually offset slightly from the RoadNode centreline.
        if (sidewaysDistance > Mathf.Max(0f, directParkingEdgeTolerance))
            return false;

        float edgeLength = Mathf.Sqrt(edgeLengthSquared);
        float behindDistanceAlongEdge = (carT - stopT) * edgeLength;

        // Must actually be behind the car, not beside or ahead of it.
        if (behindDistanceAlongEdge <= Mathf.Max(0f, directParkingMinimumAheadDistance))
            return false;

        directDistanceUnity = Vector2.Distance(carPosition, stopPosition);

        float metresPerUnit = Mathf.Max(0f, metersPerUnityUnit);

        // If calibration is invalid, do not permit distance-limited reverse.
        if (metresPerUnit <= 0f)
            return false;

        float directDistanceMeters = directDistanceUnity * metresPerUnit;

        if (directDistanceMeters > Mathf.Max(0f, maxDirectReverseParkingDistanceMeters))
            return false;

        return true;
    }

    private RoadNode GetForwardRoutingAnchor(
        out bool includeAnchorInQueue,
        out float prefixDistanceUnity)
    {
        includeAnchorInQueue = false;
        prefixDistanceUnity = 0f;

        if (currentNode == null)
            return null;

        // If the car is paused halfway along a one-way edge, it must first
        // continue to the next queued node. It must not reverse to currentNode.
        if (pathQueue.Count > 0)
        {
            float distanceFromCurrentNode = Vector2.Distance(
                transform.position,
                currentNode.transform.position
            );

            if (distanceFromCurrentNode > Mathf.Max(arriveDistance * 2f, 0.01f))
            {
                RoadNode forwardNode = pathQueue.Peek();

                if (forwardNode != null)
                {
                    includeAnchorInQueue = true;
                    prefixDistanceUnity = Vector2.Distance(
                        transform.position,
                        forwardNode.transform.position
                    );

                    return forwardNode;
                }
            }
        }

        return currentNode;
    }

    private float CalculatePathLengthUnity(List<RoadNode> path)
    {
        if (path == null || path.Count < 2)
            return 0f;

        float total = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            RoadNode a = path[i];
            RoadNode b = path[i + 1];

            if (a == null || b == null)
                continue;

            total += Vector2.Distance(
                a.transform.position,
                b.transform.position
            );
        }

        return total;
    }

    public bool RouteToNode(RoadNode targetNode)
    {
        if (waitingAtNodeChoice)
        {
            ClearMovementArrows();
            waitingAtNodeChoice = false;
            currentStoppedChoiceNode = null;
            manualArrowNavigationActive = false;
        }

        if (isUndoing || IsRouteActive())
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Cannot start new route while moving, choosing an arrow, or undoing.");

            return false;
        }

        if (currentNode == null)
        {
            Debug.LogWarning("[CarNodeFollower] Current node is null.");
            return false;
        }

        if (targetNode == null)
        {
            Debug.LogWarning("[CarNodeFollower] Target node is null.");
            return false;
        }

        List<RoadNode> path = FindPath(currentNode, targetNode);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("[CarNodeFollower] No valid one-way path found to " + targetNode.nodeId);
            return false;
        }

        pathQueue.Clear();

        foreach (RoadNode node in path)
        {
            if (node == currentNode) continue;
            pathQueue.Enqueue(node);
        }

        bool willActuallyMove = pathQueue.Count > 0 || useVirtualParkingStopAfterNodeRoute;

        if (!willActuallyMove)
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Already at target node.");

            OnNodeRouteFinished();
            return true;
        }

        RecordUndoState();
        RefreshUndoButtons();

        if (pathQueue.Count == 0)
        {
            OnNodeRouteFinished();
            return true;
        }

        if (debugLogs)
        {
            Debug.Log("[CarNodeFollower] Route started to " + targetNode.nodeId + ". Path count = " + path.Count);
        }

        return true;
    }

    private void FollowPath()
    {
        RoadNode targetNode = pathQueue.Peek();

        if (targetNode == null)
        {
            pathQueue.Dequeue();
            return;
        }

        Vector3 targetPos = targetNode.transform.position;

        Vector2 direction = (Vector2)targetPos - (Vector2)transform.position;

        if (direction.sqrMagnitude > 0.001f)
        {
            RotateCar(direction);
        }

        transform.position = Vector2.MoveTowards(
            transform.position,
            targetPos,
            GetCurrentMovementSpeedUnitsPerSecond() * Time.deltaTime
        );

        if (Vector2.Distance(transform.position, targetPos) <= arriveDistance)
        {
            transform.position = targetPos;

            RoadNode oldCurrent = currentNode;
            currentNode = targetNode;
            previousNode = oldCurrent;

            pathQueue.Dequeue();

            if (debugLogs)
                Debug.Log("[CarNodeFollower] Arrived at: " + currentNode.nodeId);

            if (stopAtEveryNodeAndShowArrows &&
                !automaticParkingRouteActive)
            {
                if (pathQueue.Count > 0)
                {
                    StopAtNodeAndShowMovementArrows(currentNode);
                    return;
                }

                if (manualArrowNavigationActive)
                {
                    StopAtNodeAndShowMovementArrows(currentNode);
                    return;
                }
            }

            if (pathQueue.Count == 0)
            {
                OnNodeRouteFinished();
            }
            else
            {
                RefreshUndoButtons();
            }
        }
    }

    private void OnNodeRouteFinished()
    {
        if (debugLogs)
            Debug.Log("[CarNodeFollower] Node route finished.");

        if (pendingParkingSpot != null && useVirtualParkingStopAfterNodeRoute)
        {
            movingToVirtualParkingStop = true;
            useVirtualParkingStopAfterNodeRoute = false;

            if (debugLogs)
            {
                Debug.Log(
                    $"[CarNodeFollower] Moving from node to virtual parking stop for {pendingParkingSpot.spotId}."
                );
            }

            RefreshUndoButtons();
            return;
        }

        FinishRouteCompletely();

        if (!automaticParkingRouteActive &&
            showArrowsWhenIdleAtNode &&
            stopAtEveryNodeAndShowArrows &&
            currentNode != null)
        {
            manualArrowNavigationActive = true;
            StopAtNodeAndShowMovementArrows(currentNode);
        }
    }

    private void FollowVirtualParkingStop()
    {
        Vector2 direction = (Vector2)virtualParkingStopPosition - (Vector2)transform.position;

        // Forward parking: turn toward the parking stop as before.
        // Reverse parking (R / دنده عقب): keep the current car orientation so
        // the car visibly backs into the spot instead of turning 180 degrees.
        if (!reversingToVirtualParkingStop && direction.sqrMagnitude > 0.001f)
        {
            RotateCar(direction);
        }

        float movementSpeed = GetCurrentMovementSpeedUnitsPerSecond();

        if (reversingToVirtualParkingStop)
        {
            movementSpeed *= Mathf.Clamp(reverseParkingSpeedMultiplier, 0.1f, 1f);
        }

        transform.position = Vector2.MoveTowards(
            transform.position,
            virtualParkingStopPosition,
            movementSpeed * Time.deltaTime
        );

        if (Vector2.Distance(transform.position, virtualParkingStopPosition) <= arriveDistance)
        {
            transform.position = virtualParkingStopPosition;
            movingToVirtualParkingStop = false;

            if (debugLogs)
            {
                Debug.Log(
                    reversingToVirtualParkingStop
                        ? "[CarNodeFollower] Reverse parking complete (R / دنده عقب)."
                        : "[CarNodeFollower] Arrived at virtual parking stop point."
                );
            }

            reversingToVirtualParkingStop = false;
            FinishRouteCompletely();
        }
    }

    private void FinishRouteCompletely()
    {
        if (debugLogs)
            Debug.Log("[CarNodeFollower] Route finished.");

        if (selectedTargetMarker != null)
            selectedTargetMarker.gameObject.SetActive(false);

        selectedTargetNode = null;

        if (pendingParkingSpot != null)
        {
            ParkingSpot arrivedSpot = pendingParkingSpot;
            pendingParkingSpot = null;
            automaticParkingRouteActive = false;
            plannedParkingRouteDistanceMeters = 0f;

            if (parkingInteraction != null)
            {
                parkingInteraction.OnConfirmedParkingRouteArrived(arrivedSpot);
            }
            else
            {
                Debug.LogWarning("[CarNodeFollower] No CarParkingInteraction found.");
            }
        }
        else
        {
            automaticParkingRouteActive = false;
            plannedParkingRouteDistanceMeters = 0f;
        }

        RefreshUndoButtons();
    }

    private void StopAtNodeAndShowMovementArrows(RoadNode node)
    {
        if (node == null)
            return;

        List<RoadNode> validNextNodes = GetValidOutgoingMovementNodes(node);

        // A node with only one legal way forward is not a real decision point.
        // Keep driving through it automatically instead of stopping the car and
        // asking the participant to click a redundant single arrow.
        bool isScenarioStartNode =
            alwaysShowArrowAtStartNode &&
            startNode != null &&
            node == startNode;

        if (autoContinueThroughSingleDirectionNodes &&
            !isScenarioStartNode &&
            validNextNodes.Count == 1)
        {
            RoadNode onlyNextNode = validNextNodes[0];

            // If a planned route is already queued, it should agree with the
            // only legal outgoing direction. If it does not, fall back to the
            // normal arrow UI rather than silently changing the planned route.
            bool canAutoContinue =
                pathQueue.Count == 0 ||
                pathQueue.Peek() == onlyNextNode;

            if (canAutoContinue)
            {
                ClearMovementArrows();
                waitingAtNodeChoice = false;
                currentStoppedChoiceNode = null;
                isBraking = false;

                // Manual arrow navigation has no queued route after arriving at
                // a node, so add the only legal next node ourselves. Planned
                // routes already have their next node in the queue.
                if (pathQueue.Count == 0)
                    pathQueue.Enqueue(onlyNextNode);

                if (debugLogs)
                {
                    Debug.Log(
                        "[CarNodeFollower] Pass-through node " +
                        node.nodeId +
                        " has one legal direction. Continuing automatically to: " +
                        onlyNextNode.nodeId
                    );
                }

                RefreshUndoButtons();
                return;
            }
        }

        if (movementArrowPrefab == null)
        {
            Debug.LogWarning("[CarNodeFollower] Movement Arrow Prefab is not assigned.");
            return;
        }

        waitingAtNodeChoice = true;
        currentStoppedChoiceNode = node;
        isBraking = false;

        ClearMovementArrows();

        int arrowCount = 0;

        foreach (RoadNode nextNode in validNextNodes)
        {
            Vector3 direction = (nextNode.transform.position - node.transform.position).normalized;

            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            Vector3 arrowPosition = node.transform.position + direction * movementArrowDistance;
            arrowPosition.z = transform.position.z;

            GameObject arrow = Instantiate(movementArrowPrefab, arrowPosition, Quaternion.identity);
            arrow.name = "MovementArrow_To_" + nextNode.nodeId;
            arrow.transform.localScale = Vector3.one * movementArrowScale;

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            arrow.transform.rotation = Quaternion.Euler(0f, 0f, angle + movementArrowRotationOffset);

            SpriteRenderer spriteRenderer = arrow.GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null)
                spriteRenderer.sortingOrder = movementArrowSortingOrder;

            if (arrow.GetComponentInChildren<Collider2D>() == null)
                arrow.AddComponent<BoxCollider2D>();

            MovementArrow movementArrow =
                arrow.GetComponentInChildren<MovementArrow>();

            if (movementArrow != null)
                movementArrow.Setup(this, nextNode);

            spawnedMovementArrows.Add(arrow);
            movementArrowTargets[arrow] = nextNode;

            arrowCount++;
        }

        if (arrowCount == 0)
        {
            waitingAtNodeChoice = false;
            currentStoppedChoiceNode = null;
            manualArrowNavigationActive = false;

            Debug.LogWarning(
                "[CarNodeFollower] Node " + node.nodeId +
                " has no valid outgoing movement arrows."
            );
        }

        if (debugLogs)
        {
            Debug.Log(
                "[CarNodeFollower] Stopped at node " +
                node.nodeId +
                ". Movement arrows shown: " +
                arrowCount
            );
        }

        RefreshUndoButtons();
    }

    private List<RoadNode> GetValidOutgoingMovementNodes(RoadNode node)
    {
        List<RoadNode> validNodes = new List<RoadNode>();

        if (node == null || node.connectedNodes == null)
            return validNodes;

        foreach (RoadNode nextNode in node.connectedNodes)
        {
            if (nextNode == null)
                continue;

            if (preventImmediateBacktracking &&
                previousNode != null &&
                nextNode == previousNode)
            {
                continue;
            }

            Vector2 direction =
                (Vector2)nextNode.transform.position -
                (Vector2)node.transform.position;

            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            validNodes.Add(nextNode);
        }

        return validNodes;
    }

    private void HandleMovementArrowClick()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        if (worldCamera == null)
            return;

        if (blockMapClicksOnUI && IsPointerOverUI())
            return;

        Vector2 worldPos = worldCamera.ScreenToWorldPoint(Input.mousePosition);
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos);

        foreach (Collider2D hit in hits)
        {
            if (hit == null) continue;

            Transform t = hit.transform;

            while (t != null)
            {
                if (brakeResumeArrows.Contains(t.gameObject))
                {
                    ResumeFromBrakeArrow();
                    return;
                }

                if (movementArrowTargets.TryGetValue(t.gameObject, out RoadNode nextNode))
                {
                    ChooseNextNodeFromArrow(nextNode);
                    return;
                }

                t = t.parent;
            }
        }
    }

    public void ChooseNextNodeFromArrow(RoadNode nextNode)
    {
        if (nextNode == null)
            return;

        bool followsPlannedRoute = pathQueue.Count > 0 && pathQueue.Peek() == nextNode;

        ClearMovementArrows();

        waitingAtNodeChoice = false;
        currentStoppedChoiceNode = null;
        isBraking = false;

        if (followsPlannedRoute)
        {
            // We are about to leave this node, so make it the next Undo target.
            RecordUndoState();

            if (debugLogs)
                Debug.Log("[CarNodeFollower] Arrow selected. Continuing planned route to: " + nextNode.nodeId);

            RefreshUndoButtons();
            return;
        }

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Arrow selected. Moving manually to: " + nextNode.nodeId);

        pathQueue.Clear();
        pathQueue.Enqueue(nextNode);

        pendingParkingSpot = null;
        movingToVirtualParkingStop = false;
        useVirtualParkingStopAfterNodeRoute = false;
        reversingToVirtualParkingStop = false;

        oppositeLaneParkingRoutePoints.Clear();
        oppositeLaneParkingUTurnActive = false;

        manualUTurnRoutePoints.Clear();
        manualUTurnActive = false;
        manualUTurnLandingProjection = default;
        manualUTurnStopProjection = default;

        automaticParkingRouteActive = false;
        plannedParkingRouteDistanceMeters = 0f;

        selectedTargetNode = null;

        if (selectedTargetMarker != null)
            selectedTargetMarker.gameObject.SetActive(false);

        manualArrowNavigationActive = true;

        RecordUndoState();
        RefreshUndoButtons();
    }

    private void ContinuePlannedRouteFromNodeChoice()
    {
        if (!waitingAtNodeChoice)
            return;

        // In manual arrow navigation there is no queued route to continue.
        // Keep the arrows visible and require the player to choose a direction.
        if (pathQueue.Count == 0)
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Move pressed at a direction-choice node. Choose an arrow first.");

            RefreshUndoButtons();
            return;
        }

        // We are about to leave this node along the queued route.
        RecordUndoState();

        ClearMovementArrows();

        waitingAtNodeChoice = false;
        currentStoppedChoiceNode = null;
        isBraking = false;

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Move pressed while arrows were showing. Continuing current route.");

        RefreshUndoButtons();
    }

    private void ClearMovementArrows()
    {
        foreach (GameObject arrow in spawnedMovementArrows)
        {
            if (arrow != null)
                Destroy(arrow);
        }

        spawnedMovementArrows.Clear();
        movementArrowTargets.Clear();
        brakeResumeArrows.Clear();
        waitingForBrakeResumeArrow = false;
    }

    private void FaceCarAlongFirstOutgoingRoad(RoadNode node)
    {
        if (node == null || node.connectedNodes == null)
            return;

        foreach (RoadNode nextNode in node.connectedNodes)
        {
            if (nextNode == null)
                continue;

            Vector2 direction =
                (Vector2)nextNode.transform.position -
                (Vector2)node.transform.position;

            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            float angle =
                Mathf.Atan2(direction.y, direction.x) *
                Mathf.Rad2Deg;

            transform.rotation = Quaternion.Euler(
                0f,
                0f,
                angle + spriteRotationOffset
            );

            return;
        }
    }

    private void RotateCar(Vector2 direction)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float finalAngle = angle + spriteRotationOffset;

        Quaternion targetRotation = Quaternion.Euler(0, 0, finalAngle);

        if (smoothRotation)
        {
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }
        else
        {
            transform.rotation = targetRotation;
        }
    }

    private List<RoadNode> FindPath(
        RoadNode start,
        RoadNode goal,
        RoadNode blockedFirstStep = null)
    {
        if (start == null || goal == null)
            return null;

        Dictionary<RoadNode, float> distance =
            new Dictionary<RoadNode, float>();

        Dictionary<RoadNode, RoadNode> cameFrom =
            new Dictionary<RoadNode, RoadNode>();

        List<RoadNode> open = new List<RoadNode>();
        HashSet<RoadNode> closed = new HashSet<RoadNode>();

        distance[start] = 0f;
        open.Add(start);

        while (open.Count > 0)
        {
            RoadNode current = GetLowestDistanceNode(open, distance);
            open.Remove(current);

            if (current == goal)
                break;

            if (!closed.Add(current))
                continue;

            if (current.connectedNodes == null)
                continue;

            foreach (RoadNode next in current.connectedNodes)
            {
                if (next == null || closed.Contains(next))
                    continue;

                if (current == start &&
                    blockedFirstStep != null &&
                    next == blockedFirstStep)
                {
                    continue;
                }

                if (blockedFirstStep == null &&
                    current == currentNode &&
                    preventImmediateBacktracking &&
                    previousNode != null &&
                    next == previousNode)
                {
                    continue;
                }

                float edgeLength = Vector2.Distance(
                    current.transform.position,
                    next.transform.position
                );

                float candidateDistance =
                    distance[current] + edgeLength;

                bool hasOldDistance =
                    distance.TryGetValue(next, out float oldDistance);

                if (!hasOldDistance ||
                    candidateDistance < oldDistance)
                {
                    distance[next] = candidateDistance;
                    cameFrom[next] = current;

                    if (!open.Contains(next))
                        open.Add(next);
                }
            }
        }

        if (!distance.ContainsKey(goal))
            return null;

        List<RoadNode> path = new List<RoadNode>();
        RoadNode temp = goal;
        path.Add(temp);

        while (temp != start)
        {
            if (!cameFrom.TryGetValue(
                    temp,
                    out RoadNode previous))
            {
                return null;
            }

            temp = previous;
            path.Add(temp);
        }

        path.Reverse();
        return path;
    }


    private RoadNode GetLowestDistanceNode(
        List<RoadNode> open,
        Dictionary<RoadNode, float> distance)
    {
        RoadNode bestNode = open[0];
        float bestDistance = distance.TryGetValue(
            bestNode,
            out float initialDistance)
            ? initialDistance
            : float.PositiveInfinity;

        for (int i = 1; i < open.Count; i++)
        {
            RoadNode candidate = open[i];

            float candidateDistance = distance.TryGetValue(
                candidate,
                out float value)
                ? value
                : float.PositiveInfinity;

            if (candidateDistance < bestDistance)
            {
                bestNode = candidate;
                bestDistance = candidateDistance;
            }
        }

        return bestNode;
    }

    private RoadNode FindBestNodeForMapClick(Vector2 clickPosition)
    {
        RoadNode nearest = FindNearestNode(clickPosition);

        if (nearest == null)
            return null;

        float nearestDistance = Vector2.Distance(clickPosition, nearest.transform.position);

        if (useMaxNodeClickDistance && nearestDistance > maxNodeClickDistance)
        {
            if (debugLogs)
            {
                Debug.Log(
                    "[CarNodeFollower] Nearest node is too far from click. Distance = " +
                    nearestDistance.ToString("0.0")
                );
            }

            return null;
        }

        if (nearest == currentNode && currentNode != null && currentNode.connectedNodes.Count > 0)
        {
            RoadNode bestNext = null;
            float bestNextDistance = Mathf.Infinity;

            foreach (RoadNode next in currentNode.connectedNodes)
            {
                if (next == null) continue;

                if (preventImmediateBacktracking && previousNode != null && next == previousNode)
                    continue;

                float d = Vector2.Distance(clickPosition, next.transform.position);

                if (d < bestNextDistance)
                {
                    bestNextDistance = d;
                    bestNext = next;
                }
            }

            if (bestNext != null)
                return bestNext;
        }

        return nearest;
    }

    private RoadNode FindNearestNode(Vector2 position)
    {
        RoadNode[] nodes = FindObjectsByType<RoadNode>(FindObjectsSortMode.None);

        RoadNode closest = null;
        float bestDistance = Mathf.Infinity;

        foreach (RoadNode node in nodes)
        {
            if (node == null) continue;

            float distance = Vector2.Distance(position, node.transform.position);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                closest = node;
            }
        }

        return closest;
    }

    // =========================
    // UNDO METHODS
    // =========================

    private void RecordUndoState()
    {
        if (!enableUndoMove)
            return;

        if (currentNode == null)
            return;

        UndoMoveState state = new UndoMoveState(
            transform.position,
            transform.rotation,
            currentNode,
            previousNode
        );

        undoHistory.Push(state);

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Undo state recorded at node: " + currentNode.nodeId);
    }

    public bool CanUndoMove()
    {
        if (!enableUndoMove ||
            undoHistory.Count == 0 ||
            isUndoing)
        {
            return false;
        }

        // waitingAtNodeChoice means the car has already reached a RoadNode and
        // is physically stopped there. Undo should be available immediately,
        // even though IsRouteActive() counts the choice state as "active".
        if (waitingAtNodeChoice)
            return true;

        return !IsRouteActive();
    }

    public void UI_UndoMoveButton()
    {
        if (!CanUndoMove())
        {
            if (debugLogs)
                Debug.Log("[CarNodeFollower] Cannot undo right now.");

            RefreshUndoButtons();
            return;
        }

        UndoMoveState previousState = undoHistory.Pop();

        if (animateUndoMove)
        {
            StartCoroutine(UndoMoveRoutine(previousState));
        }
        else
        {
            ApplyUndoStateInstantly(previousState);
        }
    }

    private IEnumerator UndoMoveRoutine(UndoMoveState previousState)
    {
        isUndoing = true;
        isBraking = false;

        ClearCurrentRouteWithoutClearingUndo();

        RefreshUndoButtons();

        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;

        float undoSpeed = Mathf.Max(
            GetCurrentMovementSpeedUnitsPerSecond() * undoSpeedMultiplier,
            0.1f
        );
        float distance = Vector2.Distance(startPosition, previousState.carPosition);
        float duration = Mathf.Max(distance / undoSpeed, 0.05f);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;

            transform.position = Vector3.Lerp(startPosition, previousState.carPosition, t);
            transform.rotation = Quaternion.Slerp(startRotation, previousState.carRotation, t);

            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyUndoStateInstantly(previousState);

        isUndoing = false;

        RefreshUndoButtons();

        if (debugLogs && currentNode != null)
            Debug.Log("[CarNodeFollower] Undo finished. Current node: " + currentNode.nodeId);
    }

    private void ApplyUndoStateInstantly(UndoMoveState previousState)
    {
        ClearCurrentRouteWithoutClearingUndo();

        transform.position = previousState.carPosition;
        transform.rotation = previousState.carRotation;

        currentNode = previousState.currentNode;
        previousNode = previousState.previousNode;

        isBraking = false;

        // After Undo, the car is back at a RoadNode. Restore the normal
        // yellow movement arrows immediately so the player can choose the
        // next legal direction again without clicking the road first.
        RestoreMovementArrowsAfterUndo();

        RefreshUndoButtons();
    }

    /// <summary>
    /// Restores the normal node-choice state after an Undo.
    /// This is especially important when Undo returns the car to the scenario
    /// start node, because ClearCurrentRouteWithoutClearingUndo() removes the
    /// old arrows before the car is moved back.
    /// </summary>
    private void RestoreMovementArrowsAfterUndo()
    {
        if (currentNode == null)
            return;

        if (!stopAtEveryNodeAndShowArrows)
            return;

        manualArrowNavigationActive = true;
        waitingAtNodeChoice = false;
        currentStoppedChoiceNode = null;

        StopAtNodeAndShowMovementArrows(currentNode);

        if (debugLogs)
        {
            Debug.Log(
                "[CarNodeFollower] Undo restored movement arrows at node: " +
                currentNode.nodeId
            );
        }
    }

    private void ClearCurrentRouteWithoutClearingUndo()
    {
        pathQueue.Clear();

        ClearMovementArrows();
        waitingAtNodeChoice = false;
        currentStoppedChoiceNode = null;
        manualArrowNavigationActive = false;

        pendingParkingSpot = null;

        movingToVirtualParkingStop = false;
        useVirtualParkingStopAfterNodeRoute = false;
        reversingToVirtualParkingStop = false;

        oppositeLaneParkingRoutePoints.Clear();
        oppositeLaneParkingUTurnActive = false;

        manualUTurnRoutePoints.Clear();
        manualUTurnActive = false;
        manualUTurnLandingProjection = default;
        manualUTurnStopProjection = default;

        automaticParkingRouteActive = false;
        plannedParkingRouteDistanceMeters = 0f;

        selectedTargetNode = null;

        if (selectedTargetMarker != null)
            selectedTargetMarker.gameObject.SetActive(false);
    }

    private bool IsRouteActive()
    {
        return pathQueue.Count > 0 ||
               movingToVirtualParkingStop ||
               waitingAtNodeChoice ||
               oppositeLaneParkingUTurnActive ||
               oppositeLaneParkingRoutePoints.Count > 0 ||
               manualUTurnActive ||
               manualUTurnRoutePoints.Count > 0;
    }

    private void CacheUndoButtons()
    {
        undoButtons = FindObjectsByType<RoadNodeUndoButton>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Found undo buttons: " + undoButtons.Length);
    }

    public void RefreshUndoButtons()
    {
        CacheUndoButtons();

        bool canUndoNow = CanUndoMove();

        if (debugLogs)
        {
            string currentName = currentNode != null ? currentNode.gameObject.name : "NULL";
            string currentId = currentNode != null ? currentNode.nodeId : "NULL";

            Debug.Log(
                "[CarNodeFollower] RefreshUndoButtons | Current Node Object = " +
                currentName +
                " | Node Id = " +
                currentId +
                " | Can Undo = " +
                canUndoNow
            );
        }

        foreach (RoadNodeUndoButton undoButton in undoButtons)
        {
            if (undoButton == null) continue;

            undoButton.Refresh(currentNode, canUndoNow);
        }
    }

    public void ClearUndoHistory()
    {
        undoHistory.Clear();
        RefreshUndoButtons();

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Undo history cleared.");
    }
}