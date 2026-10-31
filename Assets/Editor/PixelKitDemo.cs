// PixelKit 演示：在当前场景临时搭一层 UI，截图后立即销毁（不改动场景内容）。
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class PixelKitDemo
{
    const string KIT = "Assets/UI/PixelKit/";
    static Font _font;

    public static string Run(string outPath)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (scene.isDirty) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

        _font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/字魂醒狮体.ttf");
        var root = new GameObject("TMP_PixelKitDemo", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        canvas.pixelPerfect = true;
        var sc = root.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        sc.referencePixelsPerUnit = 100;
        root.AddComponent<GraphicRaycaster>();
        var t = root.transform;

        // ---------- 背包窗口 ----------
        var panel = Img("Panel", t, "panel_frame", Image.Type.Sliced);
        Rect(panel.rectTransform, new Vector2(-500, 20), new Vector2(460, 520));

        var tb = Img("TitleBar", panel.transform, "titlebar", Image.Type.Sliced);
        Rect(tb.rectTransform, new Vector2(0, 226), new Vector2(420, 28));
        var title = Txt("Title", tb.transform, "背 包", 20, new Color32(242, 208, 124, 255));
        Fit(title.rectTransform, 0, 0);
        var close = Img("Close", tb.transform, "icon_close", Image.Type.Simple);
        Rect(close.rectTransform, new Vector2(196, 0), new Vector2(16, 16));

        for (int gy = 0; gy < 4; gy++)
            for (int gx = 0; gx < 6; gx++)
            {
                string n = "slot";
                if (gx == 0 && gy == 0) n = "slot_hover";
                if (gx == 1 && gy == 0) n = "slot_selected";
                var s = Img("Slot", panel.transform, n, Image.Type.Sliced);
                Rect(s.rectTransform, new Vector2(-176 + gx * 70, 150 - gy * 70), new Vector2(58, 58));
            }

        var exp = Img("ExpBG", panel.transform, "bar_bg", Image.Type.Sliced);
        Rect(exp.rectTransform, new Vector2(40, -104), new Vector2(240, 18));
        var expf = Img("ExpFill", exp.transform, "bar_fill_exp", Image.Type.Sliced);
        Rect(expf.rectTransform, new Vector2(-2, 0), new Vector2(150, 12));
        expf.rectTransform.anchorMin = new Vector2(0, 0.5f);
        expf.rectTransform.anchorMax = new Vector2(0, 0.5f);
        expf.rectTransform.anchoredPosition = new Vector2(2, 0);

        Btn(panel.transform, "使用", new Vector2(-90, -186), "btn_normal");
        Btn(panel.transform, "丢弃", new Vector2(60, -186), "btn_pressed");

        // ---------- 右侧：属性条 + 圆形按钮 + 提示框 ----------
        string[] fills = { "bar_fill_hp", "bar_fill_mp", "bar_fill_sp", "bar_fill_exp" };
        float[] widths = { 250, 150, 190, 110 };
        for (int i = 0; i < 4; i++)
        {
            var bg = Img("BarBG" + i, t, "bar_bg", Image.Type.Sliced);
            Rect(bg.rectTransform, new Vector2(500, 240 - i * 34), new Vector2(320, 22));
            var f = Img("BarFill" + i, bg.transform, fills[i], Image.Type.Sliced);
            var frt = f.rectTransform;
            frt.anchorMin = new Vector2(0, 0.5f);
            frt.anchorMax = new Vector2(0, 0.5f);
            frt.pivot = new Vector2(0, 0.5f);
            frt.anchoredPosition = new Vector2(3, 0);
            frt.sizeDelta = new Vector2(widths[i], 16);
        }

        string[] icons = { "icon_close", "icon_minus", "icon_plus", "icon_lock", "icon_caret" };
        for (int i = 0; i < icons.Length; i++)
        {
            var ic = Img("Icon" + i, t, icons[i], Image.Type.Simple);
            Rect(ic.rectTransform, new Vector2(390 + i * 40, 70), new Vector2(16, 16));
        }

        var circle = Img("CircleBtn", t, "circle_btn", Image.Type.Simple);
        Rect(circle.rectTransform, new Vector2(560, 60), new Vector2(72, 72));

        var tip = Img("Tooltip", t, "tooltip", Image.Type.Sliced);
        Rect(tip.rectTransform, new Vector2(500, -120), new Vector2(340, 130));
        var tipTxt = Txt("TipText", tip.transform, "神秘老人\n\n年轻人，你为何会来到\n这危险的地方？", 17, new Color32(228, 216, 190, 255));
        var trt = tipTxt.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16, 12); trt.offsetMax = new Vector2(-12, -10);
        tipTxt.alignment = TextAnchor.UpperLeft;
        tipTxt.horizontalOverflow = HorizontalWrapMode.Wrap;

        var div = Img("Divider", t, "divider", Image.Type.Simple);
        Rect(div.rectTransform, new Vector2(500, -240), new Vector2(64, 9));

        // 截图时临时隐藏其它画布（例如主菜单），只保留演示层
        var hidden = new System.Collections.Generic.List<GameObject>();
        foreach (var c in Object.FindObjectsOfType<Canvas>(true))
        {
            if (c.gameObject == root) continue;
            if (!c.gameObject.activeInHierarchy) continue;
            hidden.Add(c.gameObject);
            c.gameObject.SetActive(false);
        }

        var cap = SceneCapture.Capture(outPath, 1920, 1080);

        for (int i = 0; i < hidden.Count; i++) hidden[i].SetActive(true);
        Object.DestroyImmediate(root);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        return cap;
    }

    // ---------- helpers ----------
    static Image Img(string name, Transform parent, string sprite, Image.Type type)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(KIT + sprite + ".png");
        img.type = type;
        if (img.sprite == null) Debug.LogError("[PixelKitDemo] 缺图 " + sprite);
        return img;
    }

    static Text Txt(string name, Transform parent, string content, int size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tx = go.AddComponent<Text>();
        tx.text = content;
        tx.font = _font;
        tx.fontSize = size;
        tx.color = color;
        tx.alignment = TextAnchor.MiddleCenter;
        tx.horizontalOverflow = HorizontalWrapMode.Overflow;
        tx.verticalOverflow = VerticalWrapMode.Overflow;
        tx.raycastTarget = false;
        var o = go.AddComponent<Outline>();
        o.effectColor = new Color(0, 0, 0, 0.9f);
        o.effectDistance = new Vector2(1, -1);
        return tx;
    }

    static void Btn(Transform parent, string label, Vector2 pos, string sprite)
    {
        var img = Img("Btn_" + label, parent, sprite, Image.Type.Sliced);
        Rect(img.rectTransform, pos, new Vector2(130, 38));
        var tx = Txt("Txt", img.transform, label, 17, new Color32(242, 208, 124, 255));
        Fit(tx.rectTransform, 0, 0);
    }

    static void Rect(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Fit(RectTransform rt, float padX, float padY)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }
}
