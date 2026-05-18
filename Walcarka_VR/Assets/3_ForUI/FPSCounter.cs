using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    public static int CurrentFPS { get; private set; }

    [Header("Settings")]
    public float updateInterval = 0.5f;

    private float timer;
    private int frameCount;

    private void Start()
    {
        timer = updateInterval;
    }

    private void Update()
    {
        frameCount++;
        timer -= Time.unscaledDeltaTime;

        if (timer <= 0f)
        {
            float fps = frameCount / updateInterval;
            CurrentFPS = Mathf.RoundToInt(fps);

            frameCount = 0;
            timer = updateInterval;
        }
    }
}