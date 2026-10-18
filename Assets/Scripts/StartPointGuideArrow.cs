using UnityEngine;
using TMPro;

/// <summary>
/// UI guide arrow for the Assisted-mode start point.
/// It points to the car, which has already been reset by ScenarioManager to
/// the current scenario's assigned start node. It is shown only while that
/// start point itself is visible on screen.
/// </summary>
public class StartPointGuideArrow : MonoBehaviour
{
    [Header("Camera / Mode")]
    [Tooltip("Camera used by the assisted map.")]
    public Camera assistedCamera;

    [Tooltip("Drag the object that has AssistedMapCameraController here.")]
    public AssistedMapCameraController assistedCameraController;

    [Tooltip("ScenarioManager is used so the arrow stays visible for the whole active Assisted round, but hides on intro/result screens.")]
    public ScenarioManager scenarioManager;

    [Header("References")]
    [Tooltip("Drag the Car Transform here.")]
    public Transform startTarget;

    [Tooltip("Drag the duplicated UI arrow RectTransform here.")]
    public RectTransform arrowUI;

    [Header("Optional Arrow Label")]
    [Tooltip("Optional TMP/RTLTMPro label shown near the start-point arrow.")]
    public TMP_Text arrowLabel;

    [Tooltip("Text shown beside/above the start-point arrow.")]
    public string arrowLabelText = "نقطه شروع";

    [Tooltip("Screen-pixel offset from the arrow. X=0,Y=55 places the label above it.")]
    public Vector2 arrowLabelOffsetPixels = new Vector2(0f, 55f);

    [Header("2D Projection Fix")]
    [Tooltip("For your 2D map, keep ON and use Z = 0 unless your map uses another projection plane.")]
    public bool forceTargetZForScreenProjection = true;

    public float targetScreenProjectionZ = 0f;

    [Header("Visibility")]
    [Tooltip("Master switch for the start-point arrow.")]
    public bool showArrow = true;

    [Tooltip("Keep OFF for the study: when the camera moves away from the start point, the start arrow disappears instead of following the screen edge.")]
    public bool showOnScreenEdgeWhenOffScreen = false;

    [Header("When Start Point Is Visible")]
    [Tooltip("Screen-pixel offset from the car. X=0,Y=90 places the arrow above the car.")]
    public Vector2 visibleTargetOffsetPixels = new Vector2(0f, 90f);

    [Tooltip("When ON, the arrow points directly downward to the car while the car is visible.")]
    public bool forceDownArrowWhenVisible = true;

    [Header("When Start Point Is Off-Screen")]
    [Tooltip("Distance from the screen edge.")]
    public float edgeMarginPixels = 90f;

    [Header("Rotation")]
    [Tooltip("For the same arrow sprite as your destination arrow, start with the same rotation offset used there.")]
    public float arrowRotationOffset = 0f;

    [Header("Debug")]
    public bool debugLogs = false;

    private Canvas parentCanvas;
    private RectTransform canvasRect;

    private void Awake()
    {
        if (assistedCamera == null)
            assistedCamera = Camera.main;

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

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

    private bool ShouldBeVisible()
    {
        if (!showArrow)
            return false;

        if (assistedCameraController == null)
            return false;

        if (!assistedCameraController.assistedModeActive)
            return false;

        // Show during the automatic orientation animation...
        if (assistedCameraController.IsAssistedOpeningActive)
            return true;

        // ...and keep showing throughout the participant's Assisted gameplay.
        // It hides again on scenario-intro, result, and final screens.
        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        return scenarioManager != null &&
               scenarioManager.IsScenarioGameplayActive;
    }

    private Vector3 GetTargetWorldPositionForScreenProjection()
    {
        Vector3 targetWorld = startTarget.position;

        if (forceTargetZForScreenProjection)
            targetWorld.z = targetScreenProjectionZ;

        return targetWorld;
    }

    private void UpdateArrow()
    {
        if (!ShouldBeVisible())
        {
            HideArrow();
            return;
        }

        if (assistedCamera == null ||
            startTarget == null ||
            arrowUI == null)
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

        Vector3 targetWorld =
            GetTargetWorldPositionForScreenProjection();

        Vector3 targetScreenPosition =
            assistedCamera.WorldToScreenPoint(targetWorld);

        bool targetIsInFrontOfCamera =
            targetScreenPosition.z > 0f;

        bool targetIsVisible =
            targetIsInFrontOfCamera &&
            targetScreenPosition.x >= 0f &&
            targetScreenPosition.x <= Screen.width &&
            targetScreenPosition.y >= 0f &&
            targetScreenPosition.y <= Screen.height;

        if (debugLogs)
        {
            Debug.Log(
                "[StartPointGuideArrow] targetWorld=" +
                targetWorld +
                " targetScreen=" +
                targetScreenPosition +
                " visible=" +
                targetIsVisible +
                " opening=" +
                assistedCameraController.IsAssistedOpeningActive
            );
        }

        if (targetIsVisible)
        {
            PlaceArrowNearVisibleTarget(
                targetScreenPosition
            );
        }
        else if (showOnScreenEdgeWhenOffScreen)
        {
            PlaceArrowOnScreenEdge(
                targetScreenPosition
            );
        }
        else
        {
            HideArrow();
        }
    }

    private void PlaceArrowNearVisibleTarget(
        Vector3 targetScreenPosition)
    {
        arrowUI.gameObject.SetActive(true);

        Vector2 targetScreen = new Vector2(
            targetScreenPosition.x,
            targetScreenPosition.y
        );

        Vector2 arrowScreen =
            targetScreen + visibleTargetOffsetPixels;

        arrowScreen.x = Mathf.Clamp(
            arrowScreen.x,
            edgeMarginPixels,
            Screen.width - edgeMarginPixels
        );

        arrowScreen.y = Mathf.Clamp(
            arrowScreen.y,
            edgeMarginPixels,
            Screen.height - edgeMarginPixels
        );

        SetArrowScreenPosition(arrowScreen);
        UpdateArrowLabel(arrowScreen);

        if (forceDownArrowWhenVisible)
        {
            RotateArrow(Vector2.down);
        }
        else
        {
            Vector2 directionToTarget =
                targetScreen - arrowScreen;

            RotateArrow(directionToTarget);
        }
    }

    private void PlaceArrowOnScreenEdge(
        Vector3 targetScreenPosition)
    {
        arrowUI.gameObject.SetActive(true);

        Vector2 screenCenter = new Vector2(
            Screen.width * 0.5f,
            Screen.height * 0.5f
        );

        Vector2 directionToTarget = new Vector2(
            targetScreenPosition.x - screenCenter.x,
            targetScreenPosition.y - screenCenter.y
        );

        if (targetScreenPosition.z < 0f)
            directionToTarget = -directionToTarget;

        if (directionToTarget.sqrMagnitude < 0.001f)
            directionToTarget = Vector2.up;

        directionToTarget.Normalize();

        float halfWidth =
            Screen.width * 0.5f - edgeMarginPixels;

        float halfHeight =
            Screen.height * 0.5f - edgeMarginPixels;

        float scaleX =
            Mathf.Abs(directionToTarget.x) > 0.001f
                ? halfWidth / Mathf.Abs(directionToTarget.x)
                : Mathf.Infinity;

        float scaleY =
            Mathf.Abs(directionToTarget.y) > 0.001f
                ? halfHeight / Mathf.Abs(directionToTarget.y)
                : Mathf.Infinity;

        float scale = Mathf.Min(scaleX, scaleY);

        Vector2 arrowScreen =
            screenCenter + directionToTarget * scale;

        SetArrowScreenPosition(arrowScreen);
        UpdateArrowLabel(arrowScreen);
        RotateArrow(directionToTarget);
    }

    private void SetArrowScreenPosition(
        Vector2 screenPosition)
    {
        if (arrowUI == null)
            return;

        if (parentCanvas == null ||
            canvasRect == null)
        {
            arrowUI.position = screenPosition;
            return;
        }

        Camera uiCamera = null;

        if (parentCanvas.renderMode !=
            RenderMode.ScreenSpaceOverlay)
        {
            uiCamera = parentCanvas.worldCamera;
        }

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

        float angle =
            Mathf.Atan2(
                direction.y,
                direction.x
            ) * Mathf.Rad2Deg;

        arrowUI.localRotation =
            Quaternion.Euler(
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
