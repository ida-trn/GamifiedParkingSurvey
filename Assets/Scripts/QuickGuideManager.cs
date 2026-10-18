using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuickGuideManager : MonoBehaviour
{
    [Header("Quick Guide UI References")]
    public GameObject quickGuidePanel;
    public Button openButton;
    public Button closeButton;
    public TMP_Text titleText;
    public TMP_Text bodyText;

    [Header("Parking Legend UI References")]
    public GameObject parkingLegendPanel;
    public Button parkingLegendOpenButton;
    public Button parkingLegendCloseButton;

    [Header("Guide Launcher Group")]
    [Tooltip("Parent object that contains the launcher title and launcher buttons.")]
    public GameObject launcherGroup;

    [Header("Quick Guide Text Layout")]
    [Tooltip("Matches the current TMP inspector setting shown in your screenshot.")]
    public float bodyLineSpacing = 0f;

    [Header("Conventional-Only Quick Guide Legends")]
    [Tooltip("These visual legends are shown only in Conventional mode.")]
    public GameObject uTurnButtonLegend;
    public GameObject stopButtonLegend;
    public GameObject undoButtonLegend;
    public GameObject arrowLegend;

    private const string ConventionalText =
        "فلش‌های زرد: انتخاب مسیر حرکت و ادامه حرکت پس از ترمز\n" +
        ": توقف خودرو؛ پس از توقف، برای ادامه روی فلش زرد کلیک کنید.\n" +
        ": در خیابان‌های دوطرفه، امکان دور زدن فعال می‌شود.\n" +
        ": دنده عقب در بن‌بست‌ها فعال خواهد شد.\n" +
        "انتخاب جای پارک: روی جایگاه موردنظر کلیک کنید تا خودرو در آن پارک شود.";

    private const string AssistedText =
        "جابه‌جایی نقشه: حرکت دادن نقشه\n\n" +
        "زوم: بزرگ‌نمایی و کوچک‌نمایی\n\n" +
        "انتخاب جای پارک: روی جایگاه موردنظر کلیک کنید.";

    private bool isAssistedMode = false;

    private void Start()
    {
        if (quickGuidePanel != null)
            quickGuidePanel.SetActive(false);

        if (parkingLegendPanel != null)
            parkingLegendPanel.SetActive(false);

        if (openButton != null)
            openButton.onClick.AddListener(OpenGuide);

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseGuide);

        if (parkingLegendOpenButton != null)
            parkingLegendOpenButton.onClick.AddListener(OpenParkingLegend);

        if (parkingLegendCloseButton != null)
            parkingLegendCloseButton.onClick.AddListener(CloseParkingLegend);

        ShowLauncherButtons();
        UpdateGuideText();
    }

    public void SetMode(bool assistedMode)
    {
        isAssistedMode = assistedMode;
        UpdateGuideText();
    }

    private void UpdateGuideText()
    {
        if (titleText != null)
        {
            titleText.text = isAssistedMode
                ? "راهنمای حالت کمکی"
                : "راهنمای سریع";
        }

        if (bodyText != null)
        {
            bodyText.text = isAssistedMode
                ? AssistedText
                : ConventionalText;

            bodyText.lineSpacing = bodyLineSpacing;
        }

        UpdateConventionalLegendVisibility();
    }

    private void UpdateConventionalLegendVisibility()
    {
        bool show = !isAssistedMode;

        if (uTurnButtonLegend != null)
            uTurnButtonLegend.SetActive(show);

        if (stopButtonLegend != null)
            stopButtonLegend.SetActive(show);

        if (undoButtonLegend != null)
            undoButtonLegend.SetActive(show);

        if (arrowLegend != null)
            arrowLegend.SetActive(show);
    }

    private void HideLauncherButtons()
    {
        if (launcherGroup != null)
        {
            launcherGroup.SetActive(false);
            return;
        }

        if (openButton != null)
            openButton.gameObject.SetActive(false);

        if (parkingLegendOpenButton != null)
            parkingLegendOpenButton.gameObject.SetActive(false);
    }

    private void ShowLauncherButtons()
    {
        if (launcherGroup != null)
        {
            launcherGroup.SetActive(true);
            return;
        }

        if (openButton != null)
            openButton.gameObject.SetActive(true);

        if (parkingLegendOpenButton != null)
            parkingLegendOpenButton.gameObject.SetActive(true);
    }

    public void OpenGuide()
    {
        UpdateGuideText();

        if (parkingLegendPanel != null)
            parkingLegendPanel.SetActive(false);

        if (quickGuidePanel != null)
            quickGuidePanel.SetActive(true);

        HideLauncherButtons();
    }

    public void CloseGuide()
    {
        if (quickGuidePanel != null)
            quickGuidePanel.SetActive(false);

        ShowLauncherButtons();
    }

    public void OpenParkingLegend()
    {
        if (quickGuidePanel != null)
            quickGuidePanel.SetActive(false);

        if (parkingLegendPanel != null)
            parkingLegendPanel.SetActive(true);

        HideLauncherButtons();
    }

    public void CloseParkingLegend()
    {
        if (parkingLegendPanel != null)
            parkingLegendPanel.SetActive(false);

        ShowLauncherButtons();
    }
}
