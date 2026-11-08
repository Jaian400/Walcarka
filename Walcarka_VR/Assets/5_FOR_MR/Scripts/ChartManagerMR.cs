using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using XCharts.Runtime;

[DisallowMultipleComponent]
public sealed class ChartManagerMR : MonoBehaviour
{
    public LineChart chart;
    public enum ChartMode { Line = 0, LatestBars = 1, Scatter = 2 }
    public ChartMode mode;
    public BarChart barChart;
    public ScatterChart scatterChart;
    public MetricType scatterXMetric = MetricType.Current;
    public MetricType scatterYMetric = MetricType.Torque;
    [Tooltip("Speed magnitude at or below this value is the near-zero operating state (m/min).")]
    [SerializeField, Min(0f)] private float speedDeadband = 1f;
    public enum MetricType
    {
        Velocity = 0, Current = 1, Torque = 2, Load = 3,
        ForceRight = 4, ForceLeft = 5, PositionRight = 6, PositionLeft = 7,
        TemperatureIn = 8, TemperatureOut = 9, TotalForce = 10,
        ForceDifference = 11, ForceImbalancePercent = 12, PositionDifference = 13,
        OperatingState = 14
    }
    public MetricType selectedMetric;

    [Serializable]
    public sealed class AdditionalSeries
    {
        public MetricType metric;
        public string displayName;
        public TMP_Text latestValueLabel;
    }

    [Tooltip("Optional label for the original/first series.")]
    [SerializeField] private TMP_Text latestValueLabel = null;
    [Tooltip("Optional signed right-minus-left force label in Latest Bars mode.")]
    [SerializeField] private TMP_Text forceDifferenceLabel = null;
    [Tooltip("Extra series on the same Y axis; select metrics with the same unit as Selected Metric.")]
    [SerializeField] private AdditionalSeries[] additionalSeries = Array.Empty<AdditionalSeries>();
    [Tooltip("Break the line when the source interval exceeds this many seconds; zero disables gap detection.")]
    [SerializeField, Min(0f)] private float gapThresholdSeconds = 0.5f;

    [Tooltip("Optional for prefab pages: if empty, resolves the single active StationDataBuffer at runtime.")]
    [SerializeField] private StationDataBuffer dataBuffer;

    private readonly List<StationDataSample> snapshot = new List<StationDataSample>();
    private StationDataBuffer subscribedBuffer;
    private bool started;
    private bool chartInitialized;
    private BaseChart activeChart;
    private ChartMode renderedMode;
    private MetricType renderedXMetric;
    private float renderedDeadband;
    private long lastRenderedSequence;
    private readonly List<MetricType> renderedMetrics = new List<MetricType>();
    private readonly Queue<int> pointsPerSample = new Queue<int>();
    private long previousTimeMilliseconds;
    private bool hasPreviousTime;

    public long LastRenderedSequence => lastRenderedSequence;

    private void Start()
    {
        started = true;
        RefreshFromBuffer();
    }

    private void OnEnable()
    {
        if (started) RefreshFromBuffer();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void BindDataBuffer(StationDataBuffer buffer)
    {
        Unsubscribe();
        dataBuffer = buffer;
        lastRenderedSequence = 0;
        if (started && isActiveAndEnabled) RefreshFromBuffer();
    }

    public void RefreshFromBuffer()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !started) return;
        if (!InitializeChart() || !ResolveBuffer() || !ConfigureSeries())
        {
            Unsubscribe();
            return;
        }

        if (subscribedBuffer != dataBuffer)
        {
            Unsubscribe();
            subscribedBuffer = dataBuffer;
            subscribedBuffer.SampleAdded += OnSampleAdded;
            subscribedBuffer.HistoryReset += OnHistoryReset;
        }

        activeChart.SetMaxCache(0);
        activeChart.ClearData();
        ConfigureAxes();
        lastRenderedSequence = 0;
        hasPreviousTime = false;
        pointsPerSample.Clear();
        SetLatestLabel(latestValueLabel, renderedMetrics[0], false, 0f);
        SetLatestLabel(forceDifferenceLabel, MetricType.ForceDifference, false, 0f);
        if (mode == ChartMode.Line && additionalSeries != null)
            foreach (AdditionalSeries entry in additionalSeries)
                SetLatestLabel(entry.latestValueLabel, entry.metric, false, 0f);
        dataBuffer.CopySnapshotTo(snapshot);
        if (mode == ChartMode.LatestBars)
        {
            if (snapshot.Count > 0) AppendSample(snapshot[snapshot.Count - 1]);
        }
        else foreach (StationDataSample sample in snapshot) AppendSample(sample);
        snapshot.Clear();
        activeChart.RefreshChart();
    }

    private bool InitializeChart()
    {
        switch (mode)
        {
            case ChartMode.Line:
                if (chart == null) chart = GetComponentInChildren<LineChart>(true);
                activeChart = chart;
                break;
            case ChartMode.LatestBars:
                if (barChart == null) barChart = GetComponentInChildren<BarChart>(true);
                activeChart = barChart;
                break;
            case ChartMode.Scatter:
                if (scatterChart == null) scatterChart = GetComponentInChildren<ScatterChart>(true);
                activeChart = scatterChart;
                break;
            default: activeChart = null; break;
        }
        chartInitialized = activeChart != null;
        if (!chartInitialized)
        {
            Debug.LogError($"[{nameof(ChartManagerMR)}] Assign a chart matching mode {mode}.", this);
            return false;
        }
        return true;
    }

    private bool ConfigureSeries()
    {
        renderedMetrics.Clear();
        MetricType firstMetric = mode == ChartMode.LatestBars ? MetricType.ForceRight :
            mode == ChartMode.Scatter ? scatterYMetric : selectedMetric;
        if (!Enum.IsDefined(typeof(MetricType), firstMetric) ||
            (mode == ChartMode.Scatter && !Enum.IsDefined(typeof(MetricType), scatterXMetric))) return false;
        renderedMetrics.Add(firstMetric);
        if (mode == ChartMode.Line && additionalSeries != null)
        {
            foreach (AdditionalSeries entry in additionalSeries)
            {
                if (entry == null || !Enum.IsDefined(typeof(MetricType), entry.metric) ||
                    GetMetricUnit(entry.metric) != GetMetricUnit(selectedMetric))
                {
                    Debug.LogError($"[{nameof(ChartManagerMR)}] All configured series must be valid metrics with the same unit.", this);
                    return false;
                }
                renderedMetrics.Add(entry.metric);
            }
        }
        int seriesCount = renderedMetrics.Count;
        while (activeChart.series.Count < seriesCount)
        {
            Serie added = mode == ChartMode.LatestBars ? (Serie)activeChart.AddSerie<Bar>() :
                mode == ChartMode.Scatter ? (Serie)activeChart.AddSerie<Scatter>() : activeChart.AddSerie<Line>();
            if (added == null) return false;
        }
        for (int i = 0; i < activeChart.series.Count; i++)
        {
            var serie = activeChart.series[i];
            serie.show = i < seriesCount;
            if (!serie.show) continue;
            if ((mode == ChartMode.Line && !(serie is Line)) ||
                (mode == ChartMode.LatestBars && !(serie is Bar)) ||
                (mode == ChartMode.Scatter && !(serie is Scatter)))
            {
                Debug.LogError($"[{nameof(ChartManagerMR)}] Series type must match chart mode {mode}.", this);
                return false;
            }
            serie.animation.enable = false;
            serie.symbol.show = mode == ChartMode.Scatter;
            serie.ignoreLineBreak = true;
            serie.stack = string.Empty;
            serie.serieName = i > 0 && !string.IsNullOrWhiteSpace(additionalSeries[i - 1].displayName)
                ? additionalSeries[i - 1].displayName : GetMetricName(renderedMetrics[i]);
            if (mode == ChartMode.LatestBars)
            {
                serie.serieName = "Siła nacisku";
                serie.colorBy = SerieColorBy.Data;
                serie.barWidth = 0.4f;
                serie.barMaxWidth = 90f;
            }
            if (mode == ChartMode.Line && firstMetric == MetricType.OperatingState)
                serie.lineType = LineType.StepEnd;
        }
        if (renderedMetrics.Count > 1) activeChart.EnsureChartComponent<Legend>().show = true;
        else
        {
            var legend = activeChart.GetChartComponent<Legend>();
            if (legend != null) legend.show = false;
        }
        renderedMode = mode;
        renderedXMetric = scatterXMetric;
        renderedDeadband = speedDeadband;
        return true;
    }

    private bool ResolveBuffer()
    {
        if (dataBuffer == null)
        {
            StationDataBuffer[] candidates = FindObjectsByType<StationDataBuffer>(FindObjectsSortMode.None);
            StationDataBuffer onlyActive = null;
            int activeCount = 0;
            foreach (StationDataBuffer candidate in candidates)
            {
                if (!candidate.isActiveAndEnabled) continue;
                onlyActive = candidate;
                activeCount++;
            }
            if (activeCount != 1)
            {
                Debug.LogError($"[{nameof(ChartManagerMR)}] Expected one active StationDataBuffer, found {activeCount}. Assign one explicitly using BindDataBuffer if there are multiple.", this);
                return false;
            }
            dataBuffer = onlyActive;
        }

        if (!dataBuffer.isActiveAndEnabled)
        {
            Debug.LogError($"[{nameof(ChartManagerMR)}] The data buffer must stay active outside the toggled panel.", this);
            return false;
        }
        return true;
    }

    private void OnSampleAdded(StationDataSample sample)
    {
        if (!isActiveAndEnabled || !chartInitialized) return;
        if (ConfigurationChanged())
        {
            RefreshFromBuffer();
            return;
        }
        AppendSample(sample);
    }

    private void OnHistoryReset()
    {
        RefreshFromBuffer();
    }

    private void AppendSample(StationDataSample sample)
    {
        if (sample.Sequence <= lastRenderedSequence) return;
        if (mode == ChartMode.LatestBars)
        {
            activeChart.series[0].ClearData();
            foreach (MetricType metric in new[] { MetricType.ForceRight, MetricType.ForceLeft })
            {
                bool available = TryGetMetricValue(sample, metric, out float force);
                activeChart.AddData(0, available ? force : 0d, GetMetricName(metric)).ignore = !available;
            }
            SetLatestLabel(latestValueLabel, MetricType.ForceRight,
                TryGetMetricValue(sample, MetricType.ForceRight, out float right), right);
            SetLatestLabel(forceDifferenceLabel, MetricType.ForceDifference,
                TryGetMetricValue(sample, MetricType.ForceDifference, out float difference), difference);
            lastRenderedSequence = sample.Sequence;
            return;
        }
        while (pointsPerSample.Count >= dataBuffer.Capacity)
        {
            int removeCount = pointsPerSample.Dequeue();
            for (int i = 0; i < renderedMetrics.Count; i++)
                for (int j = 0; j < removeCount; j++) activeChart.series[i].RemoveData(0);
        }

        long delta = sample.TimeMilliseconds - previousTimeMilliseconds;
        bool gap = mode == ChartMode.Line && hasPreviousTime && (delta < 0 ||
            (gapThresholdSeconds > 0f && delta > gapThresholdSeconds * 1000d));
        double x = sample.TimeMilliseconds / 1000d;
        for (int i = 0; i < renderedMetrics.Count; i++)
        {
            if (gap)
            {
                SerieData marker = activeChart.AddData(i, x, 0d, "Recording gap");
                marker.ignore = true;
            }
            bool available = TryGetMetricValue(sample, renderedMetrics[i], out float value, speedDeadband);
            double pointX = x;
            if (mode == ChartMode.Scatter)
            {
                bool xAvailable = TryGetMetricValue(sample, scatterXMetric, out float xValue, speedDeadband);
                available &= xAvailable;
                pointX = xAvailable ? xValue : 0d;
            }
            SerieData point = activeChart.AddData(i, pointX, available ? value : 0d, sample.Time);
            point.ignore = !available;
            SetLatestLabel(i == 0 ? latestValueLabel : additionalSeries[i - 1].latestValueLabel,
                renderedMetrics[i], available, value);
        }
        pointsPerSample.Enqueue(gap ? 2 : 1);
        previousTimeMilliseconds = sample.TimeMilliseconds;
        hasPreviousTime = true;
        lastRenderedSequence = sample.Sequence;
    }

    private bool ConfigurationChanged()
    {
        if (renderedMode != mode || renderedDeadband != speedDeadband) return true;
        if (mode == ChartMode.LatestBars) return false;
        if (mode == ChartMode.Scatter)
            return renderedXMetric != scatterXMetric || renderedMetrics[0] != scatterYMetric;
        int count = 1 + (additionalSeries == null ? 0 : additionalSeries.Length);
        if (renderedMetrics.Count != count || renderedMetrics[0] != selectedMetric) return true;
        for (int i = 1; i < count; i++)
            if (additionalSeries[i - 1] == null || renderedMetrics[i] != additionalSeries[i - 1].metric)
                return true;
        return false;
    }

    private static void SetLatestLabel(TMP_Text label, MetricType metric, bool available, float value)
    {
        if (label != null)
            label.text = available
                ? value.ToString("0.###", CultureInfo.CurrentCulture) + " " + GetMetricUnit(metric)
                : "No data";
    }

    public static bool TryGetMetricValue(StationDataSample sample, MetricType metric, out float value,
        float deadband = 1f)
    {
        switch (metric)
        {
            case MetricType.Velocity: value = sample.Velocity; break;
            case MetricType.Current: value = sample.Current; break;
            case MetricType.Torque: value = sample.Torque; break;
            case MetricType.OperatingState:
                if (float.IsNaN(sample.Velocity) || float.IsInfinity(sample.Velocity))
                { value = float.NaN; return false; }
                value = Math.Abs(sample.Velocity) <= Math.Max(0f, deadband) ? 0f :
                    sample.Velocity < 0f ? -1f : 1f;
                break;
            default:
                if (!sample.HasAdditionalData) { value = float.NaN; return false; }
                switch (metric)
                {
                    case MetricType.Load: value = sample.Load; break;
                    case MetricType.ForceRight: value = sample.ForceRight; break;
                    case MetricType.ForceLeft: value = sample.ForceLeft; break;
                    case MetricType.PositionRight: value = sample.PositionRight; break;
                    case MetricType.PositionLeft: value = sample.PositionLeft; break;
                    case MetricType.TemperatureIn: value = sample.TemperatureIn; break;
                    case MetricType.TemperatureOut: value = sample.TemperatureOut; break;
                    case MetricType.TotalForce: value = sample.TotalForce; break;
                    case MetricType.ForceDifference: value = sample.ForceDifference; break;
                    case MetricType.ForceImbalancePercent: value = sample.ForceImbalancePercent; break;
                    case MetricType.PositionDifference: value = sample.PositionDifference; break;
                    default: value = float.NaN; break;
                }
                break;
        }
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static string GetMetricUnit(MetricType metric)
    {
        switch (metric)
        {
            case MetricType.Velocity: return "m/min";
            case MetricType.Current: return "A";
            case MetricType.Torque: return "kNm";
            case MetricType.Load:
            case MetricType.ForceImbalancePercent: return "%";
            case MetricType.ForceRight:
            case MetricType.ForceLeft:
            case MetricType.TotalForce:
            case MetricType.ForceDifference: return "kN";
            case MetricType.PositionRight:
            case MetricType.PositionLeft:
            case MetricType.PositionDifference: return "mm";
            case MetricType.TemperatureIn:
            case MetricType.TemperatureOut: return "°C";
            default: return string.Empty;
        }
    }

    public static string GetMetricName(MetricType metric)
    {
        switch (metric)
        {
            case MetricType.Velocity: return "Prędkość";
            case MetricType.Current: return "Prąd";
            case MetricType.Torque: return "Moment";
            case MetricType.Load: return "Obciążenie";
            case MetricType.ForceRight: return "Siła prawa";
            case MetricType.ForceLeft: return "Siła lewa";
            case MetricType.PositionRight: return "Nastawa prawa";
            case MetricType.PositionLeft: return "Nastawa lewa";
            case MetricType.TemperatureIn: return "Temperatura przed";
            case MetricType.TemperatureOut: return "Temperatura za";
            case MetricType.TotalForce: return "Siła całkowita";
            case MetricType.ForceDifference: return "Siła prawa - lewa";
            case MetricType.ForceImbalancePercent: return "Nierównowaga sił";
            case MetricType.PositionDifference: return "Nastawa lewa - prawa";
            case MetricType.OperatingState: return "Stan ruchu";
            default: return metric.ToString();
        }
    }

    public void ConfigureAxes()
    {
        if (activeChart == null) return;
        var xAxis = activeChart.GetChartComponent<XAxis>();
        var yAxis = activeChart.GetChartComponent<YAxis>();
        if (xAxis != null)
        {
            xAxis.animation.show = false;
            xAxis.type = Axis.AxisType.Value;
            xAxis.boundaryGap = false;
            xAxis.minMaxType = mode == ChartMode.LatestBars ? Axis.AxisMinMaxType.Default : Axis.AxisMinMaxType.MinMax;
            xAxis.axisLabel.formatterFunction = (index, value, category, content) =>
                TimeSpan.FromSeconds(Math.Max(0d, value)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            xAxis.axisLabel.show = true;
            xAxis.axisLabel.textStyle.fontSize = 30;
            xAxis.axisName.name = "Czas";
            if (mode != ChartMode.Line)
            {
                xAxis.axisLabel.formatterFunction = null;
                xAxis.axisName.name = mode == ChartMode.LatestBars ? "Siła [kN]" : AxisTitle(scatterXMetric);
                xAxis.axisLabel.numericFormatter = "F2";
            }
            if (mode == ChartMode.LatestBars)
            {
                xAxis.splitNumber = 5;
                xAxis.axisLine.onZero = false;
                xAxis.axisName.onZero = false;
                xAxis.axisName.labelStyle.position = LabelStyle.Position.Middle;
                xAxis.axisName.labelStyle.offset = new Vector3(0f, -75f, 0f);
            }
        }
        if (yAxis != null)
        {
            yAxis.animation.show = false;
            yAxis.minMaxType = Axis.AxisMinMaxType.Default;
            yAxis.splitNumber = 5;
            yAxis.axisLabel.show = true;
            yAxis.axisLabel.textStyle.fontSize = 30;
            yAxis.type = Axis.AxisType.Value;
            yAxis.axisName.name = (renderedMetrics.Count > 1 ? "Wartość" : GetMetricName(selectedMetric)) +
                " [" + GetMetricUnit(selectedMetric) + "]";
            yAxis.axisLabel.formatterFunction = null;
            if (mode == ChartMode.LatestBars)
            {
                yAxis.type = Axis.AxisType.Category;
                yAxis.boundaryGap = true;
                yAxis.axisLine.onZero = false;
                yAxis.axisName.name = string.Empty;
                yAxis.ClearData();
                activeChart.AddYAxisData("Prawa");
                activeChart.AddYAxisData("Lewa");
            }
            else if (mode == ChartMode.Scatter)
            {
                yAxis.minMaxType = Axis.AxisMinMaxType.MinMax;
                yAxis.axisLabel.numericFormatter = "F2";
                yAxis.axisName.name = AxisTitle(scatterYMetric);
            }
            else if (selectedMetric == MetricType.OperatingState)
            {
                yAxis.minMaxType = Axis.AxisMinMaxType.Custom;
                yAxis.min = -1;
                yAxis.max = 1;
                yAxis.splitNumber = 2;
                yAxis.axisName.name = GetMetricName(selectedMetric);
                yAxis.axisLabel.formatterFunction = (index, value, category, content) =>
                    value < -0.5d ? "Ujemny (-1)" : value > 0.5d ? "Dodatni (+1)" : "Blisko zera (0)";
            }
        }
        activeChart.RefreshChart();
    }

    private static string AxisTitle(MetricType metric)
    {
        string unit = GetMetricUnit(metric);
        return GetMetricName(metric) + (unit.Length == 0 ? string.Empty : " [" + unit + "]");
    }

    private void Unsubscribe()
    {
        if (subscribedBuffer != null)
        {
            subscribedBuffer.SampleAdded -= OnSampleAdded;
            subscribedBuffer.HistoryReset -= OnHistoryReset;
        }
        subscribedBuffer = null;
    }
}
