using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StationOverviewPanelMR : MonoBehaviour
{
    [Tooltip("Optional for prefab pages: resolves the single active buffer when unassigned.")]
    [SerializeField] private StationDataBuffer dataBuffer;
    [SerializeField] private TMP_Text velocityLabel = null;
    [SerializeField] private TMP_Text currentLabel = null;
    [SerializeField] private TMP_Text torqueLabel = null;
    [SerializeField] private TMP_Text loadLabel = null;
    [SerializeField] private TMP_Text totalForceLabel = null;
    [SerializeField] private TMP_Text positionDifferenceLabel = null;
    [SerializeField] private TMP_Text temperatureInLabel = null;
    [SerializeField] private TMP_Text temperatureOutLabel = null;
    [SerializeField] private TMP_Text dataFromLabel = null;

    private readonly List<StationDataSample> snapshot = new List<StationDataSample>();
    private StationDataBuffer subscribedBuffer;
    private bool started;

    private void Start()
    {
        started = true;
        RefreshFromBuffer();
    }

    private void OnEnable()
    {
        if (started) RefreshFromBuffer();
    }

    private void OnDisable() => Unsubscribe();

    public void BindDataBuffer(StationDataBuffer buffer)
    {
        Unsubscribe();
        dataBuffer = buffer;
        if (started && isActiveAndEnabled) RefreshFromBuffer();
    }

    public void RefreshFromBuffer()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !started) return;
        if (dataBuffer == null)
        {
            int count = 0;
            foreach (StationDataBuffer candidate in FindObjectsByType<StationDataBuffer>(FindObjectsSortMode.None))
                if (candidate.isActiveAndEnabled) { dataBuffer = candidate; count++; }
            if (count != 1)
            {
                dataBuffer = null;
                ShowValues(null);
                Debug.LogError($"[{nameof(StationOverviewPanelMR)}] Expected one active StationDataBuffer, found {count}. Assign one explicitly.", this);
                return;
            }
        }
        if (!dataBuffer.isActiveAndEnabled)
        {
            ShowValues(null);
            Unsubscribe();
            Debug.LogError($"[{nameof(StationOverviewPanelMR)}] The data buffer must stay active outside the pages.", this);
            return;
        }
        if (subscribedBuffer != dataBuffer)
        {
            Unsubscribe();
            subscribedBuffer = dataBuffer;
            subscribedBuffer.SampleAdded += OnSampleAdded;
            subscribedBuffer.HistoryReset += RefreshFromBuffer;
        }
        dataBuffer.CopySnapshotTo(snapshot);
        ShowValues(snapshot.Count == 0 ? (StationDataSample?)null : snapshot[snapshot.Count - 1]);
        if (dataFromLabel != null && snapshot.Count > 0)
            dataFromLabel.text = "Pomiar odbył się: " + TimeSpan.FromMilliseconds(snapshot[0].TimeMilliseconds)
                .ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        snapshot.Clear();
    }

    private void OnSampleAdded(StationDataSample sample) => RefreshFromBuffer();

    private void ShowValues(StationDataSample? sample)
    {
        Show(velocityLabel, sample, ChartManagerMR.MetricType.Velocity);
        Show(currentLabel, sample, ChartManagerMR.MetricType.Current);
        Show(torqueLabel, sample, ChartManagerMR.MetricType.Torque);
        Show(loadLabel, sample, ChartManagerMR.MetricType.Load);
        Show(totalForceLabel, sample, ChartManagerMR.MetricType.TotalForce);
        Show(positionDifferenceLabel, sample, ChartManagerMR.MetricType.PositionDifference);
        Show(temperatureInLabel, sample, ChartManagerMR.MetricType.TemperatureIn);
        Show(temperatureOutLabel, sample, ChartManagerMR.MetricType.TemperatureOut);
        if (!sample.HasValue && dataFromLabel != null) dataFromLabel.text = "No data";
    }

    private static void Show(TMP_Text label, StationDataSample? sample, ChartManagerMR.MetricType metric)
    {
        if (label == null) return;
        label.text = sample.HasValue && ChartManagerMR.TryGetMetricValue(sample.Value, metric, out float value)
            ? value.ToString("0.###", CultureInfo.CurrentCulture) + " " + ChartManagerMR.GetMetricUnit(metric)
            : "No data";
    }

    private void Unsubscribe()
    {
        if (subscribedBuffer != null)
        {
            subscribedBuffer.SampleAdded -= OnSampleAdded;
            subscribedBuffer.HistoryReset -= RefreshFromBuffer;
        }
        subscribedBuffer = null;
    }
}
