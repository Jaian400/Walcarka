using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StationQrPlacer : MonoBehaviour
{
    public enum PlacementMode { QR = 0, Manual = 1, Auto = 2 }
    public enum PlacementState { Idle, Placing, AwaitingTriggerRelease, Placed, ScanningQr, DetectingMode }

    [Header("Assign scene objects")]
    [SerializeField] private MRUK mruk;
    [SerializeField] private Transform stationRoot;
    [SerializeField] private Transform reference;
    [SerializeField] private string expectedPayload = "Walcarka";
    [SerializeField] private PlacementMode placementMode = PlacementMode.Auto;

    [Header("Manual placement")]
    [SerializeField] private Transform controllerOrigin;
    [SerializeField] private Transform head;
    [SerializeField] private Transform placementPreview;
    [SerializeField] private float initialDistance = 1.5f;
    [SerializeField] private float minDistance = 0.5f;
    [SerializeField] private float maxDistance = 4f;
    [SerializeField] private float distanceSpeed = 1f;
    [SerializeField] private float rotationSpeed = 60f;
    [SerializeField] private float stickDeadzone = 0.2f;
    [SerializeField] private float manualFacingYawOffset;

    [Header("Optional calibration in QR-local coordinates")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;
    [SerializeField] private bool hideUntilPlaced = true;
    [SerializeField] private bool hideReferenceAfterPlacement = true;

    public PlacementState State { get; private set; }
    public PlacementMode ActivePlacementMode { get; private set; } = PlacementMode.Auto;
    public bool HasPlaced => hasConfirmedPose && State == PlacementState.Placed;
    public string Status { get; private set; }
    public int UpdateCount { get; private set; }
    public bool InputPaused => paused;
    public bool InputFocused => focused;
    public bool UnityFocused => unityFocused;
    public bool UsingXrFocus { get; private set; }
    public string LastShortcut { get; private set; } = "No X/Y press received.";

    private const string ManualReadyMessage = "Ręczne umieszczanie stacji.";
    private readonly List<MRUKTrackable> trackables = new List<MRUKTrackable>();
    private MRUKTrackable selected;
    private Vector3 referenceLocalPosition;
    private Quaternion referenceLocalRotation;
    private Pose confirmedPose;
    private Pose candidatePose;
    private bool hasConfirmedPose;
    private bool hasCandidate;
    private bool stationWasActive;
    private bool started;
    private bool scanning;
    private bool enabledTracker;
    private bool triggerArmed;
    private bool paused;
    private bool focused = true;
    private bool unityFocused = true;
    private bool focusSubscribed;
    private float distance;
    private float userYaw;
    private float facingYaw;
    private string manualMessage = ManualReadyMessage;

    private bool IsManualPlacement => State == PlacementState.Placing || State == PlacementState.AwaitingTriggerRelease;

    private void Start()
    {
        started = true;
        StartStartupPlacement();
    }

    private void OnEnable()
    {
        if (Application.isPlaying && !focusSubscribed)
        {
            OVRManager.InputFocusAcquired += OnXrFocusAcquired;
            OVRManager.InputFocusLost += OnXrFocusLost;
            focusSubscribed = true;
            RefreshInputFocus();
        }
        if (started && !HasPlaced) StartStartupPlacement();
    }

    private void StartStartupPlacement()
    {
        if (placementMode == PlacementMode.Manual) WaitForManualPlacement();
        else RestartPlacement();
    }

    private void WaitForManualPlacement()
    {
        StopPlacement();
        if (!ValidateConfigurationForMode(PlacementMode.Auto, out string error))
        {
            Report(error, true);
            return;
        }
        ActivePlacementMode = PlacementMode.Manual;
        if (placementPreview != null && placementPreview.parent == null && placementPreview != stationRoot &&
            !transform.IsChildOf(placementPreview)) placementPreview.gameObject.SetActive(false);
        if (!hasConfirmedPose && hideUntilPlaced) stationRoot.gameObject.SetActive(false);
        Report("Naciśnij X na lewym kontrolerze, aby ustawić stację. Y: skanuj QR.");
    }

    public void PlaceAgain()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        RestartPlacement();
    }

    private void RestartPlacement()
    {
        RestartPlacementForMode(placementMode);
    }

    public void PlaceManually()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        RestartPlacementForMode(PlacementMode.Manual);
    }

    public void PlaceFromQr()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        TryRestartQrPlacement();
    }

    private void RestartPlacementForMode(PlacementMode mode)
    {
        StopPlacement();
        if (!ValidateConfigurationForMode(mode, out string error))
        {
            Report(error, true);
            return;
        }
        referenceLocalPosition = stationRoot.InverseTransformPoint(reference.position);
        referenceLocalRotation = Quaternion.Inverse(stationRoot.rotation) * reference.rotation;
        ActivePlacementMode = mode;
        if (mode == PlacementMode.Auto)
        {
            stationWasActive = stationRoot.gameObject.activeSelf;
            State = PlacementState.DetectingMode;
            if (hideUntilPlaced) stationRoot.gameObject.SetActive(false);
            Report("Wykrywanie zestawu: Quest 3 — kod QR, Quest 2 — oczekiwanie na X.");
        }
        else StartResolvedPlacement(mode);
    }

    private bool TryRestartQrPlacement()
    {
        if (!ValidateConfigurationForMode(PlacementMode.QR, out string error))
        {
            Report(error, true);
            return false;
        }
        try
        {
            if (!mruk.QRCodeTrackingSupported)
            {
                Report("Ten zestaw nie obsługuje QR. Użyj X, aby ustawić stację ręcznie.", true);
                return false;
            }
        }
        catch (Exception exception)
        {
            Report("QR nie jest gotowy: " + exception.Message, true);
            return false;
        }
        RestartPlacementForMode(PlacementMode.QR);
        return State == PlacementState.ScanningQr;
    }

    private bool ValidateConfiguration(out string error) => ValidateConfigurationForMode(placementMode, out error);

    private bool ValidateConfigurationForMode(PlacementMode mode, out string error)
    {
        error = null;
        if (stationRoot == null || reference == null || reference == stationRoot ||
            !reference.IsChildOf(stationRoot) || !stationRoot.gameObject.scene.IsValid() || transform.IsChildOf(stationRoot))
            error = "Przypisz StationRoot i jego Reference. Komponent umieszczania musi być poza StationRoot.";
        else if (stationRoot.parent != null)
            error = "StationRoot musi być obiektem głównym sceny, poza kamerą i kontrolerami.";
        else if (!IsFinite(stationRoot.lossyScale) || (stationRoot.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
            error = "StationRoot musi mieć skalę (1, 1, 1). Skaluj tylko obiekty wizualne.";
        else if (!IsFinite(stationRoot.InverseTransformPoint(reference.position)))
            error = "Reference ma nieprawidłową pozycję.";
        else if (mode == PlacementMode.Manual)
        {
            if (controllerOrigin == null || head == null || placementPreview == null ||
                !controllerOrigin.gameObject.activeInHierarchy || !head.gameObject.activeInHierarchy ||
                controllerOrigin.IsChildOf(stationRoot) || head.IsChildOf(stationRoot) ||
                placementPreview.parent != null || !placementPreview.gameObject.scene.IsValid() ||
                placementPreview == stationRoot || transform.IsChildOf(placementPreview) ||
                controllerOrigin.IsChildOf(placementPreview) || head.IsChildOf(placementPreview))
                error = "Przypisz kontroler, kamerę i osobny podgląd w głównym poziomie sceny.";
            else if (!IsFinite(placementPreview.lossyScale) || (placementPreview.lossyScale - Vector3.one).sqrMagnitude > 0.000001f ||
                     !IsFinite(initialDistance) || !IsFinite(minDistance) || !IsFinite(maxDistance) ||
                     minDistance <= 0f || maxDistance < minDistance || initialDistance < minDistance || initialDistance > maxDistance ||
                     !IsFinite(distanceSpeed) || distanceSpeed < 0f || !IsFinite(rotationSpeed) || rotationSpeed < 0f ||
                     !IsFinite(stickDeadzone) || stickDeadzone < 0f || stickDeadzone >= 1f || !IsFinite(manualFacingYawOffset))
                error = "Sprawdź skalę podglądu, granice odległości, prędkości, martwą strefę i obrót.";
        }
        else if (mode == PlacementMode.QR)
        {
            if (mruk == null) mruk = MRUK.Instance;
            if (mruk == null || !mruk.isActiveAndEnabled || mruk.transform.IsChildOf(stationRoot) || string.IsNullOrWhiteSpace(expectedPayload))
                error = "Assign an active MRUK outside StationRoot and a nonempty QR payload.";
            else if (!IsFinite(positionOffset) || !IsFinite(rotationOffsetEuler))
                error = "QR calibration must contain finite values.";
        }
        else if (mode != PlacementMode.Auto) error = "Wybierz tryb Auto, Manual lub QR.";
        return error == null;
    }

    private void StartResolvedPlacement(PlacementMode mode)
    {
        if (!ValidateConfigurationForMode(mode, out string error))
        {
            StopPlacement();
            Report(error, true);
            return;
        }
        bool previousVisibility = State == PlacementState.DetectingMode ? stationWasActive : stationRoot.gameObject.activeSelf;
        ActivePlacementMode = mode;
        if (mode == PlacementMode.Manual) BeginManualPlacement();
        else BeginQrPlacement();
        stationWasActive = previousVisibility;
    }

    private static PlacementMode SelectAutomaticMode(bool editor, bool runtimeReady, OVRPlugin.SystemHeadset headset, bool qrSupported)
    {
        if (editor) return PlacementMode.Manual;
        if (!runtimeReady || headset == OVRPlugin.SystemHeadset.None) return PlacementMode.Auto;
        if (headset == OVRPlugin.SystemHeadset.Oculus_Quest || headset == OVRPlugin.SystemHeadset.Oculus_Quest_2 ||
            headset == OVRPlugin.SystemHeadset.Meta_Quest_Pro) return PlacementMode.Manual;
        if (qrSupported) return PlacementMode.QR;
        if (headset == OVRPlugin.SystemHeadset.Meta_Quest_3 || headset == OVRPlugin.SystemHeadset.Meta_Quest_3S)
            return PlacementMode.Auto;
        return PlacementMode.Manual;
    }

    private void UpdateAutomaticPlacement()
    {
        if (paused || !focused) return;
        if (Application.isEditor)
        {
            WaitForManualPlacement();
            return;
        }
        if (!OVRPlugin.initialized) return;
        var headset = OVRPlugin.GetSystemHeadsetType();
        if (headset == OVRPlugin.SystemHeadset.None) return;
        try
        {
            var mode = SelectAutomaticMode(false, true, headset, false);
            if (mode != PlacementMode.Manual)
            {
                if (mruk == null) mruk = MRUK.Instance;
                if (mruk == null || !mruk.isActiveAndEnabled)
                {
                    StopPlacement();
                    Report("Quest 3 wymaga aktywnego MRUK do umieszczenia przez QR.", true);
                    return;
                }
                mode = SelectAutomaticMode(false, true, headset, mruk.QRCodeTrackingSupported);
            }
            if (mode == PlacementMode.Auto)
            {
                Report("Oczekiwanie na obsługę QR w Quest 3. Sprawdź wersję systemu i konfigurację XR.");
                return;
            }
            if (mode == PlacementMode.Manual) WaitForManualPlacement();
            else StartResolvedPlacement(mode);
        }
        catch (Exception exception)
        {
            StopPlacement();
            Report("Nie można uruchomić automatycznego umieszczania: " + exception.Message, true);
        }
    }

    private void BeginManualPlacement()
    {
        stationWasActive = stationRoot.gameObject.activeSelf;
        distance = initialDistance;
        userYaw = 0f;
        facingYaw = stationRoot.eulerAngles.y - manualFacingYawOffset;
        triggerArmed = false;
        hasCandidate = false;
        manualMessage = ManualReadyMessage;
        State = PlacementState.Placing;
        stationRoot.gameObject.SetActive(false);
        placementPreview.gameObject.SetActive(false);
        Report(ManualReadyMessage);
    }

    private void Update()
    {
        UpdateCount++;
        RefreshInputFocus();
        if (ProcessPlacementShortcuts(
                OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch),
                OVRInput.GetDown(OVRInput.RawButton.Y, OVRInput.Controller.LTouch))) return;
        if (State == PlacementState.DetectingMode) UpdateAutomaticPlacement();
        else if (IsManualPlacement)
        {
            if (stationRoot == null || reference == null || controllerOrigin == null || head == null || placementPreview == null)
            {
                StopPlacement();
                Report("Brak obiektu umieszczania. Sprawdź przypisania i naciśnij X.", true);
                return;
            }
            ProcessManualInput(IsControllerTracked(),
                OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch),
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch),
                OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch),
                OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch), Time.unscaledDeltaTime);
        }
        else if (State == PlacementState.ScanningQr) UpdateQrPlacement();
    }

    private bool ProcessPlacementShortcuts(bool manualDown, bool qrDown)
    {
        if (manualDown || qrDown)
        {
            string button = manualDown ? "X" : "Y";
            LastShortcut = $"{button} received: state={State}, paused={paused}, inputFocus={focused}, UnityFocus={unityFocused}, XR focus source={UsingXrFocus}.";
            Debug.Log("[StationQrPlacer] " + LastShortcut, this);
        }
        if (paused || !focused) return false;
        if (manualDown)
        {
            RestartPlacementForMode(PlacementMode.Manual);
            return true;
        }
        if (qrDown)
        {
            TryRestartQrPlacement();
            return true;
        }
        return false;
    }

    private bool IsControllerTracked()
    {
        return !paused && focused && controllerOrigin != null && head != null &&
               controllerOrigin.gameObject.activeInHierarchy && head.gameObject.activeInHierarchy &&
               OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) &&
               OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch) &&
               OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);
    }

    private void ProcessManualInput(bool tracked, bool triggerHeld, bool triggerDown, bool cancelDown, Vector2 stick, float elapsed)
    {
        if (!IsManualPlacement) return;
        if (cancelDown && !paused && focused)
        {
            CancelPlacement();
            if (!IsManualPlacement) return;
        }
        if (!tracked || !IsFinite(controllerOrigin.position) || !IsFinite(controllerOrigin.forward) || !IsFinite(head.position))
        {
            InterruptManualPlacement();
            Report("Brak śledzenia prawego kontrolera. Sprawdź kontroler i zwolnij spust.");
            return;
        }
        if (State == PlacementState.AwaitingTriggerRelease)
        {
            if (!triggerHeld) CompletePlacement(candidatePose, "Ustawiono stację. X: ustaw ręcznie. Y: skanuj QR.");
            return;
        }
        float delta = IsFinite(elapsed) ? Mathf.Clamp(elapsed, 0f, 0.1f) : 0f;
        distance = Mathf.Clamp(distance + ApplyDeadzone(stick.y) * distanceSpeed * delta, minDistance, maxDistance);
        userYaw = Mathf.Repeat(userYaw + ApplyDeadzone(stick.x) * rotationSpeed * delta + 180f, 360f) - 180f;
        Vector3 target = controllerOrigin.position + controllerOrigin.forward * distance;
        Vector3 facing = head.position - target;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.000001f) facingYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.Euler(0f, facingYaw + manualFacingYawOffset + userYaw, 0f);
        candidatePose = new Pose(target - rotation * referenceLocalPosition, rotation);
        hasCandidate = IsFinite(candidatePose.position) && IsFinite(rotation.eulerAngles);
        if (!hasCandidate)
        {
            InterruptManualPlacement();
            Report("Nieprawidłowa pozycja. Sprawdź śledzenie kontrolera.");
            return;
        }
        placementPreview.SetPositionAndRotation(candidatePose.position, candidatePose.rotation);
        placementPreview.gameObject.SetActive(true);
        if (!triggerHeld) triggerArmed = true;
        if (triggerArmed && triggerDown && triggerHeld) LockCandidate();
        else Report(manualMessage);
    }

    private float ApplyDeadzone(float value)
    {
        if (!IsFinite(value) || Mathf.Abs(value) <= stickDeadzone) return 0f;
        return Mathf.Sign(value) * (Mathf.Clamp01(Mathf.Abs(value)) - stickDeadzone) / (1f - stickDeadzone);
    }

    public void ConfirmPlacement()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || State != PlacementState.Placing || !triggerArmed || !hasCandidate) return;
        if (!IsControllerTracked()) { InterruptManualPlacement(); return; }
        LockCandidate();
    }

    private void LockCandidate()
    {
        State = PlacementState.AwaitingTriggerRelease;
        triggerArmed = false;
        Report("Zwolnij spust, aby wyświetlić panele. B: anuluj.");
    }

    public void CancelPlacement()
    {
        if (!IsManualPlacement && State != PlacementState.ScanningQr) return;
        if (!hasConfirmedPose)
        {
            InterruptManualPlacement();
            manualMessage = "Brak poprzedniej pozycji. Wskaż miejsce i zatwierdź spustem.\nDrążek: odległość / obrót.";
            Report(manualMessage);
            return;
        }
        StopPlacement();
        Report("Przywrócono poprzednią pozycję stacji.");
    }

    private void InterruptManualPlacement()
    {
        if (!IsManualPlacement) return;
        State = PlacementState.Placing;
        triggerArmed = false;
        hasCandidate = false;
    }

    private void CompletePlacement(Pose pose, string message)
    {
        confirmedPose = pose;
        hasConfirmedPose = true;
        stationRoot.SetPositionAndRotation(pose.position, pose.rotation);
        if (hideReferenceAfterPlacement) reference.gameObject.SetActive(false);
        if (placementPreview != null) placementPreview.gameObject.SetActive(false);
        State = PlacementState.Placed;
        stationRoot.gameObject.SetActive(true);
        StopScanning();
        Report(message);
    }

    private void BeginQrPlacement()
    {
        try
        {
            if (!mruk.QRCodeTrackingSupported)
            {
                StopPlacement();
                Report("Native QR tracking is unavailable on this runtime/headset.", true);
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
                UnityEngine.Android.Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
#endif
            stationWasActive = stationRoot.gameObject.activeSelf;
            State = PlacementState.ScanningQr;
            mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
            scanning = true;
            var config = mruk.SceneSettings.TrackerConfiguration;
            enabledTracker = !config.QRCodeTrackingEnabled;
            config.QRCodeTrackingEnabled = true;
            mruk.SceneSettings.TrackerConfiguration = config;
            if (hideUntilPlaced) stationRoot.gameObject.SetActive(false);
            mruk.GetTrackables(trackables);
            foreach (MRUKTrackable trackable in trackables) OnTrackableAdded(trackable);
            Report("Spójrz na kod QR: " + expectedPayload);
        }
        catch (Exception exception)
        {
            StopPlacement();
            Report("QR startup failed: " + exception.Message, true);
        }
    }

    private bool Matches(MRUKTrackable trackable)
    {
        return trackable != null && trackable.TrackableType == OVRAnchor.TrackableType.QRCode &&
               string.Equals(trackable.MarkerPayloadString, expectedPayload, StringComparison.Ordinal);
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (Matches(trackable) && (selected == null || !selected.IsTracked)) selected = trackable;
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (selected == trackable) selected = null;
    }

    private void UpdateQrPlacement()
    {
        if (mruk == null || !mruk.isActiveAndEnabled || stationRoot == null || reference == null)
        {
            StopPlacement();
            Report("A placement reference became unavailable. Check assignments and Place Again.", true);
            return;
        }
        if (!mruk.TrackerConfiguration.QRCodeTrackingEnabled)
        {
            Report("Waiting for QR tracking. Allow Spatial Data permission and check Scene Support.");
            return;
        }
        if (!Matches(selected) || !selected.IsTracked)
        {
            selected = null;
            mruk.GetTrackables(trackables);
            foreach (MRUKTrackable trackable in trackables)
            {
                if (Matches(trackable) && trackable.IsTracked) { selected = trackable; break; }
            }
            if (selected == null) return;
        }
        Transform marker = selected.transform;
        Vector3 referencePosition = marker.position + marker.rotation * positionOffset;
        Quaternion referenceRotation = marker.rotation * Quaternion.Euler(rotationOffsetEuler);
        Quaternion rootRotation = referenceRotation * Quaternion.Inverse(referenceLocalRotation);
        CompletePlacement(new Pose(referencePosition - rootRotation * referenceLocalPosition, rootRotation), "Placed station at QR: " + expectedPayload);
    }

    private void StopPlacement()
    {
        bool wasPlacing = IsManualPlacement || State == PlacementState.ScanningQr || State == PlacementState.DetectingMode;
        StopScanning();
        if (wasPlacing && placementPreview != null && placementPreview != stationRoot && !transform.IsChildOf(placementPreview))
            placementPreview.gameObject.SetActive(false);
        triggerArmed = false;
        hasCandidate = false;
        State = hasConfirmedPose ? PlacementState.Placed : PlacementState.Idle;
        if (!wasPlacing || stationRoot == null) return;
        if (hasConfirmedPose) stationRoot.SetPositionAndRotation(confirmedPose.position, confirmedPose.rotation);
        stationRoot.gameObject.SetActive(hasConfirmedPose || stationWasActive);
    }

    private void StopScanning()
    {
        if (scanning && mruk != null)
        {
            mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
            if (enabledTracker)
            {
                var config = mruk.SceneSettings.TrackerConfiguration;
                config.QRCodeTrackingEnabled = false;
                mruk.SceneSettings.TrackerConfiguration = config;
            }
        }
        scanning = false;
        enabledTracker = false;
        selected = null;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    private void Report(string message, bool warning = false)
    {
        if (Status == message) return;
        Status = message;
        if (warning) Debug.LogWarning("[StationQrPlacer] " + message, this);
        else Debug.Log("[StationQrPlacer] " + message, this);
    }

    private void OnApplicationPause(bool value)
    {
        paused = value;
        if (value) InterruptManualPlacement();
    }

    private void OnApplicationFocus(bool value)
    {
        unityFocused = value;
        RefreshInputFocus();
    }

    private static bool SelectInputFocus(bool windowFocused, bool xrAvailable, bool xrFocused)
    {
        return xrAvailable ? xrFocused : windowFocused;
    }

    private void RefreshInputFocus()
    {
        bool xrAvailable = Application.isPlaying && OVRManager.instance != null && OVRPlugin.initialized;
        ApplyInputFocus(xrAvailable, xrAvailable && OVRManager.hasInputFocus);
    }

    private void ApplyInputFocus(bool xrAvailable, bool xrFocused)
    {
        UsingXrFocus = xrAvailable;
        bool next = SelectInputFocus(unityFocused, xrAvailable, xrFocused);
        if (focused && !next) InterruptManualPlacement();
        focused = next;
    }

    private void OnXrFocusAcquired() => ApplyInputFocus(true, true);
    private void OnXrFocusLost() => ApplyInputFocus(true, false);

    private void OnDisable()
    {
        if (focusSubscribed)
        {
            OVRManager.InputFocusAcquired -= OnXrFocusAcquired;
            OVRManager.InputFocusLost -= OnXrFocusLost;
            focusSubscribed = false;
        }
        StopPlacement();
    }
}
