using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Research-grade CSV logger for the gamified parking survey.
///
/// It creates a new set of files for every participant session:
/// 1) parking_rounds_...csv       -> one row per completed/timed-out task
/// 2) parking_alternatives_...csv -> four rows per task (A1-A4)
/// 3) parking_session_...csv      -> one summary row for the whole session
///
/// The alternative file is the main long-format file for later MNL/Mixed-Logit work.
/// </summary>
public class ParkingLogger : MonoBehaviour
{
    public static ParkingLogger Instance { get; private set; }

    [Header("Output")]
    [Tooltip("A subfolder is created inside Application.persistentDataPath.")]
    public string outputSubfolder = "ParkingSurveyData";

    [Tooltip("Keep this ON. It produces one row for A1, A2, A3 and A4 in every task.")]
    public bool writeAlternativeFile = true;

    [Tooltip("Normally keep this OFF because the thesis choice set is A1-A4.")]
    public bool includeFarNotModeledSpots = false;

    [Header("Debug")]
    public bool printOutputPaths = true;

    public string SessionId => sessionId;
    public string RoundFilePath => roundFilePath;
    public string AlternativeFilePath => alternativeFilePath;
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
    private string alternativeFilePath;
    private string sessionFilePath;

    private bool sessionInitialized;
    private bool sessionEnded;
    private int expectedRoundCount;
    private int completedRoundCount;

    private RoundSnapshot activeRound;

    private const string RoundHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion,SessionStartUtc,RoundStartUtc,RoundEndUtc," +
        "TaskIndex,ScenarioIndex,ScenarioId,Mode,ModeOrder,ScenarioOrder," +
        "StartPointIndex,StartNode,RoundStatus,Parked,TimedOut,ChosenAlternative,SelectedSpotId," +
        "Region,ParkingType,ParkingAreaKind,Street,StreetSide,IllegalReason,LegalFeature," +
        "DwellTimeMin,TimeLimitMin,LegalFeePerHour,IllegalFine,CitationProbability,OnTimeReward," +
        "TargetLegalOccupancy,TargetIllegalOccupancy,ActualLegalOccupancy,ActualIllegalOccupancy," +
        "TotalA1,AvailableA1,TotalA2,AvailableA2,TotalA3,AvailableA3,TotalA4,AvailableA4," +
        "AvailableSpotIdsA1,AvailableSpotIdsA2,AvailableSpotIdsA3,AvailableSpotIdsA4," +
        "ScenarioElapsedSec,MovementOrSelectionTimeSec,DrivingSearchTimeSec,DecisionTimeSec," +
        "WalkingDistanceM,WalkingTimeSec,TotalEstimatedTaskTimeSec,OnTimeGame,OnTimeIncludingWalk," +
        "RoundCost,ScoreBefore,ScoreAfter,UnityVersion,Platform";

    private const string AlternativeHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion,RoundStartUtc,RoundEndUtc," +
        "TaskIndex,ScenarioIndex,ScenarioId,Mode,ModeOrder,ScenarioOrder,RoundStatus," +
        "StartPointIndex,StartNode,ChosenSpotId,AlternativeId,Region,ParkingType,Chosen,Available," +
        "TotalSpotCount,OccupiedSpotCount,AvailableSpotCount,ActualOccupancyRate,TargetOccupancyRate," +
        "MinWalkingDistanceM,MeanWalkingDistanceM,MaxWalkingDistanceM," +
        "MinWalkingTimeSec,MeanWalkingTimeSec,MaxWalkingTimeSec,AvailableSpotIds," +
        "DwellTimeMin,TimeLimitMin,LegalFeePerHour,IllegalFine,CitationProbability,ExpectedParkingCost," +
        "ScenarioElapsedSec,MovementOrSelectionTimeSec,WalkingDistanceChosenM,WalkingTimeChosenSec," +
        "TotalEstimatedTaskTimeSec,OnTimeGame,OnTimeIncludingWalk,RoundCost,ScoreBefore,ScoreAfter,TimedOut";

    private const string SessionHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion,SessionStartUtc,SessionEndUtc,CompletionStatus," +
        "CompletedNormally,ExpectedRoundCount,CompletedRoundCount,AllExpectedRoundsCompleted," +
        "FinalScore,DurationSec,UnityVersion,Platform,RoundFile,AlternativeFile";

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
    /// Call once after occupancy has been applied and immediately before the round starts.
    /// This freezes the objective choice set before the player changes any spot state.
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
            scoreBefore = scoreBefore,
            dwellTimeMin = scenario.dwellTimeMinutes,
            timeLimitMin = scenario.timeLimitMinutes,
            legalFeePerHour = scenario.legalFeePerHour,
            illegalFine = scenario.illegalFine,
            citationProbability = scenario.citationProbability,
            onTimeReward = scenario.onTimeReward,
            targetLegalOccupancy = scenario.legalOccupancyRate,
            targetIllegalOccupancy = scenario.illegalOccupancyRate,
            roundStartUtc = DateTime.UtcNow.ToString("o", invariant),
            spots = new List<SpotSnapshot>()
        };

        ParkingSpot[] allSpots = FindObjectsByType<ParkingSpot>(FindObjectsSortMode.None);

        foreach (ParkingSpot spot in allSpots)
        {
            if (spot == null)
                continue;

            string altId = spot.GetAlternativeId();
            bool modeled = altId == "A1" || altId == "A2" || altId == "A3" || altId == "A4";

            if (!modeled && !includeFarNotModeledSpots)
                continue;

            float walkingDistance = 0f;
            float walkingTime = 0f;

            if (destinationGate != null)
            {
                walkingDistance = Vector2.Distance(spot.transform.position, destinationGate.position)
                                  * Mathf.Max(0f, metersPerUnityUnit);

                if (walkingSpeedMetersPerSecond > 0f)
                    walkingTime = walkingDistance / walkingSpeedMetersPerSecond;
            }

            activeRound.spots.Add(new SpotSnapshot
            {
                spotId = spot.spotId,
                alternativeId = altId,
                region = spot.GetRegionName(),
                parkingType = spot.GetParkingTypeName(),
                parkingAreaKind = spot.GetParkingAreaKindName(),
                street = spot.streetName,
                streetSide = spot.GetStreetSideName(),
                illegalReason = spot.GetIllegalReasonName(),
                legalFeature = spot.GetLegalParkingFeatureName(),
                occupiedAtStart = spot.isOccupied,
                walkingDistanceM = walkingDistance,
                walkingTimeSec = walkingTime
            });
        }

        Debug.Log(
            $"[ParkingLogger] Round snapshot saved | participant={participantId}, " +
            $"session={sessionId}, task={roundIndex + 1}, scenario={scenario.scenarioId}, " +
            $"mode={(assistedMode ? "Assisted" : "Conventional")}, modeledSpots={activeRound.spots.Count}"
        );
    }

    /// <summary>
    /// Call once when the round ends, either by parking or by timeout.
    /// </summary>
    public void CompleteRound(
        ParkingSpot chosenSpot,
        bool parked,
        bool timedOut,
        float scenarioElapsedSec,
        float movementOrSelectionTimeSec,
        float walkingDistanceM,
        float walkingTimeSec,
        bool onTimeGame,
        bool onTimeIncludingWalk,
        float roundCost,
        float scoreAfter)
    {
        if (!sessionInitialized)
        {
            Debug.LogWarning("[ParkingLogger] CompleteRound called before a session was initialized.");
            return;
        }

        if (activeRound == null)
        {
            Debug.LogWarning("[ParkingLogger] CompleteRound called without an active round snapshot.");
            return;
        }

        float totalEstimatedTaskTimeSec = scenarioElapsedSec + walkingTimeSec;
        string chosenAlternative = chosenSpot != null ? chosenSpot.GetAlternativeId() : string.Empty;

        AlternativeStats a1 = BuildAlternativeStats("A1");
        AlternativeStats a2 = BuildAlternativeStats("A2");
        AlternativeStats a3 = BuildAlternativeStats("A3");
        AlternativeStats a4 = BuildAlternativeStats("A4");

        string roundEndUtc = DateTime.UtcNow.ToString("o", invariant);
        string roundStatus = timedOut
            ? "TimedOut"
            : parked
                ? "Parked"
                : "NotParked";

        int legalTotalCount = a1.totalCount + a3.totalCount;
        int legalOccupiedCount = a1.occupiedCount + a3.occupiedCount;
        float actualLegalOccupancy = legalTotalCount > 0
            ? (float)legalOccupiedCount / legalTotalCount
            : 0f;

        int illegalTotalCount = a2.totalCount + a4.totalCount;
        int illegalOccupiedCount = a2.occupiedCount + a4.occupiedCount;
        float actualIllegalOccupancy = illegalTotalCount > 0
            ? (float)illegalOccupiedCount / illegalTotalCount
            : 0f;

        string chosenSpotId = chosenSpot != null ? chosenSpot.spotId : string.Empty;

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
            roundStatus,
            B(parked),
            B(timedOut),
            chosenAlternative,
            chosenSpotId,
            chosenSpot != null ? chosenSpot.GetRegionName() : string.Empty,
            chosenSpot != null ? chosenSpot.GetParkingTypeName() : string.Empty,
            chosenSpot != null ? chosenSpot.GetParkingAreaKindName() : string.Empty,
            chosenSpot != null ? chosenSpot.streetName : string.Empty,
            chosenSpot != null ? chosenSpot.GetStreetSideName() : string.Empty,
            chosenSpot != null ? chosenSpot.GetIllegalReasonName() : string.Empty,
            chosenSpot != null ? chosenSpot.GetLegalParkingFeatureName() : string.Empty,
            F(activeRound.dwellTimeMin),
            F(activeRound.timeLimitMin),
            F(activeRound.legalFeePerHour),
            F(activeRound.illegalFine),
            F(activeRound.citationProbability),
            F(activeRound.onTimeReward),
            F(activeRound.targetLegalOccupancy),
            F(activeRound.targetIllegalOccupancy),
            F(actualLegalOccupancy),
            F(actualIllegalOccupancy),
            I(a1.totalCount), I(a1.availableCount),
            I(a2.totalCount), I(a2.availableCount),
            I(a3.totalCount), I(a3.availableCount),
            I(a4.totalCount), I(a4.availableCount),
            string.Join(";", a1.availableSpotIds),
            string.Join(";", a2.availableSpotIds),
            string.Join(";", a3.availableSpotIds),
            string.Join(";", a4.availableSpotIds),
            F(scenarioElapsedSec),
            F(movementOrSelectionTimeSec),
            F(activeRound.assistedMode ? 0f : movementOrSelectionTimeSec),
            F(activeRound.assistedMode ? movementOrSelectionTimeSec : 0f),
            F(walkingDistanceM),
            F(walkingTimeSec),
            F(totalEstimatedTaskTimeSec),
            B(onTimeGame),
            B(onTimeIncludingWalk),
            F(roundCost),
            F(activeRound.scoreBefore),
            F(scoreAfter),
            Application.unityVersion,
            Application.platform.ToString()
        };

        AppendCsvRow(roundFilePath, row);

        if (writeAlternativeFile)
        {
            WriteAlternativeRow(a1, chosenAlternative, chosenSpotId, roundStatus, roundEndUtc,
                timedOut, scenarioElapsedSec, movementOrSelectionTimeSec,
                walkingDistanceM, walkingTimeSec, totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk, roundCost, scoreAfter);

            WriteAlternativeRow(a2, chosenAlternative, chosenSpotId, roundStatus, roundEndUtc,
                timedOut, scenarioElapsedSec, movementOrSelectionTimeSec,
                walkingDistanceM, walkingTimeSec, totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk, roundCost, scoreAfter);

            WriteAlternativeRow(a3, chosenAlternative, chosenSpotId, roundStatus, roundEndUtc,
                timedOut, scenarioElapsedSec, movementOrSelectionTimeSec,
                walkingDistanceM, walkingTimeSec, totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk, roundCost, scoreAfter);

            WriteAlternativeRow(a4, chosenAlternative, chosenSpotId, roundStatus, roundEndUtc,
                timedOut, scenarioElapsedSec, movementOrSelectionTimeSec,
                walkingDistanceM, walkingTimeSec, totalEstimatedTaskTimeSec,
                onTimeGame, onTimeIncludingWalk, roundCost, scoreAfter);
        }

        completedRoundCount++;
        activeRound = null;
    }

    public void EndSession(bool completedNormally, float finalScore)
    {
        if (!sessionInitialized || sessionEnded)
            return;

        sessionEnded = true;

        DateTime endUtc = DateTime.UtcNow;
        double durationSec = Math.Max(0d, (endUtc - sessionStartTimeUtc).TotalSeconds);

        bool allExpectedRoundsCompleted = expectedRoundCount <= 0
            ? completedNormally
            : completedRoundCount >= expectedRoundCount;

        string completionStatus = completedNormally && allExpectedRoundsCompleted
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
            float.IsNaN(finalScore) ? string.Empty : F(finalScore),
            durationSec.ToString("0.###", invariant),
            Application.unityVersion,
            Application.platform.ToString(),
            Path.GetFileName(roundFilePath),
            Path.GetFileName(alternativeFilePath)
        };

        AppendCsvRow(sessionFilePath, row);

        Debug.Log(
            $"[ParkingLogger] Session ended | participant={participantId}, session={sessionId}, " +
            $"completedNormally={completedNormally}, rounds={completedRoundCount}\n" +
            $"Output folder: {outputDirectory}"
        );
    }

    [ContextMenu("Print Output Folder")]
    public void PrintOutputFolder()
    {
        string folder = sessionInitialized
            ? outputDirectory
            : Path.Combine(Application.persistentDataPath, outputSubfolder);

        Debug.Log("[ParkingLogger] Output folder: " + folder);
    }

    private void EnsureSession(
        string participantIdValue,
        string studyVersionValue,
        string buildVersionValue,
        int expectedRoundCountValue)
    {
        string requestedParticipant = string.IsNullOrWhiteSpace(participantIdValue)
            ? "UNKNOWN"
            : participantIdValue.Trim();

        // Allow several students to use the same installed build without restarting the app.
        // A completed session, or a changed participant ID, starts fresh files automatically.
        if (sessionInitialized)
        {
            bool sameParticipant = string.Equals(
                participantId,
                requestedParticipant,
                StringComparison.OrdinalIgnoreCase
            );

            string requestedStudyVersion = string.IsNullOrWhiteSpace(studyVersionValue)
                ? "1.0"
                : studyVersionValue.Trim();

            string requestedBuildVersion = string.IsNullOrWhiteSpace(buildVersionValue)
                ? Application.version
                : buildVersionValue.Trim();

            bool sameStudyVersion = string.Equals(
                studyVersion,
                requestedStudyVersion,
                StringComparison.OrdinalIgnoreCase
            );

            bool sameBuildVersion = string.Equals(
                buildVersion,
                requestedBuildVersion,
                StringComparison.OrdinalIgnoreCase
            );

            if (!sessionEnded && sameParticipant && sameStudyVersion && sameBuildVersion)
                return;

            if (!sessionEnded)
                EndSession(false, float.NaN);

            ResetSessionState();
        }

        participantId = requestedParticipant;

        studyVersion = string.IsNullOrWhiteSpace(studyVersionValue)
            ? "1.0"
            : studyVersionValue.Trim();

        buildVersion = string.IsNullOrWhiteSpace(buildVersionValue)
            ? Application.version
            : buildVersionValue.Trim();

        expectedRoundCount = Mathf.Max(0, expectedRoundCountValue);

        sessionStartTimeUtc = DateTime.UtcNow;
        sessionStartUtc = sessionStartTimeUtc.ToString("o", invariant);

        string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);
        sessionId = sessionStartTimeUtc.ToString("yyyyMMdd_HHmmss_fff", invariant) + "_" + shortGuid;

        outputDirectory = Path.Combine(Application.persistentDataPath, outputSubfolder);
        Directory.CreateDirectory(outputDirectory);

        string safeParticipant = MakeSafeFileName(participantId);
        string fileStem = safeParticipant + "_" + sessionId;

        roundFilePath = Path.Combine(outputDirectory, "parking_rounds_" + fileStem + ".csv");
        alternativeFilePath = Path.Combine(outputDirectory, "parking_alternatives_" + fileStem + ".csv");
        sessionFilePath = Path.Combine(outputDirectory, "parking_session_" + fileStem + ".csv");

        File.WriteAllText(roundFilePath, RoundHeader + Environment.NewLine, new UTF8Encoding(true));

        if (writeAlternativeFile)
            File.WriteAllText(alternativeFilePath, AlternativeHeader + Environment.NewLine, new UTF8Encoding(true));

        File.WriteAllText(sessionFilePath, SessionHeader + Environment.NewLine, new UTF8Encoding(true));

        sessionInitialized = true;

        if (printOutputPaths)
        {
            Debug.Log(
                "[ParkingLogger] New research session created.\n" +
                "Rounds: " + roundFilePath + "\n" +
                "Alternatives: " + alternativeFilePath + "\n" +
                "Session: " + sessionFilePath
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
        alternativeFilePath = null;
        sessionFilePath = null;
        sessionInitialized = false;
        sessionEnded = false;
        expectedRoundCount = 0;
        completedRoundCount = 0;
        activeRound = null;
    }

    private AlternativeStats BuildAlternativeStats(string alternativeId)
    {
        AlternativeStats result = new AlternativeStats
        {
            alternativeId = alternativeId,
            region = alternativeId == "A1" || alternativeId == "A2" ? "Inner" : "Outer",
            parkingType = alternativeId == "A1" || alternativeId == "A3" ? "Legal" : "Illegal",
            minWalkingDistanceM = float.PositiveInfinity,
            maxWalkingDistanceM = 0f,
            minWalkingTimeSec = float.PositiveInfinity,
            maxWalkingTimeSec = 0f,
            availableSpotIds = new List<string>()
        };

        float sumDistance = 0f;
        float sumTime = 0f;

        foreach (SpotSnapshot spot in activeRound.spots)
        {
            if (spot.alternativeId != alternativeId)
                continue;

            result.totalCount++;

            if (spot.occupiedAtStart)
            {
                result.occupiedCount++;
                continue;
            }

            result.availableCount++;
            result.availableSpotIds.Add(spot.spotId);

            result.minWalkingDistanceM = Mathf.Min(result.minWalkingDistanceM, spot.walkingDistanceM);
            result.maxWalkingDistanceM = Mathf.Max(result.maxWalkingDistanceM, spot.walkingDistanceM);
            result.minWalkingTimeSec = Mathf.Min(result.minWalkingTimeSec, spot.walkingTimeSec);
            result.maxWalkingTimeSec = Mathf.Max(result.maxWalkingTimeSec, spot.walkingTimeSec);

            sumDistance += spot.walkingDistanceM;
            sumTime += spot.walkingTimeSec;
        }

        if (result.availableCount > 0)
        {
            result.meanWalkingDistanceM = sumDistance / result.availableCount;
            result.meanWalkingTimeSec = sumTime / result.availableCount;
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

        result.actualOccupancyRate = result.totalCount > 0
            ? (float)result.occupiedCount / result.totalCount
            : 0f;

        result.targetOccupancyRate = result.parkingType == "Legal"
            ? activeRound.targetLegalOccupancy
            : activeRound.targetIllegalOccupancy;

        float dwellHours = activeRound.dwellTimeMin / 60f;
        result.expectedParkingCost = result.parkingType == "Legal"
            ? activeRound.legalFeePerHour * dwellHours
            : activeRound.illegalFine * activeRound.citationProbability * dwellHours;

        return result;
    }

    private void WriteAlternativeRow(
        AlternativeStats stats,
        string chosenAlternative,
        string chosenSpotId,
        string roundStatus,
        string roundEndUtc,
        bool timedOut,
        float scenarioElapsedSec,
        float movementOrSelectionTimeSec,
        float walkingDistanceChosenM,
        float walkingTimeChosenSec,
        float totalEstimatedTaskTimeSec,
        bool onTimeGame,
        bool onTimeIncludingWalk,
        float roundCost,
        float scoreAfter)
    {
        bool chosen = stats.alternativeId == chosenAlternative;

        List<string> row = new List<string>
        {
            participantId,
            sessionId,
            studyVersion,
            buildVersion,
            activeRound.roundStartUtc,
            roundEndUtc,
            I(activeRound.taskIndex),
            I(activeRound.scenarioIndex),
            activeRound.scenarioId,
            activeRound.assistedMode ? "Assisted" : "Conventional",
            activeRound.modeOrder,
            activeRound.scenarioOrder,
            roundStatus,
            I(activeRound.startPointIndex),
            activeRound.startNodeName,
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
            F(stats.minWalkingDistanceM),
            F(stats.meanWalkingDistanceM),
            F(stats.maxWalkingDistanceM),
            F(stats.minWalkingTimeSec),
            F(stats.meanWalkingTimeSec),
            F(stats.maxWalkingTimeSec),
            string.Join(";", stats.availableSpotIds),
            F(activeRound.dwellTimeMin),
            F(activeRound.timeLimitMin),
            F(activeRound.legalFeePerHour),
            F(activeRound.illegalFine),
            F(activeRound.citationProbability),
            F(stats.expectedParkingCost),
            F(scenarioElapsedSec),
            F(movementOrSelectionTimeSec),
            chosen ? F(walkingDistanceChosenM) : string.Empty,
            chosen ? F(walkingTimeChosenSec) : string.Empty,
            F(totalEstimatedTaskTimeSec),
            B(onTimeGame),
            B(onTimeIncludingWalk),
            F(roundCost),
            F(activeRound.scoreBefore),
            F(scoreAfter),
            B(timedOut)
        };

        AppendCsvRow(alternativeFilePath, row);
    }

    private void AppendCsvRow(string path, List<string> values)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        StringBuilder line = new StringBuilder();

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                line.Append(',');

            line.Append(EscapeCsv(values[i]));
        }

        line.Append(Environment.NewLine);
        File.AppendAllText(path, line.ToString(), new UTF8Encoding(false));
    }

    private static string EscapeCsv(string value)
    {
        if (value == null)
            return string.Empty;

        bool needsQuotes = value.Contains(",") || value.Contains("\"") ||
                           value.Contains("\n") || value.Contains("\r");

        if (!needsQuotes)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private string F(float value) => value.ToString("0.######", invariant);
    private string I(int value) => value.ToString(invariant);
    private static string B(bool value) => value ? "1" : "0";

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "UNKNOWN";

        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');

        return value.Trim();
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
        public float scoreBefore;
        public float dwellTimeMin;
        public float timeLimitMin;
        public float legalFeePerHour;
        public float illegalFine;
        public float citationProbability;
        public float onTimeReward;
        public float targetLegalOccupancy;
        public float targetIllegalOccupancy;
        public string roundStartUtc;
        public List<SpotSnapshot> spots;
    }

    [Serializable]
    private class SpotSnapshot
    {
        public string spotId;
        public string alternativeId;
        public string region;
        public string parkingType;
        public string parkingAreaKind;
        public string street;
        public string streetSide;
        public string illegalReason;
        public string legalFeature;
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
        public float actualOccupancyRate;
        public float targetOccupancyRate;
        public float minWalkingDistanceM;
        public float meanWalkingDistanceM;
        public float maxWalkingDistanceM;
        public float minWalkingTimeSec;
        public float meanWalkingTimeSec;
        public float maxWalkingTimeSec;
        public float expectedParkingCost;
        public List<string> availableSpotIds;
    }
}