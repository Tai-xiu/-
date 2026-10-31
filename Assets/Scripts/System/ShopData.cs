using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 商店条目：出售的物品 + 该商店的购买价格
/// （购买价与物品自身sellPrice出售价分离：低买高卖形成商店利润）
/// </summary>
[Serializable]
public class ShopEntry
{
    public ItemData item;
    [Tooltip("购买单价（买1个该物品消耗的金币）")]
    public int buyPrice;
}

/// <summary>
/// 商店商品列表配置（ScriptableObject资产）
/// Project窗口右键 Create → Roguelite → Shop 创建，填入商品与购买价
/// </summary>
[CreateAssetMenu(fileName = "New Shop", menuName = "Roguelite/Shop")]
public class ShopData : ScriptableObject
{
    [Tooltip("本商店出售的商品列表")]
    public List<ShopEntry> entries = new List<ShopEntry>();
}
