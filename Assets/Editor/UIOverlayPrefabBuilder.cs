// 准星 / 交互提示 独立 Prefab 构建器（1280×720 基准）
// 菜单：Tools/UI/构建 准星与交互提示 Prefab
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class UIOverlayPrefabBuilder
{
    const string DIR = "Assets/Prefabs/UI/";
    const string CROSSHAIR = DIR + "Crosshair.prefab";
    const string INTERACT = DIR + "InteractPrompt.prefab";

    [MenuItem("Tools/UI/构建 准星与交互提示 Prefab")]
    public static void Build()
    {
        System.IO.Directory.CreateDirectory(DIR);
        AssetDatabase.Refresh();

        BuildCrosshair();
        BuildInteractPrompt();
        string setup = SetupScene();
        Debug.Log("[UIOverlay] 生成完成：" + CROSSHAIR + " / " + INTERACT + "；" + setup);
    }

    // ---------- 准星：屏幕正中 14×14，横竖两条 2px 白线 ----------
    static void BuildCrosshair()
    {
        var root = new GameObject("Crosshair", typeof(RectTransform));
        var rt = root.GetComponent<RectTransform>();
        Center(rt, new Vector2(14f, 14f));
        AddLine(rt, "Line_H", new Vector2(14f, 2f));
        AddLine(rt, "Line_V", new Vector2(2f, 14f));
        root.SetActive(false);   // 初始隐藏（锁定才显示）
        PrefabUtility.SaveAsPrefabAsset(root, CROSSHAIR);
        Object.DestroyImmediate(root);
    }

    static void AddLine(RectTransform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        Center(rt, size);
        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.85f);
        img.raycastTarget = false;
    }

    // ---------- 交互提示：四角括号 + 蓄力环 + 提示行 ----------
    static void BuildInteractPrompt()
    {
        var root = new GameObject("InteractPrompt", typeof(RectTransform));
        var rrt = root.GetComponent<RectTransform>();
        Center(rrt, Vector2.zero);

        // 四角括号（24px 方框，7px 角臂，2px 线宽）
        var bracket = new GameObject("Bracket", typeof(RectTransform));
        bracket.transform.SetParent(root.transform, false);
        var brt = bracket.GetComponent<RectTransform>();
        Center(brt, Vector2.zero);
        const float box = 24f, arm = 7f, th = 2f;
        float hx = box * 0.5f, hy = box * 0.5f;
        AddBar(brt, new Vector2(-hx + arm * 0.5f, hy - th * 0.5f), new Vector2(arm, th));
        AddBar(brt, new Vector2(-hx + th * 0.5f, hy - arm * 0.5f), new Vector2(th, arm));
        AddBar(brt, new Vector2(hx - arm * 0.5f, hy - th * 0.5f), new Vector2(arm, th));
        AddBar(brt, new Vector2(hx - th * 0.5f, hy - arm * 0.5f), new Vector2(th, arm));
        AddBar(brt, new Vector2(-hx + arm * 0.5f, -hy + th * 0.5f), new Vector2(arm, th));
        AddBar(brt, new Vector2(-hx + th * 0.5f, -hy + arm * 0.5f), new Vector2(th, arm));
        AddBar(brt, new Vector2(hx - arm * 0.5f, -hy + th * 0.5f), new Vector2(arm, th));
        AddBar(brt, new Vector2(hx - th * 0.5f, -hy + arm * 0.5f), new Vector2(th, arm));
        bracket.SetActive(false);

        // 蓄力环（44px，Radial360 顶点起顺时针）
        var ring = new GameObject("HoldRing", typeof(RectTransform), typeof(Image));
        ring.transform.SetParent(root.transform, false);
        var rrt2 = ring.GetComponent<RectTransform>();
        Center(rrt2, new Vector2(44f, 44f));
        var rimg = ring.GetComponent<Image>();
        rimg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/PixelKit/ring_hold.png");
        rimg.type = Image.Type.Filled;
        rimg.fillMethod = Image.FillMethod.Radial360;
        rimg.fillOrigin = (int)Image.Origin360.Top;
        rimg.fillClockwise = true;
        rimg.fillAmount = 0f;
        rimg.color = new Color(1f, 1f, 1f, 0.9f);
        rimg.raycastTarget = false;
        ring.SetActive(false);

        // 提示行（准星下方 34px）
        var hint = new GameObject("Hint", typeof(RectTransform), typeof(Text));
        hint.transform.SetParent(root.transform, false);
        var hrt = hint.GetComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.sizeDelta = new Vector2(400f, 22f);
        hrt.anchoredPosition = new Vector2(0f, -34f);
        var t = hint.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 14;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.text = "";
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var ol = hint.AddComponent<Outline>();
        ol.effectColor = new Color32(0x1A, 0x1A, 0x22, 0xFF);
        ol.effectDistance = new Vector2(1f, -1f);
        hint.SetActive(false);

        PrefabUtility.SaveAsPrefabAsset(root, INTERACT);
        Object.DestroyImmediate(root);
    }

    static void AddBar(RectTransform parent, Vector2 center, Vector2 size)
    {
        var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.9f);
        img.raycastTarget = false;
    }

    static void Center(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    // ---------- 场景接线：写入当前场景 GameManager 的 GameUIRefs ----------
    static string SetupScene()
    {
        var cross = AssetDatabase.LoadAssetAtPath<GameObject>(CROSSHAIR);
        var interact = AssetDatabase.LoadAssetAtPath<GameObject>(INTERACT);
        var gm = Object.FindObjectOfType<GameManager>();
        if (gm == null) return "场景里没有 GameManager";
        var refs = gm.GetComponent<GameUIRefs>();
        if (refs == null) return "GameManager 上没有 GameUIRefs";

        refs.crosshairPrefab = cross;
        refs.interactPromptPrefab = interact;
        EditorUtility.SetDirty(refs);
        var scene = gm.gameObject.scene;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        return "已写入 " + scene.name + " 的 GameUIRefs";
    }
}
