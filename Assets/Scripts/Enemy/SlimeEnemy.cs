using UnityEngine;

/// <summary>
/// 史莱姆（近战敌人，白盒原型阶段）
/// 继承EnemyAI获得完整状态机/侦测/NavMesh追踪/攻击计时/死亡掉落等公共逻辑，
/// 仅重写PerformAttack实现近战伤害。
/// 后续弓手：新建EnemyArcher继承EnemyAI，重写PerformAttack为远程投射物即可，无需改动基类。
/// </summary>
public class SlimeEnemy : EnemyAI
{
    [Header("—— 近战设置 ——")]
    [Tooltip("近战攻击力")]
    [SerializeField] private float meleeDamage = 10f;

    [Tooltip("近战判定范围（略大于攻击范围，防止贴脸边缘打空）")]
    [SerializeField] private float meleeHitRange = 1.5f;

    protected override void Awake()
    {
        base.Awake();

        // 白盒绿色外形（运行时材质实例，不生成资产，不影响其他物体的默认材质）
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            r.material.color = new Color(0.35f, 0.8f, 0.35f, 1f);
        }
    }

    /// <summary>
    /// 近战攻击：玩家处于近战判定范围内时，对其Health造成伤害
    /// </summary>
    protected override void PerformAttack()
    {
        if (Player == null || PlayerHealth == null || PlayerHealth.IsDead) return;

        // 水平距离判定
        Vector3 d = Player.position - transform.position;
        d.y = 0f;
        if (d.magnitude <= meleeHitRange)
        {
            PlayerHealth.TakeDamage(meleeDamage);
            Debug.Log("[SlimeEnemy] 近战命中玩家，造成 " + meleeDamage + " 伤害，玩家剩余 "
                + PlayerHealth.Current + "/" + PlayerHealth.Max);
        }
    }
}
