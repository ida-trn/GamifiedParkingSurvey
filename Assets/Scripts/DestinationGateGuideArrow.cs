using UnityEngine;
using TMPro;

public class DestinationGateGuideArrow : MonoBehaviour
{
    [Header("Cameras")]
    public Camera conventionalCamera;
    public Camera assistedCamera;

    [Tooltip("Drag the object that has AssistedMapCameraController here.")]
    public AssistedMapCameraController assistedCameraController;

    [Header("References")]
    [Tooltip("Drag Destination_Gate2 here.")]
    public Transform destinationGate;

    [Tooltip("Drag the UI arrow Image RectTransform here.")]
    public RectTransform arrowUI;

    [Header("Optional Arrow Label")]
    [Tooltip("Optional TMP/RTLTMPro label shown near the destination arrow.")]
    public TMP_Text arrowLabel;

    [Tooltip("Text shown beside/above the destination arrow.")]
    public string arrowLabelText = "مقصد";

    [Tooltip("Screen-pixel offset from the arrow. X=0,Y=55 places the label above it.")]
    public Vector2 arrowLabelOffsetPixels = new Vector2(0f, 55f);

    [Header("2D Projection Fix")]
    [Tooltip("For 2D maps, force the gate's Z to this value before converting to screen position. Use 0 for your map.")]
    public bool forceGateZForScreenProjection = true;
    public float gateScreenProjectionZ = 0f;

    [Header("Visibility")]
    public bool showArrow = true;

    [Tooltip("Keep OFF if you want the arrow to show even when the gate is visible.")]
    public bool hideWhenGateVisible = false;

    [Header("When Gate Is Visible")]
    [Tooltip("Arrow position relative to the gate in screen pixels. X=0, Y=90 places it above the gate.")]
    public Vector2 visibleGateOffsetPixels = new Vector2(0f, 90f);

    [Tooltip("When ON, the arrow points directly downward to the gate whenever the gate is visible.")]
    public bool forceDownArrowWhenGateVisible = true;

    [Header("When Gate Is Off-Screen")]
    [Tooltip("Distance from the screen edge when the gate is not visible.")]
    public float edgeMarginPixels = 90f;

    [Header("Rotation")]
    [Tooltip("Use 180 if your arrow currently points the correct way. If it flips, try 0, 90, or -90.")]
    public float arrowRotationOffset = 180f;

    [Header("Debug")]
    public bool debugLogs = false;

    private Canvas parentCanvas;
    private RectTransform canvasRect;

    private void Awake()
    {
        if (conventionalCamera == null)
            conventionalCamera = Camera.main;

        CacheCanvas();
        HideArrow();
    }

    private void OnEnable()
    {
        CacheCanvas();
        UpdateArrow();
    }

    private void Update()
    {
        UpdateArrow();
    }

    private void LateUpdate()
    {
        UpdateArrow();
    }

    private void CacheCanvas()
    {
        if (arrowUI == null)
            return;

        parentCanvas = arrowUI.GetComponentInParent<Canvas>();

        if (parentCanvas != null)
            canvasRect = parentCanvas.GetComponent<RectTransform>();
    }

    private Camera GetCurrentCamera()
    {
        bool assistedIsActive =
            assistedCameraController != null &&
            assistedCameraController.assistedModeActive;

        if (assistedIsActive && assistedCamera != null)
            return assistedCamera;

        if (conventionalCamera != null)
            return conventionalCamera;

        return Camera.main;
    }

    private Vector3 GetGateWorldPositionForScreenProjection()
    {
        Vector3 gateWorld = destinationGate.position;

        if (forceGateZForScreenProjection)
            gateWorld.z = gateScreenProjectionZ;

        return gateWorld;
    }

    private void UpdateArrow()
    {
        if (!showArrow)
        {
            HideArrow();
            return;
        }

        Camera currentCamera = GetCurrentCamera();

        if (currentCamera == null || destinationGate == null || arrowUI == null)
        {
            HideArrow();
            return;
        }

        if (canvasRect == null)
            CacheCanvas();

        if (canvasRect == null)
        {
            HideArrow();
            return;
        }

        Vector3 gateWorld = GetGateWorldPositionForScreenProjection();
        Vector3 gateScreenPosition = currentCamera.WorldToScreenPoint(gateWorld);

        bool gateIsInFrontOfCamera = gateScreenPosition.z > 0f;

        bool gateIsVisible =
            gateIsInFrontOfCamera &&
            gateScreenPosition.x >= 0f &&
            gateScreenPosition.x <= Screen.width &&
            gateScreenPosition.y >= 0f &&
            gateScreenPosition.y <= Screen.height;

        if (debugLogs)
        {
            Debug.Log(
                "[DestinationGateGuideArrow] gateWorld=" + gateWorld +
                " gateScreen=" + gateScreenPosition +
                " visible=" + gateIsVisible
            );
        }

        if (gateIsVisible)
        {
            if (hideWhenGateVisible)
            {
                HideArrow();
                return;
            }

            PlaceArrowNearVisibleGate(gateScreenPosition);
        }
        else
        {
            PlaceArrowOnScreenEdge(gateScreenPosition);
        }
    }

    private void PlaceArrowNearVisibleGate(Vector3 gateScreenPosition)
    {
        arrowUI.gameObject.SetActive(true);

        Vector2 gateScreen = new Vector2(gateScreenPosition.x, gateScreenPosition.y);
        Vector2 arrowScreen = gateScreen + visibleGateOffsetPixels;

        arrowScreen.x = Mathf.Clamp(arrowScreen.x, edgeMarginPixels, Screen.width - edgeMarginPixels);
        arrowScreen.y = Mathf.Clamp(arrowScreen.y, edgeMarginPixels, Screen.height - edgeMarginPixels);

        SetArrowScreenPosition(arrowScreen);
        UpdateArrowLabel(arrowScreen);

        if (forceDownArrowWhenGateVisible)
        {
            RotateArrow(Vector2.down);
        }
        else
        {
            Vector2 directionToGate = gateScreen - arrowScreen;
            RotateArrow(directionToGate);
        }
    }

    private void PlaceArrowOnScreenEdge(Vector3 gateScreenPosition)
    {
        arrowUI.gameObject.SetActive(true);

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Vector2 directionToGate = new Vector2(
            gateScreenPosition.x - screenCenter.x,
            gateScreenPosition.y - screenCenter.y
        );

        if (gateScreenPosition.z < 0f)
            directionToGate = -directionToGate;

        if (directionToGate.sqrMagnitude < 0.001f)
            directionToGate = Vector2.up;

        directionToGate.Normalize();

        float halfWidth = Screen.width * 0.5f - edgeMarginPixels;
        float halfHeight = Screen.height * 0.5f - edgeMarginPixels;

        float scaleX = Mathf.Abs(directionToGate.x) > 0.001f
            ? halfWidth / Mathf.Abs(directionToGate.x)
            : Mathf.Infinity;

        float scaleY = Mathf.Abs(directionToGate.y) > 0.001f
            ? halfHeight / Mathf.Abs(directionToGate.y)
            : Mathf.Infinity;

        float scale = Mathf.Min(scaleX, scaleY);

        Vector2 arrowScreen = screenCenter + directionToGate * scale;

        SetArrowScreenPosition(arrowScreen);
        UpdateArrowLabel(arrowScreen);
        RotateArrow(directionToGate);
    }

    private void SetArrowScreenPosition(Vector2 screenPosition)
    {
        if (arrowUI == null)
            return;

        if (parentCanvas == null || canvasRect == null)
        {
            arrowUI.position = screenPosition;
            return;
        }

        Camera uiCamera = null;

        if (parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = parentCanvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPosition,
            uiCamera,
            out Vector2 localPoint
        );

        arrowUI.anchoredPosition = localPoint;
    }

    private void UpdateArrowLabel(Vector2 arrowScreenPosition)
    {
        if (arrowLabel == null)
            return;

        arrowLabel.text = arrowLabelText ?? string.Empty;
        arrowLabel.gameObject.SetActive(true);

        RectTransform labelRect = arrowLabel.rectTransform;
        RectTransform labelParent = labelRect.parent as RectTransform;

        Vector2 labelScreenPosition =
            arrowScreenPosition + arrowLabelOffsetPixels;

        if (labelParent == null)
        {
            labelRect.position = labelScreenPosition;
        }
        else
        {
            Camera uiCamera = null;

            if (parentCanvas != null &&
                parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                uiCamera = parentCanvas.worldCamera;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                labelParent,
                labelScreenPosition,
                uiCamera,
                out Vector2 localPoint
            );

            labelRect.anchoredPosition = localPoint;
        }

        // Keep Persian text upright even while the arrow rotates.
        labelRect.rotation = Quaternion.identity;
    }

    private void RotateArrow(Vector2 direction)
    {
        if (arrowUI == null)
            return;

        if (direction.sqrMagnitude < 0.001f)
            return;

        direction.Normalize();

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        arrowUI.localRotation = Quaternion.Euler(
            0f,
            0f,
            angle + arrowRotationOffset
        );
    }

    private void HideArrow()
    {
        if (arrowUI != null)
            arrowUI.gameObject.SetActive(false);

        if (arrowLabel != null)
            arrowLabel.gameObject.SetActive(false);
    }
}