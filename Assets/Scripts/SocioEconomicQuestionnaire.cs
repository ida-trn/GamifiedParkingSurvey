using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using RTLTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Post-game socioeconomic and travel-habits questionnaire for the IUST
/// gamified parking survey.
///
/// Features:
/// - Automatically creates a Persian, one-question-at-a-time UI.
/// - Contains the remaining post-game socioeconomic/travel-habits questions. Age and gender are collected before gameplay.
/// - Automatically detects the end of ScenarioManager through reflection,
///   so ScenarioManager does not need to be edited.
/// - Saves a separate UTF-8 CSV file linked by ParticipantId and SessionId.
/// - Autosaves after every answer as Incomplete and marks it Completed on submit.
/// </summary>
public class SocioEconomicQuestionnaire : MonoBehaviour
{
    [Header("Automatic launch")]
    [Tooltip("Leave OFF when FinalQuestionnaire calls OpenQuestionnaire() directly after the reality question is submitted (recommended). Only turn this ON if you are NOT using that hand-off and instead want this panel to open on its own as soon as ScenarioManager finishes all rounds - turning both on can open this panel too early, before the reality question is shown.")]
    public bool autoLaunchAfterSurveyCompletion = false;

    [Tooltip("Optional. Assign ScenarioManager. If empty, it is found automatically.")]
    public MonoBehaviour scenarioManager;

    [Tooltip("Small delay after the final game screen before the questionnaire opens.")]
    [Min(0f)] public float launchDelaySeconds = 0.35f;

    [Header("Identity / version")]
    public string fallbackParticipantId = "P001";
    public string studyVersion = "1.0";
    public string buildVersion = "1.0";

    [Header("Output")]
    public string outputSubfolder = "ParkingSurveyData";
    public bool printOutputPath = true;

    [Header("Persian UI")]
    [Tooltip("Assign the same Persian TMP font asset used elsewhere in the survey.")]
    public TMP_FontAsset persianFontAsset;

    [Range(18f, 54f)] public float titleFontSize = 34f;
    [Range(16f, 44f)] public float questionFontSize = 28f;
    [Range(14f, 38f)] public float optionFontSize = 23f;
    [Range(12f, 32f)] public float navigationFontSize = 21f;

    [Header("Age input")]
    [Min(0)] public int minimumAge = 18;
    [Min(1)] public int maximumAge = 99;

    [Header("Optional scene references")]
    [Tooltip("Optional. These objects are hidden when the questionnaire opens.")]
    public GameObject[] objectsToHideWhenOpened;

    [Tooltip("Optional. The final gameplay ResultPanel is hidden automatically when found.")]
    public GameObject finalResultPanel;

    [Header("Completion")]
    public UnityEvent onQuestionnaireCompleted;

    private readonly List<QuestionDefinition> questions = new List<QuestionDefinition>();
    private readonly Dictionary<string, AnswerRecord> answers = new Dictionary<string, AnswerRecord>();
    private readonly List<Button> optionButtons = new List<Button>();

    private GameObject questionnaireCanvasRoot;
    private GameObject questionnairePanel;
    private GameObject thankYouPanel;
    private RTLTextMeshPro titleLabel;
    private RTLTextMeshPro progressLabel;
    private RTLTextMeshPro questionLabel;
    private RTLTextMeshPro errorLabel;
    private RectTransform optionContent;
    private Button previousButton;
    private Button nextButton;
    private RTLTextMeshPro nextButtonLabel;
    private TMP_InputField ageInputField;

    private int currentQuestionIndex;
    private bool questionnaireOpened;
    private bool questionnaireCompleted;
    private bool completionDetected;
    private float completionDetectedTime;
    private DateTime questionnaireStartUtc;
    private string participantId;
    private string sessionId;
    private string outputFilePath;

    private FieldInfo surveyCompletedField;
    private FieldInfo cumulativeScoreField;
    private FieldInfo currentScoreField;
    private float finalGameScore;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private void Awake()
    {
        BuildQuestionDefinitions();
        EnsureEventSystem();
    }

    private void Start()
    {
        if (scenarioManager == null)
            scenarioManager = FindScenarioManager();

        CacheScenarioManagerFields();
    }

    private void Update()
    {
        if (!autoLaunchAfterSurveyCompletion || questionnaireOpened || questionnaireCompleted)
            return;

        if (scenarioManager == null)
        {
            scenarioManager = FindScenarioManager();
            CacheScenarioManagerFields();

            if (scenarioManager == null)
                return;
        }

        bool isComplete = ReadSurveyCompleted();

        if (!isComplete)
        {
            completionDetected = false;
            return;
        }

        if (!completionDetected)
        {
            completionDetected = true;
            completionDetectedTime = Time.unscaledTime;
            finalGameScore = ReadFinalScore();
            return;
        }

        if (Time.unscaledTime - completionDetectedTime >= launchDelaySeconds)
            OpenQuestionnaire();
    }

    [UnityEngine.ContextMenu("Open Questionnaire Now")]
    public void OpenQuestionnaire()
    {
        if (questionnaireOpened || questionnaireCompleted)
            return;

        ResolveIdentity();
        ResolveVersions();
        finalGameScore = ReadFinalScore();
        questionnaireStartUtc = DateTime.UtcNow;
        currentQuestionIndex = 0;
        questionnaireOpened = true;

        if (finalResultPanel == null)
            finalResultPanel = FindObjectByNames("ResultPanel", "ResultsPanel");

        if (finalResultPanel != null)
            finalResultPanel.SetActive(false);

        if (objectsToHideWhenOpened != null)
        {
            foreach (GameObject obj in objectsToHideWhenOpened)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }

        BuildRuntimeUI();
        ShowQuestion(0);
        SaveQuestionnaireCsv("Incomplete");
    }

    private void BuildQuestionDefinitions()
    {
        questions.Clear();

        AddQuestion(
            "EducationLevel",
            "مقطع تحصیلی فعلی یا آخرین مدرک تحصیلی شما چیست؟",
            Option("ASSOCIATE", "کاردانی"),
            Option("BACHELOR", "کارشناسی"),
            Option("MASTER", "کارشناسی ارشد"),
            Option("PHD", "دکتری")
        );

        AddQuestion(
            "Faculty",
            "دانشکده یا حوزه تحصیلی شما در زمان تحصیل در دانشگاه علم و صنعت کدام است؟",
            Option("CIVIL", "مهندسی عمران"),
            Option("MECHANICAL", "مهندسی مکانیک"),
            Option("ARCH_URBAN", "معماری و شهرسازی"),
            Option("INDUSTRIAL_DESIGN", "طراحی صنعتی"),
            Option("MATERIALS_METALLURGY", "مهندسی مواد و متالورژی"),
            Option("COMPUTER", "مهندسی کامپیوتر"),
            Option("ELECTRICAL", "مهندسی برق"),
            Option("RAILWAY", "مهندسی راه‌آهن"),
            Option("CHEMICAL_OIL_GAS", "مهندسی شیمی، نفت و گاز"),
            Option("INDUSTRIAL", "مهندسی صنایع"),
            Option("AUTOMOTIVE", "مهندسی خودرو"),
            Option("BASIC_SCIENCES", "علوم پایه"),
            Option("MANAGEMENT_ECONOMICS_PROGRESS", "مدیریت، اقتصاد و مهندسی پیشرفت"),
            Option("NEW_TECHNOLOGIES", "فناوری‌های نوین"),
            Option("OTHER", "سایر")
        );

        AddQuestion(
            "EmploymentStatus",
            "وضعیت اشتغال شما چگونه است؟",
            Option("NOT_EMPLOYED", "شاغل نیستم"),
            Option("PART_TIME", "شاغل پاره‌وقت"),
            Option("FULL_TIME", "شاغل تمام‌وقت"),
            Option("SELF_EMPLOYED", "خوداشتغال"),
            Option("OTHER", "سایر")
        );

        AddQuestion(
            "HouseholdSize",
            "تعداد اعضای خانوار شما چند نفر است؟",
            Option("1", "۱ نفر"),
            Option("2", "۲ نفر"),
            Option("3", "۳ نفر"),
            Option("4", "۴ نفر"),
            Option("5_PLUS", "۵ نفر و بیشتر")
        );

        AddQuestion(
            "ResidenceType",
            "در حال حاضر محل سکونت شما کدام است؟",
            Option("WITH_FAMILY", "همراه خانواده"),
            Option("DORMITORY", "خوابگاه دانشجویی"),
            Option("RENTED", "منزل استیجاری مستقل"),
            Option("OWNED", "منزل شخصی مستقل"),
            Option("OTHER", "سایر")
        );

        AddQuestion(
            "HouseholdVehicleCount",
            "خانوار شما چند خودروی سواری در اختیار دارد؟",
            Option("0", "بدون خودرو"),
            Option("1", "۱ خودرو"),
            Option("2", "۲ خودرو"),
            Option("3_PLUS", "۳ خودرو و بیشتر")
        );

        AddQuestion(
            "PrivateCarAccess",
            "میزان دسترسی شما به خودروی شخصی برای سفرهای دانشگاهی چقدر است؟",
            Option("ALWAYS", "همیشه"),
            Option("MOST_DAYS", "در بیشتر روزها"),
            Option("SOMETIMES", "گاهی"),
            Option("RARELY", "به‌ندرت")
        );

        AddQuestion(
            "UsualUniversityMode",
            "شیوه معمول رفت‌وآمد شما به دانشگاه چیست؟",
            Option("CAR_DRIVER", "راننده خودروی شخصی"),
            Option("CAR_PASSENGER", "سرنشین خودروی شخصی"),
            Option("TAXI", "تاکسی یا تاکسی اینترنتی"),
            Option("PUBLIC_TRANSPORT", "حمل‌ونقل عمومی"),
            Option("MOTORCYCLE", "موتورسیکلت"),
            Option("WALK_BICYCLE", "پیاده یا دوچرخه"),
            Option("MIXED", "ترکیبی")
        );

        AddQuestion(
            "CarTravelFrequency",
            "معمولاً چند روز در هفته با خودروی شخصی به دانشگاه مراجعه می‌کنید؟",
            Option("5_PLUS_WEEK", "۵ روز یا بیشتر در هفته"),
            Option("3_4_WEEK", "۳ تا ۴ روز در هفته"),
            Option("1_2_WEEK", "۱ تا ۲ روز در هفته"),
            Option("FEW_MONTH", "چند بار در ماه"),
            Option("RARELY", "به‌ندرت")
        );

        AddQuestion(
            "Gate2ParkingFrequency",
            "چند وقت یک‌بار در محدوده درب شماره ۲ (درب ملت) برای پارک جست‌وجو می‌کنید؟",
            Option("5_PLUS_WEEK", "۵ روز یا بیشتر در هفته"),
            Option("3_4_WEEK", "۳ تا ۴ روز در هفته"),
            Option("1_2_WEEK", "۱ تا ۲ روز در هفته"),
            Option("FEW_MONTH", "چند بار در ماه"),
            Option("RARELY", "به‌ندرت")
        );

        AddQuestion(
            "UsualParkingPurpose",
            "هدف معمول شما از مراجعه با خودرو و پارک در محدوده دانشگاه چیست؟",
            Option("CLASS", "حضور در کلاس"),
            Option("RESEARCH_LAB", "فعالیت پژوهشی یا آزمایشگاهی"),
            Option("WORK", "فعالیت شغلی"),
            Option("ADMIN", "امور اداری"),
            Option("MEETING_SOCIAL", "جلسه یا فعالیت اجتماعی"),
            Option("OTHER", "سایر")
        );

        AddQuestion(
            "TypicalParkingDuration",
            "مدت توقف معمول شما در دانشگاه چقدر است؟",
            Option("UNDER_1H", "کمتر از ۱ ساعت"),
            Option("1_2H", "۱ تا ۲ ساعت"),
            Option("2_4H", "۲ تا ۴ ساعت"),
            Option("4_6H", "۴ تا ۶ ساعت"),
            Option("6H_PLUS", "بیش از ۶ ساعت")
        );

        AddQuestion(
            "TypicalSearchTime",
            "در شرایط معمول، چه مدت برای یافتن جای پارک اطراف دانشگاه جست‌وجو می‌کنید؟",
            Option("UNDER_2MIN", "کمتر از ۲ دقیقه"),
            Option("2_5MIN", "۲ تا ۵ دقیقه"),
            Option("5_10MIN", "۵ تا ۱۰ دقیقه"),
            Option("10_15MIN", "۱۰ تا ۱۵ دقیقه"),
            Option("15MIN_PLUS", "بیش از ۱۵ دقیقه")
        );

        AddQuestion(
            "RelativeWalkingSpeed",
            "سرعت معمول پیاده‌روی خود را نسبت به دیگران چگونه ارزیابی می‌کنید؟",
            Option("SLOWER", "کندتر از متوسط"),
            Option("AVERAGE", "متوسط"),
            Option("FASTER", "سریع‌تر از متوسط")
        );

        AddQuestion(
            "MonthlyHouseholdIncome",
            "درآمد ماهانه خانوار شما تقریباً در کدام بازه قرار دارد؟",
            Option("UNDER_30M", "کمتر از ۳۰ میلیون تومان"),
            Option("30_60M", "۳۰ تا ۶۰ میلیون تومان"),
            Option("60_100M", "۶۰ تا ۱۰۰ میلیون تومان"),
            Option("OVER_100M", "بیش از ۱۰۰ میلیون تومان"),
            Option("PREFER_NOT", "تمایلی به پاسخ ندارم")
        );
    }

    private void AddQuestion(string key, string persianText, params OptionDefinition[] options)
    {
        questions.Add(new QuestionDefinition
        {
            key = key,
            persianText = persianText,
            options = new List<OptionDefinition>(options)
        });
    }

    private static OptionDefinition Option(string code, string label)
    {
        return new OptionDefinition { code = code, label = label };
    }

    private void BuildRuntimeUI()
    {
        if (questionnaireCanvasRoot != null)
        {
            questionnaireCanvasRoot.SetActive(true);
            return;
        }

        questionnaireCanvasRoot = new GameObject("SocioEconomicQuestionnaireCanvas");
        questionnaireCanvasRoot.transform.SetParent(transform, false);

        Canvas canvas = questionnaireCanvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = questionnaireCanvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        questionnaireCanvasRoot.AddComponent<GraphicRaycaster>();

        Image background = questionnaireCanvasRoot.AddComponent<Image>();
        background.color = new Color(0.06f, 0.08f, 0.12f, 0.97f);
        StretchFull(background.rectTransform);

        questionnairePanel = CreatePanel("QuestionnairePanel", questionnaireCanvasRoot.transform,
            new Color(1f, 1f, 1f, 0.98f));
        RectTransform panelRect = questionnairePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.15f, 0.06f);
        panelRect.anchorMax = new Vector2(0.85f, 0.94f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        titleLabel = CreateRtlText("Title", questionnairePanel.transform,
            "پرسشنامه مشخصات فردی و عادات سفر", titleFontSize, FontStyles.Bold,
            new Color(0.08f, 0.12f, 0.18f, 1f));
        RectTransform titleRect = titleLabel.rectTransform;
        titleRect.anchorMin = new Vector2(0.05f, 0.88f);
        titleRect.anchorMax = new Vector2(0.95f, 0.98f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        progressLabel = CreateRtlText("Progress", questionnairePanel.transform,
            string.Empty, navigationFontSize, FontStyles.Normal,
            new Color(0.25f, 0.30f, 0.38f, 1f));
        RectTransform progressRect = progressLabel.rectTransform;
        progressRect.anchorMin = new Vector2(0.05f, 0.82f);
        progressRect.anchorMax = new Vector2(0.95f, 0.88f);
        progressRect.offsetMin = Vector2.zero;
        progressRect.offsetMax = Vector2.zero;

        questionLabel = CreateRtlText("Question", questionnairePanel.transform,
            string.Empty, questionFontSize, FontStyles.Bold,
            new Color(0.07f, 0.10f, 0.16f, 1f));
        RectTransform questionRect = questionLabel.rectTransform;
        questionRect.anchorMin = new Vector2(0.06f, 0.68f);
        questionRect.anchorMax = new Vector2(0.94f, 0.82f);
        questionRect.offsetMin = Vector2.zero;
        questionRect.offsetMax = Vector2.zero;

        GameObject scrollObject = new GameObject("OptionsScroll");
        scrollObject.transform.SetParent(questionnairePanel.transform, false);
        RectTransform scrollRectTransform = scrollObject.AddComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0.06f, 0.18f);
        scrollRectTransform.anchorMax = new Vector2(0.94f, 0.68f);
        scrollRectTransform.offsetMin = Vector2.zero;
        scrollRectTransform.offsetMax = Vector2.zero;

        Image scrollBackground = scrollObject.AddComponent<Image>();
        scrollBackground.color = new Color(0.94f, 0.95f, 0.97f, 1f);

        ScrollRect scrollRect = scrollObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 25f;

        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollObject.transform, false);
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        StretchFull(viewportRect);
        viewport.AddComponent<RectMask2D>();
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);

        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        optionContent = content.AddComponent<RectTransform>();
        optionContent.anchorMin = new Vector2(0f, 1f);
        optionContent.anchorMax = new Vector2(1f, 1f);
        optionContent.pivot = new Vector2(0.5f, 1f);
        optionContent.anchoredPosition = Vector2.zero;
        optionContent.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup vertical = content.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(18, 18, 18, 18);
        vertical.spacing = 12f;
        vertical.childAlignment = TextAnchor.UpperCenter;
        vertical.childControlHeight = true;
        vertical.childControlWidth = true;
        vertical.childForceExpandHeight = false;
        vertical.childForceExpandWidth = true;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scrollRect.viewport = viewportRect;
        scrollRect.content = optionContent;

        errorLabel = CreateRtlText("Error", questionnairePanel.transform,
            string.Empty, navigationFontSize, FontStyles.Bold,
            new Color(0.75f, 0.12f, 0.12f, 1f));
        RectTransform errorRect = errorLabel.rectTransform;
        errorRect.anchorMin = new Vector2(0.06f, 0.12f);
        errorRect.anchorMax = new Vector2(0.94f, 0.18f);
        errorRect.offsetMin = Vector2.zero;
        errorRect.offsetMax = Vector2.zero;

        previousButton = CreateButton("PreviousButton", questionnairePanel.transform,
            "قبلی", new Color(0.68f, 0.72f, 0.78f, 1f));
        RectTransform previousRect = previousButton.GetComponent<RectTransform>();
        previousRect.anchorMin = new Vector2(0.06f, 0.035f);
        previousRect.anchorMax = new Vector2(0.34f, 0.11f);
        previousRect.offsetMin = Vector2.zero;
        previousRect.offsetMax = Vector2.zero;
        previousButton.onClick.AddListener(GoPrevious);

        nextButton = CreateButton("NextButton", questionnairePanel.transform,
            "بعدی", new Color(0.12f, 0.45f, 0.75f, 1f));
        RectTransform nextRect = nextButton.GetComponent<RectTransform>();
        nextRect.anchorMin = new Vector2(0.66f, 0.035f);
        nextRect.anchorMax = new Vector2(0.94f, 0.11f);
        nextRect.offsetMin = Vector2.zero;
        nextRect.offsetMax = Vector2.zero;
        nextButton.onClick.AddListener(GoNextOrSubmit);
        nextButtonLabel = nextButton.GetComponentInChildren<RTLTextMeshPro>(true);

        thankYouPanel = CreatePanel("ThankYouPanel", questionnaireCanvasRoot.transform,
            new Color(1f, 1f, 1f, 0.98f));
        RectTransform thankRect = thankYouPanel.GetComponent<RectTransform>();
        thankRect.anchorMin = new Vector2(0.22f, 0.25f);
        thankRect.anchorMax = new Vector2(0.78f, 0.75f);
        thankRect.offsetMin = Vector2.zero;
        thankRect.offsetMax = Vector2.zero;

        RTLTextMeshPro thankTitle = CreateRtlText("ThankTitle", thankYouPanel.transform,
            "پایان نظرسنجی", 40f, FontStyles.Bold,
            new Color(0.08f, 0.18f, 0.28f, 1f));
        RectTransform thankTitleRect = thankTitle.rectTransform;
        thankTitleRect.anchorMin = new Vector2(0.08f, 0.64f);
        thankTitleRect.anchorMax = new Vector2(0.92f, 0.90f);
        thankTitleRect.offsetMin = Vector2.zero;
        thankTitleRect.offsetMax = Vector2.zero;

        RTLTextMeshPro thankBody = CreateRtlText("ThankBody", thankYouPanel.transform,
            "پاسخ‌های شما با موفقیت ثبت شد.\nاز مشارکت شما سپاسگزاریم.",
            28f, FontStyles.Normal, new Color(0.12f, 0.16f, 0.22f, 1f));
        RectTransform thankBodyRect = thankBody.rectTransform;
        thankBodyRect.anchorMin = new Vector2(0.08f, 0.28f);
        thankBodyRect.anchorMax = new Vector2(0.92f, 0.64f);
        thankBodyRect.offsetMin = Vector2.zero;
        thankBodyRect.offsetMax = Vector2.zero;

        thankYouPanel.SetActive(false);
    }

    private void ShowQuestion(int index)
    {
        currentQuestionIndex = Mathf.Clamp(index, 0, questions.Count - 1);
        QuestionDefinition question = questions[currentQuestionIndex];

        progressLabel.text =
            "پرسش " + ToPersianDigits((currentQuestionIndex + 1).ToString(Invariant)) +
            " از " + ToPersianDigits(questions.Count.ToString(Invariant));

        questionLabel.text = question.persianText;
        errorLabel.text = string.Empty;

        ClearOptionButtons();

        answers.TryGetValue(question.key, out AnswerRecord selectedAnswer);

        if (IsAgeQuestion(question))
        {
            CreateAgeInput(selectedAnswer);
        }
        else
        {
            for (int i = 0; i < question.options.Count; i++)
            {
                OptionDefinition option = question.options[i];
                bool selected = selectedAnswer != null && selectedAnswer.code == option.code;
                Button button = CreateOptionButton(question, option, selected);
                optionButtons.Add(button);
            }
        }

        previousButton.interactable = currentQuestionIndex > 0;

        if (nextButtonLabel != null)
            nextButtonLabel.text = currentQuestionIndex == questions.Count - 1 ? "ثبت نهایی" : "بعدی";

        Canvas.ForceUpdateCanvases();
        optionContent.anchoredPosition = Vector2.zero;
    }

    private bool IsAgeQuestion(QuestionDefinition question)
    {
        return question != null &&
               string.Equals(question.key, "Age", StringComparison.Ordinal);
    }

    private void CreateAgeInput(AnswerRecord savedAnswer)
    {
        GameObject row = new GameObject("AgeInputRow");
        row.transform.SetParent(optionContent, false);

        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0f, 90f);

        LayoutElement rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = 90f;
        rowLayout.preferredHeight = 90f;

        HorizontalLayoutGroup horizontal = row.AddComponent<HorizontalLayoutGroup>();
        horizontal.padding = new RectOffset(90, 90, 12, 12);
        horizontal.spacing = 14f;
        horizontal.childAlignment = TextAnchor.MiddleCenter;
        horizontal.childControlHeight = true;
        horizontal.childControlWidth = false;
        horizontal.childForceExpandHeight = false;
        horizontal.childForceExpandWidth = false;

        // HorizontalLayoutGroup lays children out from LEFT to RIGHT.
        // For the Persian visual order "سن: [input] سال", create "سال" first
        // so it appears on the left, and create "سن:" last so it appears on the right.
        RTLTextMeshPro yearsLabel = CreateRtlText(
            "YearsLabel",
            row.transform,
            "سال",
            optionFontSize,
            FontStyles.Normal,
            new Color(0.08f, 0.11f, 0.16f, 1f)
        );

        LayoutElement yearsLayout = yearsLabel.gameObject.AddComponent<LayoutElement>();
        yearsLayout.minWidth = 90f;
        yearsLayout.preferredWidth = 90f;
        yearsLayout.minHeight = 60f;
        yearsLayout.preferredHeight = 60f;

        GameObject inputObject = new GameObject("AgeInputField");
        inputObject.transform.SetParent(row.transform, false);

        RectTransform inputRect = inputObject.AddComponent<RectTransform>();
        inputRect.sizeDelta = new Vector2(220f, 64f);

        LayoutElement inputLayout = inputObject.AddComponent<LayoutElement>();
        inputLayout.minWidth = 220f;
        inputLayout.preferredWidth = 220f;
        inputLayout.minHeight = 64f;
        inputLayout.preferredHeight = 64f;

        Image inputBackground = inputObject.AddComponent<Image>();
        inputBackground.color = Color.white;

        ageInputField = inputObject.AddComponent<TMP_InputField>();
        ageInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
        ageInputField.characterValidation = TMP_InputField.CharacterValidation.Integer;
        ageInputField.characterLimit = 2;
        ageInputField.lineType = TMP_InputField.LineType.SingleLine;

        GameObject textAreaObject = new GameObject("Text Area");
        textAreaObject.transform.SetParent(inputObject.transform, false);

        RectTransform textAreaRect = textAreaObject.AddComponent<RectTransform>();
        StretchFull(textAreaRect, 12f, 12f, 6f, 6f);
        textAreaObject.AddComponent<RectMask2D>();

        // RTL placeholder so Persian letters are connected and displayed correctly.
        RTLTextMeshPro placeholder = CreateRtlText(
            "Placeholder",
            textAreaObject.transform,
            "مثلاً ۲۴",
            optionFontSize,
            FontStyles.Normal,
            new Color(0.45f, 0.49f, 0.55f, 0.75f)
        );
        StretchFull(placeholder.rectTransform);
        placeholder.alignment = TextAlignmentOptions.Center;
        placeholder.textWrappingMode = TextWrappingModes.NoWrap;

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(textAreaObject.transform, false);

        RectTransform textRect = textObject.AddComponent<RectTransform>();
        StretchFull(textRect);

        TMP_Text inputText = textObject.AddComponent<TextMeshProUGUI>();
        inputText.text = string.Empty;
        inputText.fontSize = optionFontSize;
        inputText.fontStyle = FontStyles.Bold;
        inputText.color = new Color(0.08f, 0.11f, 0.16f, 1f);
        inputText.alignment = TextAlignmentOptions.Center;
        inputText.textWrappingMode = TextWrappingModes.NoWrap;

        if (persianFontAsset != null)
            inputText.font = persianFontAsset;

        ageInputField.textViewport = textAreaRect;
        ageInputField.textComponent = inputText;
        ageInputField.placeholder = placeholder;

        if (savedAnswer != null && !string.IsNullOrWhiteSpace(savedAnswer.code))
            ageInputField.text = savedAnswer.code;

        ageInputField.onValueChanged.AddListener(_ =>
        {
            errorLabel.text = string.Empty;
        });

        ageInputField.onEndEdit.AddListener(_ =>
        {
            if (TryCommitAgeInput(false))
                SaveQuestionnaireCsv("Incomplete");
        });

        RTLTextMeshPro ageLabel = CreateRtlText(
            "AgeLabel",
            row.transform,
            "سن:",
            optionFontSize,
            FontStyles.Bold,
            new Color(0.08f, 0.11f, 0.16f, 1f)
        );

        LayoutElement labelLayout = ageLabel.gameObject.AddComponent<LayoutElement>();
        labelLayout.minWidth = 90f;
        labelLayout.preferredWidth = 90f;
        labelLayout.minHeight = 60f;
        labelLayout.preferredHeight = 60f;

        ageInputField.ActivateInputField();
    }

    private bool TryCommitAgeInput(bool showError)
    {
        if (ageInputField == null)
        {
            if (showError)
                errorLabel.text = "لطفاً سن خود را وارد کنید.";
            return false;
        }

        string raw = ConvertPersianDigitsToEnglish(ageInputField.text).Trim();

        if (!int.TryParse(raw, NumberStyles.Integer, Invariant, out int age))
        {
            if (showError)
                errorLabel.text = "لطفاً سن را به صورت عدد وارد کنید.";
            return false;
        }

        int minAge = Mathf.Min(minimumAge, maximumAge);
        int maxAge = Mathf.Max(minimumAge, maximumAge);

        if (age < minAge || age > maxAge)
        {
            if (showError)
            {
                errorLabel.text =
                    "سن باید بین " +
                    ToPersianDigits(minAge.ToString(Invariant)) +
                    " تا " +
                    ToPersianDigits(maxAge.ToString(Invariant)) +
                    " سال باشد.";
            }

            return false;
        }

        string value = age.ToString(Invariant);

        answers["Age"] = new AnswerRecord
        {
            code = value,
            label = value
        };

        ageInputField.text = value;
        errorLabel.text = string.Empty;
        return true;
    }

    private Button CreateOptionButton(
        QuestionDefinition question,
        OptionDefinition option,
        bool selected)
    {
        GameObject buttonObject = new GameObject("Option_" + option.code);
        buttonObject.transform.SetParent(optionContent, false);

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, 58f);

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.minHeight = 58f;
        layout.preferredHeight = 62f;

        Image image = buttonObject.AddComponent<Image>();
        image.color = selected
            ? new Color(0.16f, 0.55f, 0.84f, 1f)
            : new Color(1f, 1f, 1f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = selected
            ? new Color(0.12f, 0.48f, 0.78f, 1f)
            : new Color(0.90f, 0.94f, 0.98f, 1f);
        colors.pressedColor = new Color(0.10f, 0.42f, 0.70f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        RTLTextMeshPro label = CreateRtlText("Label", buttonObject.transform,
            option.label, optionFontSize, FontStyles.Normal,
            selected ? Color.white : new Color(0.08f, 0.11f, 0.16f, 1f));
        StretchFull(label.rectTransform, 18f, 18f, 6f, 6f);

        button.onClick.AddListener(() => SelectAnswer(question, option));
        return button;
    }

    private void SelectAnswer(QuestionDefinition question, OptionDefinition option)
    {
        answers[question.key] = new AnswerRecord
        {
            code = option.code,
            label = option.label
        };

        SaveQuestionnaireCsv("Incomplete");
        ShowQuestion(currentQuestionIndex);
    }

    private void GoPrevious()
    {
        if (currentQuestionIndex > 0)
            ShowQuestion(currentQuestionIndex - 1);
    }

    private void GoNextOrSubmit()
    {
        QuestionDefinition current = questions[currentQuestionIndex];

        if (IsAgeQuestion(current))
        {
            if (!TryCommitAgeInput(true))
                return;

            SaveQuestionnaireCsv("Incomplete");
        }
        else if (!answers.ContainsKey(current.key))
        {
            errorLabel.text = "لطفاً یکی از گزینه‌ها را انتخاب کنید.";
            return;
        }

        if (currentQuestionIndex < questions.Count - 1)
        {
            ShowQuestion(currentQuestionIndex + 1);
            return;
        }

        SubmitQuestionnaire();
    }

    private void SubmitQuestionnaire()
    {
        foreach (QuestionDefinition question in questions)
        {
            if (!answers.ContainsKey(question.key))
            {
                int missingIndex = questions.IndexOf(question);
                ShowQuestion(missingIndex);
                errorLabel.text = "برای ثبت نهایی، به همه پرسش‌ها پاسخ دهید.";
                return;
            }
        }

        questionnaireCompleted = true;
        questionnaireOpened = false;

        SaveQuestionnaireCsv("Completed");

        if (questionnairePanel != null)
            questionnairePanel.SetActive(false);

        // Do NOT show the old socioeconomic thank-you screen.
        // FinalUploadWaitingController now handles:
        // uploading screen -> final thank-you screen.
        if (thankYouPanel != null)
            thankYouPanel.SetActive(false);

        onQuestionnaireCompleted?.Invoke();

        Debug.Log(
            "[SocioEconomicQuestionnaire] Completed for participant=" + participantId +
            " | session=" + sessionId +
            "\nFile: " + outputFilePath
        );
    }

    private void SaveQuestionnaireCsv(string completionStatus)
    {
        ResolveIdentity();
        ResolveVersions();

        string folder = ResolveOutputDirectory();
        Directory.CreateDirectory(folder);

        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            string safeParticipant = MakeSafeFileName(participantId);
            string safeSession = MakeSafeFileName(sessionId);
            outputFilePath = Path.Combine(
                folder,
                "parking_questionnaire_" + safeParticipant + "_" + safeSession + ".csv"
            );
        }

        DateTime nowUtc = DateTime.UtcNow;

        List<string> header = new List<string>
        {
            "ParticipantId",
            "SessionId",
            "StudyVersion",
            "BuildVersion",
            "QuestionnaireStartUtc",
            "QuestionnaireEndUtc",
            "CompletionStatus",
            "AnsweredCount",
            "ExpectedQuestionCount",
            "FinalGameScore",
            "UnityVersion",
            "Platform"
        };

        List<string> row = new List<string>
        {
            participantId,
            sessionId,
            studyVersion,
            string.IsNullOrWhiteSpace(buildVersion) ? Application.version : buildVersion,
            questionnaireStartUtc == default
                ? nowUtc.ToString("o", Invariant)
                : questionnaireStartUtc.ToString("o", Invariant),
            completionStatus == "Completed" ? nowUtc.ToString("o", Invariant) : string.Empty,
            completionStatus,
            answers.Count.ToString(Invariant),
            questions.Count.ToString(Invariant),
            finalGameScore.ToString("0.###", Invariant),
            Application.unityVersion,
            Application.platform.ToString()
        };

        foreach (QuestionDefinition question in questions)
        {
            if (IsAgeQuestion(question))
            {
                header.Add("Age");

                if (answers.TryGetValue(question.key, out AnswerRecord ageAnswer))
                    row.Add(ageAnswer.code);
                else
                    row.Add(string.Empty);

                continue;
            }

            header.Add(question.key + "Code");
            header.Add(question.key + "Label");

            if (answers.TryGetValue(question.key, out AnswerRecord answer))
            {
                row.Add(answer.code);
                row.Add(answer.label);
            }
            else
            {
                row.Add(string.Empty);
                row.Add(string.Empty);
            }
        }

        StringBuilder csv = new StringBuilder();
        csv.AppendLine(ToCsvLine(header));
        csv.AppendLine(ToCsvLine(row));

        File.WriteAllText(outputFilePath, csv.ToString(), new UTF8Encoding(true));

        if (printOutputPath)
            Debug.Log("[SocioEconomicQuestionnaire] Saved: " + outputFilePath);
    }

    private void ResolveIdentity()
    {
        if (string.IsNullOrWhiteSpace(participantId))
            participantId = ResolveParticipantId();

        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = ResolveSessionId();

        if (string.IsNullOrWhiteSpace(participantId))
            participantId = string.IsNullOrWhiteSpace(fallbackParticipantId)
                ? "UNKNOWN"
                : fallbackParticipantId.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", Invariant);
    }

    private string ResolveParticipantId()
    {
        Type participantSessionType = FindTypeByName("ParticipantSession");

        if (participantSessionType != null)
        {
            PropertyInfo instanceProperty = participantSessionType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static
            );

            object instance = instanceProperty?.GetValue(null);

            if (instance != null)
            {
                FieldInfo idField = participantSessionType.GetField(
                    "participantId",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );

                PropertyInfo idProperty = participantSessionType.GetProperty(
                    "participantId",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );

                string value = idField?.GetValue(instance) as string;

                if (string.IsNullOrWhiteSpace(value))
                    value = idProperty?.GetValue(instance) as string;

                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim().ToUpperInvariant();
            }
        }

        if (scenarioManager != null)
        {
            FieldInfo idField = scenarioManager.GetType().GetField(
                "participantId",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            string value = idField?.GetValue(scenarioManager) as string;

            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim().ToUpperInvariant();
        }

        return fallbackParticipantId;
    }

    private string ResolveSessionId()
    {
        Type loggerType = FindTypeByName("ParkingLogger");

        if (loggerType != null)
        {
            PropertyInfo instanceProperty = loggerType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static
            );

            object loggerInstance = instanceProperty?.GetValue(null);

            if (loggerInstance != null)
            {
                PropertyInfo sessionProperty = loggerType.GetProperty(
                    "SessionId",
                    BindingFlags.Public | BindingFlags.Instance
                );

                string value = sessionProperty?.GetValue(loggerInstance) as string;

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        if (ParticipantSession.Instance != null)
            return ParticipantSession.Instance.GetOrCreateSessionId();

        return DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", Invariant);
    }

    private void ResolveVersions()
    {
        if (scenarioManager == null)
        {
            scenarioManager = FindScenarioManager();
            CacheScenarioManagerFields();
        }

        if (scenarioManager == null)
        {
            if (string.IsNullOrWhiteSpace(buildVersion))
                buildVersion = Application.version;

            return;
        }

        Type type = scenarioManager.GetType();

        FieldInfo studyField = type.GetField(
            "studyVersion",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );

        FieldInfo buildField = type.GetField(
            "buildVersion",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );

        string resolvedStudy = studyField?.GetValue(scenarioManager) as string;
        string resolvedBuild = buildField?.GetValue(scenarioManager) as string;

        if (!string.IsNullOrWhiteSpace(resolvedStudy))
            studyVersion = resolvedStudy.Trim();

        if (!string.IsNullOrWhiteSpace(resolvedBuild))
            buildVersion = resolvedBuild.Trim();
        else if (string.IsNullOrWhiteSpace(buildVersion))
            buildVersion = Application.version;
    }

    private string ResolveOutputDirectory()
    {
        Type loggerType = FindTypeByName("ParkingLogger");

        if (loggerType != null)
        {
            PropertyInfo instanceProperty = loggerType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static
            );

            object loggerInstance = instanceProperty?.GetValue(null);

            if (loggerInstance != null)
            {
                foreach (string propertyName in new[] { "SessionFilePath", "RoundFilePath", "AlternativeFilePath" })
                {
                    PropertyInfo pathProperty = loggerType.GetProperty(
                        propertyName,
                        BindingFlags.Public | BindingFlags.Instance
                    );

                    string path = pathProperty?.GetValue(loggerInstance) as string;

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        string directory = Path.GetDirectoryName(path);

                        if (!string.IsNullOrWhiteSpace(directory))
                            return directory;
                    }
                }
            }
        }

        return Path.Combine(Application.persistentDataPath, outputSubfolder);
    }

    private MonoBehaviour FindScenarioManager()
    {
        Type type = FindTypeByName("ScenarioManager");

        if (type == null)
            return null;

        UnityEngine.Object found = FindFirstObjectByType(type);
        return found as MonoBehaviour;
    }

    private void CacheScenarioManagerFields()
    {
        surveyCompletedField = null;
        cumulativeScoreField = null;
        currentScoreField = null;

        if (scenarioManager == null)
            return;

        Type type = scenarioManager.GetType();

        surveyCompletedField = type.GetField(
            "surveyCompleted",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );

        cumulativeScoreField = type.GetField(
            "cumulativeScore",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Fallback only for compatibility with older ScenarioManager versions.
        currentScoreField = type.GetField(
            "currentScore",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
    }

    private bool ReadSurveyCompleted()
    {
        if (scenarioManager == null)
            return false;

        if (surveyCompletedField == null)
            CacheScenarioManagerFields();

        if (surveyCompletedField != null && surveyCompletedField.FieldType == typeof(bool))
            return (bool)surveyCompletedField.GetValue(scenarioManager);

        return false;
    }

    private float ReadFinalScore()
    {
        if (scenarioManager == null)
            return 0f;

        if (cumulativeScoreField == null && currentScoreField == null)
            CacheScenarioManagerFields();

        // Preferred value: total score accumulated across all completed rounds.
        if (cumulativeScoreField != null)
        {
            object cumulativeValue =
                cumulativeScoreField.GetValue(scenarioManager);

            if (cumulativeValue is float cumulativeFloat)
                return cumulativeFloat;

            if (cumulativeValue is int cumulativeInt)
                return cumulativeInt;
        }

        // Compatibility fallback for older ScenarioManager versions that did
        // not expose cumulativeScore.
        if (currentScoreField != null)
        {
            object currentValue =
                currentScoreField.GetValue(scenarioManager);

            if (currentValue is float currentFloat)
                return currentFloat;

            if (currentValue is int currentInt)
                return currentInt;
        }

        return 0f;
    }

    private static Type FindTypeByName(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(typeName);

            if (type != null)
                return type;

            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
            }

            if (types == null)
                continue;

            foreach (Type candidate in types)
            {
                if (candidate != null && candidate.Name == typeName)
                    return candidate;
            }
        }

        return null;
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        RectTransform rect = panel.AddComponent<RectTransform>();
        Image image = panel.AddComponent<Image>();
        image.color = color;
        return panel;
    }

    private RTLTextMeshPro CreateRtlText(
        string name,
        Transform parent,
        string text,
        float fontSize,
        FontStyles fontStyle,
        Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        RTLTextMeshPro label = textObject.AddComponent<RTLTextMeshPro>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;

        if (persianFontAsset != null)
            label.font = persianFontAsset;

        return label;
    }

    private Button CreateButton(string name, Transform parent, string text, Color color)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        Image image = buttonObject.AddComponent<Image>();
        image.color = color;
        Button button = buttonObject.AddComponent<Button>();

        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = new Color(
            Mathf.Clamp01(color.r + 0.08f),
            Mathf.Clamp01(color.g + 0.08f),
            Mathf.Clamp01(color.b + 0.08f),
            color.a
        );
        colors.pressedColor = new Color(
            Mathf.Clamp01(color.r - 0.08f),
            Mathf.Clamp01(color.g - 0.08f),
            Mathf.Clamp01(color.b - 0.08f),
            color.a
        );
        button.colors = colors;

        RTLTextMeshPro label = CreateRtlText(
            "Label",
            buttonObject.transform,
            text,
            navigationFontSize,
            FontStyles.Bold,
            Color.white
        );
        StretchFull(label.rectTransform, 10f, 10f, 4f, 4f);

        return button;
    }

    private void ClearOptionButtons()
    {
        optionButtons.Clear();
        ageInputField = null;

        if (optionContent == null)
            return;

        for (int i = optionContent.childCount - 1; i >= 0; i--)
            Destroy(optionContent.GetChild(i).gameObject);
    }

    private static void StretchFull(RectTransform rect)
    {
        StretchFull(rect, 0f, 0f, 0f, 0f);
    }

    private static void StretchFull(
        RectTransform rect,
        float left,
        float right,
        float bottom,
        float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static GameObject FindObjectByNames(params string[] names)
    {
        foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (obj == null || !obj.scene.IsValid())
                continue;

            foreach (string name in names)
            {
                if (string.Equals(obj.name, name, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }
        }

        return null;
    }

    private static string ToCsvLine(List<string> values)
    {
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                builder.Append(',');

            builder.Append(EscapeCsv(values[i]));
        }

        return builder.ToString();
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

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "UNKNOWN";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value.Trim();
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

    private static string ToPersianDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        char[] western = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        char[] persian = { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };

        for (int i = 0; i < western.Length; i++)
            value = value.Replace(western[i], persian[i]);

        return value;
    }

    [Serializable]
    private class QuestionDefinition
    {
        public string key;
        public string persianText;
        public List<OptionDefinition> options;
    }

    [Serializable]
    private class OptionDefinition
    {
        public string code;
        public string label;
    }

    [Serializable]
    private class AnswerRecord
    {
        public string code;
        public string label;
    }
}
