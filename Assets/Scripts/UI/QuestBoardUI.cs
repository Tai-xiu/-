using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 悬赏板UI（冒险者协会，白盒线框风格，ShopUI同款480×430）
/// ─────────────────────────────────────────────
/// 布局：固定尺寸面板，自上而下——
/// 1. 顶部标题栏：左「冒险者协会」中警告（背包已满/领取失败原因）右「金币 n」（实时）
/// 2. 契约列表区：全部契约卡各一行（接没接都在板上，"其余契约看悬赏板"），每行——
///    名称(粗)+描述 | 状态/进度 | 奖励 | 动作按钮（未接取=接取 / 进行中=放弃 / 可领奖=领奖 / 已领奖=无）
///    可领奖行状态文字金色高亮；日契刷新入口见每日刷新版（第4步）
/// 数据与表现分离：接取/放弃/领奖走QuestManager公共接口，本类只渲染；
/// QuestManager.OnQuestChanged与Wallet/Inventory.Changed事件驱动刷新；
/// 打开/关闭订阅QuestBoardInteract事件（组件由QuestBoardInteract.Start懒创建，随场景卸载销毁）
/// </summary>
public class QuestBoardUI : MonoBehaviour
{
    private QuestBoardInteract _interact;
    private PlayerWallet _wallet;
    private Inventory _inventory;

    private GameObject _panel;
    private bool _panelOpen; // 面板当前是否打开（与全局面板栈注册严格一一对应）

    /// <summary>本面板在全局UI状态门中的注册id（面板栈唯一标识）</summary>
    private const string PanelId = "QuestBoard";

    // —— 标题栏 ——
    private Text _coinTitleText;   // 标题右侧「金币 n」
    private Text _warningText;     // 标题中部警告（背包已满/领取失败原因）

    // —— 契约列表 ——
    private RectTransform _list;
    private readonly List<GameObject> _rows = new List<GameObject>();

    // ===== 白盒样式常量（与ShopUI/InventoryUI一致） =====
    private static readonly Color PanelBg = new Color(0.94f, 0.94f, 0.94f, 1f);      // 面板底色
    private static readonly Color LineColor = new Color(0.85f, 0.75f, 0.53f, 1f);       // 统一线条/文字黑
    private static readonly Color WarnColor = new Color(0.65f, 0.12f, 0.12f, 1f);    // 警告文字深红
    private static readonly Color GoldColor = new Color(0.85f, 0.65f, 0.2f, 1f);     // 可领奖金色高亮（武器类型色同族）
    private const float BorderWidth = 2f;   // 统一实线边框粗细
    private const float DashSegLen = 8f;    // 虚线段长
    private const float RowHeight = 82f;    // 契约行高（名称+描述两行富文本，4行恰满列表区）

    /// <summary>
    /// 懒创建入口（QuestBoardInteract.Start调用）：已有实例则改绑新交互源（跨场景重建），否则新建
    /// </summary>
    public static QuestBoardUI EnsureInstance(QuestBoardInteract owner)
    {
        var existing = FindObjectOfType<QuestBoardUI>();
        if (existing != null)
        {
            existing.Init(owner);
            return existing;
        }
        var go = new GameObject("QuestBoardUI");
        var ui = go.AddComponent<QuestBoardUI>();
        ui.Init(owner);
        return ui;
    }

    private void Init(QuestBoardInteract owner)
    {
        _interact = owner;
    }

    private void Start()
    {
        if (_interact == null) _interact = FindObjectOfType<QuestBoardInteract>();
        if (_interact == null)
        {
            enabled = false; // 场景中无协会（如Dungeon）：本组件静默停用
            return;
        }

        var playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            _wallet = playerObj.GetComponent<PlayerWallet>();
            _inventory = playerObj.GetComponent<Inventory>();
        }

        EnsureEventSystem();
        Canvas canvas = EnsureCanvas();
        BuildPanel(canvas.transform);

        // 订阅交互与数据事件
        _interact.OpenRequested += OpenPanel;
        _interact.CloseRequested += ClosePanel;
        _interact.PlayerPresenceChanged += OnPresenceChanged;
        QuestManager.OnQuestChanged += Refresh;
        if (_wallet != null) _wallet.Changed += Refresh;
        if (_inventory != null) _inventory.Changed += Refresh;

        _panel.SetActive(false); // 默认收起
    }

    private void OnDestroy()
    {
        if (_interact != null)
        {
            _interact.OpenRequested -= OpenPanel;
            _interact.CloseRequested -= ClosePanel;
            _interact.PlayerPresenceChanged -= OnPresenceChanged;
        }
        QuestManager.OnQuestChanged -= Refresh;
        if (_wallet != null) _wallet.Changed -= Refresh;
        if (_inventory != null) _inventory.Changed -= Refresh;

        // 防计数泄漏：面板开着时组件随场景销毁（传送切换），从全局面板栈注销
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
        _panel.transform.SetAsLastSibling(); // 置顶：避免常驻UI遮挡
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
        // 孤儿清扫：上一场景实例的面板（组件已死但DDOL画布上的节点还在）按名销毁
        var old = canvasRoot.Find("QuestBoardPanel");
        if (old != null) Destroy(old.gameObject);

        // 面板底板（白盒底色+黑色实线边框，ShopUI同款480×430）
        _panel = new GameObject("QuestBoardPanel", typeof(Image), typeof(VerticalLayoutGroup));
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

        // ── 1. 顶部标题栏：左「冒险者协会」中警告 右「金币 n」 ──
        var titleBar = CreateBorderedBox(_panel.transform, "TitleBar", 44f);
        var titleLE = titleBar.GetComponent<LayoutElement>();
        titleLE.flexibleWidth = 0f;   // 显式归零：标题栏内HLG forceExpand会向父级上报flexible=1，
        titleLE.flexibleHeight = 0f;  // 抢占列表区高度（uGUI嵌套布局实测踩坑）
        var tilg = titleBar.AddComponent<HorizontalLayoutGroup>();
        tilg.padding = new RectOffset(10, 40, 6, 6); // 右侧留40给右上角退出按钮
        tilg.childControlWidth = true;
        tilg.childControlHeight = true;
        tilg.childForceExpandWidth = true;
        tilg.childForceExpandHeight = true;
        var title = CreateText(titleBar.transform, "冒险者协会", 16, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        title.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _warningText = CreateText(titleBar.transform, "", 12, TextAnchor.MiddleCenter, WarnColor);
        _warningText.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _coinTitleText = CreateText(titleBar.transform, "金币 0", 14, TextAnchor.MiddleRight);
        _coinTitleText.GetComponent<LayoutElement>().flexibleWidth = 1f;

        // ── 2. 契约列表区：全部契约卡各一行（占满剩余高度） ──
        var list = new GameObject("QuestList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        list.transform.SetParent(_panel.transform, false);
        var lle = list.GetComponent<LayoutElement>();
        lle.flexibleWidth = 0f;
        lle.flexibleHeight = 1f;
        var vlg = list.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.spacing = 8f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;  // 布局接管行高（否则行保持新建默认100高，preferredHeight=82被忽略）
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        _list = list.GetComponent<RectTransform>();

        BuildCloseButton(); // 右上角退出按钮（红圆底白叉，ShopUI同款）
    }

    // ===== 刷新 =====

    /// <summary>刷新金币文本、警告与契约列表（每卡一行，按钮按状态切换）</summary>
    private void Refresh()
    {
        if (_panel == null || !_panel.activeSelf) return;
        if (_wallet == null || QuestManager.Instance == null) return;

        _coinTitleText.text = "金币 " + _wallet.Coins;
        _warningText.text = _inventory != null && _inventory.UsedSlots >= _inventory.MaxSlots ? "背包已满" : "";

        // 重建全部契约行
        foreach (var row in _rows)
        {
            Destroy(row);
        }
        _rows.Clear();

        var cards = QuestManager.Instance.Cards;
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            _rows.Add(CreateQuestRow(_list, card));
        }
        if (cards.Count == 0)
        {
            var row = CreateBorderedBox(_list, "EmptyRow", RowHeight);
            var t = CreateText(row.transform, "（暂无悬赏契约）", 13, TextAnchor.MiddleCenter, LineColor);
            StretchRect(t.rectTransform);
            t.GetComponent<LayoutElement>().ignoreLayout = true;
            _rows.Add(row);
        }
    }

    /// <summary>契约行：实线方框（82高）——名称+描述 | 状态/进度 | 奖励 | 动作按钮</summary>
    private GameObject CreateQuestRow(RectTransform parent, QuestData card)
    {
        var row = CreateBorderedBox(parent, "QuestRow_" + card.questId, RowHeight);
        var le = row.GetComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 8, 8);
        hlg.spacing = 8f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        var state = QuestManager.Instance.GetState(card);

        // 左列：名称(粗)+描述（富文本两行，吸收剩余宽度）
        var mainT = CreateText(row.transform,
            "<b>" + card.questName + "</b>\n<size=11>" + card.desc + "</size>",
            13, TextAnchor.UpperLeft, LineColor);
        mainT.horizontalOverflow = HorizontalWrapMode.Wrap; // 描述过长换行不溢出行
        mainT.verticalOverflow = VerticalWrapMode.Truncate;
        var mle = mainT.GetComponent<LayoutElement>();
        mle.preferredWidth = 150f;
        mle.flexibleWidth = 1f;

        // 中列：状态/进度（可领奖金色高亮）
        string statusText;
        Color statusColor = LineColor;
        switch (state)
        {
            case QuestState.Active:
                statusText = "进度 " + QuestManager.Instance.GetDisplayProgress(card) + "/" + card.targetCount;
                break;
            case QuestState.Completed:
                statusText = "可领奖";
                statusColor = GoldColor;
                break;
            case QuestState.Claimed:
                statusText = card.isDaily ? "日契·可刷新" : "已领奖";
                break;
            default:
                statusText = "未接取";
                break;
        }
        var statusT = CreateText(row.transform, statusText, 12, TextAnchor.MiddleLeft, statusColor);
        statusT.fontStyle = state == QuestState.Completed ? FontStyle.Bold : FontStyle.Normal;
        statusT.GetComponent<LayoutElement>().preferredWidth = 84f;

        // 右列：奖励 + 动作按钮（按状态一卡一钮）
        var rewardT = CreateText(row.transform, card.rewardGold + "金币", 12, TextAnchor.MiddleRight, LineColor);
        rewardT.GetComponent<LayoutElement>().preferredWidth = 64f;

        switch (state)
        {
            case QuestState.NotAccepted:
                CreateBorderedButton(row.transform, "接取", new Vector2(64f, 30f), () => OnAcceptClicked(card));
                break;
            case QuestState.Active:
            case QuestState.Completed: // 已完成未领也允许放弃（拒领重接）
                CreateBorderedButton(row.transform, state == QuestState.Active ? "放弃" : "领奖",
                    new Vector2(64f, 30f),
                    state == QuestState.Active ? (UnityAction)(() => OnAbandonClicked(card))
                                               : (UnityAction)(() => OnClaimClicked(card)));
                break;
            case QuestState.Claimed when card.isDaily: // 日契：花金币手动刷新重接（2026-10-21拍板）
                CreateBorderedButton(row.transform, "刷新10金", new Vector2(76f, 30f), () => OnDailyRefreshClicked(card));
                break;
            default: // 已领奖（非日契）：无动作
                var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
                spacer.transform.SetParent(row.transform, false);
                spacer.GetComponent<LayoutElement>().preferredWidth = 64f;
                spacer.GetComponent<LayoutElement>().preferredHeight = 30f;
                break;
        }
        return row;
    }

    /// <summary>接取点击（状态由QuestManager校验，失败静默——按钮只在合法状态显示）</summary>
    private void OnAcceptClicked(QuestData card)
    {
        QuestManager.Instance.Accept(card);
    }

    /// <summary>放弃点击</summary>
    private void OnAbandonClicked(QuestData card)
    {
        QuestManager.Instance.Abandon(card);
    }

    /// <summary>领奖点击：失败（物品奖励满包）走标题警告</summary>
    private void OnClaimClicked(QuestData card)
    {
        if (!QuestManager.Instance.Claim(card))
        {
            SetWarning("背包已满，无法领取奖励");
        }
    }

    /// <summary>日契刷新点击：失败（金币不足）走标题警告</summary>
    private void OnDailyRefreshClicked(QuestData card)
    {
        if (!QuestManager.Instance.TryDailyRefresh(card))
        {
            SetWarning("金币不足，无法刷新日契（" + QuestManager.DailyRefreshCost + "金币）");
        }
    }

    private void SetWarning(string message)
    {
        _warningText.text = message;
    }

    // ===== 行与控件构建工具（白盒线框风格，ShopUI同款） =====

    /// <summary>实线小按钮：白底黑字黑框（点击=音效+动作）</summary>
    private void CreateBorderedButton(Transform parent, string label, Vector2 size, UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement), typeof(Button));
        go.transform.SetParent(parent, false);
        UIKit.EnsureLoaded();
        var bimg = go.GetComponent<Image>();
        bimg.sprite = UIKit.BtnN;
        bimg.type = Image.Type.Sliced;
        bimg.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.effectColor = LineColor;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = size.x;
        le.preferredHeight = size.y;
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick");
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
        ol.effectColor = LineColor;
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

    // ===== 右上角退出按钮（ShopUI同款红圆白叉） =====

    private static Sprite _closeCircleSprite;

    /// <summary>红色圆形按钮贴图（64px抗锯齿圆，ppu=64/30使sizeDelta=30时1:1清晰），缓存复用</summary>
    private static Sprite CloseCircleSprite()
    {
        if (_closeCircleSprite != null) return _closeCircleSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        float c = (n - 1) * 0.5f;
        float r = c - 1.5f;
        var bg = new Color(0.78f, 0.16f, 0.16f, 1f);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Clamp01(r - d + 0.5f);
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
        // 统一使用GameManager的常驻UI根画布（避免面板挂到会被销毁的传送黑屏画布上）
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
        DontDestroyOnLoad(canvas.gameObject);
        return canvas;
    }
}
