# Automatic and manual placement

`Assets/Scenes/MRScene.unity` starts in Auto mode. On standalone Quest 3/3S it waits for XR and native QR support, requests Spatial Data permission when needed, then automatically places both panels when the `Walcarka` QR marker is tracked. QR placement requires no controller confirmation. On Quest 2 and in the Unity Editor, Auto waits for X before starting manual placement. Explicit Manual mode also waits for X at startup and after component re-enabling. Explicit QR mode remains available for Link diagnostics.

The existing `StationQrPlacer` owns both modes and moves the existing station instance. Explicit QR and Manual settings remain available. The QR calibration remains `(0, -88, 0)`. QR tracking starts disabled and is enabled only when QR placement begins; it is stopped after placement if this component enabled it. Quest 3 waits for QR readiness rather than silently selecting Manual during startup.

## Manual controls (Quest 2)

- Press **X on the left controller** to start manual placement or reposition manually, regardless of the configured startup mode.
- Press **Y on the left controller** to start QR scanning again on a supported headset. An unavailable QR request preserves the current placement. These shortcuts are ignored while the application is paused or unfocused.
- Aim the right controller to position the central preview marker, including height.
- Thumbstick Y changes distance within 0.5–4 metres; X adjusts horizontal rotation.
- Release the trigger once before confirming. Press it to lock the preview, then release it to show the panels.
- B restores the last confirmed pose. On first placement, B keeps placement active.

Placement mode changes use only the left-controller X/Y buttons. There is no placement button or instruction panel in front of the player. Placement status and errors remain available in the Unity/Android logs. The right-controller thumbstick, trigger and B button handle adjusting, confirming and cancelling manual placement.

The Manual yaw offset is `-90°` because the authored panels face approximately along the station's local positive X axis. Tune only **Manual Facing Yaw Offset** if headset testing reveals a facing adjustment is needed.

Tracking loss freezes the preview and invalidates a pending confirmation. Tracking recovery, application pause and focus loss require trigger release and a fresh press. Disabling the placement component during repositioning restores the confirmed station. Placement is retained for the current session only.

Controller input uses the earlier `OVRInput` implementation in Update. Left X/Y use RawButton.X/RawButton.Y targeted to LTouch. The right trigger, B button, thumbstick and tracking checks target RTouch. No separate placement Input Actions asset or Inspector assignment is required.

Placement retains OVRInput polling in Update and requires no Input Actions asset. During an active XR session, input focus follows the headset, including in Editor Link mode. Without XR it follows Unity's application focus callback. Losing focus to a headset system overlay interrupts pending manual confirmation. Losing Editor Game-view focus alone does not block X/Y while the headset retains input focus. Manual placement still waits for X; no on-screen placement UI is added. QR scanning accepts a matching tracked marker as before. Repeated QR placement under Link remains a separate behavior to test.

During Play Mode, select the QRPlacement scene object to see **Live placement diagnostics** in the Inspector: component activity, Update count, placement state, Unity/headset focus source, queued data callbacks, the last X/Y press and status. The Update count should keep increasing even while the panels are hidden or already placed. The Inspector's **Test manual placement** and **Test QR scanning** buttons call placement directly to distinguish a placement/configuration failure from missing controller input. These diagnostic buttons are Editor-only and add nothing to the headset view.

The Link isolation test found X/Y working when ConnectionServiceMR was disabled. The data receive path now reads TCP frames and decodes UTF-8 on a worker. At most 128 sample deliveries can wait for the main thread by default; when full, the receiver waits instead of dropping samples. MainThreadDispatcher processes at most 32 callbacks or approximately 2 ms of queued work per frame, checking time between callbacks. A single slow callback can exceed that time. Pending work stays queued for later frames, so a startup burst may take several frames to catch up while placement keeps receiving frame updates. JSON parsing, buffer events and Unity UI updates still run on the main thread.

## Verification

The runtime scripts and Editor utilities compile against the installed Unity 6000.3.2f1 / Meta XR 85 assemblies. The standalone compiler emits a package source-generator warning (`AttributeBasedFieldGenerator`, CS8785). Checks include automatic mode selection and Link focus selection when the Editor Game view loses focus. Live Link/controller behavior still needs retesting.

Managed data flow integration checks passed with a 1,000-frame burst, bounded delivery batches, FIFO ordering, UTF-8 text, cancellation during backpressure, fragmented reads, oversized-frame rejection and callback error isolation. The checks execute the actual receive/dispatcher code outside Unity. They are also available outside Play Mode under **Tools > Walcarka > Validate data flow**. Retest Link with ConnectionServiceMR enabled to verify controller responsiveness with the real stream. The placement script, MRScene and StationRoot prefab were not changed by this data flow fix.

Unity execution was not authorized during implementation. The following checks have been supplied but have **not** been run in Unity:

1. Open MRScene outside Play Mode and choose **Tools > Walcarka > Validate manual placement**. This checks Auto configuration and device selection, then runs simulated controller-input tests for placement, confirmation release, tracking loss, cancellation, distance limits, yaw, interruption recovery, invalid settings and repeated repositioning.
2. Enter Play Mode to check imports and startup. Auto and Manual must wait with no preview until left X is pressed. Right-trigger input alone must not start placement. Disable/re-enable the component before pressing X and verify it still waits. Once manual placement has been requested, confirmation requires a tracked right controller.
3. Build for Android and run on Quest 2. Confirm there is no placement UI in front of the player. Check X starts/restarts Manual, preview alignment, distance/yaw, confirm and release, cancellation, controller disconnection/reconnection, headset removal/resume, and a fixed world pose while walking around.
4. Receive measurements while repositioning; cancel and confirm at a new position. Verify the existing chart pages, selected pages and buffered history survive.
5. Repeat placement and Editor Play/Stop cycles. Investigate the pre-existing XR shutdown crash separately if it recurs.
6. First retest the restored baseline over Link: X starts Manual, right-trigger confirmation works, and Y performs the initial QR placement. Then reproduce the reported failure to change placement after QR as a separate test. Build for standalone Quest 3/3S with Auto selected and verify automatic QR placement, calibration and X/Y behavior independently of Link. Verify Y on Quest 2 leaves the current placement intact. On Quest 2, Auto must wait for X with QR tracking off. Meta documents the [QR prerequisites and Link limitations](https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-qrcode-detection/).

**Tools > Walcarka > Set up manual placement** can recreate missing preview assets and wire controller references. The scene is already wired; this setup command is normally unnecessary. It removes legacy PlacementControls, selects Auto mode, resets the Manual yaw offset to `-90°`, disables QR tracking and saves MRScene.

Preview assets are under `Prefabs/Placement`. The preview has no colliders or scripts and remains outside StationRoot. The connection service, data buffer and dispatcher also remain outside StationRoot so they keep running while the station is hidden.
