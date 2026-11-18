using UnityEngine;

public class ButtonToggle : MonoBehaviour
{
    public Material materialOff;
    public Material materialOn;

    public void Toggle()
    {
        Renderer targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null) return;
        Material[] materials = targetRenderer.sharedMaterials;
        if (materials.Length == 0) return;
        int slot = materials.Length > 1 ? 1 : 0;
        SetState(materials[slot] != materialOn);
    }

    public void SetState(bool isOn)
    {
        if (!isActiveAndEnabled || materialOff == null || materialOn == null) return;
        Renderer targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null) return;
        Material[] materials = targetRenderer.sharedMaterials;
        if (materials.Length == 0) return;
        int slot = materials.Length > 1 ? 1 : 0;
        Material material = isOn ? materialOn : materialOff;
        if (materials[slot] == material) return;
        materials[slot] = material;
        targetRenderer.sharedMaterials = materials;
    }
}
