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
public partial class ShopUI : MonoBehaviour
{
    private ShopSystem _shop;
    private PlayerWallet _wallet;
    private Inventory _inventory;

    private UINodeReferences _view;
    private GameObject _panel;
    private bool _panelOpen; // 面板当前是否打开（与全局面板栈注册严格一一对应）

    /// <summary>本面板在全局UI状态门中的注册id（面板栈唯一标识）</summary>
    private const string PanelId = "Shop";

    // —— 标题栏 ——
    private Text _coinTitleText;   // 标题右侧「金币 n」
    private Text _warningText;     // 标题中部警告（背包已满/金币不足，原底部信息条上移）

    // —— 购买页分类选项栏（左侧竖排：武器/护甲/消耗品/背包） ——
    private readonly Image[] _catImages = new Image[CategoryTypes.Length];
    private readonly Text[] _catTexts = new Text[CategoryTypes.Length];
    private int _buyCategory; // 0=武器 1=护甲 2=消耗品 3=背包

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
    private static readonly ItemType[] CategoryTypes = { ItemType.Weapon, ItemType.Armor, ItemType.Consumable, ItemType.Backpack };
    private static readonly string[] CategoryLabels = { "武器", "护甲", "消耗品", "背包" };

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
        if (_panel != null) Destroy(_panel);
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



    // ===== 右上角退出按钮 =====



    /// <summary>红色圆形按钮贴图（64px抗锯齿圆，ppu=64/30使sizeDelta=30时1:1清晰），缓存复用</summary>


    /// <summary>右上角退出按钮：红色圆底+白色叉叉，锚面板右上角（ignoreLayout脱离竖排流式布局）</summary>


    /// <summary>叉叉短条（白色，±45°，不接收射线）</summary>


    /// <summary>购买页：左侧竖排分类选项栏（武器/护甲/消耗品），右侧为选中类别的商品列表</summary>


    /// <summary>出售页：固定显示框高度（面板尺寸不随内容变化），行超出时右侧滑动条滚动查看</summary>


    /// <summary>竖向滑动条：白色轨道黑框+深色滑块（白盒风格），锚显示框右缘（滑块长度由ScrollRect按内容占比自动驱动）</summary>


    /// <summary>创建Tab方框：Image底色+Outline+居中文字（样式由SwitchPage/ApplyTabStyle切换）</summary>



    /// <summary>Tab 样式：选中=kit 按钮框+金字，未选中=透明底+暗字</summary>
    private void StyleTab(Image img,Text txt,bool selected)
    {
        img.sprite=selected?_view.selectedSprite:_view.inactiveSprite;
        img.type=Image.Type.Sliced;
        img.pixelsPerUnitMultiplier=2f;   // 页签高度小，缩小九宫格边框避免边框吃掉内容/看起来被拉伸
        img.color=selected?_view.selectedTint:_view.inactiveTint;
        if(txt!=null)txt.color=selected?_view.selectedText:_view.inactiveText;
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
        _warningText.text = _inventory.OccupiedCells >= _inventory.Capacity ? "背包已满" : "";

        // 重建当前页签的列表
        foreach (var row in _rows)
        {
            row.SetActive(false); Destroy(row);
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

    /// <summary>属性说明文本（武器攻/防具防/消耗品回复/背包扩容）</summary>
    private static string AttrText(ItemData item)
    {
        if (item.type == ItemType.Backpack) return "物品栏 +" + item.capacityBonus + "格";
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


    // ===== 行与控件构建工具（白盒线框风格：黑字 + 统一黑色线条 + 无填充色块） =====

    /// <summary>
    /// 商品行：实线方框（等宽48高），内部从左到右——
    /// 虚线图标位(32×32)+名称(14)+属性(12)+价格(12)+实线小按钮(64×30)
    /// </summary>


    /// <summary>图标占位框：kit 槽位（替代原虚线框）</summary>


    /// <summary>添加一段虚线（锚定父矩形对应角，pivot=anchor使线段向内排布）</summary>


    /// <summary>实线小按钮：白底黑字黑框（规格：实线小按钮）</summary>


    /// <summary>实线方框容器：填充与面板同色（视觉无色块）+ 统一粗细黑色边框</summary>






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

    private void BuildPanel(Transform canvasRoot)
    {
        _view=UINodeReferences.Spawn("ShopPanel",canvasRoot);_panel=_view.gameObject;
        _coinTitleText=_view.Get<Text>("Coins");_warningText=_view.Get<Text>("Warning");
        var buy=_view.Get<Button>("BuyTab");var sell=_view.Get<Button>("SellTab");
        _tabBuyImage=buy.GetComponent<Image>();_tabSellImage=sell.GetComponent<Image>();
        _tabBuyText=_view.Get<Text>("BuyTabText");_tabSellText=_view.Get<Text>("SellTabText");
        _buyPage=_view.Get<GameObject>("BuyPage");_sellPage=_view.Get<GameObject>("SellPage");
        _buyList=_view.Get<RectTransform>("BuyList");_sellList=_view.Get<RectTransform>("SellList");_sellScroll=_view.Get<ScrollRect>("SellScroll");
        buy.onClick.AddListener(()=>SwitchPage(0));sell.onClick.AddListener(()=>SwitchPage(1));
        _view.Get<Button>("Close").onClick.AddListener(ClosePanel);
        for(int i=0;i<CategoryTypes.Length;i++){int index=i;var b=_view.Get<Button>("Category"+i);_catImages[i]=b.GetComponent<Image>();_catTexts[i]=_view.Get<Text>("CategoryText"+i);b.onClick.AddListener(()=>SwitchCategory(index));}
        _view.Get<UINodeReferences>("Row").gameObject.SetActive(false);_view.Get<UINodeReferences>("Empty").gameObject.SetActive(false);
        SwitchPage(0);ApplyCategoryStyles();
    }
    private GameObject CreateShopRow(RectTransform parent,string name,string attr,string price,string btnLabel,UnityAction onBtn)
    {
        var row=_view.SpawnRow("Row",parent);
        row.Get<Text>("Name").text=name;row.Get<Text>("Attribute").text=attr;row.Get<Text>("Price").text=price;
        row.Get<Text>("ActionText").text=btnLabel;row.Get<Button>("Action").onClick.AddListener(()=>{AudioManager.PlaySfx("SFX_UIClick");onBtn();});
        return row.gameObject;
    }
    private GameObject CreateEmptyRow(RectTransform parent,string label)
    {var row=_view.SpawnRow("Empty",parent);row.Get<Text>("Text").text=label;return row.gameObject;}

}
