using UnityEngine;

/// <summary>
/// 怪物类型（悬赏击杀判定口径）：与基础怪物配置表四怪一一对应；
/// None=未指定（不参与击杀契约）。场景怪物实例在检视器中赋值。
/// </summary>
public enum MonsterType
{
    None = 0,
    Goblin,      // 哥布林
    Skeleton,    // 骷髅
    Wolf,        // 魔狼
    LargeGoblin, // 大型哥布林（精英）
}

/// <summary>
/// 悬赏契约类型（决定使用哪个目标条件与结算时机）：
/// Kill=击杀目标怪物N只（任意方式击杀都计数，死亡即时跨局累计）；
/// Collect=收集目标品质物品N件（本局拾取只记清单，撤离成功才结算入进度，死亡作废）；
/// Evacuate=完成撤离N次。
/// </summary>
public enum QuestType
{
    Kill = 0,
    Collect,
    Evacuate,
}

/// <summary>契约状态：未接取→进行中→已完成(可领奖)→已领奖（日契刷新后回到未接取）</summary>
public enum QuestState
{
    NotAccepted = 0,
    Active = 1,
    Completed = 2, // 进度达成，可领奖
    Claimed = 3,   // 已领奖（日契可花金币刷新重接，见每日刷新）
}

/// <summary>
/// 悬赏契约数据卡（SO，Project右键 Create→Roguelite/QuestData 创建）；
/// 资产放 Assets/Resources/Quests/——QuestManager为DDOL自举组件无法序列化引用，
/// 运行时Resources.LoadAll加载（项目现役模式，同技能卡Resources/Skills/）。
/// 结构化目标条件：Kill取targetMonster+targetCount / Collect取targetRarity+targetCount /
/// Evacuate取targetCount，其余字段不参与判定。
/// </summary>
[CreateAssetMenu(menuName = "Roguelite/QuestData", fileName = "Quest_")]
public class QuestData : ScriptableObject
{
    [Header("—— 基本信息 ——")]
    [Tooltip("唯一id（PlayerPrefs持久化键名组成部分，创建后勿改）")]
    public string questId;

    [Tooltip("契约显示名")]
    public string questName;

    [TextArea(2, 3)]
    [Tooltip("契约描述（悬赏板与任务栏展示）")]
    public string desc;

    [Tooltip("契约类型（Kill/Collect/Evacuate）")]
    public QuestType type;

    [Tooltip("是否为每日契约（领奖后可花金币手动刷新重接）")]
    public bool isDaily;

    [Header("—— 目标条件（按类型取用） ——")]
    [Tooltip("Kill：目标怪物类型")]
    public MonsterType targetMonster = MonsterType.None;

    [Tooltip("Collect：目标物品品质（任意该品质的拾取物都计数）")]
    public ItemRarity targetRarity = ItemRarity.None;

    [Tooltip("目标数量：Kill/Collect=需要数量；Evacuate=撤离次数")]
    public int targetCount = 1;

    [Header("—— 奖励 ——")]
    [Tooltip("奖励金币")]
    public int rewardGold;

    [Tooltip("奖励物品（可选，可空；满包时保留可领奖状态不发放）")]
    public ItemData rewardItem;
}
