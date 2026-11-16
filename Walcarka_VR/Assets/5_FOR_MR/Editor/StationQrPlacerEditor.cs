using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StationQrPlacer))]
public sealed class StationQrPlacerEditor : Editor
{
    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (!Application.isPlaying) return;
        var placer = (StationQrPlacer)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Live placement diagnostics", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Component active", placer.isActiveAndEnabled.ToString());
        EditorGUILayout.LabelField("Update count", placer.UpdateCount.ToString());
        EditorGUILayout.LabelField("State", placer.State.ToString());
        EditorGUILayout.LabelField("Active mode", placer.ActivePlacementMode.ToString());
        EditorGUILayout.LabelField("Paused", placer.InputPaused.ToString());
        EditorGUILayout.LabelField("Input focused", placer.InputFocused.ToString());
        EditorGUILayout.LabelField("Unity Game view focused", placer.UnityFocused.ToString());
        EditorGUILayout.LabelField("Using XR focus (includes Link)", placer.UsingXrFocus.ToString());
        EditorGUILayout.LabelField("Queued data callbacks", MainThreadDispatcher.PendingCount.ToString());
        EditorGUILayout.LabelField("Last X/Y", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(placer.LastShortcut, MessageType.None);
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(placer.Status ?? "Not started.", MessageType.None);
        using (new EditorGUI.DisabledScope(!placer.isActiveAndEnabled))
        {
            if (GUILayout.Button("Test manual placement (bypass controller input)")) placer.PlaceManually();
            if (GUILayout.Button("Test QR scanning (bypass controller input)")) placer.PlaceFromQr();
        }
    }
}
