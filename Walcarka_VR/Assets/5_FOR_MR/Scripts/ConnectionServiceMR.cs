using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public enum MRConnectionState { Disconnected, Connecting, Connected }

[Serializable]
public sealed class MRDatasetInfo
{
    public int id;
    public string datetime;
}

[DisallowMultipleComponent]
public sealed class ConnectionServiceMR : MonoBehaviour
{
    [Header("Network Settings")]
    public int udpDiscoveryPort = 9999;
    public int discoveryTimeoutMs = 5000;
    [Min(1)] public int tcpConnectionTimeoutMs = 5000;
    [Min(1)] public int maxMessageBytes = 65536;
    [Min(1)] public int maxPendingSamples = 128;
    public int httpPort = 8080;
    [Min(1)] public int httpTimeoutSeconds = 10;
    [Min(100)] public int reconnectDelayMs = 1000;
    [Min(100)] public int maxReconnectDelayMs = 10000;

    public TextMeshProUGUI connectionText;
    public TextMeshProUGUI receviedDataText;

    public event Action<RollerData> OnDataReceived;
    public event Action<MRConnectionState> OnConnectionStateChanged;
    public event Action OnDatasetStarted;
    public event Action<bool> OnDatasetFinished;
    public MRConnectionState State { get; private set; }
    public string ServerIpAddress { get; private set; }
    public bool IsConnecting { get; private set; }
    public bool IsConnected { get; private set; }
    public bool IsLoading => requestPending || incomingDatasetId >= 0;
    public bool FollowLatest { get; private set; } = true;
    public int CurrentDatasetId { get; private set; } = -1;
    public string SelectedDatetime { get; private set; }
    public MRDatasetInfo[] AvailableDatasets { get; private set; } = Array.Empty<MRDatasetInfo>();

    private TcpClient tcpClient;
    private NetworkStream tcpStream;
    private CancellationTokenSource cancellation;
    private CancellationToken connectionToken;
    private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
    private int generation;
    private bool destroyed;
    private bool requestPending;
    private int requestedDatasetId = -1;
    private bool previousFollowLatest;
    private string previousDatetime;
    private int incomingDatasetId = -1;
    private int receivedSamples;
    private bool invalidSample;
    private bool protocolFailed;

    void Start()
    {
        Connect();
    }

    public void Connect()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || destroyed || IsConnecting || IsConnected)
            return;

        StopSession();
        cancellation = new CancellationTokenSource();
        SetConnectionState(MRConnectionState.Connecting);
        SetStatus("Szukanie serwera...", Color.white);
        _ = RunSessionAsync(generation, cancellation.Token);
    }

    public void Disconnect()
    {
        StopSession();
        SetStatus("Rozłączono", Color.white);
    }

    private async Task RunSessionAsync(int session, CancellationToken token)
    {
        int initialDelay = Math.Min(Math.Max(100, reconnectDelayMs), Math.Max(100, maxReconnectDelayMs));
        int retryDelay = initialDelay;
        try
        {
            while (IsCurrent(session, token))
            {
                TcpClient client = null;
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    CancellationToken attemptToken = attempt.Token;
                    try
                    {
                        connectionToken = attemptToken;
                        SetConnectionState(MRConnectionState.Connecting);
                        SetStatus("Szukanie serwera...", Color.white);
                        IPEndPoint endpoint = await DiscoverServerAsync(attemptToken);
                        if (!IsCurrent(session, attemptToken)) return;
                        if (endpoint == null)
                            throw new TimeoutException("Nie znaleziono serwera w wyznaczonym czasie.");

                        client = new TcpClient();
                        tcpClient = client;
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(attemptToken))
                        {
                            timeout.CancelAfter(Math.Max(1, tcpConnectionTimeoutMs));
                            using (timeout.Token.Register(client.Close))
                            {
                                try
                                {
                                    await client.ConnectAsync(endpoint.Address, endpoint.Port);
                                    timeout.Token.ThrowIfCancellationRequested();
                                }
                                catch (Exception) when (timeout.IsCancellationRequested)
                                {
                                    attemptToken.ThrowIfCancellationRequested();
                                    throw new TimeoutException("Przekroczono czas połączenia TCP.");
                                }
                            }
                        }

                        if (!IsCurrent(session, attemptToken)) return;
                        ServerIpAddress = endpoint.Address.ToString();
                        CurrentDatasetId = -1;
                        protocolFailed = false;
                        using (attemptToken.Register(client.Close))
                        using (NetworkStream stream = client.GetStream())
                        {
                            tcpStream = stream;
                            try
                            {
                                await GetDatasetsAsync(attemptToken);
                            }
                            catch (Exception exception) when (!attempt.IsCancellationRequested)
                            {
                                SetDatasetStatus($"Nie udało się pobrać listy zestawów: {exception.Message}", true);
                            }
                            if (FollowLatest)
                                await SendSubscriptionAsync(true, null, session, attemptToken);

                            SetConnectionState(MRConnectionState.Connected);
                            SetStatus($"Połączono z: {endpoint}", Color.green);
                            retryDelay = initialDelay;
                            int frameLimit = Math.Max(1, maxMessageBytes);
                            int pendingLimit = Math.Max(1, maxPendingSamples);
                            try
                            {
                                await Task.Run(() => MRConnectionProtocol.ReceiveFramesAsync(stream, frameLimit, pendingLimit,
                                    json => DeliverMessage(session, attemptToken, json), attemptToken), attemptToken);
                            }
                            finally
                            {
                                await DrainCallbacksAsync(attemptToken);
                            }
                            attemptToken.ThrowIfCancellationRequested();
                            throw new EndOfStreamException("Połączenie z serwerem zostało zamknięte.");
                        }
                    }
                    catch (Exception) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        if (IsCurrent(session, token))
                        {
                            SetStatus($"Błąd połączenia: {exception.Message}", Color.red);
                            Debug.LogWarning($"[{nameof(ConnectionServiceMR)}] {exception.Message}", this);
                        }
                    }
                    finally
                    {
                        attempt.Cancel();
                        client?.Close();
                        if (session == generation)
                        {
                            tcpClient = null;
                            tcpStream = null;
                            AvailableDatasets = Array.Empty<MRDatasetInfo>();
                            SetConnectionState(MRConnectionState.Disconnected);
                            AbortDataset();
                        }
                    }
                }
                try
                {
                    await Task.Delay(retryDelay, token);
                }
                catch (OperationCanceledException) { break; }
                retryDelay = (int)Math.Min(Math.Max(100, maxReconnectDelayMs), retryDelay * 2L);
            }
        }
        finally
        {
            if (session == generation)
            {
                cancellation?.Dispose();
                cancellation = null;
            }
        }
    }

    private static async Task DrainCallbacksAsync(CancellationToken token)
    {
        var delivered = new TaskCompletionSource<bool>();
        using (token.Register(() => delivered.TrySetCanceled()))
        {
            MainThreadDispatcher.RunOnMainThread(() => delivered.TrySetResult(true));
            await delivered.Task;
        }
    }

    public async Task<MRDatasetInfo[]> GetDatasetsAsync(CancellationToken token = default)
    {
        int session = generation;
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, connectionToken))
        {
            if (tcpStream == null || !IsCurrent(session, lifetime.Token))
                throw new OperationCanceledException("Brak połączenia z serwerem.");
            string url = new UriBuilder("http", ServerIpAddress, httpPort, "/datasets").Uri.AbsoluteUri;
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = Math.Max(1, httpTimeoutSeconds);
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                lifetime.Token.ThrowIfCancellationRequested();
                if (!IsCurrent(session, lifetime.Token)) throw new OperationCanceledException();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new IOException(request.error);
                var list = JsonUtility.FromJson<DatasetList>("{\"items\":" + request.downloadHandler.text + "}");
                if (list?.items == null) throw new FormatException("Niepoprawna lista zestawów.");
                AvailableDatasets = list.items;
                return AvailableDatasets;
            }
        }
    }

    public Task SelectDatasetAsync(MRDatasetInfo dataset)
    {
        if (dataset == null || dataset.id < 0 || string.IsNullOrEmpty(dataset.datetime))
            throw new ArgumentException("Niepoprawny zestaw.", nameof(dataset));
        return ChangeSubscriptionAsync(false, dataset);
    }

    public Task FollowLatestAsync() => ChangeSubscriptionAsync(true, null);

    public async Task PauseAutoSendAsync()
    {
        if (!IsConnected || !FollowLatest) return;
        int session = generation;
        CancellationToken token = connectionToken;
        FollowLatest = false;
        try
        {
            await WriteMessageAsync("{\"type\":\"subscribe\",\"followLatest\":false,\"sendData\":false}", session, token);
        }
        catch
        {
            if (IsCurrent(session, token)) tcpClient?.Close();
            throw;
        }
    }

    private async Task ChangeSubscriptionAsync(bool followLatest, MRDatasetInfo dataset)
    {
        if (!IsConnected || IsLoading)
            throw new InvalidOperationException("Poczekaj na połączenie i zakończenie przesyłania.");
        int session = generation;
        CancellationToken token = connectionToken;
        try { await SendSubscriptionAsync(followLatest, dataset, session, token); }
        catch
        {
            if (IsCurrent(session, token)) tcpClient?.Close();
            throw;
        }
    }

    private async Task SendSubscriptionAsync(bool followLatest, MRDatasetInfo dataset, int session, CancellationToken token)
    {
        previousFollowLatest = FollowLatest;
        previousDatetime = SelectedDatetime;
        FollowLatest = followLatest;
        SelectedDatetime = dataset?.datetime;
        requestedDatasetId = dataset?.id ?? -1;
        requestPending = true;
        SetDatasetStatus("Pobieranie zestawu...", false);
        string json = followLatest ? "{\"type\":\"subscribe\",\"followLatest\":true}"
            : "{\"type\":\"subscribe\",\"followLatest\":false,\"datasetId\":" + dataset.id.ToString(CultureInfo.InvariantCulture) + "}";
        await WriteMessageAsync(json, session, token);
    }

    private async Task WriteMessageAsync(string json, int session, CancellationToken token)
    {
        await writeLock.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(session, token) || tcpStream == null) throw new OperationCanceledException();
            await MRConnectionProtocol.WriteFrameAsync(tcpStream, json, Math.Max(1, maxMessageBytes), token);
        }
        finally { writeLock.Release(); }
    }

    [Serializable]
    private sealed class DatasetList { public MRDatasetInfo[] items; }

    [Serializable]
    private sealed class ServerMessage
    {
        public string type;
        public int datasetId;
        public int sampleCount;
        public string message;
    }

    private async Task<IPEndPoint> DiscoverServerAsync(CancellationToken token)
    {
        using (var udp = new UdpClient())
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            udp.EnableBroadcast = true;
            timeout.CancelAfter(Math.Max(1, discoveryTimeoutMs));
            using (timeout.Token.Register(udp.Close))
            {
                try
                {
                    byte[] request = Encoding.ASCII.GetBytes("DISCOVER_BACKEND_SERVICE");
                    await udp.SendAsync(request, request.Length,
                        new IPEndPoint(IPAddress.Broadcast, udpDiscoveryPort));
                    while (true)
                    {
                        UdpReceiveResult reply = await udp.ReceiveAsync();
                        timeout.Token.ThrowIfCancellationRequested();
                        string message = Encoding.ASCII.GetString(reply.Buffer).Trim();
                        if (MRConnectionProtocol.TryParseDiscoveryReply(message, reply.RemoteEndPoint.Address,
                            out IPEndPoint endpoint))
                            return endpoint;
                    }
                }
                catch (Exception) when (timeout.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                    return null;
                }
            }
        }
    }

    private void DeliverMessage(int session, CancellationToken token, string json)
    {
        if (!IsCurrent(session, token) || protocolFailed) return;
        try
        {
            var message = new ServerMessage { datasetId = -1, sampleCount = -1 };
            JsonUtility.FromJsonOverwrite(json, message);
            switch (message.type)
            {
                case "dataset_start":
                    if (incomingDatasetId >= 0 || message.datasetId < 0)
                        throw new FormatException("Niepoprawny początek zestawu.");
                    incomingDatasetId = message.datasetId;
                    receivedSamples = 0;
                    invalidSample = false;
                    if (requestPending && (FollowLatest || requestedDatasetId == message.datasetId)) requestPending = false;
                    SetDatasetStatus("Pobieranie zestawu...", false);
                    Notify(OnDatasetStarted);
                    break;
                case "measurement":
                    if (incomingDatasetId < 0) throw new FormatException("Pomiar poza zestawem.");
                    receivedSamples = checked(receivedSamples + 1);
                    var sample = new RollerData { velocity = float.NaN, current = float.NaN, torque = float.NaN };
                    JsonUtility.FromJsonOverwrite(json, sample);
                    if (!MRConnectionProtocol.IsValidSample(sample)) invalidSample = true;
                    else Notify(OnDataReceived, sample);
                    break;
                case "dataset_end":
                    if (incomingDatasetId < 0) throw new FormatException("Koniec bez początku zestawu.");
                    bool complete = !invalidSample && message.datasetId == incomingDatasetId && message.sampleCount == receivedSamples;
                    if (complete) CurrentDatasetId = incomingDatasetId;
                    incomingDatasetId = -1;
                    Notify(OnDatasetFinished, complete);
                    SetDatasetStatus(complete ? $"Dane odebrane: {receivedSamples}" : "Zestaw odrzucony: niepoprawne lub niepełne dane.", !complete);
                    break;
                case "error":
                    if (requestPending)
                    {
                        FollowLatest = previousFollowLatest;
                        SelectedDatetime = previousDatetime;
                    }
                    AbortDataset();
                    SetDatasetStatus(message.message ?? "Błąd żądania zestawu.", true);
                    break;
                default:
                    throw new FormatException("Nieznany typ wiadomości.");
            }
        }
        catch (Exception exception)
        {
            protocolFailed = true;
            AbortDataset();
            SetDatasetStatus(exception.Message, true);
            tcpClient?.Close();
        }
    }

    private void AbortDataset()
    {
        bool pending = IsLoading;
        requestPending = false;
        incomingDatasetId = -1;
        if (pending) Notify(OnDatasetFinished, false);
    }

    private void SetDatasetStatus(string text, bool error)
    {
        if (receviedDataText != null) receviedDataText.text = text;
        if (error) Debug.LogWarning($"[{nameof(ConnectionServiceMR)}] {text}", this);
    }

    private bool IsCurrent(int session, CancellationToken token)
    {
        return this != null && !destroyed && isActiveAndEnabled &&
               session == generation && !token.IsCancellationRequested;
    }

    private void StopSession()
    {
        generation++;
        var previousCancellation = cancellation;
        cancellation = null;
        previousCancellation?.Cancel();
        tcpClient?.Close();
        tcpClient = null;
        tcpStream = null;
        AvailableDatasets = Array.Empty<MRDatasetInfo>();
        previousCancellation?.Dispose();
        SetConnectionState(MRConnectionState.Disconnected);
        AbortDataset();
    }

    private void SetConnectionState(MRConnectionState state)
    {
        IsConnecting = state == MRConnectionState.Connecting;
        IsConnected = state == MRConnectionState.Connected;
        if (State == state) return;
        State = state;
        Notify(OnConnectionStateChanged, state);
    }

    private void Notify(Action listeners)
    {
        if (listeners == null) return;
        foreach (Action listener in listeners.GetInvocationList())
        {
            try { listener(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }

    private void Notify<T>(Action<T> listeners, T value)
    {
        if (listeners == null) return;
        foreach (Action<T> listener in listeners.GetInvocationList())
        {
            try { listener(value); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }

    private void SetStatus(string text, Color color)
    {
        if (connectionText == null) return;
        connectionText.text = text;
        connectionText.color = color;
    }

    public async Task<Texture2D> DownloadPlotImageAsync(string filename)
    {
        if (string.IsNullOrEmpty(ServerIpAddress) || filename == null) return null;
        string url = $"http://{ServerIpAddress}:5000/api/plots?filename={Uri.EscapeDataString(filename)}";
        using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (destroyed || this == null) return null;
                await Task.Yield();
            }
            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success) return null;
            var texture = new Texture2D(2, 2);
            if (texture.LoadImage(request.downloadHandler.data)) return texture;
            Destroy(texture);
            return null;
        }
    }

    private void OnDisable() => StopSession();
    private void OnApplicationQuit() => StopSession();
    private void OnDestroy()
    {
        destroyed = true;
        StopSession();
    }
}

internal static class MRConnectionProtocol
{
    internal static async Task WriteFrameAsync(Stream stream, string json, int maximumFrameBytes, CancellationToken token)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length == 0 || payload.Length > maximumFrameBytes)
            throw new InvalidDataException("Invalid outgoing frame length.");
        var frame = new byte[4 + payload.Length];
        uint length = (uint)payload.Length;
        frame[0] = (byte)(length >> 24);
        frame[1] = (byte)(length >> 16);
        frame[2] = (byte)(length >> 8);
        frame[3] = (byte)length;
        Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
        await stream.WriteAsync(frame, 0, frame.Length, token).ConfigureAwait(false);
    }

    internal static async Task ReceiveFramesAsync(Stream stream, int maximumFrameBytes, int pendingLimit,
        Action<string> deliver, CancellationToken token)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (deliver == null) throw new ArgumentNullException(nameof(deliver));
        if (pendingLimit < 1) throw new ArgumentOutOfRangeException(nameof(pendingLimit));
        var availableSlots = new SemaphoreSlim(pendingLimit, pendingLimit);
        var header = new byte[4];
        var encoding = new UTF8Encoding(false, true);
        try
        {
            while (await ReadExactlyAsync(stream, header, token).ConfigureAwait(false))
            {
                int length = DecodeFrameLength(header, maximumFrameBytes);
                var payload = new byte[length];
                if (!await ReadExactlyAsync(stream, payload, token).ConfigureAwait(false))
                    throw new EndOfStreamException("Połączenie zamknięto przed odebraniem treści ramki.");
                string json = encoding.GetString(payload);
                await availableSlots.WaitAsync(token).ConfigureAwait(false);
                bool queued = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    MainThreadDispatcher.RunOnMainThread(() =>
                    {
                        try
                        {
                            if (!token.IsCancellationRequested) deliver(json);
                        }
                        finally { availableSlots.Release(); }
                    });
                    queued = true;
                }
                finally
                {
                    if (!queued) availableSlots.Release();
                }
            }
        }
        finally
        {
            MainThreadDispatcher.RunOnMainThread(availableSlots.Dispose);
        }
    }

    internal static bool TryParseDiscoveryReply(string message, IPAddress address, out IPEndPoint endpoint)
    {
        endpoint = null;
        if (message == null || address == null) return false;
        string[] parts = message.Split(':');
        if (parts.Length < 3 || parts[0] != "VR_SERVER_ACK" ||
            !int.TryParse(parts[2], out int port) || port < 1 || port > 65535)
            return false;
        endpoint = new IPEndPoint(address, port);
        return true;
    }

    internal static int DecodeFrameLength(byte[] header, int maximum)
    {
        if (header == null || header.Length != 4) throw new ArgumentException("Expected a four-byte frame header.");
        uint length = ((uint)header[0] << 24) | ((uint)header[1] << 16) |
                      ((uint)header[2] << 8) | header[3];
        if (length == 0 || length > (uint)Math.Max(1, maximum))
            throw new InvalidDataException($"Invalid frame length: {length}.");
        return (int)length;
    }

    internal static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, token).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0) return false;
                throw new EndOfStreamException("Connection ended inside a frame.");
            }
            offset += read;
        }
        return true;
    }

    internal static bool IsValidSample(RollerData sample)
    {
        if (sample == null || !TryParseTimeMilliseconds(sample.time, out _) ||
            !IsFinite(sample.velocity) || !IsFinite(sample.current) || !IsFinite(sample.torque))
            return false;

        bool legacy = float.IsNaN(sample.load) && float.IsNaN(sample.forceRight) &&
            float.IsNaN(sample.forceLeft) && float.IsNaN(sample.positionRight) &&
            float.IsNaN(sample.positionLeft) && float.IsNaN(sample.temperatureIn) &&
            float.IsNaN(sample.temperatureOut);
        return legacy || (IsFinite(sample.load) && IsFinite(sample.forceRight) &&
            IsFinite(sample.forceLeft) && IsFinite(sample.positionRight) &&
            IsFinite(sample.positionLeft) && IsFinite(sample.temperatureIn) &&
            IsFinite(sample.temperatureOut));
    }

    internal static bool TryParseTimeMilliseconds(string text, out long milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Split(':', '.');
        if (parts.Length != 4 || parts[0].Length != 2 || parts[1].Length != 2 ||
            parts[2].Length != 2 || parts[3].Length < 1 || parts[3].Length > 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int millis) ||
            hours > 23 || minutes > 59 || seconds > 59 || millis > 999)
            return false;
        milliseconds = ((hours * 60L + minutes) * 60L + seconds) * 1000L + millis;
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
