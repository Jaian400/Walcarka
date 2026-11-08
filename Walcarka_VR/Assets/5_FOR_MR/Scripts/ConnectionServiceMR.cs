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

[DisallowMultipleComponent]
public sealed class ConnectionServiceMR : MonoBehaviour
{
    [Header("Network Settings")]
    public int udpDiscoveryPort = 9999;
    public int discoveryTimeoutMs = 5000;
    [Min(1)] public int tcpConnectionTimeoutMs = 5000;
    [Min(1)] public int maxMessageBytes = 65536;

    public TextMeshProUGUI connectionText;
    public TextMeshProUGUI receviedDataText;

    public event Action<RollerData> OnDataReceived;
    public string ServerIpAddress { get; private set; }
    public bool IsConnecting { get; private set; }
    public bool IsConnected { get; private set; }

    private TcpClient tcpClient;
    private CancellationTokenSource cancellation;
    private int generation;
    private bool destroyed;

    public void Connect()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || destroyed || IsConnecting || IsConnected)
            return;

        StopSession();
        cancellation = new CancellationTokenSource();
        IsConnecting = true;
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
        TcpClient client = null;
        string finalStatus = "Rozłączono z serwerem";
        Color finalColor = Color.white;
        try
        {
            IPEndPoint endpoint = await DiscoverServerAsync(token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrent(session, token)) return;
            if (endpoint == null)
                throw new TimeoutException("Nie znaleziono serwera w wyznaczonym czasie.");

            client = new TcpClient();
            tcpClient = client;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
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
                        token.ThrowIfCancellationRequested();
                        throw new TimeoutException("Przekroczono czas połączenia TCP.");
                    }
                }
            }

            if (!IsCurrent(session, token)) return;
            ServerIpAddress = endpoint.Address.ToString();
            IsConnecting = false;
            IsConnected = true;
            SetStatus($"Połączono z: {endpoint}", Color.green);

            using (token.Register(client.Close))
            using (NetworkStream stream = client.GetStream())
            {
                var header = new byte[4];
                while (await MRConnectionProtocol.ReadExactlyAsync(stream, header, token))
                {
                    int length = MRConnectionProtocol.DecodeFrameLength(header, Math.Max(1, maxMessageBytes));
                    var payload = new byte[length];
                    if (!await MRConnectionProtocol.ReadExactlyAsync(stream, payload, token))
                        throw new EndOfStreamException("Połączenie zamknięto przed odebraniem treści ramki.");

                    string json = new UTF8Encoding(false, true).GetString(payload);
                    MainThreadDispatcher.RunOnMainThread(() => DeliverSample(session, token, json));
                }
            }
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(session, token))
            {
                finalStatus = $"Błąd połączenia: {exception.Message}";
                finalColor = Color.red;
                Debug.LogWarning($"[{nameof(ConnectionServiceMR)}] {exception.Message}", this);
            }
        }
        finally
        {
            client?.Close();
            if (session == generation)
            {
                tcpClient = null;
                IsConnecting = false;
                IsConnected = false;
                cancellation?.Dispose();
                cancellation = null;
                if (!destroyed && this != null && isActiveAndEnabled && !token.IsCancellationRequested)
                    SetStatus(finalStatus, finalColor);
            }
        }
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

    private void DeliverSample(int session, CancellationToken token, string json)
    {
        if (!IsCurrent(session, token)) return;
        RollerData sample;
        try
        {
            sample = new RollerData { velocity = float.NaN, current = float.NaN, torque = float.NaN };
            JsonUtility.FromJsonOverwrite(json, sample);
            if (!MRConnectionProtocol.IsValidSample(sample))
                throw new FormatException("Expected HH:mm:ss.milliseconds, three finite drive values and either all seven extra measurements or none.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[{nameof(ConnectionServiceMR)}] Skipped invalid sample: {exception.Message}", this);
            return;
        }

        if (receviedDataText != null) receviedDataText.text = json;
        Action<RollerData> listeners = OnDataReceived;
        if (listeners == null) return;
        foreach (Action<RollerData> listener in listeners.GetInvocationList())
        {
            try { listener(sample); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
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
        previousCancellation?.Dispose();
        IsConnecting = false;
        IsConnected = false;
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
            int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, token);
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
