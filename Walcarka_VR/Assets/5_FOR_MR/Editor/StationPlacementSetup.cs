using System;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class StationPlacementSetup
{
    private const string ScenePath = "Assets/Scenes/MRScene.unity";
    private const string Folder = "Assets/5_FOR_MR/Prefabs/Placement";
    private const string PreviewPath = Folder + "/StationPlacementPreview.prefab";

    public static void SetupAndValidate()
    {
        Setup();
        StationPlacementChecks.Run();
    }

    [MenuItem("Tools/Walcarka/Set up manual placement")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var placer = UnityEngine.Object.FindFirstObjectByType<StationQrPlacer>();
        if (placer == null) throw new InvalidOperationException("Missing StationQrPlacer.");
        var settings = new SerializedObject(placer);
        var station = (Transform)settings.FindProperty("stationRoot").objectReferenceValue;
        var reference = (Transform)settings.FindProperty("reference").objectReferenceValue;
        var head = FindTransform("CenterEyeAnchor");
        var controller = FindTransform("RightControllerAnchor");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/5_FOR_MR/Prefabs", "Placement");
        var previewMaterial = GetMaterial("PlacementPreview", new Color(0.05f, 0.85f, 1f, 0.28f), true);
        var previewAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PreviewPath);
        if (previewAsset == null)
        {
            var authored = new GameObject("StationPlacementPreview");
            var marker = Cube("CenterMarker", authored.transform, previewMaterial,
                station.InverseTransformPoint(reference.position), Vector3.one * 0.08f);
            marker.transform.localRotation = Quaternion.identity;
            foreach (var panel in station.GetComponentsInChildren<StationPanelNavigator>(true))
            {
                var panelSettings = new SerializedObject(panel);
                var content = (Transform)panelSettings.FindProperty("contentRoot").objectReferenceValue;
                var size = panelSettings.FindProperty("contentSize").vector2Value;
                var placeholder = Cube(panel.name.Contains("Left") ? "LeftPanelPreview" : "RightPanelPreview",
                    authored.transform, previewMaterial, station.InverseTransformPoint(content.position), new Vector3(size.x, size.y, 0.012f));
                placeholder.transform.localRotation = Quaternion.Inverse(station.rotation) * panel.transform.rotation;
            }
            authored.SetActive(false);
            previewAsset = PrefabUtility.SaveAsPrefabAsset(authored, PreviewPath);
            UnityEngine.Object.DestroyImmediate(authored);
        }
        var preview = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "StationPlacementPreview");
        if (preview == null) preview = (GameObject)PrefabUtility.InstantiatePrefab(previewAsset, scene);
        preview.transform.SetPositionAndRotation(station.position, station.rotation);
        preview.SetActive(false);

        var controls = head.Find("PlacementControls");
        if (controls != null) UnityEngine.Object.DestroyImmediate(controls.gameObject);
        settings.FindProperty("placementMode").enumValueIndex = (int)StationQrPlacer.PlacementMode.Auto;
        settings.FindProperty("controllerOrigin").objectReferenceValue = controller;
        settings.FindProperty("head").objectReferenceValue = head;
        settings.FindProperty("placementPreview").objectReferenceValue = preview.transform;
        settings.FindProperty("manualFacingYawOffset").floatValue = -90f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var mruk = UnityEngine.Object.FindFirstObjectByType<MRUK>();
        var configuration = mruk.SceneSettings.TrackerConfiguration;
        configuration.QRCodeTrackingEnabled = false;
        mruk.SceneSettings.TrackerConfiguration = configuration;
        EditorUtility.SetDirty(mruk);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[StationPlacementSetup] Auto mode, preview and controller references saved. X: Manual; Y: QR. No placement UI.");
    }

    private static Transform FindTransform(string name)
    {
        return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(t => t.name == name);
    }

    private static GameObject Cube(string name, Transform parent, Material material, Vector3 position, Vector3 scale)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position;
        cube.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        var renderer = cube.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return cube;
    }

    private static Material GetMaterial(string name, Color color, bool transparent)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.name = name;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Cull", (float)CullMode.Off);
        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
