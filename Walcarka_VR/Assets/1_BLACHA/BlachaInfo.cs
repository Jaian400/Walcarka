using UnityEngine;
using TMPro; 

public class BlachaInfo : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI textDisplay;
    [HideInInspector] public MeshFilter meshFilter;
    [HideInInspector] public Vector3 meshSize;

    public float realLength;
    public float realWidth;
    public float realThickness;

    // bedzie podane z WalcarkaManager
    [HideInInspector] public float appliedDeformationScale = 1.0f;
    [SerializeField] private WalcarkaManager manager;

    [SerializeField] private Transform spawnPoint;

    void Start()
    {
        meshFilter = GetComponentInChildren<MeshFilter>();

        if (meshFilter == null)
        {
            Debug.LogError("Brak MeshFolter w dziecku obiektu");
        }

        SetMeasurements(0.3f, 0.1f, 0.04f);
        Spawn();
    }

    public void Spawn()
    {
        if (spawnPoint == null)
        {
            Debug.Log("SPAWN POINT???");
            return;
        }

        transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    public void Respawn()
    {
        SetMeasurements(0.3f, 0.1f, 0.04f); // on sam wezmie skale
        Spawn();
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

    public void SetMeasurements(float length, float width, float thickness)
    {
        // ajaj skala - blacha jest w cm
        realLength = length * 100 * AppliedDeformationScale;
        realWidth = width * 100 * AppliedDeformationScale;
        realThickness = thickness * 100 * AppliedDeformationScale;

        transform.localScale = new Vector3(realWidth, realThickness, realLength);
    }

    public float AppliedDeformationScale
    {
        get
        {
            if (manager != null)
                return manager.deformationScale;

            return 1.0f;
        }
    }
}
