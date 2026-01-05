using UnityEngine;
using System.Collections;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class WalcarkaProcessor : MonoBehaviour
{
    private WalcarkaManager manager;
    private BlachaInfo info;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private Transform centerPoint;
    [SerializeField] private Transform enterPoint;
    [SerializeField] private float processSpeedMultiplier = 100f;
    [SerializeField] private float stabilizationSpeed = 2.0f;
    [SerializeField] private float safetyMargin = 0.05f;

    [Header("Collidery wa³ów")]
    [SerializeField] private Collider topRollerCollider;
    [SerializeField] private Collider bottomRollerCollider;

    private bool isProcessing = false;
    private bool isStabilizing = false;

    private GameObject rolledObject = null;

    private Vector3 startScale;
    private Vector3 targetScale;

    private void OnTriggerEnter(Collider other)
    {
        if (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Blacha") && !isProcessing)
        {
            if (manager.rollerSpeed > 0 && manager.powerOn)
            {
                rolledObject = other.attachedRigidbody.gameObject;
                info = rolledObject.GetComponentInChildren<BlachaInfo>();
                if (info == null) 
                    info = rolledObject.GetComponentInChildren<BlachaInfo>();

                RecalculatePoints();

                var grabInteractable = rolledObject.GetComponent<XRGrabInteractable>();
                var rb = rolledObject.GetComponent<Rigidbody>();

                if (grabInteractable != null)
                    grabInteractable.enabled = false;

                if (rb != null) 
                    rb.isKinematic = true;

                if (topRollerCollider != null) 
                    topRollerCollider.enabled = false;

                if (bottomRollerCollider != null) 
                    bottomRollerCollider.enabled = false;

                isProcessing = true;
                isStabilizing = true;

                Debug.Log("STABILIZACJA: " + rolledObject.name);
            }
        }
    }

    void Start()
    {
        manager = GetComponentInParent<WalcarkaManager>();
    }

    void RecalculatePoints()
    {
        float L1 = info.realLength;
        float W1 = info.realWidth;
        float T1 = info.realThickness;
        float V = L1 * W1 * T1;

        float L2 = L1;

        float T2 = manager.rollerGap;
        if (T1 <= T2)
        {
            Debug.Log("MA£A GRUBOŒÆ: " + rolledObject.name);
            End();
            rolledObject.transform.position += transform.forward * 1.0f;
            return;
        }
        else
        {
            L2 = V / (W1 * T2);
        }
        float startOffset = (L1 / 2f) + safetyMargin;
        float exitOffset = (L2 / 2f) + safetyMargin;

        enterPoint.position = centerPoint.position + transform.forward * startOffset;
        exitPoint.position = centerPoint.position - transform.forward * exitOffset;

        enterPoint.rotation = transform.rotation;
        exitPoint.rotation = transform.rotation;

        startScale = rolledObject.transform.localScale;

        float newScaleZ = L2 / info.meshSize.z;
        float newScaleY = T2 / info.meshSize.y;
        targetScale = new Vector3(startScale.x, newScaleY, newScaleZ);
    }

    void Stabilize()
    {
        rolledObject.transform.SetPositionAndRotation(Vector3.MoveTowards(
            rolledObject.transform.position,
            enterPoint.position,
            stabilizationSpeed * Time.fixedDeltaTime
        ), Quaternion.RotateTowards(
            rolledObject.transform.rotation,
            enterPoint.rotation,
            stabilizationSpeed * 40f * Time.fixedDeltaTime
        ));

        float distance = Vector3.Distance(rolledObject.transform.position, enterPoint.position);
        float angle = Quaternion.Angle(rolledObject.transform.rotation, enterPoint.rotation);

        if (distance < 0.001f && angle < 0.1f)
        {
            rolledObject.transform.SetPositionAndRotation(enterPoint.position, enterPoint.rotation);
            isStabilizing = false;
        }
    }

    void Roll()
    {
        float vEntry = manager.rollerSpeed * manager.rollerRadius;

        float T1 = startScale.y * info.meshSize.y; 
        float T2 = manager.rollerGap;            

        float vExit = vEntry * (T1 / T2);

        float vAverage = (vEntry + vExit) / 2f;

        rolledObject.transform.position = Vector3.MoveTowards(
            rolledObject.transform.position,
            exitPoint.position,
            vAverage * processSpeedMultiplier * Time.fixedDeltaTime
        );

        float totalDist = Vector3.Distance(enterPoint.position, exitPoint.position);
        float currentDist = Vector3.Distance(rolledObject.transform.position, exitPoint.position);
        float progress = 1f - (currentDist / totalDist);

        rolledObject.transform.localScale = Vector3.Lerp(startScale, targetScale, progress);

        if (currentDist < 0.001f)
        {
            End();
        }

    }

    void End()
    {
        rolledObject.transform.SetPositionAndRotation(exitPoint.position, exitPoint.rotation);

        var rb = rolledObject.GetComponent<Rigidbody>();
        var grab = rolledObject.GetComponent<XRGrabInteractable>();

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
        }
        if (grab != null)
            grab.enabled = true;

        if (topRollerCollider != null)
            topRollerCollider.enabled = true;

        if (bottomRollerCollider != null)
            bottomRollerCollider.enabled = true;

        isProcessing = false;
        rolledObject = null;
        Debug.Log("KONIEC PROCESU");
    }

    void FixedUpdate()
    {
        if (!manager.powerOn)
        {
            return;
        }

        if (!isProcessing || rolledObject == null)
        {
            return;
        }

        if (isStabilizing)
        {
            Stabilize();
        }
        else
        {
            Roll();
        }
    }
}