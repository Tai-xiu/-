using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 悬赏契约管理器（DDOL自举，运行时零场景接线）
/// 架构（2026-10-21方案定稿）：
/// - 契约卡=QuestData SO，运行时Resources.LoadAll自Assets/Resources/Quests/
///   （DDOL自举组件无法序列化引用资产，沿用技能卡Resources/Skills/模式）；
/// - 状态机：NotAccepted未接取→Active进行中→Completed已完成(可领奖)→Claimed已领奖；
///   进度与状态经PlayerPrefs跨局持久（键 Quest_&lt;id&gt;_progress / Quest_&lt;id&gt;_state）；
/// - 事件接线（全部一行级）：
///     MonsterAI.OnDeath → NotifyKill（任意方式击杀都计数，即时跨局累计）；
///     PortalTeleportRunner撤离成功 → NotifyEvacuate（Evacuate计数 + Collect本局清单结算入账）；
///     LootItemCube拾取 → NotifyPickup（写入本局收集清单，只记不结算）；
///     PortalTeleportRunner进入迷宫 → OnRunStart（清本局清单，死亡/开局自然作废）；
/// - 击杀类即时计数，收集类"带出迷宫才算"（搜打撤规则：撤离成功才入账）；
/// - 任务栏追踪选卡规则（拍板）：可领奖优先 &gt; 最新接取，其余契约看悬赏板。
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance { get; private set; }

    /// <summary>任意契约状态/进度变化（悬赏板与任务栏UI订阅刷新）</summary>
    public static event Action OnQuestChanged;

    /// <summary>日契刷新价格（金币，2026-10-21拍板：手动刷新10金币）</summary>
    public const int DailyRefreshCost = 10;

    /// <summary>单张契约卡的运行时状态（progress已结算进度；state当前状态）</summary>
    private class Rt
    {
        public QuestState state = QuestState.NotAccepted;
        public int progress;
    }

    private readonly List<QuestData> _cards = new List<QuestData>();               // 全部契约卡（加载序）
    private readonly Dictionary<string, QuestData> _byId = new Dictionary<string, QuestData>(); // id→卡
    private readonly Dictionary<string, Rt> _rt = new Dictionary<string, Rt>();    // id→运行时状态
    private readonly List<string> _acceptSeq = new List<string>();                 // 接取顺序（尾=最新）
    // 本局收集清单（搜打撤：撤离成功才结算入进度；进入迷宫/撤离结算后清空）
    private readonly List<(ItemData item, int count)> _runPickups = new List<(ItemData, int)>();

    /// <summary>全部契约卡（只读，悬赏板列表用）</summary>
    public IReadOnlyList<QuestData> Cards => _cards;

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
        var go = new GameObject("QuestManager");
        go.AddComponent<QuestManager>(); // Awake内DontDestroyOnLoad
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
        LoadCards();
        LoadProgress();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ===== 卡片与进度加载 =====

    /// <summary>加载全部契约卡（Resources/Quests/），重复id/无效配置告警跳过</summary>
    private void LoadCards()
    {
        var loaded = Resources.LoadAll<QuestData>("Quests");
        foreach (var card in loaded)
        {
            if (card == null || string.IsNullOrEmpty(card.questId))
            {
                Debug.LogWarning("[QuestManager] 跳过无效契约卡（questId为空）");
                continue;
            }
            if (_byId.ContainsKey(card.questId))
            {
                Debug.LogWarning("[QuestManager] 契约id重复，跳过：" + card.questId);
                continue;
            }
            if (card.type == QuestType.Kill && card.targetMonster == MonsterType.None)
            {
                Debug.LogWarning("[QuestManager] 击杀契约未配目标怪物，跳过：" + card.questId);
                continue;
            }
            if (card.targetCount <= 0)
            {
                Debug.LogWarning("[QuestManager] 目标数量非法(≤0)，跳过：" + card.questId);
                continue;
            }
            _cards.Add(card);
            _byId.Add(card.questId, card);
        }
        if (_cards.Count == 0)
        {
            Debug.LogWarning("[QuestManager] Resources/Quests/ 下未找到契约卡");
        }
    }

    /// <summary>从PlayerPrefs恢复各卡进度与状态，并按加载序重建接取顺序表</summary>
    private void LoadProgress()
    {
        foreach (var card in _cards)
        {
            var rt = new Rt
            {
                progress = Mathf.Max(0, PlayerPrefs.GetInt(ProgressKey(card), 0)),
                state = IntToState(PlayerPrefs.GetInt(StateKey(card), (int)QuestState.NotAccepted)),
            };
            _rt[card.questId] = rt;
            if (rt.state == QuestState.Active || rt.state == QuestState.Completed)
            {
                _acceptSeq.Add(card.questId); // 跨会话的接取顺序按卡资产序重建（顺序仅影响默认追踪）
            }
        }
    }

    private static QuestState IntToState(int v)
    {
        return v >= (int)QuestState.NotAccepted && v <= (int)QuestState.Claimed
            ? (QuestState)v
            : QuestState.NotAccepted; // 脏数据兜底
    }

    private static string ProgressKey(QuestData card) { return "Quest_" + card.questId + "_progress"; }
    private static string StateKey(QuestData card) { return "Quest_" + card.questId + "_state"; }

    /// <summary>单卡持久化（进度+状态立即落盘，传送/崩溃不丢）</summary>
    private void Save(QuestData card)
    {
        var rt = GetRt(card);
        PlayerPrefs.SetInt(ProgressKey(card), rt.progress);
        PlayerPrefs.SetInt(StateKey(card), (int)rt.state);
        PlayerPrefs.Save();
    }

    private Rt GetRt(QuestData card)
    {
        if (!_rt.TryGetValue(card.questId, out var rt))
        {
            rt = new Rt();
            _rt[card.questId] = rt;
        }
        return rt;
    }

    // ===== 查询（悬赏板/任务栏UI用） =====

    /// <summary>契约当前状态</summary>
    public QuestState GetState(QuestData card) { return GetRt(card).state; }

    /// <summary>契约已结算进度（Collect不含未撤离入账的本局清单）</summary>
    public int GetProgress(QuestData card) { return GetRt(card).progress; }

    /// <summary>
    /// 展示进度：Kill/Evacuate=已结算进度；Collect=已结算+本局已拾未入账部分
    /// （未撤离的收集品带"待撤离结算"语义，死亡即作废）
    /// </summary>
    public int GetDisplayProgress(QuestData card)
    {
        var rt = GetRt(card);
        int shown = rt.progress;
        if (card.type == QuestType.Collect && rt.state == QuestState.Active)
        {
            shown += CountRunPickups(card.targetRarity);
        }
        return Mathf.Min(shown, card.targetCount);
    }

    /// <summary>本局收集清单中匹配品质的件数</summary>
    private int CountRunPickups(ItemRarity rarity)
    {
        int sum = 0;
        for (int i = 0; i < _runPickups.Count; i++)
        {
            if (_runPickups[i].item != null && _runPickups[i].item.rarity == rarity)
            {
                sum += _runPickups[i].count;
            }
        }
        return sum;
    }

    /// <summary>
    /// 任务栏追踪选卡（拍板规则）：可领奖(Completed)优先 &gt; 最新接取(Active)；
    /// 无已接契约返回null（任务栏显示"暂无契约"）
    /// </summary>
    public QuestData GetTrackedQuest()
    {
        foreach (var card in _cards)
        {
            if (GetRt(card).state == QuestState.Completed) return card;
        }
        for (int i = _acceptSeq.Count - 1; i >= 0; i--)
        {
            if (_byId.TryGetValue(_acceptSeq[i], out var card) && GetRt(card).state == QuestState.Active)
            {
                return card;
            }
        }
        return null;
    }

    // ===== 接取/放弃/领奖（悬赏板UI调用） =====

    /// <summary>接取契约：仅未接取可接（多契约可并行进行）；返回false=状态不允许</summary>
    public bool Accept(QuestData card)
    {
        if (card == null) return false;
        var rt = GetRt(card);
        if (rt.state != QuestState.NotAccepted) return false;
        rt.state = QuestState.Active;
        _acceptSeq.Remove(card.questId);
        _acceptSeq.Add(card.questId);
        Save(card);
        RaiseChanged();
        return true;
    }

    /// <summary>放弃契约（进行中/已完成未领均可放弃）：状态回未接取，进度清零重来</summary>
    public bool Abandon(QuestData card)
    {
        if (card == null) return false;
        var rt = GetRt(card);
        if (rt.state != QuestState.Active && rt.state != QuestState.Completed) return false;
        rt.state = QuestState.NotAccepted;
        rt.progress = 0;
        _acceptSeq.Remove(card.questId);
        Save(card);
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// 领奖：已完成(可领奖)→已领奖并发奖励。金币必发；物品奖励需背包有空间，
    /// 满包整体保留可领奖状态（返回false由UI提示），不出现"领了奖背包却放不下"
    /// </summary>
    public bool Claim(QuestData card)
    {
        if (card == null) return false;
        var rt = GetRt(card);
        if (rt.state != QuestState.Completed) return false;

        var player = GameObject.FindWithTag("Player");
        if (player == null) return false;
        var wallet = player.GetComponent<PlayerWallet>();
        if (wallet == null) return false;

        Inventory inventory = null;
        if (card.rewardItem != null)
        {
            inventory = player.GetComponent<Inventory>();
            if (inventory == null || !inventory.HasSpaceFor(card.rewardItem, 1))
            {
                return false; // 物品奖励放不下：保留可领奖
            }
        }

        if (card.rewardGold > 0) wallet.Add(card.rewardGold);
        if (card.rewardItem != null) inventory.AddItem(card.rewardItem, 1);

        rt.state = QuestState.Claimed;
        _acceptSeq.Remove(card.questId);
        Save(card);
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// 日契手动刷新（2026-10-21拍板：10金币）：仅isDaily卡且已领奖可刷；
    /// 扣款成功→状态回未接取+进度清零（可再次接取）；余额不足/状态不符返回false不动任何数据
    /// </summary>
    public bool TryDailyRefresh(QuestData card)
    {
        if (card == null || !card.isDaily) return false;
        var rt = GetRt(card);
        if (rt.state != QuestState.Claimed) return false;

        var player = GameObject.FindWithTag("Player");
        if (player == null) return false;
        var wallet = player.GetComponent<PlayerWallet>();
        if (wallet == null || !wallet.TrySpend(DailyRefreshCost)) return false;

        rt.state = QuestState.NotAccepted;
        rt.progress = 0;
        Save(card);
        RaiseChanged();
        return true;
    }

    // ===== 事件入口（各系统一行级调用；全部静态+空例守卫，时序安全） =====

    /// <summary>击杀通知（MonsterAI.OnDeath调用）：任意方式击杀都计数（拍板口径）</summary>
    public static void NotifyKill(MonsterType type)
    {
        if (Instance == null || type == MonsterType.None) return;
        bool changed = false;
        foreach (var card in Instance._cards)
        {
            var rt = Instance.GetRt(card);
            if (card.type != QuestType.Kill || rt.state != QuestState.Active) continue;
            if (card.targetMonster != type) continue;
            rt.progress = Mathf.Min(rt.progress + 1, card.targetCount);
            if (rt.progress >= card.targetCount) rt.state = QuestState.Completed;
            Instance.Save(card);
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    /// <summary>拾取通知（LootItemCube拾取调用）：写入本局收集清单，只记不结算；
    /// 与LootLog同用countTowardsLootLog门——背包丢弃重拾不重复计数</summary>
    public static void NotifyPickup(ItemData item, int count)
    {
        if (Instance == null || item == null || count <= 0) return;
        Instance._runPickups.Add((item, count));
        RaiseChanged(); // 任务栏展示进度实时+1（标注待撤离结算）
    }

    /// <summary>
    /// 撤离成功（Portal结算面板弹出前调用）：
    /// Evacuate契约计数；Collect契约按品质把本局清单结算入进度；
    /// 结算后清空本局清单（已入账物品作废，下一局从零开始）
    /// </summary>
    public static void NotifyEvacuate()
    {
        if (Instance == null) return;
        foreach (var card in Instance._cards)
        {
            var rt = Instance.GetRt(card);
            if (rt.state != QuestState.Active) continue;
            if (card.type == QuestType.Evacuate)
            {
                rt.progress = Mathf.Min(rt.progress + 1, card.targetCount);
                if (rt.progress >= card.targetCount) rt.state = QuestState.Completed;
                Instance.Save(card);
            }
            else if (card.type == QuestType.Collect)
            {
                int gain = Instance.CountRunPickups(card.targetRarity);
                if (gain > 0)
                {
                    rt.progress = Mathf.Min(rt.progress + gain, card.targetCount);
                    if (rt.progress >= card.targetCount) rt.state = QuestState.Completed;
                    Instance.Save(card);
                }
            }
        }
        Instance._runPickups.Clear(); // 本局清单作废（无论是否入账）
        RaiseChanged();               // 清单清空/进度变化统一触发UI刷新
    }

    /// <summary>进入迷宫/开局（PortalTeleportRunner调用）：清本局收集清单（死亡作废同路径）</summary>
    public static void OnRunStart()
    {
        if (Instance == null || Instance._runPickups.Count == 0) return;
        Instance._runPickups.Clear();
        RaiseChanged();
    }

    /// <summary>广播契约变化（UI刷新）</summary>
    private static void RaiseChanged()
    {
        OnQuestChanged?.Invoke();
    }
}
