using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ButtonCustom : MonoBehaviour
{
    public Material bodyMaterial;
    public Material frontMaterial;
    public UnityEvent onClick = new UnityEvent();

    [SerializeField, Tooltip("Use the local +Z face instead of -Z for the front material.")]
    private bool frontOnPositiveZ;
    [SerializeField, Range(0.8f, 1f)] private float pressedScale = 0.94f;
    [SerializeField, Min(0.01f)] private float clickDuration = 0.16f;

    private Coroutine clickAnimation;
    private Vector3 originalScale;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh originalMesh;
    private Mesh buttonMesh;
    private Material[] originalMaterials;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshFilter == null || meshRenderer == null || meshFilter.sharedMesh == null) return;

        originalMesh = meshFilter.sharedMesh;
        if (!originalMesh.isReadable)
        {
            Debug.LogWarning("[ButtonCustom] Enable Read/Write on the button mesh to separate its front material.", this);
            return;
        }

        var bodyTriangles = new List<int>();
        var frontTriangles = new List<int>();
        SplitTriangles(originalMesh.vertices, originalMesh.triangles, frontOnPositiveZ, bodyTriangles, frontTriangles);
        if (frontTriangles.Count == 0)
        {
            Debug.LogWarning("[ButtonCustom] The mesh has no face pointing toward the selected local Z direction.", this);
            return;
        }

        originalMaterials = meshRenderer.sharedMaterials;
        Material body = bodyMaterial != null ? bodyMaterial : meshRenderer.sharedMaterial;
        Material front = frontMaterial != null ? frontMaterial : body;
        buttonMesh = Instantiate(originalMesh);
        buttonMesh.name = originalMesh.name + " (Button Materials)";
        buttonMesh.subMeshCount = 2;
        buttonMesh.SetTriangles(bodyTriangles, 0, false);
        buttonMesh.SetTriangles(frontTriangles, 1, false);
        meshFilter.sharedMesh = buttonMesh;
        meshRenderer.sharedMaterials = new[] { body, front };
    }

    private static void SplitTriangles(Vector3[] vertices, int[] triangles, bool positiveZ,
        List<int> body, List<int> front)
    {
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 normal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                vertices[triangles[i + 2]] - vertices[triangles[i]]);
            float facing = positiveZ ? normal.z : -normal.z;
            List<int> target = facing > 0f && facing * facing >= 0.99f * normal.sqrMagnitude ? front : body;
            target.Add(triangles[i]);
            target.Add(triangles[i + 1]);
            target.Add(triangles[i + 2]);
        }
    }

    public void OnClick()
    {
        if (!isActiveAndEnabled) return;

        if (clickAnimation != null) StopCoroutine(clickAnimation);
        else originalScale = transform.localScale;

        transform.localScale = originalScale;
        clickAnimation = StartCoroutine(AnimateClick());
        onClick?.Invoke();
    }

    private IEnumerator AnimateClick()
    {
        float duration = Mathf.Max(0.01f, clickDuration);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float pulse = Mathf.Sin(Mathf.PI * elapsed / duration);
            transform.localScale = originalScale * Mathf.Lerp(1f, pressedScale, pulse);
            yield return null;
        }
        transform.localScale = originalScale;
        clickAnimation = null;
    }

    private void OnDisable()
    {
        if (clickAnimation == null) return;
        StopCoroutine(clickAnimation);
        clickAnimation = null;
        transform.localScale = originalScale;
    }

    private void OnDestroy()
    {
        if (buttonMesh == null) return;
        if (meshFilter != null && meshFilter.sharedMesh == buttonMesh)
        {
            meshFilter.sharedMesh = originalMesh;
            if (meshRenderer != null) meshRenderer.sharedMaterials = originalMaterials;
        }
        Destroy(buttonMesh);
    }
}
