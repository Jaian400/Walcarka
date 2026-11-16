using System;
using System.Linq;
using System.Reflection;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StationPlacementChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    [MenuItem("Tools/Walcarka/Validate manual placement")]
    public static void Run()
    {
        checks = 0;
        CheckAutomaticSelection();
        CheckInputFocus();
        ValidateScene();
        var previous = SceneManager.GetActiveScene();
        var testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try { CheckBehaviour(); }
        finally
        {
            EditorSceneManager.CloseScene(testScene, true);
            SceneManager.SetActiveScene(previous);
        }
        Debug.Log($"[StationPlacementChecks] PASS: {checks} assertions. Hardware, Android build and native QR checks remain manual.");
    }

    private static void ValidateScene()
    {
        var placers = UnityEngine.Object.FindObjectsByType<StationQrPlacer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Require(placers.Length == 1, "Exactly one placement owner");
        var placer = placers[0];
        Require(placer.isActiveAndEnabled, "Placement owner is active before startup");
        Require(Valid(placer), "Scene references and settings valid");
        Require(Get<StationQrPlacer.PlacementMode>(placer, "placementMode") == StationQrPlacer.PlacementMode.Auto, "Scene selects Auto");
        Require(Get<Transform>(placer, "controllerOrigin").name == "RightControllerAnchor", "Right controller assigned");
        Require(Get<Transform>(placer, "head").name == "CenterEyeAnchor", "Head assigned");
        Require(Get<Vector3>(placer, "rotationOffsetEuler") == new Vector3(0f, -88f, 0f), "QR calibration preserved");
        var preview = Get<Transform>(placer, "placementPreview");
        Require(!preview.gameObject.activeSelf && preview.parent == null, "Independent preview initially hidden");
        Require(preview.childCount == 3 && preview.GetComponentsInChildren<Collider>(true).Length == 0 &&
                preview.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "Preview contains only three visuals");
        Require(preview.Find("CenterMarker").localPosition == Get<Transform>(placer, "stationRoot").InverseTransformPoint(Get<Transform>(placer, "reference").position), "Preview marker matches reference pivot");
        var controls = Get<Transform>(placer, "head").Find("PlacementControls");
        Require(controls == null, "No head-mounted placement controls");
        var wrappers = UnityEngine.Object.FindObjectsByType<InteractableUnityEventWrapper>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Require(!wrappers.Any(wrapper => Enumerable.Range(0, wrapper.WhenSelect.GetPersistentEventCount())
            .Any(index => wrapper.WhenSelect.GetPersistentTarget(index) == placer)), "No placement select callbacks; use X/Y shortcuts");
        Require(!UnityEngine.Object.FindFirstObjectByType<MRUK>().SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled, "QR tracker starts off until mode selection");
        foreach (string name in new[] { "ConnectionService", "DataBuffer", "MainThreadDispatcher" })
        {
            var service = GameObject.Find(name);
            Require(service != null && !service.transform.IsChildOf(Get<Transform>(placer, "stationRoot")), name + " remains active outside station");
        }
    }

    private static void CheckAutomaticSelection()
    {
        Require((int)StationQrPlacer.PlacementMode.QR == 0 && (int)StationQrPlacer.PlacementMode.Manual == 1, "Existing serialized modes preserved");
        var selector = typeof(StationQrPlacer).GetMethod("SelectAutomaticMode", BindingFlags.Static | BindingFlags.NonPublic);
        StationQrPlacer.PlacementMode Select(bool editor, bool ready, OVRPlugin.SystemHeadset headset, bool supported)
            => (StationQrPlacer.PlacementMode)selector.Invoke(null, new object[] { editor, ready, headset, supported });
        Require(Select(false, true, OVRPlugin.SystemHeadset.Meta_Quest_3, true) == StationQrPlacer.PlacementMode.QR, "Quest 3 automatically selects QR");
        Require(Select(false, true, OVRPlugin.SystemHeadset.Meta_Quest_3S, true) == StationQrPlacer.PlacementMode.QR, "Quest 3S automatically selects QR");
        Require(Select(false, true, OVRPlugin.SystemHeadset.Oculus_Quest_2, true) == StationQrPlacer.PlacementMode.Manual, "Quest 2 selects the Manual path, which waits for X");
        Require(Select(true, false, OVRPlugin.SystemHeadset.None, false) == StationQrPlacer.PlacementMode.Manual, "Editor selects the Manual path, which waits for X");
        Require(Select(false, false, OVRPlugin.SystemHeadset.Meta_Quest_3, true) == StationQrPlacer.PlacementMode.Auto, "Wait for XR initialization");
        Require(Select(false, true, OVRPlugin.SystemHeadset.None, false) == StationQrPlacer.PlacementMode.Auto, "Wait for headset identification");
        Require(Select(false, true, OVRPlugin.SystemHeadset.Meta_Quest_3, false) == StationQrPlacer.PlacementMode.Auto, "Quest 3 never falls into Manual during QR startup");
    }

    private static void CheckBehaviour()
    {
        var owner = new GameObject("TestPlacementOwner");
        var placer = owner.AddComponent<StationQrPlacer>();
        var station = new GameObject("TestStation").transform;
        var reference = new GameObject("TestReference").transform;
        reference.SetParent(station, false);
        reference.localPosition = new Vector3(0.25f, 0.3f, -0.15f);
        reference.localRotation = Quaternion.Euler(0f, 23f, 0f);
        var controller = new GameObject("TestController").transform;
        controller.position = new Vector3(0.4f, 1.2f, -0.2f);
        controller.rotation = Quaternion.Euler(12f, 15f, 0f);
        var head = new GameObject("TestHead").transform;
        head.position = new Vector3(0f, 1.7f, -1f);
        var preview = new GameObject("TestPreview").transform;
        Set(placer, "stationRoot", station);
        Set(placer, "reference", reference);
        Set(placer, "controllerOrigin", controller);
        Set(placer, "head", head);
        Set(placer, "placementPreview", preview);
        Set(placer, "placementMode", StationQrPlacer.PlacementMode.Manual);
        Invoke(placer, "Start");
        Require(placer.State == StationQrPlacer.PlacementState.Idle && !preview.gameObject.activeSelf && !station.gameObject.activeSelf,
            "Manual startup waits for X with preview and unplaced station hidden");
        Input(placer, held: true, down: true);
        Require(placer.State == StationQrPlacer.PlacementState.Idle, "Right trigger cannot start idle Manual placement");
        placer.enabled = false;
        placer.enabled = true;
        Require(placer.State == StationQrPlacer.PlacementState.Idle && !preview.gameObject.activeSelf,
            "Re-enabling Manual does not start placement");
        Set(placer, "started", false);
        Set(placer, "placementMode", StationQrPlacer.PlacementMode.Auto);
        Begin(placer);
        Require(placer.State == StationQrPlacer.PlacementState.DetectingMode && !station.gameObject.activeSelf, "Auto waits without showing an unplaced station");
        Invoke(placer, "WaitForManualPlacement");
        Require(placer.State == StationQrPlacer.PlacementState.Idle && !preview.gameObject.activeSelf && !station.gameObject.activeSelf,
            "Auto Manual selection waits for X instead of starting preview");
        Invoke(placer, "ProcessPlacementShortcuts", false, false);
        Require(placer.State == StationQrPlacer.PlacementState.Idle, "Idle placement stays idle without X/Y");
        Invoke(placer, "ProcessPlacementShortcuts", true, false);
        Require(placer.State == StationQrPlacer.PlacementState.Placing && placer.ActivePlacementMode == StationQrPlacer.PlacementMode.Manual, "Auto dispatches to Manual");
        placer.enabled = false;
        Require(!station.gameObject.activeSelf && !preview.gameObject.activeSelf, "Disable restores the hidden waiting state before first confirmation");
        placer.enabled = true;
        Set(placer, "placementMode", StationQrPlacer.PlacementMode.Manual);
        Set(placer, "rotationOffsetEuler", new Vector3(0f, -88f, 0f));
        Require(Valid(placer), "Manual validates without MRUK");
        Begin(placer);
        Require(!station.gameObject.activeSelf && !preview.gameObject.activeSelf, "No stale preview before first tracked pose");
        Input(placer, held: true, down: true);
        Require(placer.State == StationQrPlacer.PlacementState.Placing, "Opening trigger cannot confirm");
        Input(placer);
        Vector3 target = controller.position + controller.forward * 1.5f;
        Require(Vector3.Distance(preview.position + preview.rotation * reference.localPosition, target) < 0.00001f, "Offset pivot aligns reference to controller target");
        Require(Vector3.Dot(preview.up, Vector3.up) > 0.99999f, "Preview stays upright");
        var original = new Pose(preview.position, preview.rotation);
        Set(placer, "rotationOffsetEuler", new Vector3(40f, 125f, 18f));
        Input(placer);
        Require(Quaternion.Angle(original.rotation, preview.rotation) < 0.001f, "Manual ignores QR rotation calibration");
        placer.CancelPlacement();
        Require(placer.State == StationQrPlacer.PlacementState.Placing && placer.Status.Contains("Brak poprzedniej"), "Startup cancel stays in placement with explanation");
        Input(placer);
        Input(placer, held: true, down: true);
        Require(placer.State == StationQrPlacer.PlacementState.AwaitingTriggerRelease && !station.gameObject.activeSelf, "Press locks pose while station hidden");
        var locked = new Pose(preview.position, preview.rotation);
        controller.position += Vector3.one;
        Input(placer, held: true);
        Require(Vector3.Distance(locked.position, preview.position) < 0.00001f, "Locked candidate cannot move");
        Input(placer, tracked: false);
        Require(placer.State == StationQrPlacer.PlacementState.Placing && !station.gameObject.activeSelf, "Tracking loss invalidates pending confirmation");
        Require(Vector3.Distance(locked.position, preview.position) < 0.00001f, "Tracking loss freezes preview");
        Input(placer, held: true, down: true);
        Require(placer.State == StationQrPlacer.PlacementState.Placing, "Recovery requires release before confirmation");
        Input(placer);
        Input(placer, held: true, down: true);
        Input(placer);
        Require(placer.HasPlaced && station.gameObject.activeSelf && !preview.gameObject.activeSelf, "Release reveals confirmed station");
        var confirmed = new Pose(station.position, station.rotation);
        Set(placer, "unityFocused", false);
        Invoke(placer, "ApplyInputFocus", true, true);
        Invoke(placer, "ProcessPlacementShortcuts", true, false);
        Require(placer.State == StationQrPlacer.PlacementState.Placing && Get<bool>(placer, "focused") && !Get<bool>(placer, "unityFocused"),
            "Link accepts X with headset input focus while the Game view is unfocused");
        Input(placer);
        Require(preview.gameObject.activeSelf, "Link X shows the preview");
        placer.CancelPlacement();
        Require(placer.HasPlaced && Vector3.Distance(station.position, confirmed.position) < 0.00001f,
            "Cancelling Link repositioning restores the confirmed pose");
        Set(placer, "unityFocused", true);
        Invoke(placer, "ApplyInputFocus", false, false);
        Set(placer, "expectedPayload", string.Empty);
        Invoke(placer, "ProcessPlacementShortcuts", false, true);
        Require(placer.HasPlaced && station.gameObject.activeSelf && Vector3.Distance(station.position, confirmed.position) < 0.00001f,
            "Unavailable QR shortcut preserves confirmed placement");
        Set(placer, "expectedPayload", "Walcarka");
        Set(placer, "placementMode", StationQrPlacer.PlacementMode.QR);
        Invoke(placer, "ProcessPlacementShortcuts", false, false);
        Require(placer.HasPlaced, "No shortcut press leaves confirmed placement unchanged");
        Invoke(placer, "ProcessPlacementShortcuts", true, false);
        Require(placer.State == StationQrPlacer.PlacementState.Placing && placer.ActivePlacementMode == StationQrPlacer.PlacementMode.Manual &&
                Get<StationQrPlacer.PlacementMode>(placer, "placementMode") == StationQrPlacer.PlacementMode.QR,
            "X requests Manual independently of configured startup mode");
        Input(placer);
        var previewBeforeQr = new Pose(preview.position, preview.rotation);
        Set(placer, "expectedPayload", string.Empty);
        Require(!(bool)typeof(StationQrPlacer).GetMethod("TryRestartQrPlacement", Private).Invoke(placer, null) &&
                placer.State == StationQrPlacer.PlacementState.Placing && preview.gameObject.activeSelf &&
                Vector3.Distance(preview.position, previewBeforeQr.position) < 0.00001f,
            "Unavailable QR request preserves ongoing manual placement");
        Set(placer, "expectedPayload", "Walcarka");
        Invoke(placer, "OnApplicationFocus", false);
        Require(!(bool)typeof(StationQrPlacer).GetMethod("ProcessPlacementShortcuts", Private).Invoke(placer, new object[] { false, true }),
            "Placement shortcuts ignored without focus");
        Invoke(placer, "OnApplicationFocus", true);
        placer.CancelPlacement();
        Set(placer, "placementMode", StationQrPlacer.PlacementMode.Manual);
        controller.position += Vector3.one;
        head.position += Vector3.one;
        Input(placer);
        Require(Vector3.Distance(station.position, confirmed.position) < 0.00001f, "Confirmed world pose ignores controller/head movement");
        for (int cycle = 0; cycle < 25; cycle++)
        {
            Begin(placer);
            Input(placer, stick: Vector2.one);
            placer.CancelPlacement();
            Require(placer.HasPlaced && station.gameObject.activeSelf && Vector3.Distance(station.position, confirmed.position) < 0.00001f &&
                    Quaternion.Angle(station.rotation, confirmed.rotation) < 0.001f && !preview.gameObject.activeSelf, "Repeated cancel restores pose " + cycle);
        }
        Begin(placer);
        for (int i = 0; i < 80; i++) Input(placer, stick: Vector2.up);
        Require(Mathf.Abs(Get<float>(placer, "distance") - 4f) < 0.00001f, "Maximum distance clamps");
        for (int i = 0; i < 100; i++) Input(placer, stick: Vector2.down);
        Require(Mathf.Abs(Get<float>(placer, "distance") - 0.5f) < 0.00001f, "Minimum distance clamps");
        float yaw = Get<float>(placer, "userYaw");
        Input(placer, stick: new Vector2(0.1f, 0.1f));
        Require(Get<float>(placer, "userYaw") == yaw, "Deadzone blocks drift");
        Input(placer, stick: Vector2.right);
        Require(Get<float>(placer, "userYaw") > yaw && Vector3.Dot(preview.up, Vector3.up) > 0.99999f, "Thumbstick rotates yaw only");
        Input(placer, stick: new Vector2(float.NaN, float.PositiveInfinity));
        Require(!float.IsNaN(preview.position.x), "Invalid stick input cannot corrupt pose");
        head.position = controller.position + controller.forward * Get<float>(placer, "distance") + Vector3.up;
        float facingYaw = Get<float>(placer, "facingYaw");
        Input(placer);
        Require(Get<float>(placer, "facingYaw") == facingYaw, "Degenerate horizontal facing retains last yaw");
        Input(placer, held: true, down: true);
        Invoke(placer, "OnApplicationPause", true);
        Require(placer.State == StationQrPlacer.PlacementState.Placing, "Pause discards pending confirmation");
        Invoke(placer, "OnApplicationPause", false);
        Input(placer, held: true, down: true);
        Require(placer.State == StationQrPlacer.PlacementState.Placing, "Resume requires release");
        Input(placer);
        Input(placer, held: true, down: true);
        Invoke(placer, "OnApplicationFocus", false);
        Require(placer.State == StationQrPlacer.PlacementState.Placing, "Focus loss discards pending confirmation");
        Invoke(placer, "OnApplicationFocus", true);
        placer.enabled = false;
        Require(placer.HasPlaced && station.gameObject.activeSelf && !preview.gameObject.activeSelf, "Disable restores confirmed station");
        placer.enabled = true;
        Set(placer, "controllerOrigin", null);
        Begin(placer);
        Require(placer.HasPlaced && station.gameObject.activeSelf, "Missing reference leaves confirmed station available");
        Set(placer, "controllerOrigin", controller);
        Set(placer, "minDistance", float.NaN);
        Require(!Valid(placer), "Invalid distance settings rejected");
        Set(placer, "minDistance", 0.5f);
        Set(placer, "stickDeadzone", 1f);
        Require(!Valid(placer), "Invalid deadzone rejected");
        Set(placer, "stickDeadzone", 0.2f);
        Begin(placer);
        Input(placer);
        Input(placer, held: true, down: true);
        Input(placer);
        Require(placer.HasPlaced && reference.parent == station && station.GetInstanceID() == Get<Transform>(placer, "stationRoot").GetInstanceID(), "Reconfirm reuses station instance and hierarchy");
    }

    private static void Begin(StationQrPlacer placer) => Invoke(placer, "RestartPlacement");

    private static void CheckInputFocus()
    {
        var selector = typeof(StationQrPlacer).GetMethod("SelectInputFocus", BindingFlags.NonPublic | BindingFlags.Static);
        bool Focus(bool window, bool available, bool xr) => (bool)selector.Invoke(null, new object[] { window, available, xr });
        Require(Focus(false, true, true), "Link headset focus overrides an unfocused Editor Game view");
        Require(!Focus(true, true, false), "Headset system overlay blocks input even when the Game view is focused");
        Require(!Focus(false, false, true), "Without XR, an unfocused Game view still blocks input");
        Require(Focus(true, false, false), "Without XR, a focused Game view allows input");
    }

    private static void Input(StationQrPlacer placer, bool tracked = true, bool held = false, bool down = false, Vector2 stick = default)
        => Invoke(placer, "ProcessManualInput", tracked, held, down, false, stick, 0.1f);
    private static bool Valid(StationQrPlacer placer)
    {
        object[] args = { null };
        return (bool)typeof(StationQrPlacer).GetMethod("ValidateConfiguration", Private).Invoke(placer, args);
    }
    private static T Get<T>(StationQrPlacer placer, string name) => (T)typeof(StationQrPlacer).GetField(name, Private).GetValue(placer);
    private static void Set(StationQrPlacer placer, string name, object value) => typeof(StationQrPlacer).GetField(name, Private).SetValue(placer, value);
    private static void Invoke(StationQrPlacer placer, string name, params object[] args) => typeof(StationQrPlacer).GetMethod(name, Private).Invoke(placer, args);
    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("[StationPlacementChecks] FAIL: " + message);
    }
}
