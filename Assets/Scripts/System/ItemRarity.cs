using UnityEngine;

/// <summary>
/// 物品品质（开箱战利品稀有度）
/// None=旧物品/普通掉落不显示品质色；其余由低到高：白蓝→紫蓝→紫
/// 光柱/掉落方块/背包格背景共用 ItemRarityStyle 的一套颜色
/// </summary>
public enum ItemRarity
{
    None = 0,    // 无品质（不染色）
    BlueWhite,   // 白蓝（低）
    VioletBlue,  // 紫蓝（中）
    Purple,      // 紫（高）
    Gold,        // 金（×1.60，掉落权重8%）
    Red          // 红（×2.00，掉落权重2%）
}

/// <summary>品质样式表：3D光柱/掉落方块染色与背包格背景淡染共用</summary>
public static class ItemRarityStyle
{
    /// <summary>品质主色（3D光柱/掉落方块用饱和版）</summary>
    public static Color GetColor(ItemRarity r)
    {
        switch (r)
        {
            case ItemRarity.BlueWhite: return new Color(0.60f, 0.85f, 1.00f);  // 白蓝：淡青蓝
            case ItemRarity.VioletBlue: return new Color(0.50f, 0.45f, 0.98f); // 紫蓝：蓝紫
            case ItemRarity.Purple: return new Color(0.80f, 0.30f, 0.95f);     // 紫：亮紫
            case ItemRarity.Gold: return new Color(1.00f, 0.84f, 0.00f);      // 金：亮金
            case ItemRarity.Red: return new Color(0.95f, 0.20f, 0.20f);      // 红：赤红
            default: return Color.white;
        }
    }

    /// <summary>品质中文名（提示文字/详情面板用）</summary>
    public static string GetName(ItemRarity r)
    {
        switch (r)
        {
            case ItemRarity.BlueWhite: return "白蓝";
            case ItemRarity.VioletBlue: return "紫蓝";
            case ItemRarity.Purple: return "紫";
            case ItemRarity.Gold: return "金";
            case ItemRarity.Red: return "红";
            default: return "";
        }
    }
    /// <summary>品质数值倍率（配置表v0.1：蓝1.0/紫1.25/金1.6/红2.0，作用于武器伤害与护甲防护）</summary>
    public static float GetMultiplier(ItemRarity r)
    {
        switch (r)
        {
            case ItemRarity.BlueWhite: return 1.00f;
            case ItemRarity.VioletBlue: return 1.10f;
            case ItemRarity.Purple: return 1.25f;
            case ItemRarity.Gold: return 1.60f;
            case ItemRarity.Red: return 2.00f;
            default: return 1f;
        }
    }

    /// <summary>品质掉落权重%（配置表v0.1：蓝60/紫30/金8/红2）</summary>
    public static float GetDropWeight(ItemRarity r)
    {
        switch (r)
        {
            case ItemRarity.BlueWhite: return 60f;
            case ItemRarity.Purple: return 30f;
            case ItemRarity.Gold: return 8f;
            case ItemRarity.Red: return 2f;
            default: return 0f;
        }
    }
}
