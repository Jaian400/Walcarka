using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class Hidden : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private GameObject instruction;
    [SerializeField] private bool hideOnStart = true;
    [SerializeField, Range(0.8f, 1f)] private float pressedScale = 0.94f;
    [SerializeField, Min(0.01f)] private float clickDuration = 0.16f;
    [SerializeField, Range(0.5f, 1f)] private float revealScale = 0.96f;
    [SerializeField, Min(0.01f)] private float revealDuration = 0.22f;

    private Coroutine clickRoutine;
    private Coroutine revealRoutine;
    private Vector3 boxScale;
    private Vector3 instructionScale;
    private bool instructionVisible;

    private void Awake()
    {
        boxScale = transform.localScale;
        if (instruction != null)
        {
            instructionScale = instruction.transform.localScale;
            instructionVisible = instruction.activeSelf;
        }
    }

    private void Start()
    {
        if (instruction == null)
        {
            return;
        }

        if (hideOnStart)
        {
            if (transform.IsChildOf(instruction.transform))
            {
                return;
            }

            instruction.SetActive(false);
            instructionVisible = false;
        }
    }

    public void ShowInstruction()
    {
        if (!isActiveAndEnabled) return;

        if (clickRoutine != null) StopCoroutine(clickRoutine);
        clickRoutine = StartCoroutine(AnimateClick());

        if (instruction == null) return;
        if (revealRoutine != null)
        {
            StopCoroutine(revealRoutine);
            revealRoutine = null;
        }

        instructionVisible = !instructionVisible;
        if (instructionVisible && !instruction.activeSelf)
        {
            instruction.transform.localScale = instructionScale * revealScale;
            instruction.SetActive(true);
        }
        revealRoutine = StartCoroutine(AnimateReveal(instructionVisible));
    }

    private IEnumerator AnimateClick()
    {
        float duration = Mathf.Max(0.01f, clickDuration);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float pulse = Mathf.Sin(Mathf.PI * elapsed / duration);
            transform.localScale = boxScale * Mathf.Lerp(1f, pressedScale, pulse);
            yield return null;
        }
        transform.localScale = boxScale;
        clickRoutine = null;
    }

    private IEnumerator AnimateReveal(bool show)
    {
        float duration = Mathf.Max(0.01f, revealDuration);
        Vector3 startScale = instruction.transform.localScale;
        Vector3 endScale = instructionScale * (show ? 1f : revealScale);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            if (instruction == null) break;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            instruction.transform.localScale = Vector3.Lerp(startScale, endScale, progress);
            yield return null;
        }
        if (instruction != null && !show) instruction.SetActive(false);
        RestoreInstruction();
        revealRoutine = null;
    }

    private void OnDisable()
    {
        if (clickRoutine != null) StopCoroutine(clickRoutine);
        if (revealRoutine != null) StopCoroutine(revealRoutine);
        clickRoutine = null;
        revealRoutine = null;
        transform.localScale = boxScale;
        if (instruction != null && !transform.IsChildOf(instruction.transform))
            instruction.SetActive(instructionVisible);
        RestoreInstruction();
    }

    private void RestoreInstruction()
    {
        if (instruction != null) instruction.transform.localScale = instructionScale;
    }

    public void OnPointerClick(PointerEventData eventData) => ShowInstruction();

    private void OnMouseDown() => ShowInstruction();
}
