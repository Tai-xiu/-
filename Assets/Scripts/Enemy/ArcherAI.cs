using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 骷髅弓手（远程敌人，白盒原型阶段）
/// 继承EnemyAI状态机，与史莱姆的差异：
/// - Chase重写为"风筝走位"：玩家超过最大射程(attackRange)→靠近；不足最小射程(shootMinRange)→后撤；
///   射程带内→进入Attack站桩射击
/// - Attack重写为"射击"：站桩面向玩家，每attackInterval秒（默认2秒，可调）调用PerformAttack发射箭矢；
///   玩家逼近到最小射程内→边保持攻击状态边后撤（后撤中不射击）
/// - PerformAttack重写为生成箭矢（Arrow脚本负责飞行/命中/销毁）
/// </summary>
public class ArcherAI : EnemyAI
{
    [Header("—— 射击设置 ——")]
    [Tooltip("最小射程：玩家低于此距离时后撤（米）")]
    [SerializeField] private float shootMinRange = 3f;

    [Tooltip("箭矢飞行速度（米/秒）")]
    [SerializeField] private float arrowSpeed = 10f;

    [Tooltip("箭矢伤害")]
    [SerializeField] private float arrowDamage = 8f;

    protected override void Awake()
    {
        base.Awake();

        // 基类按attackRange(7米)设置停距会让"后撤1~2米"被代理视为已到达而不移动，
        // 弓手需要短距走位（后撤/贴进），此处改为固定小停距
        Agent.stoppingDistance = 0.3f;

        // 白盒灰白色外形（运行时材质实例，不生成资产，不影响其他物体的默认材质）
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            r.material.color = new Color(0.75f, 0.75f, 0.8f, 1f);
        }
    }

    /// <summary>
    /// 追击（风筝走位）：玩家超出最大射程时靠近；进入最大射程即转Attack站桩射击
    /// （后撤逻辑在Attack状态内处理：玩家贴脸时边后撤边保持攻击姿态）
    /// </summary>
    protected override void UpdateChase()
    {
        if (!IsPlayerAlive() || DistanceToPlayer() > detectRadius)
        {
            Agent.isStopped = true;
            ChangeState(State.Idle);
            return;
        }

        // 进入最大射程 → 站桩射击
        if (DistanceToPlayer() <= attackRange)
        {
            Agent.isStopped = true;
            _attackTimer = attackInterval; // 进入射程首箭无需等待
            ChangeState(State.Attack);
            return;
        }

        // 超出最大射程 → 靠近玩家
        Agent.isStopped = false;
        Agent.SetDestination(Player.position);
    }

    /// <summary>
    /// 射击：面向玩家按间隔发射箭矢；玩家贴脸（低于最小射程）时后撤拉开距离
    /// </summary>
    protected override void UpdateAttack()
    {
        if (!IsPlayerAlive() || DistanceToPlayer() > detectRadius)
        {
            ChangeState(State.Chase);
            return;
        }

        float d = DistanceToPlayer();

        // 玩家贴脸 → 后撤拉开距离（后撤中不射击，保持攻击状态）
        if (d < shootMinRange)
        {
            Agent.isStopped = false;
            Vector3 awayDir = (transform.position - Player.position).normalized;
            Vector3 awayPos = transform.position + awayDir * 2f;
            // 后撤点投影回导航网格，避免目标点落在墙内导致寻路失败
            if (NavMesh.SamplePosition(awayPos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                Agent.SetDestination(hit.position);
            }
            return;
        }

        // 玩家跑出最大射程 → 转追击
        if (d > attackRange)
        {
            Agent.isStopped = false;
            ChangeState(State.Chase);
            return;
        }

        // 站桩：停下面向玩家
        Agent.isStopped = true;
        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(toPlayer.normalized, Vector3.up), 10f * Time.deltaTime);
        }

        // 射击计时
        _attackTimer += Time.deltaTime;
        if (_attackTimer >= attackInterval)
        {
            _attackTimer = 0f;
            PerformAttack();
        }
    }

    /// <summary>
    /// 射击：朝玩家当前位置发射一支箭矢（白盒不做弹道预判）
    /// </summary>
    protected override void PerformAttack()
    {
        if (Player == null || PlayerHealth == null || PlayerHealth.IsDead) return;

        // 箭矢从胸口高度发出，朝玩家胸口高度飞行
        Vector3 spawnPos = transform.position + Vector3.up * 0.5f + transform.forward * 0.4f;
        Vector3 targetPos = Player.position + Vector3.up * 0.5f;
        Vector3 dir = (targetPos - spawnPos).normalized;

        Arrow.Spawn(spawnPos, dir, arrowSpeed, arrowDamage);
        Debug.Log("[ArcherAI] 射出箭矢，朝向 " + Player.name);
    }
}
