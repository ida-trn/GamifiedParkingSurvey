using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class EligibilityScreen : MonoBehaviour
{
    [Header("Panels")]
    public GameObject eligibilityPanel;
    public GameObject ineligiblePanel;

    [Header("Eligibility Toggles")]
    public Toggle currentStudentToggle;
    public Toggle drivingLicenceToggle;
    public Toggle gate2ExperienceToggle;
    public Toggle firstParticipationToggle;

    [Header("Messages")]
    public TMP_Text errorText;

    [Header("Game Scene")]
    [Tooltip("Enter the exact name of the Unity game scene.")]
    public string gameSceneName = "ParkingGame_RealLocation";

    [Header("Automatic Participant ID")]
    [Tooltip("Use PC01, PC02, etc. when collecting data on several computers.")]
    public string stationCode = "PC01";

    public string participantCounterKey =
        "ParkingSurveyParticipantCounter";

    public int participantNumberDigits = 4;

    private bool participantIdAssigned = false;

    private void Start()
    {
        if (eligibilityPanel != null)
            eligibilityPanel.SetActive(true);

        if (ineligiblePanel != null)
            ineligiblePanel.SetActive(false);

        if (errorText != null)
            errorText.text = "";
    }

    public void SubmitEligibility()
    {
        if (!ReferencesAreAssigned())
            return;

        bool eligible =
             currentStudentToggle.isOn &&
             drivingLicenceToggle.isOn &&
              gate2ExperienceToggle.isOn &&
              firstParticipationToggle.isOn;

        if (!eligible)
        {
            eligibilityPanel.SetActive(false);
            ineligiblePanel.SetActive(true);

            Debug.Log(
                "[EligibilityScreen] Participant is not eligible."
            );

            return;
        }

        string participantId = AssignNextParticipantId();

        if (string.IsNullOrEmpty(participantId))
        {
            if (errorText != null)
            {
                errorText.text =
                    "خطا در ایجاد شناسه شرکت‌کننده.";
            }

            return;
        }

        if (ParticipantSession.Instance == null)
        {
            if (errorText != null)
            {
                errorText.text =
                    "خطا در ایجاد جلسه پژوهش.";
            }

            Debug.LogError(
                "[EligibilityScreen] ParticipantSession is missing."
            );

            return;
        }

        ParticipantSession.Instance.participantId =
            participantId;

        Debug.Log(
            "[EligibilityScreen] Eligible participant assigned ID: "
            + participantId
        );

        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError(
                "[EligibilityScreen] Game scene name is empty."
            );

            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    private string AssignNextParticipantId()
    {
        if (participantIdAssigned)
        {
            if (ParticipantSession.Instance != null)
                return ParticipantSession.Instance.participantId;

            return "";
        }

        int previousNumber =
            PlayerPrefs.GetInt(
                participantCounterKey,
                0
            );

        int nextNumber =
            previousNumber + 1;

        PlayerPrefs.SetInt(
            participantCounterKey,
            nextNumber
        );

        PlayerPrefs.Save();

        participantIdAssigned = true;

        string numberText =
            nextNumber.ToString(
                "D" + participantNumberDigits
            );

        string cleanStationCode =
            string.IsNullOrWhiteSpace(stationCode)
                ? "PC01"
                : stationCode.Trim().ToUpperInvariant();

        return cleanStationCode + "_P" + numberText;
    }

    private bool ReferencesAreAssigned()
    {
        bool assigned =
            eligibilityPanel != null &&
            ineligiblePanel != null &&
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

    public void ExitSurvey()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    [ContextMenu("Reset Participant Counter - Testing Only")]
    public void ResetParticipantCounter()
    {
        PlayerPrefs.DeleteKey(
            participantCounterKey
        );

        PlayerPrefs.Save();

        participantIdAssigned = false;

        Debug.Log(
            "[EligibilityScreen] Participant counter reset."
        );
    }
}