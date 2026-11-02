using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>首次迁移工具。已有完整 Prefab 时拒绝覆盖，保护手工布局。</summary>
public static class InventoryPanelPrefabBuilder
{
    public const string PanelPath = "Assets/Prefabs/UI/InventoryPanel.prefab";
    public const string VisualPath = "Assets/Resources/UI/InventoryItemVisual.prefab";

    [MenuItem("Tools/UI/迁移背包为可编辑 Prefab（仅首次）")]
    public static void Build()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        if (existing != null && existing.GetComponent<InventoryPanelView>() != null)
        { Debug.Log("[InventoryPanel] 已存在完整 Prefab，保留手动编辑内容"); return; }
        if (Application.isPlaying) throw new System.InvalidOperationException("请在编辑模式迁移");
        var previous = SceneManager.GetActiveScene();
        var temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject panel = null;
        try
        {
            UIKit.EnsureLoaded();
            var canvasObject = new GameObject("InventoryAuthoringCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280,720);
            var host = new GameObject("InventoryAuthoringHost"); host.SetActive(false);
            host.AddComponent<Inventory>();
            var controller = host.AddComponent<InventoryUI>();
            panel = controller.CreateAuthoringPanel(canvas.transform);
            var view = panel.GetComponent<InventoryPanelView>();
            if (!view.ValidateReferences()) throw new System.InvalidOperationException("背包节点引用校验失败");
            // 独立物品显示 Prefab：所有图形节点在编辑状态可见可改。
            var sample = ScriptableObject.CreateInstance<ItemData>();
            sample.itemName = "物品名称"; sample.icon = UIKit.Slot;
            var icon = InventoryUI.BuildAuthoringIconGroup(new Inventory.ItemStack { item=sample, count=2 },new Vector2(74,74),ItemOrient.Horizontal,false);
            icon.transform.SetParent(canvas.transform,false);
            var visual = icon.AddComponent<InventoryItemVisual>();
            visual.background=icon.transform.Find("Silhouette").GetComponent<Image>();
            visual.icon=icon.transform.Find("Icon").GetComponent<Image>();
            visual.itemName=icon.transform.Find("Name").GetComponent<Text>();
            visual.count=icon.transform.Find("Count").GetComponent<Text>();
            visual.group=icon.GetComponent<CanvasGroup>();
            var fallback = new GameObject("Fallback",typeof(RectTransform),typeof(Text));
            fallback.transform.SetParent(icon.transform,false);
            visual.fallback=fallback.GetComponent<Text>();
            visual.fallback.font=UIKit.LegacyFont; visual.fallback.fontSize=30;
            visual.fallback.alignment=TextAnchor.MiddleCenter; visual.fallback.color=InventoryUI.LineColor;
            visual.fallback.raycastTarget=false; visual.fallback.text="物";
            foreach(var rt in new[]{visual.icon.rectTransform,visual.fallback.rectTransform})
            {
                rt.anchorMin=new Vector2(.225f,.225f); rt.anchorMax=new Vector2(.775f,.775f);
                rt.offsetMin=rt.offsetMax=Vector2.zero;
            }
            visual.icon.preserveAspect=true; fallback.SetActive(false);
            icon.name="InventoryItemVisual";
            System.IO.Directory.CreateDirectory("Assets/Resources/UI");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath)==null)
                PrefabUtility.SaveAsPrefabAsset(icon,VisualPath);
            Object.DestroyImmediate(sample);
            PrefabUtility.SaveAsPrefabAsset(panel,PanelPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[InventoryPanel] 完整背包与物品模板已生成");
        }
        finally
        {
            EditorSceneManager.CloseScene(temporary,true);
            if(previous.IsValid()) SceneManager.SetActiveScene(previous);
        }
    }

    [MenuItem("Tools/UI/在当前制作画布放置背包预览")]
    public static void AddPreview()
    {
        if(Application.isPlaying || SceneManager.GetActiveScene().name!="UI")
            throw new System.InvalidOperationException("请先打开 UI.scene 制作场景");
        var canvas=Object.FindObjectOfType<Canvas>();
        if(canvas==null) throw new System.InvalidOperationException("制作场景中没有 Canvas");
        foreach(var old in canvas.GetComponentsInChildren<InventoryPanelView>(true))
        { Selection.activeGameObject=old.gameObject; return; }
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,canvas.transform);
        Undo.RegisterCreatedObjectUndo(instance,"放置背包预览");
        instance.SetActive(true); Selection.activeGameObject=instance;
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
    }
}
