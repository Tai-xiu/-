// HUDRoot.prefab 构建器：按 1920×1080 设计尺寸制作，根节点左下锚定。
// 菜单：Tools/UI/构建 HUDRoot Prefab
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class HUDRootPrefabBuilder
{
    const string OUT = "Assets/Prefabs/UI/HUDRoot.prefab";

    [MenuItem("Tools/UI/构建 HUDRoot Prefab")]
    public static void Build()
    {
        UIKit.EnsureLoaded();
        if (UIKit.BarFrame == null || UIKit.Font == null)
        {
            Debug.LogError("[HUDRoot] UIKit 素材/字体未加载，检查 Assets/Resources/UI/PixelKit");
            return;
        }

        // 根：左下锚定、1920×1080 设计尺寸（由 GameUIRefs 按画布基准缩放）
        var root = new GameObject("HUDRoot", typeof(RectTransform));
        var rrt = root.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0f, 0f);
        rrt.anchorMax = new Vector2(0f, 0f);
        rrt.pivot = new Vector2(0f, 0f);
        rrt.anchoredPosition = Vector2.zero;
        rrt.sizeDelta = new Vector2(1280f, 720f);

        BuildHpBar(root.transform);
        BuildStamina(root.transform);
        BuildHotbar(root.transform);
        BuildSkillBar(root.transform);

        System.IO.Directory.CreateDirectory("Assets/Prefabs/UI");
        AssetDatabase.Refresh();   // 新目录先入 AssetDatabase，否则保存 Prefab 会失败
        PrefabUtility.SaveAsPrefabAsset(root, OUT);
        Object.DestroyImmediate(root);

        string setup = SetupScene();
        Debug.Log("[HUDRoot] 已生成 " + OUT + "；" + setup);
    }

    /// <summary>把 GameUIRefs 挂到当前场景的 GameManager 并绑定 Prefab（幂等）</summary>
    static string SetupScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OUT);
        if (prefab == null) return "场景接线失败：Prefab 读取不到";

        var gm = Object.FindObjectOfType<GameManager>();
        if (gm == null) return "场景里没有 GameManager";

        var refs = gm.GetComponent<GameUIRefs>();
        bool added = false;
        if (refs == null)
        {
            refs = gm.gameObject.AddComponent<GameUIRefs>();
            added = true;
        }
        refs.hudPrefab = prefab;
        EditorUtility.SetDirty(refs);
        var scene = gm.gameObject.scene;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        return "已在 " + scene.name + " 的 GameManager 上" + (added ? "添加" : "更新") + " GameUIRefs";
    }

    static void BuildHpBar(Transform parent)
    {
        var bar = NewRect(parent, "HPBar_Spec", new Vector2(32f, 116f), new Vector2(320f, 16f));
        AddBg(bar, new Vector2(320f, 16f));
        var inner = NewRect(bar, "Inner", new Vector2(4f, 4f), new Vector2(-8f, -8f), true);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);

        AddFill(inner, "Ghost", new Color(1f, 1f, 1f, 0.5f));
        AddFill(inner, "Fill", new Color32(0xE5, 0x48, 0x4D, 0xFF));

        // 分段刻度：100 血 / 25 一格 = 3 条
        for (int i = 1; i <= 3; i++)
        {
            var t = new GameObject("Tick_" + i, typeof(RectTransform), typeof(Image));
            t.transform.SetParent(inner, false);
            var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(i * 0.25f, 0f);
            trt.anchorMax = new Vector2(i * 0.25f, 1f);
            trt.pivot = Vector2.zero;
            trt.sizeDelta = new Vector2(2f, 0f);
            trt.anchoredPosition = Vector2.zero;
            var img = t.GetComponent<Image>();
            img.color = new Color32(0x1A, 0x1A, 0x22, 0xFF);
            img.raycastTarget = false;
        }

        var value = NewText(bar, "Value", "", 14, Color.white, TextAnchor.LowerLeft);
        var vrt = value.rectTransform;
        vrt.anchorMin = new Vector2(0f, 1f);
        vrt.anchorMax = new Vector2(0f, 1f);
        vrt.pivot = new Vector2(0f, 0f);
        vrt.sizeDelta = new Vector2(250f, 18f);
        vrt.anchoredPosition = new Vector2(0f, 3f);
    }

    static void BuildStamina(Transform parent)
    {
        var bar = NewRect(parent, "StaminaBar_Spec", new Vector2(32f, 92f), new Vector2(240f, 16f));
        AddBg(bar, new Vector2(240f, 16f));
        var inner = NewRect(bar, "Inner", Vector2.zero, Vector2.zero, true);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);
        AddFill(inner, "Fill", new Color32(0xF5, 0xC5, 0x18, 0xFF));
    }

    static void BuildHotbar(Transform parent)
    {
        var bar = NewRect(parent, "HotbarBar_Spec", new Vector2(32f, 32f), new Vector2(216f, 48f));
        for (int i = 0; i < 4; i++)
        {
            var slot = new GameObject("Slot_" + (i + 1), typeof(RectTransform), typeof(Image));
            slot.transform.SetParent(bar, false);
            var rt = slot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(48f, 48f);
            rt.anchoredPosition = new Vector2(i * 56f, 0f);
            var img = slot.GetComponent<Image>();
            img.sprite = UIKit.Slot;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;

            var cnt = NewText(slot.transform, "Count", "", 12, Color.white, TextAnchor.LowerRight);
            var crt = cnt.rectTransform;
            crt.anchorMin = new Vector2(1f, 0f);
            crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(1f, 0f);
            crt.sizeDelta = new Vector2(34f, 14f);
            crt.anchoredPosition = new Vector2(-2f, 2f);
        }
    }


    static void BuildSkillBar(Transform parent)
    {
        var bar = NewRect(parent, "SkillBar_Spec", new Vector2(264f, 32f), new Vector2(104f, 48f));
        string[] keys = { "Q", "E" };
        for (int i = 0; i < keys.Length; i++)
        {
            var slot = new GameObject("SkillSlot_" + keys[i], typeof(RectTransform), typeof(Image));
            slot.transform.SetParent(bar, false);
            var rt = slot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(48f, 48f);
            rt.anchoredPosition = new Vector2(i * 56f, 0f);
            var img = slot.GetComponent<Image>();
            img.sprite = UIKit.Slot;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(slot.transform, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(36f, 36f);
            irt.anchoredPosition = Vector2.zero;
            var iimg = iconGo.GetComponent<Image>();
            iimg.raycastTarget = false;
            iimg.preserveAspect = true;

            var key = NewText(slot.transform, "KeyLabel", keys[i], 12,
                new Color32(0xF5, 0xC5, 0x18, 0xFF), TextAnchor.UpperLeft);
            var krt = key.rectTransform;
            krt.anchorMin = new Vector2(0f, 1f);
            krt.anchorMax = new Vector2(0f, 1f);
            krt.pivot = new Vector2(0f, 1f);
            krt.sizeDelta = new Vector2(20f, 14f);
            krt.anchoredPosition = new Vector2(3f, -2f);

            var cdGo = new GameObject("CdOverlay", typeof(RectTransform), typeof(Image));
            cdGo.transform.SetParent(slot.transform, false);
            var crt = cdGo.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var cdImg = cdGo.GetComponent<Image>();
            cdImg.color = new Color(0f, 0f, 0f, 0.65f);
            cdImg.type = Image.Type.Filled;
            cdImg.fillMethod = Image.FillMethod.Radial360;
            cdImg.fillOrigin = (int)Image.Origin360.Top;
            cdImg.fillClockwise = true;
            cdImg.fillAmount = 0f;
            cdImg.raycastTarget = false;

            var cdt = NewText(slot.transform, "CdText", "", 12, Color.white, TextAnchor.LowerRight);
            var trt = cdt.rectTransform;
            trt.anchorMin = new Vector2(1f, 0f);
            trt.anchorMax = new Vector2(1f, 0f);
            trt.pivot = new Vector2(1f, 0f);
            trt.sizeDelta = new Vector2(30f, 14f);
            trt.anchoredPosition = new Vector2(-3f, 2f);
        }
    }

    // ---------- helpers ----------
    static RectTransform NewRect(Transform parent, string name, Vector2 pos, Vector2 size, bool stretch = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    static void AddBg(Transform parent, Vector2 size)
    {
        var go = new GameObject("BG", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = UIKit.BarFrame;
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
    }

    static void AddFill(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = UIKit.BarWhite;
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
    }

    static TMPro.TextMeshProUGUI NewText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
        t.font = UIKit.Font;
        t.text = content;
        t.fontSize = size;
        t.color = color;
        switch (anchor)
        {
            case TextAnchor.UpperLeft: t.alignment = TMPro.TextAlignmentOptions.TopLeft; break;
            case TextAnchor.UpperCenter: t.alignment = TMPro.TextAlignmentOptions.Top; break;
            case TextAnchor.UpperRight: t.alignment = TMPro.TextAlignmentOptions.TopRight; break;
            case TextAnchor.MiddleLeft: t.alignment = TMPro.TextAlignmentOptions.Left; break;
            case TextAnchor.MiddleRight: t.alignment = TMPro.TextAlignmentOptions.Right; break;
            case TextAnchor.LowerLeft: t.alignment = TMPro.TextAlignmentOptions.BottomLeft; break;
            case TextAnchor.LowerCenter: t.alignment = TMPro.TextAlignmentOptions.Bottom; break;
            case TextAnchor.LowerRight: t.alignment = TMPro.TextAlignmentOptions.BottomRight; break;
            default: t.alignment = TMPro.TextAlignmentOptions.Center; break;
        }
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        return t;
    }
}
