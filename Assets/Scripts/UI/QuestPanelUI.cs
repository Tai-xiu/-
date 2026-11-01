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
public class QuestPanelUI : MonoBehaviour
{
    // 本面板仍是浅色底，用自己的深色文字/线条（不跟随背包的深色 kit 主题）
    private static readonly Color LineColor = new Color(0.85f, 0.75f, 0.53f, 1f);

    public static QuestPanelUI Instance { get; private set; }

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
    private bool BuildPanel()
    {
        Transform canvasRoot = GameManager.Instance != null && GameManager.Instance.GameCanvas != null
            ? GameManager.Instance.GameCanvas.transform
            : null;
        if (canvasRoot == null) return false;

        // 孤儿清扫：重复实例（域重载残留等）的旧面板按名销毁
        var old = canvasRoot.Find("QuestPanel");
        if (old != null) Destroy(old.gameObject);

        // 根面板：白盒底色+黑色实线边框，左侧镜像定位
        _panel = new GameObject("QuestPanel", typeof(Image), typeof(VerticalLayoutGroup));
        UIKit.EnsureLoaded();
        var panelImg = _panel.GetComponent<Image>();
        panelImg.sprite = UIKit.Panel;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = Color.white;
        var prt = _panel.GetComponent<RectTransform>();
        prt.SetParent(canvasRoot, false);
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = new Vector2(OffsetX, 0f);
        prt.sizeDelta = new Vector2(PanelW, PanelH);
        var pOutline = _panel.AddComponent<Outline>();
        pOutline.enabled = false;
        pOutline.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);

        var vlg = _panel.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 10f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        // ── 1. 标题栏（规格同背包/搜索栏标题栏） ──
        var titleBar = CreateBorderedBox("TitleBar");
        var tle = titleBar.GetComponent<LayoutElement>();
        tle.preferredHeight = 40f;
        tle.flexibleWidth = 0f;   // 显式归零：防HLG forceExpand向父级上报flexible=1抢占高度（uGUI嵌套布局老坑）
        tle.flexibleHeight = 0f;
        var titleText = CreateText(titleBar.transform, "任 务", 18, TextAnchor.MiddleCenter);
        titleText.fontStyle = FontStyle.Bold;
        StretchRect(titleText.rectTransform);
        titleText.GetComponent<LayoutElement>().ignoreLayout = true;

        // ── 2. 契约列表区（占满剩余高度；超出滚动，ShopUI出售页同款ScrollRect） ──
        var scrollGo = new GameObject("QuestList", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(_panel.transform, false);
        var sle = scrollGo.GetComponent<LayoutElement>();
        sle.flexibleHeight = 1f;
        sle.flexibleWidth = 0f;
        scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // 透明底仅承接滚轮/拖拽射线
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 8f;

        // 视口：RectMask2D软裁剪超出显示区的条目
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        StretchRect(viewport.GetComponent<RectTransform>());

        // 内容：条目容器，高度随条目数与展开数由ContentSizeFitter驱动
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = new Vector2(0f, 0f);
        crt.offsetMax = new Vector2(-18f, 0f); // 右侧留18给滑动条
        var cvlg = content.GetComponent<VerticalLayoutGroup>();
        cvlg.spacing = 6f;
        cvlg.childControlWidth = true;
        cvlg.childControlHeight = true;  // 布局接管条目高度（折叠/展开高度自适应的关键）
        cvlg.childForceExpandHeight = false;
        cvlg.childForceExpandWidth = true;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = crt;
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.verticalScrollbar = BuildScrollbar(scrollGo.transform);
        _content = crt;

        // ── 3. 底部提示框（固定引导文案） ──
        var hintBox = CreateBorderedBox("HintBox");
        var hle = hintBox.GetComponent<LayoutElement>();
        hle.preferredHeight = 90f;
        hle.flexibleWidth = 0f;
        hle.flexibleHeight = 0f;
        var hvlg = hintBox.AddComponent<VerticalLayoutGroup>();
        hvlg.padding = new RectOffset(10, 10, 8, 8);
        hvlg.childControlWidth = true;
        hvlg.childControlHeight = true;
        hvlg.childForceExpandHeight = false;
        hvlg.childForceExpandWidth = true;
        var hintText = CreateText(hintBox.transform, "接取与领奖请前往\n冒险者协会", 12, TextAnchor.UpperLeft);
        hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        hintText.GetComponent<LayoutElement>().flexibleHeight = 1f;

        return true;
    }

    /// <summary>竖向滑动条：白色轨道黑框+深色滑块（ShopUI同款白盒风格），锚显示区右缘</summary>
    private Scrollbar BuildScrollbar(Transform parent)
    {
        var go = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        go.transform.SetParent(parent, false);
        UIKit.EnsureLoaded();
        var trackImg = go.GetComponent<Image>();
        trackImg.sprite = UIKit.BarFrame;
        trackImg.type = Image.Type.Sliced;
        trackImg.color = Color.white;
        var ol = go.AddComponent<Outline>();
        ol.effectColor = LineColor;
        ol.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);
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
        hrt.sizeDelta = new Vector2(-8f, 0f);

        var sb = go.GetComponent<Scrollbar>();
        sb.handleRect = hrt;
        sb.targetGraphic = handle.GetComponent<Image>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.value = 1f;
        return sb;
    }

    // ===== 刷新（OnQuestChanged驱动 + 开包瞬间） =====

    /// <summary>重建契约条目：全部已接契约（可领奖排最前），每条=可点击表头+可展开详情组</summary>
    private void Refresh()
    {
        if (_panel == null || !_panel.activeSelf || QuestManager.Instance == null || _content == null) return;

        foreach (var entry in _entries)
        {
            Destroy(entry);
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
            // 空占位挂滚动内容容器下（CreateBorderedBox固定挂面板根，不能用——否则占位框脱离列表区）
            var empty = new GameObject("EmptyRow", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
            empty.transform.SetParent(_content, false);
            empty.AddComponent<Image>();
            var eimg = empty.GetComponent<Image>();
            eimg.sprite = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/PixelKit/panel_inner");
            eimg.type = Image.Type.Sliced;
            eimg.color = Color.white;
            var eol = empty.GetComponent<Outline>();
            eol.effectColor = LineColor;
            eol.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);
            var ele = empty.GetComponent<LayoutElement>();
            ele.preferredHeight = 64f;
            ele.flexibleWidth = 0f;
            ele.flexibleHeight = 0f;
            var t = CreateText(empty.transform, "暂无契约\n契约列表见冒险者协会悬赏板。", 12, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            StretchRect(t.rectTransform);
            t.GetComponent<LayoutElement>().ignoreLayout = true;
            _entries.Add(empty);
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
    private GameObject CreateQuestEntry(QuestData card)
    {
        var state = QuestManager.Instance.GetState(card);
        bool claimable = state == QuestState.Completed;
        Color nameColor = claimable ? GoldColor : LineColor;

        // 条目根（无固定高度：VerticalLayoutGroup作为ILayoutElement向父级上报自适应首选高度）
        var entry = new GameObject("QuestEntry_" + card.questId, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup));
        entry.transform.SetParent(_content, false);
        var entryImg = entry.GetComponent<Image>();
        entryImg.sprite = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/PixelKit/panel_inner");
        entryImg.type = Image.Type.Sliced;
        entryImg.color = Color.white;
        var ol = entry.GetComponent<Outline>();
        ol.effectColor = claimable ? GoldColor : LineColor; // 可领奖整框金色
        ol.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);
        var evlg = entry.GetComponent<VerticalLayoutGroup>();
        evlg.padding = new RectOffset(8, 8, 6, 6);
        evlg.spacing = 4f;
        evlg.childControlWidth = true;
        evlg.childControlHeight = true;
        evlg.childForceExpandHeight = false;
        evlg.childForceExpandWidth = true;

        // —— 表头（可点击折叠开关） ——
        var header = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        header.transform.SetParent(entry.transform, false);
        var headImg = header.GetComponent<Image>();
        headImg.sprite = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/PixelKit/panel_inner");
        headImg.type = Image.Type.Sliced;
        headImg.color = Color.white;
        var hle = header.GetComponent<LayoutElement>();
        hle.preferredHeight = 26f;
        hle.flexibleWidth = 1f;
        hle.flexibleHeight = 0f;
        var hhlg = header.GetComponent<HorizontalLayoutGroup>();
        hhlg.spacing = 6f;
        hhlg.childControlWidth = true;
        hhlg.childControlHeight = true;
        hhlg.childForceExpandWidth = false;
        hhlg.childForceExpandHeight = false;

        var nameT = CreateText(header.transform, card.questName, 13, TextAnchor.MiddleLeft, nameColor);
        nameT.fontStyle = FontStyle.Bold;
        nameT.GetComponent<LayoutElement>().flexibleWidth = 1f;

        string compact = claimable ? "可领奖" : QuestManager.Instance.GetDisplayProgress(card) + "/" + card.targetCount;
        var compactT = CreateText(header.transform, compact, 12, TextAnchor.MiddleRight, claimable ? GoldColor : LineColor);
        compactT.GetComponent<LayoutElement>().preferredWidth = 56f;

        bool expanded;
        _expanded.TryGetValue(card.questId, out expanded);
        var markT = CreateText(header.transform, expanded ? "−" : "+", 13, TextAnchor.MiddleRight, LineColor);
        markT.GetComponent<LayoutElement>().preferredWidth = 16f;

        // —— 详情组（SetActive折叠/展开；高度由布局自适应，无需手算） ——
        var info = new GameObject("InfoGroup", typeof(RectTransform), typeof(VerticalLayoutGroup));
        info.transform.SetParent(entry.transform, false);
        var ivlg = info.GetComponent<VerticalLayoutGroup>();
        ivlg.padding = new RectOffset(12, 0, 0, 0); // 详情相对表头缩进
        ivlg.spacing = 3f;
        ivlg.childControlWidth = true;
        ivlg.childControlHeight = true;
        ivlg.childForceExpandHeight = false;
        ivlg.childForceExpandWidth = true;

        var goalT = CreateText(info.transform, GoalText(card), 12, TextAnchor.UpperLeft);
        var progT = CreateText(info.transform, ProgressText(card, state), 12, TextAnchor.UpperLeft);
        var rewardT = CreateText(info.transform, "奖励：" + card.rewardGold + "金币" + (card.rewardItem != null ? "+" + card.rewardItem.itemName : ""), 12, TextAnchor.UpperLeft);
        var statusT = CreateText(info.transform, claimable ? "可领奖（前往协会）" : "进行中", 12, TextAnchor.UpperLeft, claimable ? GoldColor : LineColor);
        statusT.fontStyle = claimable ? FontStyle.Bold : FontStyle.Normal;
        var descT = CreateText(info.transform, card.desc, 11, TextAnchor.UpperLeft);
        descT.horizontalOverflow = HorizontalWrapMode.Wrap;
        info.SetActive(expanded); // 折叠状态按记忆恢复

        // 点击表头=切换展开（多条互不影响；状态按questId记忆跨刷新保持）
        var questId = card.questId;
        header.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick");
            bool now;
            _expanded.TryGetValue(questId, out now);
            now = !now;
            _expanded[questId] = now;
            info.SetActive(now);
            markT.text = now ? "−" : "+";
        });

        return entry;
    }

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
    private GameObject CreateBorderedBox(string boxName)
    {
        var go = new GameObject(boxName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(_panel.transform, false);
        UIKit.EnsureLoaded();
        var bimg = go.GetComponent<Image>();
        bimg.sprite = UIKit.PanelInner;
        bimg.type = Image.Type.Sliced;
        bimg.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.effectColor = LineColor;
        ol.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);
        return go;
    }

    private Text CreateText(Transform parent, string label, int size, TextAnchor anchor)
    {
        var go = new GameObject("Text", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = label;
        t.font = UIKit.LegacyFont;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = LineColor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        go.AddComponent<LayoutElement>();
        return t;
    }

    private Text CreateText(Transform parent, string label, int size, TextAnchor anchor, Color color)
    {
        var t = CreateText(parent, label, size, anchor);
        t.color = color;
        return t;
    }

    private static void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
