using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 背包左侧任务栏（悬赏契约追踪面板，白盒线框风格）
/// 附属面板范式与SearchPanelUI完全同款（2026-10-21修订：取代顶部HUD追踪条）：
/// - 几何：背包左缘(-330)+12px间隙的左侧镜像竖条（x=-(330+12+半宽)），与右侧搜索栏完全对称，
///   同高544，开包时左任务/右搜索成一整体；与搜索栏几何无重叠
/// - 显隐：Update逐帧轮询InventoryUI.IsOpen——开包出现、关包隐藏（Esc/Tab/关闭按钮全路径天然联动）；
///   不注册GameUIState面板栈（附属面板语义，Esc由背包closeAction带动）
/// - 内容（2026-10-21用户指令，取代此前"只显示1条"拍板）：列出全部已接契约（进行中+可领奖），
///   每条=可点击表头（契约名+紧凑进度+展开标记），点击展开详情（目标/进度/奖励/状态/描述），
///   再点收起；多条可同时展开，展开状态跨刷新保持（按questId记忆）；
///   可领奖条目整框金色高亮并排在最前；超出显示区滚动查看（ShopUI出售页同款ScrollRect）
/// - 数据流：只读QuestManager查询接口；QuestManager.OnQuestChanged事件驱动刷新
///   （纯展示面板，接取/放弃/领奖仍在悬赏板）；无契约显示"暂无契约"
/// - 自举DDOL单例（InteractPromptUI同模式），场景切换无孤儿（面板挂常驻GameCanvas，组件DDOL）
/// </summary>
public partial class QuestPanelUI : MonoBehaviour
{
    // 本面板仍是浅色底，用自己的深色文字/线条（不跟随背包的深色 kit 主题）
    private static readonly Color LineColor = new Color(0.85f, 0.75f, 0.53f, 1f);

    public static QuestPanelUI Instance { get; private set; }

    private UINodeReferences _view;
    private GameObject _panel;
    private InventoryUI _invUI;

    // —— 列表区引用 ——
    private RectTransform _content;                      // 条目容器（ContentSizeFitter驱动高度）
    private readonly List<GameObject> _entries = new List<GameObject>();
    private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(); // questId→是否展开（跨刷新记忆）

    private static readonly Color GoldColor = new Color(0.85f, 0.65f, 0.2f, 1f); // 可领奖金（武器类型色同族）

    // ===== 几何常量（SearchPanelUI镜像） =====
    private const float PanelW = 220f;  // 信息列宽（比搜索栏258略窄：文本列无需格子区）
    private const float PanelH = 544f;  // 与背包/搜索栏同高（垂直对齐成一整体）
    private const float OffsetX = -(330f + 12f + PanelW * 0.5f); // 背包左缘(-330)+12px间隙

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= BootstrapOnSceneLoaded;
        SceneManager.sceneLoaded += BootstrapOnSceneLoaded;
        BootstrapOnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void BootstrapOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "UI" || Instance != null) return;
        var go = new GameObject("QuestPanelUI");
        go.AddComponent<QuestPanelUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        QuestManager.OnQuestChanged += Refresh; // 契约变化即刷新（事件驱动，不逐帧SetText脏化画布）
    }

    private void OnDestroy()
    {
        if (_panel != null) Destroy(_panel);
        QuestManager.OnQuestChanged -= Refresh;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (PauseUI.IsPaused) return; // 暂停期间不动（SearchPanelUI同语义）

        // 背包引用懒查找（场景切换后旧InventoryUI随玩家销毁，逐帧补取防跨场景失效）
        if (_invUI == null) _invUI = FindObjectOfType<InventoryUI>();
        bool invOpen = _invUI != null && _invUI.IsOpen;

        if (!invOpen)
        {
            if (_panel != null && _panel.activeSelf) _panel.SetActive(false);
            return;
        }

        // 新建即刷：BuildPanel产出的面板天生active，若只靠下方激活分支补刷，
        // 首次开包（新建路径）永远跳过Refresh→任务栏空白直到下一次契约事件（实测踩坑）
        if (_panel == null)
        {
            if (!BuildPanel()) return;
            Refresh();
        }
        if (!_panel.activeSelf)
        {
            _panel.SetActive(true);
            Refresh();
        }
    }

    // ===== 面板构建 =====

    /// <summary>构建任务栏（GameCanvas下，anchoredPosition=(OffsetX,0)）；画布未就绪返回false下帧重试</summary>


    /// <summary>竖向滑动条：白色轨道黑框+深色滑块（ShopUI同款白盒风格），锚显示区右缘</summary>


    // ===== 刷新（OnQuestChanged驱动 + 开包瞬间） =====

    /// <summary>重建契约条目：全部已接契约（可领奖排最前），每条=可点击表头+可展开详情组</summary>
    private void Refresh()
    {
        if (_panel == null || !_panel.activeSelf || QuestManager.Instance == null || _content == null) return;

        foreach (var entry in _entries)
        {
            entry.SetActive(false); Destroy(entry);
        }
        _entries.Clear();

        // 收集已接契约：进行中 + 可领奖（已领奖/未接取不进任务栏），可领奖排最前
        var accepted = new List<QuestData>();
        var claimable = new List<QuestData>();
        foreach (var card in QuestManager.Instance.Cards)
        {
            var state = QuestManager.Instance.GetState(card);
            if (state == QuestState.Completed) claimable.Add(card);
            else if (state == QuestState.Active) accepted.Add(card);
        }

        if (accepted.Count == 0 && claimable.Count == 0)
        {
            _entries.Add(_view.SpawnRow("Empty",_content).gameObject);
            return;
        }

        foreach (var card in claimable) _entries.Add(CreateQuestEntry(card));
        foreach (var card in accepted) _entries.Add(CreateQuestEntry(card));
    }

    /// <summary>
    /// 单条契约条目（高度由布局自适应：折叠=表头一行，展开=表头+详情组）：
    /// 表头=契约名+紧凑进度+展开标记（整行可点击，点击展开/收起，多条互不影响）；
    /// 详情组=目标/进度/奖励/状态/描述；可领奖条目=名字金色+整框金色边框+状态行金色
    /// </summary>


    /// <summary>目标行文案（按契约类型生成口径）</summary>
    private static string GoalText(QuestData card)
    {
        switch (card.type)
        {
            case QuestType.Kill:
                return "目标：击杀" + MonsterLabel(card.targetMonster) + " ×" + card.targetCount;
            case QuestType.Collect:
                return "目标：收集" + ItemRarityStyle.GetName(card.targetRarity) + "物品 ×" + card.targetCount;
            default:
                return "目标：完成撤离 ×" + card.targetCount;
        }
    }

    /// <summary>进度行文案（Collect进行中且已有未入账拾取时提示"带出迷宫才算"）</summary>
    private string ProgressText(QuestData card, QuestState state)
    {
        int shown = QuestManager.Instance.GetDisplayProgress(card);
        if (state == QuestState.Active)
        {
            bool unsettled = card.type == QuestType.Collect
                && shown > QuestManager.Instance.GetProgress(card);
            return "进度 " + shown + "/" + card.targetCount + (unsettled ? "（未撤离）" : "");
        }
        return "进度 " + shown + "/" + card.targetCount;
    }

    /// <summary>怪物类型中文名（目标行文案用）</summary>
    private static string MonsterLabel(MonsterType type)
    {
        switch (type)
        {
            case MonsterType.Goblin: return "哥布林";
            case MonsterType.Skeleton: return "骷髅";
            case MonsterType.Wolf: return "魔狼";
            case MonsterType.LargeGoblin: return "大型哥布林";
            default: return type.ToString();
        }
    }

    // ===== 白盒样式构建工具（SearchPanelUI同款，复用InventoryUI公共常量） =====

    /// <summary>实线边框容器：填充与面板同色（视觉无色块）+ 统一粗细黑色边框</summary>








    private bool BuildPanel()
    {
        if(GameManager.Instance==null||GameManager.Instance.GameCanvas==null)return false;
        _view=UINodeReferences.Spawn("QuestPanel",GameManager.Instance.GameCanvas.transform);_panel=_view.gameObject;
        _content=_view.Get<RectTransform>("Content");_view.Get<UINodeReferences>("Row").gameObject.SetActive(false);_view.Get<UINodeReferences>("Empty").gameObject.SetActive(false);return true;
    }
    private GameObject CreateQuestEntry(QuestData card)
    {
        var row=_view.SpawnRow("Row",_content);row.name="QuestEntry_"+card.questId;
        var state=QuestManager.Instance.GetState(card);bool claimable=state==QuestState.Completed;
        row.Get<Text>("Name").text=card.questName;row.Get<Text>("Compact").text=claimable?"可领奖":QuestManager.Instance.GetDisplayProgress(card)+"/"+card.targetCount;
        row.Get<Text>("Goal").text=GoalText(card);row.Get<Text>("Progress").text=ProgressText(card,state);
        row.Get<Text>("Reward").text="奖励："+card.rewardGold+"金币"+(card.rewardItem!=null?"+"+card.rewardItem.itemName:"");
        row.Get<Text>("Status").text=claimable?"可领奖（前往协会）":"进行中";row.Get<Text>("Description").text=card.desc;
        if(claimable){row.Get<Text>("Name").color=row.selectedText;row.Get<Text>("Compact").color=row.selectedText;row.Get<Text>("Status").color=row.selectedText;var outline=row.GetComponent<Outline>();if(outline!=null)outline.effectColor=row.selectedText;}
        bool expanded;_expanded.TryGetValue(card.questId,out expanded);var info=row.Get<GameObject>("Info");var mark=row.Get<Text>("Mark");info.SetActive(expanded);mark.text=expanded?"−":"+";
        row.Get<Button>("Header").onClick.AddListener(()=>{AudioManager.PlaySfx("SFX_UIClick");bool now;_expanded.TryGetValue(card.questId,out now);_expanded[card.questId]=!now;info.SetActive(!now);mark.text=!now?"−":"+";});return row.gameObject;
    }

}
