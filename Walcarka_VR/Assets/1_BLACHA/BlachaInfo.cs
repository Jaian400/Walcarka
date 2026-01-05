using UnityEngine;
using TMPro; 

public class BlachaInfo : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI textDisplay;
    [HideInInspector] public MeshFilter meshFilter;
    [HideInInspector] public Vector3 meshSize;

    public float realWidth;
    public float realThickness;
    public float realLength;

    void Start()
    {
        meshFilter = GetComponentInChildren<MeshFilter>();

        if (meshFilter == null)
        {
            Debug.LogError("Brak MeshFolter w dziecku obiektu");
        }
    }

    void Update()
    {
        if (meshFilter == null || meshFilter.sharedMesh == null) 
            return;

        meshSize = meshFilter.sharedMesh.bounds.size;

        realWidth = meshSize.x * transform.lossyScale.x;
        realThickness = meshSize.y * transform.lossyScale.y;
        realLength = meshSize.z * transform.lossyScale.z;

        float w_mm = realWidth * 1000;
        float t_mm = realThickness * 1000;
        float l_mm = realLength * 1000;

        textDisplay.text = $"Szerokoœæ: {w_mm:F0} mm\n" +
                           $"Gruboœæ: {t_mm:F1} mm\n" +
                           $"D³ugoœæ: {l_mm:F0} mm";
    }

    void UpdateMeasurements()
    {

    }
}
