using System;
using UnityEngine;

/// <summary>
/// 玩家钱包（货币系统，白盒原型阶段）
/// - 金币数量独立于背包物品栏：死亡清空背包/装备时钱包保留（搜打撤规则：金币带出才安全）
/// - 跨场景数据：Start时若GameManager存有上个场景捕获的数据则恢复（营地↔迷宫传送，死亡保留规则不变）
/// - Add增加、TrySpend扣减（余额不足返回false，供商店购买校验）
/// - 数据变化通过 Changed 事件通知UI刷新
/// </summary>
public class PlayerWallet : MonoBehaviour
{
    [Tooltip("初始金币数量")]
    [SerializeField] private int coins = 50;

    /// <summary>当前金币数</summary>
    public int Coins => coins;

    /// <summary>数据变化事件（UI订阅刷新）</summary>
    public event Action Changed;

    private void Start()
    {
        // 跨场景数据恢复：GameManager存有上个场景捕获的金币时优先恢复（传送流程）
        if (GameManager.Instance != null && GameManager.Instance.ConsumeWallet(out int saved))
        {
            coins = saved;
            Changed?.Invoke();
        }
    }

    /// <summary>是否能支付指定金额</summary>
    public bool CanAfford(int amount) => coins >= amount;

    /// <summary>增加金币（拾取/出售返还/任务奖励等）</summary>
    public void Add(int amount)
    {
        if (amount <= 0) return;
        coins += amount;
        Changed?.Invoke();
    }

    /// <summary>
    /// 尝试扣减金币：余额足够时扣款并返回true；不足时不动余额返回false
    /// </summary>
    public bool TrySpend(int amount)
    {
        if (amount <= 0 || coins < amount) return false;
        coins -= amount;
        Changed?.Invoke();
        return true;
    }
}
