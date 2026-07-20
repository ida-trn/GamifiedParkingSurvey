using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ScenarioManager : MonoBehaviour
{
    [Header("Logging")]
    public string participantId = "P001";

    [Tooltip("Freeze this during data collection, for example 1.1-pilot-score.")]
    public string studyVersion = "1.1-pilot-score";

    [Tooltip("Change this whenever gameplay, scoring, scenarios, logging, or UI changes.")]
    public string buildVersion = "1.1.0";

    [Tooltip("Keep Fixed01 until scenario-order randomisation is implemented.")]
    public string scenarioOrderLabel = "Fixed01";

    [Header("Scenarios")]
    public List<ParkingScenario> scenarios = new List<ParkingScenario>();

    [Tooltip("If ON: each scenario runs once in conventional mode and once in assisted mode.")]
    public bool runConventionalAndAssistedForEachScenario = true;

    [Tooltip("If ON: each scenario runs Conventional first, then Assisted.")]
    public bool conventionalFirst = true;

    [Tooltip("0 = no testing limit. Use 2 to test only S01 in both modes.")]
    public int maxRoundsForTesting = 0;

    [Header("Current State")]
    public int currentScenarioIndex = 0;
    public int currentRoundIndex = 0;
    public bool currentAssistedMode = false;

    [Header("References")]
    public ParkingOccupancyManager occupancyManager;
    public CarParkingInteraction carParking;

    [Tooltip("Assign the Car object. If empty, carParking.transform is used.")]
    public Transform carTransform;

    [Header("Destination")]
    public Transform destinationGate;

    [Tooltip("Your measured calibration was 80 metres / 330 Unity units = 0.2424.")]
    public float metersPerUnityUnit = 0.2424f;

    [Tooltip("Normal adult walking speed is approximately 1.4 m/s.")]
    public float walkingSpeedMetersPerSecond = 1.4f;

    [Header("Car start by RoadNode")]
    public RoadNode[] scenarioStartNodes;
    public bool snapCarToStartNode = true;

    [Header("UI - Scenario info")]
    public TMP_Text scenarioLabel;
    public TMP_Text roundLabel;
    public TMP_Text timerLabel;
    public TMP_Text modeLabel;
    public TMP_Text dwellTimeLabel;
    public TMP_Text timeLimitLabel;
    public TMP_Text legalFeeLabel;
    public TMP_Text illegalFineLabel;
    public TMP_Text citationProbLabel;

    [Tooltip("This existing label is reused to show the on-time base points.")]
    public TMP_Text onTimeRewardLabel;

    [Header("UI - Before each scenario")]
    [Tooltip("Full-screen board shown before each round begins.")]
    public GameObject scenarioIntroPanel;
    public TMP_Text introTitleLabel;
    public TMP_Text introModeLabel;
    public TMP_Text introDetailsLabel;
    public TMP_Text introInstructionLabel;

    [Tooltip("Assign the parent GameObject of the university destination arrow.")]
    public GameObject destinationArrowRoot;

    [Header("Assisted Mode UI")]
    public GameObject assistedModeUIRoot;

    [Header("Mode-Specific UI")]
    public GameObject conventionalDrivingButtonsRoot;

    [Header("Round result UI")]
    public GameObject resultPanel;
    public TMP_Text resultTitleLabel;
    public TMP_Text resultDetailLabel;
    public TMP_Text resultScoreLabel;

    [Tooltip("Optional. Assign ResultContinueButton so it can be hidden on the final survey screen.")]
    public GameObject resultContinueButton;

    [Tooltip("Optional. Assign the RTL text child of ResultContinueButton.")]
    public TMP_Text resultContinueButtonLabel;

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

    [Header("New scoring system")]
    [Tooltip("The survey starts from zero points.")]
    public float startingScore = 0f;

    [Tooltip("Base score when total time, including walking, is within the limit.")]
    public int onTimeBasePoints = 100;

    [Tooltip("Base score when parking is completed but estimated arrival is late.")]
    public int lateBasePoints = 50;

    [Tooltip("Money-to-point conversion. 1000 means every 1,000 tomans costs one point.")]
    [Min(1f)] public float tomanPerPoint = 1000f;

    [Header("Star targets as proportions of maximum possible score")]
    [Range(0f, 1f)] public float star1TargetRatio = 0.50f;
    [Range(0f, 1f)] public float star2TargetRatio = 0.70f;
    [Range(0f, 1f)] public float star3TargetRatio = 0.85f;

    [Header("Score outputs")]
    [HideInInspector] public float currentScore;
    [HideInInspector] public float conventionalScore;
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
    private float scoreAtRoundStart;
    private float timeScaleBeforeIntro = 1f;
    private CanvasGroup destinationArrowCanvasGroup;

    private void Awake()
    {
        if (autoFindUIReferences)
            AutoBindUIReferences();
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

        currentScore = startingScore;
        conventionalScore = 0f;
        assistedScore = 0f;
        surveyCompleted = false;
        scenarioRunning = false;
        waitingForContinue = false;
        waitingForScenarioStart = false;

        ConfigureScoreScale();

        if (resultPanel != null)
            resultPanel.SetActive(false);

        if (scenarioIntroPanel != null)
            scenarioIntroPanel.SetActive(false);

        UpdateScoreUI();

        if (scenarios != null && scenarios.Count > 0)
            StartRound(0);
        else
            Debug.LogError("[ScenarioManager] No ParkingScenario assets are assigned.");
    }

    public int GetTotalRoundCount()
    {
        int scenarioCount = scenarios != null ? scenarios.Count : 0;

        int total = runConventionalAndAssistedForEachScenario
            ? scenarioCount * 2
            : scenarioCount;

        if (maxRoundsForTesting > 0)
            total = Mathf.Min(total, maxRoundsForTesting);

        return total;
    }

    private float GetMaximumPossibleScore()
    {
        return Mathf.Max(0, GetTotalRoundCount() * Mathf.Max(0, onTimeBasePoints));
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
            scoreBar.maxValue = Mathf.Max(1f, maximum);
            scoreBar.value = currentScore;
        }
    }

    public string GetCurrentModeName()
    {
        return currentAssistedMode ? "Assisted" : "Conventional";
    }

    private string GetCurrentModePersianName()
    {
        return currentAssistedMode ? "پارک هوشمند" : "پارک متداول";
    }

    public void StartRound(int roundIndex)
    {
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

        if (runConventionalAndAssistedForEachScenario)
        {
            currentScenarioIndex = roundIndex / 2;
            bool secondModeOfPair = roundIndex % 2 == 1;
            currentAssistedMode = conventionalFirst
                ? secondModeOfPair
                : !secondModeOfPair;
        }
        else
        {
            currentScenarioIndex = roundIndex;
            currentAssistedMode = scenarios[currentScenarioIndex].assistedMode;
        }

        if (currentScenarioIndex < 0 || currentScenarioIndex >= scenarios.Count)
        {
            Debug.LogError("[ScenarioManager] Scenario index is out of range.");
            return;
        }

        currentScenario = scenarios[currentScenarioIndex];

        if (currentScenario == null)
        {
            Debug.LogError("[ScenarioManager] The current ParkingScenario asset is missing.");
            return;
        }

        currentScenario.assistedMode = currentAssistedMode;

        // Keeps the existing research logger field meaningful.
        currentScenario.onTimeReward = onTimeBasePoints;

        ApplyCurrentMode();
        ResetAllParkingSpots();

        if (occupancyManager != null)
        {
            occupancyManager.legalOccupancyRate = currentScenario.legalOccupancyRate;
            occupancyManager.illegalOccupancyRate = currentScenario.illegalOccupancyRate;
            occupancyManager.ApplyOccupancy();
        }

        if (resultPanel != null)
            resultPanel.SetActive(false);

        ResetCarToStart();

        if (carParking != null)
            carParking.ResetForNewScenario();

        scoreAtRoundStart = currentScore;
        scenarioRunning = false;
        UpdateScenarioInfoUI();
        UpdateScoreUI();

        ShowScenarioIntroPanel();

        Debug.Log(
            $"[ScenarioManager] Starting round {currentRoundIndex + 1}/{totalRounds} | " +
            $"scenario={currentScenario.scenarioId} | mode={GetCurrentModeName()}"
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
        scenarioIntroPanel.SetActive(true);
        scenarioIntroPanel.transform.SetAsLastSibling();

        SetLabelText(
            introTitleLabel,
            "سناریو " +
            ToPersianDigits((currentScenarioIndex + 1).ToString(CultureInfo.InvariantCulture))
        );

        SetLabelText(introModeLabel, "حالت: " + GetCurrentModePersianName());

        string legalFeeText = currentScenario.legalFeePerHour <= 0f
            ? "رایگان"
            : FormatToman(currentScenario.legalFeePerHour) + " در ساعت";

        string details =
            "مدت پارک: " + FormatParkingDuration(currentScenario.dwellTimeMinutes) + "\n" +
            "مهلت یافتن محل پارک: " + FormatNumber(currentScenario.timeLimitMinutes) + " دقیقه\n" +
            "اشغال جایگاه‌های مجاز: " + FormatPercent(currentScenario.legalOccupancyRate) + "\n" +
            "اشغال محل‌های غیرمجاز: " + FormatPercent(currentScenario.illegalOccupancyRate) + "\n" +
            "هزینه پارک قانونی: " + legalFeeText + "\n" +
            "احتمال جریمه در ساعت: " + FormatPercent(currentScenario.citationProbability) + "\n" +
            "مبلغ جریمه پارک غیرمجاز: " + FormatToman(currentScenario.illegalFine) + "\n" +
            "امتیاز پایه رسیدن به‌موقع: " +
            ToPersianDigits(onTimeBasePoints.ToString(CultureInfo.InvariantCulture));

        SetLabelText(introDetailsLabel, details);

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
        SetDestinationArrowVisible(true);

        ApplyCurrentMode();

        scenarioStartTime = Time.time;
        scenarioRunning = true;

        // Begin logging here so time spent reading the board is not recorded
        // as driving time or assisted selection time.
        TryBeginLoggerRound();
    }

    private void SetDestinationArrowVisible(bool visible)
    {
        if (destinationArrowRoot == null)
            return;

        // Keep the arrow hierarchy active so its own controller can continue
        // updating, but hide the entire UI through a parent CanvasGroup.
        // This prevents another script from making the arrow visible again by
        // merely calling SetActive(true) on the arrow child.
        if (visible && !destinationArrowRoot.activeSelf)
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

        destinationArrowCanvasGroup.alpha = visible ? 1f : 0f;
        destinationArrowCanvasGroup.interactable = visible;
        destinationArrowCanvasGroup.blocksRaycasts = visible;
    }

    private void LateUpdate()
    {
        // Update still runs while Time.timeScale is zero. Reasserting this in
        // LateUpdate guarantees that an arrow controller cannot reveal it
        // during the scenario-information panel.
        if (waitingForScenarioStart)
            SetDestinationArrowVisible(false);
    }

    private void ApplyCurrentMode()
    {
        if (carParking != null)
            carParking.SetAssistedMode(currentAssistedMode);

        if (assistedMapCamera != null)
            assistedMapCamera.SetAssistedMode(currentAssistedMode);

        if (assistedModeUIRoot != null)
            assistedModeUIRoot.SetActive(currentAssistedMode);

        if (conventionalDrivingButtonsRoot != null)
            conventionalDrivingButtonsRoot.SetActive(!currentAssistedMode);
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

        float elapsed = Time.time - scenarioStartTime;
        float remaining = currentScenario.timeLimitMinutes * 60f - elapsed;

        if (remaining <= 0f)
        {
            SetLabelText(timerLabel, "۰۰:۰۰");

            OnScenarioTimeout();
            return;
        }

        if (timerLabel != null)
        {
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            SetLabelText(timerLabel, ToPersianDigits($"{minutes:00}:{seconds:00}"));
        }
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

    private void UpdateScenarioInfoUI()
    {
        if (currentScenario == null)
            return;

        SetLabelText(
            scenarioLabel,
            "سناریو " + ToPersianDigits((currentScenarioIndex + 1).ToString(CultureInfo.InvariantCulture))
        );

        SetLabelText(
            roundLabel,
            ToPersianDigits($"مرحله {currentRoundIndex + 1} از {GetTotalRoundCount()}")
        );

        SetLabelText(modeLabel, "حالت: " + GetCurrentModePersianName());

        SetLabelText(
            dwellTimeLabel,
            "مدت پارک: " + FormatNumber(currentScenario.dwellTimeMinutes) + " دقیقه"
        );

        SetLabelText(
            timeLimitLabel,
            "زمان مجاز رسیدن: " + FormatNumber(currentScenario.timeLimitMinutes) + " دقیقه"
        );

        SetLabelText(
            legalFeeLabel,
            "هزینه پارک قانونی: " + FormatToman(currentScenario.legalFeePerHour) + " در ساعت"
        );

        SetLabelText(
            illegalFineLabel,
            "جریمه پارک غیرمجاز: " + FormatToman(currentScenario.illegalFine)
        );

        SetLabelText(
            citationProbLabel,
            "احتمال جریمه در ساعت: " +
            ToPersianDigits(
                (currentScenario.citationProbability * 100f)
                    .ToString("0", CultureInfo.InvariantCulture)
            ) + " درصد"
        );

        SetLabelText(
            onTimeRewardLabel,
            "امتیاز پایه رسیدن به‌موقع: " +
            ToPersianDigits(onTimeBasePoints.ToString(CultureInfo.InvariantCulture))
        );
    }

    private void UpdateScoreUI()
    {
        SetLabelText(
            scoreLabel,
            "امتیاز کل: " +
            ToPersianDigits(Mathf.RoundToInt(currentScore).ToString(CultureInfo.InvariantCulture))
        );

        if (scoreBar != null)
            scoreBar.value = currentScore;

        bool has1 = currentScore >= star1Threshold;
        bool has2 = currentScore >= star2Threshold;
        bool has3 = currentScore >= star3Threshold;

        if (star1Image != null) star1Image.color = has1 ? starOnColor : starOffColor;
        if (star2Image != null) star2Image.color = has2 ? starOnColor : starOffColor;
        if (star3Image != null) star3Image.color = has3 ? starOnColor : starOffColor;
    }

    public float CalculateEstimatedWalkingDistance(ParkingSpot spot)
    {
        if (spot == null || destinationGate == null)
            return 0f;

        float unityDistance = Vector2.Distance(spot.transform.position, destinationGate.position);
        return unityDistance * Mathf.Max(0f, metersPerUnityUnit);
    }

    public void OnCarParked(ParkingSpot spot, float movementOrSelectionTimeSeconds)
    {
        if (surveyCompleted || !scenarioRunning || currentScenario == null || spot == null)
            return;

        scenarioRunning = false;
        waitingForContinue = true;

        float scenarioElapsedSeconds = Mathf.Max(0f, Time.time - scenarioStartTime);
        float timeLimitSeconds = currentScenario.timeLimitMinutes * 60f;

        float walkingDistanceMeters = CalculateEstimatedWalkingDistance(spot);
        float walkingTimeSeconds = walkingSpeedMetersPerSecond > 0f
            ? walkingDistanceMeters / walkingSpeedMetersPerSecond
            : 0f;

        float totalEstimatedTaskTimeSeconds = scenarioElapsedSeconds + walkingTimeSeconds;
        bool onTimeGame = scenarioElapsedSeconds <= timeLimitSeconds;
        bool onTimeIncludingWalk = totalEstimatedTaskTimeSeconds <= timeLimitSeconds;

        spot.walkingDistanceMeters = walkingDistanceMeters;
        spot.walkingTimeSeconds = walkingTimeSeconds;

        ParkingScoreCalculator.Result scoreResult = ParkingScoreCalculator.Calculate(
            currentScenario,
            spot,
            onTimeIncludingWalk,
            participantId,
            currentRoundIndex,
            currentAssistedMode,
            onTimeBasePoints,
            lateBasePoints,
            tomanPerPoint
        );

        currentScore += scoreResult.roundPoints;

        if (currentAssistedMode)
            assistedScore += scoreResult.roundPoints;
        else
            conventionalScore += scoreResult.roundPoints;

        UpdateScoreUI();

        TryCompleteLoggerRound(
            spot,
            parked: true,
            timedOut: false,
            scenarioElapsedSeconds,
            movementOrSelectionTimeSeconds,
            walkingDistanceMeters,
            walkingTimeSeconds,
            onTimeGame,
            onTimeIncludingWalk,
            scoreResult.realizedCostToman
        );

        Debug.Log(
            "[ScenarioManager] Round scored | " +
            $"scenario={currentScenario.scenarioId}, mode={GetCurrentModeName()}, " +
            $"spot={spot.spotId}, onTimeIncludingWalk={onTimeIncludingWalk}, " +
            $"citationOccurred={scoreResult.citationOccurred}, " +
            $"adjustedCitationProbability={scoreResult.adjustedCitationProbability:0.####}, " +
            $"randomSeed={scoreResult.randomSeed}, randomDraw={scoreResult.randomDraw:0.####}, " +
            $"realizedCostToman={scoreResult.realizedCostToman:0}, " +
            $"roundPoints={scoreResult.roundPoints}, totalScore={currentScore:0}"
        );

        ShowParkingResult(
            spot,
            scenarioElapsedSeconds,
            walkingDistanceMeters,
            walkingTimeSeconds,
            totalEstimatedTaskTimeSeconds,
            onTimeIncludingWalk,
            scoreResult
        );
    }

    private void ShowParkingResult(
        ParkingSpot spot,
        float scenarioElapsedSeconds,
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
        detail.AppendLine("نوع پارک: " + (result.isLegal ? "قانونی" : "غیرمجاز"));
        detail.AppendLine("محدوده انتخاب: " + spot.GetAlternativeId());
        detail.AppendLine("فاصله پیاده‌روی: " + FormatNumber(walkingDistanceMeters) + " متر");
        detail.AppendLine("زمان جستجو/انتخاب: " + FormatDuration(scenarioElapsedSeconds));
        detail.AppendLine("زمان پیاده‌روی: " + FormatDuration(walkingTimeSeconds));
        detail.AppendLine("زمان کل تخمینی تا مقصد: " + FormatDuration(totalEstimatedTaskTimeSeconds));
        detail.AppendLine("وضعیت زمانی: " + (onTimeIncludingWalk ? "رسیدن به‌موقع" : "دیر رسیدن"));
        detail.AppendLine();

        if (result.isLegal)
        {
            detail.AppendLine("هزینه پارک: " + FormatToman(result.legalCostToman));
        }
        else
        {
            detail.AppendLine(
                "احتمال تعدیل‌شده جریمه: " +
                ToPersianDigits((result.adjustedCitationProbability * 100f)
                    .ToString("0.#", CultureInfo.InvariantCulture)) + " درصد"
            );

            detail.AppendLine(
                "نتیجه کنترل: " +
                (result.citationOccurred ? "خودرو جریمه شد" : "جریمه‌ای ثبت نشد")
            );

            detail.AppendLine(
                "مبلغ پرداخت‌شده: " + FormatToman(result.realizedCostToman)
            );
        }

        detail.AppendLine();
        detail.AppendLine("امتیاز پایه: +" + ToPersianDigits(result.basePoints.ToString()));
        detail.AppendLine("کسر هزینه/جریمه: -" + ToPersianDigits(result.pointDeduction.ToString()));
        detail.AppendLine("امتیاز این مرحله: " + ToPersianDigits(result.roundPoints.ToString()));

        ShowResultPanel(title, detail.ToString().TrimEnd());
    }

    private void OnScenarioTimeout()
    {
        if (surveyCompleted || !scenarioRunning || currentScenario == null)
            return;

        scenarioRunning = false;
        waitingForContinue = true;

        float timeoutSeconds = currentScenario.timeLimitMinutes * 60f;

        // A timed-out round earns zero points. The accumulated score is not reduced.
        TryCompleteLoggerRound(
            chosenSpot: null,
            parked: false,
            timedOut: true,
            scenarioElapsedSeconds: timeoutSeconds,
            movementOrSelectionTimeSeconds: timeoutSeconds,
            walkingDistanceMeters: 0f,
            walkingTimeSeconds: 0f,
            onTimeGame: false,
            onTimeIncludingWalk: false,
            realizedCostToman: 0f
        );

        string detail =
            "در زمان تعیین‌شده جای پارک انتخاب نشد.\n" +
            "امتیاز این مرحله: ۰\n" +
            "امتیاز قبلی شما حفظ شد.";

        ShowResultPanel("زمان مرحله به پایان رسید", detail);
    }

    private void ShowResultPanel(string title, string detail, bool isFinalResult = false)
    {
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

        string scoreText =
            "امتیاز کل: " +
            ToPersianDigits(Mathf.RoundToInt(currentScore).ToString(CultureInfo.InvariantCulture)) +
            "\nمتداول: " +
            ToPersianDigits(Mathf.RoundToInt(conventionalScore).ToString(CultureInfo.InvariantCulture)) +
            " | هوشمند: " +
            ToPersianDigits(Mathf.RoundToInt(assistedScore).ToString(CultureInfo.InvariantCulture));

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

        surveyCompleted = true;
        scenarioRunning = false;
        waitingForContinue = true;

        TryEndLoggerSession();

        string finalDetail =
            "تمام مراحل نظرسنجی تکمیل شد.\n\n" +
            "امتیاز متداول: " + ToPersianDigits(Mathf.RoundToInt(conventionalScore).ToString()) + "\n" +
            "امتیاز هوشمند: " + ToPersianDigits(Mathf.RoundToInt(assistedScore).ToString()) + "\n" +
            "امتیاز نهایی: " + ToPersianDigits(Mathf.RoundToInt(currentScore).ToString());

        ShowResultPanel("پایان نظرسنجی", finalDetail, isFinalResult: true);

        Debug.Log(
            $"[ScenarioManager] Survey completed | total={currentScore:0}, " +
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
        introDetailsLabel = FindTextByNames(
            introDetailsLabel,
            "IntroDetailsText"
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
        LogMissingReference(modeLabel, "ModeLabel");
        LogMissingReference(dwellTimeLabel, "DwellTimeLabel");
        LogMissingReference(timeLimitLabel, "TimeLimitLabel");
        LogMissingReference(legalFeeLabel, "LegalFeeLabel");
        LogMissingReference(illegalFineLabel, "IllegalFineLabel/IllegalFeeLabel");
        LogMissingReference(citationProbLabel, "CitationProbLabel");
        LogMissingReference(onTimeRewardLabel, "OnTimeRewardLabel");
        LogMissingReference(scoreLabel, "ScoreLabel");
        LogMissingReference(resultPanel, "ResultPanel");
        LogMissingReference(resultTitleLabel, "ResultTitleText");
        LogMissingReference(resultDetailLabel, "ResultDetailText");
        LogMissingReference(resultScoreLabel, "ResultScoreText");

        if (scenarioIntroPanel == null)
            Debug.LogWarning("[ScenarioManager] Optional UI reference is missing: ScenarioIntroPanel.");

        if (resultContinueButton == null)
            Debug.LogWarning("[ScenarioManager] Optional UI reference is missing: ResultContinueButton.");
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

            if (completeMethod != null && completeMethod.GetParameters().Length == 11)
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
                method.Invoke(ParkingLogger.Instance, new object[] { true, currentScore });
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
        int rounded = Mathf.RoundToInt(Mathf.Max(0f, value));
        // Keep the ordinary comma. Some Persian font atlases do not contain
        // the Arabic thousands separator U+066C.
        string text = rounded.ToString("N0", CultureInfo.InvariantCulture);
        return ToPersianDigits(text) + " تومان";
    }

    private static string FormatNumber(float value)
    {
        string text = value.ToString("0.#", CultureInfo.InvariantCulture);
        return ToPersianDigits(text);
    }

    private static string FormatPercent(float zeroToOneValue)
    {
        string text = (Mathf.Clamp01(zeroToOneValue) * 100f)
            .ToString("0.#", CultureInfo.InvariantCulture);
        return ToPersianDigits(text) + " درصد";
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
        return ToPersianDigits($"{minutes}:{remainingSeconds:00}");
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