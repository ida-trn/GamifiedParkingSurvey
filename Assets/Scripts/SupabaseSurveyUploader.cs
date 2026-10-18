using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// WebGL-friendly Supabase REST uploader for the parking survey.
///
/// IMPORTANT:
/// - projectUrl is safe to expose.
/// - publishableKey must be an sb_publishable_... key.
/// - NEVER put sb_secret_... or service_role credentials in Unity/WebGL.
///
/// The Supabase tables are configured for anonymous INSERT only.
/// No SELECT/UPDATE/DELETE permission is granted to the WebGL client.
///
/// ParkingLogger keeps writing its local CSV files. This component adds
/// an online copy and batches alternative/spot rows into a few HTTP requests.
/// </summary>
public class SupabaseSurveyUploader : MonoBehaviour
{
    public static SupabaseSurveyUploader Instance { get; private set; }

    [Header("Supabase")]
    public string projectUrl = "https://fpmkelgfkfwmqzdmvpqh.supabase.co";

    [Tooltip("Safe client key. Must start with sb_publishable_.")]
    public string publishableKey = "sb_publishable_rXyBieI9cMyvo1zyycSAGw_EpDtZOMU";

    [Header("Upload")]
    public bool uploadEnabled = true;
    public bool debugSuccessLogs = true;

    [Range(0, 5)]
    public int maxRetries = 2;

    [Min(0.1f)]
    public float retryDelaySeconds = 1f;

    [Header("Study metadata for screens before ScenarioManager exists")]
    [Tooltip("Keep this identical to ScenarioManager.studyVersion.")]
    public string earlyStudyVersion = "2.0-release";

    [Tooltip("Keep this identical to ScenarioManager.buildVersion.")]
    public string earlyBuildVersion = "2.0.3-balanced-blocks";

    [Header("Queue performance")]
    [Tooltip("Large parking_spots / parking_spot_views batches may upload in parallel. Critical records use a separate priority worker.")]
    [Range(1, 4)]
    public int maxConcurrentBulkUploads = 2;

    private readonly Queue<UploadJob> priorityQueue = new Queue<UploadJob>();
    private readonly Queue<UploadJob> bulkQueue = new Queue<UploadJob>();

    private bool priorityWorkerRunning;
    private int bulkWorkersRunning;
    private int priorityInFlight;
    private int bulkInFlight;
    private bool hadPendingUploads;

    public int PendingPriorityUploads => priorityQueue.Count + priorityInFlight;
    public int PendingBulkUploads => bulkQueue.Count + bulkInFlight;
    public bool HasPendingUploads => PendingPriorityUploads > 0 || PendingBulkUploads > 0;

    private sealed class UploadJob
    {
        public string table;
        public string json;
        public int rowCount;
        public int attemptsMade;
        public long lastHttpCode;
        public string lastError = "";
        public string lastResponse = "";
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (GetComponent<SupabaseSurveyBridge>() == null)
            gameObject.AddComponent<SupabaseSurveyBridge>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void UploadCsvRows(
        string tableName,
        string csvHeader,
        List<List<string>> rows)
    {
        if (!uploadEnabled ||
            string.IsNullOrWhiteSpace(tableName) ||
            string.IsNullOrWhiteSpace(csvHeader) ||
            rows == null ||
            rows.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(projectUrl) ||
            string.IsNullOrWhiteSpace(publishableKey))
        {
            Debug.LogError(
                "[SupabaseSurveyUploader] Project URL or publishable key is missing."
            );
            return;
        }

        if (!publishableKey.StartsWith("sb_publishable_", StringComparison.Ordinal))
        {
            Debug.LogError(
                "[SupabaseSurveyUploader] Refusing to upload because the configured key " +
                "is not an sb_publishable_ key."
            );
            return;
        }

        string[] headers = csvHeader.Split(',');

        StringBuilder body = new StringBuilder();

        if (rows.Count > 1)
            body.Append('[');

        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                body.Append(',');

            body.Append(BuildSupabaseObject(tableName, headers, rows[i]));
        }

        if (rows.Count > 1)
            body.Append(']');

        EnqueueJob(new UploadJob
        {
            table = tableName,
            json = body.ToString(),
            rowCount = rows.Count,
            attemptsMade = 0
        }, priority: false);
    }

    public void UploadCsvRowsPriority(
        string tableName,
        string csvHeader,
        List<List<string>> rows)
    {
        if (!uploadEnabled || string.IsNullOrWhiteSpace(tableName) ||
            string.IsNullOrWhiteSpace(csvHeader) || rows == null || rows.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(publishableKey))
            return;

        if (!publishableKey.StartsWith("sb_publishable_", StringComparison.Ordinal))
            return;

        string[] headers = csvHeader.Split(',');
        StringBuilder body = new StringBuilder();
        if (rows.Count > 1) body.Append('[');

        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) body.Append(',');
            body.Append(BuildSupabaseObject(tableName, headers, rows[i]));
        }

        if (rows.Count > 1) body.Append(']');

        EnqueueJob(new UploadJob
        {
            table = tableName,
            json = body.ToString(),
            rowCount = rows.Count,
            attemptsMade = 0
        }, priority: true);
    }

    private void EnqueueJob(UploadJob job, bool priority)
    {
        if (job == null)
            return;

        hadPendingUploads = true;

        if (priority)
        {
            // FIFO priority queue: critical records keep their natural survey order.
            priorityQueue.Enqueue(job);

            if (!priorityWorkerRunning)
                StartCoroutine(ProcessPriorityQueue());

            return;
        }

        bulkQueue.Enqueue(job);
        StartBulkWorkersIfNeeded();
    }

    private IEnumerator ProcessPriorityQueue()
    {
        priorityWorkerRunning = true;

        while (priorityQueue.Count > 0)
        {
            UploadJob job = priorityQueue.Dequeue();
            priorityInFlight++;

            yield return ProcessJobWithRetries(job);

            priorityInFlight = Mathf.Max(0, priorityInFlight - 1);
        }

        priorityWorkerRunning = false;
        CheckAllUploadsComplete();
    }

    private void StartBulkWorkersIfNeeded()
    {
        int targetWorkers = Mathf.Clamp(maxConcurrentBulkUploads, 1, 4);

        while (bulkWorkersRunning < targetWorkers && bulkQueue.Count > 0)
        {
            bulkWorkersRunning++;
            StartCoroutine(ProcessBulkQueueWorker());
        }
    }

    private IEnumerator ProcessBulkQueueWorker()
    {
        while (bulkQueue.Count > 0)
        {
            UploadJob job = bulkQueue.Dequeue();
            bulkInFlight++;

            yield return ProcessJobWithRetries(job);

            bulkInFlight = Mathf.Max(0, bulkInFlight - 1);
        }

        bulkWorkersRunning = Mathf.Max(0, bulkWorkersRunning - 1);

        // A new bulk job may have arrived just as this worker was exiting.
        StartBulkWorkersIfNeeded();
        CheckAllUploadsComplete();
    }

    private IEnumerator ProcessJobWithRetries(UploadJob job)
    {
        while (true)
        {
            bool succeeded = false;
            yield return SendJob(job, result => succeeded = result);

            if (succeeded)
                yield break;

            if (job.attemptsMade > maxRetries)
            {
                Debug.LogError(
                    $"[SupabaseSurveyUploader] Giving up | table={job.table}, " +
                    $"rows={job.rowCount}, attempts={job.attemptsMade}, " +
                    $"http={job.lastHttpCode}, error={job.lastError}, " +
                    $"response={job.lastResponse}"
                );

                yield break;
            }

            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.1f, retryDelaySeconds)
            );
        }
    }

    private void CheckAllUploadsComplete()
    {
        if (!hadPendingUploads || HasPendingUploads)
            return;

        hadPendingUploads = false;

        if (debugSuccessLogs)
        {
            Debug.Log(
                "[SupabaseSurveyUploader] ALL UPLOADS COMPLETE | " +
                "priority=0, bulk=0"
            );
        }
    }

    private void OnApplicationQuit()
    {
        if (!HasPendingUploads)
            return;

        Debug.LogWarning(
            $"[SupabaseSurveyUploader] Application closing with pending uploads | " +
            $"priority={PendingPriorityUploads}, bulk={PendingBulkUploads}. " +
            "In WebGL the page should remain open until uploads finish."
        );
    }

    private IEnumerator SendJob(
        UploadJob job,
        Action<bool> completed)
    {
        string url =
            projectUrl.TrimEnd('/') +
            "/rest/v1/" +
            UnityWebRequest.EscapeURL(job.table);

        byte[] bytes = Encoding.UTF8.GetBytes(job.json);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("apikey", publishableKey);

            // We intentionally do not request inserted rows back because
            // the anonymous WebGL role has no SELECT permission.
            request.SetRequestHeader("Prefer", "return=minimal");

            job.attemptsMade++;

            yield return request.SendWebRequest();

            bool ok =
                request.result == UnityWebRequest.Result.Success &&
                request.responseCode >= 200 &&
                request.responseCode < 300;

            if (ok)
            {
                if (debugSuccessLogs)
                {
                    Debug.Log(
                        $"[SupabaseSurveyUploader] Uploaded | " +
                        $"table={job.table}, rows={job.rowCount}, " +
                        $"http={request.responseCode}"
                    );
                }

                completed(true);
                yield break;
            }

            string response =
                request.downloadHandler != null
                    ? request.downloadHandler.text
                    : string.Empty;

            job.lastHttpCode = request.responseCode;
            job.lastError = request.error ?? string.Empty;
            job.lastResponse = response ?? string.Empty;

            Debug.LogWarning(
                $"[SupabaseSurveyUploader] Upload failed | " +
                $"table={job.table}, rows={job.rowCount}, " +
                $"attempt={job.attemptsMade}/{maxRetries + 1}, " +
                $"http={request.responseCode}, error={request.error}, " +
                $"response={response}"
            );

            completed(false);
        }
    }

    private string BuildSupabaseObject(
        string tableName,
        string[] headers,
        List<string> values)
    {
        Dictionary<string, string> row =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int count = Mathf.Min(headers.Length, values.Count);

        for (int i = 0; i < count; i++)
            row[headers[i]] = values[i] ?? string.Empty;

        StringBuilder json = new StringBuilder();
        json.Append('{');

        bool first = true;

        AddString(json, ref first, "participant_id", Get(row, "ParticipantId"));
        AddString(json, ref first, "session_id", Get(row, "SessionId"));
        AddString(json, ref first, "study_version", Get(row, "StudyVersion"));
        AddString(json, ref first, "build_version", Get(row, "BuildVersion"));

        if (tableName == "parking_sessions")
        {
            AddString(json, ref first, "event_type", string.IsNullOrWhiteSpace(Get(row, "EventType")) ? "session_end" : Get(row, "EventType"));
            AddString(json, ref first, "session_start_utc", Get(row, "SessionStartUtc"));
            AddString(json, ref first, "session_end_utc", Get(row, "SessionEndUtc"));
            AddString(json, ref first, "completion_status", Get(row, "CompletionStatus"));
            AddBool(json, ref first, "completed_normally", Get(row, "CompletedNormally"));
            AddInt(json, ref first, "expected_round_count", Get(row, "ExpectedRoundCount"));
            AddInt(json, ref first, "completed_round_count", Get(row, "CompletedRoundCount"));
            AddBool(json, ref first, "all_expected_rounds_completed", Get(row, "AllExpectedRoundsCompleted"));
            AddFloat(json, ref first, "final_score", Get(row, "FinalScore"));
            AddFloat(json, ref first, "duration_sec", Get(row, "DurationSec"));
            AddString(json, ref first, "unity_version", Get(row, "UnityVersion"));
            AddString(json, ref first, "platform", Get(row, "Platform"));
        }
        else
        {
            AddInt(json, ref first, "task_index", Get(row, "TaskIndex"));
            AddInt(json, ref first, "scenario_index", Get(row, "ScenarioIndex"));
            AddString(json, ref first, "scenario_id", Get(row, "ScenarioId"));
            AddString(json, ref first, "mode", Get(row, "Mode"));
        }

        if (tableName == "parking_rounds")
        {
            AddString(json, ref first, "round_status", Get(row, "RoundStatus"));
            AddString(json, ref first, "selected_spot_id", Get(row, "SelectedSpotId"));
            AddBool(json, ref first, "timed_out", Get(row, "TimedOut"));
            AddFloat(json, ref first, "round_cost", Get(row, "RoundCost"));
            AddFloat(json, ref first, "score_before", Get(row, "ScoreBefore"));
            AddFloat(json, ref first, "score_after", Get(row, "ScoreAfter"));
            AddString(json, ref first, "round_start_utc", Get(row, "RoundStartUtc"));
            AddString(json, ref first, "round_end_utc", Get(row, "RoundEndUtc"));
        }
        else if (
            tableName == "parking_alternatives6" ||
            tableName == "parking_alternatives5")
        {
            AddString(json, ref first, "alternative_id", Get(row, "AlternativeId"));
            AddBool(json, ref first, "chosen", Get(row, "Chosen"));
            AddBool(json, ref first, "available", Get(row, "Available"));
            AddBool(json, ref first, "timed_out", Get(row, "TimedOut"));
        }
        else if (tableName == "parking_spots")
        {
            AddString(json, ref first, "spot_id", Get(row, "SpotId"));
            AddString(json, ref first, "alternative6", Get(row, "Alternative6"));
            AddString(json, ref first, "alternative5", Get(row, "Alternative5"));
            AddBool(json, ref first, "occupied_at_start", Get(row, "OccupiedAtStart"));
            AddBool(json, ref first, "available_at_start", Get(row, "AvailableAtStart"));
            AddBool(json, ref first, "chosen", Get(row, "Chosen"));
            AddBool(json, ref first, "timed_out", Get(row, "TimedOut"));
        }
        else if (tableName == "parking_spot_views")
        {
            AddString(json, ref first, "spot_id", Get(row, "SpotId"));
            AddString(json, ref first, "alternative6", Get(row, "Alternative6"));
            AddString(json, ref first, "alternative5", Get(row, "Alternative5"));
            AddBool(json, ref first, "viewed", Get(row, "Viewed"));
            AddBool(json, ref first, "viewed_available", Get(row, "ViewedAvailable"));
            AddBool(json, ref first, "chosen", Get(row, "Chosen"));
        }
        else if (tableName == "usual_parking_responses")
        {
            AddString(json, ref first, "street_codes", Get(row, "StreetCodes"));
            AddString(json, ref first, "street_names", Get(row, "StreetNames"));
            AddString(json, ref first, "other_text", Get(row, "OtherText"));
            AddString(json, ref first, "recorded_at_utc", Get(row, "RecordedAtUtc"));
        }
        else if (tableName == "final_questionnaire_responses")
        {
            AddInt(json, ref first, "reality_closeness_rating", Get(row, "RealityClosenessRating"));
            AddString(json, ref first, "reality_closeness_label_fa", Get(row, "RealityClosenessLabelFa"));
            AddString(json, ref first, "reality_feedback", Get(row, "RealityFeedback"));
            AddString(json, ref first, "recorded_at_utc", Get(row, "RecordedAtUtc"));
        }
        else if (tableName == "socioeconomic_questionnaire_responses")
        {
            AddString(json, ref first, "questionnaire_start_utc", Get(row, "QuestionnaireStartUtc"));
            AddString(json, ref first, "questionnaire_end_utc", Get(row, "QuestionnaireEndUtc"));
            AddString(json, ref first, "completion_status", Get(row, "CompletionStatus"));
            AddInt(json, ref first, "answered_count", Get(row, "AnsweredCount"));
            AddInt(json, ref first, "expected_question_count", Get(row, "ExpectedQuestionCount"));
            AddFloat(json, ref first, "final_game_score", Get(row, "FinalGameScore"));
        }
        else if (tableName == "participant_demographics")
        {
            AddInt(json, ref first, "age", Get(row, "Age"));
            AddString(json, ref first, "gender_code", Get(row, "GenderCode"));
            AddString(json, ref first, "gender_label", Get(row, "GenderLabel"));
            AddString(json, ref first, "recorded_at_utc", Get(row, "RecordedAtUtc"));
        }
        else if (tableName == "eligibility_responses")
        {
            AddBool(json, ref first, "current_student", Get(row, "CurrentStudent"));
            AddBool(json, ref first, "driving_licence", Get(row, "DrivingLicence"));
            AddBool(json, ref first, "gate2_experience", Get(row, "Gate2Experience"));
            AddBool(json, ref first, "first_participation", Get(row, "FirstParticipation"));
            AddBool(json, ref first, "eligible", Get(row, "Eligible"));
            AddString(json, ref first, "recorded_at_utc", Get(row, "RecordedAtUtc"));
        }

        AddComma(json, ref first);
        json.Append("\"payload\":");
        json.Append(BuildPayload(headers, values));

        json.Append('}');
        return json.ToString();
    }

    /// <summary>
    /// Preserve every CSV cell as a JSON string (or null if blank).
    /// This avoids silently turning legitimate numeric 0/1 fields into booleans.
    /// </summary>
    private static string BuildPayload(
        string[] headers,
        List<string> values)
    {
        StringBuilder json = new StringBuilder();
        json.Append('{');

        int count = Mathf.Min(headers.Length, values.Count);

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                json.Append(',');

            json.Append('"')
                .Append(EscapeJson(headers[i]))
                .Append("\":");

            string value = values[i];

            if (string.IsNullOrEmpty(value))
            {
                json.Append("null");
            }
            else
            {
                json.Append('"')
                    .Append(EscapeJson(value))
                    .Append('"');
            }
        }

        json.Append('}');
        return json.ToString();
    }

    private static string Get(
        Dictionary<string, string> row,
        string key)
    {
        return row.TryGetValue(key, out string value)
            ? value
            : string.Empty;
    }

    private static void AddString(
        StringBuilder json,
        ref bool first,
        string name,
        string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        AddComma(json, ref first);
        json.Append('"').Append(name).Append("\":\"");
        json.Append(EscapeJson(value));
        json.Append('"');
    }

    private static void AddBool(
        StringBuilder json,
        ref bool first,
        string name,
        string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        bool parsed =
            value == "1" ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase);

        AddComma(json, ref first);
        json.Append('"').Append(name).Append("\":");
        json.Append(parsed ? "true" : "false");
    }

    private static void AddInt(
        StringBuilder json,
        ref bool first,
        string name,
        string value)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed))
        {
            return;
        }

        AddComma(json, ref first);
        json.Append('"').Append(name).Append("\":");
        json.Append(parsed.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddFloat(
        StringBuilder json,
        ref bool first,
        string name,
        string value)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed))
        {
            return;
        }

        AddComma(json, ref first);
        json.Append('"').Append(name).Append("\":");
        json.Append(parsed.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void AddComma(
        StringBuilder json,
        ref bool first)
    {
        if (!first)
            json.Append(',');

        first = false;
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder escaped = new StringBuilder(value.Length + 8);

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': escaped.Append("\\\\"); break;
                case '"': escaped.Append("\\\""); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                case '\t': escaped.Append("\\t"); break;
                default:
                    if (c < 32)
                    {
                        escaped.Append("\\u")
                               .Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        escaped.Append(c);
                    }
                    break;
            }
        }

        return escaped.ToString();
    }
}
