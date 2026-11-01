// 批次3-背包：格子模板 Prefab。
// 菜单：Tools/UI/构建 背包格子 Prefab
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class UIInventoryPrefabBuilder
{
    const string DIR = "Assets/Prefabs/UI/Widgets/";
    const string SLOT = DIR + "InventorySlot.prefab";

    [MenuItem("Tools/UI/构建 背包格子 Prefab")]
    public static void Build()
    {
        UIKit.EnsureLoaded();
        System.IO.Directory.CreateDirectory(DIR);
        AssetDatabase.Refresh();

        // 模板：只负责视觉（kit slot + Outline 高亮），InventoryCellUI 由代码在绑定时补挂
        var go = new GameObject("InventorySlot", typeof(RectTransform), typeof(Image), typeof(Outline));
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(74f, 74f);

        var img = go.GetComponent<Image>();
        img.sprite = UIKit.Slot;
        img.type = Image.Type.Sliced;
        img.raycastTarget = true;                 // 必须可拾取，否则拖拽/点击无效
        img.color = Color.white;

        var ol = go.GetComponent<Outline>();
        ol.effectColor = new Color(0.35f, 0.28f, 0.18f, 1f);
        ol.effectDistance = new Vector2(1f, 1f);

        PrefabUtility.SaveAsPrefabAsset(go, SLOT);
        Object.DestroyImmediate(go);

        string setup = SetupScene();
        Debug.Log("[UIInventory] 生成 " + SLOT + "；" + setup);
    }

    static string SetupScene()
    {
        var slot = AssetDatabase.LoadAssetAtPath<GameObject>(SLOT);
        var gm = Object.FindObjectOfType<GameManager>();
        if (gm == null) return "场景里没有 GameManager";
        var refs = gm.GetComponent<GameUIRefs>();
        if (refs == null) return "GameManager 上没有 GameUIRefs";
        refs.inventorySlotPrefab = slot;
        EditorUtility.SetDirty(refs);
        var scene = gm.gameObject.scene;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        return "已写入 " + scene.name;
    }
}
