using System;
using System.Globalization;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

public enum ScenarioBlockSelection
{
    Automatic = 0,
    A = 1,
    B = 2
}

public class ScenarioManager : MonoBehaviour
{
    [Header("Logging")]
    public string participantId = "P001";

    [Tooltip("Freeze this during data collection, for example 1.1-pilot-score.")]
    public string studyVersion = "2.0-release";

    [Tooltip("Change this whenever gameplay, scoring, scenarios, logging, or UI changes.")]
    public string buildVersion = "2.0.3-balanced-blocks";

    [Tooltip("Automatically filled at runtime with the actual scenario asset order used for this participant.")]
    public string scenarioOrderLabel = "NotInitialised";

    [Header("Scenario Order Randomisation")]
    [Tooltip("Keep ON for the main study. Randomises the order of scenario ASSETS while keeping the participant-facing numbering sequential.")]
    public bool randomizeScenarioOrder = true;

    [Tooltip("Runtime seed used to generate the scenario order. Logged indirectly through the complete ScenarioOrder string.")]
    [HideInInspector] public int scenarioOrderSeed = 0;

    [Header("Scenario Blocking")]
    [Tooltip("Keep ON for the main study. Each participant receives only one balanced block of six scenario assets.")]
    public bool useScenarioBlocks = true;

    [Tooltip("Automatic requests a balanced block assignment from Supabase. Use A/B only for testing.")]
    public ScenarioBlockSelection scenarioBlockSelection = ScenarioBlockSelection.Automatic;

    [Tooltip("Keep ON for the main study. Automatic block allocation is requested from Supabase before the first research scenario starts.")]
    public bool useSupabaseBalancedBlockAssignment = true;

    [Tooltip("If Supabase block assignment fails, the main study does not start. Recommended ON so collected data always follow the balanced allocation rule.")]
    public bool requireSuccessfulSupabaseBlockAssignment = true;

    [Tooltip("Number of times to retry the Supabase assignment request before failing.")]
    [Min(1)] public int blockAssignmentMaxAttempts = 3;

    [Tooltip("Delay between failed assignment attempts.")]
    [Min(0.1f)] public float blockAssignmentRetryDelaySeconds = 1.5f;

    [Tooltip("Scenario IDs assigned to Block A. Keep all 12 assets in the main Scenarios list below.")]
    public string[] blockAScenarioIds = { "S01", "S02", "S06", "S07", "S10", "S12" };

    [Tooltip("Scenario IDs assigned to Block B. Keep all 12 assets in the main Scenarios list below.")]
    public string[] blockBScenarioIds = { "S03", "S04", "S05", "S08", "S09", "S11" };

    [Tooltip("Runtime block used for the current participant. Logged inside ScenarioOrderLabel.")]
    [HideInInspector] public string assignedScenarioBlock = "NotAssigned";

    [Tooltip("Runtime source of the current assignment: Supabase, Manual, LocalFallback, or NotAssigned.")]
    [HideInInspector] public string scenarioBlockAssignmentSource = "NotAssigned";

    private bool blockAssignmentRequestInProgress = false;
    private bool blockAssignmentResolved = false;

    [Header("Scenarios")]
    public List<ParkingScenario> scenarios = new List<ParkingScenario>();

    [Tooltip("If ON: each scenario runs once in conventional mode and once in assisted mode.")]
    public bool runConventionalAndAssistedForEachScenario = true;

    [Tooltip("If ON: each scenario runs Conventional first, then Assisted.")]
    public bool conventionalFirst = true;

    [Tooltip("0 = no testing limit. Use 2 to test only the first presented scenario in both modes.")]
    public int maxRoundsForTesting = 0;

    [Header("Paired Conventional / Assisted Occupancy")]
    [Tooltip("Keep ON for the study. The first mode of each scenario generates occupancy once; the paired mode then reuses the exact same occupied physical parking spots.")]
    public bool reuseExactOccupancyForModePair = true;

    [Tooltip("Logs a verification message when the paired mode restores the exact occupancy snapshot.")]
    public bool logPairedOccupancyChecks = true;

    [Header("Current State")]
    public int currentScenarioIndex = 0;
    public int currentRoundIndex = 0;
    public bool currentAssistedMode = false;

    [Header("References")]
    public ParkingOccupancyManager occupancyManager;
    public CarParkingInteraction carParking;

    [Tooltip("Tracks the observed/experienced parking choice set. Required for conventional study rounds.")]
    public ParkingViewTracker parkingViewTracker;

    [Tooltip("Assign the Car object. If empty, carParking.transform is used.")]
    public Transform carTransform;

    [Header("Destination")]
    public Transform destinationGate;

    [Tooltip("Your measured calibration was 80 metres / 330 Unity units = 0.2424.")]
    public float metersPerUnityUnit = 0.2424f;

    [Tooltip("Normal adult walking speed is approximately 1.4 m/s.")]
    public float walkingSpeedMetersPerSecond = 1.4f;

    [Header("Walking Network")]
    [Tooltip("Assign the WalkingNetworkCalculator component used to calculate sidewalk-route distances.")]
    public WalkingNetworkCalculator walkingNetworkCalculator;

    [Tooltip("Recalculate all parking-to-gate walking distances immediately before each round is logged.")]
    public bool recalculateWalkingDistancesBeforeEachRound = true;

    [Header("Car start by RoadNode")]
    public RoadNode[] scenarioStartNodes;
    public bool snapCarToStartNode = true;

    [Header("UI - Scenario info")]
    public TMP_Text scenarioLabel;
    public TMP_Text roundLabel;
    public TMP_Text timerLabel;

    [Tooltip("Optional timer progress-bar UI. If left empty, Auto Bind UI References looks for a TimerBarUI component on TimePanel.")]
    public TimerBarUI timerBarUI;

    public TMP_Text modeLabel;
    public TMP_Text dwellTimeLabel;
    public TMP_Text timeLimitLabel;
    public TMP_Text legalFeeLabel;
    public TMP_Text illegalFineLabel;
    public TMP_Text citationProbLabel;

    [Header("UI - Live scenario characteristics table")]
    public TMP_Text dwellTimeValue;
    public TMP_Text legalOccupancyValue;
    public TMP_Text illegalOccupancyValue;
    public TMP_Text smartFeeValue;

    [Tooltip("Live-panel value for No Parking / Driveway Entrance fine.")]
    public TMP_Text noParkingFineValue;

    [Tooltip("Live-panel value for No Stopping fine.")]
    public TMP_Text noStoppingFineValue;

    // Legacy single-value field kept only so older scene serialization does not break.
    [HideInInspector] public TMP_Text finePolicyValue;

    public TMP_Text citationValue;

    [Header("UI - Citation probability visual")]
    [Tooltip("Background Image of the ValueBox inside Row_CitationProbability.")]
    public Image citationValueBox;

    [Tooltip("Used when the adjusted probability is below 20%.")]
    public Color citationLowColor = new Color32(151, 220, 160, 255);

    [Tooltip("Used when the adjusted probability is from 20% up to 40%.")]
    public Color citationMediumColor = new Color32(244, 211, 94, 255);

    [Tooltip("Used when the adjusted probability is 40% or higher.")]
    public Color citationHighColor = new Color32(244, 166, 166, 255);

    [Tooltip("Upper limit for the Low category. 0.20 means 20%.")]
    [Range(0f, 1f)] public float citationLowUpperBound = 0.20f;

    [Tooltip("Upper limit for the Medium category. 0.40 means 40%.")]
    [Range(0f, 1f)] public float citationMediumUpperBound = 0.40f;

    [Tooltip("Separate text object for illegal parking types that do not have a monetary fine.")]
    public TMP_Text noFineIllegalLabel;

    [Tooltip("This existing label is reused to show the on-time base points.")]
    public TMP_Text onTimeRewardLabel;

    [Header("UI - Before each scenario")]
    [Tooltip("Full-screen board shown before each round begins.")]
    public GameObject scenarioIntroPanel;
    public TMP_Text introTitleLabel;
    public TMP_Text introModeLabel;

    [Header("UI - Scenario intro characteristics table")]
    public TMP_Text introDwellValue;
    public TMP_Text introTimeLimitValue;
    public TMP_Text introLegalOccupancyValue;
    public TMP_Text introIllegalOccupancyValue;
    public TMP_Text introSmartFeeValue;
    public TMP_Text introIllegalFeeValue;
    public TMP_Text introCitationValue;

    [Header("UI - Scenario intro cost/fine table")]
    public TMP_Text introFreeParkingRateValue;
    public TMP_Text introFreeParkingTotalValue;

    [Tooltip("Optional row label. Displays No Parking and Driveway Entrance together because they use the same fine in this study.")]
    public TMP_Text introNoParkingFineTypeLabel;

    public TMP_Text introNoParkingFineRateValue;
    public TMP_Text introNoParkingFineTotalValue;
    public TMP_Text introNoStoppingFineRateValue;
    public TMP_Text introNoStoppingFineTotalValue;

    public TMP_Text introInstructionLabel;

    [Tooltip("Assign the parent GameObject of the university destination arrow.")]
    public GameObject destinationArrowRoot;

    [Header("Assisted Mode UI")]
    public GameObject assistedModeUIRoot;

    [Header("Assisted Mode Opening")]
    [Tooltip("Parent panel containing the semi-transparent background and AssistedOpeningMessageLabel.")]
    public GameObject assistedOpeningMessagePanel;

    [Tooltip("RTL/TMP text child inside AssistedOpeningMessagePanel.")]
    public TMP_Text assistedOpeningMessageLabel;

    [Tooltip("Optional close button inside AssistedOpeningMessagePanel.")]
    public Button assistedOpeningMessageCloseButton;

    [Min(0f)]
    [Tooltip("How long the free-search instruction remains visible after the map overview appears.")]
    public float assistedOverviewMessageSeconds = 10f;

    [Header("Mode-Specific UI")]
    public GameObject conventionalDrivingButtonsRoot;

    [Header("Conventional driving speed")]
    [Tooltip("Three-position 30/40/50 km/h speed selector. Normally place SpeedSelectorUI on SpeedControlPanel under DrivingButtons.")]
    public SpeedSelectorUI speedSelectorUI;

    [Tooltip("Recommended for the study: every new Conventional round starts from the same 50 km/h starting speed instead of carrying the previous round's choice forward.")]
    public bool resetSpeedAtStartOfRound = true;

    [Header("UI - Conventional driving information")]
    [Tooltip("Optional parent containing the live driving-information labels.")]
    public GameObject conventionalDrivingInfoRoot;

    [Tooltip("Shows total distance actually driven in the current round.")]
    public TMP_Text drivingDistanceLabel;

    [Tooltip("Shows time during which the car was physically moving.")]
    public TMP_Text drivingTimeLabel;

    [Tooltip("Shows the remaining automatic route after a parking spot is confirmed.")]
    public TMP_Text remainingDrivingDistanceLabel;

    [Tooltip("Shows the walking distance from the currently confirmed parking spot to Gate 2.")]
    public TMP_Text selectedWalkingDistanceLabel;

    [Header("Round result UI")]
    public GameObject resultPanel;
    public TMP_Text resultTitleLabel;
    public TMP_Text resultDetailLabel;
    public TMP_Text resultScoreLabel;

    [Tooltip("Optional. Assign ResultContinueButton so it can be hidden on the final survey screen.")]
    public GameObject resultContinueButton;

    [Tooltip("Optional. Assign the RTL text child of ResultContinueButton.")]
    public TMP_Text resultContinueButtonLabel;

    [Header("UI - Final questionnaire")]
    [Tooltip("Required for data collection. Shown after the final scenario and before the completion screen.")]
    public FinalQuestionnaire finalQuestionnaire;

    [Header("Automatic UI binding")]
    [Tooltip("When enabled, missing UI references are found automatically by their GameObject names. This is useful after replacing normal TMP components with RTLTextMeshPro.")]
    public bool autoFindUIReferences = true;

    [Tooltip("Forces TMP/RTLTMPro to rebuild immediately after runtime text changes.")]
    public bool forceRefreshTextMesh = true;

    [Header("UI - Score bar and stars")]
    public TMP_Text scoreLabel;
    public Slider scoreBar;
    public Image star1Image;
    public Image star2Image;
    public Image star3Image;
    public Color starOffColor = Color.gray;
    public Color starOnColor = Color.yellow;

    [Header("Per-scenario scoring - 1405 pilot")]
    [Tooltip("Every study round starts with this score. Keep this at 100 for the pilot.")]
    public float startingScore = 100f;

    // Kept serialized for compatibility with older scene data and the existing
    // ParkingScoreCalculator signature. The actual time-expiry deduction is now
    // the fixed lateTimePenaltyPoints value below; the score never rises above
    // startingScore.
    [HideInInspector] public int onTimeBasePoints = 100;
    [HideInInspector] public int lateBasePoints = 0;

    [Tooltip("Money-to-point conversion used for LEGAL parking costs. Illegal parking fines use the separate illegalFineTomanPerPoint scale below.")]
    [Min(1f)] public float tomanPerPoint = 1000f;

    [Header("Illegal parking score penalties")]
    [Tooltip("For a fine-bearing illegal-parking choice, every 4,000 tomans of the applicable fine removes one point whether or not a citation occurs. The result is rounded down: 200,000 = 50 points, 100,000 = 25 points, 50,000 = 12 points.")]
    [Min(1f)] public float illegalFineTomanPerPoint = 4000f;

    [Tooltip("Fixed score deduction for illegal parking types that deliberately have no monetary fine, such as near an intersection or in front of a pedestrian crossing.")]
    [Min(0)] public int noFineIllegalPenaltyPoints = 10;

    [Header("Overtime parking")]
    [Tooltip("Extra time allowed after the normal timer reaches zero. During this period the timer is shown as negative time.")]
    [Min(0f)] public float overtimeParkingMinutes = 0.5f;

    [Tooltip("Fixed one-time score deduction when the normal scenario time expires. This is the same for every scenario, regardless of dwell time or parking cost.")]
    [Min(0)] public int lateTimePenaltyPoints = 12;

    [Header("UI - Overtime warning")]
    [Min(0f)]
    [Tooltip("How long the overtime warning remains visible in AssistedOpeningMessagePanel. The overtime countdown continues while the message is shown.")]
    public float overtimeWarningDisplaySeconds = 4f;

    [Header("Star targets as proportions of the per-scenario maximum score")]
    [Range(0f, 1f)] public float star1TargetRatio = 0.50f;
    [Range(0f, 1f)] public float star2TargetRatio = 0.70f;
    [Range(0f, 1f)] public float star3TargetRatio = 0.85f;

    [Header("Score outputs")]
    [Tooltip("Visible score for the current round only. Resets to startingScore before every study round.")]
    [HideInInspector] public float currentScore;

    [Tooltip("Hidden sum of final per-round scores. Used for the final survey total and session CSV FinalScore.")]
    [HideInInspector] public float cumulativeScore;

    [Tooltip("Hidden sum of final Conventional round scores.")]
    [HideInInspector] public float conventionalScore;

    [Tooltip("Hidden sum of final Assisted round scores.")]
    [HideInInspector] public float assistedScore;

    [HideInInspector] public float star1Threshold;
    [HideInInspector] public float star2Threshold;
    [HideInInspector] public float star3Threshold;

    [Header("Camera")]
    public AssistedMapCameraController assistedMapCamera;

    private ParkingScenario currentScenario;
    private float scenarioStartTime;
    private bool scenarioRunning;
    private bool waitingForContinue;
    private bool waitingForScenarioStart;
    private bool surveyCompleted;
    private bool latePenaltyApplied;
    private float scoreAtRoundStart;
    private float timeScaleBeforeIntro = 1f;
    private CanvasGroup destinationArrowCanvasGroup;
    private Coroutine assistedOpeningMessageCoroutine;
    private bool overtimeWarningShown;

    // ORIGINAL indices into the Scenarios list, in participant presentation order.
    private readonly List<int> scenarioPresentationOrder = new List<int>();
    private int currentScenarioPresentationIndex = 0;

    // One pre-choice occupancy snapshot per scenario. It is created before the
    // first mode is played and restored before the paired mode is played.
    // ParkingSpot object references are used deliberately so the same physical
    // spaces are restored even if a Spot ID is regenerated later.
    private readonly Dictionary<int, List<PairedSpotOccupancyState>>
        pairedOccupancySnapshots =
            new Dictionary<int, List<PairedSpotOccupancyState>>();

    // Tutorial Conventional and Assisted practice should demonstrate the same
    // parking availability. This snapshot is separate from the research
    // scenario snapshots so practice never affects collected data.
    private List<PairedSpotOccupancyState> tutorialOccupancySnapshot;

    private sealed class PairedSpotOccupancyState
    {
        public ParkingSpot spot;
        public bool occupied;
    }

    [Header("Tutorial")]
    public bool waitForTutorial = true;
    public TutorialManager tutorialManager;

    [Header("Quick Guide")]
    [Tooltip("Shows the short driving/map instruction panel for the current mode.")]
    public QuickGuideManager quickGuideManager;

    [HideInInspector]
    public bool isPracticeRound = false;

    private void Awake()
    {
        ApplyPolicy1405RuntimeMigrationDefaults();

        if (autoFindUIReferences)
            AutoBindUIReferences();
    }


    private void ApplyPolicy1405RuntimeMigrationDefaults()
    {
        // Existing Unity scenes keep old serialized Inspector values after a
        // script file is replaced. Migrate the exact old pilot defaults at
        // runtime so the new per-scenario score is not silently bypassed.
        if (startingScore <= 0f || Mathf.Approximately(startingScore, 600f))
            startingScore = 100f;

        // Older scene serialization may still contain the previous 3-minute
        // overtime value. Migrate that exact old default to the new 30-second
        // study grace period without overriding any other custom value.
        if (Mathf.Approximately(overtimeParkingMinutes, 3f))
            overtimeParkingMinutes = 0.5f;

        // Migrate the previous fixed overtime penalty to the newly requested
        // 12-point value. This keeps existing serialized scenes consistent.
        if (lateTimePenaltyPoints <= 0 || lateTimePenaltyPoints == 15)
            lateTimePenaltyPoints = 12;

        if (illegalFineTomanPerPoint <= 0f)
            illegalFineTomanPerPoint = 4000f;

        if (noFineIllegalPenaltyPoints <= 0)
            noFineIllegalPenaltyPoints = 10;

        if (string.Equals(studyVersion, "1.1-pilot-score", StringComparison.Ordinal) ||
            string.Equals(studyVersion, "1.2-pilot-policy1405", StringComparison.Ordinal) ||
            string.Equals(studyVersion, "1.3-pilot-scenario-score", StringComparison.Ordinal) ||
            string.Equals(studyVersion, "1.4-pilot-fine-policy", StringComparison.Ordinal))
        {
            studyVersion = "2.0-release";
        }

        if (string.Equals(buildVersion, "1.1.5", StringComparison.Ordinal) ||
            string.Equals(buildVersion, "1.2.0", StringComparison.Ordinal) ||
            string.Equals(buildVersion, "1.3.0", StringComparison.Ordinal) ||
            string.Equals(buildVersion, "1.4.1-PILOT", StringComparison.Ordinal) ||
            string.Equals(buildVersion, "1.4.2", StringComparison.Ordinal) ||
            string.Equals(buildVersion, "1.4.3", StringComparison.Ordinal) ||
            string.Equals(buildVersion, "2.0.0", StringComparison.Ordinal) ||
            string.Equals(buildVersion, "2.0.1", StringComparison.Ordinal) ||
            string.Equals(buildVersion, "2.0.2", StringComparison.Ordinal))
        {
            buildVersion = "2.0.3-balanced-blocks";
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && autoFindUIReferences)
            AutoBindUIReferences();
    }
#endif

    private void Start()
    {
        // Rebind once more at runtime. References may have been lost when
        // TextMeshProUGUI components were replaced with RTLTextMeshPro.
        if (autoFindUIReferences)
            AutoBindUIReferences();

        ValidateRequiredUIReferences();
        if (ParticipantSession.Instance != null &&
            !string.IsNullOrWhiteSpace(ParticipantSession.Instance.participantId))
        {
            participantId = ParticipantSession.Instance.participantId.Trim().ToUpperInvariant();
            Debug.Log("[ScenarioManager] Using participantId = " + participantId);
        }

        if (carParking != null)
            carParking.scenarioManager = this;

        if (walkingNetworkCalculator == null)
            walkingNetworkCalculator = FindFirstObjectByType<WalkingNetworkCalculator>();

        if (parkingViewTracker == null)
            parkingViewTracker = FindFirstObjectByType<ParkingViewTracker>();

        currentScore = startingScore;
        cumulativeScore = 0f;
        conventionalScore = 0f;
        assistedScore = 0f;
        latePenaltyApplied = false;
        surveyCompleted = false;
        scenarioRunning = false;
        waitingForContinue = false;
        waitingForScenarioStart = false;

        ConfigureScoreScale();

        if (resultPanel != null)
            resultPanel.SetActive(false);

        if (scenarioIntroPanel != null)
            scenarioIntroPanel.SetActive(false);

        HideAssistedOpeningMessage();
        HideOvertimeWarning();

        if (assistedOpeningMessageCloseButton != null)
        {
            assistedOpeningMessageCloseButton.onClick.RemoveListener(HideAssistedOpeningMessage);
            assistedOpeningMessageCloseButton.onClick.AddListener(HideAssistedOpeningMessage);
        }

        if (finalQuestionnaire != null)
            finalQuestionnaire.Hide();

        UpdateScoreUI();

        if (waitForTutorial)
        {
            scenarioRunning = false;
            SetTutorialInputEnabled(false);
        }
        else
        {
            BeginMainSurvey();
        }
    }

    private void OnDestroy()
    {
        if (finalQuestionnaire != null)
            finalQuestionnaire.Submitted -= OnFinalQuestionnaireSubmitted;
    }

    public int GetTotalRoundCount()
    {
        EnsureScenarioPresentationOrder();

        int scenarioCount = scenarioPresentationOrder.Count;

        int total = runConventionalAndAssistedForEachScenario
            ? scenarioCount * 2
            : scenarioCount;

        if (maxRoundsForTesting > 0)
            total = Mathf.Min(total, maxRoundsForTesting);

        return total;
    }

    private void EnsureScenarioPresentationOrder()
    {
        if (scenarioPresentationOrder.Count == 0)
            BuildScenarioPresentationOrder();
    }

    private void BuildScenarioPresentationOrder()
    {
        scenarioPresentationOrder.Clear();

        int scenarioCount = scenarios != null ? scenarios.Count : 0;

        if (scenarioCount <= 0)
        {
            assignedScenarioBlock = useScenarioBlocks ? "UNRESOLVED" : "ALL";
            scenarioOrderLabel = "EMPTY";
            scenarioOrderSeed = 0;
            return;
        }

        assignedScenarioBlock = DetermineScenarioBlock();

        if (useScenarioBlocks)
        {
            string[] selectedIds = assignedScenarioBlock == "B"
                ? blockBScenarioIds
                : blockAScenarioIds;

            HashSet<string> selectedSet = new HashSet<string>(
                selectedIds ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase
            );

            for (int index = 0; index < scenarioCount; index++)
            {
                ParkingScenario scenario = scenarios[index];

                if (scenario != null &&
                    !string.IsNullOrWhiteSpace(scenario.scenarioId) &&
                    selectedSet.Contains(scenario.scenarioId.Trim()))
                {
                    scenarioPresentationOrder.Add(index);
                }
            }

            int expectedCount = selectedSet.Count;

            if (scenarioPresentationOrder.Count != expectedCount)
            {
                Debug.LogError(
                    $"[ScenarioManager] BLOCK {assignedScenarioBlock} SETUP ERROR | " +
                    $"Expected {expectedCount} scenario IDs but found {scenarioPresentationOrder.Count} matching assets in the Scenarios list. " +
                    "Keep all S01-S12 assets assigned and make sure every scenarioId is correct."
                );
            }
        }
        else
        {
            assignedScenarioBlock = "ALL";

            for (int i = 0; i < scenarioCount; i++)
                scenarioPresentationOrder.Add(i);
        }

        if (scenarioPresentationOrder.Count <= 0)
        {
            scenarioOrderLabel = "EMPTY_BLOCK_" + assignedScenarioBlock;
            scenarioOrderSeed = 0;
            return;
        }

        scenarioOrderSeed = BuildStableScenarioOrderSeed();

        if (randomizeScenarioOrder && scenarioPresentationOrder.Count > 1)
        {
            // Separate RNG so shuffling does not change the occupancy RNG stream.
            System.Random rng = new System.Random(scenarioOrderSeed);

            for (int i = scenarioPresentationOrder.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int temp = scenarioPresentationOrder[i];
                scenarioPresentationOrder[i] = scenarioPresentationOrder[j];
                scenarioPresentationOrder[j] = temp;
            }
        }

        List<string> orderedScenarioIds =
            new List<string>(scenarioPresentationOrder.Count);

        foreach (int originalIndex in scenarioPresentationOrder)
        {
            ParkingScenario scenario =
                originalIndex >= 0 && originalIndex < scenarios.Count
                    ? scenarios[originalIndex]
                    : null;

            orderedScenarioIds.Add(
                scenario != null && !string.IsNullOrWhiteSpace(scenario.scenarioId)
                    ? scenario.scenarioId
                    : "NULL"
            );
        }

        scenarioOrderLabel =
            "Block=" + assignedScenarioBlock +
            "|source=" + scenarioBlockAssignmentSource +
            "|" +
            (randomizeScenarioOrder ? "Randomized" : "Fixed") +
            "|seed=" +
            scenarioOrderSeed.ToString(CultureInfo.InvariantCulture) +
            "|" +
            string.Join(">", orderedScenarioIds);

        Debug.Log(
            "[ScenarioManager] Scenario order prepared | " +
            scenarioOrderLabel
        );
    }

    private string DetermineScenarioBlock()
    {
        if (!useScenarioBlocks)
            return "ALL";

        if (scenarioBlockSelection == ScenarioBlockSelection.A)
            return "A";

        if (scenarioBlockSelection == ScenarioBlockSelection.B)
            return "B";

        // Automatic assignment is normally resolved from Supabase before the
        // research survey starts. Returning a resolved value here prevents any
        // accidental re-assignment while building the presentation order.
        if (assignedScenarioBlock == "A" || assignedScenarioBlock == "B")
            return assignedScenarioBlock;

        return DetermineScenarioBlockLocalFallback();
    }

    private string DetermineScenarioBlockLocalFallback()
    {
        string id = participantId ?? string.Empty;

        if (ParticipantSession.Instance != null &&
            !string.IsNullOrWhiteSpace(ParticipantSession.Instance.participantId))
        {
            id = ParticipantSession.Instance.participantId.Trim();
        }

        // Deterministic emergency fallback only. The main study should normally
        // receive its assignment from Supabase.
        unchecked
        {
            int hash = 17;
            string source = id.Trim().ToUpperInvariant();

            for (int i = 0; i < source.Length; i++)
                hash = (hash * 31) + source[i];

            return ((hash & 1) == 0) ? "A" : "B";
        }
    }

    private int BuildStableScenarioOrderSeed()
    {
        string sessionIdentity = string.Empty;

        if (ParticipantSession.Instance != null)
        {
            ParticipantSession.Instance.EnsureSessionIdentity();
            sessionIdentity =
                (ParticipantSession.Instance.sessionId ?? string.Empty) +
                "|" +
                (ParticipantSession.Instance.participantId ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(sessionIdentity))
        {
            sessionIdentity =
                participantId +
                "|" +
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        string source =
            sessionIdentity +
            "|" + studyVersion +
            "|" + buildVersion +
            "|block=" + assignedScenarioBlock +
            "|selected=" +
            scenarioPresentationOrder.Count.ToString(CultureInfo.InvariantCulture);

        unchecked
        {
            int hash = 17;
            for (int i = 0; i < source.Length; i++)
                hash = (hash * 31) + source[i];
            if (hash == int.MinValue)
                return 0;
            return Mathf.Abs(hash);
        }
    }

    private int GetCurrentScenarioDisplayNumber()
    {
        if (isPracticeRound)
            return 0;

        return Mathf.Max(0, currentScenarioPresentationIndex) + 1;
    }

    private float GetMaximumPossibleScore()
    {
        // The visible score is now per-round, so its maximum is simply the
        // common starting score rather than a survey-wide accumulated maximum.
        return Mathf.Max(1f, startingScore);
    }

    public int GetOnTimeRewardPoints(ParkingScenario scenario)
    {
        // Legacy method name kept for ParkingScenario / ParkingScoreCalculator
        // compatibility. Time-expiry scoring is now deliberately independent
        // of dwell time and smart-parking cost.
        return scenario == null ? 0 : GetLateTimePenaltyPoints();
    }

    public int GetLateTimePenaltyPoints()
    {
        return Mathf.Max(0, lateTimePenaltyPoints);
    }

    public int GetCurrentSmartParkingFeeToman()
    {
        return currentScenario == null
            ? 0
            : ParkingPolicy1405.CalculateSmartParkingFeeToman(
                currentScenario.dwellTimeMinutes
            );
    }

    public float GetCurrentIllegalFineMultiplier()
    {
        return currentScenario == null
            ? 1f
            : currentScenario.GetIllegalFineMultiplier();
    }

    public int GetCurrentNoParkingFineToman()
    {
        return ParkingPolicy1405.GetNoParkingFineToman(
            GetCurrentIllegalFineMultiplier()
        );
    }

    public int GetCurrentNoStoppingFineToman()
    {
        return ParkingPolicy1405.GetNoStoppingFineToman(
            GetCurrentIllegalFineMultiplier()
        );
    }

    public ParkingScenario GetCurrentScenario()
    {
        return currentScenario;
    }

    public float GetCurrentAdjustedCitationProbability()
    {
        return currentScenario == null
            ? 0f
            : ParkingScoreCalculator.CalculateAdjustedCitationProbability(
                currentScenario.citationProbability,
                currentScenario.dwellTimeMinutes
            );
    }

    private void ConfigureScoreScale()
    {
        float maximum = GetMaximumPossibleScore();

        star1Threshold = maximum * Mathf.Clamp01(star1TargetRatio);
        star2Threshold = maximum * Mathf.Clamp01(star2TargetRatio);
        star3Threshold = maximum * Mathf.Clamp01(star3TargetRatio);

        if (scoreBar != null)
        {
            scoreBar.minValue = 0f;
            scoreBar.maxValue = maximum;
            scoreBar.value = Mathf.Clamp(currentScore, 0f, maximum);
        }
    }

    public string GetCurrentModeName()
    {
        return currentAssistedMode ? "Assisted" : "Conventional";
    }

    /// <summary>
    /// True only while the participant is actively playing a round.
    /// Assisted start-point UI can use this to remain visible during gameplay
    /// but stay hidden behind scenario intros and result/final screens.
    /// </summary>
    public bool IsScenarioGameplayActive
    {
        get
        {
            return scenarioRunning &&
                   !waitingForContinue &&
                   !waitingForScenarioStart &&
                   !surveyCompleted;
        }
    }

    private string GetCurrentModePersianName()
    {
        return currentAssistedMode ? "کمکی" : "متعارف";
    }

    public void BeginMainSurvey()
    {
        if (blockAssignmentRequestInProgress)
            return;

        if (!useScenarioBlocks || scenarioBlockSelection != ScenarioBlockSelection.Automatic)
        {
            assignedScenarioBlock = DetermineScenarioBlock();
            scenarioBlockAssignmentSource = !useScenarioBlocks ? "Disabled" : "Manual";
            blockAssignmentResolved = true;
            BeginMainSurveyAfterBlockAssignment();
            return;
        }

        if (blockAssignmentResolved &&
            (assignedScenarioBlock == "A" || assignedScenarioBlock == "B"))
        {
            BeginMainSurveyAfterBlockAssignment();
            return;
        }

        StartCoroutine(ResolveBlockThenBeginMainSurvey());
    }

    private IEnumerator ResolveBlockThenBeginMainSurvey()
    {
        blockAssignmentRequestInProgress = true;
        SetTutorialInputEnabled(false);

        string resolvedBlock = null;
        string error = null;

        if (useSupabaseBalancedBlockAssignment)
        {
            int attempts = Mathf.Max(1, blockAssignmentMaxAttempts);

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                bool finished = false;
                string attemptBlock = null;
                string attemptError = null;

                yield return SupabaseBlockAssignmentClient.AssignBlock(
                    participantId,
                    GetCurrentSessionIdForBlockAssignment(),
                    studyVersion,
                    buildVersion,
                    (block, requestError) =>
                    {
                        attemptBlock = block;
                        attemptError = requestError;
                        finished = true;
                    }
                );

                if (finished && (attemptBlock == "A" || attemptBlock == "B"))
                {
                    resolvedBlock = attemptBlock;
                    error = null;
                    break;
                }

                error = string.IsNullOrWhiteSpace(attemptError)
                    ? "Supabase returned no valid block."
                    : attemptError;

                Debug.LogWarning(
                    $"[ScenarioManager] Supabase block assignment attempt {attempt}/{attempts} failed: {error}"
                );

                if (attempt < attempts)
                    yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, blockAssignmentRetryDelaySeconds));
            }
        }

        if (resolvedBlock == "A" || resolvedBlock == "B")
        {
            assignedScenarioBlock = resolvedBlock;
            scenarioBlockAssignmentSource = "Supabase";
            blockAssignmentResolved = true;
        }
        else if (!requireSuccessfulSupabaseBlockAssignment)
        {
            assignedScenarioBlock = DetermineScenarioBlockLocalFallback();
            scenarioBlockAssignmentSource = "LocalFallback";
            blockAssignmentResolved = true;

            Debug.LogWarning(
                "[ScenarioManager] Supabase block assignment failed. " +
                $"Using local fallback block {assignedScenarioBlock}. Error: {error}"
            );
        }
        else
        {
            blockAssignmentRequestInProgress = false;
            blockAssignmentResolved = false;
            assignedScenarioBlock = "UNRESOLVED";
            scenarioBlockAssignmentSource = "Failed";

            Debug.LogError(
                "[ScenarioManager] MAIN STUDY NOT STARTED because balanced block assignment could not be obtained from Supabase. " +
                "Check the internet connection / Supabase configuration and retry. " +
                $"Last error: {error}"
            );

            yield break;
        }

        blockAssignmentRequestInProgress = false;

        Debug.Log(
            $"[ScenarioManager] Scenario block assigned | block={assignedScenarioBlock} | source={scenarioBlockAssignmentSource}"
        );

        BeginMainSurveyAfterBlockAssignment();
    }

    private string GetCurrentSessionIdForBlockAssignment()
    {
        if (ParticipantSession.Instance != null)
        {
            ParticipantSession.Instance.EnsureSessionIdentity();
            return ParticipantSession.Instance.sessionId ?? string.Empty;
        }

        return participantId + "_" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
    }

    private void BeginMainSurveyAfterBlockAssignment()
    {
        isPracticeRound = false;
        waitingForContinue = false;
        waitingForScenarioStart = false;
        currentScore = startingScore;
        cumulativeScore = 0f;
        conventionalScore = 0f;
        assistedScore = 0f;
        latePenaltyApplied = false;
        overtimeWarningShown = false;
        HideOvertimeWarning();

        // A new survey session must generate its own scenario occupancies
        // and its own scenario presentation order.
        pairedOccupancySnapshots.Clear();
        tutorialOccupancySnapshot = null;
        BuildScenarioPresentationOrder();

        if (resultPanel != null)
            resultPanel.SetActive(false);

        UpdateScoreUI();

        if (scenarios.Count == 0)
        {
            Debug.LogWarning("[ScenarioManager] No main scenarios assigned.");
            return;
        }

        // The normal round first opens ScenarioIntroPanel. Gameplay input is
        // enabled only after the participant presses its start button.
        SetTutorialInputEnabled(false);
        StartRound(0);
    }

    public void StartPracticeRound(
        ParkingScenario practiceScenario,
        bool assistedMode)
    {
        if (practiceScenario == null)
        {
            Debug.LogError("[ScenarioManager] Practice scenario is missing.");
            return;
        }

        isPracticeRound = true;
        waitingForContinue = false;
        waitingForScenarioStart = false;

        currentScenarioIndex = -1;
        currentRoundIndex = -1;
        currentAssistedMode = assistedMode;
        currentScenario = practiceScenario;

        ResetDrivingSpeedForRoundIfNeeded();
        UpdateQuickGuideMode();

        // Allows other gameplay scripts to read the current mode.
        currentScenario.assistedMode = assistedMode;
        currentScenario.onTimeReward = GetOnTimeRewardPoints(currentScenario);

        // Tutorial practice now uses the same pre-scenario information
        // panel as the main survey. Keep all gameplay input disabled until
        // the participant reads the panel and presses its Start button.
        SetTutorialInputEnabled(false);

        if (resultPanel != null)
            resultPanel.SetActive(false);

        ApplyTutorialOccupancy(assistedMode);

        ResetCarToStart();

        if (carParking != null)
            carParking.ResetForNewScenario();

        // Practice is not logged, but showing the same 100-point starting state
        // keeps the tutorial visually consistent with the main survey.
        currentScore = startingScore;
        scoreAtRoundStart = currentScore;
        latePenaltyApplied = false;
        overtimeWarningShown = false;
        HideOvertimeWarning();
        scenarioRunning = false;

        UpdateScenarioInfoUI();
        UpdateScoreUI();
        UpdateDrivingInfoUI();

        // Show the same ScenarioIntroPanel used before the research rounds.
        // OnScenarioStartButton() will close it and begin the practice round.
        ShowScenarioIntroPanel();

        Debug.Log(
            "[ScenarioManager] Practice intro shown: " +
            (assistedMode ? "Assisted" : "Conventional")
        );
    }

    public void SetTutorialInputEnabled(bool enabled)
    {
        if (carParking != null)
            carParking.enableClickParking = enabled;

        // Speed is a Conventional-driving control. Keep the selected value
        // visible, but block changes during intro/result/Assisted states.
        if (speedSelectorUI != null)
            speedSelectorUI.SetInteractable(enabled && !currentAssistedMode);

        Transform targetCar = carTransform;

        if (targetCar == null && carParking != null)
            targetCar = carParking.transform;

        if (targetCar == null)
            return;

        CarNodeFollower follower =
            targetCar.GetComponent<CarNodeFollower>();

        if (follower != null)
            follower.enabled = enabled && !currentAssistedMode;
    }

    public void StartRound(int roundIndex)
    {
        isPracticeRound = false;

        if (surveyCompleted)
            return;

        int totalRounds = GetTotalRoundCount();

        if (roundIndex < 0 || roundIndex >= totalRounds)
        {
            Debug.LogError("[ScenarioManager] Round index is out of range.");
            return;
        }

        waitingForContinue = false;
        waitingForScenarioStart = false;
        currentRoundIndex = roundIndex;

        EnsureScenarioPresentationOrder();

        if (runConventionalAndAssistedForEachScenario)
        {
            // Round 0/1 use the first RANDOMISED scenario asset,
            // round 2/3 use the second, etc. The pair stays together.
            currentScenarioPresentationIndex = roundIndex / 2;

            if (currentScenarioPresentationIndex < 0 ||
                currentScenarioPresentationIndex >= scenarioPresentationOrder.Count)
            {
                Debug.LogError("[ScenarioManager] Scenario presentation index is out of range.");
                return;
            }

            currentScenarioIndex =
                scenarioPresentationOrder[currentScenarioPresentationIndex];

            bool secondModeOfPair = roundIndex % 2 == 1;
            currentAssistedMode = conventionalFirst
                ? secondModeOfPair
                : !secondModeOfPair;
        }
        else
        {
            currentScenarioPresentationIndex = roundIndex;

            if (currentScenarioPresentationIndex < 0 ||
                currentScenarioPresentationIndex >= scenarioPresentationOrder.Count)
            {
                Debug.LogError("[ScenarioManager] Scenario presentation index is out of range.");
                return;
            }

            currentScenarioIndex =
                scenarioPresentationOrder[currentScenarioPresentationIndex];

            currentAssistedMode =
                scenarios[currentScenarioIndex].assistedMode;
        }

        if (currentScenarioIndex < 0 || currentScenarioIndex >= scenarios.Count)
        {
            Debug.LogError("[ScenarioManager] Scenario index is out of range.");
            return;
        }

        currentScenario = scenarios[currentScenarioIndex];

        UpdateQuickGuideMode();

        if (currentScenario == null)
        {
            Debug.LogError("[ScenarioManager] The current ParkingScenario asset is missing.");
            return;
        }

        currentScenario.assistedMode = currentAssistedMode;

        ResetDrivingSpeedForRoundIfNeeded();

        // No map/car input should remain active while the scenario information
        // panel is being shown. Gameplay is enabled again only when the player
        // presses the scenario Start button.
        SetTutorialInputEnabled(false);

        // Keep the legacy scenario/logger field aligned with the new fixed
        // time-expiry penalty used for every study scenario.
        currentScenario.onTimeReward = GetOnTimeRewardPoints(currentScenario);

        ApplyCurrentMode();
        ResetAllParkingSpots();

        // IMPORTANT FOR THE EXPERIMENT:
        // When a scenario is presented once in Conventional and once in
        // Assisted mode, both presentations must contain the exact same
        // occupied/free physical parking spots. Only the information/search
        // mode changes.
        ApplyOccupancyForCurrentScenarioPair();

        if (resultPanel != null)
            resultPanel.SetActive(false);

        ResetCarToStart();

        if (carParking != null)
            carParking.ResetForNewScenario();

        // Every research round starts from the same score. This makes the
        // displayed score and the CSV ScoreBefore/ScoreAfter directly
        // comparable across scenarios and modes.
        currentScore = startingScore;
        scoreAtRoundStart = currentScore;
        latePenaltyApplied = false;
        overtimeWarningShown = false;
        HideOvertimeWarning();
        scenarioRunning = false;
        UpdateScenarioInfoUI();
        UpdateScoreUI();

        // Reset the visual timer bar for this round before the intro opens.
        // The actual countdown still starts only when participant control begins.
        if (timerBarUI != null)
        {
            timerBarUI.Initialize(
                Mathf.Max(0f, currentScenario.timeLimitMinutes) * 60f
            );
        }

        ShowScenarioIntroPanel();

        Debug.Log(
            $"[ScenarioManager] Starting round {currentRoundIndex + 1}/{totalRounds} | " +
            $"displayScenario={GetCurrentScenarioDisplayNumber()} | " +
            $"assetScenario={currentScenario.scenarioId} | " +
            $"assetIndex={currentScenarioIndex} | mode={GetCurrentModeName()} | " +
            $"order={scenarioOrderLabel}"
        );
    }

    private void ShowScenarioIntroPanel()
    {
        if (currentScenario == null)
            return;

        if (autoFindUIReferences)
            AutoBindUIReferences();

        if (scenarioIntroPanel == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] ScenarioIntroPanel was not found. " +
                "The round will start immediately."
            );
            BeginCurrentScenarioGameplay();
            return;
        }

        waitingForScenarioStart = true;
        HideAssistedOpeningMessage();
        HideOvertimeWarning();

        scenarioIntroPanel.SetActive(true);
        scenarioIntroPanel.transform.SetAsLastSibling();

        string introTitle;

        if (isPracticeRound)
        {
            introTitle = currentAssistedMode
                ? "مرحله تمرینی ۲ از ۲"
                : "مرحله تمرینی ۱ از ۲";
        }
        else
        {
            introTitle =
                "سناریو " +
                ToPersianDigits(
                    GetCurrentScenarioDisplayNumber()
                        .ToString(CultureInfo.InvariantCulture)
                );
        }

        SetLabelText(introTitleLabel, introTitle);
        SetLabelText(introModeLabel, "حالت: " + GetCurrentModePersianName());

        int smartParkingFeeToman = GetCurrentSmartParkingFeeToman();
        int noParkingFineToman = GetCurrentNoParkingFineToman();
        int noStoppingFineToman = GetCurrentNoStoppingFineToman();

        // Scenario characteristics table.
        SetLabelText(
            introDwellValue,
            FormatParkingDuration(currentScenario.dwellTimeMinutes)
        );
        SetLabelText(
            introTimeLimitValue,
            FormatNumber(currentScenario.timeLimitMinutes) + " دقیقه"
        );
        SetLabelText(
            introLegalOccupancyValue,
            FormatPercent(currentScenario.legalOccupancyRate)
        );
        SetLabelText(
            introIllegalOccupancyValue,
            FormatPercent(currentScenario.illegalOccupancyRate)
        );
        SetLabelText(
            introSmartFeeValue,
            FormatToman(smartParkingFeeToman)
        );
        SetLabelText(
            introIllegalFeeValue,
            ParkingPolicy1405.GetFinePolicyPersianLabel(
                currentScenario.illegalFinePolicy
            )
        );
        SetLabelText(
            introCitationValue,
            FormatCitationProbabilityWithLevel(
                GetCurrentAdjustedCitationProbability()
            )
        );

        // Cost/fine table.
        // DrivewayEntrance is intentionally grouped with NoParkingSign because
        // both use the exact same monetary fine in the study policy.
        SetLabelText(
            introNoParkingFineTypeLabel,
            "پارک ممنوع / جلوی ورودی پارکینگ"
        );

        // These fines are flat citation amounts in the current scoring logic,
        // not hourly charges. Therefore the displayed total for the parking
        // duration is the same amount as the base citation fine.
        SetLabelText(introFreeParkingRateValue, "رایگان");
        SetLabelText(introFreeParkingTotalValue, "رایگان");

        SetLabelText(
            introNoParkingFineRateValue,
            FormatToman(noParkingFineToman)
        );
        SetLabelText(
            introNoParkingFineTotalValue,
            FormatToman(noParkingFineToman)
        );

        SetLabelText(
            introNoStoppingFineRateValue,
            FormatToman(noStoppingFineToman)
        );
        SetLabelText(
            introNoStoppingFineTotalValue,
            FormatToman(noStoppingFineToman)
        );

        SetLabelText(
            introInstructionLabel,
            currentAssistedMode
                ? "در این حالت، همه گزینه‌های پارک در نقشه قابل مشاهده و انتخاب هستند."
                : "در این حالت، فقط گزینه‌های اطراف موقعیت فعلی خودرو قابل مشاهده‌اند و باید برای یافتن محل پارک در شبکه حرکت کنید."
        );

        if (assistedModeUIRoot != null)
            assistedModeUIRoot.SetActive(false);

        if (conventionalDrivingButtonsRoot != null)
            conventionalDrivingButtonsRoot.SetActive(false);

        if (conventionalDrivingInfoRoot != null)
            conventionalDrivingInfoRoot.SetActive(false);

        SetDestinationArrowVisible(false);

        timeScaleBeforeIntro = Time.timeScale;
        Time.timeScale = 0f;

        Canvas.ForceUpdateCanvases();
        RectTransform panelRect = scenarioIntroPanel.transform as RectTransform;
        if (panelRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }

    public void OnScenarioStartButton()
    {
        if (surveyCompleted || !waitingForScenarioStart || currentScenario == null)
            return;

        waitingForScenarioStart = false;

        if (scenarioIntroPanel != null)
            scenarioIntroPanel.SetActive(false);

        Time.timeScale = timeScaleBeforeIntro;
        BeginCurrentScenarioGameplay();
    }

    private void BeginCurrentScenarioGameplay()
    {
        ApplyCurrentMode();
        PrepareConventionalCarForScenarioStart();

        // Calculate sidewalk-route distances before the round timer and before
        // ParkingLogger freezes the alternative set for the CSV files.
        if (recalculateWalkingDistancesBeforeEachRound)
            RefreshWalkingNetworkDistances();

        if (currentAssistedMode &&
            assistedMapCamera != null &&
            assistedMapCamera.playOpeningSequence)
        {
            Transform targetCar = carTransform;

            if (targetCar == null && carParking != null)
                targetCar = carParking.transform;

            if (targetCar != null)
            {
                // The orientation animation is not part of the participant's
                // search time. Parking/map input remains disabled until the
                // overview is ready.
                scenarioRunning = false;
                SetTutorialInputEnabled(false);
                SetDestinationArrowVisible(false);

                ShowAssistedOpeningMessage(
                    "نقطه شروع شما"
                );

                assistedMapCamera.BeginAssistedOpening(
                    targetCar,
                    OnAssistedOverviewReady
                );

                return;
            }
        }

        StartInteractiveScenarioNow();
    }

    private void OnAssistedOverviewReady()
    {
        if (surveyCompleted ||
            waitingForScenarioStart ||
            currentScenario == null ||
            !currentAssistedMode)
        {
            return;
        }

        ShowAssistedOpeningMessage(
            "می‌توانید آزادانه در سراسر نقشه با زوم کردن جست‌وجو کنید و بدون رانندگی محل پارک موردنظر خود را انتخاب کنید.",
            assistedOverviewMessageSeconds
        );

        StartInteractiveScenarioNow();
    }

    private void StartInteractiveScenarioNow()
    {
        SetDestinationArrowVisible(true);
        SetTutorialInputEnabled(true);

        scenarioStartTime = Time.time;
        scenarioRunning = true;

        // Practice/tutorial rounds must never create research round logs.
        // Main rounds begin logging only when participant control begins, so
        // the automatic assisted opening animation is excluded from search time.
        if (!isPracticeRound)
        {
            TryBeginLoggerRound();

            // Start the observed-choice-set tracker AFTER ParkingLogger.BeginRound
            // so both systems share the same SessionId.
            BeginParkingViewTracking();
        }
    }

    private void ShowAssistedOpeningMessage(
        string message,
        float hideAfterSeconds = -1f)
    {
        if (assistedOpeningMessageCoroutine != null)
        {
            StopCoroutine(assistedOpeningMessageCoroutine);
            assistedOpeningMessageCoroutine = null;
        }

        if (assistedOpeningMessagePanel != null)
        {
            assistedOpeningMessagePanel.SetActive(true);
            assistedOpeningMessagePanel.transform.SetAsLastSibling();
        }

        if (assistedOpeningMessageLabel == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] AssistedOpeningMessageLabel is missing."
            );
            return;
        }

        // Keep the text child active even if it was disabled manually.
        if (!assistedOpeningMessageLabel.gameObject.activeSelf)
            assistedOpeningMessageLabel.gameObject.SetActive(true);

        SetLabelText(
            assistedOpeningMessageLabel,
            message
        );

        if (hideAfterSeconds > 0f)
        {
            assistedOpeningMessageCoroutine =
                StartCoroutine(
                    HideAssistedOpeningMessageAfterDelay(
                        hideAfterSeconds
                    )
                );
        }
    }

    private IEnumerator HideAssistedOpeningMessageAfterDelay(
        float seconds)
    {
        yield return new WaitForSecondsRealtime(
            Mathf.Max(0f, seconds)
        );

        assistedOpeningMessageCoroutine = null;
        HideAssistedOpeningMessage();
    }

    private void HideAssistedOpeningMessage()
    {
        if (assistedOpeningMessageCoroutine != null)
        {
            StopCoroutine(assistedOpeningMessageCoroutine);
            assistedOpeningMessageCoroutine = null;
        }

        if (assistedOpeningMessagePanel != null)
        {
            assistedOpeningMessagePanel.SetActive(false);
            return;
        }

        // Fallback for older scene setups that have only the label assigned.
        if (assistedOpeningMessageLabel != null)
            assistedOpeningMessageLabel.gameObject.SetActive(false);
    }

    private void RefreshWalkingNetworkDistances()
    {
        if (walkingNetworkCalculator == null)
            walkingNetworkCalculator = FindFirstObjectByType<WalkingNetworkCalculator>();

        if (walkingNetworkCalculator == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] WalkingNetworkCalculator was not found. " +
                "Walking distance will use the stored value or the straight-line fallback."
            );
            return;
        }

        walkingNetworkCalculator.metersPerUnityUnit = Mathf.Max(0f, metersPerUnityUnit);
        walkingNetworkCalculator.walkingSpeedMetersPerSecond =
            Mathf.Max(0.01f, walkingSpeedMetersPerSecond);

        walkingNetworkCalculator.RecalculateAllParkingSpots();
    }

    private void SetDestinationArrowVisible(bool visible)
    {
        if (destinationArrowRoot == null)
            return;

        // DestinationMarkerRoot contains BOTH:
        // DestinationArrow + DestinationArrowLabel.
        // Disable the whole root so world-space labels cannot remain visible.
        if (visible)
        {
            if (!destinationArrowRoot.activeSelf)
                destinationArrowRoot.SetActive(true);

            if (destinationArrowCanvasGroup == null)
            {
                destinationArrowCanvasGroup =
                    destinationArrowRoot.GetComponent<CanvasGroup>();

                if (destinationArrowCanvasGroup == null)
                {
                    destinationArrowCanvasGroup =
                        destinationArrowRoot.AddComponent<CanvasGroup>();
                }
            }

            destinationArrowCanvasGroup.alpha = 1f;
            destinationArrowCanvasGroup.interactable = true;
            destinationArrowCanvasGroup.blocksRaycasts = true;
        }
        else
        {
            if (destinationArrowCanvasGroup != null)
            {
                destinationArrowCanvasGroup.alpha = 0f;
                destinationArrowCanvasGroup.interactable = false;
                destinationArrowCanvasGroup.blocksRaycasts = false;
            }

            destinationArrowRoot.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        // Update still runs while Time.timeScale is zero. Reasserting this in
        // LateUpdate guarantees that an arrow controller cannot reveal it
        // during the scenario-information panel.
        if (waitingForScenarioStart)
            SetDestinationArrowVisible(false);
    }

    private void UpdateQuickGuideMode()
    {
        if (quickGuideManager == null)
            return;

        quickGuideManager.SetMode(currentAssistedMode);
    }

    /// <summary>
    /// Current participant-selected Conventional driving speed.
    /// Returns 50 km/h if the speed UI is not assigned, so gameplay code has
    /// a stable fallback.
    /// </summary>
    public int GetSelectedDrivingSpeedKmh()
    {
        return speedSelectorUI != null
            ? speedSelectorUI.CurrentSpeedKmh
            : 50;
    }

    public float GetSelectedDrivingSpeedMetersPerSecond()
    {
        return GetSelectedDrivingSpeedKmh() / 3.6f;
    }

    private void ResetDrivingSpeedForRoundIfNeeded()
    {
        if (speedSelectorUI == null)
            return;

        // Assisted mode has no conventional car-driving control. We leave the
        // selector hidden/disabled there and reset only actual driving rounds.
        if (!currentAssistedMode && resetSpeedAtStartOfRound)
            speedSelectorUI.SetSpeedKmh(50);

        speedSelectorUI.SetInteractable(false);
    }

    private void ApplyCurrentMode()
    {
        if (!currentAssistedMode)
            HideAssistedOpeningMessage();

        if (carParking != null)
            carParking.SetAssistedMode(currentAssistedMode);

        if (assistedMapCamera != null)
            assistedMapCamera.SetAssistedMode(currentAssistedMode);

        if (assistedModeUIRoot != null)
            assistedModeUIRoot.SetActive(currentAssistedMode);

        if (conventionalDrivingButtonsRoot != null)
            conventionalDrivingButtonsRoot.SetActive(!currentAssistedMode);

        if (conventionalDrivingInfoRoot != null)
            conventionalDrivingInfoRoot.SetActive(!currentAssistedMode);

        if (speedSelectorUI != null)
            speedSelectorUI.SetInteractable(false);

        UpdateDrivingInfoUI();
    }

    private void PrepareConventionalCarForScenarioStart()
    {
        if (currentAssistedMode)
            return;

        Transform targetCar = carTransform;

        if (targetCar == null && carParking != null)
            targetCar = carParking.transform;

        if (targetCar == null)
            return;

        CarNodeFollower follower =
            targetCar.GetComponent<CarNodeFollower>();

        if (follower != null)
            follower.PrepareForScenarioGameplay();
    }

    private void ResetCarToStart()
    {
        if (currentScenario == null)
            return;

        Transform targetCar = carTransform;

        if (targetCar == null && carParking != null)
            targetCar = carParking.transform;

        if (targetCar == null)
        {
            Debug.LogWarning("[ScenarioManager] No car Transform is assigned or available.");
            return;
        }

        if (scenarioStartNodes == null || scenarioStartNodes.Length == 0)
        {
            Debug.LogWarning("[ScenarioManager] No scenario start nodes are assigned.");
            return;
        }

        int startIndex = currentScenario.startPointIndex;

        if (startIndex < 0 || startIndex >= scenarioStartNodes.Length)
        {
            Debug.LogWarning(
                $"[ScenarioManager] Invalid startPointIndex={startIndex} for {currentScenario.scenarioId}."
            );
            return;
        }

        RoadNode startNode = scenarioStartNodes[startIndex];

        if (startNode == null)
        {
            Debug.LogWarning("[ScenarioManager] The selected start RoadNode is missing.");
            return;
        }

        CarNodeFollower follower = targetCar.GetComponent<CarNodeFollower>();

        if (follower != null)
        {
            follower.metersPerUnityUnit = Mathf.Max(
                0f,
                metersPerUnityUnit
            );

            follower.ResetToSpecificNode(startNode);
        }
        else if (snapCarToStartNode)
        {
            Vector3 position = startNode.transform.position;
            position.z = targetCar.position.z;
            targetCar.position = position;
            targetCar.rotation = startNode.transform.rotation;
        }

        Rigidbody2D rb = targetCar.GetComponent<Rigidbody2D>();

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // Conventional CameraFollow uses a smooth Lerp and the scenario intro
        // pauses Time.timeScale. Snap once here so the conventional round is
        // visibly centered on the exact same start node before gameplay begins.
        if (!currentAssistedMode && Camera.main != null)
        {
            CameraFollow follow =
                Camera.main.GetComponent<CameraFollow>();

            if (follow != null)
                follow.SnapToTarget();
        }

        Debug.Log(
            $"[ScenarioManager] START CHECK | " +
            $"scenario={currentScenario.scenarioId}, " +
            $"mode={GetCurrentModeName()}, " +
            $"startIndex={startIndex}, " +
            $"startNode={startNode.name}, " +
            $"car=({targetCar.position.x:0.##},{targetCar.position.y:0.##})"
        );
    }

    private string GetCurrentStartNodeName()
    {
        if (currentScenario == null || scenarioStartNodes == null)
            return string.Empty;

        int index = currentScenario.startPointIndex;

        if (index < 0 || index >= scenarioStartNodes.Length)
            return string.Empty;

        RoadNode node = scenarioStartNodes[index];
        return node != null ? node.name : string.Empty;
    }

    private void Update()
    {
        if (surveyCompleted || !scenarioRunning || waitingForContinue || currentScenario == null)
            return;

        // Keep live driving distance/time visible in both practice and main
        // conventional rounds.
        UpdateDrivingInfoUI();

        if (isPracticeRound)
        {
            if (timerLabel != null)
                SetLabelText(timerLabel, "ــ:ــ");

            return;
        }

        float elapsed = Time.time - scenarioStartTime;
        float normalLimitSeconds = currentScenario.timeLimitMinutes * 60f;
        float overtimeLimitSeconds = Mathf.Max(0f, overtimeParkingMinutes) * 60f;
        float remaining = normalLimitSeconds - elapsed;

        // The bar visualizes only the normal scenario time. Once normal time
        // reaches zero, it remains empty/red while the existing negative
        // overtime timer continues to count upward.
        if (timerBarUI != null)
        {
            timerBarUI.SetRemainingTime(
                Mathf.Max(0f, remaining)
            );
        }

        // Normal countdown.
        if (remaining > 0f)
        {
            if (timerLabel != null)
            {
                int minutes = Mathf.FloorToInt(remaining / 60f);
                int seconds = Mathf.FloorToInt(remaining % 60f);

                string clock = $"{minutes:00}:{seconds:00}";
                SetLabelText(timerLabel, ToPersianDigits(clock));
            }

            return;
        }

        // The normal deadline has passed. Apply the fixed time penalty exactly once,
        // keep the round playable during the grace period, and show a one-time
        // non-blocking warning so the participant knows overtime has started.
        ApplyLatePenaltyOnce();

        if (overtimeLimitSeconds > 0f)
            ShowOvertimeWarningOnce(overtimeLimitSeconds);

        float overtimeSeconds = Mathf.Max(0f, elapsed - normalLimitSeconds);

        // After the full grace period, the round is a hard timeout.
        if (overtimeLimitSeconds <= 0f || overtimeSeconds >= overtimeLimitSeconds)
        {
            SetLabelText(
                timerLabel,
                FormatNegativeTimer(overtimeLimitSeconds)
            );

            OnScenarioTimeout();
            return;
        }

        // During overtime the timer counts upward as negative elapsed time:
        // −00:00, −00:01, ... −03:00.
        SetLabelText(
            timerLabel,
            FormatNegativeTimer(overtimeSeconds)
        );
    }

    private void ApplyLatePenaltyOnce()
    {
        if (latePenaltyApplied || currentScenario == null || isPracticeRound)
            return;

        latePenaltyApplied = true;

        int latePenaltyPoints = GetLateTimePenaltyPoints();

        currentScore = Mathf.Clamp(
            currentScore - latePenaltyPoints,
            0f,
            startingScore
        );

        UpdateScoreUI();

        Debug.Log(
            $"[ScenarioManager] Normal time expired | " +
            $"scenario={currentScenario.scenarioId}, mode={GetCurrentModeName()}, " +
            $"latePenalty={latePenaltyPoints}, scenarioScoreNow={currentScore:0}, " +
            $"overtimeMinutes={overtimeParkingMinutes:0.#}"
        );
    }

    private void ShowOvertimeWarningOnce(float overtimeLimitSeconds)
    {
        if (overtimeWarningShown || isPracticeRound || overtimeLimitSeconds <= 0f)
            return;

        // The overtime warning intentionally reuses the same UI that shows
        // "starting point" and the Assisted-mode opening instruction.
        // This avoids requiring a separate OvertimeWarningPanel in the scene.
        if (autoFindUIReferences)
            AutoBindUIReferences();

        if (assistedOpeningMessagePanel == null ||
            assistedOpeningMessageLabel == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] Overtime warning could not be shown because " +
                "AssistedOpeningMessagePanel or AssistedOpeningMessageLabel is missing."
            );
            return;
        }

        int overtimeSeconds = Mathf.Max(
            1,
            Mathf.RoundToInt(overtimeLimitSeconds)
        );

        string message =
            "زمان اصلی به پایان رسید.\n" +
            "فرصت اضافه آغاز شد.\n" +
            "لطفاً در " +
            ToPersianDigits(
                overtimeSeconds.ToString(CultureInfo.InvariantCulture)
            ) +
            " ثانیه آینده یک جای پارک انتخاب کنید.";

        // ShowAssistedOpeningMessage already activates the shared panel and
        // uses realtime waiting, so the overtime countdown continues normally.
        ShowAssistedOpeningMessage(
            message,
            overtimeWarningDisplaySeconds
        );

        if (!assistedOpeningMessagePanel.activeInHierarchy)
        {
            Debug.LogWarning(
                "[ScenarioManager] AssistedOpeningMessagePanel was activated for " +
                "the overtime warning but is still not visible. Check its parent Canvas."
            );
            return;
        }

        overtimeWarningShown = true;

        Debug.Log(
            $"[ScenarioManager] OVERTIME WARNING SHOWN | " +
            $"scenario={currentScenario?.scenarioId}, mode={GetCurrentModeName()}, " +
            $"seconds={overtimeSeconds}"
        );
    }

    private void HideOvertimeWarning()
    {
        // Overtime uses the same panel/coroutine as the Assisted opening
        // messages, so hiding either one is handled by the same method.
        HideAssistedOpeningMessage();
    }

    private static string FormatNegativeTimer(float overtimeSeconds)
    {
        int safeSeconds = Mathf.Max(0, Mathf.FloorToInt(overtimeSeconds));
        int minutes = safeSeconds / 60;
        int seconds = safeSeconds % 60;

        // LTR mark + Unicode minus keeps the minus sign on the expected side
        // when the label uses RTL TextMeshPro.
        return "\u200E−" + ToPersianDigits($"{minutes:00}:{seconds:00}");
    }

    private void UpdateDrivingInfoUI()
    {
        if (currentAssistedMode)
        {
            if (conventionalDrivingInfoRoot != null)
                conventionalDrivingInfoRoot.SetActive(false);

            return;
        }

        if (conventionalDrivingInfoRoot != null &&
            scenarioRunning &&
            !waitingForScenarioStart)
        {
            conventionalDrivingInfoRoot.SetActive(true);
        }

        float drivenDistance =
            carParking != null
                ? carParking.GetDrivingDistanceMeters()
                : 0f;

        float drivingTime =
            carParking != null
                ? carParking.GetDrivingTimeSeconds()
                : 0f;

        float remainingDistance =
            carParking != null
                ? carParking.GetRemainingDrivingDistanceMeters()
                : 0f;

        ParkingSpot target =
            carParking != null
                ? carParking.ActiveParkingTarget
                : null;

        SetLabelText(
            drivingDistanceLabel,
            "مسافت طی‌شده با خودرو: " +
            FormatNumber(drivenDistance) +
            " متر"
        );

        SetLabelText(
            drivingTimeLabel,
            "زمان حرکت خودرو: " +
            FormatDuration(drivingTime)
        );

        SetLabelText(
            remainingDrivingDistanceLabel,
            target != null
                ? "مسافت باقی‌مانده تا جایگاه: " +
                  FormatNumber(remainingDistance) +
                  " متر"
                : "مسافت باقی‌مانده تا جایگاه: ـ"
        );

        SetLabelText(
            selectedWalkingDistanceLabel,
            target != null
                ? "مسافت پیاده‌روی از جایگاه: " +
                  FormatNumber(target.walkingDistanceMeters) +
                  " متر"
                : "مسافت پیاده‌روی از جایگاه: ـ"
        );
    }

    private void ResetAllParkingSpots()
    {
        ParkingSpot[] spots = FindObjectsByType<ParkingSpot>(FindObjectsSortMode.None);

        foreach (ParkingSpot spot in spots)
        {
            if (spot != null)
                spot.ResetSpot();
        }
    }

    /// <summary>
    /// Tutorial occupancy logic:
    /// Practice Conventional creates the parking situation once.
    /// Practice Assisted restores the exact same physical occupied spots.
    /// No tutorial occupancy is logged or mixed with the main experiment.
    /// </summary>
    private void ApplyTutorialOccupancy(bool assistedMode)
    {
        if (assistedMode &&
            tutorialOccupancySnapshot != null &&
            tutorialOccupancySnapshot.Count > 0)
        {
            RestorePairedOccupancySnapshot(tutorialOccupancySnapshot);

            Debug.Log(
                "[ScenarioManager] Tutorial Assisted restored the same occupancy snapshot."
            );
            return;
        }

        ResetAllParkingSpots();

        if (occupancyManager != null)
        {
            occupancyManager.legalOccupancyRate =
                currentScenario.legalOccupancyRate;

            occupancyManager.illegalOccupancyRate =
                currentScenario.illegalOccupancyRate;

            occupancyManager.scarcityLevel =
                currentScenario.scarcityLevel;

            occupancyManager.ApplyOccupancy();
        }

        // Save only after Conventional practice generation.
        if (!assistedMode)
        {
            tutorialOccupancySnapshot =
                CaptureCurrentOccupancySnapshot();

            Debug.Log(
                "[ScenarioManager] Tutorial Conventional occupancy snapshot created."
            );
        }
    }

    /// <summary>
    /// Generates occupancy once for a scenario pair, then restores exactly the
    /// same occupied/free physical ParkingSpot objects for the paired mode.
    /// This makes Conventional vs Assisted a clean information-mode comparison.
    /// </summary>
    private void ApplyOccupancyForCurrentScenarioPair()
    {
        if (currentScenario == null)
            return;

        bool pairingEnabled =
            reuseExactOccupancyForModePair &&
            runConventionalAndAssistedForEachScenario;

        bool secondModeOfPair =
            pairingEnabled &&
            currentRoundIndex >= 0 &&
            (currentRoundIndex % 2 == 1);

        // The second presentation of the same scenario must NOT randomize again.
        if (secondModeOfPair)
        {
            if (pairedOccupancySnapshots.TryGetValue(
                    currentScenarioIndex,
                    out List<PairedSpotOccupancyState> snapshot) &&
                snapshot != null &&
                snapshot.Count > 0)
            {
                RestorePairedOccupancySnapshot(snapshot);
                VerifyPairedOccupancySnapshot(snapshot);

                if (logPairedOccupancyChecks)
                {
                    Debug.Log(
                        $"[ScenarioManager] Exact paired occupancy restored | " +
                        $"scenario={currentScenario.scenarioId}, " +
                        $"mode={GetCurrentModeName()}, spots={snapshot.Count}"
                    );
                }

                return;
            }

            Debug.LogError(
                $"[ScenarioManager] Missing occupancy snapshot for paired mode of " +
                $"{currentScenario.scenarioId}. Fresh occupancy will be generated " +
                $"as a fallback, but this pair should NOT be used as pilot data."
            );
        }

        // First presentation of the scenario: create the objective parking state.
        if (occupancyManager != null)
        {
            occupancyManager.legalOccupancyRate =
                currentScenario.legalOccupancyRate;

            occupancyManager.illegalOccupancyRate =
                currentScenario.illegalOccupancyRate;

            occupancyManager.scarcityLevel =
                currentScenario.scarcityLevel;

            occupancyManager.ApplyOccupancy();
        }
        else
        {
            Debug.LogWarning(
                "[ScenarioManager] ParkingOccupancyManager is missing. " +
                "All spots will remain free for this scenario."
            );
        }

        // Freeze the exact physical occupancy before the participant can act.
        if (pairingEnabled)
        {
            List<PairedSpotOccupancyState> snapshot =
                CaptureCurrentOccupancySnapshot();

            pairedOccupancySnapshots[currentScenarioIndex] = snapshot;

            if (logPairedOccupancyChecks)
            {
                int occupiedCount = 0;

                foreach (PairedSpotOccupancyState state in snapshot)
                {
                    if (state != null && state.occupied)
                        occupiedCount++;
                }

                Debug.Log(
                    $"[ScenarioManager] Paired occupancy captured | " +
                    $"scenario={currentScenario.scenarioId}, " +
                    $"firstMode={GetCurrentModeName()}, " +
                    $"spots={snapshot.Count}, occupied={occupiedCount}"
                );
            }
        }
    }

    private List<PairedSpotOccupancyState> CaptureCurrentOccupancySnapshot()
    {
        ParkingSpot[] spots =
            FindObjectsByType<ParkingSpot>(FindObjectsSortMode.None);

        List<PairedSpotOccupancyState> snapshot =
            new List<PairedSpotOccupancyState>(spots.Length);

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            snapshot.Add(
                new PairedSpotOccupancyState
                {
                    spot = spot,
                    occupied = spot.isOccupied
                }
            );
        }

        return snapshot;
    }

    private void RestorePairedOccupancySnapshot(
        List<PairedSpotOccupancyState> snapshot)
    {
        foreach (PairedSpotOccupancyState state in snapshot)
        {
            if (state == null || state.spot == null)
                continue;

            // SetOccupied also clears any previous player-parking/highlight
            // state and refreshes the visual colour.
            state.spot.SetOccupied(state.occupied);
        }
    }

    private void VerifyPairedOccupancySnapshot(
        List<PairedSpotOccupancyState> snapshot)
    {
        int mismatchCount = 0;
        int missingSpotCount = 0;

        foreach (PairedSpotOccupancyState state in snapshot)
        {
            if (state == null || state.spot == null)
            {
                missingSpotCount++;
                continue;
            }

            if (state.spot.isOccupied != state.occupied)
                mismatchCount++;
        }

        ParkingSpot[] currentSpots =
            FindObjectsByType<ParkingSpot>(FindObjectsSortMode.None);

        if (mismatchCount > 0 ||
            missingSpotCount > 0 ||
            currentSpots.Length != snapshot.Count)
        {
            Debug.LogError(
                $"[ScenarioManager] PAIRED OCCUPANCY CHECK FAILED | " +
                $"scenario={currentScenario?.scenarioId}, " +
                $"mismatches={mismatchCount}, missing={missingSpotCount}, " +
                $"snapshotSpots={snapshot.Count}, currentSpots={currentSpots.Length}. " +
                $"Do not use this round as study data."
            );
            return;
        }

        if (logPairedOccupancyChecks)
        {
            Debug.Log(
                $"[ScenarioManager] PAIRED OCCUPANCY CHECK PASSED | " +
                $"scenario={currentScenario?.scenarioId}. " +
                $"Every physical parking spot matches the first mode."
            );
        }
    }

    private void UpdateScenarioInfoUI()
    {
        if (currentScenario == null)
            return;

        if (isPracticeRound)
        {
            SetLabelText(scenarioLabel, "مرحله تمرینی");
            SetLabelText(
                roundLabel,
                currentAssistedMode
                    ? "تمرین ۲ از ۲"
                    : "تمرین ۱ از ۲"
            );
        }
        else
        {
            SetLabelText(
                scenarioLabel,
                "سناریو " +
                ToPersianDigits(
                    GetCurrentScenarioDisplayNumber()
                        .ToString(CultureInfo.InvariantCulture)
                )
            );

            SetLabelText(
                roundLabel,
                ToPersianDigits(
                    $"مرحله {currentRoundIndex + 1} از {GetTotalRoundCount()}"
                )
            );
        }

        SetLabelText(modeLabel, "حالت: " + GetCurrentModePersianName());

        // Compact live scenario table shown in the right-side panel.
        SetLabelText(dwellTimeValue, FormatParkingDuration(currentScenario.dwellTimeMinutes));
        SetLabelText(legalOccupancyValue, FormatPercent(currentScenario.legalOccupancyRate));
        SetLabelText(illegalOccupancyValue, FormatPercent(currentScenario.illegalOccupancyRate));
        SetLabelText(smartFeeValue, FormatToman(GetCurrentSmartParkingFeeToman()));

        // Two separate live fine rows:
        // No Parking and Driveway Entrance share the same fine policy.
        SetLabelText(
            noParkingFineValue,
            FormatToman(GetCurrentNoParkingFineToman())
        );

        SetLabelText(
            noStoppingFineValue,
            FormatToman(GetCurrentNoStoppingFineToman())
        );

        UpdateCitationProbabilityVisual();

        // Legacy standalone labels are kept for scene compatibility and can stay disabled.
        SetLabelText(
             dwellTimeLabel,
             "مدت پارک: " + FormatParkingDuration(currentScenario.dwellTimeMinutes)
        );

        SetLabelText(
            timeLimitLabel,
            "زمان مجاز رسیدن: " + FormatNumber(currentScenario.timeLimitMinutes) + " دقیقه"
        );

        int smartParkingFeeToman = GetCurrentSmartParkingFeeToman();

        SetLabelText(
            legalFeeLabel,
            "پارک معمولی: رایگان\n" +
            "پارکینگ هوشمند: " + FormatToman(smartParkingFeeToman)
        );

        SetLabelText(
            illegalFineLabel,
            "سیاست جریمه: " +
            ParkingPolicy1405.GetFinePolicyPersianLabel(currentScenario.illegalFinePolicy) + "\n" +
            "جریمه پارک ممنوع / جلوی ورودی پارکینگ: " + FormatToman(GetCurrentNoParkingFineToman()) + "\n" +
            "جریمه توقف مطلقاً ممنوع: " + FormatToman(GetCurrentNoStoppingFineToman()) + "\n" +
            "جریمه جایگاه ویژه معلولین: " +
            FormatToman(ParkingPolicy1405.DisabledParkingFineToman) + "\n" +
            "حمل با جرثقیل: هزینه متغیر و جداگانه"
        );

        SetLabelText(
            citationProbLabel,
            "احتمال جریمه طی مدت پارک: " +
            FormatCitationProbabilityWithLevel(
                GetCurrentAdjustedCitationProbability()
            )
        );

        SetLabelText(
            noFineIllegalLabel,
            "پارک های غیرقانونی بدون جریمه مالی:\n" +
            "نزدیک تقاطع و مقابل خط عابر پیاده"
        );

        int latePenaltyPoints = GetLateTimePenaltyPoints();

        SetLabelText(
            onTimeRewardLabel,
            "کسر پایان زمان: -" +
            ToPersianDigits(latePenaltyPoints.ToString(CultureInfo.InvariantCulture)) +
            " امتیاز\nفرصت اضافه: " +
            ToPersianDigits(
                Mathf.RoundToInt(overtimeParkingMinutes * 60f)
                    .ToString(CultureInfo.InvariantCulture)
            ) +
            " ثانیه"
        );
    }

    private void UpdateCitationProbabilityVisual()
    {
        float probability = GetCurrentAdjustedCitationProbability();

        SetLabelText(
            citationValue,
            FormatCitationProbabilityWithLevel(probability)
        );

        if (citationValueBox != null)
            citationValueBox.color = GetCitationProbabilityColor(probability);
    }

    private string FormatCitationProbabilityWithLevel(float probability)
    {
        return FormatPercent(probability) +
               " (" +
               GetCitationProbabilityLevelPersian(probability) +
               ")";
    }

    private string GetCitationProbabilityLevelPersian(float probability)
    {
        probability = Mathf.Clamp01(probability);

        // Requested study interpretation:
        // around 10%  -> Low
        // around 27%  -> Medium
        // around 62%  -> High
        if (probability < citationLowUpperBound)
            return "کم";

        if (probability < citationMediumUpperBound)
            return "متوسط";

        return "زیاد";
    }

    private Color GetCitationProbabilityColor(float probability)
    {
        probability = Mathf.Clamp01(probability);

        if (probability < citationLowUpperBound)
            return citationLowColor;

        if (probability < citationMediumUpperBound)
            return citationMediumColor;

        return citationHighColor;
    }

    private void UpdateScoreUI()
    {
        SetLabelText(
            scoreLabel,
            "امتیاز این سناریو: " +
            ToPersianDigits(
                Mathf.RoundToInt(currentScore)
                    .ToString(CultureInfo.InvariantCulture)
            )
        );

        if (scoreBar != null)
        {
            scoreBar.value = Mathf.Clamp(
                currentScore,
                scoreBar.minValue,
                scoreBar.maxValue
            );
        }

        bool has1 = currentScore >= star1Threshold;
        bool has2 = currentScore >= star2Threshold;
        bool has3 = currentScore >= star3Threshold;

        if (star1Image != null) star1Image.color = has1 ? starOnColor : starOffColor;
        if (star2Image != null) star2Image.color = has2 ? starOnColor : starOffColor;
        if (star3Image != null) star3Image.color = has3 ? starOnColor : starOffColor;
    }

    public float CalculateEstimatedWalkingDistance(ParkingSpot spot)
    {
        if (spot == null)
            return 0f;

        if (walkingNetworkCalculator == null)
            walkingNetworkCalculator = FindFirstObjectByType<WalkingNetworkCalculator>();

        if (walkingNetworkCalculator != null &&
            walkingNetworkCalculator.TryCalculateSpotDistance(
                spot,
                out float networkDistanceMeters,
                out float networkWalkingTimeSeconds))
        {
            spot.walkingDistanceMeters = networkDistanceMeters;
            spot.walkingTimeSeconds = networkWalkingTimeSeconds;
            return networkDistanceMeters;
        }

        // A previously calculated network value is preferable to recalculating
        // a straight line when the calculator is temporarily unavailable.
        if (spot.walkingDistanceMeters > 0f)
            return spot.walkingDistanceMeters;

        // Last-resort fallback keeps the game operational while making a
        // missing walking-network setup visible in the Console.
        if (destinationGate != null)
        {
            float fallbackDistance = Vector2.Distance(
                spot.transform.position,
                destinationGate.position
            ) * Mathf.Max(0f, metersPerUnityUnit);

            Debug.LogWarning(
                $"[ScenarioManager] No WalkNode route was available for {spot.name}. " +
                $"Using straight-line fallback: {fallbackDistance:0.0} m."
            );

            return fallbackDistance;
        }

        return 0f;
    }

    public void OnCarParked(
        ParkingSpot spot,
        float movementOrSelectionTimeSeconds,
        float drivingDistanceMeters,
        float drivingTimeSeconds)
    {
        if (surveyCompleted || !scenarioRunning || currentScenario == null || spot == null)
            return;

        // Protect the hard deadline even if the parking click is processed in
        // the same frame just before Update() performs the timeout check.
        if (!isPracticeRound)
        {
            float elapsedNow = Mathf.Max(0f, Time.time - scenarioStartTime);
            float hardLimitSeconds =
                (currentScenario.timeLimitMinutes + Mathf.Max(0f, overtimeParkingMinutes)) * 60f;

            if (elapsedNow >= hardLimitSeconds)
            {
                OnScenarioTimeout();
                return;
            }
        }

        scenarioRunning = false;
        waitingForContinue = true;

        if (isPracticeRound)
        {
            Debug.Log(
                "[ScenarioManager] Practice completed. Nothing was logged."
            );

            SetTutorialInputEnabled(false);

            if (tutorialManager != null)
                tutorialManager.OnPracticeCompleted(currentAssistedMode);

            return;
        }

        if (conventionalDrivingInfoRoot != null)
            conventionalDrivingInfoRoot.SetActive(false);

        float scenarioElapsedSeconds = Mathf.Max(0f, Time.time - scenarioStartTime);
        float timeLimitSeconds = currentScenario.timeLimitMinutes * 60f;

        float walkingDistanceMeters = CalculateEstimatedWalkingDistance(spot);
        float walkingTimeSeconds = spot.walkingTimeSeconds;

        if (walkingTimeSeconds <= 0f && walkingDistanceMeters > 0f)
        {
            walkingTimeSeconds = walkingSpeedMetersPerSecond > 0f
                ? walkingDistanceMeters / walkingSpeedMetersPerSecond
                : 0f;
        }

        float totalEstimatedTaskTimeSeconds = scenarioElapsedSeconds + walkingTimeSeconds;
        bool onTimeGame = scenarioElapsedSeconds <= timeLimitSeconds;
        bool onTimeIncludingWalk = totalEstimatedTaskTimeSeconds <= timeLimitSeconds;

        // If the parking event arrives just after the normal deadline but before
        // Update() has applied the penalty, apply it here so no late choice can
        // escape the one-time time-expiry deduction.
        if (!onTimeGame)
            ApplyLatePenaltyOnce();

        spot.walkingDistanceMeters = walkingDistanceMeters;
        spot.walkingTimeSeconds = walkingTimeSeconds;

        int onTimeRewardPoints = GetOnTimeRewardPoints(currentScenario);

        ParkingScoreCalculator.Result scoreResult = ParkingScoreCalculator.Calculate(
            currentScenario,
            spot,
            onTimeGame,
            participantId,
            currentRoundIndex,
            currentAssistedMode,
            onTimeRewardPoints,
            0,
            tomanPerPoint,
            illegalFineTomanPerPoint,
            noFineIllegalPenaltyPoints
        );

        // The time-expiry penalty, if any, was already applied exactly once at
        // 00:00. ParkingScoreCalculator now owns parking-choice scoring:
        // - legal parking keeps the existing legal-cost conversion;
        // - fine-bearing illegal parking is penalized from the APPLICABLE fine
        //   whether or not a citation occurs;
        // - designated no-financial-fine illegal parking always removes 10 points.
        int parkingChoiceDeductionPoints =
            CalculateParkingChoiceScoreDeduction(spot, scoreResult);

        currentScore = Mathf.Clamp(
            currentScore - parkingChoiceDeductionPoints,
            0f,
            startingScore
        );

        // Only completed round scores feed the hidden cumulative totals.
        cumulativeScore += currentScore;

        if (currentAssistedMode)
            assistedScore += currentScore;
        else
            conventionalScore += currentScore;

        UpdateScoreUI();

        TryCompleteLoggerRound(
            spot,
            parked: true,
            timedOut: false,
            scenarioElapsedSeconds,
            movementOrSelectionTimeSeconds,
            drivingTimeSeconds,
            drivingDistanceMeters,
            walkingDistanceMeters,
            walkingTimeSeconds,
            onTimeGame,
            onTimeIncludingWalk,
            scoreResult.realizedCostToman
        );

        CompleteParkingViewTracking(spot, timedOut: false);

        Debug.Log(
            "[ScenarioManager] Round scored | " +
            $"scenario={currentScenario.scenarioId}, mode={GetCurrentModeName()}, " +
            $"spot={spot.spotId}, onTimeIncludingWalk={onTimeIncludingWalk}, " +
            $"citationOccurred={scoreResult.citationOccurred}, " +
            $"adjustedCitationProbability={scoreResult.adjustedCitationProbability:0.####}, " +
            $"finePolicy={currentScenario.illegalFinePolicy}, " +
            $"fineMultiplier={scoreResult.fineMultiplier:0.#}, " +
            $"baseIllegalFineToman={scoreResult.baseIllegalFineToman}, " +
            $"appliedIllegalFineToman={scoreResult.appliedIllegalFineToman}, " +
            $"randomSeed={scoreResult.randomSeed}, randomDraw={scoreResult.randomDraw:0.####}, " +
            $"realizedCostToman={scoreResult.realizedCostToman:0}, " +
            $"calculatorDeduction={scoreResult.pointDeduction}, " +
            $"appliedParkingDeduction={parkingChoiceDeductionPoints}, " +
            $"scenarioScore={currentScore:0}, cumulativeScore={cumulativeScore:0}"
        );

        ShowParkingResult(
            spot,
            scenarioElapsedSeconds,
            drivingDistanceMeters,
            drivingTimeSeconds,
            walkingDistanceMeters,
            walkingTimeSeconds,
            totalEstimatedTaskTimeSeconds,
            onTimeIncludingWalk,
            scoreResult
        );
    }

    /// <summary>
    /// Returns the authoritative parking-choice deduction calculated by
    /// ParkingScoreCalculator. Citation affects the realized monetary fine,
    /// but not the visual score consequence of choosing a fine-bearing illegal
    /// parking space.
    /// </summary>
    private int CalculateParkingChoiceScoreDeduction(
        ParkingSpot spot,
        ParkingScoreCalculator.Result scoreResult)
    {
        return Mathf.Max(0, scoreResult.pointDeduction);
    }

    // Compatibility for older parking-interaction scripts that report only
    // their movement/selection time. The current four-argument method remains
    // the authoritative path and preserves the measured driving metrics.
    public void OnCarParked(
        ParkingSpot spot,
        float movementOrSelectionTimeSeconds)
    {
        float drivingDistanceMeters =
            !currentAssistedMode && carParking != null
                ? carParking.GetDrivingDistanceMeters()
                : 0f;

        float drivingTimeSeconds =
            !currentAssistedMode && carParking != null
                ? carParking.GetDrivingTimeSeconds()
                : 0f;

        OnCarParked(
            spot,
            movementOrSelectionTimeSeconds,
            drivingDistanceMeters,
            drivingTimeSeconds
        );
    }

    /// <summary>
    /// Participant-facing distance-band label for the selected parking spot.
    ///
    /// IMPORTANT: This changes only the ResultPanel wording. ParkingLogger still
    /// receives the ParkingSpot itself and continues saving A1-A6 through
    /// spot.GetAlternativeId().
    ///
    /// Current study region meaning:
    /// Inner = less than 200 m
    /// Outer = 200-300 m
    /// Far   = more than 300 m
    /// </summary>
    private static string GetSelectedDistanceRangePersian(ParkingSpot spot)
    {
        if (spot == null)
            return "نامشخص";

        switch (spot.region)
        {
            case RegionType.Inner:
                return "کمتر از ۲۰۰ متر";

            case RegionType.Outer:
                return "۲۰۰ تا ۳۰۰ متر";

            case RegionType.Far:
                return "بیش از ۳۰۰ متر";

            default:
                return "نامشخص";
        }
    }

    /// <summary>
    /// Returns the participant-facing description of an illegal parking choice.
    /// It uses the exact Persian reason already stored on ParkingSpot, so the
    /// result panel stays consistent with the map labels and research metadata.
    /// </summary>
    private static string GetIllegalParkingTypePersian(ParkingSpot spot)
    {
        if (spot == null)
            return "پارک غیرمجاز";

        string illegalReason = spot.GetIllegalReasonPersianText();

        if (!string.IsNullOrWhiteSpace(illegalReason))
            return illegalReason;

        string legalFeature = spot.GetLegalParkingFeaturePersianText();

        if (!string.IsNullOrWhiteSpace(legalFeature))
            return legalFeature;

        return "پارک غیرمجاز";
    }

    private void ShowParkingResult(
        ParkingSpot spot,
        float scenarioElapsedSeconds,
        float drivingDistanceMeters,
        float drivingTimeSeconds,
        float walkingDistanceMeters,
        float walkingTimeSeconds,
        float totalEstimatedTaskTimeSeconds,
        bool onTimeIncludingWalk,
        ParkingScoreCalculator.Result result)
    {
        string title = result.isLegal
            ? "پارک با موفقیت انجام شد"
            : "پارک غیرمجاز انتخاب شد";

        StringBuilder detail = new StringBuilder();

        detail.AppendLine("حالت: " + GetCurrentModePersianName());

        detail.AppendLine(
            "محدوده انتخاب: " +
            GetSelectedDistanceRangePersian(spot)
        );

        if (!currentAssistedMode)
        {
            detail.AppendLine(
                "مسافت رانندگی: " +
                FormatNumber(drivingDistanceMeters) +
                " متر"
            );

            detail.AppendLine(
                "زمان حرکت خودرو: " +
                FormatDuration(drivingTimeSeconds)
            );
        }

        detail.AppendLine(
            "فاصله پیاده‌روی: " +
            FormatNumber(walkingDistanceMeters) +
            " متر"
        );

        detail.AppendLine(
            "زمان جستجو/انتخاب: " +
            FormatDuration(scenarioElapsedSeconds)
        );

        detail.AppendLine(
            "زمان پیاده‌روی: " +
            FormatDuration(walkingTimeSeconds)
        );

        detail.AppendLine(
            "زمان کل تخمینی تا مقصد: " +
            FormatDuration(totalEstimatedTaskTimeSeconds)
        );

        detail.AppendLine(
            "وضعیت زمان جست‌وجو: " +
            (latePenaltyApplied
                ? "استفاده از فرصت اضافه"
                : "در محدوده زمان اصلی")
        );

        detail.AppendLine();

        if (result.isLegal)
        {
            string legalType =
                spot != null
                    ? spot.GetLegalParkingFeaturePersianText()
                    : string.Empty;

            if (string.IsNullOrWhiteSpace(legalType))
            {
                legalType =
                    result.isSmartParking
                        ? "پارکینگ هوشمند"
                        : "پارک قانونی معمولی";
            }

            detail.AppendLine(
                "نوع جایگاه قانونی: " +
                legalType
            );

            detail.AppendLine(
                "هزینه پارک: " +
                FormatToman(result.legalCostToman)
            );
        }
        else
        {
            detail.AppendLine(
                "نوع جایگاه غیرقانونی: " +
                GetIllegalParkingTypePersian(spot)
            );

            bool noFinancialFine =
                ParkingPolicy1405.IsNoFinancialFineIllegalSpot(spot);

            if (noFinancialFine)
            {
                // Intersection corners and pedestrian crossings are illegal
                // choices in the study and deliberately carry no monetary fine,
                // but they now receive the fixed illegal-parking score penalty.
                detail.AppendLine("جریمه مالی: ندارد");
                detail.AppendLine(
                    "کسر امتیاز پارک غیرمجاز: -" +
                    ToPersianDigits(
                        Mathf.Max(0, noFineIllegalPenaltyPoints)
                            .ToString(CultureInfo.InvariantCulture)
                    ) +
                    " امتیاز"
                );
            }
            else
            {
                // Show only the probability across the full parking duration.
                // The hourly scenario treatment remains internal.
                detail.AppendLine(
                    "احتمال جریمه طی مدت پارک: " +
                    FormatPercent(result.adjustedCitationProbability)
                );

                // ParkingScoreCalculator already performed the citation draw.
                // Citation controls whether a monetary fine is actually paid.
                // The illegal-parking score deduction is applied regardless.
                detail.AppendLine(
                    "نتیجه کنترل: " +
                    (result.citationOccurred
                        ? "خودرو جریمه شد"
                        : "جریمه‌ای ثبت نشد")
                );

                detail.AppendLine(
                    "مبلغ جریمه پرداخت‌شده: " +
                    FormatToman(
                        result.citationOccurred
                            ? result.realizedCostToman
                            : 0f
                    )
                );

                detail.AppendLine(
                    "کسر امتیاز انتخاب پارک غیرمجاز: -" +
                    ToPersianDigits(
                        CalculateParkingChoiceScoreDeduction(spot, result)
                            .ToString(CultureInfo.InvariantCulture)
                    ) +
                    " امتیاز"
                );

                if (spot != null &&
                    spot.illegalReason == IllegalReason.DrivewayEntrance)
                {
                    detail.AppendLine(
                        "مالک یا استفاده‌کننده پارکینگ می‌تواند موضوع را به‌عنوان سد معبر گزارش کرده و برای شکایت و پیگیری اقدام کند."
                    );
                }
            }
        }

        // Keep only the separate ResultScoreText score field.
        ShowResultPanel(
            title,
            detail.ToString().TrimEnd()
        );
    }

    private void OnScenarioTimeout()
    {
        if (surveyCompleted || !scenarioRunning || currentScenario == null)
            return;

        scenarioRunning = false;
        waitingForContinue = true;

        if (conventionalDrivingInfoRoot != null)
            conventionalDrivingInfoRoot.SetActive(false);

        float timeoutSeconds =
            (currentScenario.timeLimitMinutes + Mathf.Max(0f, overtimeParkingMinutes)) * 60f;

        float timeoutDrivingDistance =
            !currentAssistedMode && carParking != null
                ? carParking.GetDrivingDistanceMeters()
                : 0f;

        float timeoutDrivingTime =
            !currentAssistedMode && carParking != null
                ? carParking.GetDrivingTimeSeconds()
                : 0f;

        float timeoutMovementOrSelectionTime =
            currentAssistedMode
                ? timeoutSeconds
                : timeoutDrivingTime;

        // Hard timeout: no parking choice was made even during the grace period.
        // The per-scenario score becomes zero; the next round will reset to 100.
        currentScore = 0f;
        cumulativeScore += currentScore;

        if (currentAssistedMode)
            assistedScore += currentScore;
        else
            conventionalScore += currentScore;

        UpdateScoreUI();

        TryCompleteLoggerRound(
            chosenSpot: null,
            parked: false,
            timedOut: true,
            scenarioElapsedSeconds: timeoutSeconds,
            movementOrSelectionTimeSeconds: timeoutMovementOrSelectionTime,
            drivingTimeSeconds: timeoutDrivingTime,
            drivingDistanceMeters: timeoutDrivingDistance,
            walkingDistanceMeters: 0f,
            walkingTimeSeconds: 0f,
            onTimeGame: false,
            onTimeIncludingWalk: false,
            realizedCostToman: 0f
        );

        CompleteParkingViewTracking(chosenSpot: null, timedOut: true);

        string detail =
            "زمان اصلی و فرصت اضافه به پایان رسید.\n" +
            "محل پارکی انتخاب نشد.\n" +
            "امتیاز این سناریو: ۰";

        ShowResultPanel("زمان مرحله به پایان رسید", detail);
    }

    private void ShowResultPanel(string title, string detail, bool isFinalResult = false)
    {
        HideAssistedOpeningMessage();
        HideOvertimeWarning();

        SetDestinationArrowVisible(false);

        // Hide conventional Move / Brake buttons while showing results
        if (conventionalDrivingButtonsRoot != null)
            conventionalDrivingButtonsRoot.SetActive(false);

        // If the user replaced TMP components with RTLTextMeshPro, the old
        // serialized references may be null. Rebind before displaying results.
        if (autoFindUIReferences)
            AutoBindUIReferences();

        if (resultPanel == null)
        {
            Debug.LogError(
                "[ScenarioManager] ResultPanel was not found. " +
                "Name the panel ResultPanel or assign it in the Inspector."
            );
            return;
        }

        resultPanel.SetActive(true);
        resultPanel.transform.SetAsLastSibling();

        SetLabelText(resultTitleLabel, title);
        SetLabelText(resultDetailLabel, detail);

        string scoreText;

        if (isFinalResult)
        {
            scoreText =
                "مجموع امتیاز: " +
                ToPersianDigits(
                    Mathf.RoundToInt(cumulativeScore)
                        .ToString(CultureInfo.InvariantCulture)
                );
        }
        else
        {
            scoreText =
                "امتیاز این سناریو: " +
                ToPersianDigits(
                    Mathf.RoundToInt(currentScore)
                        .ToString(CultureInfo.InvariantCulture)
                );
        }

        SetLabelText(resultScoreLabel, scoreText);

        if (resultContinueButton != null)
            resultContinueButton.SetActive(!isFinalResult);

        if (!isFinalResult)
            SetLabelText(resultContinueButtonLabel, "ادامه");

        Canvas.ForceUpdateCanvases();

        RectTransform panelRect = resultPanel.transform as RectTransform;
        if (panelRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }

    public void OnResultContinueButton()
    {
        if (surveyCompleted || !waitingForContinue)
            return;

        if (resultPanel != null)
            resultPanel.SetActive(false);

        waitingForContinue = false;
        GoToNextRound();
    }

    private void GoToNextRound()
    {
        int next = currentRoundIndex + 1;

        if (next < GetTotalRoundCount())
        {
            StartRound(next);
            return;
        }

        BeginFinalQuestionnaire();
    }

    private void BeginFinalQuestionnaire()
    {
        surveyCompleted = true;
        scenarioRunning = false;
        waitingForContinue = false;
        waitingForScenarioStart = false;

        SetTutorialInputEnabled(false);
        HideOvertimeWarning();
        SetDestinationArrowVisible(false);

        if (assistedModeUIRoot != null)
            assistedModeUIRoot.SetActive(false);

        if (conventionalDrivingButtonsRoot != null)
            conventionalDrivingButtonsRoot.SetActive(false);

        if (conventionalDrivingInfoRoot != null)
            conventionalDrivingInfoRoot.SetActive(false);

        if (finalQuestionnaire == null && autoFindUIReferences)
            AutoBindUIReferences();

        if (finalQuestionnaire == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] FinalQuestionnaire is missing. " +
                "The survey will go directly to the completion screen."
            );

            CompleteSurveyAfterFinalQuestionnaire();
            return;
        }

        finalQuestionnaire.Submitted -= OnFinalQuestionnaireSubmitted;
        finalQuestionnaire.Submitted += OnFinalQuestionnaireSubmitted;
        finalQuestionnaire.Show(participantId);

        Debug.Log("[ScenarioManager] Final questionnaire opened.");
    }

    private void OnFinalQuestionnaireSubmitted()
    {
        if (finalQuestionnaire != null)
            finalQuestionnaire.Submitted -= OnFinalQuestionnaireSubmitted;

        // NOTE: The survey is intentionally NOT completed here anymore.
        // The reality-question panel now hands off directly to the
        // socioeconomic questionnaire (FinalQuestionnaire.socioEconomicQuestionnaire).
        // CompleteSurveyAfterFinalQuestionnaire() is called once that
        // questionnaire finishes - see its "On Questionnaire Completed ()"
        // UnityEvent, which should be wired to this script's
        // CompleteSurveyAfterFinalQuestionnaire() method in the Inspector.
    }

    public void CompleteSurveyAfterFinalQuestionnaire()
    {
        TryEndLoggerSession();

        // FinalUploadWaitingController handles the participant-facing
        // upload screen and final thank-you screen.
        // Do not show the old ResultPanel here.

        if (resultPanel != null)
            resultPanel.SetActive(false);

        Debug.Log(
            $"[ScenarioManager] Survey completed | total={cumulativeScore:0}, " +
            $"conventional={conventionalScore:0}, assisted={assistedScore:0}"
        );
    }

    // ---------------------------------------------------------------------
    // Runtime UI binding and refresh
    // ---------------------------------------------------------------------

    [ContextMenu("Auto Bind UI References")]
    public void AutoBindUIReferences()
    {
        resultPanel = FindGameObjectByNames(
            resultPanel,
            "ResultPanel",
            "ResultsPanel"
        );

        scenarioLabel = FindTextByNames(scenarioLabel, "ScenarioLabel");
        roundLabel = FindTextByNames(roundLabel, "RoundLabel");
        timerLabel = FindTextByNames(timerLabel, "TimerLabel");

        if (timerBarUI == null)
        {
            GameObject timePanelObject = FindGameObjectByNames(
                null,
                "TimePanel"
            );

            if (timePanelObject != null)
                timerBarUI = timePanelObject.GetComponent<TimerBarUI>();
        }

        if (speedSelectorUI == null)
        {
            GameObject speedPanelObject = FindGameObjectByNames(
                null,
                "SpeedControlPanel",
                "SpeedPanel"
            );

            if (speedPanelObject != null)
                speedSelectorUI = speedPanelObject.GetComponent<SpeedSelectorUI>();
        }

        modeLabel = FindTextByNames(modeLabel, "ModeLabel");
        dwellTimeLabel = FindTextByNames(dwellTimeLabel, "DwellTimeLabel");
        timeLimitLabel = FindTextByNames(timeLimitLabel, "TimeLimitLabel");
        legalFeeLabel = FindTextByNames(legalFeeLabel, "LegalFeeLabel");
        illegalFineLabel = FindTextByNames(
            illegalFineLabel,
            "IllegalFineLabel",
            "IllegalFeeLabel"
        );
        citationProbLabel = FindTextByNames(
            citationProbLabel,
            "CitationProbLabel",
            "CitationProbabilityLabel"
        );

        dwellTimeValue = FindTextByNames(dwellTimeValue, "DwellTimeValue");
        legalOccupancyValue = FindTextByNames(legalOccupancyValue, "LegalOccupancyValue");
        illegalOccupancyValue = FindTextByNames(illegalOccupancyValue, "IllegalOccupancyValue");
        smartFeeValue = FindTextByNames(smartFeeValue, "SmartFeeValue");

        noParkingFineValue = FindTextByNames(
            noParkingFineValue,
            "NoParkingFineValue",
            "RightNoParkingFineValue"
        );

        noStoppingFineValue = FindTextByNames(
            noStoppingFineValue,
            "NoStoppingFineValue",
            "RightNoStoppingFineValue"
        );

        // Legacy field: harmless if an old scene still contains FinePolicyValue.
        finePolicyValue = FindTextByNames(finePolicyValue, "FinePolicyValue");

        citationValue = FindTextByNames(citationValue, "CitationValue");

        if (citationValueBox == null)
        {
            citationValueBox = FindImageUnderGameObject(
                "Row_CitationProbability",
                "ValueBox"
            );
        }

        noFineIllegalLabel = FindTextByNames(
            noFineIllegalLabel,
            "NoFineIllegalLabel",
            "NoFineIllegalText"
        );
        onTimeRewardLabel = FindTextByNames(
            onTimeRewardLabel,
            "OnTimeRewardLabel"
        );

        scenarioIntroPanel = FindGameObjectByNames(
            scenarioIntroPanel,
            "ScenarioIntroPanel"
        );
        introTitleLabel = FindTextByNames(
            introTitleLabel,
            "IntroTitleText"
        );
        introModeLabel = FindTextByNames(
            introModeLabel,
            "IntroModeText"
        );

        introDwellValue = FindTextByNames(
            introDwellValue,
            "IntroDwellValue"
        );
        introTimeLimitValue = FindTextByNames(
            introTimeLimitValue,
            "IntroTimeLimitValue"
        );
        introLegalOccupancyValue = FindTextByNames(
            introLegalOccupancyValue,
            "IntroLegalOccupancyValue"
        );
        introIllegalOccupancyValue = FindTextByNames(
            introIllegalOccupancyValue,
            "IntroIllegalOccupancyValue"
        );
        introSmartFeeValue = FindTextByNames(
            introSmartFeeValue,
            "IntroSmartFeeValue"
        );
        introIllegalFeeValue = FindTextByNames(
            introIllegalFeeValue,
            "IntroIllegalFeeValue"
        );
        introCitationValue = FindTextByNames(
            introCitationValue,
            "IntroCitationValue"
        );

        introFreeParkingRateValue = FindTextByNames(
            introFreeParkingRateValue,
            "FreeLegalParkingRateValue",
            "IntroFreeParkingRateValue"
        );
        introFreeParkingTotalValue = FindTextByNames(
            introFreeParkingTotalValue,
            "FreeLegalParkingTotalValue",
            "IntroFreeParkingTotalValue"
        );
        introNoParkingFineTypeLabel = FindTextByNames(
            introNoParkingFineTypeLabel,
            "NoParkingFineLabel",
            "NoParkingFineTypeLabel",
            "IntroNoParkingFineLabel"
        );

        // Fallback for the current hierarchy where the row label may still be
        // named simply "Label" under Row_NoParkingFine.
        if (introNoParkingFineTypeLabel == null)
            introNoParkingFineTypeLabel =
                FindFirstTextUnderGameObject("Row_NoParkingFine", "Value");

        introNoParkingFineRateValue = FindTextByNames(
            introNoParkingFineRateValue,
            "NoParkingFineRateValue",
            "IntroNoParkingFineRateValue"
        );
        introNoParkingFineTotalValue = FindTextByNames(
            introNoParkingFineTotalValue,
            "NoParkingFineTotalValue",
            "IntroNoParkingFineTotalValue"
        );
        introNoStoppingFineRateValue = FindTextByNames(
            introNoStoppingFineRateValue,
            "NoStoppingFineRateValue",
            "IntroNoStoppingFineRateValue"
        );
        introNoStoppingFineTotalValue = FindTextByNames(
            introNoStoppingFineTotalValue,
            "NoStoppingFineTotalValue",
            "IntroNoStoppingFineTotalValue"
        );

        introInstructionLabel = FindTextByNames(
            introInstructionLabel,
            "IntroInstructionText"
        );
        destinationArrowRoot = FindGameObjectByNames(
            destinationArrowRoot,
            "DestinationArrowRoot",
            "DestinationArrow",
            "GateArrow",
            "UniversityArrow",
            "ArrowCanvas"
        );
        scoreLabel = FindTextByNames(scoreLabel, "ScoreLabel");

        assistedOpeningMessagePanel = FindGameObjectByNames(
            assistedOpeningMessagePanel,
            "AssistedOpeningMessagePanel"
        );

        assistedOpeningMessageLabel = FindTextByNames(
            assistedOpeningMessageLabel,
            "AssistedOpeningMessageLabel"
        );

        if (assistedOpeningMessageCloseButton == null)
        {
            GameObject closeButtonObject = FindGameObjectByNames(
                null,
                "CloseAssistedMessageButton",
                "AssistedOpeningCloseButton"
            );

            if (closeButtonObject != null)
                assistedOpeningMessageCloseButton =
                    closeButtonObject.GetComponent<Button>();
        }

        conventionalDrivingInfoRoot = FindGameObjectByNames(
            conventionalDrivingInfoRoot,
            "ConventionalDrivingInfo",
            "DrivingInfoPanel"
        );

        drivingDistanceLabel = FindTextByNames(
            drivingDistanceLabel,
            "DrivingDistanceLabel"
        );

        drivingTimeLabel = FindTextByNames(
            drivingTimeLabel,
            "DrivingTimeLabel"
        );

        remainingDrivingDistanceLabel = FindTextByNames(
            remainingDrivingDistanceLabel,
            "RemainingDrivingDistanceLabel"
        );

        selectedWalkingDistanceLabel = FindTextByNames(
            selectedWalkingDistanceLabel,
            "SelectedWalkingDistanceLabel"
        );

        resultTitleLabel = FindTextByNames(
            resultTitleLabel,
            "ResultTitleText",
            "ResultsTitleText",
            "ResultTitleLabel"
        );
        resultDetailLabel = FindTextByNames(
            resultDetailLabel,
            "ResultDetailText",
            "ResultsDetailText",
            "ResultDetailLabel"
        );
        resultScoreLabel = FindTextByNames(
            resultScoreLabel,
            "ResultScoreText",
            "ResultsScoreText",
            "ResultScoreLabel"
        );

        resultContinueButton = FindGameObjectByNames(
            resultContinueButton,
            "ResultContinueButton",
            "ContinueButton"
        );

        if (resultContinueButtonLabel == null && resultContinueButton != null)
            resultContinueButtonLabel = resultContinueButton.GetComponentInChildren<TMP_Text>(true);

        if (finalQuestionnaire == null)
        {
            FinalQuestionnaire[] questionnaires =
                FindObjectsByType<FinalQuestionnaire>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );

            if (questionnaires.Length > 0)
                finalQuestionnaire = questionnaires[0];
        }
    }

    private TMP_Text FindTextByNames(TMP_Text currentReference, params string[] names)
    {
        if (currentReference != null)
            return currentReference;

        TMP_Text[] allTexts = FindObjectsByType<TMP_Text>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (string expectedName in names)
        {
            foreach (TMP_Text textComponent in allTexts)
            {
                if (textComponent == null)
                    continue;

                if (string.Equals(
                    textComponent.gameObject.name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return textComponent;
                }
            }
        }

        return null;
    }

    private Image FindImageUnderGameObject(
        string parentObjectName,
        string childObjectName)
    {
        GameObject parent = FindGameObjectByNames(null, parentObjectName);

        if (parent == null)
            return null;

        Image[] images = parent.GetComponentsInChildren<Image>(true);

        foreach (Image image in images)
        {
            if (image == null)
                continue;

            if (string.Equals(
                image.gameObject.name,
                childObjectName,
                StringComparison.OrdinalIgnoreCase))
            {
                return image;
            }
        }

        return null;
    }

    private TMP_Text FindFirstTextUnderGameObject(
        string parentObjectName,
        string excludedNameFragment = null)
    {
        GameObject parent = FindGameObjectByNames(null, parentObjectName);

        if (parent == null)
            return null;

        TMP_Text[] texts = parent.GetComponentsInChildren<TMP_Text>(true);

        foreach (TMP_Text text in texts)
        {
            if (text == null)
                continue;

            if (!string.IsNullOrEmpty(excludedNameFragment) &&
                text.gameObject.name.IndexOf(
                    excludedNameFragment,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            return text;
        }

        return null;
    }

    private GameObject FindGameObjectByNames(GameObject currentReference, params string[] names)
    {
        if (currentReference != null)
            return currentReference;

        Transform[] allTransforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (string expectedName in names)
        {
            foreach (Transform candidate in allTransforms)
            {
                if (candidate == null)
                    continue;

                if (string.Equals(
                    candidate.gameObject.name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.gameObject;
                }
            }
        }

        return null;
    }

    private void SetLabelText(TMP_Text label, string value)
    {
        if (label == null)
            return;

        label.richText = true;
        label.text = value ?? string.Empty;
        label.SetVerticesDirty();
        label.SetLayoutDirty();

        if (forceRefreshTextMesh && label.gameObject.activeInHierarchy)
            label.ForceMeshUpdate(true, true);
    }

    private void ValidateRequiredUIReferences()
    {
        LogMissingReference(scenarioLabel, "ScenarioLabel");
        LogMissingReference(roundLabel, "RoundLabel");
        LogMissingReference(timerLabel, "TimerLabel");

        if (timerBarUI == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] Optional UI reference is missing: TimerBarUI. " +
                "Attach TimerBarUI to TimePanel or assign it in the Inspector."
            );
        }

        if (assistedOpeningMessagePanel == null || assistedOpeningMessageLabel == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] AssistedOpeningMessagePanel/Label is missing. " +
                "The Assisted opening messages and overtime warning use this same UI."
            );
        }

        if (speedSelectorUI == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] Optional UI reference is missing: SpeedSelectorUI. " +
                "Attach SpeedSelectorUI to SpeedControlPanel or assign it in the Inspector."
            );
        }

        LogMissingReference(modeLabel, "ModeLabel");

        // New right-side scenario table. The old standalone information labels
        // are no longer required and may remain disabled in the hierarchy.
        LogMissingReference(dwellTimeValue, "DwellTimeValue");
        LogMissingReference(legalOccupancyValue, "LegalOccupancyValue");
        LogMissingReference(illegalOccupancyValue, "IllegalOccupancyValue");
        LogMissingReference(smartFeeValue, "SmartFeeValue");
        LogMissingReference(noParkingFineValue, "NoParkingFineValue");
        LogMissingReference(noStoppingFineValue, "NoStoppingFineValue");
        LogMissingReference(citationValue, "CitationValue");

        if (citationValueBox == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] Optional UI reference is missing: CitationValueBox. " +
                "The ValueBox under Row_CitationProbability will not change colour."
            );
        }

        LogMissingReference(noFineIllegalLabel, "NoFineIllegalLabel");
        LogMissingReference(onTimeRewardLabel, "OnTimeRewardLabel");
        LogMissingReference(scoreLabel, "ScoreLabel");
        LogMissingReference(resultPanel, "ResultPanel");
        LogMissingReference(resultTitleLabel, "ResultTitleText");
        LogMissingReference(resultDetailLabel, "ResultDetailText");
        LogMissingReference(resultScoreLabel, "ResultScoreText");

        if (scenarioIntroPanel == null)
            Debug.LogWarning("[ScenarioManager] Optional UI reference is missing: ScenarioIntroPanel.");


        if (scenarioIntroPanel != null)
        {
            LogMissingReference(introTitleLabel, "IntroTitleText");
            LogMissingReference(introModeLabel, "IntroModeText");
            LogMissingReference(introDwellValue, "IntroDwellValue");
            LogMissingReference(introTimeLimitValue, "IntroTimeLimitValue");
            LogMissingReference(introLegalOccupancyValue, "IntroLegalOccupancyValue");
            LogMissingReference(introIllegalOccupancyValue, "IntroIllegalOccupancyValue");
            LogMissingReference(introSmartFeeValue, "IntroSmartFeeValue");
            LogMissingReference(introIllegalFeeValue, "IntroIllegalFeeValue");
            LogMissingReference(introCitationValue, "IntroCitationValue");
            LogMissingReference(introFreeParkingRateValue, "FreeLegalParkingRateValue");
            LogMissingReference(introFreeParkingTotalValue, "FreeLegalParkingTotalValue");
            LogMissingReference(introNoParkingFineRateValue, "NoParkingFineRateValue");
            LogMissingReference(introNoParkingFineTotalValue, "NoParkingFineTotalValue");
            LogMissingReference(introNoStoppingFineRateValue, "NoStoppingFineRateValue");
            LogMissingReference(introNoStoppingFineTotalValue, "NoStoppingFineTotalValue");
            LogMissingReference(introInstructionLabel, "IntroInstructionText");
        }

        if (resultContinueButton == null)
            Debug.LogWarning("[ScenarioManager] Optional UI reference is missing: ResultContinueButton.");

        if (finalQuestionnaire == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] FinalQuestionnaire is missing. " +
                "Assign it before collecting survey data."
            );
        }
    }

    private static void LogMissingReference(UnityEngine.Object reference, string expectedName)
    {
        if (reference == null)
        {
            Debug.LogWarning(
                "[ScenarioManager] UI reference is missing: " + expectedName +
                ". Assign it in the Inspector or use this exact GameObject name."
            );
        }
    }

    // ---------------------------------------------------------------------
    // Observed / experienced choice-set tracking
    // ---------------------------------------------------------------------

    private void BeginParkingViewTracking()
    {
        if (parkingViewTracker == null)
            parkingViewTracker = FindFirstObjectByType<ParkingViewTracker>();

        if (parkingViewTracker == null)
        {
            Debug.LogError(
                "[ScenarioManager] ParkingViewTracker is missing. " +
                "Conventional viewed-choice-set data will not be valid."
            );
            return;
        }

        Transform targetCar = carTransform;

        if (targetCar == null && carParking != null)
            targetCar = carParking.transform;

        parkingViewTracker.BeginRound(
            participantId,
            studyVersion,
            buildVersion,
            currentRoundIndex,
            currentScenarioIndex,
            currentScenario,
            currentAssistedMode,
            targetCar,
            metersPerUnityUnit,
            carParking
        );
    }

    private void CompleteParkingViewTracking(
        ParkingSpot chosenSpot,
        bool timedOut)
    {
        if (parkingViewTracker == null)
            parkingViewTracker = ParkingViewTracker.Instance;

        if (parkingViewTracker == null)
            return;

        parkingViewTracker.CompleteRound(
            chosenSpot,
            timedOut
        );
    }

    // ---------------------------------------------------------------------
    // Logger compatibility
    // Supports both the newer research-grade logger and the older logger,
    // so replacing this manager does not force a logger rewrite immediately.
    // ---------------------------------------------------------------------

    private void TryBeginLoggerRound()
    {
        if (ParkingLogger.Instance == null)
            return;

        try
        {
            MethodInfo method = FindPublicMethod(ParkingLogger.Instance, "BeginRound");

            if (method == null)
                return;

            ParameterInfo[] parameters = method.GetParameters();
            string startNodeName = GetCurrentStartNodeName();

            if (parameters.Length == 16)
            {
                method.Invoke(ParkingLogger.Instance, new object[]
                {
                    participantId,
                    studyVersion,
                    buildVersion,
                    GetTotalRoundCount(),
                    currentRoundIndex,
                    currentScenarioIndex,
                    currentScenario,
                    currentAssistedMode,
                    conventionalFirst,
                    scenarioOrderLabel,
                    currentScenario.startPointIndex,
                    startNodeName,
                    scoreAtRoundStart,
                    destinationGate,
                    metersPerUnityUnit,
                    walkingSpeedMetersPerSecond
                });
            }
            else if (parameters.Length == 14)
            {
                method.Invoke(ParkingLogger.Instance, new object[]
                {
                    participantId,
                    studyVersion,
                    currentRoundIndex,
                    currentScenarioIndex,
                    currentScenario,
                    currentAssistedMode,
                    conventionalFirst,
                    scenarioOrderLabel,
                    currentScenario.startPointIndex,
                    startNodeName,
                    scoreAtRoundStart,
                    destinationGate,
                    metersPerUnityUnit,
                    walkingSpeedMetersPerSecond
                });
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ScenarioManager] BeginRound logger call failed: " + exception.Message);
        }
    }

    private void TryCompleteLoggerRound(
        ParkingSpot chosenSpot,
        bool parked,
        bool timedOut,
        float scenarioElapsedSeconds,
        float movementOrSelectionTimeSeconds,
        float drivingTimeSeconds,
        float drivingDistanceMeters,
        float walkingDistanceMeters,
        float walkingTimeSeconds,
        bool onTimeGame,
        bool onTimeIncludingWalk,
        float realizedCostToman)
    {
        if (ParkingLogger.Instance == null)
            return;

        try
        {
            MethodInfo completeMethod = FindPublicMethod(ParkingLogger.Instance, "CompleteRound");

            if (completeMethod != null &&
                completeMethod.GetParameters().Length == 13)
            {
                completeMethod.Invoke(ParkingLogger.Instance, new object[]
                {
                    chosenSpot,
                    parked,
                    timedOut,
                    scenarioElapsedSeconds,
                    movementOrSelectionTimeSeconds,
                    drivingTimeSeconds,
                    drivingDistanceMeters,
                    walkingDistanceMeters,
                    walkingTimeSeconds,
                    onTimeGame,
                    onTimeIncludingWalk,
                    realizedCostToman,
                    currentScore
                });

                return;
            }

            if (completeMethod != null &&
                completeMethod.GetParameters().Length == 11)
            {
                completeMethod.Invoke(ParkingLogger.Instance, new object[]
                {
                    chosenSpot,
                    parked,
                    timedOut,
                    scenarioElapsedSeconds,
                    movementOrSelectionTimeSeconds,
                    walkingDistanceMeters,
                    walkingTimeSeconds,
                    onTimeGame,
                    onTimeIncludingWalk,
                    realizedCostToman,
                    currentScore
                });

                return;
            }

            MethodInfo legacyMethod = FindPublicMethod(ParkingLogger.Instance, "LogScenarioResult");

            if (legacyMethod != null && legacyMethod.GetParameters().Length == 9)
            {
                legacyMethod.Invoke(ParkingLogger.Instance, new object[]
                {
                    participantId,
                    currentRoundIndex,
                    currentScenario,
                    chosenSpot,
                    parked,
                    onTimeIncludingWalk,
                    movementOrSelectionTimeSeconds,
                    realizedCostToman,
                    currentScore
                });
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ScenarioManager] CompleteRound logger call failed: " + exception.Message);
        }
    }

    private void TryEndLoggerSession()
    {
        if (ParkingLogger.Instance == null)
            return;

        try
        {
            MethodInfo method = FindPublicMethod(ParkingLogger.Instance, "EndSession");

            if (method != null && method.GetParameters().Length == 2)
                method.Invoke(ParkingLogger.Instance, new object[] { true, cumulativeScore });
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ScenarioManager] EndSession logger call failed: " + exception.Message);
        }
    }

    private static MethodInfo FindPublicMethod(object target, string methodName)
    {
        if (target == null)
            return null;

        MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);

        foreach (MethodInfo method in methods)
        {
            if (method.Name == methodName)
                return method;
        }

        return null;
    }

    // ---------------------------------------------------------------------
    // Persian display helpers
    // ---------------------------------------------------------------------

    private static string FormatToman(float value)
    {
        return ParkingPolicy1405.FormatToman(value);
    }

    private static string FormatNumber(float value)
    {
        return ParkingPolicy1405.FormatDecimal(value, "0.#");
    }

    private static string FormatPercent(float zeroToOneValue)
    {
        return ParkingPolicy1405.FormatPercent(zeroToOneValue);
    }

    private static string FormatParkingDuration(float totalMinutes)
    {
        int safeMinutes = Mathf.Max(0, Mathf.RoundToInt(totalMinutes));
        int hours = safeMinutes / 60;
        int minutes = safeMinutes % 60;

        if (hours > 0 && minutes > 0)
        {
            return ToPersianDigits(hours.ToString(CultureInfo.InvariantCulture)) +
                   " ساعت و " +
                   ToPersianDigits(minutes.ToString(CultureInfo.InvariantCulture)) +
                   " دقیقه";
        }

        if (hours > 0)
        {
            return ToPersianDigits(hours.ToString(CultureInfo.InvariantCulture)) +
                   " ساعت";
        }

        return ToPersianDigits(minutes.ToString(CultureInfo.InvariantCulture)) +
               " دقیقه";
    }

    private static string FormatDuration(float seconds)
    {
        int safeSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int minutes = safeSeconds / 60;
        int remainingSeconds = safeSeconds % 60;
        return ParkingPolicy1405.FormatClock(minutes, remainingSeconds);
    }

    private static string FormatSignedPoints(int value)
    {
        string sign = value > 0 ? "+" : string.Empty;
        return ToPersianDigits(sign + value.ToString(CultureInfo.InvariantCulture));
    }

    private static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        char[] western = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        char[] persian = { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };

        StringBuilder builder = new StringBuilder(input.Length);

        foreach (char character in input)
        {
            int index = Array.IndexOf(western, character);
            builder.Append(index >= 0 ? persian[index] : character);
        }

        return builder.ToString();
    }
}