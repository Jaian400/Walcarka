using UnityEngine;
using TMPro;

public class FPSDisplay : MonoBehaviour
{
    private TextMeshProUGUI fpsText;
    private int lastDisplayedFPS = -1; 

    private void Awake()
    {
        fpsText = GetComponent<TextMeshProUGUI>();
    }

    private void Update()
    {
        if (FPSCounter.CurrentFPS != lastDisplayedFPS)
        {
            lastDisplayedFPS = FPSCounter.CurrentFPS;
            UpdateUI(lastDisplayedFPS);
        }
    }

    private void UpdateUI(int fps)
    {
        if (fpsText == null) return;

        fpsText.text = $"{fps}";

        if (fps >= 70)
            fpsText.color = Color.green;
        else if (fps >= 60)
            fpsText.color = Color.yellow;
        else
            fpsText.color = Color.red;
    }
}