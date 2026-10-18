using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Requests a persistent, concurrency-safe A/B scenario-block assignment
/// from Supabase. The database RPC returns the same block when the same
/// session_id retries, and keeps A/B counts balanced within each study_version.
/// </summary>
public static class SupabaseBlockAssignmentClient
{
    // Supabase publishable values are intentionally safe to embed in a client.
    // Do NOT place a service_role key in Unity.
    private const string SupabaseUrl = "https://fpmkelgfkfwmqzdmvpqh.supabase.co";
    private const string PublishableKey = "sb_publishable_rXyBieI9cMyvo1zyycSAGw_EpDtZOMU";
    private const string AnonJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImZwbWtlbGdma2Z3bXF6ZG12cHFoIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODc0ODA3MTQsImV4cCI6MjEwMzA1NjcxNH0.7ZjniHOXq9kCyf1VPCxFbkT626QCOdHJFDIJO9mitd4";

    public static IEnumerator AssignBlock(
        string participantId,
        string sessionId,
        string studyVersion,
        string buildVersion,
        Action<string, string> completed)
    {
        if (string.IsNullOrWhiteSpace(participantId))
        {
            completed?.Invoke(null, "participantId is empty.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            completed?.Invoke(null, "sessionId is empty.");
            yield break;
        }

        string url = SupabaseUrl + "/rest/v1/rpc/assign_scenario_block";

        string json = "{" +
            "\"p_participant_id\":\"" + EscapeJson(participantId.Trim()) + "\"," +
            "\"p_session_id\":\"" + EscapeJson(sessionId.Trim()) + "\"," +
            "\"p_study_version\":\"" + EscapeJson((studyVersion ?? string.Empty).Trim()) + "\"," +
            "\"p_build_version\":\"" + EscapeJson((buildVersion ?? string.Empty).Trim()) + "\"" +
            "}";

        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("apikey", PublishableKey);
            request.SetRequestHeader("Authorization", "Bearer " + AnonJwt);
            request.timeout = 15;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string message =
                    $"HTTP {request.responseCode}: {request.error}";

                if (!string.IsNullOrWhiteSpace(request.downloadHandler?.text))
                    message += " | " + request.downloadHandler.text;

                completed?.Invoke(null, message);
                yield break;
            }

            string response = request.downloadHandler.text?.Trim();

            // PostgREST returns a scalar text result as JSON, e.g. "A".
            if (!string.IsNullOrEmpty(response) &&
                response.Length >= 2 &&
                response[0] == '"' &&
                response[response.Length - 1] == '"')
            {
                response = response.Substring(1, response.Length - 2);
            }

            response = response?.Trim().ToUpperInvariant();

            if (response != "A" && response != "B")
            {
                completed?.Invoke(null, "Unexpected Supabase block response: " + response);
                yield break;
            }

            completed?.Invoke(response, null);
        }
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder sb = new StringBuilder(value.Length + 8);

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}
