// 用 PixelKit 风格重建主菜单 UI（不修改原 Canvas，只隐藏；旧 MainMenuUI 会被替换）。
// 菜单：Tools/主菜单/重建 MainMenu UI（像素风）
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;

public static class MainMenuUIBuilder
{
    const string KIT = "Assets/UI/PixelKit/";
    const string TXT = "Assets/UI/PixelKit/Menu/";
    const string SCRIM = "Assets/UI/MainMenuScene/Textures/menu_scrim_left";
    const string CANVAS_NAME = "MainMenuUI";

    // 版面（1920×1080 参考坐标，中心为原点）
    const float COL_X = -445f;
    const float PLATE_Y = 182f;
    static readonly Vector2 PLATE_SIZE = new Vector2(700, 180);
    static readonly float[] BTN_Y = { 26f, -62f, -150f, -238f };
    const float BTN_W = 520f, BTN_H = 68f;

    [MenuItem("Tools/主菜单/重建 MainMenu UI（像素风）")]
    public static void BuildFromMenu() { Build(); }

    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);

        // 1) 隐藏其它画布（原 Canvas 保留内容，只关显示）
        var hidden = new List<string>();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.GetComponent<Canvas>() != null && go.name != CANVAS_NAME)
            {
                if (go.activeSelf) go.SetActive(false);
                hidden.Add(go.name);
            }
        }

        // 2) 重建画布
        var old = GameObject.Find(CANVAS_NAME);
        if (old != null) Object.DestroyImmediate(old);

        var canvasGO = new GameObject(CANVAS_NAME, typeof(RectTransform));
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100;
        canvasGO.AddComponent<GraphicRaycaster>();
        var root = canvasGO.transform;

        // 3) 左侧压暗
        var scrim = Img("Scrim_Left", root, SCRIM, Image.Type.Simple);
        var srt = scrim.rectTransform;
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(0, 1);
        srt.pivot = new Vector2(0, 0.5f);
        srt.anchoredPosition = Vector2.zero;
        srt.sizeDelta = new Vector2(1180, 0);
        scrim.raycastTarget = false;

        // 4) 标题牌
        var plate = Img("TitlePlate", root, KIT + "panel_frame", Image.Type.Sliced);
        Place(plate.rectTransform, new Vector2(COL_X, PLATE_Y), PLATE_SIZE);
        plate.raycastTarget = false;

        var title = Img("Title", plate.transform, TXT + "title_main", Image.Type.Simple);
        Native(title, new Vector2(0, 26));

        var div = Img("Divider", plate.transform, KIT + "divider", Image.Type.Simple);
        Native(div, new Vector2(0, -20));

        var sub = Img("Subtitle", plate.transform, TXT + "subtitle_cn", Image.Type.Simple);
        Native(sub, new Vector2(0, -54));

        // 5) 菜单按钮
        string[] names = { "START", "SETTINGS", "CREDITS", "EXIT" };
        string[] labelPaths = { TXT + "lbl_start", TXT + "lbl_settings", TXT + "lbl_credits", TXT + "lbl_exit" };
        var normal = Load(KIT + "btn_normal");
        var hover = Load(KIT + "btn_hover");
        var pressed = Load(KIT + "btn_pressed");
        var disabled = Load(KIT + "btn_disabled");

        Button first = null;
        for (int i = 0; i < names.Length; i++)
        {
            var go = new GameObject(names[i], typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            Place(rt, new Vector2(COL_X, BTN_Y[i]), new Vector2(BTN_W, BTN_H));

            var img = go.AddComponent<Image>();
            img.sprite = normal;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.SpriteSwap;
            var st = new SpriteState();
            st.highlightedSprite = hover;
            st.selectedSprite = hover;
            st.pressedSprite = pressed;
            st.disabledSprite = disabled;
            btn.spriteState = st;

            var lbl = Img("Label", go.transform, labelPaths[i], Image.Type.Simple);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0.5f, 0.5f);
            lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = new Vector2(0, -1);
            lrt.sizeDelta = new Vector2(lbl.sprite.rect.width, lbl.sprite.rect.height);
            lbl.raycastTarget = false;

            if (i == 0) first = btn;
        }

        // 6) 角落文字
        var ver = Img("Version", root, TXT + "lbl_version", Image.Type.Simple);
        var vrt = ver.rectTransform;
        vrt.anchorMin = new Vector2(0, 0); vrt.anchorMax = new Vector2(0, 0);
        vrt.pivot = new Vector2(0, 0);
        vrt.anchoredPosition = new Vector2(72, 44);
        vrt.sizeDelta = new Vector2(ver.sprite.rect.width, ver.sprite.rect.height);
        ver.raycastTarget = false;

        var hint = Img("Hint", root, TXT + "lbl_hint", Image.Type.Simple);
        var hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(1, 0); hrt.anchorMax = new Vector2(1, 0);
        hrt.pivot = new Vector2(1, 0);
        hrt.anchoredPosition = new Vector2(-72, 44);
        hrt.sizeDelta = new Vector2(hint.sprite.rect.width, hint.sprite.rect.height);
        hint.raycastTarget = false;

        // 7) 绑定原有逻辑
        var menuUi = Object.FindObjectOfType<MainMenuUI>(true);
        if (menuUi != null)
        {
            UnityEventTools.AddPersistentListener(root.Find("START").GetComponent<Button>().onClick,
                new UnityAction(menuUi.StartGame));
            UnityEventTools.AddPersistentListener(root.Find("EXIT").GetComponent<Button>().onClick,
                new UnityAction(menuUi.QuitGame));
        }

        // 8) 键盘/手柄首选
        var es = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>(true);
        if (es != null && first != null) es.firstSelectedGameObject = first.gameObject;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[MainMenuUI] PixelKit 版已生成；隐藏原画布: " + string.Join(", ", hidden.ToArray()));
    }

    // ---------- helpers ----------
    static Sprite Load(string path)
    {
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path + ".png");
        if (sp == null) Debug.LogError("[MainMenuUI] 缺图: " + path);
        return sp;
    }

    static Image Img(string name, Transform parent, string spritePath, Image.Type type)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = Load(spritePath);
        img.type = type;
        return img;
    }

    /// 按图片原始像素尺寸摆放（1:1，不缩放，保持像素清晰）
    static void Native(Image img, Vector2 pos)
    {
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(img.sprite.rect.width, img.sprite.rect.height);
        img.raycastTarget = false;
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
