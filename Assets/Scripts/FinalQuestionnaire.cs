using System;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(ToggleGroup))]
public class FinalQuestionnaire : MonoBehaviour
{
    [Header("Questionnaire UI")]
    [Tooltip("The full-screen post-game accuracy panel. If empty, this GameObject is used.")]
    public GameObject questionnairePanel;

    public TMP_Text titleText;
    public TMP_Text realityQuestionText;
    public TMP_Text feedbackQuestionText;
    public TMP_InputField feedbackInputField;
    public TMP_Text errorText;
    public TMP_Text submitButtonText;

    [Tooltip("Applies the approved Persian wording to the assigned UI labels at runtime.")]
    public bool applyDefaultPersianText = true;

    [Header("Reality Closeness - One Selection")]
    public Toggle realityVeryLowToggle;
    public Toggle realityLowToggle;
    public Toggle realityModerateToggle;
    public Toggle realityHighToggle;
    public Toggle realityVeryHighToggle;

    [Header("Questionnaire Export")]
    [Tooltip("Writes one post-game accuracy row linked to the other files by ParticipantId + SessionId.")]
    public bool writeQuestionnaireCsv = true;

    [Tooltip("Uses the same default folder name as ParkingLogger.")]
    public string outputSubfolder = "ParkingSurveyData";

    public string questionnaireFileName = "participant_reality_questionnaire.csv";


    [Header("Mobile Landscape Responsiveness")]
    [Tooltip(
        "On very wide landscape screens (common on Android phones), match the questionnaire UI to screen HEIGHT. " +
        "This keeps the optional feedback box and submit button inside the visible WebGL viewport."
    )]
    public bool fitQuestionnaireToLandscapeHeight = true;

    [Tooltip("Aspect ratio at/above which the questionnaire switches to height-matched scaling.")]
    [Range(1.6f, 3.0f)]
    public float wideLandscapeAspectThreshold = 1.9f;

    [Header("Next Step")]
    [Tooltip("Assign the SocioEconomicQuestionnaire component in the scene. When set, it is opened automatically as soon as this reality question is submitted successfully.")]
    public SocioEconomicQuestionnaire socioEconomicQuestionnaire;

    public event Action Submitted;

    private ToggleGroup realityToggleGroup;
    private bool hasSubmitted;
    private string participantIdForSubmission = "";

    private CanvasScaler questionnaireCanvasScaler;
    private bool questionnaireScalerStateCached;
    private CanvasScaler.ScreenMatchMode originalQuestionnaireScreenMatchMode;
    private float originalQuestionnaireMatchWidthOrHeight;

    private const string QuestionnaireCsvHeader =
        "ParticipantId,SessionId,StudyVersion,BuildVersion," +
        "RealityClosenessRating,RealityClosenessLabelFa,RealityFeedback,RecordedAtUtc";

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            if (questionnairePanel == null)
                questionnairePanel = gameObject;

            AutoBindFeedbackInputField();
        }
    }
#endif

    private void Awake()
    {
        if (questionnairePanel == null)
            questionnairePanel = gameObject;

        ConfigureRealityToggleGroup();
        AutoBindFeedbackInputField();

        if (applyDefaultPersianText)
            ApplyPersianQuestionnaireText();
    }

    public void Show(string participantId)
    {
        participantIdForSubmission = participantId != null
            ? participantId.Trim().ToUpperInvariant()
            : "";

        Show();
    }

    public void Show()
    {
        hasSubmitted = false;
        ClearSelection();
        ClearFeedback();
        SetError("");

        if (questionnairePanel == null)
            questionnairePanel = gameObject;

        questionnairePanel.SetActive(true);
        questionnairePanel.transform.SetAsLastSibling();

        ApplyResponsiveCanvasScaling();
        ConfigureRealityToggleGroup();
        AutoBindFeedbackInputField();

        if (applyDefaultPersianText)
            ApplyPersianQuestionnaireText();
    }

    public void Hide()
    {
        RestoreQuestionnaireCanvasScaling();

        if (questionnairePanel != null)
            questionnairePanel.SetActive(false);
    }

    // -----------------------------------------------------------------
    // Mobile / wide-landscape responsive scaling
    // -----------------------------------------------------------------

    private void ApplyResponsiveCanvasScaling()
    {
        if (!fitQuestionnaireToLandscapeHeight)
            return;

        if (questionnaireCanvasScaler == null)
        {
            Canvas targetCanvas = null;

            if (questionnairePanel != null)
                targetCanvas = questionnairePanel.GetComponentInParent<Canvas>();

            if (targetCanvas == null)
                targetCanvas = GetComponentInParent<Canvas>();

            if (targetCanvas != null)
                questionnaireCanvasScaler = targetCanvas.GetComponent<CanvasScaler>();
        }

        if (questionnaireCanvasScaler == null ||
            questionnaireCanvasScaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            return;
        }

        if (!questionnaireScalerStateCached)
        {
            originalQuestionnaireScreenMatchMode =
                questionnaireCanvasScaler.screenMatchMode;

            originalQuestionnaireMatchWidthOrHeight =
                questionnaireCanvasScaler.matchWidthOrHeight;

            questionnaireScalerStateCached = true;
        }

        float aspect =
            Screen.height > 0
                ? (float)Screen.width / Screen.height
                : 0f;

        bool wideLandscape =
            Screen.width > Screen.height &&
            aspect >= Mathf.Max(1.6f, wideLandscapeAspectThreshold);

        if (wideLandscape)
        {
            questionnaireCanvasScaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // 1 = match HEIGHT. Mobile browser chrome reduces usable height,
            // so width-matching can push the feedback box below the screen.
            questionnaireCanvasScaler.matchWidthOrHeight = 1f;
        }
        else
        {
            RestoreQuestionnaireCanvasScaling();
        }

        Canvas.ForceUpdateCanvases();

        RectTransform panelRect =
            questionnairePanel != null
                ? questionnairePanel.transform as RectTransform
                : null;

        if (panelRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }


    private void RestoreQuestionnaireCanvasScaling()
    {
        if (!questionnaireScalerStateCached || questionnaireCanvasScaler == null)
            return;

        questionnaireCanvasScaler.screenMatchMode =
            originalQuestionnaireScreenMatchMode;

        questionnaireCanvasScaler.matchWidthOrHeight =
            originalQuestionnaireMatchWidthOrHeight;
    }


    private void OnRectTransformDimensionsChange()
    {
        if (!Application.isPlaying)
            return;

        if (questionnairePanel != null && questionnairePanel.activeInHierarchy)
            ApplyResponsiveCanvasScaling();
    }


    private void OnDestroy()
    {
        RestoreQuestionnaireCanvasScaling();
    }


    // Assign this method to SubmitButton -> On Click.
    public void SubmitAnswers()
    {
        if (hasSubmitted)
            return;

        GetRealityAnswer(out int realityRating, out string realityLabelFa);

        if (realityRating == 0)
        {
            SetError(
                "لطفاً میزان نزدیک‌بودن شبیه‌ساز به تجربه واقعی پارک را انتخاب کنید."
            );
            return;
        }

        ParticipantSession session = GetOrCreateParticipantSession();

        if (string.IsNullOrWhiteSpace(session.participantId) &&
            !string.IsNullOrWhiteSpace(participantIdForSubmission))
        {
            session.participantId = participantIdForSubmission;
        }

        // Re-resolve the field at submission time as a final safeguard.
        // This prevents a missing Inspector reference from silently discarding
        // feedback that the participant typed into the visible TMP_InputField.
        AutoBindFeedbackInputField();

        string realityFeedback =
            feedbackInputField != null
                ? (feedbackInputField.text ?? "").Trim()
                : "";

        Debug.Log(
            "[FinalQuestionnaire] Feedback captured = [" +
            realityFeedback +
            "] | inputAssigned=" +
            (feedbackInputField != null)
        );

        // The usual-street answer is collected separately before the game.
        // Empty street arguments prevent this post-game panel from replacing it.
        session.SetFinalQuestionnaireAnswer(
            "",
            "",
            "",
            realityRating,
            realityLabelFa,
            realityFeedback
        );

        if (writeQuestionnaireCsv)
        {
            try
            {
                AppendRealityQuestionnaire(session);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[FinalQuestionnaire] Could not write the reality questionnaire CSV: " +
                    exception.Message
                );
            }
        }

        hasSubmitted = true;
        SetError("");
        Hide();
        Submitted?.Invoke();

        // Move the participant straight on to the socioeconomic questionnaire.
        if (socioEconomicQuestionnaire != null)
            socioEconomicQuestionnaire.OpenQuestionnaire();
    }

    [ContextMenu("Apply Persian Questionnaire Text")]
    public void ApplyPersianQuestionnaireText()
    {
        SetText(titleText, "ارزیابی شبیه‌ساز");

        SetText(
            realityQuestionText,
            "به نظر شما، تجربه جست‌وجو و انتخاب جای پارک در این شبیه‌ساز تا چه اندازه به تجربه واقعی شما در اطراف درب شماره ۲ (درب ملت) نزدیک بود؟"
        );

        SetText(
            feedbackQuestionText,
            "اگر بخشی از شبیه‌ساز با تجربه واقعی شما متفاوت بود، لطفاً به‌اختصار توضیح دهید. (اختیاری)"
        );

        SetFeedbackPlaceholder("پاسخ شما (اختیاری)");

        SetText(submitButtonText, "ثبت پاسخ و ادامه");

        SetToggleLabel(realityVeryLowToggle, "۱ ـ بسیار کم");
        SetToggleLabel(realityLowToggle, "۲ ـ کم");
        SetToggleLabel(realityModerateToggle, "۳ ـ متوسط");
        SetToggleLabel(realityHighToggle, "۴ ـ زیاد");
        SetToggleLabel(realityVeryHighToggle, "۵ ـ بسیار زیاد");
    }

    /// <summary>
    /// Finds the feedback TMP_InputField automatically if its serialized
    /// Inspector reference is missing. This is especially useful after script
    /// replacement or scene/prefab changes.
    /// </summary>
    [ContextMenu("Auto Bind Feedback Input Field")]
    public void AutoBindFeedbackInputField()
    {
        if (feedbackInputField != null)
            return;

        // First search inside the questionnaire panel because that is the most
        // specific and safest scope.
        if (questionnairePanel != null)
        {
            TMP_InputField[] panelInputs =
                questionnairePanel.GetComponentsInChildren<TMP_InputField>(true);

            // Prefer the exact GameObject name used in this survey.
            foreach (TMP_InputField input in panelInputs)
            {
                if (input == null)
                    continue;

                if (string.Equals(
                        input.gameObject.name,
                        "FeedbackInputField",
                        StringComparison.OrdinalIgnoreCase))
                {
                    feedbackInputField = input;

                    Debug.Log(
                        "[FinalQuestionnaire] Auto-bound FeedbackInputField from questionnaire panel."
                    );
                    return;
                }
            }

            // Safe fallback: if there is only one TMP_InputField anywhere in
            // this questionnaire panel, that field is the feedback box.
            if (panelInputs.Length == 1 && panelInputs[0] != null)
            {
                feedbackInputField = panelInputs[0];

                Debug.Log(
                    "[FinalQuestionnaire] Auto-bound the only TMP_InputField in the questionnaire panel as feedbackInputField."
                );
                return;
            }
        }

        // Final fallback searches inactive objects in the loaded scene.
        TMP_InputField[] allInputs =
            FindObjectsByType<TMP_InputField>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (TMP_InputField input in allInputs)
        {
            if (input == null)
                continue;

            if (string.Equals(
                    input.gameObject.name,
                    "FeedbackInputField",
                    StringComparison.OrdinalIgnoreCase))
            {
                feedbackInputField = input;

                Debug.Log(
                    "[FinalQuestionnaire] Auto-bound FeedbackInputField from scene search."
                );
                return;
            }
        }

        Debug.LogError(
            "[FinalQuestionnaire] FeedbackInputField could not be found. " +
            "Typed feedback will not be saved until a TMP_InputField named " +
            "'FeedbackInputField' exists or is assigned in the Inspector."
        );
    }

    private void ConfigureRealityToggleGroup()
    {
        realityToggleGroup = GetComponent<ToggleGroup>();
        realityToggleGroup.allowSwitchOff = true;

        AssignToggleGroup(realityVeryLowToggle, realityToggleGroup);
        AssignToggleGroup(realityLowToggle, realityToggleGroup);
        AssignToggleGroup(realityModerateToggle, realityToggleGroup);
        AssignToggleGroup(realityHighToggle, realityToggleGroup);
        AssignToggleGroup(realityVeryHighToggle, realityToggleGroup);
    }

    private void ClearSelection()
    {
        SetToggleWithoutNotification(realityVeryLowToggle, false);
        SetToggleWithoutNotification(realityLowToggle, false);
        SetToggleWithoutNotification(realityModerateToggle, false);
        SetToggleWithoutNotification(realityHighToggle, false);
        SetToggleWithoutNotification(realityVeryHighToggle, false);
    }

    private void ClearFeedback()
    {
        if (feedbackInputField != null)
            feedbackInputField.SetTextWithoutNotify("");
    }

    private void GetRealityAnswer(out int rating, out string labelFa)
    {
        rating = 0;
        labelFa = "";

        if (realityVeryLowToggle != null && realityVeryLowToggle.isOn)
        {
            rating = 1;
            labelFa = "بسیار کم";
        }
        else if (realityLowToggle != null && realityLowToggle.isOn)
        {
            rating = 2;
            labelFa = "کم";
        }
        else if (realityModerateToggle != null && realityModerateToggle.isOn)
        {
            rating = 3;
            labelFa = "متوسط";
        }
        else if (realityHighToggle != null && realityHighToggle.isOn)
        {
            rating = 4;
            labelFa = "زیاد";
        }
        else if (realityVeryHighToggle != null && realityVeryHighToggle.isOn)
        {
            rating = 5;
            labelFa = "بسیار زیاد";
        }
    }

    private ParticipantSession GetOrCreateParticipantSession()
    {
        if (ParticipantSession.Instance != null)
            return ParticipantSession.Instance;

        ParticipantSession existing =
            FindFirstObjectByType<ParticipantSession>();

        if (existing != null)
            return existing;

        GameObject sessionObject = new GameObject("ParticipantSession");
        ParticipantSession session =
            sessionObject.AddComponent<ParticipantSession>();

        if (!string.IsNullOrWhiteSpace(participantIdForSubmission))
            session.participantId = participantIdForSubmission;

        return session;
    }

    private void AppendRealityQuestionnaire(ParticipantSession session)
    {
        string safeFileName = Path.GetFileName(questionnaireFileName);

        if (string.IsNullOrWhiteSpace(safeFileName))
            safeFileName = "participant_reality_questionnaire.csv";

        string folder = Path.Combine(
            Application.persistentDataPath,
            string.IsNullOrWhiteSpace(outputSubfolder)
                ? "ParkingSurveyData"
                : outputSubfolder.Trim()
        );

        Directory.CreateDirectory(folder);

        string filePath =
            Path.Combine(folder, safeFileName);

        session.EnsureSessionIdentity();

        string sessionId = ResolveSessionId(session);
        string studyVersion = ResolveStudyVersion();
        string buildVersion = ResolveBuildVersion();

        EnsureQuestionnaireCsvHeader(filePath);

        using (StreamWriter writer = new StreamWriter(
            filePath,
            true,
            new UTF8Encoding(false)))
        {
            writer.WriteLine(string.Join(",", new string[]
            {
                EscapeCsv(session.participantId),
                EscapeCsv(sessionId),
                EscapeCsv(studyVersion),
                EscapeCsv(buildVersion),
                session.realityClosenessRating.ToString(
                    CultureInfo.InvariantCulture
                ),
                EscapeCsv(session.realityClosenessLabelFa),
                EscapeCsv(session.realityFeedback),
                EscapeCsv(
                    DateTime.UtcNow.ToString(
                        "o",
                        CultureInfo.InvariantCulture
                    )
                )
            }));
        }

        Debug.Log(
            "[FinalQuestionnaire] Reality answer saved to: " +
            filePath
        );
    }

    private void EnsureQuestionnaireCsvHeader(string filePath)
    {
        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
        {
            File.WriteAllText(
                filePath,
                QuestionnaireCsvHeader + Environment.NewLine,
                new UTF8Encoding(true)
            );
            return;
        }

        string firstLine = "";
        using (StreamReader reader = new StreamReader(
            filePath,
            Encoding.UTF8,
            true))
        {
            firstLine = reader.ReadLine() ?? "";
        }

        firstLine = firstLine.TrimStart('\uFEFF');

        if (string.Equals(
                firstLine,
                QuestionnaireCsvHeader,
                StringComparison.Ordinal))
        {
            return;
        }

        string directory =
            Path.GetDirectoryName(filePath) ?? "";

        string fileNameWithoutExtension =
            Path.GetFileNameWithoutExtension(filePath);

        string extension =
            Path.GetExtension(filePath);

        string legacyPath = Path.Combine(
            directory,
            fileNameWithoutExtension +
            "_legacy_" +
            DateTime.UtcNow.ToString(
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture
            ) +
            extension
        );

        File.Move(filePath, legacyPath);

        File.WriteAllText(
            filePath,
            QuestionnaireCsvHeader + Environment.NewLine,
            new UTF8Encoding(true)
        );

        Debug.LogWarning(
            "[FinalQuestionnaire] Existing questionnaire CSV used an older header. " +
            "It was preserved as: " + legacyPath
        );
    }

    private string ResolveSessionId(ParticipantSession session)
    {
        if (ParkingLogger.Instance != null &&
            !string.IsNullOrWhiteSpace(ParkingLogger.Instance.SessionId))
        {
            return ParkingLogger.Instance.SessionId;
        }

        return session != null
            ? session.GetOrCreateSessionId()
            : string.Empty;
    }

    private string ResolveStudyVersion()
    {
        ScenarioManager manager = FindFirstObjectByType<ScenarioManager>();

        return manager != null && !string.IsNullOrWhiteSpace(manager.studyVersion)
            ? manager.studyVersion.Trim()
            : string.Empty;
    }

    private string ResolveBuildVersion()
    {
        ScenarioManager manager = FindFirstObjectByType<ScenarioManager>();

        return manager != null && !string.IsNullOrWhiteSpace(manager.buildVersion)
            ? manager.buildVersion.Trim()
            : Application.version;
    }

    private static void AssignToggleGroup(
        Toggle toggle,
        ToggleGroup group)
    {
        if (toggle != null)
            toggle.group = group;
    }

    private static void SetToggleWithoutNotification(
        Toggle toggle,
        bool value)
    {
        if (toggle != null)
            toggle.SetIsOnWithoutNotify(value);
    }

    private static void SetToggleLabel(
        Toggle toggle,
        string value)
    {
        if (toggle == null)
            return;

        TMP_Text label =
            toggle.GetComponentInChildren<TMP_Text>(true);

        SetText(label, value);
    }

    private void SetFeedbackPlaceholder(string value)
    {
        if (feedbackInputField == null ||
            feedbackInputField.placeholder == null)
        {
            return;
        }

        TMP_Text placeholderText =
            feedbackInputField.placeholder as TMP_Text;

        SetText(placeholderText, value);
    }

    private static void SetText(
        TMP_Text label,
        string value)
    {
        if (label == null)
            return;

        label.text = value ?? "";
        label.SetVerticesDirty();
        label.SetLayoutDirty();
    }

    private static string EscapeCsv(string value)
    {
        string safeValue = value ?? "";

        if (safeValue.IndexOfAny(
            new char[] { ',', '"', '\r', '\n' }) < 0)
        {
            return safeValue;
        }

        return "\"" +
               safeValue.Replace("\"", "\"\"") +
               "\"";
    }

    private void SetError(string message)
    {
        if (errorText == null)
            return;

        errorText.text = message ?? "";
        errorText.gameObject.SetActive(
            !string.IsNullOrEmpty(message)
        );
    }
}
