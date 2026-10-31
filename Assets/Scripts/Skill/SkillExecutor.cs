using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能执行器（技能系统机制设计v0.1通用生命周期，挂在Player上）
/// ─────────────────────────────────────────
/// 生命周期（设计文档1.2，所有主动技能共用）：
///   按下按键 → [预检]体力够/CD好/不在施法中 → [前摇]可被打断 → [生效]判定 → [后摇]硬直 → [CD开始]
/// 关键规则：
/// - 前摇/生效期间受击 → 立即取消、退还50%消耗（搜打撤特色：放技能有风险）
/// - 死亡 → 强制取消（不退资源）
/// - 后摇结束前不能接新技能、不能普攻（PlayerCombat读IsCasting拦截）
/// - CD在后摇结束时才开始计（设计文档生命周期图）
/// 移速规则（PlayerController读MoveSpeedFactor）：
///   前摇=瞬发可缓动×0.5/位移类站定0；生效=0（旋风斩站定，冲锋位移由本组件驱动CC）；
///   后摇=×0.5；空闲=1
/// 输入：Q=主动技能0（旋风斩）、E=主动技能1（冲锋）；被动无按键（NotifyKill事件驱动）
/// 技能卡：Resources/Skills/ 下按名加载（自动补挂组件无法序列化引用，项目现役模式）
/// 挂载：PlayerCombat.Awake自动补挂（Stamina同模式，两场景Player通用零接线）
/// </summary>
[RequireComponent(typeof(Health))]
public class SkillExecutor : MonoBehaviour
{
    /// <summary>施法阶段（HUD脉冲与移速因子的判定依据）</summary>
    public enum CastPhase { None, Windup, Activate, Recovery }

    [Header("—— 调试 ——")]
    [SerializeField] private bool debugLog = false; // E2E与排查用开关

    private Health _health;
    private Stamina _stamina;
    private PlayerCombat _combat;           // 伤害来源（AttackDamage=当前武器伤害口径）
    private CharacterController _cc;        // 冲锋位移驱动
    private FirstPersonHands _hands;        // 旋风斩连扫复用横劈动画
    private Camera _cam;                    // 冲锋/旋风斩FOV脉冲
    private float _baseFov = 70f;

    private SkillData _whirlwind;           // Q（资源名约定：Skills/Skill_Whirlwind）
    private SkillData _charge;              // E（Skills/Skill_Charge）
    private SkillData _passive;             // 嗜血（Skills/Skill_Bloodthirst，可缺）
    private SkillData _casting;             // 当前施法中的技能卡

    private readonly float[] _cdReadyTime = new float[2];   // 各主动技能CD就绪时刻（Time.time基准）
    private CastPhase _phase = CastPhase.None;
    private Coroutine _routine;
    private Coroutine _fovRoutine;
    /// <summary>冲锋期间忽略碰撞的敌方碰撞体（结束/打断/死亡统一恢复，防悬空引用）</summary>
    private readonly List<Collider> _dashIgnored = new List<Collider>();

    /// <summary>两个主动技能卡（0=Q旋风斩，1=E冲锋；HUD与输入共用此顺序）</summary>
    public SkillData[] ActiveSkills { get; private set; } = new SkillData[2];
    /// <summary>被动技能卡（可为null=无被动）</summary>
    public SkillData PassiveSkill => _passive;
    /// <summary>是否施法中（前摇/生效/后摇任一阶段）——PlayerCombat据此拦截普攻</summary>
    public bool IsCasting => _phase != CastPhase.None;
    /// <summary>当前阶段</summary>
    public CastPhase Phase => _phase;
    /// <summary>当前施法的技能卡（HUD脉冲判断用）</summary>
    public SkillData CastingSkill => _casting;

    /// <summary>
    /// 移速因子：空闲1；前摇=瞬发0.5（可缓动）/位移类0（站定）；生效=0（站定或由本组件位移）；后摇0.5
    /// </summary>
    public float MoveSpeedFactor
    {
        get
        {
            switch (_phase)
            {
                case CastPhase.None: return 1f;
                case CastPhase.Windup:
                    return _casting != null && _casting.canMoveDuringWindup ? 0.5f : 0f;
                case CastPhase.Recovery: return 0.5f;
                default: return 0f; // Activate：一律锁玩家自身移动（冲锋的位移由协程驱动）
            }
        }
    }

    private void Awake()
    {
        _health = GetComponent<Health>();
        _combat = GetComponent<PlayerCombat>();
        _stamina = GetComponent<Stamina>();
        _cc = GetComponent<CharacterController>();
        _hands = GetComponent<FirstPersonHands>();
        _cam = GetComponentInChildren<Camera>();
        if (_cam != null) _baseFov = _cam.fieldOfView;

        // 技能卡加载（Resources约定路径；缺失只告警不崩溃，对应槽位失效）
        _whirlwind = Resources.Load<SkillData>("Skills/Skill_Whirlwind");
        _charge = Resources.Load<SkillData>("Skills/Skill_Charge");
        _passive = Resources.Load<SkillData>("Skills/Skill_Bloodthirst");
        if (_whirlwind == null) Debug.LogWarning("[SkillExecutor] 未找到 Resources/Skills/Skill_Whirlwind，Q技能失效");
        if (_charge == null) Debug.LogWarning("[SkillExecutor] 未找到 Resources/Skills/Skill_Charge，E技能失效");
        ActiveSkills[0] = _whirlwind;
        ActiveSkills[1] = _charge;
    }

    private void OnEnable()
    {
        if (_health != null)
        {
            _health.onDamaged.AddListener(OnSelfDamaged);   // 受击打断
            _health.onDeath.AddListener(OnSelfDeath);       // 死亡强制取消
        }
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.onDamaged.RemoveListener(OnSelfDamaged);
            _health.onDeath.RemoveListener(OnSelfDeath);
        }
    }

    private void Update()
    {
        if (_health == null || _health.IsDead) return;
        if (PauseUI.IsPaused || GameUIState.IsAnyPanelOpen) return; // 暂停/面板打开不放技能

        if (Input.GetKeyDown(KeyCode.Q)) TryCast(0);
        if (Input.GetKeyDown(KeyCode.E)) TryCast(1);
    }

    // ───────────────────────── 查询接口（HUD/其他系统读） ─────────────────────────

    /// <summary>技能是否在CD中</summary>
    public bool IsOnCd(int idx)
    {
        return idx >= 0 && idx < 2 && Time.time < _cdReadyTime[idx];
    }

    /// <summary>CD剩余秒数</summary>
    public float GetCdRemaining(int idx)
    {
        return idx >= 0 && idx < 2 ? Mathf.Max(0f, _cdReadyTime[idx] - Time.time) : 0f;
    }

    /// <summary>CD总时长（HUD环比例分母）</summary>
    public float GetCdDuration(int idx)
    {
        var s = idx >= 0 && idx < 2 ? ActiveSkills[idx] : null;
        return s != null ? s.cooldown : 0f;
    }

    /// <summary>体力是否够放该技能（HUD灰显判据）</summary>
    public bool HasEnoughStamina(int idx)
    {
        var s = idx >= 0 && idx < 2 ? ActiveSkills[idx] : null;
        if (s == null) return false;
        return _stamina == null || _stamina.CanSwingCost(s.staminaCost);
    }

    // ───────────────────────── 释放入口 ─────────────────────────

    /// <summary>
    /// 施放主动技能（预检→扣资源→启动生命周期协程）。
    /// 返回false=被拒绝（施法中/CD中/体力不足/面板暂停/技能缺失），拒绝时不扣任何资源
    /// </summary>
    public bool TryCast(int idx)
    {
        var skill = idx >= 0 && idx < 2 ? ActiveSkills[idx] : null;
        if (skill == null) return false;
        if (IsCasting || IsOnCd(idx)) return false;             // 后摇+CD双拦截，杜绝连放
        if (_health == null || _health.IsDead) return false;
        if (PauseUI.IsPaused || GameUIState.IsAnyPanelOpen) return false;
        if (_stamina != null && !_stamina.CanSwingCost(skill.staminaCost)) return false; // 体力不足

        // 扣体力（预检通过才扣；打断时退50%）
        if (_stamina != null) _stamina.SpendSwing(skill.staminaCost);

        _casting = skill;
        _routine = StartCoroutine(CastRoutine(idx, skill));
        if (debugLog) Debug.Log($"[SkillExecutor] 施放 {skill.displayName}（体力-{skill.staminaCost}）");
        return true;
    }

    /// <summary>生命周期主协程：前摇→生效→后摇→CD（任一阶段被 _interrupted 置位即终止）</summary>
    private IEnumerator CastRoutine(int idx, SkillData skill)
    {
        // —— 前摇 ——
        _phase = CastPhase.Windup;
        for (float t = 0f; t < skill.windup; t += Time.deltaTime)
        {
            yield return null; // 受击打断由OnSelfDamaged→CleanupCast→StopCoroutine直接终止本协程
        }

        // —— 生效 ——
        _phase = CastPhase.Activate;
        if (skill.skillType == SkillData.SkillType.Dash)
        {
            yield return DashMove(skill);   // 冲锋：生效期=位移本身（逐帧命中检测）
        }
        else
        {
            DoMeleeRangeHit(skill);         // 旋风斩：判定即时执行（设计文档"生效↓0.1s判定窗口"）
            PlaySlashFx(skill);             // 环形刀光+音效+手臂连扫
            yield return new WaitForSeconds(skill.activate); // 生效停留（站定由MoveSpeedFactor=0保证）
        }

        // —— 后摇 ——
        _phase = CastPhase.Recovery;
        for (float t = 0f; t < skill.recovery; t += Time.deltaTime)
        {
            yield return null; // 后摇不受击打断（收招硬直不可逆），死亡走StopCoroutine
        }

        // —— 收尾：后摇结束CD才开始（设计文档生命周期） ——
        CleanupCast();
        _cdReadyTime[idx] = Time.time + skill.cooldown;
        if (debugLog) Debug.Log($"[SkillExecutor] {skill.displayName} 完成，进入CD {skill.cooldown}s");
    }

    /// <summary>施法终止统一清理（协程结束/打断/死亡都走这里，保证冲锋碰撞恢复不遗漏）</summary>
    private void CleanupCast()
    {
        RestoreDashCollisions();
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        _phase = CastPhase.None;
        _casting = null;
    }

    // ───────────────────────── 判定实现 ─────────────────────────

    /// <summary>
    /// 近战范围判定（旋风斩）：以自身为心range半径全向命中所有Tag=Enemy且带Health的目标，
    /// 伤害=当前武器伤害×倍率。命中怪物的受击硬直/击退走MonsterAI.OnSelfDamaged既有链路
    /// </summary>
    private void DoMeleeRangeHit(SkillData skill)
    {
        if (skill.damageMultiplier <= 0f) return;
        float dmg = (_combat != null ? _combat.AttackDamage : 10f) * skill.damageMultiplier;
        int hitCount = 0;

        Collider[] hits = Physics.OverlapSphere(transform.position, skill.range);
        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Enemy")) continue;
            var targetHealth = hit.GetComponent<Health>();
            if (targetHealth == null || targetHealth.IsDead) continue;

            targetHealth.TakeDamage(dmg);
            hitCount++;
            if (targetHealth.IsDead) NotifyKill(); // 击杀归因：旋风斩击杀也触发嗜血
            if (debugLog) Debug.Log($"[SkillExecutor] 旋风斩命中 {hit.name}，伤害{dmg}，剩余{targetHealth.Current}");
        }

        if (hitCount > 0) AudioManager.PlaySfx("SFX_HitImpact", transform.position);
    }

    /// <summary>
    /// 冲锋位移：生效期内沿施法起始朝向匀速突进dashDistance（CC逐帧Move）。
    /// - 路径命中：每帧OverlapSphere(dashHitRadius)检测，同一目标只结算一次，
    ///   伤害+怪物自带受击硬直/击退（经OnSelfDamaged，霸体怪自动免疫——不侵入怪物AI）
    /// - 突进期间忽略与敌人的碰撞（可穿过怪群命中多目标，结束时统一恢复）
    /// - 撞墙：单帧实际位移<期望30%判定提前停止
    /// </summary>
    private IEnumerator DashMove(SkillData skill)
    {
        Vector3 dir = transform.forward;
        dir.y = 0f;
        dir.Normalize();
        IgnoreEnemyCollisions(); // 突进前对路径周边敌人忽略碰撞（可穿过怪群）

        float traveled = 0f;
        var damaged = new HashSet<Health>();
        if (_fovRoutine != null) StopCoroutine(_fovRoutine);
        _fovRoutine = StartCoroutine(FovPulse(10f, skill.activate)); // FOV拉伸=速度感（零新资产）

        while (traveled < skill.dashDistance)
        {
            float step = skill.dashDistance * (Time.deltaTime / Mathf.Max(0.01f, skill.activate));
            Vector3 before = transform.position;
            if (_cc != null) _cc.Move(dir * step);
            float moved = Vector3.Distance(before, transform.position);
            traveled += moved;

            // 撞墙提前停（敌方碰撞已忽略，挡路的只可能是墙）
            if (moved < step * 0.3f)
            {
                if (debugLog) Debug.Log("[SkillExecutor] 冲锋撞墙提前结束");
                break;
            }

            // 路径命中（一次/目标）
            if (skill.damageMultiplier > 0f)
            {
                float dmg = (_combat != null ? _combat.AttackDamage : 10f) * skill.damageMultiplier;
                Collider[] hits = Physics.OverlapSphere(transform.position, skill.dashHitRadius);
                foreach (var hit in hits)
                {
                    if (!hit.CompareTag("Enemy")) continue;
                    var targetHealth = hit.GetComponent<Health>();
                    if (targetHealth == null || targetHealth.IsDead || damaged.Contains(targetHealth)) continue;
                    damaged.Add(targetHealth);
                    targetHealth.TakeDamage(dmg);
                    if (targetHealth.IsDead) NotifyKill();
                    if (debugLog) Debug.Log($"[SkillExecutor] 冲锋命中 {hit.name}，伤害{dmg}，剩余{targetHealth.Current}");
                }
            }
            yield return null;
        }
        AudioManager.PlaySfx("SFX_SwordSwing", transform.position); // 占位音效（专属冲锋音后续生成）
    }

    /// <summary>冲锋开始：忽略与周边敌人的碰撞（突进可穿过怪群；Superarmor霸体怪也可穿过，Demo可接受）</summary>
    private void IgnoreEnemyCollisions()
    {
        if (_cc == null) return;
        _dashIgnored.Clear();
        Collider[] hits = Physics.OverlapSphere(transform.position, 6f); // 覆盖4m突进路径+身位
        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Enemy")) continue;
            foreach (var col in hit.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue; // 触发器本就不挡CC
                Physics.IgnoreCollision(_cc, col, true);
                _dashIgnored.Add(col);
            }
        }
    }

    /// <summary>冲锋结束/打断/死亡统一恢复碰撞（已销毁的跳过，防悬空引用异常）</summary>
    private void RestoreDashCollisions()
    {
        if (_cc == null) { _dashIgnored.Clear(); return; }
        foreach (var col in _dashIgnored)
        {
            if (col != null) Physics.IgnoreCollision(_cc, col, false);
        }
        _dashIgnored.Clear();
    }

    /// <summary>旋风斩表现：环形刀光特效+挥砍音效+第一人称手臂连扫+FOV微脉冲</summary>
    private void PlaySlashFx(SkillData skill)
    {
        // 环形刀光（特效库SwordWhirlwindWhite，与普攻刀光同风格）
        if (!string.IsNullOrEmpty(skill.vfxResourceName))
        {
            var fxPrefab = Resources.Load<GameObject>("Effects/" + skill.vfxResourceName);
            if (fxPrefab != null)
            {
                // 圆弧网格(ETFX_CirclePlane)竖立authored在本地XY平面，靠预制体根部270°X躺平；
                // Instantiate的显式旋转会覆盖预制体根旋转，identity会让刀光立成竖轮正对玩家——必须显式-90°X
                // 环心=腰部：脚底(CC center-half)+45%身高，与玩家缩放/场景无关
                var fxPos = transform.position;
                if (_cc != null)
                    fxPos.y += _cc.center.y - _cc.height * 0.5f + _cc.height * 0.45f;
                var fx = Instantiate(fxPrefab, fxPos, Quaternion.Euler(-90f, 0f, 0f));
                // 存活=技能总时长+0.2s（用户定稿）；本函数在生效起点调用，剩余存活=activate+recovery+0.2
                Destroy(fx, skill.activate + skill.recovery + 0.2f);
            }
            else
            {
                Debug.LogWarning($"[SkillExecutor] 未找到特效 Resources/Effects/{skill.vfxResourceName}");
            }
        }

        AudioManager.PlaySfx("SFX_SwordSwing", transform.position);
        // 手臂连扫：生效期首尾各重播一次横劈（Rebind从头重播是项目唯一可靠复位手法）
        if (_hands != null) _hands.PlayAttack(true);
        if (skill.activate > 0.15f && isActiveAndEnabled)
        {
            StartCoroutine(DelayedSecondSlash(skill.activate * 0.5f));
        }
        if (_fovRoutine != null) StopCoroutine(_fovRoutine);
        _fovRoutine = StartCoroutine(FovPulse(3f, skill.activate)); // 轻微镜头脉冲
    }

    private IEnumerator DelayedSecondSlash(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_hands != null && _phase == CastPhase.Activate) _hands.PlayAttack(true);
    }

    /// <summary>FOV脉冲：基础值→基础+peak→基础，正弦包络（冲锋10=明显速度感，旋风斩3=轻微）</summary>
    private IEnumerator FovPulse(float peak, float duration)
    {
        if (_cam == null || duration <= 0f) yield break;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
            _cam.fieldOfView = _baseFov + peak * k;
            yield return null;
        }
        _cam.fieldOfView = _baseFov;
    }

    // ───────────────────────── 打断 / 死亡 / 被动 ─────────────────────────

    /// <summary>自身受击：前摇/生效期→打断（退还50%消耗）；后摇不受击打断（收招硬直不可逆）</summary>
    private void OnSelfDamaged(float amount)
    {
        if (_phase != CastPhase.Windup && _phase != CastPhase.Activate) return;
        // CleanupCast会清空_casting，退款金额先记录再清理（实测踩坑：先清后读=永远退0）
        float refund = _casting != null ? _casting.staminaCost * 0.5f : 0f;
        if (debugLog) Debug.Log($"[SkillExecutor] {_casting?.displayName} 施法被受击打断（{_phase}），退还{refund}体力");
        CleanupCast();
        if (refund > 0f && _stamina != null) _stamina.Restore(refund);
    }

    /// <summary>自身死亡：强制取消（不退资源）</summary>
    private void OnSelfDeath()
    {
        if (_phase == CastPhase.None) return;
        if (debugLog) Debug.Log("[SkillExecutor] 玩家死亡，施法强制取消");
        CleanupCast();
    }

    /// <summary>
    /// 击杀通知（嗜血被动入口）：PlayerCombat普攻与本组件技能命中处，目标死亡后调用。
    /// 每次击杀独立结算（一次旋风斩杀3个=回3份）；满血/自身死亡/无被动时静默
    /// </summary>
    public void NotifyKill()
    {
        if (_passive == null || _passive.passiveHealOnKill <= 0f) return;
        if (_health == null || _health.IsDead) return;
        if (_health.Current >= _health.Max) return; // 满血不溢出不提示
        _health.Heal(_passive.passiveHealOnKill);
        FloatingText.Show($"+{_passive.passiveHealOnKill}", transform.position + Vector3.up * 1.8f, 1f);
        if (debugLog) Debug.Log($"[SkillExecutor] 嗜血触发：击杀回复{_passive.passiveHealOnKill}HP");
    }
}
