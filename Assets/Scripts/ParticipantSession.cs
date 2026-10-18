using System;
using System.Globalization;
using UnityEngine;

public class ParticipantSession : MonoBehaviour
{
    public static ParticipantSession Instance { get; private set; }

    [Header("Current Participant")]
    public string participantId = "";

    [Header("Current Survey Session")]
    [Tooltip("Stable ID shared by pre-game questions, gameplay logs, and post-game questionnaires.")]
    public string sessionId = "";

    [Tooltip("UTC timestamp for when this survey session object was first created.")]
    public string sessionStartUtc = "";

    [Header("Real-life Parking Profile")]
    [Tooltip("True after the participant has answered the usual-parking question.")]
    public bool hasUsualParkingAnswer = false;

    [Tooltip("Participant ID that owns the saved usual-parking answer.")]
    public string usualParkingAnswerParticipantId = "";

    [Tooltip("Pipe-separated stable street codes, for example HDR|MAL|OTHER.")]
    public string usualParkingStreetCodes = "";

    [Tooltip("Pipe-separated English street names matching ParkingSpot street names.")]
    public string usualParkingStreetNames = "";

    [Tooltip("Typed street name when Other is selected.")]
    public string usualParkingOtherText = "";

    [Header("Final Questionnaire")]
    [Tooltip("True after the participant has submitted the final questionnaire.")]
    public bool hasFinalQuestionnaireAnswer = false;

    [Tooltip("Participant ID that owns the saved final-questionnaire answer.")]
    public string finalQuestionnaireParticipantId = "";

    [Tooltip("Pipe-separated stable answer codes selected in the final questionnaire.")]
    public string finalQuestionnaireSelectedCodes = "";

    [Tooltip("Pipe-separated Persian labels matching the selected answer codes.")]
    public string finalQuestionnaireSelectedNamesFa = "";

    [Tooltip("Typed answer when Other is selected.")]
    public string finalQuestionnaireOtherText = "";

    [Tooltip("Participant rating for how closely the game resembles real parking.")]
    public int realityClosenessRating = 0;

    [Tooltip("Persian label associated with the reality-closeness rating.")]
    public string realityClosenessLabelFa = "";

    [Tooltip("Optional participant feedback describing differences from real parking experience.")]
    public string realityFeedback = "";

    private void Awake()
    {
        // Prevent more than one ParticipantSession from existing.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        EnsureSessionIdentity();

        // Keep this object when Unity changes scenes.
        DontDestroyOnLoad(gameObject);
    }


    public void EnsureSessionIdentity()
    {
        DateTime nowUtc = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(sessionStartUtc))
        {
            sessionStartUtc = nowUtc.ToString(
                "o",
                CultureInfo.InvariantCulture
            );
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            string shortGuid =
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 8);

            sessionId =
                nowUtc.ToString(
                    "yyyyMMdd_HHmmss_fff",
                    CultureInfo.InvariantCulture
                ) +
                "_" +
                shortGuid;
        }
    }

    public string GetOrCreateSessionId()
    {
        EnsureSessionIdentity();
        return sessionId;
    }

    public void SetUsualParkingAnswer(
        string streetCodes,
        string streetNames,
        string otherText)
    {
        usualParkingAnswerParticipantId = NormalizeParticipantId(participantId);
        usualParkingStreetCodes = streetCodes ?? "";
        usualParkingStreetNames = streetNames ?? "";
        usualParkingOtherText = otherText != null ? otherText.Trim() : "";
        hasUsualParkingAnswer = true;
    }

    public bool HasUsualParkingAnswerForCurrentParticipant()
    {
        return hasUsualParkingAnswer &&
               NormalizeParticipantId(usualParkingAnswerParticipantId) ==
               NormalizeParticipantId(participantId);
    }

    public void SetFinalQuestionnaireAnswer(
        string selectedCodes,
        string selectedNamesFa,
        string otherText,
        int closenessRating,
        string closenessLabelFa,
        string feedbackText = "")
    {
        finalQuestionnaireParticipantId =
            NormalizeParticipantId(participantId);

        finalQuestionnaireSelectedCodes = selectedCodes ?? "";
        finalQuestionnaireSelectedNamesFa = selectedNamesFa ?? "";
        finalQuestionnaireOtherText =
            otherText != null ? otherText.Trim() : "";

        realityClosenessRating = closenessRating;
        realityClosenessLabelFa = closenessLabelFa ?? "";
        realityFeedback =
            feedbackText != null ? feedbackText.Trim() : "";

        hasFinalQuestionnaireAnswer = true;
    }

    public bool HasFinalQuestionnaireAnswerForCurrentParticipant()
    {
        return hasFinalQuestionnaireAnswer &&
               NormalizeParticipantId(finalQuestionnaireParticipantId) ==
               NormalizeParticipantId(participantId);
    }

    private static string NormalizeParticipantId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim().ToUpperInvariant();
    }
}
