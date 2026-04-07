using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;

[System.Serializable]
public class RollerData
{
    public string time;
    public float velocity;
    public float current;
    public float torque;
}

public class ConnectionServiceModern : MonoBehaviour
{
    [Header("Network Settings")]
    [Tooltip("Port UDP zdefiniowany w configu serwera")]
    public int udpDiscoveryPort = 9999;
    public int discoveryTimeoutMs = 5000;

    [SerializeField] public TextMeshProUGUI connectionText;
    [SerializeField] public TextMeshProUGUI receviedDataText;

    private TcpClient tcpClient;
    private NetworkStream tcpStream;
    private CancellationTokenSource cancellationTokenSource;

    // ------------------------------------------------------------

    public static event Action<RollerData> OnDataReceived;


    private void Start()
    {
        Connect();
    }

    private async void Connect()
    {
        cancellationTokenSource = new CancellationTokenSource();
        connectionText.text = "Szukanie serwera...";

        bool success = await ConnectToBackendAsync(cancellationTokenSource.Token);

        if (success)
        {
            string remoteAddr = tcpClient.Client.RemoteEndPoint.ToString();
            connectionText.text = $"Po³¹czono z: {remoteAddr}";
            connectionText.color = Color.green;
        }
        else
        {
            connectionText.text = "Nie uda³o po³¹czyæ siê z serwerem";
            connectionText.color = Color.red;
        }
    }

    private async Task<bool> ConnectToBackendAsync(CancellationToken token)
    {
        Debug.Log("Rozpoczynam wyszukiwanie serwera (UDP Discovery)...");

        IPEndPoint serverEndpoint = await DiscoverServerAsync(token);

        if (serverEndpoint != null)
        {
            Debug.Log($"Znaleziono serwer! IP: {serverEndpoint.Address}, Port TCP: {serverEndpoint.Port}");

            await StartTcpConnectionAsync(serverEndpoint.Address.ToString(), serverEndpoint.Port);
            return tcpClient != null && tcpClient.Connected;
        }
        else
        {
            Debug.LogError("Nie uda³o siê znaleŸæ serwera.");
            return false;
        }
    }

    private async Task<IPEndPoint> DiscoverServerAsync(CancellationToken token)
    {
        using (UdpClient udpClient = new UdpClient())
        {
            udpClient.EnableBroadcast = true;

            byte[] requestData = Encoding.ASCII.GetBytes("DISCOVER_BACKEND_SERVICE");
            IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, udpDiscoveryPort);

            try
            {
                await udpClient.SendAsync(requestData, requestData.Length, broadcastEndpoint);

                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(discoveryTimeoutMs, token);

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

                if (completedTask == receiveTask)
                {
                    UdpReceiveResult result = receiveTask.Result;
                    string responseMsg = Encoding.ASCII.GetString(result.Buffer).Trim();

                    Debug.Log($"Odebrano odpowiedŸ UDP: {responseMsg}");

                    if (responseMsg.StartsWith("VR_SERVER_ACK"))
                    {
                        string[] parts = responseMsg.Split(':');
                        if (parts.Length >= 3)
                        {
                            string serverName = parts[1];
                            if (int.TryParse(parts[2], out int tcpPort))
                            {
                                return new IPEndPoint(result.RemoteEndPoint.Address, tcpPort);
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("Timeout wyszukiwania serwera (UDP).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"B³¹d podczas UDP Discovery: {ex.Message}");
            }
        }
        return null;
    }

    private async Task StartTcpConnectionAsync(string ip, int port)
    {
        try
        {
            tcpClient = new TcpClient();
            Debug.Log($"Próba po³¹czenia TCP z {ip}:{port}...");

            await tcpClient.ConnectAsync(ip, port);
            tcpStream = tcpClient.GetStream();

            Debug.Log("<color=green>Po³¹czono z serwerem TCP!</color>");

            _ = ReceiveTcpDataAsync(cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Debug.LogError($"B³¹d po³¹czenia TCP: {ex.Message}");
        }
    }

    private async Task ReceiveTcpDataAsync(CancellationToken token)
    {
        byte[] sizeBuffer = new byte[4];

        try
        {
            while (!token.IsCancellationRequested && tcpStream != null)
            {
                int bytesRead = await ReadExactlyAsync(tcpStream, sizeBuffer, 4, token);
                if (bytesRead == 0) 
                    break; 

                if (BitConverter.IsLittleEndian) Array.Reverse(sizeBuffer);
                uint dataSize = BitConverter.ToUInt32(sizeBuffer, 0);

                byte[] dataBuffer = new byte[dataSize];
                await ReadExactlyAsync(tcpStream, dataBuffer, (int)dataSize, token);

                string jsonString = System.Text.Encoding.UTF8.GetString(dataBuffer);
                RollerData incomingData = JsonUtility.FromJson<RollerData>(jsonString);

                MainThreadDispatcher.RunOnMainThread(() =>
                {
                    OnDataReceived?.Invoke(incomingData);
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"B³¹d odczytu: {ex.Message}");
        }
    }

    private async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, token);
            if (read == 0) return 0;
            totalRead += read;
        }
        return totalRead;
    }

    private void OnDestroy()
    {
        if (cancellationTokenSource != null)
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }

        if (tcpStream != null) tcpStream.Close();
        if (tcpClient != null) tcpClient.Close();

        Debug.Log("Po³¹czenie zamkniête poprawnie.");
    }
}