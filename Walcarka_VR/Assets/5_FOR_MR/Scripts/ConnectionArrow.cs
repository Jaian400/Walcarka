using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class ConnectionArrow : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private ConnectionServiceMR connectionService;
    [SerializeField] private Renderer arrowRenderer;
    [SerializeField] private Material disconnectedMaterial;
    [SerializeField] private Material connectingMaterial;
    [SerializeField] private Material connectedMaterial;
    [SerializeField] private GameObject panel;
    [SerializeField] private bool hidePanelOnStart = true;

    private void OnEnable()
    {
        if (connectionService == null)
            connectionService = FindFirstObjectByType<ConnectionServiceMR>();
        if (connectionService != null)
            connectionService.OnConnectionStateChanged += ApplyState;
        ApplyState(connectionService != null ? connectionService.State : MRConnectionState.Disconnected);
    }

    private void Start()
    {
        if (hidePanelOnStart && CanTogglePanel()) panel.SetActive(false);
    }

    private void OnDisable()
    {
        if (connectionService != null)
            connectionService.OnConnectionStateChanged -= ApplyState;
    }

    private void ApplyState(MRConnectionState state)
    {
        Material material = state == MRConnectionState.Connected ? connectedMaterial
            : state == MRConnectionState.Connecting ? connectingMaterial : disconnectedMaterial;
        if (arrowRenderer != null && material != null) arrowRenderer.sharedMaterial = material;
    }

    // Wire this method to the Meta InteractableUnityEventWrapper select event.
    public void TogglePanel()
    {
        if (isActiveAndEnabled && CanTogglePanel()) panel.SetActive(!panel.activeSelf);
    }

    private bool CanTogglePanel() => panel != null && !transform.IsChildOf(panel.transform);

    public void OnPointerClick(PointerEventData eventData) => TogglePanel();
}
