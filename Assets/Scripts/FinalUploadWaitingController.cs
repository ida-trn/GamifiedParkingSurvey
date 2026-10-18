using System;
using System.Collections;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Final survey upload + thank-you flow.
///
/// Flow:
/// 1) Socioeconomic questionnaire completes.
/// 2) FinalUploadCanvas appears with the upload-wait message.
/// 3) The survey session is ended so final rows can enter the existing upload queue.
/// 4) This controller waits until Supabase priority + bulk queues are empty.
/// 5) FinalUploadCanvas STAYS ACTIVE and changes into the final thank-you screen.
/// 6) ScenarioManager's old ResultPanel is kept hidden for the rest of the session.
/// </summary>
public class FinalUploadWaitingController : MonoBehaviour
{
    [Header("References")]
    public ScenarioManager scenarioManager;

    [Tooltip("Assign FinalUploadCanvas.")]
    public GameObject waitingCanvasRoot;

    [Tooltip("Assign FinalUploadTitle.")]
    public TMP_Text waitingTitleText;

    [Tooltip("Assign FinalUploadMessage.")]
    public TMP_Text waitingMessageText;

    [Tooltip("Assign LoadingDots.")]
    public TMP_Text loadingDotsText;

    [Header("Timing")]
    [Min(0f)]
    public float minimumWaitingSeconds = 3f;

    [Min(0.2f)]
    public float idleConfirmationSeconds = 1.5f;

    [Min(10f)]
    public float maximumWaitingSeconds = 180f;

    [Min(0.1f)]
    public float dotStepSeconds = 0.35f;

    [Header("Upload waiting text")]
    [TextArea]
    public string waitingTitle = "در حال ثبت پاسخ‌ها...";

    [TextArea(2, 5)]
    public string waitingMessage =
        "لطفاً این صفحه را نبندید.\n" +
        "پاسخ‌های شما در حال ذخیره‌سازی است و ممکن است چند لحظه زمان ببرد.";

    [Header("Final thank-you text")]
    [TextArea]
    public string successTitle = "سپاس از مشارکت شما";

    [TextArea(3, 7)]
    public string successMessage =
        "از مشارکت شما در این نظرسنجی سپاسگزاریم.\n\n" +
        "پاسخ‌های شما با موفقیت ثبت شد.\n\n" +
        "اکنون می‌توانید این صفحه را ببندید.";

    [Header("Share invitation (shown only after successful upload)")]
    [Tooltip("Show a polite request to share the survey with someone who meets the eligibility criteria.")]
    public bool showShareInvitation = true;

    [TextArea(2, 5)]
    public string shareInvitationMessage =
        "اگر فردی را می‌شناسید که شرایط شرکت در این پژوهش را دارد، " +
        "سپاسگزار می‌شویم لینک نظرسنجی را با او به اشتراک بگذارید.";

    [Header("Survey link copy button")]
    [Tooltip("Assign the Button under FinalUploadWaitingPanel. It appears only after the uploads finish.")]
    public Button copySurveyLinkButton;

    [Tooltip("Public survey link to copy; do not use an admin/Supabase link here.")]
    public string surveyLink = "https://ida-trn.github.io/parking-survey/";

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void CopySurveyLinkWebGL(string url);
#endif

    /// <summary>
    /// Connected automatically to copySurveyLinkButton at startup.
    /// In the Unity Editor the native clipboard is used; in WebGL,
    /// a browser plugin confirms success or failure in an on-screen toast.
    /// </summary>
    public void CopySurveyLink()
    {
        string url = (surveyLink ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("[FinalUploadWaitingController] Survey link is empty.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        CopySurveyLinkWebGL(url);
#else
        GUIUtility.systemCopyBuffer = url;
        Debug.Log("[FinalUploadWaitingController] Survey link copied to clipboard.");
#endif
    }

    [Header("Upload error text")]
    [TextArea]
    public string errorTitle = "خطا در ثبت پاسخ‌ها";

    [TextArea(2, 6)]
    public string errorMessage =
        "ذخیره‌سازی پاسخ‌ها کامل نشد.\n" +
        "لطفاً این صفحه را نبندید و اتصال اینترنت را بررسی کنید.";

    private Coroutine waitingCoroutine;
    private bool permanentUploadFailureDetected;
    private bool finalUiOwnsScreen;

    private void Awake()
    {
        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (waitingCanvasRoot != null)
            waitingCanvasRoot.SetActive(false);

        if (copySurveyLinkButton != null)
        {
            copySurveyLinkButton.gameObject.SetActive(false);
            // No need to add an On Click() listener manually in the Inspector.
            copySurveyLinkButton.onClick.AddListener(CopySurveyLink);
        }
    }

    private void OnDestroy()
    {
        if (copySurveyLinkButton != null)
            copySurveyLinkButton.onClick.RemoveListener(CopySurveyLink);
    }

    private void OnEnable()
    {
        Application.logMessageReceived += OnLogMessageReceived;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= OnLogMessageReceived;
    }

    private void LateUpdate()
    {
        if (!finalUiOwnsScreen)
            return;

        // Prevent the legacy ScenarioManager ResultPanel from reappearing.
        if (scenarioManager != null && scenarioManager.resultPanel != null)
            scenarioManager.resultPanel.SetActive(false);

        // Keep FinalUploadCanvas visible and above every other overlay canvas.
        if (waitingCanvasRoot != null)
        {
            waitingCanvasRoot.SetActive(true);
            waitingCanvasRoot.transform.SetAsLastSibling();

            Canvas canvas = waitingCanvasRoot.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = 10000;
            }
        }
    }

    /// <summary>
    /// Connect SocioEconomicQuestionnaire -> On Questionnaire Completed()
    /// to this method.
    /// </summary>
    public void BeginFinalUploadWait()
    {
        if (waitingCoroutine != null)
            return;

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (scenarioManager == null)
        {
            Debug.LogError("[FinalUploadWaitingController] ScenarioManager was not found.");
            return;
        }

        if (waitingCanvasRoot == null)
        {
            Debug.LogError("[FinalUploadWaitingController] FinalUploadCanvas is not assigned.");
            return;
        }

        finalUiOwnsScreen = true;
        permanentUploadFailureDetected = false;

        if (copySurveyLinkButton != null)
            copySurveyLinkButton.gameObject.SetActive(false);

        // Show the waiting UI first.
        SetText(waitingTitleText, waitingTitle);
        SetText(waitingMessageText, waitingMessage);

        if (loadingDotsText != null)
        {
            loadingDotsText.gameObject.SetActive(true);
            loadingDotsText.text = "●  ○  ○";
        }

        waitingCanvasRoot.SetActive(true);
        waitingCanvasRoot.transform.SetAsLastSibling();

        Canvas waitingCanvas = waitingCanvasRoot.GetComponent<Canvas>();
        if (waitingCanvas != null)
        {
            waitingCanvas.overrideSorting = true;
            waitingCanvas.sortingOrder = 10000;
        }

        // End/log the survey so the final session row enters the upload queue.
        scenarioManager.CompleteSurveyAfterFinalQuestionnaire();

        // Hide the legacy result panel immediately.
        if (scenarioManager.resultPanel != null)
            scenarioManager.resultPanel.SetActive(false);

        Debug.Log(
            "[FinalUploadWaitingController] FINAL UI STARTED | " +
            "FinalUploadCanvas is the only participant-facing final screen."
        );

        waitingCoroutine = StartCoroutine(WaitForFinalUploads());
    }

    private IEnumerator WaitForFinalUploads()
    {
        float startedAt = Time.realtimeSinceStartup;
        float minimumUntil = startedAt + Mathf.Max(0f, minimumWaitingSeconds);
        float idleSince = -1f;
        float nextDotChangeAt = startedAt;
        int dotFrame = 0;

        while (true)
        {
            float now = Time.realtimeSinceStartup;

            AnimateDots(ref dotFrame, ref nextDotChangeAt, now);

            if (permanentUploadFailureDetected)
            {
                ShowUploadError();
                yield break;
            }

            SupabaseSurveyUploader uploader = SupabaseSurveyUploader.Instance;

            if (uploader == null)
            {
                if (now >= minimumUntil)
                {
                    Debug.LogError(
                        "[FinalUploadWaitingController] SupabaseSurveyUploader was not found."
                    );
                    ShowUploadError();
                    yield break;
                }

                yield return null;
                continue;
            }

            if (!uploader.uploadEnabled)
            {
                Debug.LogError(
                    "[FinalUploadWaitingController] Supabase uploading is disabled."
                );
                ShowUploadError();
                yield break;
            }

            if (uploader.HasPendingUploads)
            {
                idleSince = -1f;
            }
            else if (now >= minimumUntil)
            {
                if (idleSince < 0f)
                    idleSince = now;

                if (now - idleSince >= Mathf.Max(0.2f, idleConfirmationSeconds))
                {
                    ShowFinalThankYouScreen();
                    yield break;
                }
            }

            if (now - startedAt >= Mathf.Max(10f, maximumWaitingSeconds))
            {
                Debug.LogError(
                    "[FinalUploadWaitingController] Timed out waiting for final uploads. " +
                    $"priority={uploader.PendingPriorityUploads}, " +
                    $"bulk={uploader.PendingBulkUploads}"
                );

                ShowUploadError();
                yield break;
            }

            yield return null;
        }
    }

    private void AnimateDots(
        ref int frame,
        ref float nextChangeAt,
        float now)
    {
        if (loadingDotsText == null ||
            !loadingDotsText.gameObject.activeInHierarchy ||
            now < nextChangeAt)
        {
            return;
        }

        switch (frame % 3)
        {
            case 0:
                loadingDotsText.text = "●  ○  ○";
                break;
            case 1:
                loadingDotsText.text = "○  ●  ○";
                break;
            default:
                loadingDotsText.text = "○  ○  ●";
                break;
        }

        frame++;
        nextChangeAt = now + Mathf.Max(0.1f, dotStepSeconds);
    }

    private void ShowFinalThankYouScreen()
    {
        // DO NOT disable FinalUploadCanvas.
        // Reuse the same canvas as the permanent final thank-you screen.
        SetText(waitingTitleText, successTitle);
        // Compose at runtime: Unity may already have successMessage saved in
        // the Inspector, so editing its source initializer alone is unreliable.
        SetText(waitingMessageText, BuildFinalSuccessMessage());

        if (loadingDotsText != null)
            loadingDotsText.gameObject.SetActive(false);

        if (copySurveyLinkButton != null)
            copySurveyLinkButton.gameObject.SetActive(true);

        if (waitingCanvasRoot != null)
        {
            waitingCanvasRoot.SetActive(true);
            waitingCanvasRoot.transform.SetAsLastSibling();

            Canvas canvas = waitingCanvasRoot.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = 10000;
            }
        }

        if (scenarioManager != null && scenarioManager.resultPanel != null)
            scenarioManager.resultPanel.SetActive(false);

        waitingCoroutine = null;

        Debug.Log(
            "[FinalUploadWaitingController] THANK-YOU SCREEN ACTIVE | " +
            "FinalUploadCanvas remains visible; legacy ResultPanel is suppressed."
        );
    }

    private string BuildFinalSuccessMessage()
    {
        string baseMessage = (successMessage ?? string.Empty).Trim();

        if (!showShareInvitation || string.IsNullOrWhiteSpace(shareInvitationMessage))
            return baseMessage;

        // Put the share invitation BEFORE the closing instruction even when
        // successMessage was serialized in an existing Unity scene or prefab.
        const string closingMessage = "اکنون می‌توانید این صفحه را ببندید.";
        if (baseMessage.EndsWith(closingMessage, System.StringComparison.Ordinal))
        {
            baseMessage = baseMessage.Substring(
                0, baseMessage.Length - closingMessage.Length
            ).TrimEnd();
        }

        string invitation = shareInvitationMessage.Trim();

        // Avoid repeating the sentence if the Inspector message already has it.
        if (baseMessage.Contains(invitation))
            return baseMessage + "\n\n" + closingMessage;

        return baseMessage + "\n\n" + invitation + "\n\n" + closingMessage;
    }

    private void ShowUploadError()
    {
        if (copySurveyLinkButton != null)
            copySurveyLinkButton.gameObject.SetActive(false);

        SetText(waitingTitleText, errorTitle);
        SetText(waitingMessageText, errorMessage);

        if (loadingDotsText != null)
        {
            loadingDotsText.gameObject.SetActive(true);
            loadingDotsText.text = "⚠";
        }

        if (waitingCanvasRoot != null)
            waitingCanvasRoot.SetActive(true);

        if (scenarioManager != null && scenarioManager.resultPanel != null)
            scenarioManager.resultPanel.SetActive(false);

        waitingCoroutine = null;
    }

    private void OnLogMessageReceived(
        string condition,
        string stackTrace,
        LogType type)
    {
        if (string.IsNullOrEmpty(condition))
            return;

        if (condition.Contains("[SupabaseSurveyUploader] Giving up") ||
            condition.Contains("[SupabaseSurveyUploader] Project URL or publishable key is missing") ||
            condition.Contains("[SupabaseSurveyUploader] Refusing to upload"))
        {
            permanentUploadFailureDetected = true;
        }
    }

    private static void SetText(TMP_Text label, string value)
    {
        if (label == null)
            return;

        label.text = value ?? string.Empty;
        label.SetVerticesDirty();
        label.SetLayoutDirty();

        if (label.gameObject.activeInHierarchy)
            label.ForceMeshUpdate(true, true);
    }
}
