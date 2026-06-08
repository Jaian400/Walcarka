using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.IO;

public class PlotImageViewer : MonoBehaviour
{
    public ConnectionServiceModern connectionService;
    public RawImage plotDisplay;

    public string defaultFilename = "wykres.png";

    [Header("Profiling Settings")]
    [SerializeField] private bool enableProfiling = true;
    private string logFilePath;

    void Start()
    {
        if (enableProfiling)
        {
            logFilePath = Path.Combine(Application.persistentDataPath, "image_transfer_results.csv");
            if (!File.Exists(logFilePath))
            {
                File.WriteAllText(logFilePath, "Timestamp;ImageName;TotalTime_ms;FramesInBg;AvgFPS;AllocatedMemory_KB\n");
            }
            Debug.Log($"<color=cyan>[PROFILER IMAGE]</color> Zapis logów obrazu ustawiony w: {logFilePath}");
        }
    }

    public async void LoadPlotFromServer()
    {
        if (connectionService == null || plotDisplay == null) return;

        long startMem = System.GC.GetTotalMemory(false);
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        float startTime = Time.realtimeSinceStartup;
        int startFrame = Time.frameCount;

        Texture2D newTexture = await connectionService.DownloadPlotImageAsync(defaultFilename);

        sw.Stop();
        float elapsedSeconds = Time.realtimeSinceStartup - startTime;
        int framesElapsed = Time.frameCount - startFrame;
        float avgFps = framesElapsed / (elapsedSeconds > 0 ? elapsedSeconds : 0.001f);

        if (newTexture != null)
        {
            Texture2D oldTexture = plotDisplay.texture as Texture2D;
            plotDisplay.texture = newTexture;

            if (oldTexture != null)
                Destroy(oldTexture);

            long endMem = System.GC.GetTotalMemory(false);
            long allocatedBytes = endMem - startMem;

            if (enableProfiling)
            {
                string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logLine = $"{timestamp};{defaultFilename};{sw.ElapsedMilliseconds};{framesElapsed};{avgFps:F1};{allocatedBytes / 1024f:F2}\n";

                try
                {
                    File.AppendAllText(logFilePath, logLine);
                    Debug.Log($"<color=cyan>[PROFILER IMAGE]</color> Pomyœlnie za³adowano i oprofilowano obraz: {defaultFilename}\n" +
                              $"- Czas operacji: <b>{sw.ElapsedMilliseconds} ms</b>\n" +
                              $"- Klatki wyrenderowane w tle: <b>{framesElapsed}</b> (Œr. FPS: <b>{avgFps:F1}</b>)\n" +
                              $"- Alokacja RAM: <b>{allocatedBytes / 1024f:F2} KB</b>");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"B³¹d zapisu transferu obrazu do pliku: {e.Message}");
                }
            }
        }
    }
}