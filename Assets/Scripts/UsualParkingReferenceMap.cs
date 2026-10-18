using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[DefaultExecutionOrder(10000)]
public class UsualParkingReferenceMap : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("The existing panel that contains the usual-parking question.")]
    public GameObject usualParkingQuestionPanel;

    [Tooltip("Transparent full-screen overlay containing the instruction panel and Close Map button.")]
    public GameObject referenceMapOverlay;

    [Tooltip("The existing tutorial explanation panel that otherwise remains visible behind the map.")]
    public GameObject tutorialPanel;

    [Tooltip("Optional existing practice banner. It will be hidden only while the reference map is open.")]
    public GameObject practiceBanner;

    [Header("Hide While Reference Map Is Open")]
    [Tooltip("Assign DrivingButtonsCanvas here. If the speed bar is inside it, the speed bar is hidden too.")]
    public GameObject drivingControlsRoot;

    [Tooltip("Optional. Assign SpeedControlPanel here only if the speed bar is NOT a child of DrivingButtonsCanvas.")]
    public GameObject speedControlRoot;

    [Tooltip("Assign the Car GameObject.")]
    public GameObject carRoot;

    [Tooltip("Assign DestinationArrowCanvas. If left empty, the script looks for an object with this exact name automatically.")]
    public GameObject destinationArrowCanvas;

    [Tooltip("Optional extra arrow/root objects that should disappear on the reference map.")]
    public GameObject[] arrowRoots;

    [Header("Neutral Parking Appearance")]
    [Tooltip("If ON, visible ParkingSpot SpriteRenderers are temporarily recolored to the road-grey colour instead of showing legal/illegal/occupied colours.")]
    public bool neutralizeParkingSpotColors = true;

    [Tooltip("Road grey requested: RGBA 155,155,155,255.")]
    public Color parkingReferenceColor = new Color32(155, 155, 155, 255);

    [Tooltip("Hide world-space/UI labels attached under ParkingSpot objects so legality/type text is not revealed.")]
    public bool hideParkingSpotCanvases = true;

    [Header("Map")]
    public Camera mapCamera;

    [Tooltip("Use the same map-bounds SpriteRenderer used by AssistedMapCameraController.")]
    public SpriteRenderer mapBoundsSprite;

    [Header("Existing Camera Controllers")]
    [Tooltip("Assign AssistedMapCameraController so it cannot force its normal camera state while the reference map is open.")]
    public AssistedMapCameraController assistedMapController;

    [Tooltip("Assign the CameraFollow component on MainCamera if present.")]
    public CameraFollow cameraFollow;

    [Header("Zoom")]
    public float minZoomSize = 120f;
    public float maxZoomSize = 2300f;
    public float zoomSpeed = 120f;

    [Range(0.5f, 1f)]
    [Tooltip("Initial reference-map overview zoom. 0.8 means the map starts 20% more zoomed-in than a full-map fit.")]
    public float initialOverviewZoomMultiplier = 0.8f;

    [Min(1.01f)]
    [Tooltip("Used by optional UI + / - buttons.")]
    public float zoomButtonFactor = 1.25f;

    [Header("Pan")]
    public float mousePanSpeed = 1f;
    public float touchPanSpeed = 1f;
    public float keyboardPanSpeed = 750f;

    [Header("Map Bounds")]
    [Range(0f, 0.45f)]
    public float mapBoundsInsetXFraction = 0.04f;

    [Range(0f, 0.45f)]
    public float mapBoundsInsetYFraction = 0.04f;

    private bool mapOpen;

    // Original camera state.
    private Vector3 savedCameraPosition;
    private float savedZoom;

    // Original UI/world-object state.
    private bool tutorialPanelWasActive;
    private bool practiceBannerWasActive;
    private bool questionPanelWasActive;
    private bool drivingControlsWasActive;
    private bool speedControlWasActive;
    private bool carWasActive;

    private readonly List<GameObjectState> arrowObjectStates =
        new List<GameObjectState>();

    // Original camera-controller states.
    private bool assistedControllerWasEnabled;
    private bool cameraFollowWasEnabled;

    // Parking appearance state.
    private readonly List<SpriteRendererState> parkingSpriteStates =
        new List<SpriteRendererState>();

    private readonly List<CanvasState> parkingCanvasStates =
        new List<CanvasState>();

    private sealed class GameObjectState
    {
        public GameObject gameObject;
        public bool wasActive;
    }

    private sealed class SpriteRendererState
    {
        public SpriteRenderer renderer;
        public bool wasEnabled;
        public Color originalColor;
    }

    private sealed class CanvasState
    {
        public Canvas canvas;
        public bool wasEnabled;
    }

    // Mouse pan state.
    private bool mouseDragging;
    private Vector2 lastMousePosition;

    // Touch state.
    private bool touchDragging;
    private Vector2 lastTouchPosition;

    private bool pinchActive;
    private float previousPinchDistance = -1f;

    public bool IsMapOpen => mapOpen;

    private void Awake()
    {
        if (mapCamera == null)
            mapCamera = Camera.main;

        if (destinationArrowCanvas == null)
            destinationArrowCanvas = FindGameObjectIncludingInactive("DestinationArrowCanvas");

        if (referenceMapOverlay != null)
            referenceMapOverlay.SetActive(false);
    }

    private void OnDisable()
    {
        // Safety net: if this component gets disabled while the map is open,
        // restore the normal tutorial/question view.
        if (mapOpen)
            CloseMap();
    }

    private void Update()
    {
        if (!mapOpen || mapCamera == null)
            return;

        // Touch first so touch-enabled WebGL/mobile does not also process
        // the same gesture as a mouse drag.
        bool touchActive = HandleTouch();

        if (!touchActive)
        {
            HandleMouseWheel();
            HandleMouseDrag();
        }

        HandleKeyboard();
    }

    private void LateUpdate()
    {
        if (!mapOpen)
            return;

        // Some gameplay scripts refresh parking colours/arrows during Update.
        // Re-assert the neutral reference-map appearance at the end of the frame.
        EnforceReferenceMapVisuals();
    }

    public void OpenMap()
    {
        if (mapOpen)
            return;

        if (mapCamera == null)
            mapCamera = Camera.main;

        if (mapCamera == null)
        {
            Debug.LogWarning(
                "[UsualParkingReferenceMap] No map camera / Main Camera was found."
            );
            return;
        }

        mapOpen = true;

        // Save camera state so returning to the question does not alter
        // the following tutorial/practice view.
        savedCameraPosition = mapCamera.transform.position;
        savedZoom = mapCamera.orthographicSize;

        // Remember which UI/world objects were active before opening.
        questionPanelWasActive =
            usualParkingQuestionPanel != null &&
            usualParkingQuestionPanel.activeSelf;

        tutorialPanelWasActive =
            tutorialPanel != null &&
            tutorialPanel.activeSelf;

        practiceBannerWasActive =
            practiceBanner != null &&
            practiceBanner.activeSelf;

        drivingControlsWasActive =
            drivingControlsRoot != null &&
            drivingControlsRoot.activeSelf;

        speedControlWasActive =
            speedControlRoot != null &&
            speedControlRoot.activeSelf;

        carWasActive =
            carRoot != null &&
            carRoot.activeSelf;

        // Temporarily stop scripts that normally own MainCamera.
        if (assistedMapController != null)
        {
            assistedControllerWasEnabled =
                assistedMapController.enabled;

            assistedMapController.enabled = false;
        }

        if (cameraFollow != null)
        {
            cameraFollowWasEnabled =
                cameraFollow.enabled;

            cameraFollow.enabled = false;
        }

        // Hide all normal tutorial/question UI that would otherwise sit
        // above the map.
        if (tutorialPanel != null)
            tutorialPanel.SetActive(false);

        if (practiceBanner != null)
            practiceBanner.SetActive(false);

        if (drivingControlsRoot != null)
            drivingControlsRoot.SetActive(false);

        if (speedControlRoot != null)
            speedControlRoot.SetActive(false);

        // Hide car and any assigned arrows only while this reference map is open.
        if (carRoot != null)
            carRoot.SetActive(false);

        HideArrowRoots();

        // IMPORTANT:
        // UsualParkingQuestion.OnDisable() restores the world street labels,
        // so hiding this panel also makes the street names visible.
        if (usualParkingQuestionPanel != null)
            usualParkingQuestionPanel.SetActive(false);

        ApplyNeutralParkingAppearance();

        if (referenceMapOverlay != null)
        {
            referenceMapOverlay.SetActive(true);
            referenceMapOverlay.transform.SetAsLastSibling();
        }

        mapCamera.enabled = true;
        mapCamera.orthographic = true;

        ResetGestureState();
        ShowWholeMap();
    }

    public void CloseMap()
    {
        if (!mapOpen)
            return;

        mapOpen = false;
        ResetGestureState();

        RestoreParkingAppearance();

        // Restore camera exactly as it was before opening the map.
        if (mapCamera != null)
        {
            mapCamera.transform.position =
                savedCameraPosition;

            mapCamera.orthographicSize =
                Mathf.Max(0.01f, savedZoom);
        }

        if (referenceMapOverlay != null)
            referenceMapOverlay.SetActive(false);

        // Restore only what was active before opening.
        if (tutorialPanel != null)
            tutorialPanel.SetActive(tutorialPanelWasActive);

        if (practiceBanner != null)
            practiceBanner.SetActive(practiceBannerWasActive);

        if (drivingControlsRoot != null)
            drivingControlsRoot.SetActive(drivingControlsWasActive);

        if (speedControlRoot != null)
            speedControlRoot.SetActive(speedControlWasActive);

        if (carRoot != null)
            carRoot.SetActive(carWasActive);

        RestoreArrowRoots();

        if (usualParkingQuestionPanel != null)
            usualParkingQuestionPanel.SetActive(questionPanelWasActive);

        // Restore normal camera ownership after restoring position/zoom.
        if (cameraFollow != null)
            cameraFollow.enabled = cameraFollowWasEnabled;

        if (assistedMapController != null)
            assistedMapController.enabled = assistedControllerWasEnabled;
    }

    private void HideArrowRoots()
    {
        arrowObjectStates.Clear();

        HashSet<GameObject> seen =
            new HashSet<GameObject>();

        RememberAndHideArrowObject(destinationArrowCanvas, seen);

        if (arrowRoots != null)
        {
            foreach (GameObject arrowRoot in arrowRoots)
                RememberAndHideArrowObject(arrowRoot, seen);
        }

        // CarNodeFollower creates the yellow direction arrows as independent
        // runtime GameObjects named "MovementArrow_To_<node>".
        Transform[] allTransforms =
            FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (Transform candidate in allTransforms)
        {
            if (candidate == null)
                continue;

            GameObject candidateObject = candidate.gameObject;

            if (!candidateObject.name.StartsWith(
                    "MovementArrow_To_",
                    System.StringComparison.Ordinal))
            {
                continue;
            }

            RememberAndHideArrowObject(candidateObject, seen);
        }
    }

    private void RememberAndHideArrowObject(
        GameObject target,
        HashSet<GameObject> seen)
    {
        if (target == null || !seen.Add(target))
            return;

        arrowObjectStates.Add(
            new GameObjectState
            {
                gameObject = target,
                wasActive = target.activeSelf
            }
        );

        target.SetActive(false);
    }

    private void RestoreArrowRoots()
    {
        foreach (GameObjectState state in arrowObjectStates)
        {
            if (state == null || state.gameObject == null)
                continue;

            state.gameObject.SetActive(state.wasActive);
        }

        arrowObjectStates.Clear();
    }

    private void ApplyNeutralParkingAppearance()
    {
        RestoreParkingAppearance();

        if (!neutralizeParkingSpotColors &&
            !hideParkingSpotCanvases)
        {
            return;
        }

        ParkingSpot[] spots =
            FindObjectsByType<ParkingSpot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        HashSet<SpriteRenderer> seenSprites =
            new HashSet<SpriteRenderer>();

        HashSet<Canvas> seenCanvases =
            new HashSet<Canvas>();

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            if (neutralizeParkingSpotColors)
            {
                SpriteRenderer[] spriteRenderers =
                    spot.GetComponentsInChildren<SpriteRenderer>(true);

                foreach (SpriteRenderer spriteRenderer in spriteRenderers)
                {
                    if (spriteRenderer == null ||
                        !seenSprites.Add(spriteRenderer))
                    {
                        continue;
                    }

                    parkingSpriteStates.Add(
                        new SpriteRendererState
                        {
                            renderer = spriteRenderer,
                            wasEnabled = spriteRenderer.enabled,
                            originalColor = spriteRenderer.color
                        }
                    );

                    // Keep the original enabled/disabled state.
                    // Only visible parking graphics are recolored.
                    if (spriteRenderer.enabled)
                        spriteRenderer.color = parkingReferenceColor;
                }
            }

            if (hideParkingSpotCanvases)
            {
                Canvas[] canvases =
                    spot.GetComponentsInChildren<Canvas>(true);

                foreach (Canvas canvasComponent in canvases)
                {
                    if (canvasComponent == null ||
                        !seenCanvases.Add(canvasComponent))
                    {
                        continue;
                    }

                    parkingCanvasStates.Add(
                        new CanvasState
                        {
                            canvas = canvasComponent,
                            wasEnabled = canvasComponent.enabled
                        }
                    );

                    canvasComponent.enabled = false;
                }
            }
        }

        Debug.Log(
            "[UsualParkingReferenceMap] Neutral parking appearance applied | " +
            $"spots={spots.Length}, sprites={parkingSpriteStates.Count}, " +
            $"canvasesHidden={parkingCanvasStates.Count}"
        );
    }

    private void RestoreParkingAppearance()
    {
        foreach (SpriteRendererState state in parkingSpriteStates)
        {
            if (state == null || state.renderer == null)
                continue;

            state.renderer.color = state.originalColor;
            state.renderer.enabled = state.wasEnabled;
        }

        foreach (CanvasState state in parkingCanvasStates)
        {
            if (state == null || state.canvas == null)
                continue;

            state.canvas.enabled = state.wasEnabled;
        }

        parkingSpriteStates.Clear();
        parkingCanvasStates.Clear();
    }

    private void EnforceReferenceMapVisuals()
    {
        if (carRoot != null && carRoot.activeSelf)
            carRoot.SetActive(false);

        if (drivingControlsRoot != null &&
            drivingControlsRoot.activeSelf)
        {
            drivingControlsRoot.SetActive(false);
        }

        if (speedControlRoot != null &&
            speedControlRoot.activeSelf)
        {
            speedControlRoot.SetActive(false);
        }

        if (destinationArrowCanvas != null &&
            destinationArrowCanvas.activeSelf)
        {
            destinationArrowCanvas.SetActive(false);
        }

        if (arrowRoots != null)
        {
            foreach (GameObject arrowRoot in arrowRoots)
            {
                if (arrowRoot != null && arrowRoot.activeSelf)
                    arrowRoot.SetActive(false);
            }
        }

        // Catch any yellow movement arrow that was instantiated independently.
        Transform[] allTransforms =
            FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (Transform candidate in allTransforms)
        {
            if (candidate == null)
                continue;

            if (candidate.gameObject.activeSelf &&
                candidate.gameObject.name.StartsWith(
                    "MovementArrow_To_",
                    System.StringComparison.Ordinal))
            {
                candidate.gameObject.SetActive(false);
            }
        }

        // Re-apply the neutral colour every LateUpdate. ParkingSpot or other
        // gameplay scripts may refresh their normal green/red/orange colours
        // during Update, which caused only some spaces to remain grey before.
        if (neutralizeParkingSpotColors)
        {
            foreach (SpriteRendererState state in parkingSpriteStates)
            {
                if (state == null ||
                    state.renderer == null ||
                    !state.renderer.enabled)
                {
                    continue;
                }

                state.renderer.color = parkingReferenceColor;
            }
        }

        if (hideParkingSpotCanvases)
        {
            foreach (CanvasState state in parkingCanvasStates)
            {
                if (state == null || state.canvas == null)
                    continue;

                state.canvas.enabled = false;
            }
        }
    }

    private static GameObject FindGameObjectIncludingInactive(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        Transform[] allTransforms =
            FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (Transform candidate in allTransforms)
        {
            if (candidate != null &&
                string.Equals(
                    candidate.gameObject.name,
                    objectName,
                    System.StringComparison.Ordinal))
            {
                return candidate.gameObject;
            }
        }

        return null;
    }

    public void ZoomIn()
    {
        if (!mapOpen || mapCamera == null)
            return;

        SetZoom(
            mapCamera.orthographicSize /
            Mathf.Max(1.01f, zoomButtonFactor)
        );
    }

    public void ZoomOut()
    {
        if (!mapOpen || mapCamera == null)
            return;

        SetZoom(
            mapCamera.orthographicSize *
            Mathf.Max(1.01f, zoomButtonFactor)
        );
    }

    public void ResetMapView()
    {
        if (!mapOpen)
            return;

        ShowWholeMap();
    }

    private void ShowWholeMap()
    {
        if (mapBoundsSprite == null || mapCamera == null)
        {
            Debug.LogWarning(
                "[UsualParkingReferenceMap] Map Bounds Sprite is missing."
            );
            return;
        }

        Bounds bounds = GetEffectiveBounds();

        Vector3 position =
            mapCamera.transform.position;

        position.x = bounds.center.x;
        position.y = bounds.center.y;

        mapCamera.transform.position = position;

        float neededByHeight =
            bounds.size.y / 2f;

        float neededByWidth =
            bounds.size.x /
            (2f * Mathf.Max(0.01f, mapCamera.aspect));

        float fittedOverviewSize =
            Mathf.Max(neededByHeight, neededByWidth);

        fittedOverviewSize *= Mathf.Clamp(
            initialOverviewZoomMultiplier,
            0.5f,
            1f
        );

        mapCamera.orthographicSize =
            Mathf.Clamp(
                fittedOverviewSize,
                minZoomSize,
                maxZoomSize
            );

        ClampCameraToBounds();
    }

    private void HandleMouseWheel()
    {
        float scroll = Input.mouseScrollDelta.y;

        if (Mathf.Abs(scroll) < 0.01f)
            return;

        SetZoom(
            mapCamera.orthographicSize -
            scroll * zoomSpeed
        );
    }

    private void HandleMouseDrag()
    {
        Vector2 mousePosition =
            Input.mousePosition;

        if (Input.GetMouseButtonDown(0))
        {
            if (IsScreenPositionOverUI(mousePosition))
            {
                mouseDragging = false;
                return;
            }

            mouseDragging = true;
            lastMousePosition = mousePosition;
        }

        if (Input.GetMouseButton(0) && mouseDragging)
        {
            MoveFromScreenDrag(
                lastMousePosition,
                mousePosition,
                mousePanSpeed
            );

            lastMousePosition = mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
            mouseDragging = false;
    }

    /// <summary>
    /// Returns true while Unity is reporting touch input.
    /// Supports one-finger pan and two-finger pinch zoom.
    /// </summary>
    private bool HandleTouch()
    {
        int touchCount = Input.touchCount;

        if (touchCount <= 0)
        {
            touchDragging = false;
            pinchActive = false;
            previousPinchDistance = -1f;
            return false;
        }

        if (touchCount >= 2)
        {
            touchDragging = false;

            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);

            float distance =
                Vector2.Distance(
                    first.position,
                    second.position
                );

            if (!pinchActive)
            {
                pinchActive = true;
                previousPinchDistance = distance;
                return true;
            }

            if (previousPinchDistance > 0.01f &&
                distance > 0.01f)
            {
                float ratio =
                    previousPinchDistance /
                    distance;

                SetZoom(
                    mapCamera.orthographicSize *
                    ratio
                );
            }

            previousPinchDistance = distance;
            return true;
        }

        // Wait until all fingers are lifted after a pinch.
        if (pinchActive)
            return true;

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
        {
            if (IsTouchOverUI(touch))
            {
                touchDragging = false;
                return true;
            }

            touchDragging = true;
            lastTouchPosition = touch.position;
        }
        else if (touch.phase == TouchPhase.Moved &&
                 touchDragging)
        {
            MoveFromScreenDrag(
                lastTouchPosition,
                touch.position,
                touchPanSpeed
            );

            lastTouchPosition = touch.position;
        }
        else if (touch.phase == TouchPhase.Ended ||
                 touch.phase == TouchPhase.Canceled)
        {
            touchDragging = false;
        }

        return true;
    }

    private void HandleKeyboard()
    {
        Vector3 direction = Vector3.zero;

        if (Input.GetKey(KeyCode.LeftArrow) ||
            Input.GetKey(KeyCode.A))
        {
            direction.x -= 1f;
        }

        if (Input.GetKey(KeyCode.RightArrow) ||
            Input.GetKey(KeyCode.D))
        {
            direction.x += 1f;
        }

        if (Input.GetKey(KeyCode.UpArrow) ||
            Input.GetKey(KeyCode.W))
        {
            direction.y += 1f;
        }

        if (Input.GetKey(KeyCode.DownArrow) ||
            Input.GetKey(KeyCode.S))
        {
            direction.y -= 1f;
        }

        if (direction.sqrMagnitude < 0.001f)
            return;

        mapCamera.transform.position +=
            direction.normalized *
            keyboardPanSpeed *
            Time.unscaledDeltaTime;

        ClampCameraToBounds();
    }

    private void MoveFromScreenDrag(
        Vector2 previous,
        Vector2 current,
        float speed)
    {
        if (mapCamera == null || Screen.height <= 0)
            return;

        float worldUnitsPerPixel =
            (mapCamera.orthographicSize * 2f) /
            Screen.height;

        Vector2 screenDelta =
            previous - current;

        Vector3 movement = new Vector3(
            screenDelta.x * worldUnitsPerPixel * speed,
            screenDelta.y * worldUnitsPerPixel * speed,
            0f
        );

        mapCamera.transform.position += movement;

        ClampCameraToBounds();
    }

    private void SetZoom(float newSize)
    {
        if (mapCamera == null)
            return;

        mapCamera.orthographicSize =
            Mathf.Clamp(
                newSize,
                minZoomSize,
                maxZoomSize
            );

        ClampCameraToBounds();
    }

    private void ClampCameraToBounds()
    {
        if (mapBoundsSprite == null || mapCamera == null)
            return;

        Bounds bounds = GetEffectiveBounds();

        Vector3 position =
            mapCamera.transform.position;

        float halfHeight =
            mapCamera.orthographicSize;

        float halfWidth =
            halfHeight * mapCamera.aspect;

        float minX =
            bounds.min.x + halfWidth;

        float maxX =
            bounds.max.x - halfWidth;

        float minY =
            bounds.min.y + halfHeight;

        float maxY =
            bounds.max.y - halfHeight;

        if (minX <= maxX)
            position.x =
                Mathf.Clamp(position.x, minX, maxX);
        else
            position.x =
                bounds.center.x;

        if (minY <= maxY)
            position.y =
                Mathf.Clamp(position.y, minY, maxY);
        else
            position.y =
                bounds.center.y;

        mapCamera.transform.position = position;
    }

    private Bounds GetEffectiveBounds()
    {
        Bounds raw =
            mapBoundsSprite.bounds;

        float insetX =
            raw.size.x *
            Mathf.Clamp(
                mapBoundsInsetXFraction,
                0f,
                0.45f
            );

        float insetY =
            raw.size.y *
            Mathf.Clamp(
                mapBoundsInsetYFraction,
                0f,
                0.45f
            );

        Vector3 size = raw.size;

        size.x =
            Mathf.Max(
                0.01f,
                size.x - insetX * 2f
            );

        size.y =
            Mathf.Max(
                0.01f,
                size.y - insetY * 2f
            );

        return new Bounds(
            raw.center,
            size
        );
    }

    private bool IsScreenPositionOverUI(
        Vector2 screenPosition)
    {
        if (EventSystem.current == null)
            return false;

        PointerEventData pointerData =
            new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

        List<RaycastResult> results =
            new List<RaycastResult>();

        EventSystem.current.RaycastAll(
            pointerData,
            results
        );

        return results.Count > 0;
    }

    private bool IsTouchOverUI(Touch touch)
    {
        if (EventSystem.current == null)
            return false;

        return EventSystem.current.IsPointerOverGameObject(
            touch.fingerId
        );
    }

    private void ResetGestureState()
    {
        mouseDragging = false;
        touchDragging = false;
        pinchActive = false;
        previousPinchDistance = -1f;
    }
}
