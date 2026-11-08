using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using XCharts.Runtime;

[DisallowMultipleComponent]
public sealed class StationPanelNavigator : MonoBehaviour
{
    [Serializable]
    public sealed class ChartPage
    {
        [Tooltip("Stable identity, independent of the prefab name or list position.")]
        public string pageId;
        public string displayName;
        [Tooltip("Drag a prefab asset from the Project window, not a scene instance.")]
        public GameObject prefab;

        public ChartPage(string id, string label)
        {
            pageId = id;
            displayName = label;
        }
    }

    [Serializable]
    public sealed class PageChangedEvent : UnityEvent<string> { }

    [Header("Your panel objects")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private TMP_Text pageLabel;
    [Tooltip("Optional. Hidden when fewer than two pages are configured.")]
    [SerializeField] private GameObject previousArrow;
    [SerializeField] private GameObject nextArrow;

    [Header("Chart prefab list (order = navigation order)")]
    [SerializeField] private ChartPage[] pages =
    {
        new ChartPage("velocity", "Velocity"),
        new ChartPage("current", "Current"),
        new ChartPage("torque", "Torque")
    };
    [SerializeField] private string initialPageId = "velocity";

    [Header("Chart size inside Content (metres at panel scale 1)")]
    [SerializeField] private bool fitToContentSize = true;
    [SerializeField] private Vector2 contentSize = new Vector2(0.6f, 0.4f);

    [Header("Optional event: selected page ID")]
    [SerializeField] private PageChangedEvent onPageChanged = new PageChangedEvent();

    private readonly List<GameObject> instances = new List<GameObject>();
    private GameObject runtimeRoot;
    private int currentIndex = -1;
    private bool initialized;

    public int PageCount => instances.Count;
    public string CurrentPageId => currentIndex >= 0 ? pages[currentIndex].pageId : string.Empty;

    private void Awake()
    {
        if (Application.isPlaying) BuildPages();
    }

    private void OnEnable()
    {
        if (runtimeRoot != null && initialized) runtimeRoot.SetActive(true);
    }

    private void OnDisable()
    {
        if (runtimeRoot != null) runtimeRoot.SetActive(false);
    }

    private void BuildPages()
    {
        if (!ValidateConfiguration(out string error))
        {
            ShowUnavailable(error);
            return;
        }

        if (pages.Length == 0)
        {
            ShowUnavailable("No charts configured");
            return;
        }

        runtimeRoot = new GameObject("RuntimeChartPages");
        runtimeRoot.SetActive(false);
        runtimeRoot.transform.SetParent(contentRoot, false);
        string instanceId = Guid.NewGuid().ToString("N");

        try
        {
            for (int i = 0; i < pages.Length; i++)
            {
                GameObject instance = Instantiate(pages[i].prefab, runtimeRoot.transform, false);
                instance.SetActive(false);
                instance.name = pages[i].prefab.name;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                BaseChart[] charts = instance.GetComponentsInChildren<BaseChart>(true);
                for (int chartIndex = 0; chartIndex < charts.Length; chartIndex++)
                    charts[chartIndex].chartName = $"StationPanel_{instanceId}_{i}_{chartIndex}";

                if (fitToContentSize) FitPage(instance);
                instances.Add(instance);
            }
        }
        catch (Exception exception)
        {
            runtimeRoot.SetActive(false);
            Destroy(runtimeRoot);
            runtimeRoot = null;
            instances.Clear();
            ShowUnavailable("Could not create chart pages");
            Debug.LogException(exception, this);
            return;
        }

        int initialIndex = Array.FindIndex(pages, page => page.pageId == initialPageId);
        if (initialIndex < 0)
        {
            initialIndex = 0;
            Debug.LogWarning($"[{nameof(StationPanelNavigator)}] Initial page '{initialPageId}' was not found; using '{pages[0].pageId}'.", this);
        }

        initialized = true;
        SetNavigationVisible(instances.Count > 1);
        ShowPage(initialIndex);
        runtimeRoot.SetActive(isActiveAndEnabled);
    }

    private bool ValidateConfiguration(out string error)
    {
        error = null;
        if (contentRoot == null || contentRoot == transform || !contentRoot.IsChildOf(transform))
            error = "Assign a Content child of this panel";
        else if (pages == null)
            error = "Assign the chart page list";
        else if (fitToContentSize &&
                 (!IsPositiveFinite(contentSize.x) || !IsPositiveFinite(contentSize.y)))
            error = "Content Size must contain positive finite dimensions";

        if (error != null) return false;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < pages.Length; i++)
        {
            ChartPage page = pages[i];
            if (page == null || string.IsNullOrWhiteSpace(page.pageId) || !ids.Add(page.pageId))
                error = $"Page {i + 1} needs a nonempty, unique Page Id";
            else if (page.prefab == null || page.prefab.scene.IsValid())
                error = $"Page {i + 1} needs a prefab asset from the Project window";

            if (error != null) return false;
        }
        return true;
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void FitPage(GameObject instance)
    {
        Canvas canvas = instance.GetComponentInChildren<Canvas>(true);
        RectTransform rect = canvas != null ? canvas.transform as RectTransform : null;
        if (rect == null)
        {
            Debug.LogWarning($"[{nameof(StationPanelNavigator)}] '{instance.name}' has no Canvas to fit; keeping its authored size.", this);
            return;
        }

        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var bounds = new Bounds(instance.transform.InverseTransformPoint(corners[0]), Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            bounds.Encapsulate(instance.transform.InverseTransformPoint(corners[i]));

        if (!IsPositiveFinite(bounds.size.x) || !IsPositiveFinite(bounds.size.y))
        {
            Debug.LogWarning($"[{nameof(StationPanelNavigator)}] '{instance.name}' has no usable Canvas bounds; keeping its authored size.", this);
            return;
        }

        float scale = Mathf.Min(contentSize.x / bounds.size.x, contentSize.y / bounds.size.y);
        instance.transform.localScale = Vector3.one * scale;
        instance.transform.localPosition = -bounds.center * scale;
    }

    public void NextPage()
    {
        if (CanNavigate()) ShowPage((currentIndex + 1) % instances.Count);
    }

    public void PreviousPage()
    {
        if (CanNavigate()) ShowPage((currentIndex + instances.Count - 1) % instances.Count);
    }

    public void SetPageById(string pageId)
    {
        if (!CanNavigate()) return;
        int index = Array.FindIndex(pages, page => page.pageId == pageId);
        if (index >= 0) ShowPage(index);
        else Debug.LogWarning($"[{nameof(StationPanelNavigator)}] Unknown page ID '{pageId}'.", this);
    }

    private bool CanNavigate()
    {
        return Application.isPlaying && isActiveAndEnabled && initialized && instances.Count > 0;
    }

    private void ShowPage(int index)
    {
        if (currentIndex == index) return;
        if (currentIndex >= 0) instances[currentIndex].SetActive(false);
        currentIndex = index;
        instances[currentIndex].SetActive(true);

        ChartPage page = pages[currentIndex];
        string title = string.IsNullOrWhiteSpace(page.displayName) ? page.prefab.name : page.displayName;
        if (pageLabel != null) 
        {
            pageLabel.text = $"Panel {currentIndex + 1}/{instances.Count}";
        }
        onPageChanged?.Invoke(page.pageId);
    }

    private void SetNavigationVisible(bool visible)
    {
        if (previousArrow != null) previousArrow.SetActive(visible);
        if (nextArrow != null) nextArrow.SetActive(visible);
    }

    private void ShowUnavailable(string message)
    {
        if (pageLabel != null) pageLabel.text = message;
        SetNavigationVisible(false);
        Debug.LogWarning($"[{nameof(StationPanelNavigator)}] {message}", this);
    }

}
