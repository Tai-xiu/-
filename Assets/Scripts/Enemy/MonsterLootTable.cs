using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 怪物掉落表SO（配置表v0.1第六节）：挂各怪物（哥布林/骷髅/大型哥布林/魔狼）实例的
/// MonsterAI.lootTable字段；死亡后尸体可搜索，搜索完成时Roll()独立概率掉落
/// 品质组条目（蓝装/紫装）= 从对应品质与类型的物品池随机一件
/// </summary>
public enum LootQualityGroup
{
    None = 0,      // 无组：直接掉entry.item
    BlueWeapon,    // 蓝品质武器（随机一件：铁长剑/战斧/短剑）
    PurpleWeapon,  // 紫品质武器（精钢长剑/符文战斧/疾风短剑）
    BlueArmor,     // 蓝品质护甲（锁子甲/轻甲）
    PurpleArmor    // 紫品质护甲（板甲/重甲）
}

[CreateAssetMenu(menuName = "Roguelite/MonsterLootTable")]
public class MonsterLootTable : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("具体物品（魔石/金币袋等；品质组条目留空）")]
        public ItemData item;

        [Tooltip("品质组（蓝装/紫装=对应品质与类型随机一件）")]
        public LootQualityGroup qualityGroup = LootQualityGroup.None;

        [Tooltip("独立掉落概率（0~1，各条目独立判定）")]
        [Range(0f, 1f)] public float chance;
    }

    public List<Entry> entries = new List<Entry>();

    /// <summary>独立概率逐条目roll，返回掉落结果（物品×数量1）</summary>
    public List<(ItemData item, int count)> Roll()
    {
        var result = new List<(ItemData, int)>();
        foreach (var e in entries)
        {
            if (e.chance <= 0f) continue;
            if (UnityEngine.Random.value > e.chance) continue;
            if (e.qualityGroup == LootQualityGroup.None)
            {
                if (e.item != null) result.Add((e.item, 1));
                continue;
            }
            var pick = PickFromGroup(e.qualityGroup);
            if (pick != null) result.Add((pick, 1));
        }
        return result;
    }

    private static List<ItemData> _pool;
    private static float _poolTime = -999f;

    /// <summary>从物品池按品质+类型随机挑一件（池5秒缓存）</summary>
    private static ItemData PickFromGroup(LootQualityGroup group)
    {
        if (Time.unscaledTime - _poolTime > 5f || _pool == null)
        {
            _pool = new List<ItemData>(Resources.FindObjectsOfTypeAll<ItemData>());
            _pool.RemoveAll(i => i == null || i.type == ItemType.Consumable || i.name.Contains("血瓶"));
            _poolTime = Time.unscaledTime;
        }
        ItemRarity want;
        ItemType wantType;
        switch (group)
        {
            case LootQualityGroup.PurpleWeapon: want = ItemRarity.Purple; wantType = ItemType.Weapon; break;
            case LootQualityGroup.BlueWeapon: want = ItemRarity.BlueWhite; wantType = ItemType.Weapon; break;
            case LootQualityGroup.PurpleArmor: want = ItemRarity.Purple; wantType = ItemType.Armor; break;
            default: want = ItemRarity.BlueWhite; wantType = ItemType.Armor; break;
        }
        var candidates = _pool.FindAll(i => i.rarity == want && i.type == wantType);
        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }
}