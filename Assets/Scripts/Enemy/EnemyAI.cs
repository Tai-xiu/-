using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 敌人AI基类（白盒原型阶段）
/// 结构设计（史莱姆/弓手/后续敌人复用）：
/// - 本基类负责"公共行为"：四状态机（Idle待机→Chase追踪→Attack攻击→Dead死亡）、
///   玩家侦测（detectRadius）、NavMeshAgent移动、攻击计时（attackInterval）、
///   死亡表现（缩放变小）与金币掉落
/// - 三个状态更新方法均为 protected virtual，子类可按需重写：
///     史莱姆 SlimeEnemy → 仅重写 PerformAttack（近战伤害），Chase用基类直追逻辑
///     骷髅弓手 ArcherAI  → 重写 UpdateChase（风筝走位）与 UpdateAttack（站桩射击），
///                          并重写 PerformAttack 生成箭矢
/// 状态流转（基类默认）：
///   Idle：原地待命；玩家进入侦测半径 → Chase
///   Chase：NavMeshAgent追踪玩家；距离≤attackRange → Attack；玩家脱离侦测半径 → Idle
///   Attack：停下面向玩家；每attackInterval秒调用一次PerformAttack；玩家拉开距离 → Chase
///   Dead：由Health.onDeath触发（唯一入口）；停止寻路 → 缩放消失 → 原地掉金币 → 销毁自身
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(Health))]
public class EnemyAI : MonoBehaviour
{
    /// <summary>敌人状态</summary>
    public enum State { Idle, Chase, Attack, Dead }

    [Header("—— 侦测与移动 ——")]
    [Tooltip("侦测半径：玩家进入后开始追踪（米）")]
    [SerializeField] protected float detectRadius = 6f;

    [Tooltip("追踪移动速度（米/秒）")]
    [SerializeField] protected float moveSpeed = 3f;

    [Header("—— 攻击设置 ——")]
    [Tooltip("攻击范围：玩家进入后停下攻击（米）；弓手此处含义为最大射击距离")]
    [SerializeField] protected float attackRange = 1.2f;

    [Tooltip("攻击间隔（秒）")]
    [SerializeField] protected float attackInterval = 1.5f;

    [Header("—— 死亡表现 ——")]
    [Tooltip("死亡缩放动画时长（秒）")]
    [SerializeField] private float deathShrinkDuration = 0.5f;

    [Tooltip("金币占位物边长（米）")]
    [SerializeField] private float coinSize = 0.3f;

    [Tooltip("死亡掉落的金币物品（拾取进背包用，Inspector指定金币资产）")]
    [SerializeField] private ItemData coinItem;

    /// <summary>当前状态（公开只读，便于调试与测试）</summary>
    public State CurrentState { get; private set; } = State.Idle;

    /// <summary>寻路代理缓存</summary>
    protected NavMeshAgent Agent { get; private set; }
    /// <summary>自身生命组件缓存</summary>
    protected Health SelfHealth { get; private set; }
    /// <summary>玩家Transform（Start时按Tag查找）</summary>
    protected Transform Player { get; private set; }
    /// <summary>玩家生命组件（判断玩家是否存活）</summary>
    protected Health PlayerHealth { get; private set; }

    /// <summary>攻击冷却计时器（子类重写攻击状态时可复用）</summary>
    protected float _attackTimer;
    /// <summary>金币共享材质（运行时创建一次，静态缓存避免重复实例）</summary>
    private static Material _coinMaterial;

    protected virtual void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        SelfHealth = GetComponent<Health>();
        Agent.speed = moveSpeed;
        // 略近于攻击范围停下，确保停稳时玩家处于可攻击距离
        Agent.stoppingDistance = attackRange * 0.8f;

        // 自动挂载头顶血条（所有继承EnemyAI的敌人自动获得）
        if (GetComponent<EnemyHealthBar>() == null)
        {
            gameObject.AddComponent<EnemyHealthBar>();
        }
    }

    protected virtual void Start()
    {
        // 按Tag查找玩家
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            Player = playerObj.transform;
            PlayerHealth = playerObj.GetComponent<Health>();
        }

        // 订阅自身死亡事件（唯一Dead入口）
        if (SelfHealth != null)
        {
            SelfHealth.onDeath.AddListener(OnDeath);
        }
    }

    protected virtual void OnDestroy()
    {
        // 移除监听，防止对象销毁后回调悬空
        if (SelfHealth != null)
        {
            SelfHealth.onDeath.RemoveListener(OnDeath);
        }
    }

    protected virtual void Update()
    {
        if (CurrentState == State.Dead) return;

        switch (CurrentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Attack:
                UpdateAttack();
                break;
        }
    }

    /// <summary>Idle：原地待命，玩家进入侦测半径则开始追踪（子类一般无需重写）</summary>
    protected virtual void UpdateIdle()
    {
        Agent.isStopped = true;
        if (IsPlayerAlive() && DistanceToPlayer() <= detectRadius)
        {
            ChangeState(State.Chase);
        }
    }

    /// <summary>Chase：基类默认直追玩家；弓手重写为风筝走位</summary>
    protected virtual void UpdateChase()
    {
        if (!IsPlayerAlive() || DistanceToPlayer() > detectRadius)
        {
            Agent.isStopped = true;
            ChangeState(State.Idle);
            return;
        }

        Agent.isStopped = false;
        Agent.SetDestination(Player.position);

        if (DistanceToPlayer() <= attackRange)
        {
            Agent.isStopped = true;
            _attackTimer = attackInterval; // 抵达即冷却完毕，第一击无需等待
            ChangeState(State.Attack);
        }
    }

    /// <summary>Attack：基类默认停下面向玩家按间隔攻击；弓手重写为站桩射击+近身后撤</summary>
    protected virtual void UpdateAttack()
    {
        if (!IsPlayerAlive() || DistanceToPlayer() > detectRadius)
        {
            ChangeState(State.Chase);
            return;
        }

        // 平滑面向玩家
        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(toPlayer.normalized, Vector3.up), 10f * Time.deltaTime);
        }

        // 玩家跑出攻击范围 → 继续追踪
        if (DistanceToPlayer() > attackRange)
        {
            ChangeState(State.Chase);
            return;
        }

        // 攻击计时
        _attackTimer += Time.deltaTime;
        if (_attackTimer >= attackInterval)
        {
            _attackTimer = 0f;
            PerformAttack();
        }
    }

    /// <summary>
    /// 执行一次攻击（子类重写实现具体攻击方式：史莱姆近战/弓手箭矢投射物）
    /// 仅在Attack状态且玩家处于攻击范围内时被基类调用
    /// </summary>
    protected virtual void PerformAttack()
    {
        // 基类默认无攻击行为
    }

    /// <summary>
    /// 死亡处理（Health.onDeath触发）：停止寻路 → 状态切Dead → 缩放变小 → 原地掉落金币 → 销毁自身
    /// </summary>
    private void OnDeath()
    {
        if (CurrentState == State.Dead) return;
        ChangeState(State.Dead);

        if (Agent != null) Agent.enabled = false; // 停止寻路，避免销毁过程中报错
        StartCoroutine(DeathRoutine());
    }

    /// <summary>死亡表现协程：缩放到0后在原死亡位置掉金币并销毁</summary>
    private IEnumerator DeathRoutine()
    {
        // 记录死亡位置（本体原地缩放，金币掉在原地）
        Vector3 deathPos = transform.position;

        Vector3 startScale = transform.localScale;
        float t = 0f;
        while (t < deathShrinkDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / deathShrinkDuration);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, k);
            yield return null;
        }

        // 掉落金币占位物（拾取逻辑后续步骤实现）
        SpawnCoin(deathPos);

        Destroy(gameObject);
    }

    /// <summary>生成金币占位物：小Cube，淡黄色（运行时材质，不生成资产）</summary>
    private void SpawnCoin(Vector3 position)
    {
        if (_coinMaterial == null)
        {
            _coinMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            _coinMaterial.color = new Color(1f, 0.85f, 0.2f, 1f); // 金黄色
        }

        GameObject coin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        coin.name = "GoldCoin";
        coin.transform.position = new Vector3(position.x, coinSize * 0.5f, position.z);
        coin.transform.localScale = Vector3.one * coinSize;
        coin.GetComponent<MeshRenderer>().sharedMaterial = _coinMaterial;
        // 占位金币不参与实体阻挡（设为Trigger，玩家可正常走过）
        coin.GetComponent<BoxCollider>().isTrigger = true;
        // 挂拾取组件：玩家触碰自动拾取进背包（金币物品资产在Inspector指定）
        if (coinItem != null)
        {
            coin.AddComponent<PickupItem>().Init(coinItem, 1);
        }
    }

    /// <summary>切换状态（Dead只允许由OnDeath进入）</summary>
    protected void ChangeState(State next)
    {
        CurrentState = next;
    }

    /// <summary>与玩家的水平距离</summary>
    protected float DistanceToPlayer()
    {
        if (Player == null) return float.MaxValue;
        Vector3 d = Player.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    /// <summary>玩家是否存在且存活</summary>
    protected bool IsPlayerAlive()
    {
        return Player != null && PlayerHealth != null && !PlayerHealth.IsDead;
    }
}
