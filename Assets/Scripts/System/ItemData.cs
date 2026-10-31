using UnityEngine;

/// <summary>物品摆放方向（横放=按定义宽高原样占用，竖放=宽高互换；等比物品两方向等价）</summary>
public enum ItemOrient
{
    Horizontal, // 横放（如铁剑2×1）
    Vertical    // 竖放（如铁剑1×2）
}

/// <summary>物品类型</summary>
public enum ItemType
{
    Weapon,     // 武器：提供攻击力加成
    Armor,      // 防具：提供减伤值
    Consumable, // 消耗品：使用后回复生命
    Material    // 材料：金币等，仅可出售/持有
}

/// <summary>装备槽类型（拖拽装备时的类型校验用：武器只能进武器槽，护甲只能进护甲槽）</summary>
/// <summary>武器子类型（配置表v0.1：长剑/斧/短剑，展示与分类用）</summary>
public enum WeaponType
{
    None,       // 非武器
    LongSword,  // 长剑
    Axe,        // 斧
    ShortSword  // 短剑
}
public enum EquipSlotType
{
    Weapon, // 武器槽
    Armor   // 护甲槽
}

/// <summary>
/// 物品数据定义（ScriptableObject资产）
/// 每种物品一个.asset文件，在Project窗口右键 Create → Roguelite → Item 创建
/// 数值含义随类型不同：武器只看attackPower、防具只看defensePower、消耗品只看healAmount
/// </summary>
[CreateAssetMenu(fileName = "New Item", menuName = "Roguelite/Item")]
public class ItemData : ScriptableObject
{
    [Header("—— 基本信息 ——")]
    [Tooltip("物品ID（预留：存档/数据表主键用）")]
    public string itemId;

    [Tooltip("物品名称")]
    public string itemName = "新物品";

    [Tooltip("物品类型")]
    public ItemType type = ItemType.Material;

    [Tooltip("描述（后续 Tooltip/商店展示用）")]
    [TextArea] public string description;

    [Header("—— 占位尺寸（背包多格系统） ——")]
    [Tooltip("占用格宽（横放时的横向格数；铁剑=2，护甲=2，小件=1）")]
    public int width = 1;

    [Tooltip("占用格高（横放时的纵向格数；铁剑=1，护甲=2，小件=1）")]
    public int height = 1;

    [Header("—— 图标 ——")]
    [Tooltip("物品图标（批次4正式美术用；白盒期留空时背包显示程序化占位图标块）")]
    public Sprite icon;

    [Header("—— 品质 ——")]
    [Tooltip("品质（开箱光柱/掉落方块/背包格背景色）：None=不显示品质色")]
    public ItemRarity rarity = ItemRarity.None;

    [Header("—— 数值 ——")]
    [Tooltip("武器：攻击力加成")]
    public float attackPower;

    [Tooltip("防具：减伤值（受到伤害 = 原伤害 - 减伤值，最低保留1点）")]
    public float defensePower;

    [Tooltip("消耗品：使用后回复的生命值")]
    public float healAmount;

    [Header("—— 配置表v0.1 扩展 ——")]
    [Tooltip("武器子类型（长剑/斧/短剑）")]
    public WeaponType weaponType = WeaponType.None;
    [Tooltip("武器攻速（次/秒）；攻击间隔 = 1/攻速")]
    public float attackSpeed = 1f;
    [Tooltip("武器：每次挥砍的体力消耗")]
    public float staminaCostPerSwing = 12f;
    [Tooltip("护甲重量（移速=基础4.0×max(0.6,1-总重×0.04)）")]
    public float weight;
    [Tooltip("消耗品：使用后回复的体力值")]
    public float healStamina;
    [Tooltip("消耗品：食用硬直时长（秒，期间移速-50%，受击打断）")]
    public float eatDuration = 1.5f;
    [Tooltip("商店购入基准价（出售价=基准的70%~80%，金币袋/宝石类全额出售）")]
    public int price;

    [Header("—— 经济与堆叠 ——")]
    [Tooltip("出售价格（回城变现用，后续步骤）")]
    public int sellPrice;

    [Tooltip("堆叠上限：1表示不可堆叠（多格物品建议保持1）")]
    public int maxStack = 1;
}
