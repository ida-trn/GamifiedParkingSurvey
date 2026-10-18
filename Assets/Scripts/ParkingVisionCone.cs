using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMPORARY / OPTIONAL visual prototype for conventional parking visibility.
///
/// Attach this script to the same Car GameObject that has CarNodeFollower.
/// It does NOT change occupancy generation, routing, scoring, or logging.
///
/// Conventional mode:
/// - Parking spots outside the forward vision area are drawn with an "unknown" colour.
/// - A spot is revealed when it enters the cone or the small radius around the car.
/// - Once revealed, it stays revealed for the rest of that round.
///
/// Assisted mode:
/// - All parking spots remain fully visible.
///
/// Remove this component (and this .cs file) to completely remove the experiment.
/// </summary>
[DisallowMultipleComponent]
public class ParkingVisionCone : MonoBehaviour
{
    [Header("Enable / Mode")]
    [Tooltip("Master switch for this temporary experiment.")]
    public bool enableVisibilityExperiment = true;

    [Tooltip("When ON, visibility restriction is used only in Conventional mode. Assisted mode always shows every spot.")]
    public bool conventionalOnly = true;

    [Header("Vision Area")]
    [Tooltip("Forward viewing distance in REAL metres.")]
    [Min(1f)]
    public float visionDistanceMeters = 60f;

    [Tooltip("Total forward field of view. 80 means 40 degrees to each side.")]
    [Range(10f, 180f)]
    public float fieldOfViewDegrees = 80f;

    [Tooltip("Spots very close to the car are revealed even if they sit slightly behind/beside it.")]
    [Min(0f)]
    public float nearbyRevealRadiusMeters = 8f;

    [Tooltip("How often the script checks visibility. 0.08-0.15 s is plenty for this prototype.")]
    [Range(0.02f, 1f)]
    public float visibilityCheckInterval = 0.10f;

    [Header("Unknown Parking Appearance")]
    [Tooltip("Temporary colour used for parking spots whose occupancy has not yet been seen.")]
    public Color unknownOccupancyColor = new Color32(205, 205, 205, 255);

    [Tooltip("Once a spot has been seen, remember it for the rest of the current round.")]
    public bool rememberSeenSpots = true;

    [Header("Debug Vision Triangle")]
    [Tooltip("Keep ON while testing. Turn OFF for the real experiment.")]
    public bool showVisionTriangle = true;

    [Tooltip("Transparent fill of the temporary vision triangle.")]
    public Color triangleFillColor = new Color(1f, 0.92f, 0.15f, 0.14f);

    [Tooltip("Outline colour of the temporary vision triangle.")]
    public Color triangleOutlineColor = new Color(1f, 0.92f, 0.15f, 0.75f);

    [Min(0.005f)]
    public float triangleOutlineWidth = 0.05f;

    [Tooltip("Sorting order for the debug triangle. Keep it behind parking labels/buttons.")]
    public int triangleSortingOrder = -20;

    [Header("References (auto-filled if empty)")]
    public ScenarioManager scenarioManager;
    public CarNodeFollower carFollower;

    [Header("Debug")]
    public bool debugLogs = false;

    private readonly List<ParkingSpot> allSpots = new List<ParkingSpot>();
    private readonly HashSet<ParkingSpot> revealedSpots = new HashSet<ParkingSpot>();
    private readonly Dictionary<ParkingSpot, SpriteRenderer> spriteRenderers =
        new Dictionary<ParkingSpot, SpriteRenderer>();

    private float nextVisibilityCheckTime;
    private int lastRoundIndex = int.MinValue;
    private bool lastAssistedMode;
    private bool lastPracticeState;
    private bool hasRoundSignature;

    private GameObject triangleRoot;
    private Mesh triangleMesh;
    private MeshRenderer triangleMeshRenderer;
    private LineRenderer triangleLineRenderer;
    private Material triangleFillMaterial;
    private Material triangleLineMaterial;

    private void Awake()
    {
        if (carFollower == null)
            carFollower = GetComponent<CarNodeFollower>();

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        RefreshParkingSpotCache();
        BuildDebugTriangle();
    }

    private void OnEnable()
    {
        if (carFollower == null)
            carFollower = GetComponent<CarNodeFollower>();

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        RefreshParkingSpotCache();
        hasRoundSignature = false;
        nextVisibilityCheckTime = 0f;

        BuildDebugTriangle();
        UpdateDebugTriangleVisibility();
    }

    private void Update()
    {
        if (!enableVisibilityExperiment)
        {
            RestoreAllParkingVisuals();
            SetTriangleActive(false);
            return;
        }

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (carFollower == null)
            carFollower = GetComponent<CarNodeFollower>();

        DetectRoundOrModeChange();

        bool assisted = IsAssistedMode();

        if (conventionalOnly && assisted)
        {
            RevealAllSpots();
            SetTriangleActive(false);
            return;
        }

        bool gameplayActive =
            scenarioManager == null || scenarioManager.IsScenarioGameplayActive;

        SetTriangleActive(showVisionTriangle && gameplayActive);

        if (!gameplayActive)
            return;

        if (Time.unscaledTime < nextVisibilityCheckTime)
            return;

        nextVisibilityCheckTime =
            Time.unscaledTime + Mathf.Max(0.02f, visibilityCheckInterval);

        UpdateVisibleParkingSpots();
    }

    private void LateUpdate()
    {
        if (!enableVisibilityExperiment)
            return;

        if (conventionalOnly && IsAssistedMode())
            return;

        // ParkingSpot may refresh its own colour because of hover/highlight.
        // Reassert the "unknown" appearance at the end of the frame.
        foreach (ParkingSpot spot in allSpots)
        {
            if (spot == null || revealedSpots.Contains(spot))
                continue;

            if (!spriteRenderers.TryGetValue(spot, out SpriteRenderer renderer) ||
                renderer == null)
            {
                renderer = spot.GetComponent<SpriteRenderer>();
                spriteRenderers[spot] = renderer;
            }

            if (renderer != null)
                renderer.color = unknownOccupancyColor;
        }
    }

    private void DetectRoundOrModeChange()
    {
        if (scenarioManager == null)
            return;

        int roundIndex = scenarioManager.currentRoundIndex;
        bool assisted = scenarioManager.currentAssistedMode;
        bool practice = scenarioManager.isPracticeRound;

        bool changed =
            !hasRoundSignature ||
            roundIndex != lastRoundIndex ||
            assisted != lastAssistedMode ||
            practice != lastPracticeState;

        if (!changed)
            return;

        lastRoundIndex = roundIndex;
        lastAssistedMode = assisted;
        lastPracticeState = practice;
        hasRoundSignature = true;

        RefreshParkingSpotCache();

        if (conventionalOnly && assisted)
        {
            RevealAllSpots();
        }
        else
        {
            BeginFreshVisibilityRound();
        }

        if (debugLogs)
        {
            Debug.Log(
                "[ParkingVisionCone] Visibility reset | round=" +
                roundIndex +
                ", assisted=" + assisted +
                ", practice=" + practice
            );
        }
    }

    private void BeginFreshVisibilityRound()
    {
        // First restore the true colours generated by ParkingSpot / occupancy logic.
        RestoreAllParkingVisuals();

        revealedSpots.Clear();

        // Immediately reveal anything already inside the starting field of view.
        UpdateVisibleParkingSpots();
    }

    private void UpdateVisibleParkingSpots()
    {
        if (allSpots.Count == 0)
            RefreshParkingSpotCache();

        float metresPerUnit = GetMetersPerUnityUnit();

        if (metresPerUnit <= 0.0001f)
            return;

        float visionDistanceUnity =
            Mathf.Max(0f, visionDistanceMeters) / metresPerUnit;

        float nearbyRadiusUnity =
            Mathf.Max(0f, nearbyRevealRadiusMeters) / metresPerUnit;

        float halfFov =
            Mathf.Clamp(fieldOfViewDegrees, 10f, 180f) * 0.5f;

        Vector2 carPosition = transform.position;
        Vector2 forward = GetTravelForwardDirection();

        foreach (ParkingSpot spot in allSpots)
        {
            if (spot == null)
                continue;

            if (rememberSeenSpots && revealedSpots.Contains(spot))
                continue;

            Vector2 toSpot =
                (Vector2)spot.transform.position - carPosition;

            float distance = toSpot.magnitude;

            bool visible = distance <= nearbyRadiusUnity;

            if (!visible &&
                distance <= visionDistanceUnity &&
                distance > 0.0001f)
            {
                float angle =
                    Vector2.Angle(forward, toSpot / distance);

                visible = angle <= halfFov;
            }

            if (visible)
                RevealSpot(spot);
            else if (!rememberSeenSpots)
                HideSpot(spot);
        }
    }

    private Vector2 GetTravelForwardDirection()
    {
        float spriteOffset =
            carFollower != null
                ? carFollower.spriteRotationOffset
                : 0f;

        float travelAngleDegrees =
            transform.eulerAngles.z - spriteOffset;

        float radians =
            travelAngleDegrees * Mathf.Deg2Rad;

        return new Vector2(
            Mathf.Cos(radians),
            Mathf.Sin(radians)
        ).normalized;
    }

    private float GetMetersPerUnityUnit()
    {
        if (carFollower != null &&
            carFollower.metersPerUnityUnit > 0f)
        {
            return carFollower.metersPerUnityUnit;
        }

        if (scenarioManager != null &&
            scenarioManager.metersPerUnityUnit > 0f)
        {
            return scenarioManager.metersPerUnityUnit;
        }

        return 0.2424f;
    }

    private bool IsAssistedMode()
    {
        return scenarioManager != null &&
               scenarioManager.currentAssistedMode;
    }

    private void RevealSpot(ParkingSpot spot)
    {
        if (spot == null)
            return;

        revealedSpots.Add(spot);

        // Public method already present in your ParkingSpot script.
        // This restores the correct occupied/free/legal/illegal/smart colour.
        spot.UpdateVisual();
    }

    private void HideSpot(ParkingSpot spot)
    {
        if (spot == null)
            return;

        revealedSpots.Remove(spot);

        if (!spriteRenderers.TryGetValue(spot, out SpriteRenderer renderer) ||
            renderer == null)
        {
            renderer = spot.GetComponent<SpriteRenderer>();
            spriteRenderers[spot] = renderer;
        }

        if (renderer != null)
            renderer.color = unknownOccupancyColor;
    }

    private void RevealAllSpots()
    {
        foreach (ParkingSpot spot in allSpots)
        {
            if (spot == null)
                continue;

            revealedSpots.Add(spot);
            spot.UpdateVisual();
        }
    }

    private void RestoreAllParkingVisuals()
    {
        foreach (ParkingSpot spot in allSpots)
        {
            if (spot != null)
                spot.UpdateVisual();
        }
    }

    [ContextMenu("Refresh Parking Spot Cache")]
    public void RefreshParkingSpotCache()
    {
        allSpots.Clear();
        spriteRenderers.Clear();

        ParkingSpot[] spots =
            FindObjectsByType<ParkingSpot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            allSpots.Add(spot);
            spriteRenderers[spot] =
                spot.GetComponent<SpriteRenderer>();
        }
    }

    // ---------------------------------------------------------------------
    // Temporary visible triangle
    // ---------------------------------------------------------------------

    private void BuildDebugTriangle()
    {
        if (triangleRoot != null)
            return;

        triangleRoot = new GameObject("DEBUG_ParkingVisionTriangle");
        triangleRoot.transform.SetParent(transform, false);
        triangleRoot.transform.localPosition = Vector3.zero;

        MeshFilter meshFilter =
            triangleRoot.AddComponent<MeshFilter>();

        triangleMeshRenderer =
            triangleRoot.AddComponent<MeshRenderer>();

        triangleMesh = new Mesh();
        triangleMesh.name = "ParkingVisionTriangleMesh";
        meshFilter.sharedMesh = triangleMesh;

        Shader spriteShader = Shader.Find("Sprites/Default");

        if (spriteShader != null)
        {
            triangleFillMaterial = new Material(spriteShader);
            triangleFillMaterial.name = "TEMP_ParkingVisionFill";
            triangleFillMaterial.color = triangleFillColor;
            triangleMeshRenderer.sharedMaterial = triangleFillMaterial;
        }

        triangleMeshRenderer.sortingOrder = triangleSortingOrder;

        triangleLineRenderer =
            triangleRoot.AddComponent<LineRenderer>();

        triangleLineRenderer.useWorldSpace = false;
        triangleLineRenderer.loop = true;
        triangleLineRenderer.positionCount = 3;
        triangleLineRenderer.startWidth = triangleOutlineWidth;
        triangleLineRenderer.endWidth = triangleOutlineWidth;
        triangleLineRenderer.numCapVertices = 2;
        triangleLineRenderer.numCornerVertices = 2;
        triangleLineRenderer.sortingOrder = triangleSortingOrder + 1;

        if (spriteShader != null)
        {
            triangleLineMaterial = new Material(spriteShader);
            triangleLineMaterial.name = "TEMP_ParkingVisionOutline";
            triangleLineMaterial.color = triangleOutlineColor;
            triangleLineRenderer.sharedMaterial = triangleLineMaterial;
        }

        UpdateDebugTriangleGeometry();
        UpdateDebugTriangleVisibility();
    }

    private void UpdateDebugTriangleGeometry()
    {
        if (triangleRoot == null || triangleMesh == null)
            return;

        float metresPerUnit = GetMetersPerUnityUnit();

        if (metresPerUnit <= 0.0001f)
            metresPerUnit = 0.2424f;

        float distanceUnity =
            Mathf.Max(0.01f, visionDistanceMeters / metresPerUnit);

        float halfAngle =
            Mathf.Clamp(fieldOfViewDegrees, 10f, 180f) *
            0.5f *
            Mathf.Deg2Rad;

        Vector3 origin = Vector3.zero;

        Vector3 left = new Vector3(
            Mathf.Cos(halfAngle) * distanceUnity,
            Mathf.Sin(halfAngle) * distanceUnity,
            0f
        );

        Vector3 right = new Vector3(
            Mathf.Cos(halfAngle) * distanceUnity,
            -Mathf.Sin(halfAngle) * distanceUnity,
            0f
        );

        triangleMesh.Clear();
        triangleMesh.vertices = new Vector3[]
        {
            origin,
            left,
            right
        };

        triangleMesh.triangles = new int[]
        {
            0, 1, 2
        };

        triangleMesh.RecalculateBounds();

        if (triangleLineRenderer != null)
        {
            triangleLineRenderer.SetPosition(0, origin);
            triangleLineRenderer.SetPosition(1, left);
            triangleLineRenderer.SetPosition(2, right);

            triangleLineRenderer.startWidth = triangleOutlineWidth;
            triangleLineRenderer.endWidth = triangleOutlineWidth;

            if (triangleLineMaterial != null)
                triangleLineMaterial.color = triangleOutlineColor;
        }

        if (triangleFillMaterial != null)
            triangleFillMaterial.color = triangleFillColor;

        if (triangleMeshRenderer != null)
            triangleMeshRenderer.sortingOrder = triangleSortingOrder;

        if (triangleLineRenderer != null)
            triangleLineRenderer.sortingOrder = triangleSortingOrder + 1;

        // CarNodeFollower may use a sprite rotation offset. Rotate this local
        // +X triangle back by that offset so it follows actual travel direction.
        float spriteOffset =
            carFollower != null
                ? carFollower.spriteRotationOffset
                : 0f;

        triangleRoot.transform.localRotation =
            Quaternion.Euler(0f, 0f, -spriteOffset);
    }

    private void UpdateDebugTriangleVisibility()
    {
        UpdateDebugTriangleGeometry();

        bool shouldShow =
            enableVisibilityExperiment &&
            showVisionTriangle &&
            !(conventionalOnly && IsAssistedMode());

        SetTriangleActive(shouldShow);
    }

    private void SetTriangleActive(bool active)
    {
        if (triangleRoot != null &&
            triangleRoot.activeSelf != active)
        {
            triangleRoot.SetActive(active);
        }

        if (active)
            UpdateDebugTriangleGeometry();
    }

    private void OnDisable()
    {
        RestoreAllParkingVisuals();
        SetTriangleActive(false);
    }

    private void OnDestroy()
    {
        RestoreAllParkingVisuals();

        if (triangleFillMaterial != null)
            Destroy(triangleFillMaterial);

        if (triangleLineMaterial != null)
            Destroy(triangleLineMaterial);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        visionDistanceMeters = Mathf.Max(1f, visionDistanceMeters);
        nearbyRevealRadiusMeters = Mathf.Max(0f, nearbyRevealRadiusMeters);
        visibilityCheckInterval = Mathf.Clamp(
            visibilityCheckInterval,
            0.02f,
            1f
        );

        if (Application.isPlaying)
        {
            UpdateDebugTriangleGeometry();
            UpdateDebugTriangleVisibility();
        }
    }
#endif
}
