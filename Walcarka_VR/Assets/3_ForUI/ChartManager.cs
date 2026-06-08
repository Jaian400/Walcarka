using UnityEngine;
using XCharts.Runtime;
using System.IO;
using UnityEngine.Profiling;

public class ChartManager : MonoBehaviour
{
    public LineChart chart;

    public enum MetricType { Velocity, Current, Torque }
    public MetricType selectedMetric;

    [Header("Staty Settings")]
    [SerializeField] private bool enableProfiling = true;

    private int pointsAddedThisFrame = 0;
    private double totalTimeThisFrameMs = 0;
    private long totalMemoryAllocatedThisFrame = 0;

    private string logFilePath;

    private void Start()
    {
        if (chart == null) chart = GetComponent<LineChart>();
        chart.series[0].animation.enable = false;
        chart.series[0].symbol.show = false;
        ConfigureAxes();

        if (enableProfiling)
        {
            logFilePath = Path.Combine(Application.persistentDataPath, "chart_profiler_results.csv");
            if (!File.Exists(logFilePath))
            {
                File.WriteAllText(logFilePath, "Timestamp;Metric;FrameCount;PointsAdded;ChartTime_ms;FrameTime_ms;ChartImpact_Percent;FPS;AllocatedMemory_B\n");
            }
            Debug.Log($"<color=cyan>[PROFILER]</color> Zapis logów wykresu ustawiony w: {logFilePath}");
        }
    }

    public void ConfigureAxes()
    {
        var xAxis = chart.GetChartComponent<XAxis>();
        var yAxis = chart.GetChartComponent<YAxis>();

        xAxis.animation.show = false;
        yAxis.animation.show = false;

        if (yAxis != null)
        {
            yAxis.minMaxType = Axis.AxisMinMaxType.Default;

            yAxis.splitNumber = 5; 
            yAxis.axisLabel.show = true;
            yAxis.axisLabel.textStyle.fontSize = 30;

            yAxis.axisName.name = GetAxisName();
        }

        if (xAxis != null)
        {
            xAxis.type = Axis.AxisType.Time;
            xAxis.axisLabel.show = true;
            xAxis.axisLabel.textStyle.fontSize = 30;

            xAxis.axisName.name = "Czas";
        }

        chart.RefreshChart();
    }

    private string GetAxisName()
    {
        switch (selectedMetric)
        {
            case MetricType.Velocity: return "Prêdkoœæ [m/min]";
            case MetricType.Current: return "Pr¹d [A]";
            case MetricType.Torque: return "Moment [kNm]";
            default: return "";
        }
    }

    private void OnEnable()
    {
        ConnectionServiceModern.OnDataReceived += UpdateChart;
    }

    private void OnDisable()
    {
        ConnectionServiceModern.OnDataReceived -= UpdateChart;
    }

    private void UpdateChart(RollerData newData)
    {
        if (chart == null) return;

        float valueToChart = 0f;
        switch (selectedMetric)
        {
            case MetricType.Velocity: valueToChart = newData.velocity; break;
            case MetricType.Current: valueToChart = newData.current; break;
            case MetricType.Torque: valueToChart = newData.torque; break;
        }

        if (enableProfiling)
        {
            long startMem = System.GC.GetTotalMemory(false);
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            chart.AddXAxisData(newData.time);
            chart.AddData(0, valueToChart);

            sw.Stop();
            long endMem = System.GC.GetTotalMemory(false);

            pointsAddedThisFrame++;
            totalTimeThisFrameMs += sw.Elapsed.TotalMilliseconds;

            long allocatedThisPoint = endMem - startMem;
            if (allocatedThisPoint > 0)
            {
                totalMemoryAllocatedThisFrame += allocatedThisPoint;
            }
        }
        else
        {
            chart.AddXAxisData(newData.time);
            chart.AddData(0, valueToChart);
        }
    }

    private void LateUpdate()
    {
        if (enableProfiling && pointsAddedThisFrame > 0)
        {
            float currentFPS = 1.0f / (Time.unscaledDeltaTime > 0 ? Time.unscaledDeltaTime : 0.001f);
            float frameTimeMs = Time.unscaledDeltaTime * 1000f;

            double chartImpactPercent = (totalTimeThisFrameMs / frameTimeMs) * 100.0;

            string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logLine = $"{timestamp};{selectedMetric};{Time.frameCount};{pointsAddedThisFrame};{totalTimeThisFrameMs:F4};{frameTimeMs:F4};{chartImpactPercent:F2};{currentFPS:F1};{totalMemoryAllocatedThisFrame / 1024f:F2}\n";

            try
            {
                File.AppendAllText(logFilePath, logLine);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"B³¹d zapisu profiler do pliku: {e.Message}");
            }

            Debug.Log($"<color=lime>[PROFILER WYKRESU]</color> Klatka Unity: <b>#{Time.frameCount}</b>\n" +
                      $"- Dodane punkty w tej klatce: <b>{pointsAddedThisFrame}</b>\n" +
                      $"- Czas CPU zu¿yty przez wykres: <b>{totalTimeThisFrameMs:F3} ms</b> (Zu¿ywa: <color=yellow><b>{chartImpactPercent:F1}%</b></color> czasu klatki)\n" +
                      $"- Ca³kowity czas klatki: <b>{frameTimeMs:F1} ms</b> (P³ynnoœæ gry: <b>{currentFPS:F1} FPS</b>)\n" +
                      $"- Zaalokowana pamiêæ sterty (RAM): <b>{totalMemoryAllocatedThisFrame:F2} KB</b>");

            pointsAddedThisFrame = 0;
            totalTimeThisFrameMs = 0;
            totalMemoryAllocatedThisFrame = 0;
        }

    }
}