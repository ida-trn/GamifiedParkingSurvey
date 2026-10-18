using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class TutorialManager : MonoBehaviour
{
    [Header("Main References")]
    public ScenarioManager scenarioManager;
    public ParkingScenario practiceScenario;

    [Header("UI Roots")]
    public GameObject tutorialCanvas;
    public GameObject tutorialPanel;
    public GameObject gameplayUIRoot;
    public GameObject practiceBanner;

    [Header("Tutorial Text")]
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public TMP_Text pageCounterText;
    public TMP_Text nextButtonText;
    public TMP_Text practiceBannerText;

    [Header("Buttons")]
    public Button previousButton;

    [Header("Tutorial Parking Guides")]

    [Tooltip(
        "First horizontal legend: legal, smart, no-parking, disabled, occupied, etc."
    )]
    [FormerlySerializedAs("parkingLegendRoot")]
    public GameObject generalParkingLegendRoot;

    [Tooltip(
        "Second horizontal legend containing the different illegal parking reasons."
    )]
    public GameObject illegalReasonsLegendRoot;

    [Header("Tutorial Visual Style")]

    [Tooltip(
        "Optional Image component used as the light background of the tutorial panel."
    )]
    public Image tutorialPanelBackground;

    [Tooltip(
        "Optional fullscreen overlay Image behind the tutorial panel."
    )]
    public Image tutorialScreenOverlay;

    [Tooltip("Light background color for the tutorial panel.")]
    public Color tutorialPanelBackgroundColor =
        new Color(1f, 1f, 1f, 0.96f);

    [Tooltip("Light overlay over the map behind the tutorial.")]
    public Color tutorialScreenOverlayColor =
        new Color(1f, 1f, 1f, 0.28f);


    [Header("Mobile Landscape Responsiveness")]
    [Tooltip(
        "On very wide landscape screens (common on Android phones), match the UI scale to screen HEIGHT instead of width. " +
        "This preserves the tutorial's vertical spacing and prevents the parking legends from crowding/overlapping."
    )]
    public bool fitTutorialToLandscapeHeight = true;

    [Tooltip("Aspect ratio at/above which the tutorial switches to height-matched scaling.")]
    [Range(1.6f, 3.0f)]
    public float wideLandscapeAspectThreshold = 1.9f;

    private CanvasScaler tutorialCanvasScaler;
    private bool tutorialScalerStateCached;
    private CanvasScaler.ScreenMatchMode originalTutorialScreenMatchMode;
    private float originalTutorialMatchWidthOrHeight;


    private int pageIndex = 0;


    // ================================================================
    // TUTORIAL CONTENT
    // ================================================================

    private readonly string[] titles =
    {
        "راهنمای شبیه‌ساز انتخاب محل پارک",
        "راهنمای حالت کمکی"
    };


    private readonly string[] bodies =
    {
        // ------------------------------------------------------------
        // PAGE 1 — Main tutorial + parking visuals
        // ------------------------------------------------------------
        "در این شبیه‌ساز تصور کنید با خودروی شخصی به دانشگاه علم و صنعت ایران آمده‌اید و مقصد شما درب شماره ۲ (درب ملت) است.\n\n" +

        "در هر مرحله، جای پارکی را انتخاب کنید که در شرایط واقعی انتخاب می‌کردید. پاسخ درست یا غلطی وجود ندارد؛ هدف، تصمیم‌گیری مشابه شرایط واقعی است.\n\n" +

        "پیش از شروع هر سناریوی اصلی، یک پنل اطلاعات سناریو نمایش داده می‌شود. لطفاً قبل از شروع هر سناریو، اطلاعات آن را با دقت مطالعه کنید.\n\n" +

        "اطلاعات مربوط به زمان، هزینه تعرفه پارک هوشمند، هزینه جریمه پارک های غیرمجاز و احتمال جریمه در هر سناریو در پنل سمت راست نمایش داده می‌شود.\n\n" +

        "در پایین صفحه، شکل و رنگ انواع جایگاه‌های پارک های مجاز و غیرمجاز نمایش داده شده است.",


        // ------------------------------------------------------------
        // PAGE 2 — Assisted mode explanation
        // ------------------------------------------------------------
        "اکنون تمرین حالت متعارف به پایان رسیده است.\n\n" +

        "در مرحله بعد، تمرین حالت کمکی را انجام می‌دهید.\n\n" +

        "در حالت کمکی، کل نقشه و همه جایگاه‌های پارک <color=red>بدون رانندگی</color> قابل مشاهده هستند.\n\n" +

        "با جابه‌جایی نقشه و بزرگ‌نمایی یا کوچک‌نمایی می‌توانید نقشه را بررسی کرده و بخش‌های مختلف آن را مشاهده کنید.\n\n" +

        "سپس روی جایگاه موردنظر کلیک کنید و انتخاب خود را تأیید یا لغو کنید.\n\n" +

        "نتیجه این تمرین نیز ثبت نمی‌شود."
    };


    private readonly string[] buttonTexts =
    {
        "شروع تمرین متعارف",
        "شروع تمرین حالت کمکی"
    };


    // ================================================================
    // INITIALIZATION
    // ================================================================

    private void Start()
    {
        if (scenarioManager == null)
        {
            Debug.LogError(
                "[TutorialManager] ScenarioManager is not assigned."
            );
            return;
        }

        if (practiceScenario == null)
        {
            Debug.LogError(
                "[TutorialManager] PracticeScenario is not assigned."
            );
            return;
        }

        if (tutorialCanvas != null)
            tutorialCanvas.SetActive(true);

        if (tutorialPanel != null)
            tutorialPanel.SetActive(true);

        if (gameplayUIRoot != null)
            gameplayUIRoot.SetActive(false);

        if (practiceBanner != null)
            practiceBanner.SetActive(false);

        scenarioManager.SetTutorialInputEnabled(false);

        pageIndex = 0;

        ApplyResponsiveCanvasScaling();
        ApplyTutorialVisualStyle();
        ShowCurrentPage();
    }


    // ================================================================
    // MOBILE / WIDE-LANDSCAPE RESPONSIVE SCALING
    // ================================================================

    private void ApplyResponsiveCanvasScaling()
    {
        if (!fitTutorialToLandscapeHeight)
            return;

        if (tutorialCanvasScaler == null)
        {
            Canvas targetCanvas = null;

            if (tutorialCanvas != null)
            {
                targetCanvas = tutorialCanvas.GetComponent<Canvas>();

                if (targetCanvas == null)
                    targetCanvas = tutorialCanvas.GetComponentInParent<Canvas>();
            }

            if (targetCanvas == null)
                targetCanvas = GetComponentInParent<Canvas>();

            if (targetCanvas != null)
                tutorialCanvasScaler = targetCanvas.GetComponent<CanvasScaler>();
        }

        if (tutorialCanvasScaler == null ||
            tutorialCanvasScaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            return;
        }

        if (!tutorialScalerStateCached)
        {
            originalTutorialScreenMatchMode =
                tutorialCanvasScaler.screenMatchMode;

            originalTutorialMatchWidthOrHeight =
                tutorialCanvasScaler.matchWidthOrHeight;

            tutorialScalerStateCached = true;
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
            tutorialCanvasScaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // 1 = match HEIGHT. This is the important part for ultra-wide
            // Android landscape screens where browser chrome reduces the
            // usable vertical space.
            tutorialCanvasScaler.matchWidthOrHeight = 1f;
        }
        else
        {
            RestoreTutorialCanvasScaling();
        }

        Canvas.ForceUpdateCanvases();
    }


    private void RestoreTutorialCanvasScaling()
    {
        if (!tutorialScalerStateCached || tutorialCanvasScaler == null)
            return;

        tutorialCanvasScaler.screenMatchMode =
            originalTutorialScreenMatchMode;

        tutorialCanvasScaler.matchWidthOrHeight =
            originalTutorialMatchWidthOrHeight;
    }


    private void OnRectTransformDimensionsChange()
    {
        if (!Application.isPlaying)
            return;

        if (tutorialCanvas != null && tutorialCanvas.activeInHierarchy)
            ApplyResponsiveCanvasScaling();
    }


    private void OnDestroy()
    {
        RestoreTutorialCanvasScaling();
    }


    // ================================================================
    // VISUAL STYLE
    // ================================================================

    private void ApplyTutorialVisualStyle()
    {
        if (tutorialPanelBackground != null)
        {
            tutorialPanelBackground.color =
                tutorialPanelBackgroundColor;
        }

        if (tutorialScreenOverlay != null)
        {
            tutorialScreenOverlay.color =
                tutorialScreenOverlayColor;
        }

        if (titleText != null)
        {
            titleText.color = Color.black;
            titleText.alignment =
                TextAlignmentOptions.Center;
        }

        if (bodyText != null)
        {
            bodyText.color = Color.black;
            bodyText.alignment =
                TextAlignmentOptions.Center;
        }
    }


    // ================================================================
    // PAGE DISPLAY
    // ================================================================

    private void ShowCurrentPage()
    {
        if (pageIndex < 0 || pageIndex >= titles.Length)
        {
            Debug.LogError(
                "[TutorialManager] Invalid page index: " +
                pageIndex
            );

            return;
        }

        if (titleText != null)
        {
            titleText.text = titles[pageIndex];
            titleText.alignment =
                TextAlignmentOptions.Center;
        }

        if (bodyText != null)
        {
            bodyText.text = bodies[pageIndex];
            bodyText.richText = true;
            bodyText.alignment =
                TextAlignmentOptions.Center;
        }

        if (nextButtonText != null)
        {
            nextButtonText.text =
                buttonTexts[pageIndex];
        }


        // ------------------------------------------------------------
        // Old page-navigation UI is no longer needed.
        // ------------------------------------------------------------

        if (pageCounterText != null)
        {
            pageCounterText.gameObject.SetActive(false);
        }

        if (previousButton != null)
        {
            previousButton.gameObject.SetActive(false);
        }


        // ------------------------------------------------------------
        // Both parking-guide rows appear ONLY on page 1.
        // The Assisted explanation page stays clean.
        // ------------------------------------------------------------

        bool showParkingGuides =
            pageIndex == 0;

        if (generalParkingLegendRoot != null)
        {
            generalParkingLegendRoot.SetActive(
                showParkingGuides
            );
        }

        if (illegalReasonsLegendRoot != null)
        {
            illegalReasonsLegendRoot.SetActive(
                showParkingGuides
            );
        }


        ApplyResponsiveCanvasScaling();
        ApplyTutorialVisualStyle();
    }


    // ================================================================
    // NEXT BUTTON
    // ================================================================

    public void NextPage()
    {
        // Page 1 -> Conventional practice
        if (pageIndex == 0)
        {
            StartPractice(false);
            return;
        }

        // Assisted explanation page -> Assisted practice
        if (pageIndex == 1)
        {
            StartPractice(true);
        }
    }


    // Kept so an old Inspector button reference cannot cause an error.
    public void PreviousPage()
    {
        // The simplified tutorial does not use Back navigation.
    }


    // ================================================================
    // PRACTICE
    // ================================================================

    private void StartPractice(bool assistedMode)
    {
        if (tutorialPanel != null)
        {
            tutorialPanel.SetActive(false);
        }

        if (gameplayUIRoot != null)
        {
            gameplayUIRoot.SetActive(true);
        }

        if (practiceBanner != null)
        {
            practiceBanner.SetActive(true);
        }

        if (practiceBannerText != null)
        {
            practiceBannerText.text =
                assistedMode
                    ? "مرحله تمرینی ۲ از ۲ ـ حالت کمکی"
                    : "مرحله تمرینی ۱ از ۲ ـ حالت متعارف";
        }

        scenarioManager.StartPracticeRound(
            practiceScenario,
            assistedMode
        );
    }


    // ================================================================
    // PRACTICE COMPLETED
    // ================================================================

    public void OnPracticeCompleted(bool assistedMode)
    {
        scenarioManager.SetTutorialInputEnabled(false);

        if (gameplayUIRoot != null)
        {
            gameplayUIRoot.SetActive(false);
        }

        if (practiceBanner != null)
        {
            practiceBanner.SetActive(false);
        }


        // ------------------------------------------------------------
        // Conventional practice finished:
        // show Assisted explanation page.
        // ------------------------------------------------------------

        if (!assistedMode)
        {
            pageIndex = 1;

            if (tutorialPanel != null)
            {
                tutorialPanel.SetActive(true);
            }

            ShowCurrentPage();
            return;
        }


        // ------------------------------------------------------------
        // Assisted practice finished:
        // tutorial is complete.
        // ------------------------------------------------------------

        FinishTutorial();
    }


    // ================================================================
    // FINISH
    // ================================================================

    private void FinishTutorial()
    {
        if (gameplayUIRoot != null)
        {
            gameplayUIRoot.SetActive(true);
        }

        RestoreTutorialCanvasScaling();
        scenarioManager.BeginMainSurvey();

        if (tutorialCanvas != null)
        {
            tutorialCanvas.SetActive(false);
        }
    }
}