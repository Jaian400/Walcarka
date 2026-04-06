using UnityEngine;
using System;
using System.Collections.Concurrent;

public class MainThreadDispatcher : MonoBehaviour
{
    private static readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();

    public static void RunOnMainThread(Action action) => _executionQueue.Enqueue(action);

    void Update()
    {
        while (_executionQueue.TryDequeue(out var action)) action?.Invoke();
    }
}