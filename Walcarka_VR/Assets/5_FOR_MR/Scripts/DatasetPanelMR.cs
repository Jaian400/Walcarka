using System;
using System.Globalization;
using System.Threading;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DatasetPanelMR : MonoBehaviour
{
    [SerializeField] private ConnectionServiceMR connectionService;
    [SerializeField] private TMP_Text datetimeText;
    [SerializeField] private ButtonToggle autoSendButton;

    private MRDatasetInfo[] datasets = Array.Empty<MRDatasetInfo>();
    private CancellationTokenSource refreshCancellation;
    private bool refreshing;
    private int currentIndex = -1;
    private string selectedDatetime;
    private bool autoSend = true;

    private void OnEnable()
    {
        if (connectionService == null) connectionService = FindFirstObjectByType<ConnectionServiceMR>();
        if (connectionService == null)
        {
            ClearList();
            return;
        }
        connectionService.OnConnectionStateChanged += OnConnectionChanged;
        connectionService.OnDatasetFinished += OnDatasetFinished;
        UseDatasetList(connectionService.AvailableDatasets);
    }

    private void OnDisable()
    {
        if (connectionService != null)
        {
            connectionService.OnConnectionStateChanged -= OnConnectionChanged;
            connectionService.OnDatasetFinished -= OnDatasetFinished;
        }
        CancelRefresh();
    }

    private void LateUpdate()
    {
        if (connectionService != null && autoSendButton != null)
            autoSendButton.SetState(autoSend);
    }

    public async void RefreshDatasets()
    {
        if (!isActiveAndEnabled || refreshing || connectionService == null ||
            !connectionService.IsConnected || connectionService.IsLoading) return;
        CancelRefresh();
        var owner = new CancellationTokenSource();
        refreshCancellation = owner;
        CancellationToken token = owner.Token;
        refreshing = true;
        try
        {
            MRDatasetInfo[] available = await connectionService.GetDatasetsAsync(token);
            token.ThrowIfCancellationRequested();
            if (this == null || !isActiveAndEnabled) return;
            UseDatasetList(available);
            if (currentIndex < 0) return;
            if (autoSend) await connectionService.FollowLatestAsync();
            else await connectionService.SelectDatasetAsync(datasets[currentIndex]);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (this != null && isActiveAndEnabled)
                Debug.LogWarning($"[{nameof(DatasetPanelMR)}] {exception.Message}", this);
        }
        finally
        {
            if (refreshCancellation == owner)
            {
                refreshCancellation = null;
                refreshing = false;
                owner.Dispose();
            }
        }
    }

    private bool CanSelect => isActiveAndEnabled && !refreshing && datasets.Length > 0 &&
        connectionService != null && connectionService.IsConnected && !connectionService.IsLoading;

    public void PreviousDataset() => MoveSelection(-1);
    public void NextDataset() => MoveSelection(1);

    private async void MoveSelection(int direction)
    {
        if (!CanSelect || datasets.Length < 2) return;
        autoSend = false;
        currentIndex = currentIndex < 0 ? (direction > 0 ? 0 : datasets.Length - 1)
            : (currentIndex + direction + datasets.Length) % datasets.Length;
        ShowDatetime();
        try { await connectionService.PauseAutoSendAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (this == null || !isActiveAndEnabled) return;
            Debug.LogWarning($"[{nameof(DatasetPanelMR)}] {exception.Message}", this);
        }
    }

    public async void ToggleAutoSend()
    {
        if (!CanSelect) return;
        autoSend = !autoSend;
        if (autoSend)
        {
            currentIndex = datasets.Length - 1;
            ShowDatetime();
        }
        try
        {
            if (!autoSend) await connectionService.PauseAutoSendAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (this == null || !isActiveAndEnabled) return;
            Debug.LogWarning($"[{nameof(DatasetPanelMR)}] {exception.Message}", this);
        }
    }

    private void UseDatasetList(MRDatasetInfo[] available)
    {
        datasets = available;
        string selection = selectedDatetime ?? connectionService.SelectedDatetime;
        currentIndex = autoSend ? datasets.Length - 1 : Array.FindIndex(datasets, item => item.datetime == selection);
        if (currentIndex < 0 && selection == null && datasets.Length > 0) currentIndex = datasets.Length - 1;
        ShowDatetime();
    }

    private void ShowDatetime()
    {
        bool selected = currentIndex >= 0 && currentIndex < datasets.Length;
        if (selected) selectedDatetime = datasets[currentIndex].datetime;
        if (datetimeText == null) return;
        string value = selected ? selectedDatetime : "--";
        if (DateTime.TryParseExact(value, "yyyy_MM_dd_HH_mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime parsed))
            value = parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        datetimeText.text = value;
    }

    private void OnConnectionChanged(MRConnectionState state)
    {
        if (state == MRConnectionState.Connected)
        {
            UseDatasetList(connectionService.AvailableDatasets);
            return;
        }
        CancelRefresh();
        ClearList();
    }

    private void OnDatasetFinished(bool complete)
    {
        if (!complete && connectionService.IsConnected) autoSend = connectionService.FollowLatest;
    }

    private void ClearList()
    {
        datasets = Array.Empty<MRDatasetInfo>();
        currentIndex = -1;
        ShowDatetime();
    }

    private void CancelRefresh()
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = null;
        refreshing = false;
    }
}
