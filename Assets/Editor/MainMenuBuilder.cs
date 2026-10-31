// 主菜单 UI 构建器（编辑器工具）
// 产物 = Assets/Scenes/MainMenu.unity 里真实的 UGUI 对象层级，可直接在 Inspector 里改。
// 菜单：Tools/主菜单/重建 MainMenu 场景
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class MainMenuBuilder
{
    const string TEX = "Assets/UI/MainMenu/Textures/";
    const string SCENE = "Assets/Scenes/MainMenu.unity";

    // ---------- palette ----------
    static readonly Color INK = new Color32(10, 8, 15, 255);

    [MenuItem("Tools/主菜单/重建 MainMenu 场景")]
    public static void BuildFromMenu() { Build(); }

    public static void Build()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty) { Debug.LogError("[MainMenu] 当前场景有未保存修改，已中止，避免丢失。"); return; }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- camera ----------
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = INK;
        cam.orthographic = true;
        cam.cullingMask = 0;                 // 纯 UI 场景，不渲染 3D
        camGO.AddComponent<AudioListener>();
        camGO.transform.position = new Vector3(0, 0, -10);

        // ---------- canvas ----------
        var canvasGO = new GameObject("MainMenuCanvas", typeof(RectTransform));
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100;
        canvasGO.AddComponent<GraphicRaycaster>();
        var root = canvasGO.transform;

        // ---------- background ----------
        var wall = MakeImage("BG_Wall", root, "bg_stone", Image.Type.Tiled);
        Stretch(wall.rectTransform);
        wall.raycastTarget = false;

        var shade = MakeImage("BG_Shade", root, null, Image.Type.Simple);
        Stretch(shade.rectTransform);
        shade.color = new Color(0.04f, 0.03f, 0.06f, 0.30f);
        shade.raycastTarget = false;

        var glowC = MakeImage("Glow_Center", root, "glow_warm", Image.Type.Simple);
        Place(glowC.rectTransform, new Vector2(0, 280), new Vector2(1100, 1100));
        glowC.color = new Color(1, 1, 1, 0.55f);
        glowC.raycastTarget = false;

        // ---------- torches ----------
        foreach (int side in new int[] { -1, 1 })
        {
            float x = side * 750f;
            var g = MakeImage("Glow_Torch_" + (side < 0 ? "L" : "R"), root, "glow_warm", Image.Type.Simple);
            Place(g.rectTransform, new Vector2(x, 215), new Vector2(420, 420));
            g.color = new Color(1, 1, 1, 0.80f);
            g.raycastTarget = false;

            var t = MakeImage("Torch_" + (side < 0 ? "L" : "R"), root, "torch", Image.Type.Simple);
            Place(t.rectTransform, new Vector2(x, 210), new Vector2(48, 90));
            t.raycastTarget = false;

            var bracket = MakeImage("Torch_Bracket_" + (side < 0 ? "L" : "R"), root, null, Image.Type.Simple);
            Place(bracket.rectTransform, new Vector2(x, 164), new Vector2(16, 10));
            bracket.color = new Color32(58, 49, 72, 255);
            bracket.raycastTarget = false;
        }

        // ---------- plaque ----------
        var plaque = MakeImage("Plaque", root, "panel_plaque", Image.Type.Sliced);
        Place(plaque.rectTransform, new Vector2(0, 310), new Vector2(1000, 320));
        plaque.raycastTarget = false;

        var title = MakeImage("Title", plaque.transform, "title_main", Image.Type.Simple);
        Place(title.rectTransform, new Vector2(0, 45), new Vector2(505, 99));
        title.raycastTarget = false;

        var divider = MakeImage("Divider", plaque.transform, "divider", Image.Type.Simple);
        Place(divider.rectTransform, new Vector2(0, -24), new Vector2(144, 24));
        divider.raycastTarget = false;

        var subtitle = MakeImage("Subtitle", plaque.transform, "subtitle_cn", Image.Type.Simple);
        Place(subtitle.rectTransform, new Vector2(0, -75), new Vector2(293, 80));
        subtitle.raycastTarget = false;

        // ---------- buttons ----------
        string[] names = { "Btn_Start", "Btn_Continue", "Btn_Settings", "Btn_Quit" };
        string[] icons = { "icon_sword", "icon_scroll", "icon_gear", "icon_door" };
        string[] labels = { "lbl_start", "lbl_continue", "lbl_settings", "lbl_quit" };
        string[] texts = { "开始游戏", "继续游戏", "设置", "退出游戏" };

        float[] yPos = { 68f, -36f, -140f, -244f };
        Button first = null;

        for (int i = 0; i < names.Length; i++)
        {
            var go = new GameObject(names[i], typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            Place(rt, new Vector2(0, yPos[i]), new Vector2(460, 84));

            var img = go.AddComponent<Image>();
            img.sprite = Load("btn_normal");
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.targetGraphic = img;
            var sb = new SpriteState();
            sb.highlightedSprite = Load("btn_hover");
            sb.pressedSprite = Load("btn_pressed");
            sb.selectedSprite = Load("btn_hover");
            sb.disabledSprite = Load("btn_normal");
            btn.spriteState = sb;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.Automatic;
            btn.navigation = nav;
            if (i == 0) first = btn;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.55f);
            shadow.effectDistance = new Vector2(2, -8);

            // icon
            var ic = MakeImage("Icon", go.transform, icons[i], Image.Type.Simple);
            var irt = ic.rectTransform;
            irt.anchorMin = new Vector2(0, 0.5f);
            irt.anchorMax = new Vector2(0, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = new Vector2(52, 0);
            irt.sizeDelta = new Vector2(40, 40);
            ic.raycastTarget = false;

            // label
            var lb = MakeImage("Label", go.transform, labels[i], Image.Type.Simple);
            var lrt = lb.rectTransform;
            var spr = lb.sprite;
            lrt.anchorMin = new Vector2(0, 0.5f);
            lrt.anchorMax = new Vector2(0, 0.5f);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = new Vector2(96, 0);
            lrt.sizeDelta = new Vector2(spr.rect.width, spr.rect.height);
            lb.raycastTarget = false;

            go.AddComponent<DungeonEcho.MainMenuEntry>().label = texts[i];
        }

        // ---------- footer ----------
        var hint = MakeImage("Hint", root, "lbl_hint", Image.Type.Simple);
        var hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0, 0);
        hrt.anchorMax = new Vector2(0, 0);
        hrt.pivot = new Vector2(0, 0);
        hrt.anchoredPosition = new Vector2(70, 26);
        hrt.sizeDelta = new Vector2(hint.sprite.rect.width, hint.sprite.rect.height);
        hint.raycastTarget = false;

        var ver = MakeImage("Version", root, "lbl_version", Image.Type.Simple);
        var vrt = ver.rectTransform;
        vrt.anchorMin = new Vector2(1, 0);
        vrt.anchorMax = new Vector2(1, 0);
        vrt.pivot = new Vector2(1, 0);
        vrt.anchoredPosition = new Vector2(-70, 26);
        vrt.sizeDelta = new Vector2(ver.sprite.rect.width, ver.sprite.rect.height);
        ver.raycastTarget = false;

        // ---------- vignette on top ----------
        var vig = MakeImage("Vignette", root, "vignette", Image.Type.Simple);
        Stretch(vig.rectTransform);
        vig.raycastTarget = false;

        // ---------- event system ----------
        var esGO = new GameObject("EventSystem");
        var es = esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        es.firstSelectedGameObject = first != null ? first.gameObject : null;

        // ---------- controller ----------
        var ctrl = canvasGO.AddComponent<DungeonEcho.MainMenuController>();
        ctrl.firstButton = first != null ? first.gameObject : null;

        // ---------- save ----------
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, SCENE);

        Debug.Log("[MainMenu] 已生成 " + SCENE + "，按钮 " + names.Length + " 个");
    }

    // ---------- helpers ----------
    static Sprite Load(string name)
    {
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(TEX + name + ".png");
        if (sp == null) Debug.LogError("[MainMenu] 找不到 Sprite: " + name);
        return sp;
    }

    static Image MakeImage(string name, Transform parent, string spriteName, Image.Type type)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        if (spriteName != null) img.sprite = Load(spriteName);
        img.type = type;
        if (type == Image.Type.Sliced) img.pixelsPerUnitMultiplier = 1f;
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
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
