using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>首次迁移与独立预览场景生成。已有 Prefab/预览场景不覆盖。</summary>
public static class EditableUIPrefabBuilder
{
    public const string Folder = "Assets/Resources/UI/Panels/";

    [MenuItem("Tools/UI/迁移其他界面为可编辑 Prefab（仅首次）")]
    public static void Build()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("请先退出 Play");
        System.IO.Directory.CreateDirectory(Folder);
        foreach (var name in new[] { "ShopPanel", "QuestPanel", "QuestBoardPanel", "SearchBarPanel" })
            BuildOne(name);
        AssetDatabase.SaveAssets();
    }

    private static void BuildOne(string name)
    {
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(.08f,.08f,.1f);
            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280,720);
            string path = Folder + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var host = new GameObject("AuthoringHost"); host.SetActive(false);
                GameObject panel;
                if (name == "ShopPanel") panel = host.AddComponent<ShopUI>().CreateEditablePrefab(canvas.transform);
                else if (name == "QuestPanel") panel = host.AddComponent<QuestPanelUI>().CreateEditablePrefab(canvas.transform);
                else if (name == "QuestBoardPanel") panel = host.AddComponent<QuestBoardUI>().CreateEditablePrefab(canvas.transform);
                else panel = host.AddComponent<SearchPanelUI>().CreateEditablePrefab(canvas.transform);
                // 文本不阻挡点击；保留每个节点原有字体、尺寸和颜色。
                foreach (var text in panel.GetComponentsInChildren<Text>(true)) text.raycastTarget = false;
                prefab = PrefabUtility.SaveAsPrefabAsset(panel,path);
                Object.DestroyImmediate(panel);
                Object.DestroyImmediate(host);
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab,canvas.transform);
            instance.SetActive(true);
            // 独立预览场景居中仅为实例覆盖，Prefab 保留游戏中左右侧栏的位置。
            ((RectTransform)instance.transform).anchoredPosition = Vector2.zero;
            string directory = "Assets/Scenes/UIEditing/" + name;
            System.IO.Directory.CreateDirectory(directory);
            string scenePath = directory + "/UI.scene";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[EditableUI] " + name + " 已就绪；预览 " + scenePath);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene,true);
            if(previous.IsValid()) SceneManager.SetActiveScene(previous);
        }
    }
}
