using UnityEngine;

// Attach this to the Main Camera.
[DisallowMultipleComponent]
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;   // the car

    [Header("Forward View")]
    [Tooltip(
        "0 keeps the car centered. 0.40 leaves roughly 70% of the visible area " +
        "in front of the car. Recommended: 0.35-0.45.")]
    [Range(0f, 0.65f)]
    public float forwardViewBias = 0.40f;

    [Header("Smooth Camera Motion")]
    [Tooltip(
        "How long the camera takes to settle toward its new position. " +
        "Higher = softer/slower. Recommended: 0.25-0.35 seconds.")]
    [Min(0.01f)]
    public float positionSmoothTime = 0.30f;

    [Tooltip(
        "How long the forward-looking direction takes to rotate after the car turns. " +
        "Higher = slower/smoother direction changes. Recommended: 0.55-0.80 seconds.")]
    [Min(0.01f)]
    public float directionSmoothTime = 0.65f;

    [Tooltip("Very small movements below this amount are ignored when determining travel direction.")]
    [Min(0f)]
    public float movementDirectionThreshold = 0.001f;

    [Tooltip("Maximum camera movement speed in Unity units/second.")]
    [Min(0.1f)]
    public float maximumFollowSpeed = 500f;

    [Header("Perspective fallback")]
    [Tooltip("Used only if this camera is not orthographic.")]
    [Min(0f)]
    public float perspectiveLookAheadDistance = 22f;

    private Camera attachedCamera;

    private Vector3 previousTargetPosition;
    private Vector3 cameraVelocity;

    private float currentForwardAngle;
    private float targetForwardAngle;
    private float forwardAngleVelocity;
    private bool hasForwardDirection;

    // Prevents a broken state from flooding the Console every frame.
    private bool invalidStateWarningLogged;

    public bool HasForwardDirection
    {
        get { return hasForwardDirection; }
    }

    public Vector2 CurrentForwardDirection
    {
        get
        {
            if (!IsFinite(currentForwardAngle))
                return Vector2.right;

            float radians = currentForwardAngle * Mathf.Deg2Rad;

            Vector2 direction = new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians)
            );

            return IsFinite(direction)
                ? direction
                : Vector2.right;
        }
    }

    private void Awake()
    {
        attachedCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        if (target == null)
            return;

        if (!IsFinite(target.position))
        {
            LogInvalidStateOnce(
                "Target position was invalid during Start(). Camera follow was not initialised."
            );
            return;
        }

        previousTargetPosition = target.position;
        InitialiseDirectionFromTargetFacing();
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (attachedCamera == null)
            attachedCamera = GetComponent<Camera>();

        Vector3 currentTargetPosition = target.position;

        // Never let an invalid target position contaminate the camera state.
        if (!IsFinite(currentTargetPosition))
        {
            LogInvalidStateOnce(
                "Car/target position became NaN or Infinity. Camera update skipped."
            );
            return;
        }

        if (!IsFinite(previousTargetPosition))
            previousTargetPosition = currentTargetPosition;

        /*
         * IMPORTANT:
         * Scenario/UI transitions can temporarily set Time.timeScale to 0.
         * In that case Time.deltaTime becomes 0.
         *
         * Mathf.SmoothDamp / SmoothDampAngle can corrupt their velocity state
         * when called with a zero delta time in certain equal-position cases.
         * That is what can eventually produce:
         *
         *     MainCamera position = { NaN, NaN, -10 }
         *
         * Do not run either damping function until scaled gameplay time is
         * moving again. Updating previousTargetPosition also prevents the
         * scenario-start teleport from being treated as driving direction.
         */
        float deltaTime = Time.deltaTime;

        if (!IsFinite(deltaTime) || deltaTime <= 0.000001f)
        {
            previousTargetPosition = currentTargetPosition;
            return;
        }

        Vector2 movementDelta =
            (Vector2)(currentTargetPosition - previousTargetPosition);

        float threshold =
            IsFinite(movementDirectionThreshold)
                ? Mathf.Max(0f, movementDirectionThreshold)
                : 0.001f;

        if (IsFinite(movementDelta) &&
            movementDelta.sqrMagnitude > threshold * threshold)
        {
            Vector2 movementDirection = movementDelta.normalized;

            if (IsFinite(movementDirection))
            {
                float newTargetAngle =
                    Mathf.Atan2(
                        movementDirection.y,
                        movementDirection.x
                    ) * Mathf.Rad2Deg;

                if (IsFinite(newTargetAngle))
                {
                    targetForwardAngle = newTargetAngle;

                    if (!hasForwardDirection)
                    {
                        currentForwardAngle = targetForwardAngle;
                        forwardAngleVelocity = 0f;
                        hasForwardDirection = true;
                    }
                }
            }
        }
        else if (!hasForwardDirection)
        {
            InitialiseDirectionFromTargetFacing();
        }

        // Recover cleanly if an older frame already poisoned one of the
        // SmoothDampAngle state values.
        if (!IsFinite(currentForwardAngle) ||
            !IsFinite(targetForwardAngle) ||
            !IsFinite(forwardAngleVelocity))
        {
            currentForwardAngle = 0f;
            targetForwardAngle = 0f;
            forwardAngleVelocity = 0f;
            hasForwardDirection = false;

            InitialiseDirectionFromTargetFacing();

            LogInvalidStateOnce(
                "Invalid forward-angle smoothing state detected and reset."
            );
        }

        if (hasForwardDirection)
        {
            float safeDirectionSmoothTime =
                IsFinite(directionSmoothTime)
                    ? Mathf.Max(0.01f, directionSmoothTime)
                    : 0.65f;

            currentForwardAngle =
                Mathf.SmoothDampAngle(
                    currentForwardAngle,
                    targetForwardAngle,
                    ref forwardAngleVelocity,
                    safeDirectionSmoothTime,
                    Mathf.Infinity,
                    deltaTime
                );

            if (!IsFinite(currentForwardAngle) ||
                !IsFinite(forwardAngleVelocity))
            {
                currentForwardAngle = targetForwardAngle;
                forwardAngleVelocity = 0f;

                LogInvalidStateOnce(
                    "SmoothDampAngle produced an invalid value. Direction smoothing was safely reset."
                );
            }
        }

        Vector2 lookAheadOffset =
            CalculateLookAheadOffset();

        if (!IsFinite(lookAheadOffset))
        {
            lookAheadOffset = Vector2.zero;

            LogInvalidStateOnce(
                "Look-ahead offset became invalid. Falling back to a centered camera."
            );
        }

        Vector3 currentCameraPosition = transform.position;

        // Unity normally rejects a NaN Transform assignment, but this also
        // makes recovery safe if another script ever corrupts the camera.
        if (!IsFinite(currentCameraPosition))
        {
            float fallbackZ =
                IsFinite(currentCameraPosition.z)
                    ? currentCameraPosition.z
                    : -10f;

            currentCameraPosition = new Vector3(
                currentTargetPosition.x,
                currentTargetPosition.y,
                fallbackZ
            );

            cameraVelocity = Vector3.zero;
            transform.position = currentCameraPosition;

            LogInvalidStateOnce(
                "Camera transform contained an invalid value and was snapped back to the target."
            );
        }

        Vector3 desiredPosition =
            new Vector3(
                currentTargetPosition.x + lookAheadOffset.x,
                currentTargetPosition.y + lookAheadOffset.y,
                currentCameraPosition.z
            );

        if (!IsFinite(desiredPosition))
        {
            desiredPosition = new Vector3(
                currentTargetPosition.x,
                currentTargetPosition.y,
                currentCameraPosition.z
            );

            cameraVelocity = Vector3.zero;

            LogInvalidStateOnce(
                "Desired camera position became invalid. Falling back to the car position."
            );
        }

        if (!IsFinite(cameraVelocity))
        {
            cameraVelocity = Vector3.zero;

            LogInvalidStateOnce(
                "Camera SmoothDamp velocity became invalid and was reset."
            );
        }

        float safePositionSmoothTime =
            IsFinite(positionSmoothTime)
                ? Mathf.Max(0.01f, positionSmoothTime)
                : 0.30f;

        float safeMaximumFollowSpeed =
            IsFinite(maximumFollowSpeed)
                ? Mathf.Max(0.1f, maximumFollowSpeed)
                : 500f;

        Vector3 nextCameraPosition =
            Vector3.SmoothDamp(
                currentCameraPosition,
                desiredPosition,
                ref cameraVelocity,
                safePositionSmoothTime,
                safeMaximumFollowSpeed,
                deltaTime
            );

        // Final safety net: never assign NaN/Infinity to the Transform.
        if (!IsFinite(nextCameraPosition) ||
            !IsFinite(cameraVelocity))
        {
            cameraVelocity = Vector3.zero;
            nextCameraPosition = desiredPosition;

            LogInvalidStateOnce(
                "Camera SmoothDamp produced an invalid position. Camera was safely snapped to its valid desired position."
            );
        }

        transform.position = nextCameraPosition;

        previousTargetPosition =
            currentTargetPosition;
    }

    private Vector2 CalculateLookAheadOffset()
    {
        if (!hasForwardDirection)
            return Vector2.zero;

        Vector2 forward =
            CurrentForwardDirection;

        if (!IsFinite(forward))
            return Vector2.zero;

        float bias =
            IsFinite(forwardViewBias)
                ? Mathf.Clamp(forwardViewBias, 0f, 0.65f)
                : 0.40f;

        if (attachedCamera != null &&
            attachedCamera.orthographic)
        {
            float halfHeight =
                IsFinite(attachedCamera.orthographicSize)
                    ? Mathf.Max(0f, attachedCamera.orthographicSize)
                    : 0f;

            float aspect =
                IsFinite(attachedCamera.aspect) &&
                attachedCamera.aspect > 0.000001f
                    ? attachedCamera.aspect
                    : 1f;

            float halfWidth =
                halfHeight * aspect;

            Vector2 offset = new Vector2(
                forward.x * halfWidth * bias,
                forward.y * halfHeight * bias
            );

            return IsFinite(offset)
                ? offset
                : Vector2.zero;
        }

        float lookAheadDistance =
            IsFinite(perspectiveLookAheadDistance)
                ? Mathf.Max(0f, perspectiveLookAheadDistance)
                : 22f;

        Vector2 perspectiveOffset =
            forward * lookAheadDistance;

        return IsFinite(perspectiveOffset)
            ? perspectiveOffset
            : Vector2.zero;
    }

    private void InitialiseDirectionFromTargetFacing()
    {
        if (target == null)
            return;

        // CarNodeFollower in this project uses spriteRotationOffset = 0,
        // so the car's local +X direction is its travel direction.
        Vector2 facing = target.right;

        if (!IsFinite(facing) ||
            facing.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        facing.Normalize();

        float angle =
            Mathf.Atan2(
                facing.y,
                facing.x
            ) * Mathf.Rad2Deg;

        if (!IsFinite(angle))
            return;

        currentForwardAngle = angle;
        targetForwardAngle = angle;
        forwardAngleVelocity = 0f;
        hasForwardDirection = true;
    }

    /// <summary>
    /// ScenarioManager calls this when a Conventional round is reset.
    /// The camera is immediately placed on the new car start point so it never
    /// shows the previous scenario. Once gameplay resumes, the forward bias
    /// transitions smoothly into place.
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null)
            return;

        Vector3 targetPosition = target.position;

        if (!IsFinite(targetPosition))
        {
            LogInvalidStateOnce(
                "SnapToTarget() received an invalid target position and was skipped."
            );
            return;
        }

        previousTargetPosition =
            targetPosition;

        cameraVelocity =
            Vector3.zero;

        currentForwardAngle = 0f;
        targetForwardAngle = 0f;
        forwardAngleVelocity = 0f;
        hasForwardDirection = false;

        invalidStateWarningLogged = false;

        InitialiseDirectionFromTargetFacing();

        Vector3 currentCameraPosition = transform.position;

        float cameraZ =
            IsFinite(currentCameraPosition.z)
                ? currentCameraPosition.z
                : -10f;

        transform.position =
            new Vector3(
                targetPosition.x,
                targetPosition.y,
                cameraZ
            );
    }

    private void LogInvalidStateOnce(string message)
    {
        if (invalidStateWarningLogged)
            return;

        invalidStateWarningLogged = true;

        Debug.LogWarning(
            "[CameraFollow] " + message,
            this
        );
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector2 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }
}
