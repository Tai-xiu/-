using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 骷髅（远程怪，基础怪物设计配置表第二怪）——继承MonsterAI通用状态机，差异：
/// - KeepDistance保持射程替换Chase追击：玩家>射程12m追近；6~12m站定攻击；<6m后退拉开
///   （移速2.2慢于玩家行走——被逼近时拉开只是延缓，逼近是解法）
/// - 攻击=直线弹体（无追踪，可被掩体/立柱阻挡，弹速12m/s，伤害10，前摇0.8s举弓明显）：
///   弹体从左手弓位射向玩家胸口当前点，飞行途中不修正方向——玩家横移即可躲开
/// - 被贴身近战命中→2s哑火（慌乱后撤不射箭），给近战玩家奖励窗口
/// - 数值=配置表：HP30/攻10（弹体）/速2.2/前摇0.8/间隔2.5/射程12/仇恨10/脱战18m·5s
/// 模型：白骨胶囊+红色眼点（正面标识）+左手水平Cube弓（前摇举弓动画）
/// </summary>
public class SkeletonAI : MonsterAI
{
    [Header("—— 骷髅专属 ——")]
    [Tooltip("后退拉开触发距离（米）：玩家贴近低于该距离后撤")]
    [SerializeField] private float retreatDistance = 6f;
    [Tooltip("哑火时长（秒）：被贴身近战命中后慌乱后撤不射箭")]
    [SerializeField] private float jamDuration = 2f;
    [Tooltip("哑火触发距离（米）：被近战命中时与玩家的距离低于该值进入哑火")]
    [SerializeField] private float jamTriggerDistance = 3f;
    [Tooltip("弹体速度（米/秒，配置表=12）")]
    [SerializeField] private float projectileSpeed = 12f;
    [Tooltip("弹体最大飞行距离（米）")]
    [SerializeField] private float projectileMaxDist = 15f;
    [Tooltip("弹体视觉边长（米）")]
    [SerializeField] private float projectileSize = 0.15f;

    private float _jamTimer;       // 哑火剩余（>0=慌乱后撤不射箭）
    private float _retreatRepath;  // 后撤重寻路节流

    protected override void Awake()
    {
        // 配置表数值覆盖（在base.Awake使用前赋值；Inspector可再调）
        moveSpeed = 2.2f;
        aggroRadius = 10f;
        leashRadius = 18f;
        attackRange = 12f;      // 射程语义：KeepDistance的攻击上限
        attackDamage = 10f;
        attackWindup = 0.8f;
        attackInterval = 2.5f;
        bodyColor = new Color(0.85f, 0.85f, 0.8f, 1f); // 白骨色
        base.Awake();

        Agent.stoppingDistance = 0.5f; // 保持射程逻辑自行控距，不走基类攻击距离停位
    }

    /// <summary>
    /// KeepDistance保持射程（替换基类Chase直追）：
    /// >12m追近 → 6~12m站定攻击 → <6m后退拉开；哑火期间强制后撤不射箭
    /// </summary>
    protected override void UpdateChase()
    {
        if (!IsPlayerAlive())
        {
            EnterReturn();
            return;
        }
        if (AccumulateLeash()) return;

        // 哑火：慌乱后撤不射箭（被贴身近战命中触发）
        if (_jamTimer > 0f)
        {
            _jamTimer -= Time.deltaTime;
            RetreatStep();
            return;
        }

        float dist = DistanceToPlayer();
        if (dist > attackRange)
        {
            // >12m：追近（进入射程后转站定攻击）
            Agent.isStopped = false;
            Agent.speed = moveSpeed;
            Agent.SetDestination(Player.position);
        }
        else
        {
            // ≤12m（含贴脸）：站定攻击/回击——间隔走满立即停步回弓反击（2026-10-10用户规则）
            EnterAttack();
        }
    }

    /// <summary>后撤一步：远离玩家方向3m寻位点（0.4s节流重寻）</summary>
    private void RetreatStep()
    {
        Agent.isStopped = false;
        Agent.speed = moveSpeed;
        _retreatRepath -= Time.deltaTime;
        if (_retreatRepath > 0f) return;
        _retreatRepath = 0.4f;

        var away = transform.position - Player.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f) away = -transform.forward;
        var dest = transform.position + away.normalized * 3f;
        if (NavMesh.SamplePosition(dest, out var hit, 2f, NavMesh.AllAreas))
        {
            Agent.SetDestination(hit.position);
        }
    }

    /// <summary>
    /// 远程攻击：举弓前摇0.8s（武器动画配合）→ 弹体射向玩家胸口当前点（直线，不追踪）
    /// → 恢复至间隔走满回KeepDistance（≤12m直接重进前摇）
    /// 恢复期（间隔走满前）：玩家靠近（<retreatDistance，无法反击的窗口）→ 边恢复边后撤拉开；
    /// 间隔走满 → 立即停步回弓反击（即使玩家仍在贴脸——停步站定举弓射出，不再拉开）
    /// </summary>
    protected override void UpdateAttack()
    {
        if (!IsPlayerAlive())
        {
            EnterReturn();
            return;
        }
        if (AccumulateLeash()) return;

        FacePlayer(10f);
        _attackTimer += Time.deltaTime;

        if (_attackTimer < attackWindup)
        {
            // 举弓前摇：站定举弓（间隔走满即停步回弓反击，不再因贴脸放弃）
            return;
        }

        if (!_strikeHit)
        {
            _strikeHit = true;
            FireProjectile();
        }

        if (_attackTimer < attackInterval)
        {
            // 恢复期（用户规则）：玩家贴近才拉开距离（边恢复边后撤，防近战白嫖）；
            // 距离安全则站桩回气；间隔走满立即回击
            if (DistanceToPlayer() < retreatDistance)
            {
                RetreatStep();
            }
            else
            {
                Agent.isStopped = true;
            }
            return;
        }
        EnterChase();
    }

    /// <summary>发射直线弹体：从左手弓位射向玩家胸口当前点（不追踪，走位可躲）</summary>
    private void FireProjectile()
    {
        var firePos = transform.position + Vector3.up * 1.1f
            + transform.forward * 0.4f - transform.right * 0.35f; // 左手弓位
        var targetPos = Player.position + Vector3.up * 1.0f;      // 瞄准玩家胸口
        var dir = (targetPos - firePos).normalized;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "SkeletonBolt";
        Object.Destroy(go.GetComponent<Collider>()); // 弹体无碰撞体：命中/阻挡走球形检测
        go.transform.position = firePos;
        go.transform.localScale = Vector3.one * projectileSize;
        go.GetComponent<MeshRenderer>().sharedMaterial = BoltMaterial();

        var proj = go.AddComponent<MonsterProjectile>();
        proj.Init(dir, projectileSpeed, attackDamage, projectileMaxDist, transform);
        AudioManager.PlaySfx("SFX_SwordSwing", transform.position); // 射击音占位（待专属音效）
    }

    private static Material _boltMaterial;
    private Material BoltMaterial()
    {
        if (_boltMaterial == null)
        {
            _boltMaterial = new Material(Shader.Find("Standard"));
            _boltMaterial.color = new Color(0.85f, 0.8f, 0.7f, 1f); // 骨白弹体
        }
        return _boltMaterial;
    }

    /// <summary>被贴身近战命中→2s哑火（慌乱后撤不射箭，给近战玩家奖励窗口）</summary>
    protected override void OnSelfDamaged(float amount)
    {
        base.OnSelfDamaged(amount); // 白闪+击退+打断（基类通用受击）
        if (CurrentState == State.Dead) return;
        if (Player != null && DistanceToPlayer() <= jamTriggerDistance)
        {
            _jamTimer = jamDuration;
            if (CurrentState == State.Attack) EnterChase(); // 打断射击转慌乱后撤
        }
    }

    /// <summary>骷髅武器动画：前摇举弓（水平弓抬至瞄准角）→保持→射击后回位</summary>
    protected override void UpdateWeapon()
    {
        if (weapon == null) return;
        if (!_weaponRestCaptured)
        {
            _weaponRestRot = weapon.localRotation;
            _weaponRestCaptured = true;
        }

        Quaternion target;
        if (CurrentState == State.Attack && _attackTimer < attackWindup)
        {
            float k = Mathf.Clamp01(_attackTimer / attackWindup);
            target = _weaponRestRot * Quaternion.Euler(-35f * k, 0f, 0f); // 举弓瞄准
        }
        else if (CurrentState == State.Attack && _attackTimer < attackWindup + 0.3f)
        {
            target = _weaponRestRot * Quaternion.Euler(-35f, 0f, 0f); // 射后保持片刻
        }
        else
        {
            target = _weaponRestRot;
        }
        weapon.localRotation = Quaternion.Slerp(weapon.localRotation, target, 25f * Time.deltaTime);
    }
}
