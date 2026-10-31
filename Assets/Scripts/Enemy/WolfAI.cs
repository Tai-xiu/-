using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 魔狼（快速近战怪，基础怪物设计配置表第三怪）——继承MonsterAI通用状态机，差异：
/// - Attack距离分流双段攻击（2026-10-10用户规则）：
///   近距≤2.0m：小突进（0.5m快速推进+触及判定咬中）；
///   2~4m：原地蓄力冲刺（压低前摇0.3s后高速直线冲向玩家，触发器触碰即咬中，
///   冲到玩家身前0.9m停止，无抛物线无后跳）
/// - 霸体：攻击全程取消受击打断（被打仅白闪，循环不中断不后撤）
/// - 防撞：攻击位移全程与玩家保持≥0.9m【水平】距离——狼身永不接触玩家=玩家零位移
///   （钳制必须用水平距离：狼y0.35/玩家y1.0垂直差会把三维距离稀释——实测踩坑）
/// - 移速5.5>玩家行走5：想拉开必须冲刺（耗耐力），教玩家耐力管理
/// - 数值=配置表：HP25/攻6/速5.5/蓄力前摇0.3/间隔1.2/触及2.0/仇恨12（嗅觉）/脱战20m·8s
/// 模型：黑色胶囊（高0.7m=哥布林一半）+黄色眼点（正面标识，黑身上醒目），无武器（扑咬）
/// </summary>
public class WolfAI : MonsterAI
{
    [Header("—— 魔狼专属 ——")]
    [Tooltip("近距小突进攻击上限（米）：≤该值用小突进")]
    [SerializeField] private float meleeRange = 2.0f;
    [Tooltip("冲刺攻击触发上限（米）：该值内可蓄力冲刺（配置表2~4m）")]
    [SerializeField] private float maxAttackRange = 4.0f;
    [Tooltip("冲刺距离（米）：朝玩家方向高速冲刺（会按玩家实际距离收敛）")]
    [SerializeField] private float dashDistance = 3f;
    [Tooltip("冲刺时长（秒）")]
    [SerializeField] private float dashDuration = 0.35f;
    [Tooltip("咬击触发器（攻击段启用，触碰玩家即伤害；留空自动找子物体BiteTrigger）")]
    [SerializeField] private Collider biteTrigger;
    [Tooltip("小突进距离（米）：近距攻击的推进量")]
    [SerializeField] private float meleeLungeDistance = 0.5f;
    [Tooltip("小突进时长（秒）")]
    [SerializeField] private float meleeLungeDuration = 0.2f;
    [Tooltip("蹲伏压低系数：身体scale.y压到该倍率（蓄力前摇可读）")]
    [SerializeField] private float crouchFactor = 0.6f;

    /// <summary>魔狼攻击子相位（None=空闲，可公开供调试）</summary>
    public enum WolfPhase { None, Crouch, MeleeLunge, Dash }
    public WolfPhase Phase { get; private set; } = WolfPhase.None;

    private Coroutine _cycle;            // 攻击协程引用（死亡打断用）
    private bool _cycleRunning;          // 攻击协程驱动中
    private Vector3 _baseScale;          // 原始身体缩放（蹲伏压低/打断恢复用）
    private bool _biteHit;               // 本轮攻击是否已咬中（触发器只结算一次）
    private readonly Collider[] _biteBuf = new Collider[8]; // 咬击重叠查询复用缓冲（免每帧分配）

    protected override void Awake()
    {
        // 配置表数值覆盖（在base.Awake使用前赋值；Inspector可再调）
        moveSpeed = 5.5f;
        aggroRadius = 12f;
        leashRadius = 20f;
        leashTime = 8f;
        attackRange = 2.0f;
        attackDamage = 6f;
        attackWindup = 0.3f;
        attackInterval = 1.2f;
        bodyColor = new Color(0.12f, 0.12f, 0.14f, 1f); // 黑色
        base.Awake();

        _baseScale = transform.localScale;

        // 咬击触发器：自动找子物体+挂转发器Owner
        if (biteTrigger == null)
        {
            var t = transform.Find("BiteTrigger");
            if (t != null) biteTrigger = t.GetComponent<Collider>();
        }
        if (biteTrigger != null)
        {
            biteTrigger.enabled = false;
            var fwd = biteTrigger.GetComponent<WolfBiteTrigger>();
            if (fwd != null) fwd.Owner = this;
        }
    }

    /// <summary>
    /// 攻击调度（协程全权驱动Attack状态）：按距离分流——
    /// 近距≤meleeRange(2m)：小突进攻击（0.5m快速推进+触及判定咬中）；
    /// meleeRange~maxAttackRange(2~4m)：原地蓄力冲刺（高速直线冲向玩家，触发器触碰即咬中，
    /// 冲到玩家身前0.9m停止）
    /// </summary>
    protected override void UpdateAttack()
    {
        if (_cycleRunning) return; // 攻击协程驱动中
        if (!IsPlayerAlive())
        {
            EnterReturn();
            return;
        }
        if (AccumulateLeash()) return;

        FacePlayer(10f);
        Agent.isStopped = true;

        float dist = DistanceToPlayer();
        if (dist <= meleeRange)
        {
            StartAttack(false); // 近距小突进
        }
        else if (dist <= maxAttackRange)
        {
            StartAttack(true); // 蓄力冲刺
        }
    }

    /// <summary>启动攻击协程（统一入口：置运行标记防每帧重入）</summary>
    private void StartAttack(bool dash)
    {
        if (_cycleRunning) return;
        _cycleRunning = true;
        _cycle = StartCoroutine(AttackRoutine(dash));
    }

    /// <summary>
    /// 攻击协程（dash=2~4m原地蓄力直线冲刺触发器咬中 / false=小突进0.5m触及判定咬中）：
    /// 蓄力压低前摇0.3s → 位移段 → 恢复至间隔走满回Chase。
    /// 位移期间禁用NavMeshAgent逐帧插值（落点Warp回网格）；
    /// 全程与玩家保持≥0.9m水平间距（狼身永不接触玩家=玩家零位移）；
    /// 霸体：受击不打断不击退（用户规则：取消受击打断），仅白闪
    /// </summary>
    private IEnumerator AttackRoutine(bool dash)
    {
        _cycleRunning = true; // 置运行标记：UpdateAttack守卫防每帧重入（多协程打架=抽搐，实测踩坑）
        Phase = WolfPhase.Crouch;
        Vector3 baseScale = transform.localScale;
        Vector3 crouchScale = new Vector3(baseScale.x, baseScale.y * crouchFactor, baseScale.z);
        float w = 0f;
        while (w < attackWindup)
        {
            w += Time.deltaTime;
            _attackTimer += Time.deltaTime;
            float k = Mathf.Clamp01(w / attackWindup);
            transform.localScale = Vector3.Lerp(baseScale, crouchScale, k);
            FacePlayer(12f);
            yield return null;
        }
        transform.localScale = baseScale;
        Phase = dash ? WolfPhase.Dash : WolfPhase.MeleeLunge;
        _biteHit = false;
        if (biteTrigger != null) biteTrigger.enabled = true;
        if (Agent != null) Agent.enabled = false;

        Vector3 startPos = transform.position;
        var toPlayer = Player.position - startPos;
        toPlayer.y = 0f;
        var dir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : transform.forward;
        // 位移量：冲刺=min(3m, 距玩家-0.9m身位圈)；小突进=0.5m但同样收敛到身位圈外（狼身永不接触玩家）
        float moveDist = dash
            ? Mathf.Min(dashDistance, Mathf.Max(0.1f, toPlayer.magnitude - 0.9f))
            : Mathf.Max(0.05f, Mathf.Min(meleeLungeDistance, toPlayer.magnitude - 0.9f));
        float moveDur = dash ? dashDuration : meleeLungeDuration;
        var landPos = startPos + dir * moveDist;
        if (NavMesh.SamplePosition(landPos, out var navHit, 2f, NavMesh.AllAreas))
        {
            landPos.x = navHit.position.x; // 只取网格水平投影
            landPos.z = navHit.position.z;
        }
        landPos.y = startPos.y; // 全程贴地水平：Y随SamplePosition重算会有~0.1m起伏（实测踩坑，回气段Warp再贴网格）
        landPos = ClampKeepOut(landPos); // 终点同样钳制（逐帧钳同规则）：终点吸附不得绕过身位圈
        BiteCheckNow(); // 启用即查：追击近身时启用触发器可能早已与玩家重叠（无ENTER事件，实测踩坑）
        float t = 0f;
        while (t < moveDur)
        {
            t += Time.deltaTime;
            _attackTimer += Time.deltaTime;
            float k = Mathf.Clamp01(t / moveDur);
            transform.position = ClampKeepOut(Vector3.Lerp(startPos, landPos, k));
            BiteCheckNow(); // 逐帧Stay式查询：位移穿越与静止重叠都必咬中（OnTriggerEnter只报进入）
            yield return null;
        }
        transform.position = landPos;
        if (biteTrigger != null) biteTrigger.enabled = false;

        // 恢复至间隔走满（站桩回气）后回Chase
        while (_attackTimer < attackInterval)
        {
            _attackTimer += Time.deltaTime;
            yield return null;
        }
        RecoverFromCycle();
        EnterChase();
        _cycle = null;
        _cycleRunning = false; // 循环自然结束：清运行标记
    }

    /// <summary>水平身位圈钳：狼身永不深入玩家0.9m圈（途中+终点共用，防推挤=玩家零位移）</summary>
    private Vector3 ClampKeepOut(Vector3 pos)
    {
        if (Player == null) return pos;
        var flat = new Vector3(pos.x - Player.position.x, 0f, pos.z - Player.position.z);
        if (flat.magnitude >= 0.9f) return pos;
        var n = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.back;
        return new Vector3(Player.position.x + n.x * 0.9f, pos.y, Player.position.z + n.z * 0.9f);
    }

    /// <summary>咬击触发器命中回调（WolfBiteTrigger转发）：本轮攻击只结算一次</summary>
    public void NotifyBiteHit(Collider other)
    {
        if (_biteHit || CurrentState == State.Dead) return;
        if (!other.CompareTag("Player")) return;
        TryBite(other);
    }

    /// <summary>Stay式咬击查询：OverlapBox按触发器当前世界位姿找玩家（不依赖OnTriggerEnter的进入事件——
    /// 攻击启用时若早已重叠则无ENTER事件，实测踩坑；事件回调与本查询双通道并存互为兜底）</summary>
    private void BiteCheckNow()
    {
        if (_biteHit || CurrentState == State.Dead) return;
        if (biteTrigger == null || !biteTrigger.enabled) return;
        var box = biteTrigger as BoxCollider;
        if (box == null) return;
        var tr = biteTrigger.transform;
        var center = tr.TransformPoint(box.center);
        var half = Vector3.Scale(box.size * 0.5f, tr.lossyScale);
        int n = Physics.OverlapBoxNonAlloc(center, half, _biteBuf, tr.rotation, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            if (_biteBuf[i].CompareTag("Player"))
            {
                TryBite(_biteBuf[i]);
                return;
            }
        }
    }

    /// <summary>咬中结算：本轮只结算一次，伤害走玩家Health（无击退）</summary>
    private void TryBite(Collider other)
    {
        if (_biteHit) return;
        var targetHealth = other.GetComponent<Health>();
        if (targetHealth == null || targetHealth.IsDead) return;
        _biteHit = true;
        if (PlayerHealth != null) PlayerHealth.TakeDamage(attackDamage);
        AudioManager.PlaySfx("SFX_HitImpact", transform.position);
    }

    /// <summary>循环中断/结束后恢复：姿态复原+导航代理重启（Warp回网格最近点）+咬击触发器关闭</summary>
    private void RecoverFromCycle()
    {
        Phase = WolfPhase.None;
        transform.localScale = _baseScale;
        if (biteTrigger != null) biteTrigger.enabled = false;
        if (Agent != null && !Agent.enabled)
        {
            if (NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas))
            {
                Agent.Warp(hit.position);
            }
            Agent.enabled = true;
        }
    }

    /// <summary>霸体（用户规则：取消受击打断）：受击仅白闪，攻击循环不中断、不被击退不后撤</summary>
    protected override bool Superarmor { get { return true; } }

    /// <summary>死亡：停攻击循环+姿态复原+代理重启，再走基类死亡（倒地常驻）</summary>
    protected override void OnDeath()
    {
        if (_cycle != null)
        {
            StopCoroutine(_cycle);
            _cycle = null;
        }
        RecoverFromCycle();
        base.OnDeath();
    }
}
