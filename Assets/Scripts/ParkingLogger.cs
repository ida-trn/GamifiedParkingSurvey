using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Storage-optimized research logger for the gamified parking survey.
///
/// Per participant/session it now stores:
/// 1) parking_rounds            -> 1 row per completed/timed-out round
/// 2) parking_alternatives6     -> 6 aggregate A1-A6 rows per round
/// 3) parking_spots             -> 1 COMPACT occupancy row per scenario pair
///                                (12 rows for a 12-scenario study, not 47,496)
/// 4) parking_spot_views        -> 1 COMPACT route row per Conventional round
///                                (RoadNode route + compact 30/40/50 speed summary;
///                                 no spot-view rows and no high-frequency speed rows)
/// 5) parking_sessions          -> session_start + session_end
///
/// The collapsed A1-A5 table is intentionally no longer written/uploaded.
/// It can be derived later from A1-A6 by recoding A6 -> A5.
///
/// Static parking-space attributes are not repeated for every participant.
/// Use the context-menu command "Export Static Parking Spot Master CSV" once
/// for the study/build and archive that file with the research data.
/// </summary>
public class ParkingLogger : MonoBehaviour
{
    public static ParkingLogger Instance { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void PrizeDrawLinkUnitySession(string sessionId, string participantId, string buildVersion);
#endif

    private void NotifyPrizeDrawOfSession()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(participantId))
            return;
        try
        {
            PrizeDrawLinkUnitySession(sessionId, participantId, buildVersion);
        }
        catch (Exception ex)
        {
            // The prize draw is optional; never interrupt the study itself.
            Debug.LogWarning("[ParkingLogger] Prize draw link bridge unavailable: " + ex.Message);
        }
#endif
    }

    [Header("Output")]
    [Tooltip("A subfolder is created inside Application.persistentDataPath.")]
    public string outputSubfolder = "ParkingSurveyData";

    [Tooltip("Keep ON. Writes the canonical A1-A6 long-format alternative file/table. A1-A5 is derived later, not stored.")]
    public bool writeAlternativeFile = true;

    [Header("Compact route logging")]
    [Tooltip("Conventional route is sampled as the nearest RoadNode sequence. This replaces spot-by-spot view uploads.")]
    public bool writeConventionalRouteSummary = true;

    [Tooltip("Seconds between nearest-RoadNode samples during Conventional gameplay.")]
    [Min(0.02f)]
    public float routeSampleIntervalSeconds = 0.10f;

    [Header("Debug")]
    public bool printOutputPaths = true;

    public string SessionId => sessionId;
    public string RoundFilePath => roundFilePath;

    // Compatibility: canonical alternative file is now only A1-A6.
    public string AlternativeFilePath => alternative6FilePath;
    public string Alternative6FilePath => alternative6FilePath;

    // Compatibility only. No A1-A5 file is created anymore.
    public string Alternative5FilePath => string.Empty;

    // Compatibility: this now points to the compact scenario-occupancy file.
    public string SpotFilePath => occupancyFilePath;

    public string OccupancyFilePath => occupancyFilePath;
    public string RouteFilePath => routeFilePath;
    public string SessionFilePath => sessionFilePath;

    private readonly CultureInfo invariant = CultureInfo.InvariantCulture;

    private string sessionId;
    private string participantId;
    private string studyVersion;
    private string buildVersion;
    private string sessionStartUtc;
    private DateTime sessionStartTimeUtc;

    private string outputDirectory;
    private string roundFilePath;
    private string alternative6FilePath;
    private string occupancyFilePath;
    private string routeFilePath;
    private string sessionFilePath;

    private bool sessionInitialized;
    private bool sessionEnded;
    private int expectedRoundCount;
    private int completedRoundCount;
    private float cumulativeScoreAfterRounds;

    private RoundSnapshot activeRound;

    // One occupancy snapshot per paired scenario, not once per mode.
    private readonly HashSet<int> occupancyLoggedScenarioIndices =
        new HashSet<int>();

    // Conventional route summary.
    private RoadNode[] cachedRoadNodes;
    private Transform routeCarTransform;
    private CarNodeFollower routeCarFollower;
    private SpeedSelectorUI routeSpeedSelector;
    private float routeStartedAtTime;
    private float nextRouteSampleTime;
    private int routeStartSpeedKmh;
    private int routeSpeedChangeCount;
    private readonly List<string> routeNodeNames = new List<string>();
    private readonly List<string> routeNodePositions = new List<string>();
    private readonly List<string> routeNodeElapsedSeconds = new List<string>();
    private readonly List<string> routeNodeSelectedSpeeds = new List<string>();
    private readonly List<string> routeSpeedEvents = new List<string>();

    // Supabase batch buffer for A1-A6 alternatives.
    private List<List<string>> pendingAlternative6UploadRows;

    private const string RoundHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion,SessionStartUtc,RoundStartUtc,RoundEndUtc," +
        "TaskIndex,ScenarioIndex,ScenarioId,Mode,ModeOrder,ScenarioOrder," +
        "StartPointIndex,StartNode,StartNetworkDistanceToGateM,RoundStatus,Parked,TimedOut,ChosenAlternative6,SelectedSpotId," +
        "Region,ParkingType,ParkingAreaKind,Street,StreetSide,IllegalReason,LegalFeature," +
        "SelectedIsSmartParking,SelectedTowEligible,SelectedParkingFeeToman,SmartParkingFeeForDwell,CitationProbabilityStay," +
        "DwellTimeMin,TimeLimitMin,LegalFeePerHour,FinePolicy,FineMultiplier,SelectedBaseIllegalFineToman,SelectedAppliedIllegalFineToman,CitationProbability,OnTimeReward," +
        "TargetLegalOccupancy,TargetIllegalOccupancy,ActualLegalOccupancy,ActualIllegalOccupancy,ScarcityLevel," +
        "OccupancyMethod,A1OccupancyWeight,A2OccupancyWeight,A3OccupancyWeight,A4OccupancyWeight,A5OccupancyWeight,A6OccupancyWeight," +
        "TotalA1,AvailableA1,TotalA2,AvailableA2,TotalA3,AvailableA3,TotalA4,AvailableA4," +
        "TotalA5,AvailableA5,TotalA6,AvailableA6," +
        "ScenarioElapsedSec,MovementOrSelectionTimeSec,DrivingSearchTimeSec,DrivingTimeSec,DrivingDistanceM,DecisionTimeSec," +
        "WalkingDistanceM,WalkingTimeSec,TotalEstimatedTaskTimeSec,OnTimeGame,OnTimeIncludingWalk," +
        "RoundCost,ScoreBefore,ScoreAfter,CumulativeScoreAfter,UnityVersion,Platform";

    private const string AlternativeHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion,AlternativeLogic,RoundStartUtc,RoundEndUtc," +
        "TaskIndex,ScenarioIndex,ScenarioId,Mode,ModeOrder,ScenarioOrder,RoundStatus," +
        "StartPointIndex,StartNode,StartNetworkDistanceToGateM,ChosenSpotId,AlternativeId,Region,ParkingType,Chosen,Available," +
        "TotalSpotCount,OccupiedSpotCount,AvailableSpotCount,ActualOccupancyRate,TargetOccupancyRate,ScarcityLevel," +
        "OccupancyMethod,OccupancyWeight," +
        "SmartSpotCount,AvailableSmartSpotCount,TowEligibleSpotCount,AvailableTowEligibleSpotCount," +
        "MinWalkingDistanceM,MeanWalkingDistanceM,MaxWalkingDistanceM," +
        "MinWalkingTimeSec,MeanWalkingTimeSec,MaxWalkingTimeSec," +
        "DwellTimeMin,TimeLimitMin,LegalFeePerHour,FinePolicy,FineMultiplier,CitationProbability,CitationProbabilityStay,ExpectedParkingCost," +
        "ScenarioElapsedSec,MovementOrSelectionTimeSec,DrivingTimeChosenSec,DrivingDistanceChosenM,WalkingDistanceChosenM,WalkingTimeChosenSec," +
        "TotalEstimatedTaskTimeSec,OnTimeGame,OnTimeIncludingWalk,RoundCost,ScoreBefore,ScoreAfter,TimedOut";

    // Reuses the existing parking_spots Supabase table, but one row now
    // represents the whole paired-scenario occupancy state.
    private const string OccupancyHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion," +
        "TaskIndex,ScenarioIndex,ScenarioId,Mode,SpotId,OccupiedAtStart,AvailableAtStart,Chosen,TimedOut," +
        "RecordType,SnapshotUtc,TotalModeledSpots,OccupiedSpotCount,ScarcityLevel," +
        "OccupancyMethod,A1OccupancyWeight,A2OccupancyWeight,A3OccupancyWeight,A4OccupancyWeight,A5OccupancyWeight,A6OccupancyWeight," +
        "TotalA1,OccupiedA1,TotalA2,OccupiedA2,TotalA3,OccupiedA3," +
        "TotalA4,OccupiedA4,TotalA5,OccupiedA5,TotalA6,OccupiedA6," +
        "OccupiedSpotIds";

    // Reuses the existing parking_spot_views Supabase table, but one row now
    // stores the full RoadNode route for one Conventional round.
    private const string RouteHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion," +
        "TaskIndex,ScenarioIndex,ScenarioId,Mode,SpotId,Viewed,ViewedAvailable,Chosen," +
        "RecordType,RoundStartUtc,RoundEndUtc,RouteRule,RouteNodeCount," +
        "RouteNodeNames,RouteNodePositionsXY,RouteNodeElapsedSec,DrivingDistanceM";

    private const string SessionHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion,SessionStartUtc,SessionEndUtc,CompletionStatus," +
        "CompletedNormally,ExpectedRoundCount,CompletedRoundCount,AllExpectedRoundsCompleted," +
        "FinalScore,DurationSec,UnityVersion,Platform,RoundFile,Alternative6File,OccupancyFile,RouteFile";

    private const string MasterSpotHeader =
        "SpotId,Alternative6,Region,ParkingType,ParkingAreaKind,Street,StreetSide,IllegalReason,LegalFeature," +
        "IsSmartParking,TowEligible,WorldX,WorldY,WalkingDistanceM,WalkingTimeSec";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (activeRound == null ||
            activeRound.assistedMode ||
            !writeConventionalRouteSummary)
        {
            return;
        }

        if (Time.time < nextRouteSampleTime)
            return;

        nextRouteSampleTime =
            Time.time + Mathf.Max(0.02f, routeSampleIntervalSeconds);

        CaptureNearestRouteNode();
    }

    private void OnApplicationQuit()
    {
        if (sessionInitialized && !sessionEnded)
            EndSession(false, float.NaN);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Called once after occupancy is applied and immediately before participant
    /// control begins. The full spot snapshot remains IN MEMORY only so A1-A6
    /// aggregate statistics can still be computed exactly.
    /// </summary>
    public void BeginRound(
        string participantIdValue,
        string studyVersionValue,
        string buildVersionValue,
        int expectedRoundCountValue,
        int roundIndex,
        int scenarioIndex,
        ParkingScenario scenario,
        bool assistedMode,
        bool conventionalFirst,
        string scenarioOrder,
        int startPointIndex,
        string startNodeName,
        float scoreBefore,
        Transform destinationGate,
        float metersPerUnityUnit,
        float walkingSpeedMetersPerSecond)
    {
        if (scenario == null)
        {
            Debug.LogWarning("[ParkingLogger] BeginRound ignored because scenario is null.");
            return;
        }

        EnsureSession(
            participantIdValue,
            studyVersionValue,
            buildVersionValue,
            expectedRoundCountValue
        );

        float startNetworkDistanceToGateM =
            CalculateStartNetworkDistanceToGateMeters(
                startPointIndex,
                startNodeName,
                destinationGate,
                metersPerUnityUnit
            );

        ParkingOccupancyManager occupancyManager =
            FindFirstObjectByType<ParkingOccupancyManager>();

        bool weightedExactTotals =
            occupancyManager != null &&
            occupancyManager.useWeightedExactTotals;

        string occupancyMethod =
            occupancyManager == null
                ? "Unknown"
                : weightedExactTotals
                    ? "ScarcityCalibratedExactTotalV1"
                    : "LegacyIndependentBernoulli";

        float a1OccupancyWeight =
            occupancyManager != null
                ? occupancyManager.a1InnerLegalWeight
                : -1f;

        float a2OccupancyWeight =
            occupancyManager != null
                ? occupancyManager.a2InnerIllegalWeight
                : -1f;

        float a3OccupancyWeight =
            occupancyManager != null
                ? occupancyManager.a3OuterLegalWeight
                : -1f;

        float a4OccupancyWeight =
            occupancyManager != null
                ? occupancyManager.a4OuterIllegalWeight
                : -1f;

        float a5OccupancyWeight =
            occupancyManager != null
                ? occupancyManager.a5FarLegalWeight
                : -1f;

        float a6OccupancyWeight =
            occupancyManager != null
                ? occupancyManager.a6FarIllegalWeight
                : -1f;

        activeRound = new RoundSnapshot
        {
            taskIndex = roundIndex,
            scenarioIndex = scenarioIndex,
            scenarioId = scenario.scenarioId,
            assistedMode = assistedMode,
            modeOrder = conventionalFirst ? "ConventionalFirst" : "AssistedFirst",
            scenarioOrder = string.IsNullOrWhiteSpace(scenarioOrder) ? "Fixed01" : scenarioOrder,
            startPointIndex = startPointIndex,
            startNodeName = startNodeName ?? string.Empty,
            startNetworkDistanceToGateM = startNetworkDistanceToGateM,
            scoreBefore = scoreBefore,
            dwellTimeMin = scenario.dwellTimeMinutes,
            timeLimitMin = scenario.timeLimitMinutes,
            legalFeePerHour = scenario.legalFeePerHour,
            smartParkingFeeForDwell =
                ParkingPolicy1405.CalculateSmartParkingFeeToman(
                    scenario.dwellTimeMinutes
                ),
            finePolicy = ParkingPolicy1405.GetFinePolicyName(
                scenario.illegalFinePolicy
            ),
            fineMultiplier = scenario.GetIllegalFineMultiplier(),
            citationProbability = scenario.citationProbability,
            citationProbabilityStay =
                ParkingScoreCalculator.CalculateAdjustedCitationProbability(
                    scenario.citationProbability,
                    scenario.dwellTimeMinutes
                ),
            onTimeReward = scenario.onTimeReward,
            targetLegalOccupancy = scenario.legalOccupancyRate,
            targetIllegalOccupancy = scenario.illegalOccupancyRate,
            scarcityLevel = scenario.scarcityLevel.ToString(),
            occupancyMethod = occupancyMethod,
            a1OccupancyWeight = a1OccupancyWeight,
            a2OccupancyWeight = a2OccupancyWeight,
            a3OccupancyWeight = a3OccupancyWeight,
            a4OccupancyWeight = a4OccupancyWeight,
            a5OccupancyWeight = a5OccupancyWeight,
            a6OccupancyWeight = a6OccupancyWeight,
            roundStartUtc = DateTime.UtcNow.ToString("o", invariant),
            spots = new List<SpotSnapshot>()
        };

        ParkingSpot[] allSpots =
            FindObjectsByType<ParkingSpot>(FindObjectsSortMode.None);

        foreach (ParkingSpot spot in allSpots)
        {
            if (spot == null || spot.IsPhysicallyBlockedParking())
                continue;

            string alternative6 = spot.GetAlternativeId();

            if (!IsModeledAlternative(alternative6))
                continue;

            float walkingDistance =
                Mathf.Max(0f, spot.walkingDistanceMeters);

            float walkingTime =
                Mathf.Max(0f, spot.walkingTimeSeconds);

            if (walkingTime <= 0f &&
                walkingDistance > 0f &&
                walkingSpeedMetersPerSecond > 0f)
            {
                walkingTime =
                    walkingDistance / walkingSpeedMetersPerSecond;
            }

            bool isSmartParking =
                ParkingPolicy1405.IsSmartParking(spot);

            bool towEligible =
                ParkingPolicy1405.IsTowWarningEligible(spot);

            bool isLegalSpot =
                spot.parkingType == ParkingType.Legal;

            float spotParkingFeeToman =
                isSmartParking
                    ? activeRound.smartParkingFeeForDwell
                    : 0f;

            int baseIllegalFineToman =
                ParkingPolicy1405.GetBaseIllegalFineToman(spot);

            int appliedIllegalFineToman =
                ParkingPolicy1405.GetAppliedIllegalFineToman(
                    spot,
                    activeRound.fineMultiplier
                );

            float expectedParkingCostToman =
                isLegalSpot
                    ? spotParkingFeeToman
                    : appliedIllegalFineToman *
                      activeRound.citationProbabilityStay;

            activeRound.spots.Add(
                new SpotSnapshot
                {
                    spotId = spot.spotId,
                    alternative6 = alternative6,
                    region = spot.GetRegionName(),
                    parkingType = spot.GetParkingTypeName(),
                    parkingAreaKind = spot.GetParkingAreaKindName(),
                    street = spot.streetName,
                    streetSide = spot.GetStreetSideName(),
                    illegalReason = spot.GetIllegalReasonName(),
                    legalFeature = spot.GetLegalParkingFeatureName(),
                    isSmartParking = isSmartParking,
                    towEligible = towEligible,
                    spotParkingFeeToman = spotParkingFeeToman,
                    baseIllegalFineToman = baseIllegalFineToman,
                    appliedIllegalFineToman = appliedIllegalFineToman,
                    expectedParkingCostToman = expectedParkingCostToman,
                    occupiedAtStart = spot.isOccupied,
                    walkingDistanceM = walkingDistance,
                    walkingTimeSec = walkingTime
                }
            );
        }

        // Save exact occupancy only once for the scenario pair.
        if (!occupancyLoggedScenarioIndices.Contains(scenarioIndex))
        {
            WriteCompactOccupancySnapshot();
            occupancyLoggedScenarioIndices.Add(scenarioIndex);
        }

        BeginRouteCapture();

        Debug.Log(
            $"[ParkingLogger] Round snapshot ready | participant={participantId}, " +
            $"session={sessionId}, task={roundIndex + 1}, scenario={scenario.scenarioId}, " +
            $"mode={(assistedMode ? "Assisted" : "Conventional")}, " +
            $"modeledSpots={activeRound.spots.Count}"
        );
    }

    public void CompleteRound(
        ParkingSpot chosenSpot,
        bool parked,
        bool timedOut,
        float scenarioElapsedSec,
        float movementOrSelectionTimeSec,
        float drivingTimeSec,
        float drivingDistanceM,
        float walkingDistanceM,
        float walkingTimeSec,
        bool onTimeGame,
        bool onTimeIncludingWalk,
        float roundCost,
        float scoreAfter)
    {
        if (!sessionInitialized)
        {
            Debug.LogWarning(
                "[ParkingLogger] CompleteRound called before session initialization."
            );
            return;
        }

        if (activeRound == null)
        {
            Debug.LogWarning(
                "[ParkingLogger] CompleteRound called without an active round."
            );
            return;
        }

        // Capture the final nearest node before writing the route summary.
        if (!activeRound.assistedMode && writeConventionalRouteSummary)
            CaptureNearestRouteNode();

        float totalEstimatedTaskTimeSec =
            scenarioElapsedSec + walkingTimeSec;

        float safeDrivingTimeSec =
            activeRound.assistedMode
                ? 0f
                : Mathf.Max(0f, drivingTimeSec);

        float safeDrivingDistanceM =
            activeRound.assistedMode
                ? 0f
                : Mathf.Max(0f, drivingDistanceM);

        float decisionTimeSec =
            activeRound.assistedMode
                ? Mathf.Max(0f, movementOrSelectionTimeSec)
                : Mathf.Max(
                    0f,
                    scenarioElapsedSec - safeDrivingTimeSec
                );

        string chosenAlternative6 =
            chosenSpot != null
                ? chosenSpot.GetAlternativeId()
                : string.Empty;

        AlternativeStats a1 = BuildAlternativeStats("A1");
        AlternativeStats a2 = BuildAlternativeStats("A2");
        AlternativeStats a3 = BuildAlternativeStats("A3");
        AlternativeStats a4 = BuildAlternativeStats("A4");
        AlternativeStats a5 = BuildAlternativeStats("A5");
        AlternativeStats a6 = BuildAlternativeStats("A6");

        string roundEndUtc =
            DateTime.UtcNow.ToString("o", invariant);

        string roundStatus =
            timedOut
                ? "TimedOut"
                : parked
                    ? "Parked"
                    : "NotParked";

        int legalTotalCount = 0;
        int legalOccupiedCount = 0;
        int illegalTotalCount = 0;
        int illegalOccupiedCount = 0;

        foreach (SpotSnapshot snapshot in activeRound.spots)
        {
            bool legal = string.Equals(
                snapshot.parkingType,
                "Legal",
                StringComparison.OrdinalIgnoreCase
            );

            if (legal)
            {
                legalTotalCount++;
                if (snapshot.occupiedAtStart)
                    legalOccupiedCount++;
            }
            else
            {
                illegalTotalCount++;
                if (snapshot.occupiedAtStart)
                    illegalOccupiedCount++;
            }
        }

        float actualLegalOccupancy =
            legalTotalCount > 0
                ? (float)legalOccupiedCount / legalTotalCount
                : 0f;

        float actualIllegalOccupancy =
            illegalTotalCount > 0
                ? (float)illegalOccupiedCount / illegalTotalCount
                : 0f;

        string chosenSpotId =
            chosenSpot != null
                ? chosenSpot.spotId
                : string.Empty;

        bool selectedIsSmartParking =
            ParkingPolicy1405.IsSmartParking(chosenSpot);

        bool selectedTowEligible =
            ParkingPolicy1405.IsTowWarningEligible(chosenSpot);

        float selectedParkingFeeToman =
            selectedIsSmartParking
                ? activeRound.smartParkingFeeForDwell
                : 0f;

        int selectedBaseIllegalFineToman =
            ParkingPolicy1405.GetBaseIllegalFineToman(chosenSpot);

        int selectedAppliedIllegalFineToman =
            ParkingPolicy1405.GetAppliedIllegalFineToman(
                chosenSpot,
                activeRound.fineMultiplier
            );

        // This field sits with the selected-spot fields in parking_rounds.
        // Store the effective stay-level citation probability for the CHOSEN
        // spot, not just the scenario treatment. Legal and no-financial-fine
        // illegal choices therefore correctly log 0 here.
        float selectedCitationProbabilityStay =
            chosenSpot != null &&
            chosenSpot.parkingType == ParkingType.Illegal &&
            !ParkingPolicy1405.IsNoFinancialFineIllegalSpot(chosenSpot)
                ? activeRound.citationProbabilityStay
                : 0f;

        cumulativeScoreAfterRounds += scoreAfter;

        List<string> row = new List<string>
        {
            participantId,
            sessionId,
            studyVersion,
            buildVersion,
            sessionStartUtc,
            activeRound.roundStartUtc,
            roundEndUtc,
            I(activeRound.taskIndex),
            I(activeRound.scenarioIndex),
            activeRound.scenarioId,
            activeRound.assistedMode ? "Assisted" : "Conventional",
            activeRound.modeOrder,
            activeRound.scenarioOrder,
            I(activeRound.startPointIndex),
            activeRound.startNodeName,
            FOptional(activeRound.startNetworkDistanceToGateM),
            roundStatus,
            B(parked),
            B(timedOut),
            chosenAlternative6,
            chosenSpotId,
            chosenSpot != null ? chosenSpot.GetRegionName() : string.Empty,
            chosenSpot != null ? chosenSpot.GetParkingTypeName() : string.Empty,
            chosenSpot != null ? chosenSpot.GetParkingAreaKindName() : string.Empty,
            chosenSpot != null ? chosenSpot.streetName : string.Empty,
            chosenSpot != null ? chosenSpot.GetStreetSideName() : string.Empty,
            chosenSpot != null ? chosenSpot.GetIllegalReasonName() : string.Empty,
            chosenSpot != null ? chosenSpot.GetLegalParkingFeatureName() : string.Empty,
            B(selectedIsSmartParking),
            B(selectedTowEligible),
            F(selectedParkingFeeToman),
            F(activeRound.smartParkingFeeForDwell),
            F(selectedCitationProbabilityStay),
            F(activeRound.dwellTimeMin),
            F(activeRound.timeLimitMin),
            F(activeRound.legalFeePerHour),
            activeRound.finePolicy,
            F(activeRound.fineMultiplier),
            I(selectedBaseIllegalFineToman),
            I(selectedAppliedIllegalFineToman),
            F(activeRound.citationProbability),
            F(activeRound.onTimeReward),
            F(activeRound.targetLegalOccupancy),
            F(activeRound.targetIllegalOccupancy),
            F(actualLegalOccupancy),
            F(actualIllegalOccupancy),
            activeRound.scarcityLevel,

            activeRound.occupancyMethod,
            FOptional(activeRound.a1OccupancyWeight),
            FOptional(activeRound.a2OccupancyWeight),
            FOptional(activeRound.a3OccupancyWeight),
            FOptional(activeRound.a4OccupancyWeight),
            FOptional(activeRound.a5OccupancyWeight),
            FOptional(activeRound.a6OccupancyWeight),

            I(a1.totalCount), I(a1.availableCount),
            I(a2.totalCount), I(a2.availableCount),
            I(a3.totalCount), I(a3.availableCount),
            I(a4.totalCount), I(a4.availableCount),
            I(a5.totalCount), I(a5.availableCount),
            I(a6.totalCount), I(a6.availableCount),

            F(scenarioElapsedSec),
            F(movementOrSelectionTimeSec),
            F(safeDrivingTimeSec),
            F(safeDrivingTimeSec),
            F(safeDrivingDistanceM),
            F(decisionTimeSec),
            F(walkingDistanceM),
            F(walkingTimeSec),
            F(totalEstimatedTaskTimeSec),
            B(onTimeGame),
            B(onTimeIncludingWalk),
            F(roundCost),
            F(activeRound.scoreBefore),
            F(scoreAfter),
            F(cumulativeScoreAfterRounds),
            Application.unityVersion,
            Application.platform.ToString()
        };

        AppendCsvRow(roundFilePath, row);

        UploadRowsToSupabasePriority(
            "parking_rounds",
            RoundHeader,
            new List<List<string>> { row }
        );

        if (writeAlternativeFile)
        {
            pendingAlternative6UploadRows =
                new List<List<string>>();

            WriteAlternativeRow(
                alternative6FilePath,
                a1, chosenAlternative6, chosenSpotId,
                roundStatus, roundEndUtc, timedOut,
                scenarioElapsedSec, movementOrSelectionTimeSec,
                safeDrivingTimeSec, safeDrivingDistanceM,
                walkingDistanceM, walkingTimeSec,
                totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk,
                roundCost, scoreAfter
            );

            WriteAlternativeRow(
                alternative6FilePath,
                a2, chosenAlternative6, chosenSpotId,
                roundStatus, roundEndUtc, timedOut,
                scenarioElapsedSec, movementOrSelectionTimeSec,
                safeDrivingTimeSec, safeDrivingDistanceM,
                walkingDistanceM, walkingTimeSec,
                totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk,
                roundCost, scoreAfter
            );

            WriteAlternativeRow(
                alternative6FilePath,
                a3, chosenAlternative6, chosenSpotId,
                roundStatus, roundEndUtc, timedOut,
                scenarioElapsedSec, movementOrSelectionTimeSec,
                safeDrivingTimeSec, safeDrivingDistanceM,
                walkingDistanceM, walkingTimeSec,
                totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk,
                roundCost, scoreAfter
            );

            WriteAlternativeRow(
                alternative6FilePath,
                a4, chosenAlternative6, chosenSpotId,
                roundStatus, roundEndUtc, timedOut,
                scenarioElapsedSec, movementOrSelectionTimeSec,
                safeDrivingTimeSec, safeDrivingDistanceM,
                walkingDistanceM, walkingTimeSec,
                totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk,
                roundCost, scoreAfter
            );

            WriteAlternativeRow(
                alternative6FilePath,
                a5, chosenAlternative6, chosenSpotId,
                roundStatus, roundEndUtc, timedOut,
                scenarioElapsedSec, movementOrSelectionTimeSec,
                safeDrivingTimeSec, safeDrivingDistanceM,
                walkingDistanceM, walkingTimeSec,
                totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk,
                roundCost, scoreAfter
            );

            WriteAlternativeRow(
                alternative6FilePath,
                a6, chosenAlternative6, chosenSpotId,
                roundStatus, roundEndUtc, timedOut,
                scenarioElapsedSec, movementOrSelectionTimeSec,
                safeDrivingTimeSec, safeDrivingDistanceM,
                walkingDistanceM, walkingTimeSec,
                totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk,
                roundCost, scoreAfter
            );

            UploadRowsToSupabasePriority(
                "parking_alternatives6",
                AlternativeHeader,
                pendingAlternative6UploadRows
            );

            pendingAlternative6UploadRows = null;
        }

        WriteCompactRouteSummary(
            roundEndUtc,
            safeDrivingDistanceM
        );

        completedRoundCount++;
        activeRound = null;
        ResetRouteCapture();
    }

    public void EndSession(
        bool completedNormally,
        float finalScore)
    {
        if (!sessionInitialized || sessionEnded)
            return;

        sessionEnded = true;

        DateTime endUtc = DateTime.UtcNow;

        double durationSec =
            Math.Max(
                0d,
                (endUtc - sessionStartTimeUtc).TotalSeconds
            );

        bool allExpectedRoundsCompleted =
            expectedRoundCount <= 0
                ? completedNormally
                : completedRoundCount >= expectedRoundCount;

        string completionStatus =
            completedNormally &&
            allExpectedRoundsCompleted
                ? "Completed"
                : "Incomplete";

        List<string> row = new List<string>
        {
            participantId,
            sessionId,
            studyVersion,
            buildVersion,
            sessionStartUtc,
            endUtc.ToString("o", invariant),
            completionStatus,
            B(completedNormally),
            I(expectedRoundCount),
            I(completedRoundCount),
            B(allExpectedRoundsCompleted),
            float.IsNaN(finalScore)
                ? string.Empty
                : F(finalScore),
            durationSec.ToString("0.###", invariant),
            Application.unityVersion,
            Application.platform.ToString(),
            Path.GetFileName(roundFilePath),
            Path.GetFileName(alternative6FilePath),
            Path.GetFileName(occupancyFilePath),
            Path.GetFileName(routeFilePath)
        };

        AppendCsvRow(sessionFilePath, row);

        UploadRowsToSupabasePriority(
            "parking_sessions",
            SessionHeader,
            new List<List<string>> { row }
        );

        // A final opportunity to link the optional pre-survey phone entry.
        NotifyPrizeDrawOfSession();

        Debug.Log(
            $"[ParkingLogger] Session ended | participant={participantId}, " +
            $"session={sessionId}, completedNormally={completedNormally}, " +
            $"rounds={completedRoundCount}"
        );
    }

    [ContextMenu("Print Output Folder")]
    public void PrintOutputFolder()
    {
        string folder =
            sessionInitialized
                ? outputDirectory
                : Path.Combine(
                    Application.persistentDataPath,
                    outputSubfolder
                );

        Debug.Log(
            "[ParkingLogger] Output folder: " +
            folder
        );
    }

    /// <summary>
    /// Run once in the Editor for the frozen study map. This file contains
    /// static spot properties/coordinates and should be archived once, not
    /// duplicated for every participant.
    /// </summary>
    [ContextMenu("Export Static Parking Spot Master CSV")]
    public void ExportStaticParkingSpotMasterCsv()
    {
        WalkingNetworkCalculator calculator =
            FindFirstObjectByType<WalkingNetworkCalculator>();

        if (calculator != null)
            calculator.RecalculateAllParkingSpots();

        string folder = Path.Combine(
            Application.persistentDataPath,
            outputSubfolder
        );

        Directory.CreateDirectory(folder);

        string version = ResolveCurrentBuildVersion();
        string safeVersion = MakeSafeFileName(version);

        string path = Path.Combine(
            folder,
            "parking_spot_master_" +
            safeVersion + ".csv"
        );

        File.WriteAllText(
            path,
            MasterSpotHeader + Environment.NewLine,
            new UTF8Encoding(true)
        );

        ParkingSpot[] spots =
            FindObjectsByType<ParkingSpot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        List<ParkingSpot> modeled =
            new List<ParkingSpot>();

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null || spot.IsPhysicallyBlockedParking())
                continue;

            if (IsModeledAlternative(
                    spot.GetAlternativeId()))
            {
                modeled.Add(spot);
            }
        }

        modeled.Sort(
            (a, b) => string.Compare(
                a != null ? a.spotId : string.Empty,
                b != null ? b.spotId : string.Empty,
                StringComparison.Ordinal
            )
        );

        foreach (ParkingSpot spot in modeled)
        {
            List<string> row = new List<string>
            {
                spot.spotId ?? string.Empty,
                spot.GetAlternativeId(),
                spot.GetRegionName(),
                spot.GetParkingTypeName(),
                spot.GetParkingAreaKindName(),
                spot.streetName ?? string.Empty,
                spot.GetStreetSideName(),
                spot.GetIllegalReasonName(),
                spot.GetLegalParkingFeatureName(),
                B(ParkingPolicy1405.IsSmartParking(spot)),
                B(ParkingPolicy1405.IsTowWarningEligible(spot)),
                F(spot.transform.position.x),
                F(spot.transform.position.y),
                F(Mathf.Max(0f, spot.walkingDistanceMeters)),
                F(Mathf.Max(0f, spot.walkingTimeSeconds))
            };

            AppendCsvRow(path, row);
        }

        Debug.Log(
            $"[ParkingLogger] Static parking master exported | " +
            $"spots={modeled.Count}, path={path}"
        );
    }

    private void EnsureSession(
        string participantIdValue,
        string studyVersionValue,
        string buildVersionValue,
        int expectedRoundCountValue)
    {
        string requestedParticipant =
            string.IsNullOrWhiteSpace(participantIdValue)
                ? "UNKNOWN"
                : participantIdValue.Trim();

        if (sessionInitialized)
        {
            bool sameParticipant =
                string.Equals(
                    participantId,
                    requestedParticipant,
                    StringComparison.OrdinalIgnoreCase
                );

            string requestedStudyVersion =
                string.IsNullOrWhiteSpace(studyVersionValue)
                    ? "1.0"
                    : studyVersionValue.Trim();

            string requestedBuildVersion =
                string.IsNullOrWhiteSpace(buildVersionValue)
                    ? Application.version
                    : buildVersionValue.Trim();

            bool sameStudyVersion =
                string.Equals(
                    studyVersion,
                    requestedStudyVersion,
                    StringComparison.OrdinalIgnoreCase
                );

            bool sameBuildVersion =
                string.Equals(
                    buildVersion,
                    requestedBuildVersion,
                    StringComparison.OrdinalIgnoreCase
                );

            if (!sessionEnded &&
                sameParticipant &&
                sameStudyVersion &&
                sameBuildVersion)
            {
                return;
            }

            if (!sessionEnded)
                EndSession(false, float.NaN);

            ResetSessionState();
        }

        participantId = requestedParticipant;

        studyVersion =
            string.IsNullOrWhiteSpace(studyVersionValue)
                ? "1.0"
                : studyVersionValue.Trim();

        buildVersion =
            string.IsNullOrWhiteSpace(buildVersionValue)
                ? Application.version
                : buildVersionValue.Trim();

        expectedRoundCount =
            Mathf.Max(0, expectedRoundCountValue);

        cumulativeScoreAfterRounds = 0f;
        occupancyLoggedScenarioIndices.Clear();

        if (ParticipantSession.Instance != null)
        {
            ParticipantSession.Instance.EnsureSessionIdentity();

            sessionId =
                ParticipantSession.Instance.sessionId;

            if (!DateTime.TryParse(
                    ParticipantSession.Instance.sessionStartUtc,
                    invariant,
                    DateTimeStyles.RoundtripKind,
                    out sessionStartTimeUtc))
            {
                sessionStartTimeUtc = DateTime.UtcNow;
            }

            sessionStartTimeUtc =
                sessionStartTimeUtc.ToUniversalTime();

            sessionStartUtc =
                sessionStartTimeUtc.ToString(
                    "o",
                    invariant
                );
        }
        else
        {
            sessionStartTimeUtc = DateTime.UtcNow;

            sessionStartUtc =
                sessionStartTimeUtc.ToString(
                    "o",
                    invariant
                );

            string shortGuid =
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 8);

            sessionId =
                sessionStartTimeUtc.ToString(
                    "yyyyMMdd_HHmmss_fff",
                    invariant
                ) +
                "_" +
                shortGuid;
        }

        outputDirectory =
            Path.Combine(
                Application.persistentDataPath,
                outputSubfolder
            );

        Directory.CreateDirectory(outputDirectory);

        string safeParticipant =
            MakeSafeFileName(participantId);

        string fileStem =
            safeParticipant + "_" + sessionId;

        roundFilePath = Path.Combine(
            outputDirectory,
            "parking_rounds_" + fileStem + ".csv"
        );

        alternative6FilePath = Path.Combine(
            outputDirectory,
            "parking_alternatives6_" + fileStem + ".csv"
        );

        occupancyFilePath = Path.Combine(
            outputDirectory,
            "parking_scenario_occupancy_" + fileStem + ".csv"
        );

        routeFilePath = Path.Combine(
            outputDirectory,
            "parking_routes_" + fileStem + ".csv"
        );

        sessionFilePath = Path.Combine(
            outputDirectory,
            "parking_session_" + fileStem + ".csv"
        );

        File.WriteAllText(
            roundFilePath,
            RoundHeader + Environment.NewLine,
            new UTF8Encoding(true)
        );

        if (writeAlternativeFile)
        {
            File.WriteAllText(
                alternative6FilePath,
                AlternativeHeader + Environment.NewLine,
                new UTF8Encoding(true)
            );
        }

        File.WriteAllText(
            occupancyFilePath,
            OccupancyHeader + Environment.NewLine,
            new UTF8Encoding(true)
        );

        File.WriteAllText(
            routeFilePath,
            RouteHeader + Environment.NewLine,
            new UTF8Encoding(true)
        );

        File.WriteAllText(
            sessionFilePath,
            SessionHeader + Environment.NewLine,
            new UTF8Encoding(true)
        );

        sessionInitialized = true;

        const string sessionStartUploadHeader =
            "ParticipantId,SessionId,StudyVersion,BuildVersion,EventType,SessionStartUtc";

        List<string> sessionStartUploadRow =
            new List<string>
            {
                participantId,
                sessionId,
                studyVersion,
                buildVersion,
                "session_start",
                sessionStartUtc
            };

        UploadRowsToSupabasePriority(
            "parking_sessions",
            sessionStartUploadHeader,
            new List<List<string>>
            {
                sessionStartUploadRow
            }
        );

        // Link the optional phone number to this exact Unity-generated session.
        // The webpage retries until the queued session_start row is in Supabase.
        NotifyPrizeDrawOfSession();

        if (printOutputPaths)
        {
            Debug.Log(
                "[ParkingLogger] New optimized research session created.\n" +
                "Rounds: " + roundFilePath + "\n" +
                "Alternatives A1-A6: " + alternative6FilePath + "\n" +
                "Compact occupancy: " + occupancyFilePath + "\n" +
                "Conventional routes: " + routeFilePath + "\n" +
                "Session: " + sessionFilePath + "\n" +
                "A1-A5 file: NOT WRITTEN (derive A6 -> A5 later)"
            );
        }
    }

    private void ResetSessionState()
    {
        sessionId = null;
        participantId = null;
        studyVersion = null;
        buildVersion = null;
        sessionStartUtc = null;

        outputDirectory = null;
        roundFilePath = null;
        alternative6FilePath = null;
        occupancyFilePath = null;
        routeFilePath = null;
        sessionFilePath = null;

        sessionInitialized = false;
        sessionEnded = false;
        expectedRoundCount = 0;
        completedRoundCount = 0;
        cumulativeScoreAfterRounds = 0f;

        activeRound = null;

        occupancyLoggedScenarioIndices.Clear();
        ResetRouteCapture();
    }

    private void WriteCompactOccupancySnapshot()
    {
        if (activeRound == null ||
            activeRound.spots == null ||
            string.IsNullOrWhiteSpace(occupancyFilePath))
        {
            return;
        }

        List<string> occupiedIds =
            new List<string>();

        int[] totalByAlternative = new int[6];
        int[] occupiedByAlternative = new int[6];

        foreach (SpotSnapshot spot in activeRound.spots)
        {
            if (spot == null)
                continue;

            int altIndex =
                AlternativeIndex(spot.alternative6);

            if (altIndex >= 0)
            {
                totalByAlternative[altIndex]++;

                if (spot.occupiedAtStart)
                    occupiedByAlternative[altIndex]++;
            }

            if (spot.occupiedAtStart &&
                !string.IsNullOrWhiteSpace(spot.spotId))
            {
                occupiedIds.Add(spot.spotId);
            }
        }

        occupiedIds.Sort(StringComparer.Ordinal);

        List<string> row = new List<string>
        {
            participantId,
            sessionId,
            studyVersion,
            buildVersion,

            // The first task that presented this scenario pair.
            I(activeRound.taskIndex),
            I(activeRound.scenarioIndex),
            activeRound.scenarioId,
            "PairedScenario",

            // Existing table requires SpotId, so use a clear sentinel.
            "__OCCUPANCY__",

            "0",
            "0",
            "0",
            "0",

            "ScenarioOccupancySummary",
            DateTime.UtcNow.ToString("o", invariant),
            I(activeRound.spots.Count),
            I(occupiedIds.Count),
            activeRound.scarcityLevel,

            activeRound.occupancyMethod,
            FOptional(activeRound.a1OccupancyWeight),
            FOptional(activeRound.a2OccupancyWeight),
            FOptional(activeRound.a3OccupancyWeight),
            FOptional(activeRound.a4OccupancyWeight),
            FOptional(activeRound.a5OccupancyWeight),
            FOptional(activeRound.a6OccupancyWeight),

            I(totalByAlternative[0]), I(occupiedByAlternative[0]),
            I(totalByAlternative[1]), I(occupiedByAlternative[1]),
            I(totalByAlternative[2]), I(occupiedByAlternative[2]),
            I(totalByAlternative[3]), I(occupiedByAlternative[3]),
            I(totalByAlternative[4]), I(occupiedByAlternative[4]),
            I(totalByAlternative[5]), I(occupiedByAlternative[5]),

            string.Join(";", occupiedIds)
        };

        AppendCsvRow(occupancyFilePath, row);

        // Only 12 compact rows per full participant, so priority upload is fine.
        UploadRowsToSupabasePriority(
            "parking_spots",
            OccupancyHeader,
            new List<List<string>> { row }
        );

        Debug.Log(
            $"[ParkingLogger] Compact occupancy saved | " +
            $"scenario={activeRound.scenarioId}, " +
            $"modeled={activeRound.spots.Count}, occupied={occupiedIds.Count}"
        );
    }

    private void BeginRouteCapture()
    {
        ResetRouteCapture();

        if (activeRound == null ||
            activeRound.assistedMode ||
            !writeConventionalRouteSummary)
        {
            return;
        }

        ScenarioManager manager =
            FindFirstObjectByType<ScenarioManager>();

        if (manager != null)
        {
            routeCarTransform =
                manager.carTransform != null
                    ? manager.carTransform
                    : manager.carParking != null
                        ? manager.carParking.transform
                        : null;
        }

        if (routeCarTransform == null)
        {
            CarParkingInteraction interaction =
                CarParkingInteraction.Instance;

            if (interaction != null)
                routeCarTransform = interaction.transform;
        }

        cachedRoadNodes =
            FindObjectsByType<RoadNode>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        if (routeCarTransform != null)
        {
            routeCarFollower =
                routeCarTransform.GetComponent<CarNodeFollower>();

            if (routeCarFollower == null)
                routeCarFollower =
                    routeCarTransform.GetComponentInParent<CarNodeFollower>();
        }

        if (manager != null)
            routeSpeedSelector = manager.speedSelectorUI;

        if (routeSpeedSelector == null)
        {
            routeSpeedSelector =
                FindFirstObjectByType<SpeedSelectorUI>(
                    FindObjectsInactive.Include
                );
        }

        routeStartedAtTime = Time.time;
        nextRouteSampleTime = Time.time;
        routeStartSpeedKmh = GetCurrentRouteSelectedSpeedKmh();
        routeSpeedChangeCount = 0;

        if (routeSpeedSelector != null)
        {
            routeSpeedSelector.SpeedChanged -= OnRouteSpeedChanged;
            routeSpeedSelector.SpeedChanged += OnRouteSpeedChanged;
        }

        // Event zero records the initial 50/40/30 state at the participant's
        // starting location. Later entries are created only when the slider
        // actually changes, keeping storage extremely small.
        RecordRouteSpeedEvent(routeStartSpeedKmh);
        CaptureNearestRouteNode();
    }

    private void CaptureNearestRouteNode()
    {
        if (activeRound == null ||
            activeRound.assistedMode ||
            routeCarTransform == null ||
            cachedRoadNodes == null ||
            cachedRoadNodes.Length == 0)
        {
            return;
        }

        RoadNode nearest =
            FindNearestRoadNode(
                routeCarTransform.position,
                cachedRoadNodes
            );

        if (nearest == null)
            return;

        string nodeName =
            nearest.name ?? string.Empty;

        if (routeNodeNames.Count > 0 &&
            string.Equals(
                routeNodeNames[routeNodeNames.Count - 1],
                nodeName,
                StringComparison.Ordinal))
        {
            return;
        }

        routeNodeNames.Add(nodeName);

        routeNodePositions.Add(
            F(nearest.transform.position.x) +
            ":" +
            F(nearest.transform.position.y)
        );

        routeNodeElapsedSeconds.Add(
            F(Mathf.Max(
                0f,
                Time.time - routeStartedAtTime
            ))
        );

        // Parallel to RouteNodeNames: the speed selected when this node first
        // became the nearest route node. Mid-edge speed changes are preserved
        // separately in the compact speed-event sequence.
        routeNodeSelectedSpeeds.Add(
            I(GetCurrentRouteSelectedSpeedKmh())
        );
    }

    private void OnRouteSpeedChanged(int newSpeedKmh)
    {
        if (activeRound == null ||
            activeRound.assistedMode ||
            !writeConventionalRouteSummary)
        {
            return;
        }

        routeSpeedChangeCount++;
        RecordRouteSpeedEvent(newSpeedKmh);
    }

    private void RecordRouteSpeedEvent(int speedKmh)
    {
        if (routeCarTransform == null)
            return;

        float elapsed = Mathf.Max(0f, Time.time - routeStartedAtTime);

        string nearestNodeName = string.Empty;

        if (cachedRoadNodes != null && cachedRoadNodes.Length > 0)
        {
            RoadNode nearest = FindNearestRoadNode(
                routeCarTransform.position,
                cachedRoadNodes
            );

            if (nearest != null)
                nearestNodeName = nearest.name ?? string.Empty;
        }

        // Compact event format: elapsedSec@speedKmh@nearestNode@worldX:worldY
        // Example: 12.4@40@Node_120@135.2:88.7
        routeSpeedEvents.Add(
            F(elapsed) + "@" +
            I(speedKmh) + "@" +
            nearestNodeName + "@" +
            F(routeCarTransform.position.x) + ":" +
            F(routeCarTransform.position.y)
        );
    }

    private int GetCurrentRouteSelectedSpeedKmh()
    {
        if (routeSpeedSelector != null)
            return routeSpeedSelector.CurrentSpeedKmh;

        if (routeCarFollower != null)
            return Mathf.RoundToInt(routeCarFollower.CurrentDrivingSpeedKmh);

        return 0;
    }

    private string BuildCompactSpeedSummary()
    {
        float time30 = 0f;
        float time40 = 0f;
        float time50 = 0f;
        float distance30 = 0f;
        float distance40 = 0f;
        float distance50 = 0f;
        int dominantByTime = 0;
        int dominantByDistance = 0;

        if (routeCarFollower != null)
        {
            time30 = routeCarFollower.DrivingTimeAt30Seconds;
            time40 = routeCarFollower.DrivingTimeAt40Seconds;
            time50 = routeCarFollower.DrivingTimeAt50Seconds;

            distance30 = routeCarFollower.DrivingDistanceAt30Meters;
            distance40 = routeCarFollower.DrivingDistanceAt40Meters;
            distance50 = routeCarFollower.DrivingDistanceAt50Meters;

            dominantByTime =
                routeCarFollower.DominantDrivingSpeedByTimeKmh;

            dominantByDistance =
                routeCarFollower.DominantDrivingSpeedByDistanceKmh;
        }

        // Kept inside the existing RouteRule TEXT field so the Supabase schema
        // does not need extra columns and one Conventional round still uploads
        // as exactly one compact route row.
        return
            "SpeedV1" +
            "|start=" + I(routeStartSpeedKmh) +
            "|domTime=" + I(dominantByTime) +
            "|domDist=" + I(dominantByDistance) +
            "|t30=" + F(time30) +
            "|t40=" + F(time40) +
            "|t50=" + F(time50) +
            "|d30=" + F(distance30) +
            "|d40=" + F(distance40) +
            "|d50=" + F(distance50) +
            "|changes=" + I(routeSpeedChangeCount) +
            "|nodeSpeeds=" + string.Join(">", routeNodeSelectedSpeeds) +
            "|events=" + string.Join("~", routeSpeedEvents);
    }

    private void WriteCompactRouteSummary(
        string roundEndUtc,
        float drivingDistanceM)
    {
        if (activeRound == null ||
            activeRound.assistedMode ||
            !writeConventionalRouteSummary ||
            string.IsNullOrWhiteSpace(routeFilePath))
        {
            return;
        }

        List<string> row = new List<string>
        {
            participantId,
            sessionId,
            studyVersion,
            buildVersion,
            I(activeRound.taskIndex),
            I(activeRound.scenarioIndex),
            activeRound.scenarioId,
            "Conventional",

            // Existing parking_spot_views table requires SpotId.
            "__ROUTE__",

            "0",
            "0",
            "0",

            "RoadNodeRouteSummary",
            activeRound.roundStartUtc,
            roundEndUtc,
            "NearestRoadNodeSequence|" + BuildCompactSpeedSummary(),
            I(routeNodeNames.Count),
            string.Join(";", routeNodeNames),
            string.Join(";", routeNodePositions),
            string.Join(";", routeNodeElapsedSeconds),
            F(Mathf.Max(0f, drivingDistanceM))
        };

        AppendCsvRow(routeFilePath, row);

        UploadRowsToSupabasePriority(
            "parking_spot_views",
            RouteHeader,
            new List<List<string>> { row }
        );

        Debug.Log(
            $"[ParkingLogger] Conventional route saved | " +
            $"scenario={activeRound.scenarioId}, " +
            $"task={activeRound.taskIndex}, nodes={routeNodeNames.Count}, " +
            $"speedChanges={routeSpeedChangeCount}, " +
            $"dominantTime={(routeCarFollower != null ? routeCarFollower.DominantDrivingSpeedByTimeKmh : 0)}, " +
            $"dominantDistance={(routeCarFollower != null ? routeCarFollower.DominantDrivingSpeedByDistanceKmh : 0)}"
        );
    }

    private void ResetRouteCapture()
    {
        if (routeSpeedSelector != null)
            routeSpeedSelector.SpeedChanged -= OnRouteSpeedChanged;

        cachedRoadNodes = null;
        routeCarTransform = null;
        routeCarFollower = null;
        routeSpeedSelector = null;
        routeStartedAtTime = 0f;
        nextRouteSampleTime = 0f;
        routeStartSpeedKmh = 0;
        routeSpeedChangeCount = 0;
        routeNodeNames.Clear();
        routeNodePositions.Clear();
        routeNodeElapsedSeconds.Clear();
        routeNodeSelectedSpeeds.Clear();
        routeSpeedEvents.Clear();
    }

    private AlternativeStats BuildAlternativeStats(
        string alternativeId)
    {
        AlternativeStats result =
            new AlternativeStats
            {
                alternativeId = alternativeId,
                region = GetRegionForAlternative(alternativeId),
                parkingType =
                    GetParkingTypeForAlternative(alternativeId),
                minWalkingDistanceM =
                    float.PositiveInfinity,
                maxWalkingDistanceM = 0f,
                minWalkingTimeSec =
                    float.PositiveInfinity,
                maxWalkingTimeSec = 0f
            };

        float sumDistance = 0f;
        float sumTime = 0f;
        float sumTargetOccupancy = 0f;
        float sumExpectedCost = 0f;
        int costAndTargetCount = 0;

        foreach (SpotSnapshot spot in activeRound.spots)
        {
            if (spot.alternative6 != alternativeId)
                continue;

            result.totalCount++;

            bool legal =
                string.Equals(
                    spot.parkingType,
                    "Legal",
                    StringComparison.OrdinalIgnoreCase
                );

            float targetOccupancy =
                legal
                    ? activeRound.targetLegalOccupancy
                    : activeRound.targetIllegalOccupancy;

            float expectedCost =
                Mathf.Max(
                    0f,
                    spot.expectedParkingCostToman
                );

            if (spot.isSmartParking)
                result.smartSpotCount++;

            if (spot.towEligible)
                result.towEligibleSpotCount++;

            sumTargetOccupancy += targetOccupancy;
            sumExpectedCost += expectedCost;
            costAndTargetCount++;

            if (spot.occupiedAtStart)
            {
                result.occupiedCount++;
                continue;
            }

            result.availableCount++;

            if (spot.isSmartParking)
                result.availableSmartSpotCount++;

            if (spot.towEligible)
                result.availableTowEligibleSpotCount++;

            result.minWalkingDistanceM =
                Mathf.Min(
                    result.minWalkingDistanceM,
                    spot.walkingDistanceM
                );

            result.maxWalkingDistanceM =
                Mathf.Max(
                    result.maxWalkingDistanceM,
                    spot.walkingDistanceM
                );

            result.minWalkingTimeSec =
                Mathf.Min(
                    result.minWalkingTimeSec,
                    spot.walkingTimeSec
                );

            result.maxWalkingTimeSec =
                Mathf.Max(
                    result.maxWalkingTimeSec,
                    spot.walkingTimeSec
                );

            sumDistance += spot.walkingDistanceM;
            sumTime += spot.walkingTimeSec;
        }

        if (result.availableCount > 0)
        {
            result.meanWalkingDistanceM =
                sumDistance /
                result.availableCount;

            result.meanWalkingTimeSec =
                sumTime /
                result.availableCount;
        }
        else
        {
            result.minWalkingDistanceM = 0f;
            result.maxWalkingDistanceM = 0f;
            result.meanWalkingDistanceM = 0f;
            result.minWalkingTimeSec = 0f;
            result.maxWalkingTimeSec = 0f;
            result.meanWalkingTimeSec = 0f;
        }

        result.actualOccupancyRate =
            result.totalCount > 0
                ? (float)result.occupiedCount /
                  result.totalCount
                : 0f;

        result.targetOccupancyRate =
            costAndTargetCount > 0
                ? sumTargetOccupancy /
                  costAndTargetCount
                : 0f;

        result.expectedParkingCost =
            costAndTargetCount > 0
                ? sumExpectedCost /
                  costAndTargetCount
                : 0f;

        return result;
    }

    private void WriteAlternativeRow(
        string filePath,
        AlternativeStats stats,
        string chosenAlternative,
        string chosenSpotId,
        string roundStatus,
        string roundEndUtc,
        bool timedOut,
        float scenarioElapsedSec,
        float movementOrSelectionTimeSec,
        float drivingTimeChosenSec,
        float drivingDistanceChosenM,
        float walkingDistanceChosenM,
        float walkingTimeChosenSec,
        float totalEstimatedTaskTimeSec,
        bool onTimeGame,
        bool onTimeIncludingWalk,
        float roundCost,
        float scoreAfter)
    {
        bool chosen =
            stats.alternativeId ==
            chosenAlternative;

        List<string> row =
            new List<string>
            {
                participantId,
                sessionId,
                studyVersion,
                buildVersion,
                "A1-A6",
                activeRound.roundStartUtc,
                roundEndUtc,
                I(activeRound.taskIndex),
                I(activeRound.scenarioIndex),
                activeRound.scenarioId,
                activeRound.assistedMode
                    ? "Assisted"
                    : "Conventional",
                activeRound.modeOrder,
                activeRound.scenarioOrder,
                roundStatus,
                I(activeRound.startPointIndex),
                activeRound.startNodeName,
                FOptional(
                    activeRound.startNetworkDistanceToGateM
                ),
                chosenSpotId,
                stats.alternativeId,
                stats.region,
                stats.parkingType,
                B(chosen),
                B(stats.availableCount > 0),
                I(stats.totalCount),
                I(stats.occupiedCount),
                I(stats.availableCount),
                F(stats.actualOccupancyRate),
                F(stats.targetOccupancyRate),
                activeRound.scarcityLevel,
                activeRound.occupancyMethod,
                FOptional(
                    GetOccupancyWeightForAlternative(
                        stats.alternativeId
                    )
                ),
                I(stats.smartSpotCount),
                I(stats.availableSmartSpotCount),
                I(stats.towEligibleSpotCount),
                I(stats.availableTowEligibleSpotCount),
                F(stats.minWalkingDistanceM),
                F(stats.meanWalkingDistanceM),
                F(stats.maxWalkingDistanceM),
                F(stats.minWalkingTimeSec),
                F(stats.meanWalkingTimeSec),
                F(stats.maxWalkingTimeSec),
                F(activeRound.dwellTimeMin),
                F(activeRound.timeLimitMin),
                F(activeRound.legalFeePerHour),
                activeRound.finePolicy,
                F(activeRound.fineMultiplier),
                F(activeRound.citationProbability),
                F(activeRound.citationProbabilityStay),
                F(stats.expectedParkingCost),
                F(scenarioElapsedSec),
                F(movementOrSelectionTimeSec),
                chosen
                    ? F(drivingTimeChosenSec)
                    : string.Empty,
                chosen
                    ? F(drivingDistanceChosenM)
                    : string.Empty,
                chosen
                    ? F(walkingDistanceChosenM)
                    : string.Empty,
                chosen
                    ? F(walkingTimeChosenSec)
                    : string.Empty,
                F(totalEstimatedTaskTimeSec),
                B(onTimeGame),
                B(onTimeIncludingWalk),
                F(roundCost),
                F(activeRound.scoreBefore),
                F(scoreAfter),
                B(timedOut)
            };

        AppendCsvRow(filePath, row);
        pendingAlternative6UploadRows?.Add(row);
    }

    private float GetOccupancyWeightForAlternative(
        string alternativeId)
    {
        if (activeRound == null)
            return -1f;

        switch (alternativeId)
        {
            case "A1":
                return activeRound.a1OccupancyWeight;

            case "A2":
                return activeRound.a2OccupancyWeight;

            case "A3":
                return activeRound.a3OccupancyWeight;

            case "A4":
                return activeRound.a4OccupancyWeight;

            case "A5":
                return activeRound.a5OccupancyWeight;

            case "A6":
                return activeRound.a6OccupancyWeight;

            default:
                return -1f;
        }
    }

    private static bool IsModeledAlternative(
        string alternativeId)
    {
        return alternativeId == "A1" ||
               alternativeId == "A2" ||
               alternativeId == "A3" ||
               alternativeId == "A4" ||
               alternativeId == "A5" ||
               alternativeId == "A6";
    }

    private static int AlternativeIndex(
        string alternativeId)
    {
        switch (alternativeId)
        {
            case "A1": return 0;
            case "A2": return 1;
            case "A3": return 2;
            case "A4": return 3;
            case "A5": return 4;
            case "A6": return 5;
            default: return -1;
        }
    }

    private static string GetRegionForAlternative(
        string alternativeId)
    {
        if (alternativeId == "A1" ||
            alternativeId == "A2")
        {
            return "Inner";
        }

        if (alternativeId == "A3" ||
            alternativeId == "A4")
        {
            return "Outer";
        }

        if (alternativeId == "A5" ||
            alternativeId == "A6")
        {
            return "Far";
        }

        return "Unknown";
    }

    private static string GetParkingTypeForAlternative(
        string alternativeId)
    {
        if (alternativeId == "A1" ||
            alternativeId == "A3" ||
            alternativeId == "A5")
        {
            return "Legal";
        }

        if (alternativeId == "A2" ||
            alternativeId == "A4" ||
            alternativeId == "A6")
        {
            return "Illegal";
        }

        return "Unknown";
    }

    // ------------------------------------------------------------------
    // Scenario start -> destination road-network distance
    // ------------------------------------------------------------------

    private float CalculateStartNetworkDistanceToGateMeters(
        int startPointIndex,
        string startNodeName,
        Transform destinationGate,
        float metersPerUnityUnit)
    {
        if (destinationGate == null)
            return -1f;

        float scale =
            Mathf.Max(
                0f,
                metersPerUnityUnit
            );

        if (scale <= 0f)
            return -1f;

        RoadNode[] allNodes =
            FindObjectsByType<RoadNode>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        if (allNodes == null ||
            allNodes.Length == 0)
        {
            return -1f;
        }

        RoadNode startNode =
            ResolveScenarioStartNode(
                startPointIndex,
                startNodeName,
                allNodes
            );

        if (startNode == null)
            return -1f;

        RoadNode gateRoadNode =
            FindNearestRoadNode(
                destinationGate.position,
                allNodes
            );

        if (gateRoadNode == null)
            return -1f;

        return TryShortestDirectedRoadDistance(
                startNode,
                gateRoadNode,
                scale,
                out float distanceMeters)
            ? distanceMeters
            : -1f;
    }

    private RoadNode ResolveScenarioStartNode(
        int startPointIndex,
        string startNodeName,
        RoadNode[] allNodes)
    {
        ScenarioManager manager =
            FindFirstObjectByType<ScenarioManager>();

        if (manager != null &&
            manager.scenarioStartNodes != null &&
            startPointIndex >= 0 &&
            startPointIndex <
            manager.scenarioStartNodes.Length)
        {
            RoadNode assigned =
                manager.scenarioStartNodes[
                    startPointIndex
                ];

            if (assigned != null)
                return assigned;
        }

        if (!string.IsNullOrWhiteSpace(
                startNodeName))
        {
            foreach (RoadNode node in allNodes)
            {
                if (node != null &&
                    string.Equals(
                        node.name,
                        startNodeName,
                        StringComparison.Ordinal))
                {
                    return node;
                }
            }
        }

        return null;
    }

    private static RoadNode FindNearestRoadNode(
        Vector3 worldPosition,
        RoadNode[] nodes)
    {
        RoadNode best = null;
        float bestSquaredDistance =
            float.PositiveInfinity;

        foreach (RoadNode node in nodes)
        {
            if (node == null)
                continue;

            float squaredDistance =
                ((Vector2)node.transform.position -
                 (Vector2)worldPosition)
                .sqrMagnitude;

            if (squaredDistance <
                bestSquaredDistance)
            {
                bestSquaredDistance =
                    squaredDistance;

                best = node;
            }
        }

        return best;
    }

    private static bool TryShortestDirectedRoadDistance(
        RoadNode start,
        RoadNode goal,
        float metersPerUnityUnit,
        out float distanceMeters)
    {
        distanceMeters = 0f;

        if (start == null || goal == null)
            return false;

        if (start == goal)
            return true;

        Dictionary<RoadNode, float> distances =
            new Dictionary<RoadNode, float>();

        HashSet<RoadNode> visited =
            new HashSet<RoadNode>();

        List<RoadNode> open =
            new List<RoadNode>();

        distances[start] = 0f;
        open.Add(start);

        while (open.Count > 0)
        {
            RoadNode current =
                GetClosestOpenRoadNode(
                    open,
                    distances
                );

            open.Remove(current);

            if (current == null ||
                visited.Contains(current))
            {
                continue;
            }

            if (current == goal)
            {
                distanceMeters =
                    distances[current] *
                    metersPerUnityUnit;

                return true;
            }

            visited.Add(current);

            if (current.connectedNodes == null)
                continue;

            foreach (
                RoadNode neighbour
                in current.connectedNodes)
            {
                if (neighbour == null ||
                    visited.Contains(neighbour))
                {
                    continue;
                }

                float segmentLength =
                    Vector2.Distance(
                        current.transform.position,
                        neighbour.transform.position
                    );

                float candidate =
                    distances[current] +
                    segmentLength;

                if (!distances.TryGetValue(
                        neighbour,
                        out float existing) ||
                    candidate < existing)
                {
                    distances[neighbour] =
                        candidate;

                    if (!open.Contains(neighbour))
                        open.Add(neighbour);
                }
            }
        }

        return false;
    }

    private static RoadNode GetClosestOpenRoadNode(
        List<RoadNode> open,
        Dictionary<RoadNode, float> distances)
    {
        RoadNode best = null;
        float bestDistance =
            float.PositiveInfinity;

        foreach (RoadNode node in open)
        {
            if (node == null)
                continue;

            float value =
                distances.TryGetValue(
                    node,
                    out float distance)
                    ? distance
                    : float.PositiveInfinity;

            if (value < bestDistance)
            {
                bestDistance = value;
                best = node;
            }
        }

        return best;
    }

    private void UploadRowsToSupabasePriority(
        string tableName,
        string header,
        List<List<string>> rows)
    {
        if (rows == null ||
            rows.Count == 0)
        {
            return;
        }

        SupabaseSurveyUploader uploader =
            SupabaseSurveyUploader.Instance;

        if (uploader == null)
        {
            uploader =
                FindFirstObjectByType<
                    SupabaseSurveyUploader
                >();
        }

        if (uploader == null)
        {
            Debug.LogWarning(
                "[ParkingLogger] SupabaseSurveyUploader not found. " +
                "Local CSV logging continues."
            );
            return;
        }

        uploader.UploadCsvRowsPriority(
            tableName,
            header,
            rows
        );
    }

    private void AppendCsvRow(
        string path,
        List<string> values)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        StringBuilder line =
            new StringBuilder();

        for (int i = 0;
             i < values.Count;
             i++)
        {
            if (i > 0)
                line.Append(',');

            line.Append(
                EscapeCsv(values[i])
            );
        }

        line.Append(
            Environment.NewLine
        );

        File.AppendAllText(
            path,
            line.ToString(),
            new UTF8Encoding(false)
        );
    }

    private static string EscapeCsv(
        string value)
    {
        if (value == null)
            return string.Empty;

        bool needsQuotes =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        if (!needsQuotes)
            return value;

        return "\"" +
               value.Replace("\"", "\"\"") +
               "\"";
    }

    private string F(float value)
    {
        return value.ToString(
            "0.######",
            invariant
        );
    }

    private string FOptional(
        float value)
    {
        if (float.IsNaN(value) ||
            float.IsInfinity(value) ||
            value < 0f)
        {
            return string.Empty;
        }

        return F(value);
    }

    private string I(int value)
    {
        return value.ToString(
            invariant
        );
    }

    private static string B(bool value)
    {
        return value ? "1" : "0";
    }

    private static string MakeSafeFileName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "UNKNOWN";

        foreach (
            char c
            in Path.GetInvalidFileNameChars())
        {
            value =
                value.Replace(c, '_');
        }

        return value.Trim();
    }

    private string ResolveCurrentBuildVersion()
    {
        if (!string.IsNullOrWhiteSpace(buildVersion))
            return buildVersion;

        ScenarioManager manager =
            FindFirstObjectByType<ScenarioManager>();

        if (manager != null &&
            !string.IsNullOrWhiteSpace(
                manager.buildVersion))
        {
            return manager.buildVersion.Trim();
        }

        return Application.version;
    }

    [Serializable]
    private class RoundSnapshot
    {
        public int taskIndex;
        public int scenarioIndex;
        public string scenarioId;
        public bool assistedMode;
        public string modeOrder;
        public string scenarioOrder;
        public int startPointIndex;
        public string startNodeName;
        public float startNetworkDistanceToGateM;
        public float scoreBefore;
        public float dwellTimeMin;
        public float timeLimitMin;
        public float legalFeePerHour;
        public float smartParkingFeeForDwell;
        public string finePolicy;
        public float fineMultiplier;
        public float citationProbability;
        public float citationProbabilityStay;
        public float onTimeReward;
        public float targetLegalOccupancy;
        public float targetIllegalOccupancy;
        public string scarcityLevel;

        // Reproducibility metadata for the occupancy-generation algorithm.
        public string occupancyMethod;
        public float a1OccupancyWeight;
        public float a2OccupancyWeight;
        public float a3OccupancyWeight;
        public float a4OccupancyWeight;
        public float a5OccupancyWeight;
        public float a6OccupancyWeight;

        public string roundStartUtc;
        public List<SpotSnapshot> spots;
    }

    [Serializable]
    private class SpotSnapshot
    {
        public string spotId;
        public string alternative6;
        public string region;
        public string parkingType;
        public string parkingAreaKind;
        public string street;
        public string streetSide;
        public string illegalReason;
        public string legalFeature;
        public bool isSmartParking;
        public bool towEligible;
        public float spotParkingFeeToman;
        public int baseIllegalFineToman;
        public int appliedIllegalFineToman;
        public float expectedParkingCostToman;
        public bool occupiedAtStart;
        public float walkingDistanceM;
        public float walkingTimeSec;
    }

    private class AlternativeStats
    {
        public string alternativeId;
        public string region;
        public string parkingType;
        public int totalCount;
        public int occupiedCount;
        public int availableCount;
        public int smartSpotCount;
        public int availableSmartSpotCount;
        public int towEligibleSpotCount;
        public int availableTowEligibleSpotCount;
        public float actualOccupancyRate;
        public float targetOccupancyRate;
        public float minWalkingDistanceM;
        public float meanWalkingDistanceM;
        public float maxWalkingDistanceM;
        public float minWalkingTimeSec;
        public float meanWalkingTimeSec;
        public float maxWalkingTimeSec;
        public float expectedParkingCost;
    }
}
