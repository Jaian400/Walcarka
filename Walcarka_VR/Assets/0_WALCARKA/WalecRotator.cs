using UnityEngine;

public class WalecRotator : MonoBehaviour
{
    [Header("Kierunek obrotu")]
    [SerializeField] bool rotateClockwise = true;
    private WalcarkaManager manager;
    private float directionMultiplier = 1f;
    private float speed = 60f;

    void Start()
    {
        manager = GetComponentInParent<WalcarkaManager>();

        if (rotateClockwise)
        {
            directionMultiplier = 1f;
        } 
        else
        {
            directionMultiplier = -1f;
        }

    }

    void Update()
    {
        if (manager != null && manager.powerOn)
        {
            float omega = manager.rollerSpeed;
            float degreesPerSecond = omega * Mathf.Rad2Deg;

            transform.Rotate(Vector3.forward * degreesPerSecond * directionMultiplier * Time.deltaTime);
        }

    }
}
