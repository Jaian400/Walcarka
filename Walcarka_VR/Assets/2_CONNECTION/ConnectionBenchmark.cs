using UnityEngine;
using TMPro;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

public class ConnectionBenchmark : MonoBehaviour
{
    [Header("Serwer")]
    public string serverIP = "127.0.0.1";
    public int port = 8081;

    [Header("Parametry testu")]
    [SerializeField] private int testBatchSize = 1048576; // 1MB

    [Header("UI")]
    public TextMeshProUGUI statsText;
    public TextMeshProUGUI batchSizeInMBText;
    private string logFilePath;

    private TcpClient client;
    private NetworkStream stream;
    private byte[] receiveBuffer;

    async void Start()
    {
        logFilePath = Path.Combine(Application.persistentDataPath, "connection_benchmark_results.csv");
        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "Timestamp;BatchSize_MB;Time_ms;Speed_Mbps;FPS\n");
        }

        batchSizeInMBText.text = (testBatchSize / 1024 / 1024).ToString();

        await ConnectToServer();
    }

    public void UI_TriggerBenchmark()
    {
        RunTest(testBatchSize);
    }

    /*
    public void UI_SetBatchSize(string input)
    {
        if (int.TryParse(input, out int result))
        {
            testBatchSize = result / 1024 ;
            statsText.text = $"Ustawiono paczkê: {testBatchSize / 1024f / 1024f:F2} MB";
        }
    }
    */

    public void UI_AddMB()
    {
        testBatchSize += 1 * 1024 * 1024;
        batchSizeInMBText.text = (testBatchSize / 1024 / 1024).ToString();
    }

    public void UI_SetServerIP(string input)
    {
        serverIP = input;
        statsText.text = $"Zmieniono IP na: {serverIP}";
    }

    public void RunTest(int sizeInBytes)
    {
        _ = ExecuteTest(sizeInBytes);
    }

    private async Task ExecuteTest(int size)
    {
        if (client == null || !client.Connected)
        {
            statsText.text = "Brak po³¹czenia! Próba ponownego ³¹czenia...";
            await ConnectToServer();
            return;
        }

        statsText.text = $"Pobieranie {size / 1024 / 1024f:F2} MB...";

        if (receiveBuffer == null || receiveBuffer.Length != size)
            receiveBuffer = new byte[size];

        Stopwatch sw = new Stopwatch();

        byte[] sizeHeader = BitConverter.GetBytes(size);
        if (BitConverter.IsLittleEndian) Array.Reverse(sizeHeader);

        try
        {
            sw.Start();
            await stream.WriteAsync(sizeHeader, 0, 4);

            int totalRead = 0;
            while (totalRead < size)
            {
                int read = await stream.ReadAsync(receiveBuffer, totalRead, size - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
            sw.Stop();

            SaveAndDisplayResults(totalRead, sw.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            statsText.text = "B³¹d transferu: " + e.Message;
        }
    }

    private void SaveAndDisplayResults(int bytes, long ms)
    {
        float mb = bytes / (1024f * 1024f);
        float seconds = ms / 1000f;
        float speedMbps = (mb * 8) / seconds;
        float currentFPS = 1f / Time.deltaTime;

        statsText.text = $"<color=green>ODEBRANO: {mb:F2} MB</color>\n" +
                         $"CZAS: {ms} ms\n" +
                         $"PRÊDKOŒÆ: {speedMbps:F2} Mbps\n" +
                         $"FPS: {currentFPS:F0}";

        string logLine = $"{DateTime.Now:HH:mm:ss};{mb:F2};{ms};{speedMbps:F2};{currentFPS:F0}\n";
        File.AppendAllText(logFilePath, logLine);
        UnityEngine.Debug.Log("Zapisano log do: " + logFilePath);
    }

    private async Task ConnectToServer()
    {
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(serverIP, port);
            stream = client.GetStream();
        }
        catch (Exception e)
        {
            statsText.text = "B³¹d po³¹czenia: " + e.Message;
        }
    }

    private void OnApplicationQuit()
    {
        stream?.Close();
        client?.Close();
    }
}