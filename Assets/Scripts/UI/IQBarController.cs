using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class IQBarController : MonoBehaviour
{
    [Header("--- UI REFERENCES ---")]
    [Tooltip("Drag the Slider component here — displays this device's current IQ (read-only).")]
    public Slider iqSlider;

    [Tooltip("(Optional) Text that shows the current IQ number next to the bar.")]
    public TMP_Text iqValueText;

    [Tooltip("(Optional) Text that shows what difficulty level the current IQ maps to (e.g. 'Level 3').")]
    public TMP_Text levelText;

    [Header("--- RANGE (display only) ---")]
    public float sliderMin = 40f;
    public float sliderMax = 180f;

    private void Awake()
    {
        if (iqSlider == null)
            iqSlider = GetComponent<Slider>();

        if (iqSlider != null)
        {
            iqSlider.minValue = sliderMin;
            iqSlider.maxValue = sliderMax;
            iqSlider.wholeNumbers = false;
            iqSlider.interactable = false;
        }
    }

    private void OnEnable()
    {
        RefreshFromManager();
    }

    public void RefreshFromManager()
    {
        if (PlayerIQManager.Instance == null)
            return;

        float savedIQ = PlayerIQManager.Instance.GetLocalIQ();

        if (iqSlider != null)
            iqSlider.SetValueWithoutNotify(Mathf.Clamp(savedIQ, iqSlider.minValue, iqSlider.maxValue));

        UpdateLabels(savedIQ);
    }

    private void UpdateLabels(float iq)
    {
        if (iqValueText != null)
            iqValueText.text = iq.ToString("F2");

        if (levelText != null && PlayerIQManager.Instance != null)
            levelText.text = "Nível " + PlayerIQManager.Instance.GetLocalDifficultyLevel();
    }
}
