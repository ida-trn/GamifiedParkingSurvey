using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// Three-position speed selector for Conventional driving.
/// The Unity Slider is intentionally configured as 0 / 1 / 2, so clicking
/// or dragging anywhere on the track snaps to the nearest speed: 30/40/50 km/h.
///
/// This component owns only the UI/state. Other gameplay code can read
/// CurrentSpeedKmh / CurrentSpeedMetersPerSecond or subscribe to SpeedChanged.
/// </summary>
[DisallowMultipleComponent]
public class SpeedSelectorUI : MonoBehaviour
{
    [Header("Required UI")]
    [SerializeField] private Slider speedSlider;
    [FormerlySerializedAs("currentSpeedText")]
    [SerializeField] private TMP_Text speedTitle;

    [Header("Three fixed markers")]
    [SerializeField] private Image dot30;
    [SerializeField] private Image dot40;
    [SerializeField] private Image dot50;

    [Header("Optional handle visual")]
    [Tooltip("Optional Image on the Slider Handle. Leave empty if you do not want the script to recolor it.")]
    [SerializeField] private Image handleImage;

    [Header("Behaviour")]
    [Tooltip("The selector always uses exactly these three speeds for the study UI.")]
    [SerializeField] private int defaultSpeedKmh = 50;

    [Tooltip("Show Persian digits in the speed label.")]
    [SerializeField] private bool usePersianDigits = true;

    [Tooltip("Enlarge the selected fixed marker slightly.")]
    [SerializeField] private float selectedDotScale = 1.25f;

    [Header("Colors")]
    [SerializeField] private Color selectedColor = new Color32(37, 99, 235, 255);
    [SerializeField] private Color normalColor = new Color32(210, 215, 222, 255);

    private static readonly int[] SpeedOptions = { 30, 40, 50 };

    public int CurrentSpeedKmh { get; private set; } = 50;
    public float CurrentSpeedMetersPerSecond => CurrentSpeedKmh / 3.6f;
    public int SelectedIndex { get; private set; } = 2;
    public int DefaultSpeedKmh => defaultSpeedKmh;
    public bool IsUserInteractable => speedSlider != null && speedSlider.interactable;

    /// <summary>
    /// Raised whenever the participant actually changes the selected speed.
    /// The value is km/h (30, 40, or 50). ParkingLogger subscribes to this
    /// during Conventional rounds so only change events are stored rather than
    /// high-frequency slider samples.
    /// </summary>
    public event Action<int> SpeedChanged;

    private void Awake()
    {
        ConfigureSlider();
        ConfigureMarkerRaycasts();
        ResetToDefault(notify: false);
    }

    private void OnEnable()
    {
        // Keeps the visual state correct if the Conventional driving controls
        // are hidden for Assisted mode and later shown again.
        RefreshVisuals();
    }

    private void OnDestroy()
    {
        if (speedSlider != null)
            speedSlider.onValueChanged.RemoveListener(OnSliderChanged);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        selectedDotScale = Mathf.Max(1f, selectedDotScale);

        // Keep invalid Inspector values from becoming a fourth experimental
        // speed. The nearest of 30/40/50 is used.
        defaultSpeedKmh = GetNearestSpeed(defaultSpeedKmh);

        if (!Application.isPlaying)
        {
            ConfigureSlider(addRuntimeListener: false);
            ConfigureMarkerRaycasts();
        }
    }
#endif

    private void ConfigureSlider(bool addRuntimeListener = true)
    {
        if (speedSlider == null)
            return;

        // Slider indices, not km/h values. This guarantees equal visual spacing.
        speedSlider.minValue = 0f;
        speedSlider.maxValue = 2f;
        speedSlider.wholeNumbers = true;

        if (addRuntimeListener)
        {
            speedSlider.onValueChanged.RemoveListener(OnSliderChanged);
            speedSlider.onValueChanged.AddListener(OnSliderChanged);
        }
    }

    private void ConfigureMarkerRaycasts()
    {
        // The fixed dots and any labels placed under them are visual only.
        // Disabling every Graphic in each marker subtree lets clicks pass
        // through to the Slider track underneath, including clicks directly
        // on the 30 / 40 / 50 labels.
        DisableMarkerRaycasts(dot30);
        DisableMarkerRaycasts(dot40);
        DisableMarkerRaycasts(dot50);
    }

    private static void DisableMarkerRaycasts(Image marker)
    {
        if (marker == null)
            return;

        Graphic[] graphics = marker.GetComponentsInChildren<Graphic>(true);

        foreach (Graphic graphic in graphics)
        {
            if (graphic != null)
                graphic.raycastTarget = false;
        }
    }

    private void OnSliderChanged(float rawValue)
    {
        int index = Mathf.Clamp(Mathf.RoundToInt(rawValue), 0, SpeedOptions.Length - 1);
        ApplyIndex(index, notify: true);
    }

    /// <summary>
    /// Selects one of the three positions: 0=30, 1=40, 2=50.
    /// </summary>
    public void SetSpeedByIndex(int index)
    {
        ApplyIndex(Mathf.Clamp(index, 0, SpeedOptions.Length - 1), notify: true);
    }

    /// <summary>
    /// Accepts any requested km/h value but intentionally snaps it to the
    /// nearest study speed (30/40/50).
    /// </summary>
    public void SetSpeedKmh(int speedKmh)
    {
        SetSpeedByIndex(GetNearestIndex(speedKmh));
    }

    /// <summary>
    /// Resets to the Inspector default (normally 50 km/h).
    /// ScenarioManager uses this at the beginning of a new Conventional round.
    /// </summary>
    public void ResetToDefault()
    {
        ResetToDefault(notify: true);
    }

    public void ResetToDefault(bool notify)
    {
        ApplyIndex(GetNearestIndex(defaultSpeedKmh), notify);
    }

    /// <summary>
    /// Enables/disables user interaction without hiding the control.
    /// </summary>
    public void SetInteractable(bool interactable)
    {
        if (speedSlider != null)
            speedSlider.interactable = interactable;
    }

    private void ApplyIndex(int index, bool notify)
    {
        index = Mathf.Clamp(index, 0, SpeedOptions.Length - 1);

        int oldSpeed = CurrentSpeedKmh;

        SelectedIndex = index;
        CurrentSpeedKmh = SpeedOptions[index];

        if (speedSlider != null && !Mathf.Approximately(speedSlider.value, index))
            speedSlider.SetValueWithoutNotify(index);

        RefreshVisuals();

        if (notify && oldSpeed != CurrentSpeedKmh)
            SpeedChanged?.Invoke(CurrentSpeedKmh);
    }

    private void RefreshVisuals()
    {
        if (speedTitle != null)
        {
            string speed = CurrentSpeedKmh.ToString();
            if (usePersianDigits)
                speed = ToPersianDigits(speed);

            // The title and current value now live in the same TMP/RTL text object.
            speedTitle.text = $"سرعت: {speed} کیلومتر/ساعت";
            speedTitle.SetVerticesDirty();
            speedTitle.SetLayoutDirty();

            if (speedTitle.gameObject.activeInHierarchy)
                speedTitle.ForceMeshUpdate(true, true);
        }

        UpdateDot(dot30, SelectedIndex == 0);
        UpdateDot(dot40, SelectedIndex == 1);
        UpdateDot(dot50, SelectedIndex == 2);

        if (handleImage != null)
            handleImage.color = selectedColor;
    }

    private void UpdateDot(Image dot, bool selected)
    {
        if (dot == null)
            return;

        dot.color = selected ? selectedColor : normalColor;
        dot.rectTransform.localScale = Vector3.one * (selected ? selectedDotScale : 1f);
    }

    private static int GetNearestIndex(int speedKmh)
    {
        int bestIndex = 0;
        int bestDistance = Mathf.Abs(speedKmh - SpeedOptions[0]);

        for (int i = 1; i < SpeedOptions.Length; i++)
        {
            int distance = Mathf.Abs(speedKmh - SpeedOptions[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static int GetNearestSpeed(int speedKmh)
    {
        return SpeedOptions[GetNearestIndex(speedKmh)];
    }

    private static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        char[] western = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        char[] persian = { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };

        char[] characters = input.ToCharArray();

        for (int i = 0; i < characters.Length; i++)
        {
            char c = characters[i];
            for (int digit = 0; digit < western.Length; digit++)
            {
                if (c == western[digit])
                {
                    characters[i] = persian[digit];
                    break;
                }
            }
        }

        return new string(characters);
    }
}
