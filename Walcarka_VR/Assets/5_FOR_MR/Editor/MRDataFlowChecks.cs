using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

public static class MRDataFlowChecks
{
    private static readonly Func<int, double, Action<Exception>, int> ProcessPending =
        (Func<int, double, Action<Exception>, int>)Delegate.CreateDelegate(typeof(Func<int, double, Action<Exception>, int>),
            typeof(MainThreadDispatcher).GetMethod("ProcessPending", BindingFlags.NonPublic | BindingFlags.Static));
    private static readonly Func<Stream, int, int, Action<string>, CancellationToken, Task> ReceiveFramesAsync =
        (Func<Stream, int, int, Action<string>, CancellationToken, Task>)Delegate.CreateDelegate(
            typeof(Func<Stream, int, int, Action<string>, CancellationToken, Task>),
            typeof(ConnectionServiceMR).Assembly.GetType("MRConnectionProtocol")
                .GetMethod("ReceiveFramesAsync", BindingFlags.NonPublic | BindingFlags.Static));

    [MenuItem("Tools/Walcarka/Validate data flow")]
    public static async void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before running data flow checks.");
        int count = await RunChecksAsync();
        UnityEngine.Debug.Log($"[MRDataFlowChecks] PASS: {count} assertions. Real Link responsiveness still requires testing.");
    }

    public static async Task<int> RunChecksAsync()
    {
        int checks = 0;
        void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("[MRDataFlowChecks] FAIL: " + message);
        }
        void FailOnError(Exception error) => throw new InvalidOperationException("Unexpected dispatcher error.", error);
        Require(MainThreadDispatcher.PendingCount == 0, "Dispatcher starts empty");

        var delivered = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            int captured = i;
            MainThreadDispatcher.RunOnMainThread(() => delivered.Add(captured));
        }
        Require(ProcessPending(3, 1000d, FailOnError) == 3 && delivered.Count == 3,
            "A callback limit leaves work for later frames");
        Require(MainThreadDispatcher.PendingCount == 7, "Unprocessed callbacks are retained");
        ProcessPending(20, 1000d, FailOnError);
        for (int i = 0; i < 10; i++) Require(delivered[i] == i, "Dispatcher keeps FIFO order " + i);

        bool deferred = false;
        MainThreadDispatcher.RunOnMainThread(() => MainThreadDispatcher.RunOnMainThread(() => deferred = true));
        Require(ProcessPending(100, 1000d, FailOnError) == 1 && !deferred,
            "Work queued by a callback waits for the next drain");
        ProcessPending(100, 1000d, FailOnError);
        Require(deferred, "Deferred work is eventually delivered");

        int errors = 0;
        bool afterError = false;
        MainThreadDispatcher.RunOnMainThread(() => throw new InvalidOperationException("Expected test failure."));
        MainThreadDispatcher.RunOnMainThread(() => afterError = true);
        ProcessPending(100, 1000d, _ => errors++);
        Require(errors == 1 && afterError, "One failing callback does not block subsequent callbacks");

        bool laterCallback = false;
        MainThreadDispatcher.RunOnMainThread(() =>
        {
            long until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 100;
            while (Stopwatch.GetTimestamp() < until) Thread.SpinWait(100);
        });
        MainThreadDispatcher.RunOnMainThread(() => laterCallback = true);
        Require(ProcessPending(100, 0.1d, FailOnError) == 1 && !laterCallback,
            "Time budget stops between callbacks even when one callback overruns");
        ProcessPending(100, 1000d, FailOnError);
        Require(laterCallback, "Time-limited work is retained");

        const int frames = 1000;
        var messages = new List<string>();
        using (var stream = BuildFrames(frames))
        {
            Task producer = Task.Run(() => ReceiveFramesAsync(stream, 65536, 4, messages.Add, CancellationToken.None));
            await WaitUntilAsync(() => MainThreadDispatcher.PendingCount == 4 || producer.IsCompleted);
            Require(!producer.IsCompleted && MainThreadDispatcher.PendingCount == 4,
                "A buffered startup burst is held by the pending sample limit");
            Require(messages.Count == 0, "Worker never calls delivery listeners directly");
            int inputOpportunities = 0;
            var deadline = Stopwatch.StartNew();
            while (!producer.IsCompleted || MainThreadDispatcher.PendingCount > 0)
            {
                Require(deadline.Elapsed < TimeSpan.FromSeconds(30), "Burst processing completes without deadlock");
                ProcessPending(2, 1000d, FailOnError);
                inputOpportunities++;
                await Task.Delay(1);
            }
            await producer;
            Require(inputOpportunities >= frames / 2, "Input gets opportunities between bounded delivery batches");
        }
        Require(messages.Count == frames, "Backpressure preserves every received frame through EOF");
        for (int i = 0; i < messages.Count; i++)
            Require(messages[i] == "Walcarka-zażółć-" + i, "Frame order and UTF-8 are preserved " + i);

        messages.Clear();
        using (var cancellation = new CancellationTokenSource())
        using (var stream = BuildFrames(100))
        {
            Task producer = Task.Run(() => ReceiveFramesAsync(stream, 65536, 2, messages.Add, cancellation.Token));
            await WaitUntilAsync(() => MainThreadDispatcher.PendingCount == 2 || producer.IsCompleted);
            cancellation.Cancel();
            Require(await Task.WhenAny(producer, Task.Delay(2000)) == producer, "Cancellation interrupts backpressure wait");
            bool cancelled = false;
            try { await producer; }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled, "Cancelled receiver exits with cancellation");
            while (MainThreadDispatcher.PendingCount > 0)
                ProcessPending(100, 1000d, FailOnError);
            Require(messages.Count == 0, "Cancelled queued deliveries are skipped and their slots released safely");
        }

        using (var source = BuildFrames(4))
        using (var fragmented = new FragmentedStream(source.ToArray()))
        {
            messages.Clear();
            await Task.Run(() => ReceiveFramesAsync(fragmented, 65536, 8, messages.Add, CancellationToken.None));
            while (MainThreadDispatcher.PendingCount > 0)
                ProcessPending(100, 1000d, FailOnError);
            Require(messages.Count == 4 && messages[3] == "Walcarka-zażółć-3", "Fragmented reads reconstruct complete frames");
        }

        using (var invalid = new MemoryStream(new byte[] { 0, 1, 0, 1 }))
        {
            bool rejected = false;
            try { await Task.Run(() => ReceiveFramesAsync(invalid, 65536, 2, _ => { }, CancellationToken.None)); }
            catch (InvalidDataException) { rejected = true; }
            while (MainThreadDispatcher.PendingCount > 0)
                ProcessPending(100, 1000d, FailOnError);
            Require(rejected, "Oversized frames are rejected without leaking the delivery gate");
        }
        Require(MainThreadDispatcher.PendingCount == 0, "Checks leave no queued work");
        return checks;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("Producer did not reach expected state.");
            await Task.Delay(1);
        }
    }

    private static MemoryStream BuildFrames(int count)
    {
        var stream = new MemoryStream();
        for (int i = 0; i < count; i++)
        {
            byte[] payload = Encoding.UTF8.GetBytes("Walcarka-zażółć-" + i);
            int length = payload.Length;
            stream.Write(new[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length }, 0, 4);
            stream.Write(payload, 0, length);
        }
        stream.Position = 0;
        return stream;
    }

    private sealed class FragmentedStream : MemoryStream
    {
        public FragmentedStream(byte[] bytes) : base(bytes) { }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => base.ReadAsync(buffer, offset, Math.Min(1, count), token);
    }
}
