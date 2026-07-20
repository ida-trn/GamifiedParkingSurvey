using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

public class CarNodeFollower : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 4f;
    public float arriveDistance = 0.1f;
    public RoadNode startNode;

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

    [Header("Undo Move")]
    public bool enableUndoMove = true;
    public bool animateUndoMove = true;
    public float undoSpeedMultiplier = 1f;

    [Header("Debug")]
    public bool debugLogs = true;

    [Header("Node Stop Movement Arrows")]
    [Tooltip("When true, the car pauses after reaching each intermediate node and shows arrows for possible next movements.")]
    public bool stopAtEveryNodeAndShowArrows = true;

    [Tooltip("Arrow prefab shown around the car. It should have a SpriteRenderer. A BoxCollider2D will be added automatically if missing.")]
    public GameObject movementArrowPrefab;

    [Tooltip("How far each arrow appears from the car/node.")]
    public float movementArrowDistance = 0.8f;

    [Tooltip("Scale applied to spawned movement arrows.")]
    public float movementArrowScale = 0.35f;

    [Tooltip("If your arrow sprite points RIGHT, use 0. If it points UP, use -90.")]
    public float movementArrowRotationOffset = -90f;

    [Tooltip("Sorting order for arrows so they appear above the map.")]
    public int movementArrowSortingOrder = 200;

    [Tooltip("If true, arrows also appear when the route finishes and the car is idle at a node.")]
    public bool showArrowsWhenIdleAtNode = false;

    private RoadNode previousNode;
    private RoadNode currentNode;
    private RoadNode selectedTargetNode;

    private Queue<RoadNode> pathQueue = new Queue<RoadNode>();

    private ParkingSpot pendingParkingSpot;
    private CarParkingInteraction parkingInteraction;

    private bool isBraking = false;

    private bool movingToVirtualParkingStop = false;
    private bool useVirtualParkingStopAfterNodeRoute = false;
    private Vector3 virtualParkingStopPosition;

    private bool isUndoing = false;

    private bool waitingAtNodeChoice = false;
    private bool manualArrowNavigationActive = false;
    private RoadNode currentStoppedChoiceNode;

    private readonly List<GameObject> spawnedMovementArrows = new List<GameObject>();
    private readonly Dictionary<GameObject, RoadNode> movementArrowTargets = new Dictionary<GameObject, RoadNode>();

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

        isBraking = false;
        isUndoing = false;
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

    private void Awake()
    {
        parkingInteraction = GetComponent<CarParkingInteraction>();

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
        if (waitingAtNodeChoice)
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
            if (pathQueue.Count > 0)
            {
                FollowPath();
            }
            else if (movingToVirtualParkingStop)
            {
                FollowVirtualParkingStop();
            }
        }
    }

    private void HandleClickOrTouchTargetSelection()
    {
        if (!enableClickToSelectMoveTarget)
            return;

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

        Collider2D roadHit = Physics2D.OverlapPoint(worldPos, roadLayer);

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

        if (waitingAtNodeChoice)
        {
            ContinuePlannedRouteFromNodeChoice();
            return;
        }

        isBraking = false;

        if (pathQueue.Count > 0 || movingToVirtualParkingStop)
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

        isBraking = true;

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Stop/Brake pressed. Car paused.");

        RefreshUndoButtons();
    }

    public void RouteToParkingSpot(ParkingSpot spot)
    {
        if (spot == null)
            return;

        if (isUndoing)
            return;

        if (waitingAtNodeChoice)
        {
            ClearMovementArrows();
            waitingAtNodeChoice = false;
            currentStoppedChoiceNode = null;
            manualArrowNavigationActive = false;
        }

        if (IsRouteActive())
        {
            if (!allowParkingClickToInterruptRoute)
            {
                if (debugLogs)
                    Debug.Log("[CarNodeFollower] Parking click ignored because the car is already moving.");

                return;
            }

            if (debugLogs)
            {
                Debug.Log(
                    "[CarNodeFollower] Parking spot clicked while moving. " +
                    "Cancelling current node route and starting parking route."
                );
            }

            ClearCurrentRouteWithoutClearingUndo();
        }

        pendingParkingSpot = spot;
        isBraking = false;
        movingToVirtualParkingStop = false;
        useVirtualParkingStopAfterNodeRoute = false;

        if (spot.HasParkingSegment())
        {
            RoadNode segmentStart = spot.parkingSegmentStartNode;

            virtualParkingStopPosition = spot.GetParkingStopPoint();
            virtualParkingStopPosition.z = transform.position.z;

            selectedTargetNode = segmentStart;
            useVirtualParkingStopAfterNodeRoute = true;

            if (selectedTargetMarker != null)
            {
                selectedTargetMarker.position = virtualParkingStopPosition;
                selectedTargetMarker.gameObject.SetActive(true);
            }

            if (debugLogs)
            {
                Debug.Log(
                    $"[CarNodeFollower] Parking spot {spot.spotId} clicked. " +
                    $"Routing to segment start {segmentStart.nodeId}, then virtual stop point."
                );
            }

            bool routeStarted = RouteToNode(segmentStart);

            if (!routeStarted)
            {
                pendingParkingSpot = null;
                useVirtualParkingStopAfterNodeRoute = false;
            }

            return;
        }

        RoadNode targetNode = spot.nearestRoadNode;

        if (targetNode == null)
        {
            targetNode = FindNearestNode(spot.transform.position);

            if (debugLogs)
            {
                Debug.LogWarning(
                    $"[CarNodeFollower] Parking spot {spot.spotId} has no nearestRoadNode. Using nearest node instead."
                );
            }
        }

        if (targetNode == null)
        {
            Debug.LogWarning("[CarNodeFollower] Cannot route to parking spot. No target node found.");
            pendingParkingSpot = null;
            return;
        }

        selectedTargetNode = targetNode;

        if (selectedTargetMarker != null)
        {
            selectedTargetMarker.position = targetNode.transform.position;
            selectedTargetMarker.gameObject.SetActive(true);
        }

        bool normalRouteStarted = RouteToNode(targetNode);

        if (!normalRouteStarted)
        {
            pendingParkingSpot = null;
        }
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
            speed * Time.deltaTime
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

            if (stopAtEveryNodeAndShowArrows)
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

        if (showArrowsWhenIdleAtNode && stopAtEveryNodeAndShowArrows && currentNode != null)
        {
            manualArrowNavigationActive = true;
            StopAtNodeAndShowMovementArrows(currentNode);
        }
    }

    private void FollowVirtualParkingStop()
    {
        Vector2 direction = (Vector2)virtualParkingStopPosition - (Vector2)transform.position;

        if (direction.sqrMagnitude > 0.001f)
        {
            RotateCar(direction);
        }

        transform.position = Vector2.MoveTowards(
            transform.position,
            virtualParkingStopPosition,
            speed * Time.deltaTime
        );

        if (Vector2.Distance(transform.position, virtualParkingStopPosition) <= arriveDistance)
        {
            transform.position = virtualParkingStopPosition;
            movingToVirtualParkingStop = false;

            if (debugLogs)
                Debug.Log("[CarNodeFollower] Arrived at virtual parking stop point.");

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
            if (parkingInteraction != null)
            {
                parkingInteraction.FinalizePark(pendingParkingSpot);
            }
            else
            {
                Debug.LogWarning("[CarNodeFollower] No CarParkingInteraction found.");
            }

            pendingParkingSpot = null;
        }

        RefreshUndoButtons();
    }

    private void StopAtNodeAndShowMovementArrows(RoadNode node)
    {
        if (node == null)
            return;

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

        foreach (RoadNode nextNode in node.connectedNodes)
        {
            if (nextNode == null) continue;

            if (preventImmediateBacktracking && previousNode != null && nextNode == previousNode)
                continue;

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

            spawnedMovementArrows.Add(arrow);
            movementArrowTargets[arrow] = nextNode;

            arrowCount++;
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

        ClearMovementArrows();

        waitingAtNodeChoice = false;
        currentStoppedChoiceNode = null;
        isBraking = false;

        if (debugLogs)
            Debug.Log("[CarNodeFollower] Move pressed while arrows were showing. Continuing current route.");

        RefreshUndoButtons();

        if (pathQueue.Count == 0 && !manualArrowNavigationActive)
        {
            OnNodeRouteFinished();
        }
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

    private List<RoadNode> FindPath(RoadNode start, RoadNode goal)
    {
        Queue<RoadNode> open = new Queue<RoadNode>();
        Dictionary<RoadNode, RoadNode> cameFrom = new Dictionary<RoadNode, RoadNode>();

        open.Enqueue(start);
        cameFrom[start] = null;

        while (open.Count > 0)
        {
            RoadNode current = open.Dequeue();

            if (current == goal)
                break;

            foreach (RoadNode next in current.connectedNodes)
            {
                if (next == null) continue;
                if (cameFrom.ContainsKey(next)) continue;

                if (current == currentNode &&
                    preventImmediateBacktracking &&
                    previousNode != null &&
                    next == previousNode)
                {
                    continue;
                }

                cameFrom[next] = current;
                open.Enqueue(next);
            }
        }

        if (!cameFrom.ContainsKey(goal))
            return null;

        List<RoadNode> path = new List<RoadNode>();
        RoadNode temp = goal;

        while (temp != null)
        {
            path.Add(temp);
            temp = cameFrom[temp];
        }

        path.Reverse();
        return path;
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
        return enableUndoMove &&
               undoHistory.Count > 0 &&
               !IsRouteActive() &&
               !isUndoing;
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

        float undoSpeed = Mathf.Max(speed * undoSpeedMultiplier, 0.1f);
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

        RefreshUndoButtons();
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

        selectedTargetNode = null;

        if (selectedTargetMarker != null)
            selectedTargetMarker.gameObject.SetActive(false);
    }

    private bool IsRouteActive()
    {
        return pathQueue.Count > 0 || movingToVirtualParkingStop || waitingAtNodeChoice;
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