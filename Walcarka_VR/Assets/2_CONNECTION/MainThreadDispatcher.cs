using UnityEngine;
using System;
using System.Collections.Concurrent;

public class MainThreadDispatcher : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxActionsPerFrame = 32;
    [SerializeField, Min(0.1f)] private float maxMillisecondsPerFrame = 2f;

    private static readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();

    public static int PendingCount => _executionQueue.Count;

    public static void RunOnMainThread(Action action)
    {
        if (action != null) _executionQueue.Enqueue(action);
    }

    void Update()
    {
        ProcessPending(maxActionsPerFrame, maxMillisecondsPerFrame, exception => Debug.LogException(exception, this));
    }

    internal static int ProcessPending(int maximumActions, double maximumMilliseconds, Action<Exception> onError)
    {
        int available = Math.Min(_executionQueue.Count, Math.Max(1, maximumActions));
        double milliseconds = double.IsNaN(maximumMilliseconds) || double.IsInfinity(maximumMilliseconds)
            ? 2d : Math.Max(0.1d, maximumMilliseconds);
        double tickBudget = milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000d;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int processed = 0;
        while (processed < available)
        {
            if (processed > 0 && System.Diagnostics.Stopwatch.GetTimestamp() - started >= tickBudget) break;
            if (!_executionQueue.TryDequeue(out Action action)) break;
            try { action(); }
            catch (Exception exception) { onError?.Invoke(exception); }
            processed++;
        }
        return processed;
    }
}
