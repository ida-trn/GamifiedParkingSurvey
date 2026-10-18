using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class AssistedMapCameraController : MonoBehaviour
{
    [Header("Camera")]
    public Camera controlledCamera;

    [Header("Mode")]
    public bool assistedModeActive = false;

    [Header("Disable Only In Assisted Mode")]
    public Behaviour[] disableInAssistedMode;

    [Header("Conventional Mode View")]
    [Tooltip("If ON, the camera's Orthographic Size when the scene loads is saved as the fixed conventional zoom.")]
    public bool captureInitialZoomAsConventional = true;

    [Min(0.01f)]
    [Tooltip("Fixed Orthographic Size used in every conventional round.")]
    public float conventionalZoomSize = 120f;

    [Tooltip("If ON, the conventional zoom remains fixed throughout the round.")]
    public bool lockConventionalZoom = true;

    [Header("Destination / Gate")]
    public Transform destinationGate;
    public bool startAtGate = true;
    public float gateStartZoom = 450f;

    [Header("Assisted Opening Sequence")]
    [Tooltip("If ON, an assisted round first shows the car/start point, then smoothly zooms out to the full map before interaction begins.")]
    public bool playOpeningSequence = true;

    [Min(0.01f)]
    [Tooltip("Orthographic size while the participant is first shown the car/start point.")]
    public float startPointZoomSize = 180f;

    [Min(0f)]
    [Tooltip("How long the camera stays on the car before zooming out.")]
    public float startPointHoldSeconds = 1.25f;

    [Min(0.05f)]
    [Tooltip("How long the smooth transition from the car to the map overview takes. Around 9 seconds gives a slower, easier-to-follow assisted opening.")]
    public float overviewTransitionSeconds = 9f;

    [Range(0.5f, 1.2f)]
    [Tooltip("1 = exactly fit the whole map. Values below 1 zoom in slightly. 0.9 is recommended.")]
    public float overviewZoomMultiplier = 0.75f;

    [Header("Start Point World Arrow")]
    [Tooltip("Optional world-space arrow shown during the assisted opening to clearly mark the initial car position. You can duplicate your gate world arrow and assign the duplicate here.")]
    public Transform startWorldArrow;

    [Tooltip("Distance of the start arrow from the car/start point.")]
    public float startWorldArrowDistance = 80f;

    [Tooltip("Direction from the start point to the arrow. X=0,Y=1 places it above the car and points down.")]
    public Vector2 startWorldArrowDirectionFromStart = Vector2.up;

    [Tooltip("Rotation offset for the start arrow sprite. Use the same value as your gate-world-arrow sprite if it uses the same artwork.")]
    public float startWorldArrowRotationOffset = 0f;

    [Header("Map Bounds")]
    public SpriteRenderer mapBoundsSprite;
    public bool autoCalculateMaxZoomFromMap = true;
    public bool useMapBoundsClamp = true;

    [Range(0f, 0.45f)]
    [Tooltip("Trims this fraction from BOTH left and right sides of Map Bounds. Increase slightly if zoomed-out views reveal unwanted outer/blank areas.")]
    public float mapBoundsInsetXFraction = 0.04f;

    [Range(0f, 0.45f)]
    [Tooltip("Trims this fraction from BOTH top and bottom sides of Map Bounds. Increase slightly if zoomed-out views reveal unwanted outer/blank areas.")]
    public float mapBoundsInsetYFraction = 0.04f;

    [Header("Assisted Mode Zoom")]
    public bool enableZoom = true;
    public float minZoomSize = 120f;
    public float maxZoomSize = 2300f;

    [Tooltip("Mouse-wheel zoom amount per wheel step.")]
    public float zoomSpeed = 120f;

    [Tooltip("Enable two-finger pinch zoom on Android/iOS/touch devices.")]
    public bool enablePinchZoom = true;

    [Min(0.1f)]
    [Tooltip("1 = natural pinch response. Increase slightly if pinch feels too slow.")]
    public float pinchZoomSensitivity = 1f;

    [Min(1.01f)]
    [Tooltip("Used by optional UI + / - buttons. 1.25 means each press changes zoom by about 20%.")]
    public float zoomButtonFactor = 1.25f;

    [Header("Assisted Mode Pan")]
    [Tooltip("Desktop/laptop: hold LEFT mouse button and drag to pan.")]
    public bool enableMouseDragPan = true;

    [Tooltip("Phone/tablet: drag with ONE finger to pan.")]
    public bool enableTouchPan = true;

    public bool enableKeyboardPan = true;

    [Min(0.01f)]
    public float mousePanSpeed = 1f;

    [Min(0.01f)]
    public float touchPanSpeed = 1f;

    public float keyboardPanSpeed = 750f;

    [Min(1f)]
    [Tooltip("Pointer movement required before a press becomes a map drag instead of a parking click/tap.")]
    public float dragThresholdPixels = 15f;

    [Tooltip("If ON, beginning a pan on normal screen UI (buttons/panels) is ignored. World-space parking labels are not treated as blocking UI.")]
    public bool ignorePanWhenPointerOverUI = true;

    [Header("Gate Screen Arrow - Optional")]
    [Tooltip("Optional UI Image arrow inside Canvas. It appears only in assisted mode when gate is outside camera view.")]
    public RectTransform gateScreenArrow;

    [Tooltip("If your UI arrow sprite points RIGHT, use 0. If it points the opposite way, use 180.")]
    public float gateScreenArrowRotationOffset = 180f;

    public float gateScreenArrowEdgeMargin = 80f;

    [Header("Gate World Arrow - Recommended")]
    [Tooltip("A red arrow SpriteRenderer object in the world/map. It stays close to the gate and points directly to it.")]
    public Transform gateWorldArrow;

    [Tooltip("Distance of the world arrow from the gate. Use 40-120 depending on your map scale.")]
    public float gateWorldArrowDistanceFromGate = 80f;

    [Tooltip("Direction from the gate to the arrow. X=0,Y=1 means the arrow appears above the gate and points down to it.")]
    public Vector2 gateWorldArrowDirectionFromGate = Vector2.up;

    [Tooltip("If your world arrow sprite points RIGHT by default, use 0. If it points wrong, try 90, -90, or 180.")]
    public float gateWorldArrowRotationOffset = 0f;

    [Tooltip("If ON, the world arrow hides when the gate is outside the camera view. Usually keep OFF.")]
    public bool hideWorldArrowWhenGateOffScreen = false;

    [Header("Debug")]
    public bool debugLogs = false;

    public static AssistedMapCameraController Instance { get; private set; }

    private Vector3 desiredCameraPosition;
    private bool hasDesiredPosition = false;

    private Coroutine assistedOpeningRoutine;
    private bool assistedOpeningActive = false;

    public bool IsAssistedOpeningActive => assistedOpeningActive;

    // Mouse drag state.
    private bool mousePanCandidate = false;
    private bool mouseDragged = false;
    private Vector2 mousePressScreenPosition;
    private Vector2 lastMouseScreenPosition;

    // Touch drag / pinch state.
    private bool touchPanCandidate = false;
    private bool touchDragged = false;
    private bool pinchInProgress = false;

    // Stores the real separation between the two fingers on the previous
    // touch frame. This is more reliable in mobile WebGL than reconstructing
    // previous touch positions from Touch.deltaPosition.
    private float lastPinchDistance = -1f;

    private Vector2 touchPressScreenPosition;
    private Vector2 lastTouchScreenPosition;

    // Kept briefly after a drag/pinch so ParkingSpot.OnPointerClick cannot
    // accidentally finalize a parking choice on the same release.
    private float suppressParkingClickUntilTime = -1f;
    private const float ParkingClickSuppressionSeconds = 0.20f;

    private readonly List<RaycastResult> uiRaycastResults =
        new List<RaycastResult>(16);

    private void Awake()
    {
        Instance = this;

        if (controlledCamera == null)
            controlledCamera = GetComponent<Camera>();

        if (controlledCamera == null)
            controlledCamera = Camera.main;

        if (controlledCamera != null)
        {
            controlledCamera.orthographic = true;

            if (captureInitialZoomAsConventional)
            {
                conventionalZoomSize = Mathf.Max(
                    0.01f,
                    controlledCamera.orthographicSize
                );
            }

            desiredCameraPosition = controlledCamera.transform.position;
            hasDesiredPosition = true;
        }

        HideGateScreenArrow();
        HideGateWorldArrow();
        HideStartWorldArrow();
    }

    private void OnDestroy()
    {
        StopAssistedOpening();

        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        HideGateScreenArrow();
        HideGateWorldArrow();

        if (!assistedModeActive)
            ResetConventionalView();
    }

    public void SetAssistedMode(bool active)
    {
        if (!active)
            StopAssistedOpening();

        assistedModeActive = active;
        ResetPointerGestureState();
        suppressParkingClickUntilTime = -1f;

        foreach (Behaviour b in disableInAssistedMode)
        {
            if (b != null)
                b.enabled = !active;
        }

        if (!active)
        {
            ResetConventionalView();
            HideGateScreenArrow();
            HideGateWorldArrow();
            HideStartWorldArrow();

            if (debugLogs)
            {
                Debug.Log(
                    "[AssistedMapCameraController] Conventional mode ON. " +
                    $"Fixed zoom={conventionalZoomSize:0.##}"
                );
            }

            return;
        }

        if (controlledCamera == null)
        {
            Debug.LogWarning(
                "[AssistedMapCameraController] No camera assigned."
            );

            HideGateScreenArrow();
            HideGateWorldArrow();
            return;
        }

        controlledCamera.enabled = true;
        controlledCamera.orthographic = true;

        RecalculateMaxZoomFromMap();

        // When the new opening sequence is enabled, ScenarioManager starts it
        // only after the scenario intro closes. This avoids moving the camera
        // to the gate behind the intro panel.
        if (startAtGate && !playOpeningSequence)
            MoveCameraToGate();

        UpdateGateWorldArrow();
        UpdateGateScreenArrow();

        if (debugLogs)
        {
            Debug.Log(
                "[AssistedMapCameraController] Assisted mode ON. " +
                $"Starting zoom={controlledCamera.orthographicSize:0.##}"
            );
        }
    }

    private void Update()
    {
        if (!assistedModeActive)
        {
            if (lockConventionalZoom)
                EnforceConventionalZoom();

            HideGateScreenArrow();
            HideGateWorldArrow();
            return;
        }

        if (controlledCamera == null)
        {
            HideGateScreenArrow();
            HideGateWorldArrow();
            return;
        }

        // The opening animation owns the camera temporarily. No participant
        // pan/zoom input is accepted until the overview is ready.
        if (assistedOpeningActive)
        {
            HideGateScreenArrow();
            HideGateWorldArrow();
            return;
        }

        // Touch is handled first so mobile browsers do not also treat the
        // same finger gesture as a mouse drag.
        bool touchInputActive = HandleTouchInput();

        if (!touchInputActive)
        {
            HandleMouseWheelZoom();
            HandleMouseDragPan();
        }

        HandleKeyboardPan();
        ApplyDesiredCameraPosition();

        UpdateGateWorldArrow();
        UpdateGateScreenArrow();
    }

    private void LateUpdate()
    {
        if (!assistedModeActive)
        {
            // LateUpdate ensures another camera script cannot leave a
            // different zoom value in different conventional rounds.
            if (lockConventionalZoom)
                EnforceConventionalZoom();

            HideGateScreenArrow();
            HideGateWorldArrow();
            return;
        }

        if (controlledCamera == null)
        {
            HideGateScreenArrow();
            HideGateWorldArrow();
            return;
        }

        if (assistedOpeningActive)
        {
            HideGateScreenArrow();
            HideGateWorldArrow();
            return;
        }

        ApplyDesiredCameraPosition();

        UpdateGateWorldArrow();
        UpdateGateScreenArrow();
    }

    private void ResetConventionalView()
    {
        if (controlledCamera == null)
            return;

        controlledCamera.enabled = true;
        controlledCamera.orthographic = true;
        EnforceConventionalZoom();

        // Do not move the camera here. The normal conventional camera-follow
        // component remains responsible for following the car.
        desiredCameraPosition = controlledCamera.transform.position;
        hasDesiredPosition = true;
    }

    private void EnforceConventionalZoom()
    {
        if (controlledCamera == null)
            return;

        controlledCamera.orthographicSize =
            Mathf.Max(0.01f, conventionalZoomSize);
    }

    [ContextMenu("Capture Current Zoom As Conventional")]
    public void CaptureCurrentZoomAsConventional()
    {
        if (controlledCamera == null)
        {
            Debug.LogWarning(
                "[AssistedMapCameraController] No camera assigned."
            );
            return;
        }

        conventionalZoomSize = Mathf.Max(
            0.01f,
            controlledCamera.orthographicSize
        );

        Debug.Log(
            "[AssistedMapCameraController] Conventional zoom captured: " +
            $"{conventionalZoomSize:0.##}"
        );
    }

    [ContextMenu("Reset Conventional View Now")]
    public void ResetConventionalViewNow()
    {
        assistedModeActive = false;
        ResetConventionalView();
        HideGateScreenArrow();
        HideGateWorldArrow();
    }

    private void RecalculateMaxZoomFromMap()
    {
        if (!autoCalculateMaxZoomFromMap)
            return;

        if (mapBoundsSprite == null || controlledCamera == null)
            return;

        Bounds b = GetEffectiveMapBounds();

        float neededByHeight = b.size.y / 2f;
        float neededByWidth =
            b.size.x / (2f * controlledCamera.aspect);

        maxZoomSize = Mathf.Max(
            neededByHeight,
            neededByWidth
        );
    }

    public void MoveCameraToGate()
    {
        if (controlledCamera == null)
            return;

        Vector3 pos = controlledCamera.transform.position;

        if (destinationGate != null)
        {
            pos.x = destinationGate.position.x;
            pos.y = destinationGate.position.y;
        }

        desiredCameraPosition = pos;
        hasDesiredPosition = true;

        controlledCamera.orthographicSize = Mathf.Clamp(
            gateStartZoom,
            minZoomSize,
            maxZoomSize
        );

        ApplyDesiredCameraPosition();
    }

    /// <summary>
    /// Called by ScenarioManager after the scenario information panel closes.
    /// The camera first shows the car/start point, pauses briefly, then
    /// transitions to a full-map overview. The callback is invoked only when
    /// the participant can start interacting with the assisted map.
    /// </summary>
    public void BeginAssistedOpening(
        Transform startTarget,
        Action onOverviewReady = null)
    {
        if (!assistedModeActive ||
            !playOpeningSequence ||
            controlledCamera == null ||
            startTarget == null)
        {
            onOverviewReady?.Invoke();
            return;
        }

        StopAssistedOpening();

        assistedOpeningRoutine = StartCoroutine(
            AssistedOpeningCoroutine(
                startTarget,
                onOverviewReady
            )
        );
    }

    private IEnumerator AssistedOpeningCoroutine(
        Transform startTarget,
        Action onOverviewReady)
    {
        assistedOpeningActive = true;
        ResetPointerGestureState();
        RegisterParkingClickSuppression();

        HideGateScreenArrow();
        HideGateWorldArrow();

        RecalculateMaxZoomFromMap();

        UpdateStartWorldArrow(startTarget);

        Vector3 cameraPosition =
            controlledCamera.transform.position;

        Vector3 startPosition = new Vector3(
            startTarget.position.x,
            startTarget.position.y,
            cameraPosition.z
        );

        float startZoom = Mathf.Clamp(
            startPointZoomSize,
            minZoomSize,
            maxZoomSize
        );

        // Center exactly on the car during the start-point view. We deliberately
        // do not clamp this first frame so a car near a map edge can still be
        // shown in the middle of the screen.
        desiredCameraPosition = startPosition;
        hasDesiredPosition = true;
        controlledCamera.transform.position = startPosition;
        controlledCamera.orthographicSize = startZoom;

        if (startPointHoldSeconds > 0f)
        {
            float holdElapsed = 0f;

            while (holdElapsed < startPointHoldSeconds)
            {
                if (!assistedModeActive)
                {
                    assistedOpeningActive = false;
                    assistedOpeningRoutine = null;
                    yield break;
                }

                holdElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        Vector3 overviewPosition = startPosition;

        if (mapBoundsSprite != null)
        {
            Bounds mapBounds = GetEffectiveMapBounds();

            overviewPosition.x = mapBounds.center.x;
            overviewPosition.y = mapBounds.center.y;
        }

        float overviewZoom = CalculateOverviewZoomSize();

        float transitionDuration =
            Mathf.Max(0.05f, overviewTransitionSeconds);

        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            if (!assistedModeActive)
            {
                assistedOpeningActive = false;
                assistedOpeningRoutine = null;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(
                elapsed / transitionDuration
            );

            // SmootherStep gives an even gentler start and finish than
            // SmoothStep, which makes the start-point -> overview zoom feel
            // slower and easier to follow without changing the final view.
            float easedT =
                t * t * t *
                (t * (t * 6f - 15f) + 10f);

            Vector3 interpolatedPosition =
                Vector3.Lerp(
                    startPosition,
                    overviewPosition,
                    easedT
                );

            interpolatedPosition.z =
                controlledCamera.transform.position.z;

            controlledCamera.transform.position =
                interpolatedPosition;

            controlledCamera.orthographicSize =
                Mathf.Lerp(
                    startZoom,
                    overviewZoom,
                    easedT
                );

            UpdateStartWorldArrow(startTarget);

            desiredCameraPosition =
                interpolatedPosition;

            hasDesiredPosition = true;

            yield return null;
        }

        desiredCameraPosition = overviewPosition;
        hasDesiredPosition = true;
        controlledCamera.orthographicSize = overviewZoom;

        // The overview should obey the normal map bounds before control is
        // returned to the participant.
        ApplyDesiredCameraPosition();

        assistedOpeningActive = false;
        assistedOpeningRoutine = null;

        HideStartWorldArrow();
        RegisterParkingClickSuppression();

        UpdateGateWorldArrow();
        UpdateGateScreenArrow();

        onOverviewReady?.Invoke();
    }

    private float CalculateOverviewZoomSize()
    {
        if (controlledCamera == null)
            return Mathf.Clamp(
                maxZoomSize,
                minZoomSize,
                maxZoomSize
            );

        if (mapBoundsSprite == null)
        {
            return Mathf.Clamp(
                maxZoomSize,
                minZoomSize,
                maxZoomSize
            );
        }

        Bounds bounds = GetEffectiveMapBounds();

        float neededByHeight =
            bounds.size.y / 2f;

        float neededByWidth =
            bounds.size.x /
            (2f * Mathf.Max(0.01f, controlledCamera.aspect));

        float requiredSize =
            Mathf.Max(
                neededByHeight,
                neededByWidth
            );

        requiredSize *= Mathf.Clamp(
            overviewZoomMultiplier,
            0.5f,
            1.2f
        );

        return Mathf.Clamp(
            requiredSize,
            minZoomSize,
            maxZoomSize
        );
    }

    public void StopAssistedOpening()
    {
        if (assistedOpeningRoutine != null)
        {
            StopCoroutine(assistedOpeningRoutine);
            assistedOpeningRoutine = null;
        }

        assistedOpeningActive = false;
        HideStartWorldArrow();
    }

    private void HandleMouseWheelZoom()
    {
        if (!enableZoom)
            return;

        float scroll = Input.mouseScrollDelta.y;

        if (Mathf.Abs(scroll) < 0.01f)
            return;

        SetZoomSize(
            controlledCamera.orthographicSize -
            scroll * zoomSpeed
        );
    }

    private void HandleMouseDragPan()
    {
        if (!enableMouseDragPan)
        {
            ResetMousePanState();
            return;
        }

        Vector2 mousePosition = Input.mousePosition;

        if (Input.GetMouseButtonDown(0))
        {
            mousePanCandidate =
                !ignorePanWhenPointerOverUI ||
                !IsScreenPositionOverBlockingUI(mousePosition);

            mouseDragged = false;
            mousePressScreenPosition = mousePosition;
            lastMouseScreenPosition = mousePosition;
        }

        if (Input.GetMouseButton(0) && mousePanCandidate)
        {
            float totalMovement = Vector2.Distance(
                mousePressScreenPosition,
                mousePosition
            );

            if (!mouseDragged &&
                totalMovement >= dragThresholdPixels)
            {
                mouseDragged = true;
                RegisterParkingClickSuppression();
            }

            if (mouseDragged)
            {
                Vector3 movement = ScreenDragToWorldMovement(
                    lastMouseScreenPosition,
                    mousePosition,
                    mousePanSpeed
                );

                MoveCameraBy(movement);
                RegisterParkingClickSuppression();
            }

            lastMouseScreenPosition = mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
        {
            if (mouseDragged)
                RegisterParkingClickSuppression();

            ResetMousePanState();
        }
    }

    /// <summary>
    /// Handles one-finger pan and two-finger pinch.
    /// Returns true while Unity is reporting touch input so the same touch
    /// is not also processed by the mouse path on mobile/WebGL.
    /// </summary>
    private bool HandleTouchInput()
    {
        int touchCount = Input.touchCount;

        if (touchCount <= 0)
        {
            ResetTouchPanState();
            pinchInProgress = false;
            lastPinchDistance = -1f;
            return false;
        }

        if (touchCount >= 2)
        {
            touchPanCandidate = false;
            touchDragged = false;

            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);

            float currentDistance =
                Vector2.Distance(
                    first.position,
                    second.position
                );

            // A pinch is a map gesture, never a parking selection.
            RegisterParkingClickSuppression();

            // The first frame of a new pinch only establishes the baseline.
            if (!pinchInProgress)
            {
                pinchInProgress = true;
                lastPinchDistance = currentDistance;
                return true;
            }

            if (!enablePinchZoom || !enableZoom)
            {
                lastPinchDistance = currentDistance;
                return true;
            }

            if (ignorePanWhenPointerOverUI &&
                (IsScreenPositionOverBlockingUI(first.position) ||
                 IsScreenPositionOverBlockingUI(second.position)))
            {
                // Keep the baseline current so zoom does not jump when the
                // fingers leave the UI region.
                lastPinchDistance = currentDistance;
                return true;
            }

            if (lastPinchDistance > 0.01f &&
                currentDistance > 0.01f)
            {
                // Natural mobile pinch for an orthographic camera:
                //
                // Fingers APART:
                // currentDistance grows -> ratio < 1
                // orthographicSize shrinks -> ZOOM IN.
                //
                // Fingers TOGETHER:
                // currentDistance shrinks -> ratio > 1
                // orthographicSize grows -> ZOOM OUT.
                float distanceRatio =
                    lastPinchDistance /
                    currentDistance;

                float adjustedRatio = Mathf.Pow(
                    distanceRatio,
                    Mathf.Max(0.1f, pinchZoomSensitivity)
                );

                SetZoomSize(
                    controlledCamera.orthographicSize *
                    adjustedRatio
                );
            }

            lastPinchDistance = currentDistance;
            return true;
        }

        // If one finger remains after a pinch, wait until every finger has
        // been lifted before starting another one-finger pan.
        if (pinchInProgress)
        {
            lastPinchDistance = -1f;
            RegisterParkingClickSuppression();
            return true;
        }

        if (!enableTouchPan)
        {
            ResetTouchPanState();
            return true;
        }

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
        {
            touchPanCandidate =
                !ignorePanWhenPointerOverUI ||
                !IsScreenPositionOverBlockingUI(touch.position);

            touchDragged = false;
            touchPressScreenPosition = touch.position;
            lastTouchScreenPosition = touch.position;
        }

        if ((touch.phase == TouchPhase.Moved ||
             touch.phase == TouchPhase.Stationary) &&
            touchPanCandidate)
        {
            float totalMovement = Vector2.Distance(
                touchPressScreenPosition,
                touch.position
            );

            if (!touchDragged &&
                totalMovement >= dragThresholdPixels)
            {
                touchDragged = true;
                RegisterParkingClickSuppression();
            }

            if (touchDragged &&
                touch.phase == TouchPhase.Moved)
            {
                Vector3 movement = ScreenDragToWorldMovement(
                    lastTouchScreenPosition,
                    touch.position,
                    touchPanSpeed
                );

                MoveCameraBy(movement);
                RegisterParkingClickSuppression();
            }

            lastTouchScreenPosition = touch.position;
        }

        if (touch.phase == TouchPhase.Ended ||
            touch.phase == TouchPhase.Canceled)
        {
            if (touchDragged)
                RegisterParkingClickSuppression();

            ResetTouchPanState();
        }

        return true;
    }

    private Vector3 ScreenDragToWorldMovement(
        Vector2 previousScreenPosition,
        Vector2 currentScreenPosition,
        float speed)
    {
        if (controlledCamera == null || Screen.height <= 0)
            return Vector3.zero;

        // For an orthographic camera, world-units-per-screen-pixel is based
        // on the camera's vertical size. This keeps drag behavior stable even
        // while the camera itself is moving.
        float worldUnitsPerPixel =
            (controlledCamera.orthographicSize * 2f) /
            Screen.height;

        Vector2 screenDelta =
            previousScreenPosition -
            currentScreenPosition;

        return new Vector3(
            screenDelta.x * worldUnitsPerPixel * speed,
            screenDelta.y * worldUnitsPerPixel * speed,
            0f
        );
    }

    private bool IsScreenPositionOverBlockingUI(
        Vector2 screenPosition)
    {
        if (EventSystem.current == null)
            return false;

        PointerEventData pointerData =
            new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

        uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(
            pointerData,
            uiRaycastResults
        );

        foreach (RaycastResult result in uiRaycastResults)
        {
            if (result.gameObject == null)
                continue;

            Canvas canvas =
                result.gameObject.GetComponentInParent<Canvas>();

            // Screen-space UI blocks map gestures. World-space canvases
            // attached to parking spots do not.
            if (canvas != null &&
                canvas.renderMode != RenderMode.WorldSpace)
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterParkingClickSuppression()
    {
        suppressParkingClickUntilTime =
            Time.unscaledTime +
            ParkingClickSuppressionSeconds;
    }

    /// <summary>
    /// ParkingSpot calls this before accepting a pointer click.
    /// It is true only in assisted mode while a drag/pinch is occurring or
    /// immediately after one has ended.
    /// </summary>
    public bool ShouldSuppressParkingSpotClick()
    {
        if (!assistedModeActive)
            return false;

        return assistedOpeningActive ||
               mouseDragged ||
               touchDragged ||
               pinchInProgress ||
               Time.unscaledTime <=
               suppressParkingClickUntilTime;
    }

    private void ResetMousePanState()
    {
        mousePanCandidate = false;
        mouseDragged = false;
    }

    private void ResetTouchPanState()
    {
        touchPanCandidate = false;
        touchDragged = false;
    }

    private void ResetPointerGestureState()
    {
        ResetMousePanState();
        ResetTouchPanState();
        pinchInProgress = false;
        lastPinchDistance = -1f;
    }

    private void SetZoomSize(float newSize)
    {
        if (controlledCamera == null)
            return;

        controlledCamera.orthographicSize = Mathf.Clamp(
            newSize,
            minZoomSize,
            maxZoomSize
        );

        // The allowed camera center changes with zoom level, so clamp again.
        ApplyDesiredCameraPosition();
    }

    /// <summary>
    /// Optional UI button hook. Assign this to a "+" button OnClick().
    /// </summary>
    public void ZoomIn()
    {
        if (!assistedModeActive ||
            !enableZoom ||
            controlledCamera == null)
        {
            return;
        }

        SetZoomSize(
            controlledCamera.orthographicSize /
            Mathf.Max(1.01f, zoomButtonFactor)
        );
    }

    /// <summary>
    /// Optional UI button hook. Assign this to a "-" button OnClick().
    /// </summary>
    public void ZoomOut()
    {
        if (!assistedModeActive ||
            !enableZoom ||
            controlledCamera == null)
        {
            return;
        }

        SetZoomSize(
            controlledCamera.orthographicSize *
            Mathf.Max(1.01f, zoomButtonFactor)
        );
    }

    private void HandleKeyboardPan()
    {
        if (!enableKeyboardPan)
            return;

        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.A) ||
            Input.GetKey(KeyCode.LeftArrow))
        {
            move.x -= 1f;
        }

        if (Input.GetKey(KeyCode.D) ||
            Input.GetKey(KeyCode.RightArrow))
        {
            move.x += 1f;
        }

        if (Input.GetKey(KeyCode.W) ||
            Input.GetKey(KeyCode.UpArrow))
        {
            move.y += 1f;
        }

        if (Input.GetKey(KeyCode.S) ||
            Input.GetKey(KeyCode.DownArrow))
        {
            move.y -= 1f;
        }

        if (move.sqrMagnitude < 0.001f)
            return;

        MoveCameraBy(
            move.normalized *
            keyboardPanSpeed *
            Time.deltaTime
        );
    }

    private void MoveCameraBy(Vector3 movement)
    {
        if (!hasDesiredPosition)
        {
            desiredCameraPosition =
                controlledCamera.transform.position;

            hasDesiredPosition = true;
        }

        desiredCameraPosition += movement;
        desiredCameraPosition.z =
            controlledCamera.transform.position.z;

        ApplyDesiredCameraPosition();
    }

    private void ApplyDesiredCameraPosition()
    {
        if (controlledCamera == null)
            return;

        if (!hasDesiredPosition)
        {
            desiredCameraPosition =
                controlledCamera.transform.position;

            hasDesiredPosition = true;
        }

        Vector3 pos = desiredCameraPosition;

        if (useMapBoundsClamp &&
            mapBoundsSprite != null)
        {
            pos = ClampToMapBounds(pos);
        }

        pos.z = controlledCamera.transform.position.z;

        desiredCameraPosition = pos;
        controlledCamera.transform.position = pos;
    }

    private Bounds GetEffectiveMapBounds()
    {
        if (mapBoundsSprite == null)
            return new Bounds(Vector3.zero, Vector3.zero);

        Bounds raw = mapBoundsSprite.bounds;

        float insetX =
            raw.size.x *
            Mathf.Clamp(mapBoundsInsetXFraction, 0f, 0.45f);

        float insetY =
            raw.size.y *
            Mathf.Clamp(mapBoundsInsetYFraction, 0f, 0.45f);

        Vector3 size = raw.size;
        size.x = Mathf.Max(0.01f, size.x - insetX * 2f);
        size.y = Mathf.Max(0.01f, size.y - insetY * 2f);

        return new Bounds(raw.center, size);
    }

    private Vector3 ClampToMapBounds(Vector3 position)
    {
        Bounds b = GetEffectiveMapBounds();

        float halfHeight =
            controlledCamera.orthographicSize;

        float halfWidth =
            halfHeight * controlledCamera.aspect;

        float minX = b.min.x + halfWidth;
        float maxX = b.max.x - halfWidth;
        float minY = b.min.y + halfHeight;
        float maxY = b.max.y - halfHeight;

        if (minX <= maxX)
            position.x = Mathf.Clamp(
                position.x,
                minX,
                maxX
            );
        else
            position.x = b.center.x;

        if (minY <= maxY)
            position.y = Mathf.Clamp(
                position.y,
                minY,
                maxY
            );
        else
            position.y = b.center.y;

        return position;
    }

    private void UpdateStartWorldArrow(Transform startTarget)
    {
        if (startWorldArrow == null || startTarget == null)
            return;

        startWorldArrow.gameObject.SetActive(true);

        Vector2 directionFromStart =
            startWorldArrowDirectionFromStart;

        if (directionFromStart.sqrMagnitude < 0.001f)
            directionFromStart = Vector2.up;

        directionFromStart.Normalize();

        Vector3 arrowPosition =
            startTarget.position +
            (Vector3)(
                directionFromStart *
                startWorldArrowDistance
            );

        arrowPosition.z = startWorldArrow.position.z;
        startWorldArrow.position = arrowPosition;

        Vector2 directionToStart =
            (Vector2)startTarget.position -
            (Vector2)startWorldArrow.position;

        if (directionToStart.sqrMagnitude < 0.001f)
            directionToStart = Vector2.down;

        directionToStart.Normalize();

        float angle =
            Mathf.Atan2(
                directionToStart.y,
                directionToStart.x
            ) * Mathf.Rad2Deg;

        startWorldArrow.rotation =
            Quaternion.Euler(
                0f,
                0f,
                angle + startWorldArrowRotationOffset
            );
    }

    private void HideStartWorldArrow()
    {
        if (startWorldArrow != null)
            startWorldArrow.gameObject.SetActive(false);
    }

    private void UpdateGateWorldArrow()
    {
        if (gateWorldArrow == null)
            return;

        if (!assistedModeActive ||
            destinationGate == null)
        {
            HideGateWorldArrow();
            return;
        }

        if (hideWorldArrowWhenGateOffScreen &&
            controlledCamera != null)
        {
            Vector3 viewportPoint =
                controlledCamera.WorldToViewportPoint(
                    destinationGate.position
                );

            bool gateIsVisible =
                viewportPoint.z > 0f &&
                viewportPoint.x >= 0f &&
                viewportPoint.x <= 1f &&
                viewportPoint.y >= 0f &&
                viewportPoint.y <= 1f;

            if (!gateIsVisible)
            {
                HideGateWorldArrow();
                return;
            }
        }

        gateWorldArrow.gameObject.SetActive(true);

        Vector2 directionFromGate =
            gateWorldArrowDirectionFromGate;

        if (directionFromGate.sqrMagnitude < 0.001f)
            directionFromGate = Vector2.up;

        directionFromGate.Normalize();

        Vector3 arrowPosition =
            destinationGate.position +
            (Vector3)(
                directionFromGate *
                gateWorldArrowDistanceFromGate
            );

        arrowPosition.z = gateWorldArrow.position.z;

        gateWorldArrow.position = arrowPosition;

        Vector2 directionToGate =
            (Vector2)destinationGate.position -
            (Vector2)gateWorldArrow.position;

        if (directionToGate.sqrMagnitude < 0.001f)
            directionToGate = Vector2.down;

        directionToGate.Normalize();

        float angle =
            Mathf.Atan2(
                directionToGate.y,
                directionToGate.x
            ) * Mathf.Rad2Deg;

        gateWorldArrow.rotation =
            Quaternion.Euler(
                0f,
                0f,
                angle + gateWorldArrowRotationOffset
            );
    }

    private void UpdateGateScreenArrow()
    {
        if (gateScreenArrow == null)
            return;

        if (!assistedModeActive ||
            controlledCamera == null ||
            destinationGate == null)
        {
            HideGateScreenArrow();
            return;
        }

        Vector3 viewportPoint =
            controlledCamera.WorldToViewportPoint(
                destinationGate.position
            );

        bool gateIsVisible =
            viewportPoint.z > 0f &&
            viewportPoint.x >= 0f &&
            viewportPoint.x <= 1f &&
            viewportPoint.y >= 0f &&
            viewportPoint.y <= 1f;

        if (gateIsVisible)
        {
            HideGateScreenArrow();
            return;
        }

        gateScreenArrow.gameObject.SetActive(true);

        Vector2 directionFromCenter =
            new Vector2(
                viewportPoint.x - 0.5f,
                viewportPoint.y - 0.5f
            );

        if (viewportPoint.z < 0f)
            directionFromCenter =
                -directionFromCenter;

        if (directionFromCenter.sqrMagnitude < 0.001f)
            directionFromCenter = Vector2.up;

        directionFromCenter.Normalize();

        Canvas canvas =
            gateScreenArrow.GetComponentInParent<Canvas>();

        RectTransform canvasRect =
            canvas != null
                ? canvas.GetComponent<RectTransform>()
                : null;

        if (canvasRect == null)
            return;

        float halfWidth =
            canvasRect.rect.width * 0.5f -
            gateScreenArrowEdgeMargin;

        float halfHeight =
            canvasRect.rect.height * 0.5f -
            gateScreenArrowEdgeMargin;

        float scaleX =
            Mathf.Abs(directionFromCenter.x) > 0.001f
                ? halfWidth /
                  Mathf.Abs(directionFromCenter.x)
                : Mathf.Infinity;

        float scaleY =
            Mathf.Abs(directionFromCenter.y) > 0.001f
                ? halfHeight /
                  Mathf.Abs(directionFromCenter.y)
                : Mathf.Infinity;

        float scale = Mathf.Min(scaleX, scaleY);

        Vector2 localArrowPosition =
            directionFromCenter * scale;

        gateScreenArrow.anchoredPosition =
            localArrowPosition;

        float angle =
            Mathf.Atan2(
                directionFromCenter.y,
                directionFromCenter.x
            ) * Mathf.Rad2Deg;

        gateScreenArrow.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                angle + gateScreenArrowRotationOffset
            );
    }

    private void HideGateScreenArrow()
    {
        if (gateScreenArrow != null)
            gateScreenArrow.gameObject.SetActive(false);
    }

    private void HideGateWorldArrow()
    {
        if (gateWorldArrow != null)
            gateWorldArrow.gameObject.SetActive(false);
    }
}
