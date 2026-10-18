using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class EligibilityScreen : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("First panel shown when the scene starts.")]
    public GameObject orientationPanel;

    public GameObject eligibilityPanel;
    public GameObject demographicsPanel;
    public GameObject ineligiblePanel;

    [Header("Eligibility Toggles")]
    public Toggle currentStudentToggle;
    public Toggle drivingLicenceToggle;
    public Toggle gate2ExperienceToggle;
    public Toggle firstParticipationToggle;

    [Header("Eligibility Flow")]
    [Tooltip(
        "When enabled, participants who do not meet all four screening " +
        "conditions may still continue to demographics and complete the survey. " +
        "Their actual answers and eligible=false are still uploaded to Supabase."
    )]
    public bool allowIneligibleToContinue = true;

    [Header("Early Demographics (shown after eligibility screening)")]
    public TMP_InputField ageInputField;
    public Toggle femaleToggle;
    public Toggle maleToggle;
    public TMP_Text demographicsErrorText;

    [Min(0)] public int minimumAge = 18;
    [Min(1)] public int maximumAge = 99;

    [Header("Messages")]
    public TMP_Text errorText;

    [Header("Game Scene")]
    [Tooltip("Enter the exact name of the Unity game scene.")]
    public string gameSceneName = "ParkingGame_RealLocation_Final";

    [Header("Automatic Participant ID")]
    [Tooltip(
        "Prefix used for automatically generated participant IDs. " +
        "WEB is recommended for the online survey."
    )]
    public string participantIdPrefix = "WEB";

    [Tooltip(
        "Number of random hexadecimal characters appended to the ID. " +
        "12 is recommended."
    )]
    [Range(8, 24)]
    public int randomIdLength = 12;

    private bool participantIdAssigned = false;
    private string assignedParticipantId = "";

    private void Start()
    {
        // First show only the orientation instruction.
        if (orientationPanel != null)
            orientationPanel.SetActive(true);
        else
            Debug.LogWarning(
                "[EligibilityScreen] OrientationPanel has not been assigned."
            );

        // Hide eligibility until the participant presses Continue.
        if (eligibilityPanel != null)
            eligibilityPanel.SetActive(false);

        // Hide early demographics until eligibility is passed.
        if (demographicsPanel != null)
            demographicsPanel.SetActive(false);

        // Hide the ineligible screen at the beginning.
        if (ineligiblePanel != null)
            ineligiblePanel.SetActive(false);

        if (errorText != null)
            errorText.text = "";

        if (demographicsErrorText != null)
            demographicsErrorText.text = "";
    }

    /// <summary>
    /// Called by the Continue button on the OrientationPanel.
    /// Opens the eligibility questionnaire.
    /// </summary>
    public void ContinueToEligibility()
    {
        if (orientationPanel != null)
            orientationPanel.SetActive(false);

        if (eligibilityPanel != null)
            eligibilityPanel.SetActive(true);

        if (errorText != null)
            errorText.text = "";
    }

    public void SubmitEligibility()
    {
        if (!ReferencesAreAssigned())
            return;

        ParticipantSession session = GetOrCreateParticipantSession();

        if (session == null)
        {
            if (errorText != null)
                errorText.text = "خطا در ایجاد جلسه پژوهش.";

            Debug.LogError(
                "[EligibilityScreen] Could not create ParticipantSession."
            );

            return;
        }

        string participantId = AssignUniqueParticipantId();

        if (string.IsNullOrWhiteSpace(participantId))
        {
            if (errorText != null)
                errorText.text = "خطا در ایجاد شناسه شرکت‌کننده.";

            return;
        }

        session.participantId = participantId;
        session.EnsureSessionIdentity();

        // Screening result remains TRUE only when all four conditions hold.
        // A participant may still proceed with eligible=false if the
        // allowIneligibleToContinue setting is enabled.
        bool eligible =
            currentStudentToggle.isOn &&
            drivingLicenceToggle.isOn &&
            gate2ExperienceToggle.isOn &&
            firstParticipationToggle.isOn;

        // Upload eligibility answers to Supabase.
        if (SupabaseSurveyBridge.Instance != null)
        {
            SupabaseSurveyBridge.Instance.UploadEligibility(
                currentStudentToggle.isOn,
                drivingLicenceToggle.isOn,
                gate2ExperienceToggle.isOn,
                firstParticipationToggle.isOn,
                eligible
            );
        }

        // Preserve the original blocking behavior only when explicitly requested.
        // By default, screening is recorded but does not prevent completion.
        if (!eligible && !allowIneligibleToContinue)
        {
            eligibilityPanel.SetActive(false);

            if (ineligiblePanel != null)
                ineligiblePanel.SetActive(true);

            Debug.Log(
                "[EligibilityScreen] Participant did not meet all screening " +
                "conditions and was stopped. ID=" + participantId
            );

            return;
        }

        if (!eligible)
        {
            Debug.Log(
                "[EligibilityScreen] Participant did not meet all screening " +
                "conditions but may continue. eligible=false | ID=" +
                participantId
            );
        }
        else
        {
            Debug.Log(
                "[EligibilityScreen] Eligible participant assigned ID: " +
                participantId
            );
        }

        // All permitted participants continue to the same demographics screen.
        eligibilityPanel.SetActive(false);

        if (ineligiblePanel != null)
            ineligiblePanel.SetActive(false);

        if (demographicsPanel != null)
        {
            demographicsPanel.SetActive(true);

            if (demographicsErrorText != null)
                demographicsErrorText.text = "";

            return;
        }

        Debug.LogError("[EligibilityScreen] DemographicsPanel is not assigned.");
        return;
    }

    /// <summary>
    /// Called by the Start button on the demographics panel.
    /// Validates and immediately uploads age/gender, then loads the game.
    /// </summary>
    public void SubmitDemographicsAndStart()
    {
        if (ageInputField == null || femaleToggle == null || maleToggle == null)
        {
            Debug.LogError("[EligibilityScreen] Early-demographics references are missing.");
            if (demographicsErrorText != null)
                demographicsErrorText.text = "خطا در تنظیم صفحه. لطفاً با پژوهشگر تماس بگیرید.";
            return;
        }

        string rawAge = ConvertPersianDigitsToEnglish(ageInputField.text).Trim();
        if (!int.TryParse(rawAge, NumberStyles.Integer, CultureInfo.InvariantCulture, out int age))
        {
            if (demographicsErrorText != null)
                demographicsErrorText.text = "لطفاً سن خود را به صورت عدد وارد کنید.";
            return;
        }

        int minAge = Mathf.Min(minimumAge, maximumAge);
        int maxAge = Mathf.Max(minimumAge, maximumAge);
        if (age < minAge || age > maxAge)
        {
            if (demographicsErrorText != null)
                demographicsErrorText.text = $"سن باید بین {minAge} تا {maxAge} سال باشد.";
            return;
        }

        if (femaleToggle.isOn == maleToggle.isOn)
        {
            if (demographicsErrorText != null)
                demographicsErrorText.text = "لطفاً جنسیت خود را انتخاب کنید.";
            return;
        }

        string genderCode = femaleToggle.isOn ? "FEMALE" : "MALE";
        string genderLabel = femaleToggle.isOn ? "زن" : "مرد";

        if (SupabaseSurveyBridge.Instance != null)
            SupabaseSurveyBridge.Instance.UploadEarlyDemographics(age, genderCode, genderLabel);
        else
            Debug.LogWarning("[EligibilityScreen] SupabaseSurveyBridge is missing; demographics were not uploaded.");

        LoadGameScene();
    }

    private void LoadGameScene()
    {
        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError(
                "[EligibilityScreen] Game scene name is empty."
            );

            if (errorText != null)
                errorText.text = "خطا در بارگذاری مرحله اصلی.";

            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Generates a unique participant ID.
    ///
    /// Example:
    /// WEB_P_20260823_125410_583_A14F08BC52D9
    /// </summary>
    private string AssignUniqueParticipantId()
    {
        if (participantIdAssigned &&
            !string.IsNullOrWhiteSpace(assignedParticipantId))
        {
            return assignedParticipantId;
        }

        string cleanPrefix =
            string.IsNullOrWhiteSpace(participantIdPrefix)
                ? "WEB"
                : participantIdPrefix.Trim().ToUpperInvariant();

        int safeRandomLength =
            Mathf.Clamp(randomIdLength, 8, 24);

        string timestamp =
            DateTime.UtcNow.ToString(
                "yyyyMMdd_HHmmss_fff",
                CultureInfo.InvariantCulture
            );

        string randomPart =
            Guid.NewGuid()
                .ToString("N")
                .Substring(0, safeRandomLength)
                .ToUpperInvariant();

        assignedParticipantId =
            cleanPrefix +
            "_P_" +
            timestamp +
            "_" +
            randomPart;

        participantIdAssigned = true;

        return assignedParticipantId;
    }

    private ParticipantSession GetOrCreateParticipantSession()
    {
        if (ParticipantSession.Instance != null)
            return ParticipantSession.Instance;

        ParticipantSession existing =
            FindFirstObjectByType<ParticipantSession>();

        if (existing != null)
            return existing;

        GameObject sessionObject =
            new GameObject("ParticipantSession");

        return sessionObject.AddComponent<ParticipantSession>();
    }

    private bool ReferencesAreAssigned()
    {
        bool assigned =
            eligibilityPanel != null &&
            demographicsPanel != null &&
            (allowIneligibleToContinue || ineligiblePanel != null) &&
            currentStudentToggle != null &&
            drivingLicenceToggle != null &&
            gate2ExperienceToggle != null &&
            firstParticipationToggle != null;

        if (!assigned)
        {
            Debug.LogError(
                "[EligibilityScreen] One or more references are missing."
            );

            if (errorText != null)
            {
                errorText.text =
                    "خطا در تنظیم صفحه. لطفاً با پژوهشگر تماس بگیرید.";
            }
        }

        return assigned;
    }

    private static string ConvertPersianDigitsToEnglish(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        char[] persian = { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };
        char[] arabic = { '٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩' };
        char[] western = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };

        for (int i = 0; i < western.Length; i++)
        {
            value = value.Replace(persian[i], western[i]);
            value = value.Replace(arabic[i], western[i]);
        }
        return value;
    }

    public void ExitSurvey()
    {
#if UNITY_WEBGL && !UNITY_EDITOR

        // Browsers do not allow Unity to close the tab.
        Debug.Log(
            "[EligibilityScreen] ExitSurvey called in WebGL. " +
            "Browser tabs cannot be closed programmatically."
        );

#else

        Application.Quit();

#endif

#if UNITY_EDITOR

        UnityEditor.EditorApplication.isPlaying = false;

#endif
    }
}