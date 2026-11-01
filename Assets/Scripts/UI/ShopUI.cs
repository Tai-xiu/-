using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 商店UI（白盒线框版 V3，对照用户线框图）
/// ─────────────────────────────────────────────
/// 布局：竖版固定尺寸面板（切页/内容变化均不改变面板大小），自上而下——
/// 1. 顶部标题栏：实线方框，左「商店」中警告（背包已满/金币不足）右「金币 n」（实时）
/// 2. Tab切换区：购买/出售两个并排方框——选中=黑底白字，未选中=白底黑字空心框
/// 3. 商品列表区：
///    购买页=左侧竖排分类选项栏（武器/护甲/消耗品）+右侧对应类别商品行；
///    出售页=背包物品行（跳过空格，行→格索引映射），超出显示框时右侧滑动条滚动查看
///    每行：虚线图标位(32×32)+物品名称+属性说明+价格+实线小按钮「购买/出售」
/// 样式：全黑文字、统一2px黑边框、无填充色块（选中Tab黑底白字除外）
/// 数据与表现分离：交易经ShopSystem（Buy/Sell），本类只渲染；
/// Wallet/Inventory的Changed事件驱动刷新；打开/关闭订阅ShopSystem事件
/// </summary>
public class ShopUI : MonoBehaviour
{
    private ShopSystem _shop;
    private PlayerWallet _wallet;
    private Inventory _inventory;

    private GameObject _panel;
    private bool _panelOpen; // 面板当前是否打开（与全局面板栈注册严格一一对应）

    /// <summary>本面板在全局UI状态门中的注册id（面板栈唯一标识）</summary>
    private const string PanelId = "Shop";

    // —— 标题栏 ——
    private Text _coinTitleText;   // 标题右侧「金币 n」
    private Text _warningText;     // 标题中部警告（背包已满/金币不足，原底部信息条上移）

    // —— 购买页分类选项栏（左侧竖排：武器/护甲/消耗品） ——
    private readonly Image[] _catImages = new Image[3];
    private readonly Text[] _catTexts = new Text[3];
    private int _buyCategory; // 0=武器 1=护甲 2=消耗品

    // —— 出售页滚动 ——
    private ScrollRect _sellScroll; // 出售行超出显示框时滚动查看

    // —— 页签 ——
    private Image _tabBuyImage;
    private Text _tabBuyText;
    private Image _tabSellImage;
    private Text _tabSellText;
    private int _currentPage; // 0=购买 1=出售

    // —— 列表 ——
    private GameObject _buyPage;
    private GameObject _sellPage;
    private RectTransform _buyList;
    private RectTransform _sellList;
    private readonly List<GameObject> _rows = new List<GameObject>();

    // ===== 白盒样式常量（与InventoryUI一致） =====
    private static readonly Color PanelBg = new Color(0.94f, 0.94f, 0.94f, 1f);      // 面板底色
    private static readonly Color LineColor = new Color(0.85f, 0.75f, 0.53f, 1f);       // 统一线条/文字黑
    private static readonly Color WarnColor = new Color(0.65f, 0.12f, 0.12f, 1f);    // 警告文字深红（同InventoryUI提示色）
    private static readonly Color TabSelectedBg = new Color(0.1f, 0.1f, 0.1f, 1f);   // 选中Tab黑底
    private static readonly Color TabUnselectedText = new Color(0.55f, 0.50f, 0.42f, 1f); // 未选中Tab暗字
    private static readonly Color TabSelectedText = new Color(0.95f, 0.95f, 0.95f, 1f); // 选中Tab白字
    private const float BorderWidth = 2f;   // 统一实线边框粗细
    private const float DashSegLen = 8f;    // 虚线段长
    private const float RowHeight = 48f;    // 商品行高（等宽等高）

    /// <summary>购买页分类栏的过滤顺序与标签（与ItemType对应；新材料类商品需扩栏）</summary>
    private static readonly ItemType[] CategoryTypes = { ItemType.Weapon, ItemType.Armor, ItemType.Consumable };
    private static readonly string[] CategoryLabels = { "武器", "护甲", "消耗品" };

    private void Start()
    {
        _wallet = GetComponent<PlayerWallet>();
        _inventory = GetComponent<Inventory>();
        _shop = FindObjectOfType<ShopSystem>();
        if (_shop == null)
        {
            Debug.LogError("[ShopUI] 场景中未找到ShopSystem（商店区域）");
            enabled = false;
            return;
        }

        EnsureEventSystem();
        Canvas canvas = EnsureCanvas();
        BuildPanel(canvas.transform);

        // 订阅交互与数据事件
        _shop.OpenRequested += OpenPanel;
        _shop.CloseRequested += ClosePanel;
        _shop.PlayerPresenceChanged += OnPresenceChanged;
        _wallet.Changed += Refresh;
        _inventory.Changed += Refresh;

        _panel.SetActive(false); // 默认收起
    }

    private void OnDestroy()
    {
        if (_shop != null)
        {
            _shop.OpenRequested -= OpenPanel;
            _shop.CloseRequested -= ClosePanel;
            _shop.PlayerPresenceChanged -= OnPresenceChanged;
        }
        if (_wallet != null) _wallet.Changed -= Refresh;
        if (_inventory != null) _inventory.Changed -= Refresh;

        // 防计数泄漏：面板开着时组件随场景/玩家销毁（如传送切换），从全局面板栈注销，
        // 否则栈永久残留导致后续冻结/路由异常
        if (_panelOpen)
        {
            _panelOpen = false;
            GameUIState.UnregisterPanel(PanelId);
        }
    }

    private void OpenPanel()
    {
        if (_panelOpen) return; // 防事件重复触发导致重复注册
        _panelOpen = true;
        GameUIState.RegisterPanel(PanelId, ClosePanel); // 入栈（closeAction供Esc分层路由调用）
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling(); // 置顶：避免常驻UI（头顶血条等）遮挡
        Refresh();
    }

    private void ClosePanel()
    {
        if (!_panelOpen) return; // 与打开严格一一对应，防重复注销
        _panelOpen = false;
        GameUIState.UnregisterPanel(PanelId);
        _panel.SetActive(false);
    }

    private void OnPresenceChanged(bool inRange)
    {
        if (!inRange) ClosePanel();
    }

    // ===== 面板构建 =====

    private void BuildPanel(Transform canvasRoot)
    {
        // 面板底板（竖版，白盒底色+黑色实线边框）
        _panel = new GameObject("ShopPanel", typeof(Image), typeof(VerticalLayoutGroup));
        UIKit.EnsureLoaded();
        var panelImg = _panel.GetComponent<Image>();
        panelImg.sprite = UIKit.Panel;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = Color.white;
        var prt = _panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(480f, 430f);
        _panel.transform.SetParent(canvasRoot, false);
        var pOutline = _panel.AddComponent<Outline>();
        pOutline.enabled = false;
        pOutline.effectDistance = new Vector2(BorderWidth, BorderWidth);

        var pvlg = _panel.GetComponent<VerticalLayoutGroup>();
        pvlg.padding = new RectOffset(10, 10, 8, 8);
        pvlg.spacing = 8f;
        pvlg.childControlWidth = true;
        pvlg.childControlHeight = true;
        pvlg.childForceExpandHeight = false;
        pvlg.childForceExpandWidth = true;

        // ── 1. 顶部标题栏：实线方框，左「商店」中警告 右「金币 n」 ──
        var titleBar = CreateBorderedBox(_panel.transform, "TitleBar", 44f);
        var titleLE = titleBar.GetComponent<LayoutElement>();
        titleLE.flexibleWidth = 0f;   // 显式归零：标题栏内HLG forceExpand会向父级上报flexible=1，
        titleLE.flexibleHeight = 0f;  // 抢占列表区高度（uGUI嵌套布局实测踩坑，同Tabs行处理）
        var tilg = titleBar.AddComponent<HorizontalLayoutGroup>();
        tilg.padding = new RectOffset(10, 40, 6, 6); // 右侧留40给右上角退出按钮，避免金币文字被盖
        tilg.childControlWidth = true;
        tilg.childControlHeight = true;
        tilg.childForceExpandWidth = true;
        tilg.childForceExpandHeight = true;
        var title = CreateText(titleBar.transform, "商店", 16, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        title.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _warningText = CreateText(titleBar.transform, "", 12, TextAnchor.MiddleCenter, WarnColor); // 警告上移标题栏中部（底部信息条已取消）
        _warningText.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _coinTitleText = CreateText(titleBar.transform, "金币 50", 14, TextAnchor.MiddleRight);
        _coinTitleText.GetComponent<LayoutElement>().flexibleWidth = 1f;

        // ── 2. Tab切换区：两个并排方框 ──
        var tabRow = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        tabRow.transform.SetParent(_panel.transform, false);
        var tle = tabRow.GetComponent<LayoutElement>();
        tle.preferredHeight = 36f;
        tle.flexibleWidth = 0f;
        tle.flexibleHeight = 0f;
        var thlg = tabRow.GetComponent<HorizontalLayoutGroup>();
        thlg.spacing = 8f;
        thlg.childControlWidth = true;
        thlg.childControlHeight = true;
        thlg.childForceExpandWidth = true;
        thlg.childForceExpandHeight = true;

        var buyTab = CreateTabBox(tabRow.transform, "购买");
        _tabBuyImage = buyTab.GetComponent<Image>();
        _tabBuyText = buyTab.GetComponentInChildren<Text>();
        var sellTab = CreateTabBox(tabRow.transform, "出售");
        _tabSellImage = sellTab.GetComponent<Image>();
        _tabSellText = sellTab.GetComponentInChildren<Text>();
        // 点击切页（Tab本身即按钮）
        buyTab.AddComponent<Button>().onClick.AddListener(() => { AudioManager.PlaySfx("SFX_UIClick"); SwitchPage(0); });
        sellTab.AddComponent<Button>().onClick.AddListener(() => { AudioManager.PlaySfx("SFX_UIClick"); SwitchPage(1); });

        // ── 3. 商品列表区：购买页（左分类栏+右列表）/ 出售页（固定显示框+超出滚动） ──
        BuildBuyPage();
        BuildSellPage();

        SwitchPage(0); // 默认购买页（含Tab样式与列表刷新）

        BuildCloseButton(); // 右上角退出按钮（红圆底白叉）
    }

    // ===== 右上角退出按钮 =====

    private static Sprite _closeCircleSprite;

    /// <summary>红色圆形按钮贴图（64px抗锯齿圆，ppu=64/30使sizeDelta=30时1:1清晰），缓存复用</summary>
    private static Sprite CloseCircleSprite()
    {
        if (_closeCircleSprite != null) return _closeCircleSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        float c = (n - 1) * 0.5f;
        float r = c - 1.5f;
        var bg = new Color(0.78f, 0.16f, 0.16f, 1f); // 红色圆底
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Clamp01(r - d + 0.5f); // 边缘1px抗锯齿
                tex.SetPixel(x, y, new Color(bg.r, bg.g, bg.b, a));
            }
        }
        tex.Apply();
        _closeCircleSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 30f);
        return _closeCircleSprite;
    }

    /// <summary>右上角退出按钮：红色圆底+白色叉叉，锚面板右上角（ignoreLayout脱离竖排流式布局）</summary>
    private void BuildCloseButton()
    {
        var btn = new GameObject("Btn_Close", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btn.transform.SetParent(_panel.transform, false);
        var img = btn.GetComponent<Image>();
        UIKit.EnsureLoaded();
        img.sprite = UIKit.CircleBtn;
        img.type = Image.Type.Simple;
        img.color = Color.white;
        var rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-8f, -8f);
        rt.sizeDelta = new Vector2(30f, 30f);
        btn.GetComponent<LayoutElement>().ignoreLayout = true;
        btn.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick");
            ClosePanel();
        });

        MakeXBar(btn.transform, 45f);
        MakeXBar(btn.transform, -45f);
    }

    /// <summary>叉叉短条（白色，±45°，不接收射线）</summary>
    private void MakeXBar(Transform parent, float zAngle)
    {
        var bar = new GameObject("X", typeof(Image));
        bar.transform.SetParent(parent, false);
        var img = bar.GetComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = false;
        var rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(16f, 3f);
        rt.localRotation = Quaternion.Euler(0f, 0f, zAngle);
    }

    /// <summary>购买页：左侧竖排分类选项栏（武器/护甲/消耗品），右侧为选中类别的商品列表</summary>
    private void BuildBuyPage()
    {
        _buyPage = new GameObject("BuyPage", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        _buyPage.transform.SetParent(_panel.transform, false);
        var ple = _buyPage.GetComponent<LayoutElement>();
        ple.flexibleHeight = 1f;
        ple.flexibleWidth = 0f;
        var hlg = _buyPage.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // 左侧分类栏：三个Tab上下排列，均分列高（选中=黑底白字，与页签样式一致）
        var col = new GameObject("CategoryCol", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        col.transform.SetParent(_buyPage.transform, false);
        var cle = col.GetComponent<LayoutElement>();
        cle.preferredWidth = 76f;
        cle.flexibleWidth = 0f;
        cle.flexibleHeight = 1f;
        var cvlg = col.GetComponent<VerticalLayoutGroup>();
        cvlg.spacing = 8f;
        cvlg.childControlWidth = true;
        cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true;
        cvlg.childForceExpandHeight = true;
        for (int i = 0; i < CategoryLabels.Length; i++)
        {
            int idx = i; // 闭包值捕获（for循环变量共享，必须拷贝到局部）
            var tab = CreateTabBox(col.transform, CategoryLabels[i]);
            _catImages[i] = tab.GetComponent<Image>();
            _catTexts[i] = tab.GetComponentInChildren<Text>();
            tab.AddComponent<Button>().onClick.AddListener(() =>
            {
                AudioManager.PlaySfx("SFX_UIClick");
                SwitchCategory(idx);
            });
        }
        ApplyCategoryStyles();

        // 右侧商品列表：当前选中类别的行容器
        var list = new GameObject("BuyList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        list.transform.SetParent(_buyPage.transform, false);
        var lle = list.GetComponent<LayoutElement>();
        lle.flexibleWidth = 1f;
        lle.flexibleHeight = 1f;
        var vlg = list.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true; // 必须由布局接管行高，否则行保持新建默认100高，LayoutElement.preferredHeight=48会被忽略
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        _buyList = list.GetComponent<RectTransform>();
    }

    /// <summary>出售页：固定显示框高度（面板尺寸不随内容变化），行超出时右侧滑动条滚动查看</summary>
    private void BuildSellPage()
    {
        _sellPage = new GameObject("SellPage", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        _sellPage.transform.SetParent(_panel.transform, false);
        var le = _sellPage.GetComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.flexibleWidth = 0f;
        _sellPage.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // 透明底仅承接拖拽/滚轮射线（视觉与原列表页一致）
        _sellScroll = _sellPage.GetComponent<ScrollRect>();
        _sellScroll.horizontal = false;
        _sellScroll.vertical = true;
        _sellScroll.movementType = ScrollRect.MovementType.Clamped;
        _sellScroll.scrollSensitivity = 8f;

        // 视口：RectMask2D软裁剪超出显示框的行
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(_sellPage.transform, false);
        StretchRect(viewport.GetComponent<RectTransform>());

        // 内容：行容器，高度随行数由ContentSizeFitter驱动，ScrollRect据其计算滚动范围
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = new Vector2(0f, 0f);
        crt.offsetMax = new Vector2(-18f, 0f); // 右侧留18给滑动条，行按钮不被盖
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true; // 必须由布局接管行高，否则行保持新建默认100高，LayoutElement.preferredHeight=48会被忽略
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _sellList = crt;
        _sellScroll.content = crt;
        _sellScroll.viewport = viewport.GetComponent<RectTransform>();
        _sellScroll.verticalScrollbar = BuildScrollbar(_sellPage.transform);
    }

    /// <summary>竖向滑动条：白色轨道黑框+深色滑块（白盒风格），锚显示框右缘（滑块长度由ScrollRect按内容占比自动驱动）</summary>
    private Scrollbar BuildScrollbar(Transform parent)
    {
        UIKit.EnsureLoaded();
        var go = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        go.transform.SetParent(parent, false);
        // 轨道：kit 条框（深色内凹），与面板同族，不再是白块
        var trackImg = go.GetComponent<Image>();
        trackImg.sprite = UIKit.BarFrame;
        trackImg.type = Image.Type.Sliced;
        trackImg.color = Color.white;
        var ol = go.AddComponent<Outline>();
        ol.enabled = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(14f, 0f);
        rt.anchoredPosition = new Vector2(-2f, 0f);

        var handle = new GameObject("Sliding Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(go.transform, false);
        handle.GetComponent<Image>().color = LineColor;
        handle.GetComponent<Image>().raycastTarget = false;
        var hrt = handle.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0f, 0f);
        hrt.anchorMax = new Vector2(1f, 1f);
        hrt.sizeDelta = new Vector2(-10f, -6f); // 横向/纵向内缩，露出 kit 轨道边框

        var sb = go.GetComponent<Scrollbar>();
        sb.handleRect = hrt;
        sb.targetGraphic = handle.GetComponent<Image>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.value = 1f;
        return sb;
    }

    /// <summary>创建Tab方框：Image底色+Outline+居中文字（样式由SwitchPage/ApplyTabStyle切换）</summary>
    private GameObject CreateTabBox(Transform parent, string label)
    {
        var go = new GameObject("Tab_" + label, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.enabled = false;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var t = CreateText(go.transform, label, 14, TextAnchor.MiddleCenter, TabSelectedText);
        t.fontStyle = FontStyle.Bold;
        StretchRect(t.rectTransform);
        t.GetComponent<LayoutElement>().ignoreLayout = true;
        return go;
    }


    /// <summary>Tab 样式：选中=kit 按钮框+金字，未选中=透明底+暗字</summary>
    private static void StyleTab(Image img, Text txt, bool sel)
    {
        UIKit.EnsureLoaded();
        // 始终保留 kit 边框（未选中也是暗框），避免 sprite 置空导致 preferredSize 变化挤压布局
        img.sprite = sel ? UIKit.BtnN : UIKit.BtnP;
        img.type = Image.Type.Sliced;
        img.color = sel ? Color.white : new Color(0.72f, 0.70f, 0.66f, 1f);
        if (txt != null) txt.color = sel ? LineColor : TabUnselectedText;
    }

    /// <summary>切换购买/出售页签并应用选中样式（选中=黑底白字，未选中=白底黑字）</summary>
    private void SwitchPage(int page)
    {
        _currentPage = page;
        _buyPage.SetActive(page == 0);
        _sellPage.SetActive(page == 1);
        bool buySel = page == 0;
        StyleTab(_tabBuyImage, _tabBuyText, buySel);
        StyleTab(_tabSellImage, _tabSellText, !buySel);
        Refresh();
    }

    /// <summary>切换购买页左侧分类选项并应用选中样式（重建购买列表）</summary>
    private void SwitchCategory(int cat)
    {
        _buyCategory = Mathf.Clamp(cat, 0, CategoryLabels.Length - 1);
        ApplyCategoryStyles();
        Refresh();
    }

    /// <summary>分类Tab样式：选中=黑底白字，未选中=白底黑字（与购买/出售页签一致）</summary>
    private void ApplyCategoryStyles()
    {
        for (int i = 0; i < _catImages.Length; i++)
        {
            bool sel = i == _buyCategory;
            StyleTab(_catImages[i], _catTexts[i], sel);
        }
    }

    // ===== 刷新 =====

    /// <summary>刷新金币文本、警告与当前页签列表</summary>
    private void Refresh()
    {
        if (_panel == null || !_panel.activeSelf) return;
        if (_wallet == null || _inventory == null || _shop == null || _shop.Data == null) return;

        _coinTitleText.text = "金币 " + _wallet.Coins;
        _warningText.text = _inventory.UsedSlots >= _inventory.MaxSlots ? "背包已满" : "";

        // 重建当前页签的列表
        foreach (var row in _rows)
        {
            Destroy(row);
        }
        _rows.Clear();

        if (_currentPage == 0)
        {
            var entries = _shop.Data.entries;
            var wanted = CategoryTypes[_buyCategory]; // 当前分类栏对应的物品类型
            bool anyEntry = false;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry.item == null || entry.item.type != wanted) continue; // 分类过滤：商品归入对应选项栏
                    anyEntry = true;
                    int idx = i; // 闭包值捕获（for循环变量共享，必须拷贝到局部；保留原始entries索引供ShopSystem.Buy使用）
                    _rows.Add(CreateShopRow(_buyList,
                        entry.item.itemName,
                        AttrText(entry.item),
                        entry.buyPrice + "金币",
                        "购买",
                        () => OnBuyClicked(idx, entry.buyPrice)));
                }
            }
            if (!anyEntry)
            {
                _rows.Add(CreateEmptyRow(_buyList, "（暂无此类商品）"));
            }
        }
        else
        {
            // 出售页：跳过空格，行按钮闭包捕获真实背包格索引（拖拽自由放置后格位不连续）
            var slots = _inventory.Slots;
            bool anyItem = false;
            for (int i = 0; i < slots.Count; i++)
            {
                var stack = slots[i];
                if (stack == null) continue;
                anyItem = true;
                int slotIndex = i;
                _rows.Add(CreateShopRow(_sellList,
                    stack.item.itemName + " x" + stack.count,
                    "出售价 " + stack.item.sellPrice + "/个",
                    stack.item.sellPrice + "金币",
                    "出售",
                    () => OnSellClicked(slotIndex)));
            }
            if (!anyItem)
            {
                _rows.Add(CreateEmptyRow(_sellList, "（背包空无一物）"));
            }
            _sellScroll.verticalNormalizedPosition = 1f; // 重建行后回到列表顶部
        }
    }

    /// <summary>属性说明文本（武器攻/防具防/消耗品回复）</summary>
    private static string AttrText(ItemData item)
    {
        if (item.attackPower > 0) return "攻击 +" + item.attackPower;
        if (item.defensePower > 0) return "防御 +" + item.defensePower;
        if (item.healAmount > 0) return "回复 " + item.healAmount;
        return "材料";
    }

    /// <summary>购买点击：失败时在底部信息条提示原因（金币不足/背包已满）</summary>
    private void OnBuyClicked(int idx0, int price)
    {
        if (_shop.Buy(idx0))
        {
            SetWarning(""); // 成功：Changed事件会触发Refresh并清空警告
        }
        else
        {
            SetWarning(!_wallet.CanAfford(price) ? "金币不足" : "背包已满");
        }
    }

    /// <summary>出售点击：失败（无出售价）时提示</summary>
    private void OnSellClicked(int slotIndex)
    {
        if (!_shop.Sell(slotIndex))
        {
            SetWarning("该物品无法出售");
        }
    }

    private void SetWarning(string message)
    {
        _warningText.text = message;
    }

    /// <summary>空列表占位行（居中文字，无按钮）</summary>
    private GameObject CreateEmptyRow(RectTransform parent, string label)
    {
        var row = CreateBorderedBox(parent, "EmptyRow", RowHeight);
        var t = CreateText(row.transform, label, 13, TextAnchor.MiddleCenter, LineColor);
        StretchRect(t.rectTransform);
        t.GetComponent<LayoutElement>().ignoreLayout = true;
        return row;
    }

    // ===== 行与控件构建工具（白盒线框风格：黑字 + 统一黑色线条 + 无填充色块） =====

    /// <summary>
    /// 商品行：实线方框（等宽48高），内部从左到右——
    /// 虚线图标位(32×32)+名称(14)+属性(12)+价格(12)+实线小按钮(64×30)
    /// </summary>
    private GameObject CreateShopRow(RectTransform parent, string name, string attr, string price, string btnLabel, UnityAction onBtn)
    {
        var row = CreateBorderedBox(parent, "ShopRow", RowHeight);
        var le = row.GetComponent<LayoutElement>();
        le.flexibleWidth = 1f; // 等宽
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 8, 8);
        hlg.spacing = 8f;
        hlg.childControlWidth = true; // 必须由布局接管子项尺寸，否则文本保持新建默认100宽，按钮会被推出行/面板右缘
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        CreateDashedIconBox(row.transform, 32f); // 虚线图标占位

        var nameT = CreateText(row.transform, name, 14, TextAnchor.MiddleLeft, LineColor);
        var nle = nameT.GetComponent<LayoutElement>();
        nle.preferredWidth = 84f;

        var attrT = CreateText(row.transform, attr, 12, TextAnchor.MiddleLeft, LineColor);
        var ale = attrT.GetComponent<LayoutElement>();
        ale.preferredWidth = 100f;
        ale.flexibleWidth = 1f; // 吸收剩余宽度，保证价格/按钮右端对齐

        var priceT = CreateText(row.transform, price, 12, TextAnchor.MiddleRight, LineColor);
        priceT.GetComponent<LayoutElement>().preferredWidth = 66f;

        CreateBorderedButton(row.transform, btnLabel, new Vector2(64f, 30f), onBtn);
        return row;
    }

    /// <summary>图标占位框：kit 槽位（替代原虚线框）</summary>
    private GameObject CreateDashedIconBox(Transform parent, float size)
    {
        UIKit.EnsureLoaded();
        var go = new GameObject("IconBox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = size;
        le.preferredHeight = size;

        var img = go.GetComponent<Image>();
        img.sprite = UIKit.Slot;
        img.type = Image.Type.Sliced;
        img.color = Color.white;
        img.raycastTarget = false;
        return go;
    }

    /// <summary>添加一段虚线（锚定父矩形对应角，pivot=anchor使线段向内排布）</summary>
    private void AddDash(RectTransform parent, Vector2 pos, Vector2 size, Vector2 anchor)
    {
        var seg = new GameObject("Dash", typeof(Image));
        seg.transform.SetParent(parent, false);
        seg.GetComponent<Image>().color = LineColor;
        seg.GetComponent<Image>().raycastTarget = false;
        var srt = seg.GetComponent<RectTransform>();
        srt.anchorMin = anchor;
        srt.anchorMax = anchor;
        srt.pivot = anchor;
        srt.sizeDelta = size;
        srt.anchoredPosition = pos;
    }

    /// <summary>实线小按钮：白底黑字黑框（规格：实线小按钮）</summary>
    private void CreateBorderedButton(Transform parent, string label, Vector2 size, UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement), typeof(Button));
        go.transform.SetParent(parent, false);
        UIKit.EnsureLoaded();
        var bimg = go.GetComponent<Image>();
        bimg.sprite = UIKit.BtnN;          // kit 按钮框
        bimg.type = Image.Type.Sliced;
        bimg.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.enabled = false;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = size.x;
        le.preferredHeight = size.y;
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick"); // UI点击音效
            onClick.Invoke();
        });

        var t = CreateText(go.transform, label, 13, TextAnchor.MiddleCenter, LineColor);
        StretchRect(t.rectTransform);
    }

    /// <summary>实线方框容器：填充与面板同色（视觉无色块）+ 统一粗细黑色边框</summary>
    private GameObject CreateBorderedBox(Transform parent, string boxName, float height)
    {
        var go = new GameObject(boxName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        UIKit.EnsureLoaded();
        var bimg = go.GetComponent<Image>();
        bimg.sprite = UIKit.PanelInner;
        bimg.type = Image.Type.Sliced;
        bimg.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.enabled = false;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        return go;
    }

    private Text CreateText(Transform parent, string label, int size, TextAnchor anchor, Color? color = null)
    {
        var go = new GameObject("Text", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = label;
        t.font = UIKit.LegacyFont;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color ?? LineColor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        go.AddComponent<LayoutElement>();
        return t;
    }

    private void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }

    private Canvas EnsureCanvas()
    {
        // 统一使用GameManager的常驻UI根画布（避免面板挂到会被销毁的传送黑屏/场景画布上）
        if (GameManager.Instance != null && GameManager.Instance.GameCanvas != null)
        {
            return GameManager.Instance.GameCanvas;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
        }
        // UI根画布常驻：跨场景不销毁（商店面板跨场景可用）
        DontDestroyOnLoad(canvas.gameObject);
        return canvas;
    }
}
