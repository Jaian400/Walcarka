using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class WalcarkaManager : MonoBehaviour
{
    [Header("Prêdkoœæ obrotu wa³ów (OMEGA)")]
    public float rollerSpeed = 100f;

    [Header("Rozstaw wa³ów (w metrach)")]
    [Range(0.01f, 0.2f)] public float rollerGap = 0.05f; 
    [SerializeField] Transform topRoller;
    [SerializeField] Transform bottomRoller;
    [SerializeField] Transform centerPoint;

    private Vector3 topInitialPos;
    private Vector3 bottomInitialPos;

    [HideInInspector] public float rollerRadius;

    public bool powerOn = true;

    [Header("UI References")]
    [SerializeField] private Image powerButtonImage;
    [SerializeField] private TextMeshProUGUI rpmText;
    [SerializeField] private TextMeshProUGUI gapText;

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

        CalculateRollerRadius();

        UpdateUI();
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

    private void UpdateUI()
    {
        if (powerButtonImage == null)
        {
            return;
        }

        if (powerOn) 
        {
            powerButtonImage.color = Color.green;
        }
        else
        {
            powerButtonImage.color = Color.red;
        }

        
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
            if (topRoller != null && bottomRoller != null)
            {
                topRoller.localPosition = new Vector3(topInitialPos.x, centerPoint.position.y + (rollerGap / 2) + rollerRadius, topInitialPos.z);
                bottomRoller.localPosition = new Vector3(bottomInitialPos.x, centerPoint.position.y - (rollerGap / 2) - rollerRadius, bottomInitialPos.z);
            }
        }

        if (rpmText != null)
            if(powerOn)
                rpmText.text = $"OMEGA: {rollerSpeed} rad/s";
            else
                rpmText.text = $"OMEGA: 0 rad/s";

        if (gapText != null)
            gapText.text = $"GAP: {rollerGap * 1000:F1} mm";
    }
}
