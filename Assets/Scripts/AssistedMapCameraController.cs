using UnityEngine;

public class AssistedMapCameraController : MonoBehaviour
{
    [Header("Camera")]
    public Camera controlledCamera;

    [Header("Mode")]
    public bool assistedModeActive = false;

    [Header("Disable Only In Assisted Mode")]
    public Behaviour[] disableInAssistedMode;

    [Header("Destination / Gate")]
    public Transform destinationGate;
    public bool startAtGate = true;
    public float gateStartZoom = 450f;

    [Header("Map Bounds")]
    public SpriteRenderer mapBoundsSprite;
    public bool autoCalculateMaxZoomFromMap = true;
    public bool useMapBoundsClamp = true;

    [Header("Zoom")]
    public bool enableZoom = true;
    public float minZoomSize = 120f;
    public float maxZoomSize = 2300f;
    public float zoomSpeed = 120f;

    [Header("Pan")]
    public bool enableMouseDragPan = true;
    public bool enableKeyboardPan = true;
    public float mousePanSpeed = 1f;
    public float keyboardPanSpeed = 750f;

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

    private Vector3 desiredCameraPosition;
    private Vector3 lastMouseWorldPosition;
    private bool hasDesiredPosition = false;
    private bool isDragging = false;

    private void Awake()
    {
        if (controlledCamera == null)
            controlledCamera = GetComponent<Camera>();

        if (controlledCamera == null)
            controlledCamera = Camera.main;

        if (controlledCamera != null)
        {
            desiredCameraPosition = controlledCamera.transform.position;
            hasDesiredPosition = true;
        }

        HideGateScreenArrow();
        HideGateWorldArrow();
    }

    private void Start()
    {
        HideGateScreenArrow();
        HideGateWorldArrow();
    }

    public void SetAssistedMode(bool active)
    {
        assistedModeActive = active;
        isDragging = false;

        foreach (Behaviour b in disableInAssistedMode)
        {
            if (b != null)
                b.enabled = !active;
        }

        if (!active)
        {
            HideGateScreenArrow();
            HideGateWorldArrow();

            if (debugLogs)
                Debug.Log("[AssistedMapCameraController] Conventional mode. Gate arrows hidden.");

            return;
        }

        if (controlledCamera == null)
        {
            Debug.LogWarning("[AssistedMapCameraController] No camera assigned.");
            HideGateScreenArrow();
            HideGateWorldArrow();
            return;
        }

        controlledCamera.enabled = true;
        controlledCamera.orthographic = true;

        RecalculateMaxZoomFromMap();

        if (startAtGate)
            MoveCameraToGate();

        UpdateGateWorldArrow();
        UpdateGateScreenArrow();

        if (debugLogs)
            Debug.Log("[AssistedMapCameraController] Assisted mode ON.");
    }

    private void Update()
    {
        if (!assistedModeActive)
        {
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

        HandleZoom();
        HandleMouseDragPan();
        HandleKeyboardPan();
        ApplyDesiredCameraPosition();

        UpdateGateWorldArrow();
        UpdateGateScreenArrow();
    }

    private void LateUpdate()
    {
        if (!assistedModeActive)
        {
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

        ApplyDesiredCameraPosition();

        UpdateGateWorldArrow();
        UpdateGateScreenArrow();
    }

    private void RecalculateMaxZoomFromMap()
    {
        if (!autoCalculateMaxZoomFromMap)
            return;

        if (mapBoundsSprite == null || controlledCamera == null)
            return;

        Bounds b = mapBoundsSprite.bounds;

        float neededByHeight = b.size.y / 2f;
        float neededByWidth = b.size.x / (2f * controlledCamera.aspect);

        maxZoomSize = Mathf.Max(neededByHeight, neededByWidth);
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

    private void HandleZoom()
    {
        if (!enableZoom)
            return;

        float scroll = Input.mouseScrollDelta.y;

        if (Mathf.Abs(scroll) < 0.01f)
            return;

        controlledCamera.orthographicSize -= scroll * zoomSpeed;

        controlledCamera.orthographicSize = Mathf.Clamp(
            controlledCamera.orthographicSize,
            minZoomSize,
            maxZoomSize
        );
    }

    private void HandleMouseDragPan()
    {
        if (!enableMouseDragPan)
            return;

        bool dragPressed = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        bool dragHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);
        bool dragReleased = Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2);

        if (dragPressed)
        {
            lastMouseWorldPosition = controlledCamera.ScreenToWorldPoint(Input.mousePosition);
            isDragging = true;
        }

        if (dragReleased)
        {
            isDragging = false;
        }

        if (!isDragging || !dragHeld)
            return;

        Vector3 currentMouseWorldPosition = controlledCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector3 difference = lastMouseWorldPosition - currentMouseWorldPosition;

        MoveCameraBy(difference * mousePanSpeed);
    }

    private void HandleKeyboardPan()
    {
        if (!enableKeyboardPan)
            return;

        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            move.x -= 1f;

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            move.x += 1f;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            move.y += 1f;

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            move.y -= 1f;

        if (move.sqrMagnitude < 0.001f)
            return;

        MoveCameraBy(move.normalized * keyboardPanSpeed * Time.deltaTime);
    }

    private void MoveCameraBy(Vector3 movement)
    {
        if (!hasDesiredPosition)
        {
            desiredCameraPosition = controlledCamera.transform.position;
            hasDesiredPosition = true;
        }

        desiredCameraPosition += movement;
        desiredCameraPosition.z = controlledCamera.transform.position.z;

        ApplyDesiredCameraPosition();
    }

    private void ApplyDesiredCameraPosition()
    {
        if (controlledCamera == null)
            return;

        if (!hasDesiredPosition)
        {
            desiredCameraPosition = controlledCamera.transform.position;
            hasDesiredPosition = true;
        }

        Vector3 pos = desiredCameraPosition;

        if (useMapBoundsClamp && mapBoundsSprite != null)
            pos = ClampToMapBounds(pos);

        pos.z = controlledCamera.transform.position.z;

        desiredCameraPosition = pos;
        controlledCamera.transform.position = pos;
    }

    private Vector3 ClampToMapBounds(Vector3 position)
    {
        Bounds b = mapBoundsSprite.bounds;

        float halfHeight = controlledCamera.orthographicSize;
        float halfWidth = halfHeight * controlledCamera.aspect;

        float minX = b.min.x + halfWidth;
        float maxX = b.max.x - halfWidth;
        float minY = b.min.y + halfHeight;
        float maxY = b.max.y - halfHeight;

        if (minX <= maxX)
            position.x = Mathf.Clamp(position.x, minX, maxX);
        else
            position.x = b.center.x;

        if (minY <= maxY)
            position.y = Mathf.Clamp(position.y, minY, maxY);
        else
            position.y = b.center.y;

        return position;
    }

    private void UpdateGateWorldArrow()
    {
        if (gateWorldArrow == null)
            return;

        if (!assistedModeActive || destinationGate == null)
        {
            HideGateWorldArrow();
            return;
        }

        if (hideWorldArrowWhenGateOffScreen && controlledCamera != null)
        {
            Vector3 viewportPoint = controlledCamera.WorldToViewportPoint(destinationGate.position);

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

        Vector2 directionFromGate = gateWorldArrowDirectionFromGate;

        if (directionFromGate.sqrMagnitude < 0.001f)
            directionFromGate = Vector2.up;

        directionFromGate.Normalize();

        Vector3 arrowPosition = destinationGate.position + (Vector3)(directionFromGate * gateWorldArrowDistanceFromGate);
        arrowPosition.z = gateWorldArrow.position.z;

        gateWorldArrow.position = arrowPosition;

        Vector2 directionToGate = ((Vector2)destinationGate.position - (Vector2)gateWorldArrow.position);

        if (directionToGate.sqrMagnitude < 0.001f)
            directionToGate = Vector2.down;

        directionToGate.Normalize();

        float angle = Mathf.Atan2(directionToGate.y, directionToGate.x) * Mathf.Rad2Deg;

        gateWorldArrow.rotation = Quaternion.Euler(
            0f,
            0f,
            angle + gateWorldArrowRotationOffset
        );
    }

    private void UpdateGateScreenArrow()
    {
        if (gateScreenArrow == null)
            return;

        if (!assistedModeActive || controlledCamera == null || destinationGate == null)
        {
            HideGateScreenArrow();
            return;
        }

        Vector3 viewportPoint = controlledCamera.WorldToViewportPoint(destinationGate.position);

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

        Vector2 directionFromCenter = new Vector2(
            viewportPoint.x - 0.5f,
            viewportPoint.y - 0.5f
        );

        if (viewportPoint.z < 0f)
            directionFromCenter = -directionFromCenter;

        if (directionFromCenter.sqrMagnitude < 0.001f)
            directionFromCenter = Vector2.up;

        directionFromCenter.Normalize();

        Canvas canvas = gateScreenArrow.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;

        if (canvasRect == null)
            return;

        float halfWidth = canvasRect.rect.width * 0.5f - gateScreenArrowEdgeMargin;
        float halfHeight = canvasRect.rect.height * 0.5f - gateScreenArrowEdgeMargin;

        float scaleX = Mathf.Abs(directionFromCenter.x) > 0.001f
            ? halfWidth / Mathf.Abs(directionFromCenter.x)
            : Mathf.Infinity;

        float scaleY = Mathf.Abs(directionFromCenter.y) > 0.001f
            ? halfHeight / Mathf.Abs(directionFromCenter.y)
            : Mathf.Infinity;

        float scale = Mathf.Min(scaleX, scaleY);

        Vector2 localArrowPosition = directionFromCenter * scale;

        gateScreenArrow.anchoredPosition = localArrowPosition;

        float angle = Mathf.Atan2(directionFromCenter.y, directionFromCenter.x) * Mathf.Rad2Deg;

        gateScreenArrow.localRotation = Quaternion.Euler(
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