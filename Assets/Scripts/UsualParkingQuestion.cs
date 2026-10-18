using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class UsualParkingQuestion : MonoBehaviour
{
    [Serializable]
    private class StreetChoice
    {
        public string code;
        public string englishName;
        public Toggle toggle;

        public StreetChoice(string code, string englishName, Toggle toggle)
        {
            this.code = code;
            this.englishName = englishName;
            this.toggle = toggle;
        }
    }

    [Header("Panel")]
    [Tooltip("Optional. If empty, this GameObject is used as the panel root.")]
    public GameObject panelRoot;

    public TMP_Text errorText;
    public GameObject otherInputRoot;
    public TMP_InputField otherStreetInput;

    [Header("Street toggles")]
    public Toggle toggleHeydarkhani;
    public Toggle toggleMalekloo;
    [FormerlySerializedAs("toggleNiloufaroushan")]
    public Toggle toggleNilforooshan;
    public Toggle toggleElmOSanat;
    [FormerlySerializedAs("toggleAbgoonF1")]
    public Toggle toggleAbgoon41;
    public Toggle toggleZolfaghari;
    public Toggle toggleLotfizadeh;
    public Toggle toggleSaadatmand;
    public Toggle toggleLayeghnia;
    public Toggle toggleMehran;
    public Toggle toggleBagheri;
    public Toggle toggleSalehi;
    [FormerlySerializedAs("toggleAttariF2")]
    public Toggle toggleAttari42;
    public Toggle toggleEbadi;
    public Toggle toggleVahdat;
    public Toggle toggleShohada;
    public Toggle toggleOther;

    [Header("Behaviour")]
    [Tooltip("Hide this panel after a valid answer is submitted.")]
    public bool hidePanelAfterSubmit = true;

    [Header("Output")]
    [Tooltip("Uses the same default folder name as ParkingLogger.")]
    public string outputSubfolder = "ParkingSurveyData";

    [Tooltip("Write or update participant_profiles.csv in the survey output folder.")]
    public bool writeParticipantProfileCsv = true;

    [Tooltip("Called after a valid answer is saved. Connect this to the method that starts the tutorial.")]
    public UnityEvent onSubmitted;

    private readonly List<StreetChoice> streetChoices = new List<StreetChoice>();
    private bool listenersRegistered;
    private StreetLabelManager streetLabelManager;

    private void Awake()
    {
        if (panelRoot == null)
            panelRoot = gameObject;

        BuildStreetChoiceList();
        RegisterListeners();
        RefreshOtherInputVisibility();
        ClearError();
    }

    private void OnEnable()
    {
        BuildStreetChoiceList();
        RegisterListeners();
        RefreshOtherInputVisibility();
        ClearError();

        // This also covers the case where the question panel starts enabled
        // from the Inspector instead of being opened through Show().
        if (panelRoot == null)
            panelRoot = gameObject;

        if (panelRoot.activeInHierarchy)
            SetStreetLabelsHidden(true);
    }

    private void OnDisable()
    {
        // If this component/panel is disabled directly instead of through
        // Hide(), restore the labels as a safety net.
        SetStreetLabelsHidden(false);
    }

    private void OnDestroy()
    {
        SetStreetLabelsHidden(false);
        UnregisterListeners();
    }

    private StreetLabelManager GetStreetLabelManager()
    {
        if (streetLabelManager == null)
            streetLabelManager = FindFirstObjectByType<StreetLabelManager>();

        return streetLabelManager;
    }

    private void SetStreetLabelsHidden(bool hidden)
    {
        StreetLabelManager manager = GetStreetLabelManager();

        if (manager != null)
            manager.SetLabelsHidden(hidden);
    }

    private void BuildStreetChoiceList()
    {
        streetChoices.Clear();

        // Stable codes are used in the CSV. English names should match the
        // street names used by the parking objects wherever possible.
        streetChoices.Add(new StreetChoice("HDR", "Heydarkhani", toggleHeydarkhani));
        streetChoices.Add(new StreetChoice("MAL", "Malekloo", toggleMalekloo));
        streetChoices.Add(new StreetChoice("NIL", "Nilforooshan", toggleNilforooshan));
        streetChoices.Add(new StreetChoice("ELS", "Elm O Sanat", toggleElmOSanat));
        streetChoices.Add(new StreetChoice("ABG", "Abgoon 41", toggleAbgoon41));
        streetChoices.Add(new StreetChoice("ZOL", "Zolfaghari", toggleZolfaghari));
        streetChoices.Add(new StreetChoice("LOT", "Lotfizadeh", toggleLotfizadeh));
        streetChoices.Add(new StreetChoice("SAA", "Saadatmand", toggleSaadatmand));
        streetChoices.Add(new StreetChoice("LAY", "Layeghnia", toggleLayeghnia));
        streetChoices.Add(new StreetChoice("MEH", "Mehran", toggleMehran));
        streetChoices.Add(new StreetChoice("BAG", "Bagheri", toggleBagheri));
        streetChoices.Add(new StreetChoice("SAL", "Salehi", toggleSalehi));
        streetChoices.Add(new StreetChoice("ATT", "Attari 42", toggleAttari42));
        streetChoices.Add(new StreetChoice("EBA", "Ebadi", toggleEbadi));
        streetChoices.Add(new StreetChoice("VAH", "Vahdat", toggleVahdat));
        streetChoices.Add(new StreetChoice("SHO", "Shohada", toggleShohada));
    }

    private void RegisterListeners()
    {
        if (listenersRegistered)
            return;

        foreach (StreetChoice choice in streetChoices)
        {
            if (choice.toggle != null)
                choice.toggle.onValueChanged.AddListener(OnAnyToggleChanged);
        }

        if (toggleOther != null)
            toggleOther.onValueChanged.AddListener(OnOtherToggleChanged);

        if (otherStreetInput != null)
            otherStreetInput.onValueChanged.AddListener(OnOtherTextChanged);

        listenersRegistered = true;
    }

    private void UnregisterListeners()
    {
        if (!listenersRegistered)
            return;

        foreach (StreetChoice choice in streetChoices)
        {
            if (choice.toggle != null)
                choice.toggle.onValueChanged.RemoveListener(OnAnyToggleChanged);
        }

        if (toggleOther != null)
            toggleOther.onValueChanged.RemoveListener(OnOtherToggleChanged);

        if (otherStreetInput != null)
            otherStreetInput.onValueChanged.RemoveListener(OnOtherTextChanged);

        listenersRegistered = false;
    }

    private void OnAnyToggleChanged(bool _)
    {
        ClearError();
    }

    private void OnOtherToggleChanged(bool _)
    {
        RefreshOtherInputVisibility();
        ClearError();
    }

    private void OnOtherTextChanged(string _)
    {
        ClearError();
    }

    private void RefreshOtherInputVisibility()
    {
        bool showOtherInput = toggleOther != null && toggleOther.isOn;

        if (otherInputRoot != null)
            otherInputRoot.SetActive(showOtherInput);
    }

    public void Show()
    {
        if (panelRoot == null)
            panelRoot = gameObject;

        panelRoot.SetActive(true);
        SetStreetLabelsHidden(true);

        RefreshOtherInputVisibility();
        ClearError();
    }

    public void Hide()
    {
        if (panelRoot == null)
            panelRoot = gameObject;

        SetStreetLabelsHidden(false);
        panelRoot.SetActive(false);
    }

    public void SubmitAnswer()
    {
        BuildStreetChoiceList();

        List<string> selectedCodes = new List<string>();
        List<string> selectedNames = new List<string>();

        foreach (StreetChoice choice in streetChoices)
        {
            if (choice.toggle != null && choice.toggle.isOn)
            {
                selectedCodes.Add(choice.code);
                selectedNames.Add(choice.englishName);
            }
        }

        bool otherSelected = toggleOther != null && toggleOther.isOn;
        string otherText = otherStreetInput != null
            ? (otherStreetInput.text ?? string.Empty).Trim()
            : string.Empty;

        if (otherSelected)
        {
            selectedCodes.Add("OTHER");
            selectedNames.Add("Other");
        }

        if (selectedCodes.Count == 0)
        {
            SetError("لطفاً حداقل یک خیابان را انتخاب کنید.");
            return;
        }

        if (otherSelected && string.IsNullOrWhiteSpace(otherText))
        {
            SetError("لطفاً نام خیابان را وارد کنید.");
            return;
        }

        ParticipantSession session = GetOrCreateParticipantSession();

        session.SetUsualParkingAnswer(
            string.Join("|", selectedCodes),
            string.Join("|", selectedNames),
            otherText
        );

        if (writeParticipantProfileCsv)
            SaveParticipantProfileCsv(session);

        ClearError();

        Debug.Log(
            "[UsualParkingQuestion] Saved answer for participant " +
            NormalizeParticipantId(session.participantId) +
            " | codes=" + session.usualParkingStreetCodes +
            " | names=" + session.usualParkingStreetNames
        );

        if (hidePanelAfterSubmit)
            Hide();

        onSubmitted?.Invoke();
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
        return sessionObject.AddComponent<ParticipantSession>();
    }

    private void SaveParticipantProfileCsv(ParticipantSession session)
    {
        try
        {
            session.EnsureSessionIdentity();

            string folder = Path.Combine(
                Application.persistentDataPath,
                string.IsNullOrWhiteSpace(outputSubfolder)
                    ? "ParkingSurveyData"
                    : outputSubfolder.Trim()
            );

            Directory.CreateDirectory(folder);

            string path = Path.Combine(
                folder,
                "participant_profiles.csv"
            );

            string participantId = NormalizeParticipantId(session.participantId);
            string sessionId = session.GetOrCreateSessionId();
            string studyVersion = ResolveStudyVersion();
            string buildVersion = ResolveBuildVersion();

            string[] header =
            {
                "ParticipantId",
                "SessionId",
                "StudyVersion",
                "BuildVersion",
                "UsualParkingStreetCodes",
                "UsualParkingStreetNames",
                "UsualParkingOtherText",
                "RecordedAtUtc"
            };

            List<string[]> rows = new List<string[]>();

            if (File.Exists(path))
            {
                string[] existingLines = File.ReadAllLines(path, Encoding.UTF8);

                for (int i = 1; i < existingLines.Length; i++)
                {
                    string line = existingLines[i];

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] columns = ParseCsvLine(line);

                    if (columns.Length == 0)
                        continue;

                    string existingParticipantId =
                        NormalizeParticipantId(columns[0]);

                    // Keep rows belonging to other participants.
                    if (existingParticipantId != participantId)
                        rows.Add(columns);
                }
            }

            rows.Add(new[]
            {
                participantId,
                sessionId,
                studyVersion,
                buildVersion,
                session.usualParkingStreetCodes ?? string.Empty,
                session.usualParkingStreetNames ?? string.Empty,
                session.usualParkingOtherText ?? string.Empty,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            });

            using (StreamWriter writer =
                   new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(ToCsvLine(header));

                foreach (string[] row in rows)
                    writer.WriteLine(ToCsvLine(row));
            }

            Debug.Log(
                "[UsualParkingQuestion] participant_profiles.csv saved at: " +
                path
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[UsualParkingQuestion] Could not save participant profile CSV: " +
                exception.Message
            );
        }
    }

    private static string ToCsvLine(IEnumerable<string> values)
    {
        List<string> escaped = new List<string>();

        foreach (string value in values)
            escaped.Add(EscapeCsv(value));

        return string.Join(",", escaped);
    }

    private static string EscapeCsv(string value)
    {
        string safe = value ?? string.Empty;

        if (safe.Contains("\""))
            safe = safe.Replace("\"", "\"\"");

        if (safe.Contains(",") ||
            safe.Contains("\"") ||
            safe.Contains("\n") ||
            safe.Contains("\r"))
        {
            safe = "\"" + safe + "\"";
        }

        return safe;
    }

    private static string[] ParseCsvLine(string line)
    {
        List<string> values = new List<string>();
        StringBuilder current = new StringBuilder();
        bool insideQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char character = line[i];

            if (character == '"')
            {
                if (insideQuotes &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (character == ',' && !insideQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        values.Add(current.ToString());
        return values.ToArray();
    }

    private void SetError(string message)
    {
        if (errorText != null)
            errorText.text = message ?? string.Empty;
        else
            Debug.LogWarning("[UsualParkingQuestion] " + message);
    }

    private void ClearError()
    {
        if (errorText != null)
            errorText.text = string.Empty;
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

    private static string NormalizeParticipantId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "UNKNOWN"
            : value.Trim().ToUpperInvariant();
    }
}
