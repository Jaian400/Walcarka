using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StationQrPlacer : MonoBehaviour
{
    [Header("Assign scene objects")]
    [SerializeField] private MRUK mruk;
    [SerializeField] private Transform stationRoot = null;
    [Tooltip("The placeholder cube inside StationRoot. Aligns with the QR; panels keep their local offsets.")]
    [SerializeField] private Transform reference = null;
    [SerializeField] private string expectedPayload = "Walcarka";

    [Header("Optional calibration in QR-local coordinates")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;
    [SerializeField] private bool hideUntilPlaced = true;
    [SerializeField] private bool hideReferenceAfterPlacement = true;

    public bool HasPlaced { get; private set; }
    public string Status { get; private set; }

    private readonly List<MRUKTrackable> trackables = new List<MRUKTrackable>();
    private MRUKTrackable selected;
    private Vector3 referenceLocalPosition;
    private Quaternion referenceLocalRotation;
    private bool initialized;
    private bool started;
    private bool scanning;
    private bool enabledTracker;

    private void Start()
    {
        started = true;
        PlaceAgain();
    }

    private void OnEnable()
    {
        if (started && !HasPlaced) PlaceAgain();
    }

    public void PlaceAgain()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        StopScanning();
        if (mruk == null) mruk = MRUK.Instance;
        if (mruk == null || !mruk.isActiveAndEnabled || stationRoot == null || reference == null ||
            !reference.IsChildOf(stationRoot) || !stationRoot.gameObject.scene.IsValid() ||
            transform.IsChildOf(stationRoot) || mruk.transform.IsChildOf(stationRoot) ||
            string.IsNullOrWhiteSpace(expectedPayload))
        {
            Report("Assign an active MRUK, the scene StationRoot and its cube Reference. Keep this component and MRUK outside StationRoot.", true);
            return;
        }
        if ((stationRoot.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
        {
            Report("StationRoot and its parents need scale 1. Keep visual scaling on children.", true);
            return;
        }
        if (!initialized)
        {
            referenceLocalPosition = stationRoot.InverseTransformPoint(reference.position);
            referenceLocalRotation = Quaternion.Inverse(stationRoot.rotation) * reference.rotation;
            initialized = true;
        }

        HasPlaced = false;
        if (hideUntilPlaced) stationRoot.gameObject.SetActive(false);
        try
        {
            if (!mruk.QRCodeTrackingSupported)
            {
                Report("Native QR tracking is unavailable on this runtime/headset.", true);
                return;
            }
            mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
            scanning = true;
            var config = mruk.SceneSettings.TrackerConfiguration;
            enabledTracker = !config.QRCodeTrackingEnabled;
            config.QRCodeTrackingEnabled = true;
            mruk.SceneSettings.TrackerConfiguration = config;
            mruk.GetTrackables(trackables);
            foreach (MRUKTrackable trackable in trackables) OnTrackableAdded(trackable);
            Report("Looking for QR: " + expectedPayload);
        }
        catch (Exception exception)
        {
            StopScanning();
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

    private void Update()
    {
        if (!scanning || HasPlaced) return;
        if (mruk == null || !mruk.isActiveAndEnabled || stationRoot == null || reference == null)
        {
            StopScanning();
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
        stationRoot.SetPositionAndRotation(referencePosition - rootRotation * referenceLocalPosition, rootRotation);
        if (hideReferenceAfterPlacement && reference != stationRoot) reference.gameObject.SetActive(false);
        stationRoot.gameObject.SetActive(true);
        HasPlaced = true;
        StopScanning();
        Report("Placed station at QR: " + expectedPayload);
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

    private void Report(string message, bool warning = false)
    {
        if (Status == message) return;
        Status = message;
        if (warning) Debug.LogWarning("[StationQrPlacer] " + message, this);
        else Debug.Log("[StationQrPlacer] " + message, this);
    }

    private void OnDisable() => StopScanning();
}
