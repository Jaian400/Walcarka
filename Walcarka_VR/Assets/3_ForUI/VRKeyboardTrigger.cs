using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

public class VRKeyboardTrigger : MonoBehaviour, ISelectHandler, IPointerClickHandler
{
    private TMP_InputField inputField;
    private TouchScreenKeyboard keyboard;

    void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OpenKeyboard();
    }

    public void OnSelect(BaseEventData eventData)
    {
        OpenKeyboard();
    }

    private void OpenKeyboard()
    {
        if (keyboard == null || !keyboard.active)
        {
            keyboard = TouchScreenKeyboard.Open(inputField.text, TouchScreenKeyboardType.Default);
            inputField.ActivateInputField(); 
        }
    }

    void Update()
    {
        if (keyboard != null && keyboard.active)
        {
            inputField.text = keyboard.text;
        }
    }
}