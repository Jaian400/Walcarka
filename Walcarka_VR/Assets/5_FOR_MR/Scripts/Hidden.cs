using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class Hidden : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private GameObject instruction;
    [SerializeField] private bool hideOnStart = true;

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
        }
    }

    public void ShowInstruction()
    {
        if (instruction != null)
            instruction.SetActive(true);
    }

    public void OnPointerClick(PointerEventData eventData) => ShowInstruction();

    private void OnMouseDown() => ShowInstruction();
}
