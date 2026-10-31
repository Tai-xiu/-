using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 通用生命值组件（白盒原型阶段）
/// 玩家与敌人共用：
/// - TakeDamage受伤、Heal治疗
/// - 生命归零时触发onDeath UnityEvent（Inspector可挂回调，也支持代码AddListener，便于扩展死亡表现/结算）
/// - onDamaged事件携带实际伤害量，供受击反馈（如变红）等系统监听
/// - DamageModifier伤害修正委托：护甲减伤等系统注入公式（玩家由Inventory注入），默认原样生效
/// 注意：本组件只负责数值与事件，不做销毁/复活等具体死亡处理（死亡结算后续步骤实现）
/// </summary>
public class Health : MonoBehaviour
{
    [Header("—— 生命设置 ——")]
    [Tooltip("最大生命值")]
    [SerializeField] private float maxHealth = 100f;

    [Tooltip("死亡时触发（Inspector可挂回调，用于扩展死亡表现/结算）")]
    public UnityEvent onDeath;

    [Tooltip("受伤时触发，参数为实际伤害量（用于受击反馈等）")]
    public UnityEvent<float> onDamaged;

    /// <summary>
    /// 伤害修正委托：受击时若被设置（如玩家护甲减伤），以返回值作为实际伤害
    /// </summary>
    public Func<float, float> DamageModifier;

    /// <summary>当前生命值（只读，外部通过TakeDamage/Heal修改）</summary>
    public float Current { get; private set; }
    /// <summary>最大生命值</summary>
    public float Max => maxHealth;
    /// <summary>是否已死亡</summary>
    public bool IsDead { get; private set; }

    private void Awake()
    {
        // 初始为满血
        Current = maxHealth;
    }

    /// <summary>
    /// 受伤：先经伤害修正（护甲减伤），扣除生命值，归零时触发死亡事件
    /// </summary>
    public void TakeDamage(float amount)
    {
        // 已死亡或无效伤害直接忽略，避免重复触发死亡
        if (IsDead || amount <= 0f) return;

        // 应用伤害修正（如护甲减伤），修正后≤0视为无效打击
        if (DamageModifier != null)
        {
            amount = DamageModifier(amount);
            if (amount <= 0f) return;
        }

        Current = Mathf.Max(0f, Current - amount);
        onDamaged.Invoke(amount);

        if (Current <= 0f)
        {
            IsDead = true;
            Die();
        }
    }

    /// <summary>
    /// 治疗：恢复生命值（不超过上限），死亡状态下无法治疗
    /// </summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Min(maxHealth, Current + amount);
    }

    /// <summary>
    /// 复活：回满生命并清除死亡状态（死亡结算"返回营地"流程调用）
    /// </summary>
    public void ReviveFull()
    {
        IsDead = false;
        Current = maxHealth;
    }

    /// <summary>
    /// 死亡：触发UnityEvent，具体死亡表现由各监听者决定
    /// </summary>
    private void Die()
    {
        onDeath.Invoke();
    }
}
