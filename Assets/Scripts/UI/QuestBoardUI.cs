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
public partial class QuestBoardUI : MonoBehaviour
{
    private QuestBoardInteract _interact;
    private PlayerWallet _wallet;
    private Inventory _inventory;

    private UINodeReferences _view;
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
        if (_panel != null) Destroy(_panel);
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



    // ===== 刷新 =====

    /// <summary>刷新金币文本、警告与契约列表（每卡一行，按钮按状态切换）</summary>
    private void Refresh()
    {
        if (_panel == null || !_panel.activeSelf) return;
        if (_wallet == null || QuestManager.Instance == null) return;

        _coinTitleText.text = "金币 " + _wallet.Coins;
        _warningText.text = _inventory != null && _inventory.OccupiedCells >= _inventory.Capacity ? "背包已满" : "";

        // 重建全部契约行
        foreach (var row in _rows)
        {
            row.SetActive(false); Destroy(row);
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
            var empty = _view.SpawnRow("Empty",_list);
            var row = empty.gameObject;
            _rows.Add(row);
        }
    }

    /// <summary>契约行：实线方框（82高）——名称+描述 | 状态/进度 | 奖励 | 动作按钮</summary>


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


    /// <summary>实线方框容器：填充与面板同色（视觉无色块）+ 统一粗细黑色边框</summary>




    // ===== 右上角退出按钮（ShopUI同款红圆白叉） =====



    /// <summary>红色圆形按钮贴图（64px抗锯齿圆，ppu=64/30使sizeDelta=30时1:1清晰），缓存复用</summary>


    /// <summary>右上角退出按钮：红色圆底+白色叉叉，锚面板右上角（ignoreLayout脱离竖排流式布局）</summary>


    /// <summary>叉叉短条（白色，±45°，不接收射线）</summary>




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

    private void BuildPanel(Transform canvasRoot)
    {
        _view=UINodeReferences.Spawn("QuestBoardPanel",canvasRoot);_panel=_view.gameObject;
        _coinTitleText=_view.Get<Text>("Coins");_warningText=_view.Get<Text>("Warning");_list=_view.Get<RectTransform>("List");
        _view.Get<Button>("Close").onClick.AddListener(ClosePanel);
        _view.Get<UINodeReferences>("Row").gameObject.SetActive(false);_view.Get<UINodeReferences>("Empty").gameObject.SetActive(false);
    }
    private GameObject CreateQuestRow(RectTransform parent,QuestData card)
    {
        var row=_view.SpawnRow("Row",parent);row.name="QuestRow_"+card.questId;
        var state=QuestManager.Instance.GetState(card);
        row.Get<Text>("Name").text="<b>"+card.questName+"</b>\n<size=11>"+card.desc+"</size>";
        var status=row.Get<Text>("Status");
        status.text=state==QuestState.Active?"进度 "+QuestManager.Instance.GetDisplayProgress(card)+"/"+card.targetCount:state==QuestState.Completed?"可领奖":state==QuestState.Claimed?(card.isDaily?"日契·可刷新":"已领奖"):"未接取";
        if(state==QuestState.Completed)status.color=row.selectedText;
        row.Get<Text>("Reward").text=card.rewardGold+"金币";
        var button=row.Get<Button>("Action");
        bool available=state!=QuestState.Claimed||card.isDaily;
        button.interactable=available;
        var group=button.GetComponent<CanvasGroup>();group.alpha=available?1:0;group.blocksRaycasts=available;group.interactable=available;
        row.Get<Text>("ActionText").text=state==QuestState.NotAccepted?"接取":state==QuestState.Active?"放弃":state==QuestState.Completed?"领奖":"刷新10金";
        button.onClick.AddListener(()=>{AudioManager.PlaySfx("SFX_UIClick");if(state==QuestState.NotAccepted)OnAcceptClicked(card);else if(state==QuestState.Active)OnAbandonClicked(card);else if(state==QuestState.Completed)OnClaimClicked(card);else OnDailyRefreshClicked(card);});
        return row.gameObject;
    }

}
