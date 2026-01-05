using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;

[System.Serializable]
public class MachineData
{
    public float omega;
    public float gap;
}

public class ConnectionService : MonoBehaviour
{
    [SerializeField] private string serverUrl = "http://localhost:8080/machine-state";
    [SerializeField] private float updateInterval = 1.0f;

    [SerializeField] private WalcarkaManager manager;

    [SerializeField] private TMP_InputField ipInputField;
    private string savedIP;

    private void Start()
    {
        savedIP = PlayerPrefs.GetString("SavedRemoteIP", "localhost");
        if (ipInputField != null) ipInputField.text = savedIP;

        StartCoroutine(GetDataLoop());
    }

    public void SaveNewIP()
    {
        if (ipInputField != null)
        {
            savedIP = ipInputField.text;
            PlayerPrefs.SetString("SavedRemoteIP", savedIP);
            PlayerPrefs.Save();
            UpdateUrl();
            Debug.Log("Zapisano nowe IP: " + savedIP);
        }
    }

    void UpdateUrl()
    {
        serverUrl = $"http://{savedIP}:8080/machine-state";
    }

    IEnumerator GetDataLoop()
    {
        while (true)
        {
            yield return StartCoroutine(FetchData());
            yield return new WaitForSeconds(updateInterval);
        }
    }

    IEnumerator FetchData()
    {
        using (UnityWebRequest webRequest = UnityWebRequest.Get(serverUrl))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Error: {webRequest.error}");
            }
            else
            {
                string jsonResponse = webRequest.downloadHandler.text;
                ParseAndApplyData(jsonResponse);
            }
        }
    }

    void ParseAndApplyData(string json)
    {
        try
        {
            MachineData data = JsonUtility.FromJson<MachineData>(json);

            if (data != null && manager != null)
            {
                manager.rollerSpeed = data.omega;
                manager.rollerGap = data.gap;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Parer error: {e.Message}");
        }
    }

}
