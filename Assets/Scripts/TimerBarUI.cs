using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TimerBarUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;

    [Tooltip("Optional. Leave this empty when ScenarioManager already controls TimerLabel.")]
    [SerializeField] private TMP_Text timeText;

    public float MaxTimeSeconds { get; private set; } = 300f;
    public float RemainingTimeSeconds { get; private set; } = 300f;

    public void Initialize(float maxTimeSeconds)
    {
        MaxTimeSeconds = Mathf.Max(1f, maxTimeSeconds);
        RemainingTimeSeconds = MaxTimeSeconds;
        RefreshUI();
    }

    public void SetRemainingTime(float seconds)
    {
        RemainingTimeSeconds = Mathf.Clamp(seconds, 0f, MaxTimeSeconds);
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (fillImage != null)
        {
            float ratio = RemainingTimeSeconds / MaxTimeSeconds;
            fillImage.fillAmount = ratio;

            // > 2:00 = green
            // 1:00 - 2:00 = yellow
            // 0:30 - 0:59 = orange
            // < 0:30 = red
            if (RemainingTimeSeconds > 120f)
            {
                fillImage.color = new Color(0.20f, 0.70f, 0.30f);
            }
            else if (RemainingTimeSeconds >= 60f)
            {
                fillImage.color = new Color(0.95f, 0.80f, 0.20f);
            }
            else if (RemainingTimeSeconds >= 30f)
            {
                fillImage.color = new Color(1.00f, 0.55f, 0.10f);
            }
            else
            {
                fillImage.color = new Color(0.85f, 0.20f, 0.20f);
            }
        }

        if (timeText != null)
        {
            int totalSeconds = Mathf.CeilToInt(RemainingTimeSeconds);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            timeText.text = $"{minutes}:{seconds:00}";
        }
    }
}