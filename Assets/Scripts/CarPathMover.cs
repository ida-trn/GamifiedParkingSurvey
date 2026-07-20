using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Single source of truth for mouse input on the car:
///  - click on a road -> drive there (following roads via RoadPathfinder)
///  - click on a free parking spot -> drive there, then auto-park on arrival
///  - click on an occupied spot -> ignored
///
/// Replaces CarController / GridCarMovement / ClickToMove on the Car GameObject.
/// Keep CarParkingInteraction on the Car too — this script calls FinalizePark() on it.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CarPathMover : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float turnSpeed = 720f;            // degrees/sec
    public float waypointReachedDist = 0.15f;

    [Header("Click detection")]
    [Tooltip("Same Road layer used by RoadPathfinder.")]
    public LayerMask roadLayer;
    [Tooltip("Put a dedicated 'ParkingSpot' layer on your spot prefabs and assign it here.")]
    public LayerMask parkingSpotLayer;
    [Tooltip("Defaults to Camera.main if left empty.")]
    public Camera cam;

    [Header("Parking hookup")]
    public CarParkingInteraction parkingInteraction;

    [Header("Debug")]
    public bool debugLogs = true;

    private Rigidbody2D rb;
    private List<Vector2> currentPath;
    private int waypointIndex;
    private bool isMoving;
    private System.Action onArrivedCallback;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
            HandleClick();
    }

    void HandleClick()
    {
        // Don't move the car if the click landed on UI (scenario panel, buttons, etc.)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector2 worldPoint = cam.ScreenToWorldPoint(Input.mousePosition);

        // 1) Clicked a parking spot?
        Collider2D spotHit = Physics2D.OverlapPoint(worldPoint, parkingSpotLayer);
        if (spotHit != null)
        {
            ParkingSpot spot = spotHit.GetComponent<ParkingSpot>();
            if (spot == null)
            {
                if (debugLogs) Debug.LogWarning("[CarPathMover] Hit something on parkingSpotLayer with no ParkingSpot component.");
                return;
            }

            if (spot.isOccupied)
            {
                if (debugLogs) Debug.Log("[CarPathMover] That spot is already occupied.");
                return;
            }

            RequestDriveToSpot(spot);
            return;
        }

        // 2) Otherwise treat it as a road-travel request.
        Collider2D roadHit = Physics2D.OverlapPoint(worldPoint, roadLayer);
        Vector2 target = worldPoint;

        if (roadHit == null)
        {
            // Off-road click: snap to the nearest road point instead of rejecting it outright.
            if (RoadPathfinder.Instance == null)
            {
                if (debugLogs) Debug.LogWarning("[CarPathMover] No RoadPathfinder in scene.");
                return;
            }
            target = RoadPathfinder.Instance.SnapToNearestRoad(worldPoint);
        }

        MoveTo(target);
    }

    /// <summary>Drive toward an arbitrary point, following roads.</summary>
    public void MoveTo(Vector2 target, System.Action onArrived = null)
    {
        if (RoadPathfinder.Instance == null)
        {
            Debug.LogWarning("[CarPathMover] No RoadPathfinder in scene.");
            return;
        }

        List<Vector2> path = RoadPathfinder.Instance.FindPath(rb.position, target);
        if (path == null || path.Count == 0)
        {
            if (debugLogs) Debug.Log("[CarPathMover] No path found to that point.");
            return;
        }

        currentPath = path;
        waypointIndex = 0;
        onArrivedCallback = onArrived;
        isMoving = true;
    }

    /// <summary>Drive to a parking spot; FinalizePark() runs automatically on arrival.</summary>
    public void RequestDriveToSpot(ParkingSpot spot)
    {
        MoveTo(spot.transform.position, () =>
        {
            if (parkingInteraction != null)
                parkingInteraction.FinalizePark(spot);
            else
                Debug.LogWarning("[CarPathMover] No CarParkingInteraction assigned — can't finalize parking.");
        });
    }

    public void StopMoving()
    {
        isMoving = false;
        currentPath = null;
        rb.linearVelocity = Vector2.zero;
    }

    void FixedUpdate()
    {
        if (!isMoving || currentPath == null) return;

        Vector2 targetPoint = currentPath[waypointIndex];
        Vector2 toTarget = targetPoint - rb.position;

        if (toTarget.magnitude <= waypointReachedDist)
        {
            waypointIndex++;
            if (waypointIndex >= currentPath.Count)
            {
                isMoving = false;
                rb.linearVelocity = Vector2.zero;

                var callback = onArrivedCallback;
                onArrivedCallback = null;
                callback?.Invoke();
                return;
            }
            targetPoint = currentPath[waypointIndex];
            toTarget = targetPoint - rb.position;
        }

        Vector2 dir = toTarget.normalized;
        rb.linearVelocity = dir * moveSpeed;

        // Sprite convention from your existing CarController: rotation 0 = facing right.
        float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float newAngle = Mathf.MoveTowardsAngle(rb.rotation, targetAngle, turnSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(newAngle);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (currentPath == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < currentPath.Count - 1; i++)
            Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
    }
#endif
}
