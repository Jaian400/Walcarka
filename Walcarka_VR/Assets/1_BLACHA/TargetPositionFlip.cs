using UnityEngine;

public class TargetPositionFlip : MonoBehaviour
{
    [Header("Odleg³oœæ InfoMeasurments nad obiektem")]
    [SerializeField] private float targetDist = 1.0f;
    private float dist;

    void Start()
    {
        dist = targetDist / transform.parent.localScale.y;
    }

    void Update()
    {
        float worldYAbove = transform.parent.TransformPoint(0, dist, 0).y;
        float worldYBelow = transform.parent.TransformPoint(0, -dist, 0).y;

        if (worldYAbove > worldYBelow)
        {
            transform.localPosition = new Vector3(0, dist, 0);
        }
        else
        {
            transform.localPosition = new Vector3(0, -dist, 0);
        }
    }
}
