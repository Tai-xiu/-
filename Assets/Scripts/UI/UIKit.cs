using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// PixelKit 皮肤层：运行时 UI 全部由代码构建，这里集中提供
/// - 素材加载（Assets/Resources/UI/PixelKit）
/// - 像素字体（TMP 位图字体 PixelFont5x7）
/// - 像素画布（1920×1080，1 素材像素 = 1 屏幕像素）
/// - 常用工厂：面板 / 槽位 / 按钮 / 进度条 / 文字
/// 迁就旧代码：LegacyScale = 1.5，旧尺寸以 1280×720 为基准，乘 1.5 后在 1920 画布上保持原视觉大小。
/// </summary>
public static class UIKit
{
    public const float LegacyScale = 1.5f;
    public const int RefW = 1920, RefH = 1080;
    const string ROOT = "UI/PixelKit/";

    public static TMP_FontAsset Font;

    public static Sprite Panel, PanelInner, TitleBar, Tooltip, Divider, CircleBtn;
    public static Sprite Slot, SlotHover, SlotSelected;
    public static Sprite BtnN, BtnH, BtnP, BtnD;
    public static Sprite BarBg, BarFrame, BarHp, BarMp, BarSp, BarExp, BarWhite;
    public static Sprite IconClose, IconMinus, IconPlus, IconLock, IconCaret;

    static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Font = Resources.Load<TMP_FontAsset>(ROOT + "Fonts/PixelFont5x7");
        if (Font == null) Debug.LogError("[UIKit] 缺字体 UI/PixelKit/Fonts/PixelFont5x7");

        Panel = L("panel_frame");
        PanelInner = L("panel_inner");
        TitleBar = L("titlebar");
        Tooltip = L("tooltip");
        Divider = L("divider");
        CircleBtn = L("circle_btn");
        Slot = L("slot");
        SlotHover = L("slot_hover");
        SlotSelected = L("slot_selected");
        BtnN = L("btn_normal");
        BtnH = L("btn_hover");
        BtnP = L("btn_pressed");
        BtnD = L("btn_disabled");
        BarBg = L("bar_bg");
        BarFrame = L("bar_frame");
        BarHp = L("bar_fill_hp");
        BarMp = L("bar_fill_mp");
        BarSp = L("bar_fill_sp");
        BarExp = L("bar_fill_exp");
        BarWhite = L("bar_fill_white");
        IconClose = L("icon_close");
        IconMinus = L("icon_minus");
        IconPlus = L("icon_plus");
        IconLock = L("icon_lock");
        IconCaret = L("icon_caret");
    }

    static Sprite L(string name) => Resources.Load<Sprite>(ROOT + name);

    // ================= 画布 =================

    static Canvas _canvas;

    /// <summary>像素画布（1920×1080 基准，1:1 素材）。跨场景常驻，独立于旧的 1280×720 GameCanvas。</summary>
    public static Canvas PixelCanvas()
    {
        if (_canvas != null) return _canvas;
        var go = new GameObject("UICanvas_Pixel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.pixelPerfect = true;
        _canvas.sortingOrder = 30;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(RefW, RefH);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = 0.5f;
        sc.referencePixelsPerUnit = 100;
        Object.DontDestroyOnLoad(go);
        return _canvas;
    }

    // ================= 工厂 =================

    /// <summary>九宫格图片（默认 Sliced）</summary>
    public static Image Img(Transform parent, string name, Sprite sp, Vector2 size, Vector2 pos, bool sliced = true)
    {
        EnsureLoaded();
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sp;
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return img;
    }

    /// <summary>铺满父级的图片</summary>
    public static Image Stretch(Transform parent, string name, Sprite sp, bool sliced = true)
    {
        var img = Img(parent, name, sp, Vector2.zero, Vector2.zero, sliced);
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return img;
    }

    /// <summary>像素字体文本</summary>
    public static TextMeshProUGUI Txt(Transform parent, string name, string content, int size, Color color,
        TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        EnsureLoaded();
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = Font;
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = ConvertAnchor(anchor);
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        // 像素字体不需要额外描边，靠暗描边色块保证可读
        return t;
    }

    static TextAlignmentOptions ConvertAnchor(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Center;
        }
    }

    /// <summary>进度条部件（底槽 + 填充 + 可选残影 + 可选文本）</summary>
    public class Bar
    {
        public RectTransform root;      // 条本体（左下锚定）
        public RectTransform inner;     // 去掉边框的内区
        public RectTransform fill;      // 前景填充（左 pivot，改宽度）
        public Image fillImg;
        public RectTransform ghost;     // 残影（可选，白色半透明）
        public TextMeshProUGUI text;    // 数值（可选）
        public float innerWidth;
    }

    /// <summary>构建一条像素进度条。size 为含边框总尺寸。</summary>
    public static Bar BuildBar(Transform parent, string name, Vector2 size, Sprite fillSprite,
        float inset = 4f, bool withGhost = false)
    {
        EnsureLoaded();
        if (fillSprite == null) fillSprite = BarWhite;   // 首次调用时实参可能还未初始化
        var b = new Bar();
        var rootGo = new GameObject(name, typeof(RectTransform));
        rootGo.transform.SetParent(parent, false);
        b.root = rootGo.GetComponent<RectTransform>();
        b.root.anchorMin = b.root.anchorMax = new Vector2(0f, 0f);
        b.root.pivot = new Vector2(0f, 0f);
        b.root.sizeDelta = size;

        var bg = Img(b.root, "BG", BarFrame, size, Vector2.zero);
        bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0f, 0f);
        bg.rectTransform.pivot = new Vector2(0f, 0f);
        bg.rectTransform.sizeDelta = size;

        var innerGo = new GameObject("Inner", typeof(RectTransform));
        innerGo.transform.SetParent(b.root, false);
        b.inner = innerGo.GetComponent<RectTransform>();
        b.inner.anchorMin = Vector2.zero;
        b.inner.anchorMax = Vector2.one;
        b.inner.offsetMin = new Vector2(inset, inset);
        b.inner.offsetMax = new Vector2(-inset, -inset);
        b.innerWidth = size.x - inset * 2f;

        if (withGhost)
        {
            var g = Img(b.inner, "Ghost", fillSprite, new Vector2(b.innerWidth, 0f), Vector2.zero);
            b.ghost = g.rectTransform;
            b.ghost.anchorMin = new Vector2(0f, 0f);
            b.ghost.anchorMax = new Vector2(0f, 1f);
            b.ghost.offsetMin = Vector2.zero;
            b.ghost.offsetMax = Vector2.zero;
            b.ghost.pivot = new Vector2(0f, 0.5f);
            b.ghost.sizeDelta = new Vector2(b.innerWidth, 0f);
            g.color = new Color(1f, 1f, 1f, 0.45f);
        }

        var f = Img(b.inner, "Fill", fillSprite, new Vector2(b.innerWidth, 0f), Vector2.zero);
        b.fill = f.rectTransform;
        b.fillImg = f;
        b.fill.anchorMin = new Vector2(0f, 0f);
        b.fill.anchorMax = new Vector2(0f, 1f);
        b.fill.offsetMin = Vector2.zero;
        b.fill.offsetMax = Vector2.zero;
        b.fill.pivot = new Vector2(0f, 0.5f);
        b.fill.sizeDelta = new Vector2(b.innerWidth, 0f);
        return b;
    }
}
