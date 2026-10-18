using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Connects the survey components that already save locally to Supabase.
/// Add this beside SupabaseSurveyUploader (the uploader also auto-adds it).
/// Existing local CSV behaviour is preserved.
/// </summary>
public class SupabaseSurveyBridge : MonoBehaviour
{
    public static SupabaseSurveyBridge Instance { get; private set; }

    private UsualParkingQuestion usualParkingQuestion;
    private FinalQuestionnaire finalQuestionnaire;
    private SocioEconomicQuestionnaire socioeconomicQuestionnaire;
    private ParkingViewTracker parkingViewTracker;

    private bool lastParkingViewRoundActive;
    private string currentViewFilePath = string.Empty;
    private int uploadedViewDataLines;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

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

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        BindSceneObjects();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnbindEvents();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        UnbindEvents();
        BindSceneObjects();
    }

    private void Update()
    {
        // Runtime questionnaire objects may appear after scene load.
        if (usualParkingQuestion == null ||
            finalQuestionnaire == null ||
            socioeconomicQuestionnaire == null ||
            parkingViewTracker == null)
        {
            BindSceneObjects();
        }

        MonitorParkingViewFile();
    }

    private void BindSceneObjects()
    {
        if (usualParkingQuestion == null)
        {
            usualParkingQuestion = FindIncludingInactive<UsualParkingQuestion>();
            if (usualParkingQuestion != null)
                usualParkingQuestion.onSubmitted.AddListener(UploadUsualParkingResponse);
        }

        if (finalQuestionnaire == null)
        {
            finalQuestionnaire = FindIncludingInactive<FinalQuestionnaire>();
            if (finalQuestionnaire != null)
                finalQuestionnaire.Submitted += UploadFinalQuestionnaireResponse;
        }

        if (socioeconomicQuestionnaire == null)
        {
            socioeconomicQuestionnaire = FindIncludingInactive<SocioEconomicQuestionnaire>();
            if (socioeconomicQuestionnaire != null)
                socioeconomicQuestionnaire.onQuestionnaireCompleted.AddListener(UploadSocioeconomicQuestionnaire);
        }

        if (parkingViewTracker == null)
        {
            parkingViewTracker = FindIncludingInactive<ParkingViewTracker>();
            if (parkingViewTracker != null)
            {
                lastParkingViewRoundActive = parkingViewTracker.RoundActive;
                ResetViewFileCursorIfNeeded();
            }
        }
    }

    private void UnbindEvents()
    {
        if (usualParkingQuestion != null)
            usualParkingQuestion.onSubmitted.RemoveListener(UploadUsualParkingResponse);

        if (finalQuestionnaire != null)
            finalQuestionnaire.Submitted -= UploadFinalQuestionnaireResponse;

        if (socioeconomicQuestionnaire != null)
            socioeconomicQuestionnaire.onQuestionnaireCompleted.RemoveListener(UploadSocioeconomicQuestionnaire);

        usualParkingQuestion = null;
        finalQuestionnaire = null;
        socioeconomicQuestionnaire = null;
        parkingViewTracker = null;
        lastParkingViewRoundActive = false;
    }

    public void UploadEligibility(
        bool currentStudent,
        bool drivingLicence,
        bool gate2Experience,
        bool firstParticipation,
        bool eligible)
    {
        ParticipantSession session = ParticipantSession.Instance;
        if (session == null)
            return;

        session.EnsureSessionIdentity();

        string[] versions = ResolveVersions();
        string recordedAtUtc = DateTime.UtcNow.ToString("o", Invariant);

        const string header =
            "ParticipantId,SessionId,StudyVersion,BuildVersion," +
            "CurrentStudent,DrivingLicence,Gate2Experience,FirstParticipation,Eligible,RecordedAtUtc";

        List<string> row = new List<string>
        {
            NormalizeParticipantId(session.participantId),
            session.sessionId ?? string.Empty,
            versions[0],
            versions[1],
            B(currentStudent),
            B(drivingLicence),
            B(gate2Experience),
            B(firstParticipation),
            B(eligible),
            recordedAtUtc
        };

        Upload("eligibility_responses", header, row, true);
    }

    public void UploadEarlyDemographics(int age, string genderCode, string genderLabel)
    {
        ParticipantSession session = ParticipantSession.Instance;
        if (session == null)
            return;

        session.EnsureSessionIdentity();
        string[] versions = ResolveVersions();

        const string header =
            "ParticipantId,SessionId,StudyVersion,BuildVersion," +
            "Age,GenderCode,GenderLabel,RecordedAtUtc";

        List<string> row = new List<string>
        {
            NormalizeParticipantId(session.participantId),
            session.sessionId ?? string.Empty,
            versions[0],
            versions[1],
            age.ToString(Invariant),
            genderCode ?? string.Empty,
            genderLabel ?? string.Empty,
            DateTime.UtcNow.ToString("o", Invariant)
        };

        Upload("participant_demographics", header, row, true);
    }

    private void UploadUsualParkingResponse()
    {
        ParticipantSession session = ParticipantSession.Instance;
        if (session == null)
            return;

        session.EnsureSessionIdentity();
        string[] versions = ResolveVersions();

        const string header =
            "ParticipantId,SessionId,StudyVersion,BuildVersion," +
            "StreetCodes,StreetNames,OtherText,RecordedAtUtc";

        List<string> row = new List<string>
        {
            NormalizeParticipantId(session.participantId),
            session.sessionId ?? string.Empty,
            versions[0],
            versions[1],
            session.usualParkingStreetCodes ?? string.Empty,
            session.usualParkingStreetNames ?? string.Empty,
            session.usualParkingOtherText ?? string.Empty,
            DateTime.UtcNow.ToString("o", Invariant)
        };

        Upload("usual_parking_responses", header, row, true);
    }

    private void UploadFinalQuestionnaireResponse()
    {
        ParticipantSession session = ParticipantSession.Instance;
        if (session == null)
            return;

        session.EnsureSessionIdentity();
        string[] versions = ResolveVersions();

        const string header =
            "ParticipantId,SessionId,StudyVersion,BuildVersion," +
            "RealityClosenessRating,RealityClosenessLabelFa,RealityFeedback,RecordedAtUtc";

        List<string> row = new List<string>
        {
            NormalizeParticipantId(session.participantId),
            session.sessionId ?? string.Empty,
            versions[0],
            versions[1],
            session.realityClosenessRating.ToString(Invariant),
            session.realityClosenessLabelFa ?? string.Empty,
            session.realityFeedback ?? string.Empty,
            DateTime.UtcNow.ToString("o", Invariant)
        };

        Upload("final_questionnaire_responses", header, row, true);
    }

    private void UploadSocioeconomicQuestionnaire()
    {
        if (socioeconomicQuestionnaire == null)
            return;

        string path = GetPrivateStringField(socioeconomicQuestionnaire, "outputFilePath");
        UploadSingleRowCsvFile("socioeconomic_questionnaire_responses", path, true);
    }

    private void MonitorParkingViewFile()
    {
        if (parkingViewTracker == null)
            return;

        bool active = parkingViewTracker.RoundActive;

        if (!active && lastParkingViewRoundActive)
        {
            UploadNewParkingViewRows();
        }

        lastParkingViewRoundActive = active;
    }

    private void ResetViewFileCursorIfNeeded()
    {
        if (parkingViewTracker == null)
            return;

        string path = parkingViewTracker.ViewFilePath ?? string.Empty;
        if (!string.Equals(path, currentViewFilePath, StringComparison.Ordinal))
        {
            currentViewFilePath = path;
            uploadedViewDataLines = 0;
        }
    }

    private void UploadNewParkingViewRows()
    {
        ResetViewFileCursorIfNeeded();

        string path = currentViewFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length <= 1)
            return;

        string header = StripBom(lines[0]);
        int availableDataLines = lines.Length - 1;

        if (uploadedViewDataLines > availableDataLines)
            uploadedViewDataLines = 0;

        List<List<string>> rows = new List<List<string>>();

        for (int i = 1 + uploadedViewDataLines; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            rows.Add(new List<string>(ParseCsvLine(lines[i])));
        }

        if (rows.Count == 0)
            return;

        SupabaseSurveyUploader uploader = SupabaseSurveyUploader.Instance;
        if (uploader == null)
            return;

        uploader.UploadCsvRows("parking_spot_views", header, rows);
        uploadedViewDataLines = availableDataLines;
    }

    private void UploadSingleRowCsvFile(string tableName, string path, bool priority)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length < 2)
            return;

        string header = StripBom(lines[0]);
        string dataLine = lines[lines.Length - 1];

        if (string.IsNullOrWhiteSpace(dataLine))
            return;

        List<string> row = new List<string>(ParseCsvLine(dataLine));
        Upload(tableName, header, row, priority);
    }

    private void Upload(string tableName, string header, List<string> row, bool priority)
    {
        SupabaseSurveyUploader uploader = SupabaseSurveyUploader.Instance;
        if (uploader == null || row == null)
            return;

        List<List<string>> rows = new List<List<string>> { row };

        if (priority)
            uploader.UploadCsvRowsPriority(tableName, header, rows);
        else
            uploader.UploadCsvRows(tableName, header, rows);
    }

    private string[] ResolveVersions()
    {
        SupabaseSurveyUploader uploader = SupabaseSurveyUploader.Instance;

        string study = uploader != null
            ? uploader.earlyStudyVersion ?? string.Empty
            : string.Empty;

        string build = uploader != null && !string.IsNullOrWhiteSpace(uploader.earlyBuildVersion)
            ? uploader.earlyBuildVersion
            : Application.version;

        // Once the gameplay scene exists, ScenarioManager remains the source of truth.
        ScenarioManager manager = FindIncludingInactive<ScenarioManager>();
        if (manager != null)
        {
            study = manager.studyVersion ?? study;
            build = string.IsNullOrWhiteSpace(manager.buildVersion)
                ? build
                : manager.buildVersion;
        }

        return new[] { study, build };
    }

    private static T FindIncludingInactive<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }

    private static string GetPrivateStringField(object target, string fieldName)
    {
        if (target == null)
            return string.Empty;

        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        return field != null ? field.GetValue(target) as string ?? string.Empty : string.Empty;
    }

    private static List<string> ParseCsvLine(string line)
    {
        List<string> values = new List<string>();
        StringBuilder current = new StringBuilder();
        bool insideQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (c == ',' && !insideQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static string StripBom(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value[0] == '\uFEFF' ? value.Substring(1) : value;
    }

    private static string NormalizeParticipantId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "UNKNOWN"
            : value.Trim().ToUpperInvariant();
    }

    private static string B(bool value) => value ? "1" : "0";
}
