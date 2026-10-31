using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>战利品条目：物品 + 权重 + 掉落数量范围</summary>
[Serializable]
public class LootEntry
{
    public ItemData item;

    [Tooltip("权重：数值越大越容易抽出（所有条目权重相加后按比例抽取）")]
    public int weight = 10;

    [Tooltip("该条目一次掉落的最小数量")]
    public int minCount = 1;

    [Tooltip("该条目一次掉落的最大数量")]
    public int maxCount = 1;
}

/// <summary>
/// 战利品表（ScriptableObject资产）
/// Roll按权重随机抽取rollsMin~rollsMax件（每次独立加权抽取，可能重复抽到同一条目）
/// 示例：金币堆(weight40, 数量10~30)、血瓶(25)、铁剑(20)、皮甲(15)
/// </summary>
[CreateAssetMenu(fileName = "New LootTable", menuName = "Roguelite/LootTable")]
public class LootTable : ScriptableObject
{
    [Tooltip("掉落条目")]
    public List<LootEntry> entries = new List<LootEntry>();

    [Tooltip("每次开启抽取的最小件数")]
    public int rollsMin = 1;

    [Tooltip("每次开启抽取的最大件数")]
    public int rollsMax = 2;

    /// <summary>
    /// 按权重随机抽取：返回 (物品, 数量) 列表，长度在 rollsMin~rollsMax 之间
    /// </summary>
    public List<(ItemData item, int count)> Roll()
    {
        var results = new List<(ItemData item, int count)>();
        if (entries == null || entries.Count == 0) return results;

        int rolls = UnityEngine.Random.Range(rollsMin, rollsMax + 1);
        for (int r = 0; r < rolls; r++)
        {
            // 权重总和
            int total = 0;
            foreach (var e in entries) total += Mathf.Max(0, e.weight);
            if (total <= 0) break;

            // 加权随机：命中区间落在哪个条目即抽中哪个
            int roll = UnityEngine.Random.Range(1, total + 1);
            foreach (var e in entries)
            {
                roll -= Mathf.Max(0, e.weight);
                if (roll <= 0)
                {
                    int count = UnityEngine.Random.Range(e.minCount, e.maxCount + 1);
                    results.Add((e.item, count));
                    break;
                }
            }
        }
        return results;
    }
}
