using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ArrowClickFeedback : MonoBehaviour
{
    [Tooltip("Assign the visual model child so the collider and interaction root stay stationary.")]
    [SerializeField] private Transform visualTarget = null;
    [Tooltip("Optional. Defaults to the first renderer under Visual Target.")]
    [SerializeField] private Renderer arrowRenderer = null;
    [SerializeField, Range(0.8f, 1f)] private float pressedScale = 0.94f;
    [SerializeField, Range(0f, 1f)] private float lightenAmount = 0.3f;
    [SerializeField, Min(0.01f)] private float duration = 0.2f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private Coroutine animationRoutine;
    private Transform activeVisual;
    private Renderer activeRenderer;
    private Vector3 originalScale;
    private Color originalColor;
    private Color pressedColor;
    private int colorProperty;
    private MaterialPropertyBlock originalProperties;
    private MaterialPropertyBlock animatedProperties;
    private bool hasColor;

    public void PlayClick()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;

        StopAndRestore();
        activeVisual = visualTarget != null ? visualTarget : transform;
        originalScale = activeVisual.localScale;
        activeRenderer = arrowRenderer != null
            ? arrowRenderer : activeVisual.GetComponentInChildren<Renderer>();
        CaptureColor();
        animationRoutine = StartCoroutine(Animate());
    }

    private void CaptureColor()
    {
        hasColor = false;
        if (activeRenderer == null || activeRenderer.sharedMaterial == null) return;

        var material = activeRenderer.sharedMaterial;
        colorProperty = material.HasProperty(BaseColorId) ? BaseColorId : ColorId;
        if (!material.HasProperty(colorProperty)) return;

        originalProperties = new MaterialPropertyBlock();
        animatedProperties = new MaterialPropertyBlock();
        activeRenderer.GetPropertyBlock(originalProperties);
        activeRenderer.GetPropertyBlock(animatedProperties);
        originalColor = originalProperties.HasColor(colorProperty)
            ? originalProperties.GetColor(colorProperty) : material.GetColor(colorProperty);
        pressedColor = Color.Lerp(originalColor,
            new Color(1f, 1f, 1f, originalColor.a), Mathf.Clamp01(lightenAmount));
        hasColor = true;
    }

    private IEnumerator Animate()
    {
        float elapsed = 0f;
        float animationDuration = Mathf.Max(0.01f, duration);
        while (elapsed < animationDuration)
        {
            float weight = Mathf.Sin(Mathf.PI * elapsed / animationDuration);
            if (activeVisual != null)
                activeVisual.localScale = originalScale * Mathf.Lerp(1f, pressedScale, weight);
            if (hasColor && activeRenderer != null)
            {
                animatedProperties.SetColor(colorProperty, Color.Lerp(originalColor, pressedColor, weight));
                activeRenderer.SetPropertyBlock(animatedProperties);
            }
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        Restore();
        animationRoutine = null;
    }

    private void OnDisable() => StopAndRestore();

    private void StopAndRestore()
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = null;
        Restore();
    }

    private void Restore()
    {
        if (activeVisual != null) activeVisual.localScale = originalScale;
        if (hasColor && activeRenderer != null)
            activeRenderer.SetPropertyBlock(originalProperties.isEmpty ? null : originalProperties);
        activeVisual = null;
        activeRenderer = null;
        hasColor = false;
    }
}
