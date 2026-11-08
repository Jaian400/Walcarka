using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StationDataBuffer : MonoBehaviour
{
    [SerializeField] private ConnectionServiceMR connectionService;
    [SerializeField, Min(1)] private int maxSamples = 2000;

    public event Action<StationDataSample> SampleAdded;
    public event Action HistoryReset;

    private ConnectionServiceMR subscribedService;
    private StationSampleHistory history;

    public int Count => History.Count;
    public int Capacity => History.Capacity;
    public long LatestSequence => History.LatestSequence;

    private StationSampleHistory History
    {
        get
        {
            if (history == null) history = new StationSampleHistory(Math.Max(1, maxSamples));
            return history;
        }
    }

    private void Awake()
    {
        _ = History;
    }

    private void OnEnable()
    {
        if (connectionService == null) connectionService = GetComponent<ConnectionServiceMR>();
        if (connectionService == null)
        {
            Debug.LogError($"[{nameof(StationDataBuffer)}] Assign Connection Service, or put both components on the same always-active object.", this);
            return;
        }

        subscribedService = connectionService;
        subscribedService.OnDataReceived += AddSample;
    }

    private void OnDisable()
    {
        if (subscribedService != null) subscribedService.OnDataReceived -= AddSample;
        subscribedService = null;
    }

    private void Update()
    {
        if (history != null && history.Capacity != Math.Max(1, maxSamples))
            SetCapacity(maxSamples);
    }

    public void AddSample(RollerData sample)
    {
        if (!MRConnectionProtocol.IsValidSample(sample)) return;
        StationDataSample stored = History.Add(sample);
        Action<StationDataSample> listeners = SampleAdded;
        if (listeners == null) return;
        foreach (Action<StationDataSample> listener in listeners.GetInvocationList())
        {
            try { listener(stored); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }

    public void CopySnapshotTo(List<StationDataSample> destination)
    {
        History.CopySnapshotTo(destination);
    }

    public void ClearHistory()
    {
        if (!Application.isPlaying) return;
        History.Clear();
        NotifyHistoryReset();
    }

    public void SetCapacity(int capacity)
    {
        maxSamples = Math.Max(1, capacity);
        if (History.Capacity == maxSamples) return;
        History.Resize(maxSamples);
        NotifyHistoryReset();
    }

    private void NotifyHistoryReset()
    {
        Action listeners = HistoryReset;
        if (listeners == null) return;
        foreach (Action listener in listeners.GetInvocationList())
        {
            try { listener(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}

public readonly struct StationDataSample
{
    public long Sequence { get; }
    public string Time { get; }
    public float Velocity { get; }
    public float Current { get; }
    public float Torque { get; }
    public float Load { get; }
    public float ForceRight { get; }
    public float ForceLeft { get; }
    public float PositionRight { get; }
    public float PositionLeft { get; }
    public float TemperatureIn { get; }
    public float TemperatureOut { get; }
    public bool HasAdditionalData { get; }
    public long TimeMilliseconds { get; }

    public float TotalForce => HasAdditionalData ? ForceRight + ForceLeft : float.NaN;
    public float ForceDifference => HasAdditionalData ? ForceRight - ForceLeft : float.NaN;
    public float PositionDifference => HasAdditionalData ? PositionLeft - PositionRight : float.NaN;
    public float ForceImbalancePercent => HasAdditionalData && TotalForce != 0f
        ? (float)(100d * ((double)ForceRight - ForceLeft) / ((double)ForceRight + ForceLeft))
        : float.NaN;

    public StationDataSample(long sequence, RollerData data)
    {
        Sequence = sequence;
        Time = data.time;
        Velocity = data.velocity;
        Current = data.current;
        Torque = data.torque;
        Load = data.load;
        ForceRight = data.forceRight;
        ForceLeft = data.forceLeft;
        PositionRight = data.positionRight;
        PositionLeft = data.positionLeft;
        TemperatureIn = data.temperatureIn;
        TemperatureOut = data.temperatureOut;
        HasAdditionalData = data.HasAdditionalData;
        MRConnectionProtocol.TryParseTimeMilliseconds(data.time, out long milliseconds);
        TimeMilliseconds = milliseconds;
    }
}

internal sealed class StationSampleHistory
{
    private StationDataSample[] samples;
    private int head;
    private long sequence;
    public int Count { get; private set; }
    public int Capacity => samples.Length;
    public long LatestSequence => sequence;

    internal StationSampleHistory(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        samples = new StationDataSample[capacity];
    }

    internal StationDataSample Add(RollerData sample)
    {
        if (!MRConnectionProtocol.IsValidSample(sample))
            throw new ArgumentException("Sample requires a time and finite metric values.", nameof(sample));

        var stored = new StationDataSample(++sequence, sample);
        int index = (head + Count) % Capacity;
        samples[index] = stored;
        if (Count == Capacity) head = (head + 1) % Capacity;
        else Count++;
        return stored;
    }

    internal void CopySnapshotTo(List<StationDataSample> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        for (int i = 0; i < Count; i++)
            destination.Add(samples[(head + i) % Capacity]);
    }

    internal void Clear()
    {
        Array.Clear(samples, 0, samples.Length);
        Count = 0;
        head = 0;
    }

    internal void Resize(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity == Capacity) return;
        int retained = Math.Min(Count, capacity);
        var replacement = new StationDataSample[capacity];
        int skip = Count - retained;
        for (int i = 0; i < retained; i++)
            replacement[i] = samples[(head + skip + i) % Capacity];
        samples = replacement;
        head = 0;
        Count = retained;
    }
}
