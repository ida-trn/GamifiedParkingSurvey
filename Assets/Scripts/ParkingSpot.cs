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

    [Tooltip("If true, Spot ID and GameObject name update automatically when street/side/region/type/number changes. Spot ID uses the detailed A1-A6 classification; Spot Number itself is preserved unless explicitly renumbered.")]
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

    [Tooltip("Global unique number for this physical parking spot. Assigned by ParkingSpotIdAssigner. Changing street, side, region, or parking type does not change this number.")]
    public int spotNumber = 0;

    [Header("Choice Alternative Data")]
    public RegionType region = RegionType.Inner;

    [Tooltip("NormalParkingSpot = normal selectable parking. NoParkingZone = physically unavailable for parking; it remains visible/clickable only to show an explanation.")]
    public ParkingAreaKind parkingAreaKind = ParkingAreaKind.NormalParkingSpot;

    public ParkingType parkingType = ParkingType.Legal;

    [Header("Illegal Parking Details")]
    [Tooltip("Use None only for legal parking. For illegal parking, choose the reason.")]
    public IllegalReason illegalReason = IllegalReason.None;

    [Header("Physical Parking Availability")]
    [Tooltip(
        "Automatically controlled by Parking Area Kind. " +
        "NoParkingZone = physically blocked/unavailable; NormalParkingSpot = selectable. " +
        "You do not need to change this checkbox manually."
    )]
    public bool parkingPhysicallyBlocked = false;

    [TextArea(2, 4)]
    [Tooltip("Message shown when the participant clicks a physically blocked parking place.")]
    public string physicallyBlockedMessage =
        "پارک کردن در این محل به دلیل وجود مانع فیزیکی امکان‌پذیر نیست.";

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

    [Tooltip("Fallback canvas size used only when Fit Label Inside Parking Spot is disabled.")]
    public Vector2 reasonLabelCanvasSize = new Vector2(120f, 45f);

    [Tooltip("Base world-space scale used by the label. The script compensates for stretched parking spots so the text itself is not stretched.")]
    public float reasonLabelCanvasScale = 0.01f;

    [Header("Persian Label Fit")]
    [Tooltip("Automatically sizes the label canvas from this parking spot's SpriteRenderer so the label stays inside this spot instead of overlapping neighboring spots.")]
    public bool fitReasonLabelInsideSpot = true;

    [Tooltip("Empty margin kept on EACH side of the parking spot. 0.08 means 8% on every side.")]
    [Range(0f, 0.40f)]
    public float reasonLabelMarginFraction = 0.08f;

    [Tooltip("Minimum TMP auto-size font size.")]
    public float reasonLabelMinFontSize = 20f;

    [Tooltip("Maximum TMP auto-size font size.")]
    public float reasonLabelMaxFontSize = 1000f;

    [Tooltip("Used only when Fit Label Inside Parking Spot is disabled.")]
    public float reasonLabelFontSize = 20f;

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
    [Tooltip("Orange for illegal parking reasons with no financial fine, such as intersection corners and pedestrian crossings. Color: #FF4800.")]
    public Color illegalFreeColor = new Color32(255, 72, 0, 255);

    [Tooltip("Color for smart parking spots / پارکینگ هوشمند.")]
    public Color smartParkingFreeColor = new Color32(255, 72, 0, 255);

    [Tooltip("Color for legal parking spots that are reserved for disabled people / ویژه معلولین.")]
    public Color disabledParkingFreeColor = new Color32(52, 52, 255, 255);

    [Tooltip("Color for illegal parking reasons that should use the stronger fined/enforced red: No Parking Sign, No Stopping Sign, Driveway Entrance, and Bus Stop.")]
    public Color finedIllegalFreeColor = new Color32(229, 0, 0, 255);

    [Tooltip("Color for no-parking zones / غیرمجاز. Yellow: #FFEB00.")]
    public Color noParkingZoneFreeColor = new Color32(255, 235, 0, 255);

    public Color occupiedColor = new Color(0.7f, 0.7f, 0.7f);
    public Color hoverColor = Color.yellow;
    public Color parkedByPlayerColor = Color.blue;

    [Header("Pointer Selection")]
    [Min(1f)]
    [Tooltip("Maximum pointer movement still treated as a click/tap instead of a map drag.")]
    public float clickMovementTolerancePixels = 15f;

    private SpriteRenderer sr;
    private bool isHighlighted = false;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        ApplyIllegalColorMigrationDefaults();
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
        ApplyIllegalColorMigrationDefaults();
        RefreshAfterDataChange(autoUpdateSpotIdInInspector);
    }

    private void ApplyIllegalColorMigrationDefaults()
    {
        // Unity preserves old serialized Inspector values after a script change.
        // Migrate older no-fine illegal colors to the new orange #FF4800.
        Color oldNoFineIllegalColor = new Color32(255, 31, 31, 255);

        if (illegalFreeColor == Color.red || illegalFreeColor == oldNoFineIllegalColor)
            illegalFreeColor = new Color32(255, 72, 0, 255);

        // Migrate the old orange NoParkingZone color to the new yellow #FFEB00.
        Color oldNoParkingZoneColor = new Color(1f, 0.45f, 0f, 1f);

        if (noParkingZoneFreeColor == oldNoParkingZoneColor)
            noParkingZoneFreeColor = new Color32(255, 235, 0, 255);
    }

    /// <summary>
    /// Refreshes all derived parking-spot data after street, side, legality,
    /// area kind, region, or related Inspector data changes.
    ///
    /// IMPORTANT: This method never changes spotNumber.
    /// If regenerateSpotId is true, it rebuilds the descriptive Spot ID from
    /// the current Street + Side + A1-A6 alternative + existing Spot Number.
    /// </summary>
    public void RefreshAfterDataChange(bool regenerateSpotId = true)
    {
        sr = GetComponent<SpriteRenderer>();

        UpdateStreetCodeAndName();
        ValidateParkingLogic();

        if (regenerateSpotId)
        {
            GenerateSpotId();
        }

        UpdateVisualOnly();
        ScheduleReasonLabelRefresh();
    }

    private void ValidateParkingLogic()
    {
        // Physical availability is derived automatically from ParkingAreaKind.
        // This prevents having to tick hundreds of NoParkingZone spots by hand
        // and guarantees that ONLY NoParkingZone spots use the blocked behavior.
        parkingPhysicallyBlocked =
            parkingAreaKind == ParkingAreaKind.NoParkingZone;

        // A physically blocked place is a static map obstruction, not a
        // scenario-occupied parking choice. Keep its normal map color and
        // never let occupancy mark it grey.
        if (IsPhysicallyBlockedParking())
        {
            isOccupied = false;
            occupiedByPlayer = false;
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
        reasonLabelCanvasSize = new Vector2(120f, 45f);
        reasonLabelCanvasScale = 0.01f;

        fitReasonLabelInsideSpot = true;
        reasonLabelMarginFraction = 0.08f;

        reasonLabelMinFontSize = 20f;
        reasonLabelMaxFontSize = 1000f;
        reasonLabelFontSize = 20f;

        reasonLabelColor = Color.black;
        reasonLabelOutlineColor = Color.black;
        reasonLabelOutlineThickness = 0.307f;
        reasonLabelSortingOrder = 500;
        reasonLabelRotationZ = 270f;
    }

    /// <summary>
    /// Returns the parking spot's visible size in this GameObject's local space.
    /// This lets the label use the actual spot as its boundary instead of a
    /// hard-coded 120 x 45 canvas.
    /// </summary>
    private Vector2 GetParkingSpotLocalVisualSize()
    {
        if (sr == null)
            sr = GetComponent<SpriteRenderer>();

        if (sr != null)
        {
            Vector3 localBoundsSize = sr.localBounds.size;

            if (localBoundsSize.x > 0.0001f && localBoundsSize.y > 0.0001f)
            {
                return new Vector2(
                    Mathf.Abs(localBoundsSize.x),
                    Mathf.Abs(localBoundsSize.y)
                );
            }

            if (sr.sprite != null)
            {
                Vector3 spriteSize = sr.sprite.bounds.size;

                if (spriteSize.x > 0.0001f && spriteSize.y > 0.0001f)
                {
                    return new Vector2(
                        Mathf.Abs(spriteSize.x),
                        Mathf.Abs(spriteSize.y)
                    );
                }
            }
        }

        return new Vector2(
            Mathf.Max(0.01f, reasonLabelCanvasSize.x * reasonLabelCanvasScale),
            Mathf.Max(0.01f, reasonLabelCanvasSize.y * reasonLabelCanvasScale)
        );
    }

    /// <summary>
    /// True when the label is rotated about 90 or 270 degrees, so its canvas
    /// width/height are swapped relative to the parking spot.
    /// </summary>
    private bool ReasonLabelRotationSwapsAxes()
    {
        float z = Mathf.Repeat(reasonLabelRotationZ, 360f);

        return Mathf.Abs(Mathf.DeltaAngle(z, 90f)) < 1f ||
               Mathf.Abs(Mathf.DeltaAngle(z, 270f)) < 1f;
    }

    /// <summary>
    /// Fits the label inside the parking spot and compensates for non-uniform
    /// scaling inherited from the parking spot / road prefab.
    /// </summary>
    private void FitReasonLabelCanvasInsideSpot(RectTransform canvasRect)
    {
        if (canvasRect == null || reasonLabelCanvas == null)
            return;

        if (!fitReasonLabelInsideSpot)
        {
            reasonLabelCanvas.transform.localScale =
                Vector3.one * Mathf.Max(0.0001f, reasonLabelCanvasScale);

            canvasRect.sizeDelta = reasonLabelCanvasSize;
            return;
        }

        Vector2 spotLocalSize = GetParkingSpotLocalVisualSize();

        Vector3 lossyScale = transform.lossyScale;
        float parentScaleX = Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x));
        float parentScaleY = Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y));
        float baseScale = Mathf.Max(0.0001f, reasonLabelCanvasScale);

        float spotWorldWidth = spotLocalSize.x * parentScaleX;
        float spotWorldHeight = spotLocalSize.y * parentScaleY;

        float margin = Mathf.Clamp(reasonLabelMarginFraction, 0f, 0.40f);
        float usableFraction = Mathf.Max(0.05f, 1f - (margin * 2f));

        spotWorldWidth *= usableFraction;
        spotWorldHeight *= usableFraction;

        bool swapsAxes = ReasonLabelRotationSwapsAxes();

        if (swapsAxes)
        {
            reasonLabelCanvas.transform.localScale = new Vector3(
                baseScale / parentScaleY,
                baseScale / parentScaleX,
                1f
            );

            canvasRect.sizeDelta = new Vector2(
                spotWorldHeight / baseScale,
                spotWorldWidth / baseScale
            );
        }
        else
        {
            reasonLabelCanvas.transform.localScale = new Vector3(
                baseScale / parentScaleX,
                baseScale / parentScaleY,
                1f
            );

            canvasRect.sizeDelta = new Vector2(
                spotWorldWidth / baseScale,
                spotWorldHeight / baseScale
            );
        }

        canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.anchoredPosition = Vector2.zero;
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

        // Never silently assign 1 to a new/unassigned spot.
        // That could create duplicate Spot Numbers. New or duplicated spots
        // should receive a number through ParkingSpotIdAssigner.
        if (spotNumber <= 0)
        {
            spotId = $"{streetCode}_{sideCode}_{altId}_UNASSIGNED";
            gameObject.name = spotId;
            return;
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

    /// <summary>
    /// Detailed six-alternative classification used by the Spot ID:
    /// A1 = Inner + Legal
    /// A2 = Inner + Illegal
    /// A3 = Outer + Legal
    /// A4 = Outer + Illegal
    /// A5 = Far + Legal
    /// A6 = Far + Illegal
    /// </summary>
    public string GetAlternativeId()
    {
        return GetAlternativeId6();
    }

    /// <summary>
    /// Six-alternative version.
    /// Keeps legal and illegal parking separate in the Far region.
    /// </summary>
    public string GetAlternativeId6()
    {
        if (region == RegionType.Inner && parkingType == ParkingType.Legal)
            return "A1";

        if (region == RegionType.Inner && parkingType == ParkingType.Illegal)
            return "A2";

        if (region == RegionType.Outer && parkingType == ParkingType.Legal)
            return "A3";

        if (region == RegionType.Outer && parkingType == ParkingType.Illegal)
            return "A4";

        if (region == RegionType.Far && parkingType == ParkingType.Legal)
            return "A5";

        if (region == RegionType.Far && parkingType == ParkingType.Illegal)
            return "A6";

        return "UNKNOWN";
    }

    /// <summary>
    /// Collapsed five-alternative version for analysis/model comparison:
    /// A1 = Inner + Legal
    /// A2 = Inner + Illegal
    /// A3 = Outer + Legal
    /// A4 = Outer + Illegal
    /// A5 = Far, regardless of legal/illegal status.
    ///
    /// This method does NOT change the Spot ID. It is intended for logging
    /// or later model estimation when you want the five-alternative logic.
    /// </summary>
    public string GetAlternativeId5()
    {
        string detailedAlternative = GetAlternativeId6();

        if (detailedAlternative == "A6")
            return "A5";

        return detailedAlternative;
    }

    public string GetIllegalReasonPersianText()
    {
        switch (illegalReason)
        {
            case IllegalReason.DrivewayEntrance:
                return "جلوی ورودی پارکینگ";

            case IllegalReason.NoParkingSign:
                return "تابلو پارک ممنوع";

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
        // All orange NoParkingZone spots use one consistent visible label,
        // regardless of their IllegalReason value.
        if (IsNoParkingZone())
        {
            return "غیرمجاز";
        }

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

        // NoParkingZone spots are always shown with the label "غیرمجاز",
        // even when IllegalReason is None.
        if (IsNoParkingZone())
            return true;

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
            // OnValidate and OnEnable can also run while Unity is importing or
            // inspecting a prefab asset. Never create/delete label objects in
            // an asset outside Prefab Mode; only refresh scene instances.
            if (EditorUtility.IsPersistent(this) ||
                PrefabUtility.IsPartOfPrefabAsset(gameObject))
            {
                return;
            }

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

#if UNITY_EDITOR
        // DestroyImmediate on a saved prefab/asset object is blocked by
        // Unity to prevent data loss. Asset contents must be edited explicitly
        // in Prefab Mode, not by this automatic scene-label cleanup.
        if (EditorUtility.IsPersistent(obj) ||
            PrefabUtility.IsPartOfPrefabAsset(obj))
        {
            return;
        }
#endif

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

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (child.name == "IllegalReasonLabel")
            {
                DestroyObjectSafe(child.gameObject);
                continue;
            }

            if (child.name != "IllegalReasonCanvas")
                continue;

            Canvas canvas = child.GetComponent<Canvas>();

            if (canvas != null && canvasToKeep == null)
            {
                canvasToKeep = canvas;
                labelToKeep = canvas.GetComponentInChildren<RTLTextMeshPro>(true);
            }
            else
            {
                DestroyObjectSafe(child.gameObject);
            }
        }

        reasonLabelCanvas = canvasToKeep;
        reasonLabel = labelToKeep;
    }

    private void CreateReasonLabel()
    {
        GameObject canvasObject = new GameObject("IllegalReasonCanvas");
        canvasObject.layer = gameObject.layer;
        canvasObject.transform.SetParent(transform, false);
        canvasObject.transform.localPosition = reasonLabelLocalOffset;
        canvasObject.transform.localRotation = Quaternion.Euler(0f, 0f, reasonLabelRotationZ);

        RectTransform canvasRect = canvasObject.AddComponent<RectTransform>();

        reasonLabelCanvas = canvasObject.AddComponent<Canvas>();
        reasonLabelCanvas.renderMode = RenderMode.WorldSpace;
        reasonLabelCanvas.overrideSorting = true;
        reasonLabelCanvas.sortingOrder = reasonLabelSortingOrder;

        if (sr != null)
        {
            reasonLabelCanvas.sortingLayerID = sr.sortingLayerID;
        }

        FitReasonLabelCanvasInsideSpot(canvasRect);

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
#if UNITY_EDITOR
        if (!Application.isPlaying &&
            (EditorUtility.IsPersistent(this) ||
             PrefabUtility.IsPartOfPrefabAsset(gameObject)))
        {
            return;
        }
#endif

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

        reasonLabelCanvas.renderMode = RenderMode.WorldSpace;
        reasonLabelCanvas.overrideSorting = true;
        reasonLabelCanvas.sortingOrder = reasonLabelSortingOrder;

        if (sr != null)
        {
            reasonLabelCanvas.sortingLayerID = sr.sortingLayerID;
        }

        RectTransform canvasRect = reasonLabelCanvas.GetComponent<RectTransform>();
        if (canvasRect != null)
        {
            FitReasonLabelCanvasInsideSpot(canvasRect);
        }

        reasonLabel.text = GetParkingLabelPersianText();
        reasonLabel.alignment = TextAlignmentOptions.Center;
        reasonLabel.color = reasonLabelColor;

        if (fitReasonLabelInsideSpot)
        {
            reasonLabel.enableAutoSizing = true;
            reasonLabel.fontSizeMin = Mathf.Max(1f, reasonLabelMinFontSize);
            reasonLabel.fontSizeMax = Mathf.Max(reasonLabel.fontSizeMin, reasonLabelMaxFontSize);
            reasonLabel.fontSize = reasonLabel.fontSizeMax;
        }
        else
        {
            reasonLabel.enableAutoSizing = false;
            reasonLabel.fontSize = reasonLabelFontSize;
        }

        reasonLabel.textWrappingMode = TextWrappingModes.Normal;

        // Never allow text geometry to spill into a neighboring parking spot.
        reasonLabel.overflowMode = TextOverflowModes.Ellipsis;

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
        reasonLabelCanvasSize = new Vector2(120f, 45f);
        reasonLabelCanvasScale = 0.01f;

        fitReasonLabelInsideSpot = true;
        reasonLabelMarginFraction = 0.08f;
        reasonLabelMinFontSize = 20f;
        reasonLabelMaxFontSize = 1000f;
        reasonLabelFontSize = 20f;

        reasonLabelColor = Color.black;
        reasonLabelOutlineColor = Color.black;
        reasonLabelOutlineThickness = 0.307f;
        reasonLabelSortingOrder = 500;
        reasonLabelRotationZ = 270f;

        RefreshReasonLabel();
    }

    public bool IsPhysicallyBlockedParking()
    {
        // Derive from ParkingAreaKind as well as the serialized field so the
        // behavior is correct even before OnValidate/Awake has refreshed it.
        return parkingAreaKind == ParkingAreaKind.NoParkingZone;
    }

    public string GetPhysicallyBlockedParkingMessagePersian()
    {
        if (!string.IsNullOrWhiteSpace(physicallyBlockedMessage))
            return physicallyBlockedMessage.Trim();

        return "پارک کردن در این محل به دلیل وجود مانع فیزیکی امکان‌پذیر نیست.";
    }

    public bool IsModeledAlternative()
    {
        // Physical obstructions remain visible/clickable map features for an
        // explanation, but they are not parking alternatives in the study.
        if (IsPhysicallyBlockedParking())
            return false;

        return region == RegionType.Inner ||
               region == RegionType.Outer ||
               region == RegionType.Far;
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
        if (region == RegionType.Far) return "far";

        return "unknown";
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
        if (parkingPhysicallyBlocked)
        {
            isOccupied = false;
            occupiedByPlayer = false;
            isHighlighted = false;
            UpdateVisual();
            return;
        }

        isOccupied = true;
        occupiedByPlayer = false;
        isHighlighted = false;
        UpdateVisual();
    }

    public void MarkParkedByPlayer()
    {
        if (IsPhysicallyBlockedParking())
            return;

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
        // Physical obstructions are static unavailable areas, not randomly
        // occupied parking spaces. Keeping isOccupied=false preserves the
        // spot's existing map color instead of turning it grey.
        isOccupied = IsPhysicallyBlockedParking() ? false : occupied;
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

    private bool UsesFinedIllegalColor()
    {
        if (parkingType != ParkingType.Illegal)
            return false;

        switch (illegalReason)
        {
            case IllegalReason.DrivewayEntrance:
            case IllegalReason.NoParkingSign:
            case IllegalReason.NoStoppingSign:
            case IllegalReason.BusStop:
                return true;

            default:
                return false;
        }
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
        else if (parkingType == ParkingType.Legal &&
                 legalParkingFeature == LegalParkingFeature.SmartParking)
        {
            sr.color = smartParkingFreeColor;
        }
        else if (parkingType == ParkingType.Legal &&
                 legalParkingFeature == LegalParkingFeature.DisabledParking)
        {
            sr.color = disabledParkingFreeColor;
        }
        else
        {
            if (parkingType == ParkingType.Legal)
            {
                sr.color = legalFreeColor;
            }
            else
            {
                sr.color = UsesFinedIllegalColor()
                    ? finedIllegalFreeColor
                    : illegalFreeColor;
            }
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
        // First line of defense against accidental selection:
        // if the pointer travelled far enough to be a drag, do not treat the
        // release as a parking-space click/tap.
        if (eventData != null)
        {
            float pointerMovement = Vector2.Distance(
                eventData.pressPosition,
                eventData.position
            );

            if (pointerMovement >
                Mathf.Max(1f, clickMovementTolerancePixels))
            {
                SetHighlighted(false);
                return;
            }
        }

        // Second line of defense for assisted map mode. The camera controller
        // briefly suppresses parking clicks after mouse/finger drag or pinch.
        AssistedMapCameraController mapController =
            AssistedMapCameraController.Instance;

        if (mapController != null &&
            mapController.ShouldSuppressParkingSpotClick())
        {
            SetHighlighted(false);
            return;
        }

        CarParkingInteraction parkingInteraction =
            CarParkingInteraction.Instance;

        if (parkingInteraction == null)
        {
            Debug.LogWarning(
                "[ParkingSpot] No CarParkingInteraction instance found."
            );
            return;
        }

        parkingInteraction.OnParkingSpotClicked(this);
    }
}
