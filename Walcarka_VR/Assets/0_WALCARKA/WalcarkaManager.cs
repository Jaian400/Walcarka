using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

public class WalcarkaManager : MonoBehaviour
{
    [Header("Prêdkoœæ obrotu wa³ów (OMEGA)")]
    public float rollerSpeed = 100f;

    private float initialRollerGap = 0.02f;
    [Header("Rozstaw wa³ów (w metrach)")]
    public float rollerGap = 0.05f; 
    [SerializeField] Transform topRoller;
    [SerializeField] Transform bottomRoller;
    [SerializeField] Transform centerPoint;

    private Vector3 topInitialPos;
    private Vector3 bottomInitialPos;

    [HideInInspector] public float rollerRadius;

    public bool powerOn = true;

    [Header("NOWOSC EKSPERYMENT")]
    [SerializeField] public float deformationScale = 1.0f;
    public List<float> telemetricSpeeds = new List<float>();
    public List<float> telemetricTimes = new List<float>();

    private DateTime lastRecordTime;
    private float accumulatedTelemetryTime = 0f;

    [Header("UI References")]
    [SerializeField] private Image powerButtonImage;
    [SerializeField] private TextMeshProUGUI rpmText;
    [SerializeField] private TextMeshProUGUI gapText;
    [SerializeField] private TextMeshProUGUI deformationText;

    void Start()
    {
        if (topRoller != null)
        {
            topInitialPos = topRoller.localPosition;
        }
        if (bottomRoller != null)
        {
            bottomInitialPos = bottomRoller.localPosition;
        }

        SetDeformationScale(2.0f);
        CalculateRollerRadius();
        SetRollers(initialRollerGap);
        UpdateUI();
    }

    private void OnEnable()
    {
        ConnectionServiceModern.OnDataReceived += OnNetworkDataReceived;
    }

    private void OnDisable()
    {
        ConnectionServiceModern.OnDataReceived -= OnNetworkDataReceived;
    }

    private void OnNetworkDataReceived(RollerData data)
    {
        SaveTelemetricData(data.time, data.velocity);
    }

    public void SaveTelemetricData(string timeString, float originalVelocity)
    {
        if (DateTime.TryParse(timeString, out DateTime currentTime))
        {
            if (telemetricSpeeds.Count == 0)
            {
                accumulatedTelemetryTime = 0f;
                lastRecordTime = currentTime;
            }
            else
            {
                float deltaSeconds = (float)(currentTime - lastRecordTime).TotalSeconds;

                if (deltaSeconds < 0) 
                    deltaSeconds = 0;

                accumulatedTelemetryTime += deltaSeconds;
                lastRecordTime = currentTime;
            }

            telemetricTimes.Add(accumulatedTelemetryTime);

            float vel = Mathf.Abs(originalVelocity) / 60f;
            // zmiana! bierzemy deformacje juz w procesie
            // vel = Mathf.Abs(vel) / deformationScale; 
            telemetricSpeeds.Add(vel);
        }
        else
        {
            Debug.LogWarning($"Nie uda³o siê odczytaæ czasu: {timeString}");
        }
    } 

    public void SetDeformationScale(float scale)
    {
        deformationScale = scale;
        SetRollers(initialRollerGap);
        UpdateUI();
    }

    public void DeformationScaleUp()
    {
        SetDeformationScale(deformationScale + 0.5f);
    }

    public void DeformationScaleDown()
    {
        if (deformationScale > 1)
        {
            SetDeformationScale(deformationScale - 0.5f);
        }
    }

    void CalculateRollerRadius()
    {
        if (topRoller != null)
        {
            MeshFilter mf = topRoller.GetComponentInChildren<MeshFilter>();
            if (mf != null)
            {
                rollerRadius = mf.sharedMesh.bounds.extents.x * topRoller.localScale.x;
            }
            else
            {
                rollerRadius = 0.5f * topRoller.localScale.x;
            }
        }
    }

    // ustawia walce w odleglosci RollerGap
    public void SetRollers(float inputRollerGap)
    {
        this.rollerGap = inputRollerGap * deformationScale;
        if (topRoller != null && bottomRoller != null)
        {
            topRoller.localPosition = new Vector3(topInitialPos.x, centerPoint.position.y + (rollerGap / 2) + rollerRadius, topInitialPos.z);
            bottomRoller.localPosition = new Vector3(bottomInitialPos.x, centerPoint.position.y - (rollerGap / 2) - rollerRadius, bottomInitialPos.z);
        }
    }

    public void UpdateUI()
    {
        if (powerButtonImage == null)
        {
            return;
        }

        if (powerOn) 
        {
            powerButtonImage.color = Color.green;
            rpmText.text = $"OMEGA: {rollerSpeed} rad/s";
        }
        else
        {
            powerButtonImage.color = Color.red;
            rpmText.text = $"OMEGA: 0 rad/s";
        }

        if (gapText != null)
            gapText.text = $"GAP: {rollerGap * 1000:F1} mm";

        if (deformationText != null)
            deformationText.text = $"SKALA DEFORMACJI: {deformationScale}";
    }

    public void PowerSwitch()
    {
        powerOn = !powerOn;
        UpdateUI();
    }

    void Update()
    {
        if (powerOn)
        {
        }
    }
}
