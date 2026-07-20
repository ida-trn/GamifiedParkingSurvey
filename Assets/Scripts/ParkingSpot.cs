using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using RTLTMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class ParkingSpot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IPointerDownHandler
{
    [Header("Identity")]
    public string spotId;

    [Tooltip("If true, Spot ID and GameObject name update automatically when street/side/region/type/number changes.")]
    public bool autoUpdateSpotIdInInspector = true;

    [Header("Street Location Data")]
    [InspectorName("Street Name")]
    public StreetNameOption streetNameChoice = StreetNameOption.ShahidMalekloo;

    [InspectorName("Street Code")]
    public StreetCodeOption streetCodeChoice = StreetCodeOption.MAL;

    [Tooltip("If true, Street Code automatically follows Street Name.")]
    public bool autoSyncCodeWithStreetName = true;

    [HideInInspector] public string streetCode = "MAL";
    [HideInInspector] public string streetName = "Shahid Malekloo";

    public StreetSide streetSide = StreetSide.Unknown;

    [Tooltip("Will be assigned manually or automatically by ParkingSpotIdAssigner.")]
    public int spotNumber = 0;

    [Header("Choice Alternative Data")]
    public RegionType region = RegionType.Inner;

    [Tooltip("NormalParkingSpot = normal selectable parking. NoParkingZone = forbidden area, but still selectable/parkable in the survey.")]
    public ParkingAreaKind parkingAreaKind = ParkingAreaKind.NormalParkingSpot;

    public ParkingType parkingType = ParkingType.Legal;

    [Header("Illegal Parking Details")]
    [Tooltip("Use None only for legal parking. For illegal parking, choose the reason.")]
    public IllegalReason illegalReason = IllegalReason.None;

    [Header("Legal Parking Details")]
    [Tooltip("Use None for normal legal parking. Choose a feature only for special legal spaces such as smart parking or disabled parking.")]
    public LegalParkingFeature legalParkingFeature = LegalParkingFeature.None;

    [TextArea]
    public string notes;

    [Header("Persian Parking Label")]
    public bool showPersianReasonLabel = true;

    [Tooltip("If true, the recommended label settings are applied automatically to every parking spot.")]
    public bool autoApplyRecommendedLabelSettings = true;

    [Tooltip("Assign a Persian-supporting TMP Font Asset here.")]
    public TMP_FontAsset persianFontAsset;

    [Tooltip("Canvas created automatically for the Persian label.")]
    public Canvas reasonLabelCanvas;

    [Tooltip("Text created automatically inside the canvas.")]
    public RTLTextMeshPro reasonLabel;

    public Vector3 reasonLabelLocalOffset = new Vector3(0f, 0f, -1f);

    [Tooltip("Width and height of the text canvas. Increase X/Y if the wrapped text is too narrow.")]
    public Vector2 reasonLabelCanvasSize = new Vector2(120f, 90f);

    public float reasonLabelCanvasScale = 0.01f;

    [Tooltip("Font size used when Auto Size is disabled.")]
    public float reasonLabelFontSize = 30f;

    [Header("Persian Label Auto Size")]
    [Tooltip("Uses the same Auto Size behavior shown in the RTL Text Mesh Pro Inspector.")]
    public bool reasonLabelAutoSize = true;

    [Min(1f)]
    [Tooltip("Smallest font size Auto Size may use.")]
    public float reasonLabelAutoSizeMin = 20f;

    [Min(1f)]
    [Tooltip("Largest font size Auto Size may use.")]
    public float reasonLabelAutoSizeMax = 200f;

    [Header("Keep Label Inside Parking Spot")]
    [Tooltip("The text Canvas may be smaller than the spot, but it can never become wider or taller than the parking sprite.")]
    public bool constrainReasonLabelToSprite = true;

    [Range(0.5f, 1f)]
    [Tooltip("1 = use the full sprite bounds. A value such as 0.94 leaves a small safe margin inside the parking spot.")]
    public float reasonLabelSpriteBoundsPadding = 0.94f;

    public Color reasonLabelColor = Color.black;

    [Header("Persian Label Outline")]
    public Color reasonLabelOutlineColor = Color.black;

    [Range(0f, 1f)]
    public float reasonLabelOutlineThickness = 0.307f;

    public int reasonLabelSortingOrder = 500;

    [Tooltip("Rotation of the Persian label relative to the parking spot. 270 means -90 degrees.")]
    public float reasonLabelRotationZ = 270f;

    [Header("Road Network Link")]
    public RoadNode nearestRoadNode;

    [Header("Parking Approach Segment")]
    [Tooltip("The node before this parking row/block face, in the driving direction.")]
    public RoadNode parkingSegmentStartNode;

    [Tooltip("The node after this parking row/block face, in the driving direction.")]
    public RoadNode parkingSegmentEndNode;

    [Tooltip("Optional offset from road centerline toward the parking spot. Usually 0 to 5.")]
    public float parkingStopOffset = 0f;

    [Header("Walking Data")]
    public float walkingDistanceMeters;
    public float walkingTimeSeconds;

    [Header("State")]
    public bool isOccupied = false;
    public bool occupiedByPlayer = false;

    [Header("Colors")]
    public Color legalFreeColor = Color.green;
    public Color illegalFreeColor = Color.red;

    [Tooltip("Color for legal parking spots that are reserved for disabled people / ویژه معلولین.")]
    public Color disabledParkingFreeColor = new Color32(52, 52, 255, 255);

    [Tooltip("Color for parkable no-parking zones. They are still illegal, but visually separated from normal illegal spots.")]
    public Color noParkingZoneFreeColor = new Color(1f, 0.45f, 0f);

    public Color occupiedColor = new Color(0.7f, 0.7f, 0.7f);
    public Color hoverColor = Color.yellow;
    public Color parkedByPlayerColor = Color.blue;

    private SpriteRenderer sr;
    private bool isHighlighted = false;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        UpdateStreetCodeAndName();

        if (string.IsNullOrWhiteSpace(spotId) || spotId == "0")
        {
            GenerateSpotId();
        }

        ValidateParkingLogic();
        UpdateVisualOnly();
        ScheduleReasonLabelRefresh();
    }

    private void OnEnable()
    {
        sr = GetComponent<SpriteRenderer>();
        ScheduleReasonLabelRefresh();
    }

    private void Start()
    {
        RefreshReasonLabel();
    }

    private void OnValidate()
    {
        sr = GetComponent<SpriteRenderer>();

        UpdateStreetCodeAndName();
        ValidateParkingLogic();

        if (autoUpdateSpotIdInInspector)
        {
            GenerateSpotId();
        }

        UpdateVisualOnly();
        ScheduleReasonLabelRefresh();
    }

    private void ValidateParkingLogic()
    {
        if (parkingAreaKind == ParkingAreaKind.NoParkingZone)
        {
            parkingType = ParkingType.Illegal;
        }

        if (parkingType == ParkingType.Legal)
        {
            illegalReason = IllegalReason.None;
        }
        else
        {
            legalParkingFeature = LegalParkingFeature.None;
        }
    }

    private void ApplyAutomaticLabelDefaults()
    {
        if (!autoApplyRecommendedLabelSettings)
            return;

        reasonLabelLocalOffset = new Vector3(0f, 0f, -1f);
        reasonLabelCanvasSize = new Vector2(120f, 90f);
        reasonLabelCanvasScale = 0.01f;
        reasonLabelFontSize = 30f;
        reasonLabelAutoSize = true;
        reasonLabelAutoSizeMin = 18f;
        reasonLabelAutoSizeMax = 200f;
        constrainReasonLabelToSprite = true;
        reasonLabelSpriteBoundsPadding = 0.94f;
        reasonLabelColor = Color.black;
        reasonLabelOutlineColor = Color.black;
        reasonLabelOutlineThickness = 0.307f;
        reasonLabelSortingOrder = 500;
        reasonLabelRotationZ = 270f;
    }

    public void UpdateStreetCodeAndName()
    {
        if (autoSyncCodeWithStreetName)
        {
            streetCodeChoice = GetStreetCodeChoiceFromName(streetNameChoice);
        }

        streetCode = GetStreetCodeText(streetCodeChoice);
        streetName = GetStreetNameText(streetNameChoice);
    }

    private StreetCodeOption GetStreetCodeChoiceFromName(StreetNameOption name)
    {
        switch (name)
        {
            case StreetNameOption.Heydarkhani: return StreetCodeOption.HDR;
            case StreetNameOption.ShahidMalekloo: return StreetCodeOption.MAL;
            case StreetNameOption.ShahidNilforooshan: return StreetCodeOption.NIL;
            case StreetNameOption.ElmOSanat: return StreetCodeOption.ELM;
            case StreetNameOption.Abgoon41: return StreetCodeOption.ABG41;
            case StreetNameOption.Zolfaghari: return StreetCodeOption.ZOL;
            case StreetNameOption.Lotfizadeh: return StreetCodeOption.LOT;
            case StreetNameOption.Saadatmand: return StreetCodeOption.SAA;
            case StreetNameOption.Layeghnia: return StreetCodeOption.LAY;
            case StreetNameOption.Mehran: return StreetCodeOption.MEH;
            case StreetNameOption.Bagheri: return StreetCodeOption.BAG;
            case StreetNameOption.Salehi: return StreetCodeOption.SAL;
            case StreetNameOption.Attari42: return StreetCodeOption.ATT42;
            case StreetNameOption.Ebadi: return StreetCodeOption.EBA;
            case StreetNameOption.Vahdat: return StreetCodeOption.VAH;
            case StreetNameOption.Shohada: return StreetCodeOption.SHO;
            default: return StreetCodeOption.MAL;
        }
    }

    private string GetStreetCodeText(StreetCodeOption code)
    {
        switch (code)
        {
            case StreetCodeOption.HDR: return "HDR";
            case StreetCodeOption.MAL: return "MAL";
            case StreetCodeOption.NIL: return "NIL";
            case StreetCodeOption.ELM: return "ELM";
            case StreetCodeOption.ABG41: return "ABG41";
            case StreetCodeOption.ZOL: return "ZOL";
            case StreetCodeOption.LOT: return "LOT";
            case StreetCodeOption.SAA: return "SAA";
            case StreetCodeOption.LAY: return "LAY";
            case StreetCodeOption.MEH: return "MEH";
            case StreetCodeOption.BAG: return "BAG";
            case StreetCodeOption.SAL: return "SAL";
            case StreetCodeOption.ATT42: return "ATT42";
            case StreetCodeOption.EBA: return "EBA";
            case StreetCodeOption.VAH: return "VAH";
            case StreetCodeOption.SHO: return "SHO";
            default: return "UNK";
        }
    }

    private string GetStreetNameText(StreetNameOption name)
    {
        switch (name)
        {
            case StreetNameOption.Heydarkhani: return "Heydarkhani";
            case StreetNameOption.ShahidMalekloo: return "Shahid Malekloo";
            case StreetNameOption.ShahidNilforooshan: return "Shahid Nilforooshan";
            case StreetNameOption.ElmOSanat: return "Elm-o-Sanat";
            case StreetNameOption.Abgoon41: return "Abgoon 41";
            case StreetNameOption.Zolfaghari: return "Zolfaghari";
            case StreetNameOption.Lotfizadeh: return "Lotfizadeh";
            case StreetNameOption.Saadatmand: return "Saadatmand";
            case StreetNameOption.Layeghnia: return "Layeghnia";
            case StreetNameOption.Mehran: return "Mehran";
            case StreetNameOption.Bagheri: return "Bagheri";
            case StreetNameOption.Salehi: return "Salehi";
            case StreetNameOption.Attari42: return "Attari 42";
            case StreetNameOption.Ebadi: return "Ebadi";
            case StreetNameOption.Vahdat: return "Vahdat";
            case StreetNameOption.Shohada: return "Shohada";
            default: return "Unknown";
        }
    }

    public void GenerateSpotId()
    {
        UpdateStreetCodeAndName();

        string sideCode = GetStreetSideCode();
        string altId = GetAlternativeId();

        if (spotNumber <= 0)
        {
            spotNumber = 1;
        }

        spotId = $"{streetCode}_{sideCode}_{altId}_{spotNumber:000}";
        gameObject.name = spotId;
    }

    public string GetStreetSideCode()
    {
        switch (streetSide)
        {
            case StreetSide.North: return "N";
            case StreetSide.South: return "S";
            case StreetSide.East: return "E";
            case StreetSide.West: return "W";
            default: return "U";
        }
    }

    public string GetStreetSideName()
    {
        return streetSide.ToString();
    }

    public string GetParkingAreaKindName()
    {
        return parkingAreaKind.ToString();
    }

    public string GetParkingTypeName()
    {
        return parkingType.ToString();
    }

    public string GetIllegalReasonName()
    {
        return illegalReason.ToString();
    }

    public string GetLegalParkingFeatureName()
    {
        return legalParkingFeature.ToString();
    }

    public string GetAlternativeId()
    {
        if (region == RegionType.Inner && parkingType == ParkingType.Legal)
            return "A1";

        if (region == RegionType.Inner && parkingType == ParkingType.Illegal)
            return "A2";

        if (region == RegionType.Outer && parkingType == ParkingType.Legal)
            return "A3";

        if (region == RegionType.Outer && parkingType == ParkingType.Illegal)
            return "A4";

        return "FAR_NOT_MODELED";
    }

    public string GetIllegalReasonPersianText()
    {
        switch (illegalReason)
        {
            case IllegalReason.DrivewayEntrance:
                return "درب پارکینگ";

            case IllegalReason.NoParkingSign:
                return "پارک ممنوع";

            case IllegalReason.NoStoppingSign:
                return "توقف ممنوع";

            case IllegalReason.IntersectionCorner:
                return "نزدیک تقاطع";

            case IllegalReason.PedestrianCrossing:
                return "خط عابر پیاده";

            case IllegalReason.BusStop:
                return "ایستگاه اتوبوس";

            case IllegalReason.Other:
                return "پارک غیرمجاز";

            case IllegalReason.None:
            default:
                return "";
        }
    }

    public string GetLegalParkingFeaturePersianText()
    {
        switch (legalParkingFeature)
        {
            case LegalParkingFeature.SmartParking:
                return "پارکینگ هوشمند";

            case LegalParkingFeature.DisabledParking:
                return "ویژه معلولین";

            case LegalParkingFeature.Other:
                return "پارکینگ ویژه";

            case LegalParkingFeature.None:
            default:
                return "";
        }
    }

    public string GetParkingLabelPersianText()
    {
        if (parkingType == ParkingType.Illegal)
        {
            return GetIllegalReasonPersianText();
        }

        if (parkingType == ParkingType.Legal)
        {
            return GetLegalParkingFeaturePersianText();
        }

        return "";
    }

    private bool ShouldShowReasonLabel()
    {
        if (!showPersianReasonLabel)
            return false;

        if (parkingType == ParkingType.Illegal)
            return illegalReason != IllegalReason.None;

        if (parkingType == ParkingType.Legal)
            return legalParkingFeature != LegalParkingFeature.None;

        return false;
    }

    private void ScheduleReasonLabelRefresh()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorApplication.delayCall -= DelayedRefreshReasonLabel;
            EditorApplication.delayCall += DelayedRefreshReasonLabel;
            return;
        }
#endif

        RefreshReasonLabel();
    }

#if UNITY_EDITOR
    private void DelayedRefreshReasonLabel()
    {
        EditorApplication.delayCall -= DelayedRefreshReasonLabel;

        if (this == null)
            return;

        RefreshReasonLabel();
    }
#endif

    private void DestroyObjectSafe(GameObject obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    private void CleanupReasonLabels()
    {
        Canvas canvasToKeep = null;
        RTLTextMeshPro labelToKeep = null;

        Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
        System.Collections.Generic.List<GameObject> objectsToDelete =
            new System.Collections.Generic.List<GameObject>();

        // Prefer the original hierarchy: IllegalReasonCanvas directly under ParkingSpot.
        foreach (Transform item in allTransforms)
        {
            if (item == null || item == transform)
                continue;

            if (item.name != "IllegalReasonCanvas")
                continue;

            Canvas canvas = item.GetComponent<Canvas>();

            if (canvas == null)
            {
                objectsToDelete.Add(item.gameObject);
                continue;
            }

            if (canvasToKeep == null && item.parent == transform)
            {
                canvasToKeep = canvas;
                continue;
            }

            if (canvasToKeep == null)
            {
                canvasToKeep = canvas;
                continue;
            }

            objectsToDelete.Add(item.gameObject);
        }

        // Do not re-parent a Transform inside a Prefab instance.
        // A Canvas under an obsolete root is deleted and a fresh direct-child
        // Canvas is created instead. This avoids Unity's prefab re-parenting error.
        if (canvasToKeep != null && canvasToKeep.transform.parent != transform)
        {
            objectsToDelete.Add(canvasToKeep.gameObject);
            canvasToKeep = null;
            labelToKeep = null;
        }

        // Remove obsolete roots from previous versions.
        allTransforms = GetComponentsInChildren<Transform>(true);

        foreach (Transform item in allTransforms)
        {
            if (item == null || item == transform)
                continue;

            if (item.name == "ReasonLabelScaleRoot" ||
                item.name == "IllegalReasonScaleRoot" ||
                item.name == "IllegalReasonIconRoot")
            {
                objectsToDelete.Add(item.gameObject);
            }
        }

        // Keep only one RTL label inside the surviving canvas.
        if (canvasToKeep != null)
        {
            RTLTextMeshPro[] labels =
                canvasToKeep.GetComponentsInChildren<RTLTextMeshPro>(true);

            foreach (RTLTextMeshPro label in labels)
            {
                if (label == null)
                    continue;

                if (labelToKeep == null)
                    labelToKeep = label;
                else
                    objectsToDelete.Add(label.gameObject);
            }
        }

        // Remove loose labels not belonging to the surviving canvas.
        allTransforms = GetComponentsInChildren<Transform>(true);

        foreach (Transform item in allTransforms)
        {
            if (item == null || item == transform)
                continue;

            if (item.name != "IllegalReasonLabel")
                continue;

            if (labelToKeep == null || item.gameObject != labelToKeep.gameObject)
                objectsToDelete.Add(item.gameObject);
        }

        // Delete only topmost scheduled objects to avoid destroying twice.
        System.Collections.Generic.HashSet<GameObject> scheduled =
            new System.Collections.Generic.HashSet<GameObject>(objectsToDelete);

        foreach (GameObject obj in objectsToDelete)
        {
            if (obj == null)
                continue;

            bool parentAlsoScheduled = false;
            Transform parent = obj.transform.parent;

            while (parent != null && parent != transform)
            {
                if (scheduled.Contains(parent.gameObject))
                {
                    parentAlsoScheduled = true;
                    break;
                }

                parent = parent.parent;
            }

            if (!parentAlsoScheduled)
                DestroyObjectSafe(obj);
        }

        reasonLabelCanvas = canvasToKeep;
        reasonLabel = labelToKeep;
    }

    private Vector2 GetParkingSpriteLocalSize()
    {
        if (sr == null)
            sr = GetComponent<SpriteRenderer>();

        if (sr == null)
            return Vector2.one;

        if (sr.drawMode != SpriteDrawMode.Simple)
            return sr.size;

        if (sr.sprite != null)
            return sr.sprite.bounds.size;

        return Vector2.one;
    }

    private bool IsReasonLabelQuarterTurn()
    {
        float angle = Mathf.Repeat(reasonLabelRotationZ, 360f);

        return Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < 0.01f ||
               Mathf.Abs(Mathf.DeltaAngle(angle, 270f)) < 0.01f;
    }

    private Vector2 GetMaximumCanvasSizeInsideSprite()
    {
        Vector2 spriteSize = GetParkingSpriteLocalSize();
        float padding = Mathf.Clamp(reasonLabelSpriteBoundsPadding, 0.5f, 1f);

        // Account for an optional X/Y offset so the Canvas still remains
        // completely inside the sprite after being moved.
        float availableSpriteX = Mathf.Max(
            0.001f,
            spriteSize.x * padding - 2f * Mathf.Abs(reasonLabelLocalOffset.x)
        );

        float availableSpriteY = Mathf.Max(
            0.001f,
            spriteSize.y * padding - 2f * Mathf.Abs(reasonLabelLocalOffset.y)
        );

        float canvasScaleX = Mathf.Abs(reasonLabelCanvasScale) > 0.000001f
            ? Mathf.Abs(reasonLabelCanvasScale)
            : 1f;

        float canvasScaleY = canvasScaleX;

        // When the Canvas is rotated 90° or 270°, its local width lies along
        // the sprite's local height and its local height lies along the
        // sprite's local width.
        if (IsReasonLabelQuarterTurn())
        {
            return new Vector2(
                availableSpriteY / canvasScaleX,
                availableSpriteX / canvasScaleY
            );
        }

        return new Vector2(
            availableSpriteX / canvasScaleX,
            availableSpriteY / canvasScaleY
        );
    }

    private Vector2 GetConstrainedReasonLabelCanvasSize()
    {
        Vector2 requested = new Vector2(
            Mathf.Max(0.001f, Mathf.Abs(reasonLabelCanvasSize.x)),
            Mathf.Max(0.001f, Mathf.Abs(reasonLabelCanvasSize.y))
        );

        if (!constrainReasonLabelToSprite)
            return requested;

        Vector2 maximum = GetMaximumCanvasSizeInsideSprite();

        return new Vector2(
            Mathf.Min(requested.x, maximum.x),
            Mathf.Min(requested.y, maximum.y)
        );
    }

    private void ApplyConstrainedReasonLabelCanvasSize()
    {
        if (reasonLabelCanvas == null)
            return;

        RectTransform canvasRect =
            reasonLabelCanvas.GetComponent<RectTransform>();

        if (canvasRect != null)
            canvasRect.sizeDelta = GetConstrainedReasonLabelCanvasSize();
    }

    private void CreateReasonLabel()
    {
        GameObject canvasObject = new GameObject("IllegalReasonCanvas");
        canvasObject.layer = gameObject.layer;
        canvasObject.transform.SetParent(transform, false);
        canvasObject.transform.localPosition = reasonLabelLocalOffset;
        canvasObject.transform.localRotation = Quaternion.Euler(0f, 0f, reasonLabelRotationZ);
        canvasObject.transform.localScale = Vector3.one * reasonLabelCanvasScale;

        RectTransform canvasRect = canvasObject.AddComponent<RectTransform>();
        canvasRect.sizeDelta = GetConstrainedReasonLabelCanvasSize();

        reasonLabelCanvas = canvasObject.AddComponent<Canvas>();
        reasonLabelCanvas.renderMode = RenderMode.WorldSpace;
        reasonLabelCanvas.overrideSorting = true;
        reasonLabelCanvas.sortingOrder = reasonLabelSortingOrder;

        if (sr != null)
        {
            reasonLabelCanvas.sortingLayerID = sr.sortingLayerID;
        }

        GameObject textObject = new GameObject("IllegalReasonLabel");
        textObject.layer = gameObject.layer;
        textObject.transform.SetParent(canvasObject.transform, false);
        textObject.transform.localRotation = Quaternion.identity;
        textObject.transform.localScale = Vector3.one;

        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        reasonLabel = textObject.AddComponent<RTLTextMeshPro>();
    }

    private void RefreshReasonLabel()
    {
        ApplyAutomaticLabelDefaults();

        CleanupReasonLabels();

        if (!ShouldShowReasonLabel())
        {
            if (reasonLabelCanvas != null)
                reasonLabelCanvas.gameObject.SetActive(false);

            return;
        }

        if (reasonLabelCanvas == null || reasonLabel == null)
        {
            CreateReasonLabel();
        }

        ApplyReasonLabelSettings();
    }

    private void ApplyReasonLabelSettings()
    {
        if (reasonLabelCanvas == null || reasonLabel == null)
            return;

        reasonLabelCanvas.gameObject.SetActive(true);

        reasonLabelCanvas.transform.localPosition = reasonLabelLocalOffset;
        reasonLabelCanvas.transform.localRotation = Quaternion.Euler(0f, 0f, reasonLabelRotationZ);
        reasonLabelCanvas.transform.localScale = Vector3.one * reasonLabelCanvasScale;
        reasonLabelCanvas.renderMode = RenderMode.WorldSpace;
        reasonLabelCanvas.overrideSorting = true;
        reasonLabelCanvas.sortingOrder = reasonLabelSortingOrder;

        if (sr != null)
        {
            reasonLabelCanvas.sortingLayerID = sr.sortingLayerID;
        }

        ApplyConstrainedReasonLabelCanvasSize();

        reasonLabel.text = GetParkingLabelPersianText();
        reasonLabel.alignment = TextAlignmentOptions.Center;

        float safeMin = Mathf.Max(1f, reasonLabelAutoSizeMin);
        float safeMax = Mathf.Max(safeMin, reasonLabelAutoSizeMax);

        reasonLabel.enableAutoSizing = reasonLabelAutoSize;
        reasonLabel.fontSizeMin = safeMin;
        reasonLabel.fontSizeMax = safeMax;

        // Start Auto Size from the maximum, exactly like enabling Auto Size
        // in the RTL Text Mesh Pro Inspector.
        reasonLabel.fontSize = reasonLabelAutoSize
            ? safeMax
            : reasonLabelFontSize;

        reasonLabel.color = reasonLabelColor;

        reasonLabel.textWrappingMode = TextWrappingModes.PreserveWhitespace;
        reasonLabel.overflowMode = TextOverflowModes.Truncate;

        if (persianFontAsset != null)
        {
            reasonLabel.font = persianFontAsset;
        }

        Material labelMaterial = reasonLabel.fontMaterial;

        if (labelMaterial != null)
        {
            labelMaterial.SetColor(ShaderUtilities.ID_OutlineColor, reasonLabelOutlineColor);
            labelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, reasonLabelOutlineThickness);
            reasonLabel.fontMaterial = labelMaterial;
        }

        RectTransform textRect = reasonLabel.GetComponent<RectTransform>();
        if (textRect != null)
        {
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        reasonLabel.ForceMeshUpdate(true, true);
    }

    [UnityEngine.ContextMenu("Rebuild Persian Parking Label")]
    public void RebuildPersianReasonLabel()
    {
        RefreshReasonLabel();
    }

    [UnityEngine.ContextMenu("Apply Recommended Label Settings")]
    public void ApplyRecommendedLabelSettings()
    {
        reasonLabelLocalOffset = new Vector3(0f, 0f, -1f);
        reasonLabelCanvasSize = new Vector2(120f, 90f);
        reasonLabelCanvasScale = 0.01f;
        reasonLabelFontSize = 30f;
        reasonLabelAutoSize = true;
        reasonLabelAutoSizeMin = 18f;
        reasonLabelAutoSizeMax = 200f;
        constrainReasonLabelToSprite = true;
        reasonLabelSpriteBoundsPadding = 0.94f;
        reasonLabelColor = Color.black;
        reasonLabelOutlineColor = Color.black;
        reasonLabelOutlineThickness = 0.307f;
        reasonLabelSortingOrder = 500;
        reasonLabelRotationZ = 270f;

        RefreshReasonLabel();
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void ScheduleOriginalHierarchyAutoSizeRefresh()
    {
        EditorApplication.delayCall -= OriginalHierarchyAutoSizeRefresh;
        EditorApplication.delayCall += OriginalHierarchyAutoSizeRefresh;
    }

    private static void OriginalHierarchyAutoSizeRefresh()
    {
        EditorApplication.delayCall -= OriginalHierarchyAutoSizeRefresh;

        if (EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            Application.isPlaying)
        {
            EditorApplication.delayCall += OriginalHierarchyAutoSizeRefresh;
            return;
        }

        RebuildAllOriginalAutoSizeLabels(false);
    }

    [MenuItem("Tools/Parking/Restore Original Labels And Apply Auto Size")]
    private static void RestoreOriginalLabelsAndApplyAutoSize()
    {
        RebuildAllOriginalAutoSizeLabels(true);
    }

    [MenuItem("Tools/Parking/Keep All Text Labels Inside Their Spots")]
    private static void KeepAllTextLabelsInsideTheirSpots()
    {
        RebuildAllOriginalAutoSizeLabels(true);
    }

    private static void RebuildAllOriginalAutoSizeLabels(bool showLog)
    {
        ParkingSpot[] spots = Resources.FindObjectsOfTypeAll<ParkingSpot>();
        int rebuiltCount = 0;

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null || EditorUtility.IsPersistent(spot))
                continue;

            spot.reasonLabelAutoSize = true;
            spot.reasonLabelAutoSizeMin =
                Mathf.Max(18f, spot.reasonLabelAutoSizeMin);
            spot.reasonLabelAutoSizeMax =
                Mathf.Max(200f, spot.reasonLabelAutoSizeMax);
            spot.constrainReasonLabelToSprite = true;
            spot.reasonLabelSpriteBoundsPadding =
                Mathf.Clamp(spot.reasonLabelSpriteBoundsPadding, 0.5f, 1f);

            spot.RefreshReasonLabel();
            EditorUtility.SetDirty(spot);
            rebuiltCount++;
        }

        if (showLog)
        {
            Debug.Log(
                $"[ParkingSpot] Restored the original direct Canvas hierarchy and applied Auto Size to {rebuiltCount} spots."
            );
        }
    }
#endif

    public bool IsModeledAlternative()
    {
        return region == RegionType.Inner || region == RegionType.Outer;
    }

    public bool IsLegal()
    {
        return parkingType == ParkingType.Legal;
    }

    public bool IsIllegal()
    {
        return parkingType == ParkingType.Illegal;
    }

    public bool IsNoParkingZone()
    {
        return parkingAreaKind == ParkingAreaKind.NoParkingZone;
    }

    public string GetRegionName()
    {
        if (region == RegionType.Inner) return "inner";
        if (region == RegionType.Outer) return "outer";
        return "far_not_modeled";
    }

    public bool HasParkingSegment()
    {
        return parkingSegmentStartNode != null && parkingSegmentEndNode != null;
    }

    public Vector3 GetParkingStopPoint()
    {
        if (!HasParkingSegment())
        {
            if (nearestRoadNode != null)
                return nearestRoadNode.transform.position;

            return transform.position;
        }

        Vector2 a = parkingSegmentStartNode.transform.position;
        Vector2 b = parkingSegmentEndNode.transform.position;
        Vector2 p = transform.position;

        Vector2 ab = b - a;

        if (ab.sqrMagnitude < 0.001f)
            return a;

        float t = Vector2.Dot(p - a, ab) / ab.sqrMagnitude;
        t = Mathf.Clamp01(t);

        Vector2 projectedPoint = a + t * ab;

        Vector2 towardSpot = ((Vector2)transform.position - projectedPoint).normalized;
        Vector2 finalPoint = projectedPoint + towardSpot * parkingStopOffset;

        return new Vector3(finalPoint.x, finalPoint.y, transform.position.z);
    }

    public void OccupySpot()
    {
        isOccupied = true;
        occupiedByPlayer = false;
        isHighlighted = false;
        UpdateVisual();
    }

    public void MarkParkedByPlayer()
    {
        isOccupied = true;
        occupiedByPlayer = true;
        isHighlighted = false;
        UpdateVisual();
    }

    public void ResetSpot()
    {
        isOccupied = false;
        occupiedByPlayer = false;
        isHighlighted = false;
        UpdateVisual();
    }

    public void SetOccupied(bool occupied)
    {
        isOccupied = occupied;
        occupiedByPlayer = false;
        isHighlighted = false;
        UpdateVisual();
    }

    public void SetHighlighted(bool highlighted)
    {
        if (isOccupied) return;

        isHighlighted = highlighted;
        UpdateVisual();
    }

    private void UpdateVisualOnly()
    {
        if (sr == null)
            sr = GetComponent<SpriteRenderer>();

        if (sr == null)
            return;

        if (occupiedByPlayer)
        {
            sr.color = parkedByPlayerColor;
        }
        else if (isOccupied)
        {
            sr.color = occupiedColor;
        }
        else if (isHighlighted)
        {
            sr.color = hoverColor;
        }
        else if (parkingAreaKind == ParkingAreaKind.NoParkingZone)
        {
            sr.color = noParkingZoneFreeColor;
        }
        else if (parkingType == ParkingType.Legal && legalParkingFeature == LegalParkingFeature.DisabledParking)
        {
            sr.color = disabledParkingFreeColor;
        }
        else
        {
            sr.color = parkingType == ParkingType.Legal
                ? legalFreeColor
                : illegalFreeColor;
        }
    }

    public void UpdateVisual()
    {
        UpdateVisualOnly();
        ScheduleReasonLabelRefresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetHighlighted(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHighlighted(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        SetHighlighted(true);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        CarParkingInteraction parkingInteraction = CarParkingInteraction.Instance;

        if (parkingInteraction == null)
        {
            Debug.LogWarning("[ParkingSpot] No CarParkingInteraction instance found.");
            return;
        }

        parkingInteraction.OnParkingSpotClicked(this);
    }
}