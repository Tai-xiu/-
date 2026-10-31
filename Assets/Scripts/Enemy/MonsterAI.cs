using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 基础怪物AI（基础怪物设计配置表·通用状态机，哥布林为基准默认行为）
/// 状态流转（配置表通用状态机）：
///   Patrol巡逻（出生点patrolRadius内随机游走，速度50%）
///   → Alert发现（玩家进入仇恨范围：原地转向玩家0.5s，头顶红色"!"淡出+发现音效）
///   → Chase追击（脱战：玩家距离>脱战距离持续脱战时间 → Return回归）
///   → Attack攻击（抬手前摇可读：变色警示+武器举起，前摇后0.25s线性加速突进0.5m贴近玩家
///     （贴近到0.9m即停，不拖拽不顶撞）→ 判定窗0.1s快速劈下并命中 → 恢复至攻击间隔走满；
///     前摇中玩家远离1.5m以上=走位躲开，放弃本次转追击）
///   Hurt受击（覆盖层非独立状态：受击白闪+被击退hurtKnockback+打断抬手；霸体怪物重写Superarmor）
///   Dead死亡（HP≤0：倒地0.5s躺平+死亡音效，尸体常驻不消失不阻挡，暂无掉落）
///   Return回归（走回出生点，途中玩家再进仇恨范围会重新发现——"!"+音效重新播报）
/// 数值=哥布林基准（配置表：攻8/速2.8/前摇0.6/间隔1.8/距离1.8/仇恨8/脱战15m·5s；
/// 玩家受击无击退——2026-10-10用户裁定）
/// 伤害通路：玩家攻击按Tag=Enemy+Health扇形判定（PlayerCombat通用）；攻击玩家走其Health
/// （护甲DamageModifier自动生效）；后续怪物（骷髅/魔狼/大型哥布林）继承本类重写差异状态
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(Health))]
public class MonsterAI : MonoBehaviour
{
    /// <summary>怪物状态（Hurt为受击覆盖层，不独占状态机）</summary>
    public enum State { Patrol, Alert, Chase, Attack, Dead, Return }

    [Header("—— 侦测与移动（哥布林基准值） ——")]
    [Tooltip("追击/回归移动速度（米/秒）")]
    [SerializeField] protected float moveSpeed = 2.8f;
    [Tooltip("巡逻半径（米）：围绕出生点随机游走")]
    [SerializeField] protected float patrolRadius = 5f;
    [Tooltip("巡逻速度系数（相对追击速度，配置表=50%）")]
    [SerializeField, Range(0.1f, 1f)] protected float patrolSpeedFactor = 0.5f;
    [Tooltip("仇恨范围（米）：玩家进入即发现")]
    [SerializeField] protected float aggroRadius = 8f;
    [Tooltip("脱战距离（米）：玩家超出该距离开始累计脱战")]
    [SerializeField] protected float leashRadius = 15f;
    [Tooltip("脱战时间（秒）：持续超出脱战距离该时长后回归")]
    [SerializeField] protected float leashTime = 5f;

    [Header("—— 攻击（哥布林基准值） ——")]
    [Tooltip("攻击距离（米）：玩家进入后停下攻击")]
    [SerializeField] protected float attackRange = 1.8f;
    [Tooltip("攻击力（伤害玩家的数值，玩家护甲在其Health侧修正）")]
    [SerializeField] protected float attackDamage = 8f;
    [Tooltip("攻击抬手前摇（秒）：抬手期间变警示色，走位躲开有收益")]
    [SerializeField] protected float attackWindup = 0.6f;
    [Tooltip("攻击间隔（秒）：含抬手与恢复的完整周期")]
    [SerializeField] protected float attackInterval = 1.8f;
    [Tooltip("判定窗时长（秒）：抬手结束后判定框生效的时长，窗内命中一次")]
    [SerializeField] protected float strikeWindow = 0.1f;

    [Header("—— 受击反馈 ——")]
    [Tooltip("受击白闪时长（秒）")]
    [SerializeField] protected float hurtFlashDuration = 0.15f;
    [Tooltip("被击退距离（米，远离伤害源；霸体怪物重写Superarmor后仅白闪）")]
    [SerializeField] protected float hurtKnockback = 0.5f;

    [Header("—— 攻击突进 ——")]
    [Tooltip("突进总距离（米）：前摇结束后0.25s内线性加速推进（非瞬移），防玩家在攻击距离边缘反复拉扯")]
    [SerializeField] protected float attackLungeDistance = 0.5f;
    [Tooltip("突进时长（秒）：线性加速（初速0递增到峰值），总位移=attackLungeDistance")]
    [SerializeField] protected float lungeDuration = 0.25f;
    [Tooltip("突进停止距离（米）：推进中与玩家贴近到该距离即停，防止突进拖拽/顶撞玩家")]
    [SerializeField] protected float lungeStopDistance = 0.9f;

    [Header("—— 表现 ——")]
    [Tooltip("发现状态时长（秒）：原地转向玩家")]
    [SerializeField] protected float alertDuration = 0.5f;
    [Tooltip("发现音效（暂用UI点击音占位，待专属音效资产）")]
    [SerializeField] protected string alertSfx = "SFX_UIClick";
    [Tooltip("死亡音效（暂用受伤音占位，待专属音效资产）")]
    [SerializeField] protected string deathSfx = "SFX_Hurt";
    [Tooltip("身体基础色（哥布林绿；白闪/抬手警示色在此基础上过渡）")]
    [SerializeField] protected Color bodyColor = new Color(0.35f, 0.75f, 0.35f, 1f);
    [Tooltip("抬手警示色（前摇期间身体过渡到该色，提高抬手可读性）")]
    [SerializeField] protected Color windupColor = new Color(0.95f, 0.55f, 0.1f, 1f);
    [Tooltip("武器Transform（右手侧Cube，可选接线）：前摇举起→判定窗快速劈下→其余回位；不接线无武器动画")]
    [SerializeField] protected Transform weapon;

    /// <summary>当前状态（公开只读，调试与测试用）</summary>
    public State CurrentState { get; private set; } = State.Patrol;

    protected NavMeshAgent Agent;
    protected Health SelfHealth;
    protected Transform Player;
    protected Health PlayerHealth;
    protected Vector3 SpawnPos;
    protected Renderer BodyRenderer;
    protected Material BodyMat;      // 实例化材质（白闪/警示色不影响其它怪物与资产）
    protected Color BaseColor;

    // 运行时计时与标记
    protected float _alertTimer;         // Alert停留计时
    protected float _attackTimer;        // Attack内推进（前摇→判定窗→恢复由其分段）
    protected bool _strikeHit;           // 本轮判定窗是否已命中（窗内只结算一次）
    protected float _leashTimer;         // 脱战累计
    protected float _flash;              // 白闪剩余
    protected float _patrolRepathTimer;  // 巡逻换点计时
    protected bool _patrolHasTarget;     // 是否已有巡逻目标
    protected bool _alertPlayed;         // 本次敌对期间"!"+音效只播一次（回归Patrol后复位）
    protected GameObject _alertMark;     // 头顶红色"!"（3D文本，billboard+淡出）
    protected TextMesh _alertText;
    protected float _alertMarkFade;      // "!"淡出剩余
    protected Quaternion _weaponRestRot; // 武器静止姿态（Awake捕获，前摇/劈下相对其偏转）
    protected bool _weaponRestCaptured;  // 武器静止姿态是否已捕获

    protected virtual void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        SelfHealth = GetComponent<Health>();
        Agent.speed = moveSpeed;
        Agent.stoppingDistance = attackRange * 0.8f; // 略近于攻击距离停稳，确保进入判定范围

        // 头顶血条（复用白盒阶段组件：死亡自动隐藏，玩家靠近淡入）
        if (GetComponent<EnemyHealthBar>() == null) gameObject.AddComponent<EnemyHealthBar>();

        // 身体材质实例化：白闪/警示色只影响自身
        BodyRenderer = GetComponentInChildren<Renderer>();
        if (BodyRenderer != null)
        {
            BodyMat = BodyRenderer.material;
            BaseColor = bodyColor;
            BodyMat.color = BaseColor;
        }

        BuildAlertMark();

        // 捕获武器静止姿态（前摇举起/劈下相对其偏转）
        if (weapon != null)
        {
            _weaponRestRot = weapon.localRotation;
            _weaponRestCaptured = true;
        }
    }

    protected virtual void Start()
    {
        SpawnPos = transform.position;
        var playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            Player = playerObj.transform;
            PlayerHealth = playerObj.GetComponent<Health>();
        }
        SelfHealth.onDamaged.AddListener(OnSelfDamaged);
        SelfHealth.onDeath.AddListener(OnDeath);
    }

    protected virtual void OnDestroy()
    {
        if (SelfHealth != null)
        {
            SelfHealth.onDamaged.RemoveListener(OnSelfDamaged);
            SelfHealth.onDeath.RemoveListener(OnDeath);
        }
    }

    protected virtual void Update()
    {
        if (CurrentState == State.Dead) return; // 尸体常驻：死亡后不再有任何逻辑

        switch (CurrentState)
        {
            case State.Patrol: UpdatePatrol(); break;
            case State.Alert: UpdateAlert(); break;
            case State.Chase: UpdateChase(); break;
            case State.Attack: UpdateAttack(); break;
            case State.Return: UpdateReturn(); break;
        }

        UpdateBodyColor();
        UpdateWeapon();
        UpdateAlertMark();
    }

    // ===== 状态实现 =====

    /// <summary>巡逻：出生点半径内随机游走（50%速度），玩家进入仇恨范围 → 发现</summary>
    protected virtual void UpdatePatrol()
    {
        Agent.speed = moveSpeed * patrolSpeedFactor;
        if (IsPlayerAlive() && DistanceToPlayer() <= aggroRadius)
        {
            EnterAlert();
            return;
        }

        _patrolRepathTimer -= Time.deltaTime;
        if (!_patrolHasTarget || _patrolRepathTimer <= 0f
            || Agent.remainingDistance <= Agent.stoppingDistance + 0.1f)
        {
            var rand = SpawnPos + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f))
                .normalized * Random.Range(1f, patrolRadius);
            if (NavMesh.SamplePosition(rand, out var hit, 2f, NavMesh.AllAreas))
            {
                Agent.SetDestination(hit.position);
                _patrolHasTarget = true;
                _patrolRepathTimer = Random.Range(2.5f, 4.5f);
            }
        }
    }

    /// <summary>发现：原地转向玩家alertDuration秒后进入追击（"!"+音效进入时已播）</summary>
    protected virtual void UpdateAlert()
    {
        Agent.isStopped = true;
        FacePlayer(12f);
        _alertTimer += Time.deltaTime;
        if (_alertTimer >= alertDuration)
        {
            EnterChase();
        }
    }

    /// <summary>追击：直追玩家；进入攻击距离→攻击；脱战或玩家死亡→回归</summary>
    protected virtual void UpdateChase()
    {
        Agent.speed = moveSpeed;
        if (!IsPlayerAlive())
        {
            EnterReturn();
            return;
        }
        if (AccumulateLeash()) return;

        Agent.SetDestination(Player.position);
        if (DistanceToPlayer() <= attackRange)
        {
            EnterAttack();
        }
    }

    /// <summary>
    /// 攻击：抬手前摇（警示色可读）→ 突进段lungeDuration秒（线性加速推进attackLungeDistance，
    /// 与玩家贴近到lungeStopDistance即停——防止突进拖拽/顶撞玩家）→ 判定窗0.1s（玩家在
    /// 范围+0.3m余量内命中一次）→ 恢复至攻击间隔走满回Chase（间隔=完整周期）
    /// 抬手中玩家远离attackRange+1.5m以上=走位躲开，放弃本次转追击
    /// </summary>
    protected virtual void UpdateAttack()
    {
        if (!IsPlayerAlive())
        {
            EnterReturn();
            return;
        }
        if (AccumulateLeash()) return;

        FacePlayer(10f);
        Agent.isStopped = true;
        _attackTimer += Time.deltaTime;

        float lungeEnd = attackWindup + lungeDuration;

        if (_attackTimer < attackWindup)
        {
            // 抬手中玩家拉开1.5m以上：走位躲开本次攻击
            if (DistanceToPlayer() > attackRange + 1.5f)
            {
                EnterChase();
            }
            return;
        }

        if (_attackTimer < lungeEnd)
        {
            // 突进段：线性加速推进（步长∝已过相位k），贴近玩家到lungeStopDistance即停
            if (Agent != null && Agent.enabled && attackLungeDistance > 0f
                && DistanceToPlayer() > lungeStopDistance)
            {
                float k = Mathf.Clamp01((_attackTimer - attackWindup) / lungeDuration);
                float step = attackLungeDistance * (2f * k) * (Time.deltaTime / lungeDuration);
                var fwd = transform.forward;
                fwd.y = 0f;
                if (step > 0f && fwd.sqrMagnitude > 0.001f)
                {
                    Agent.Move(fwd.normalized * step);
                }
            }
            return;
        }

        if (_attackTimer < lungeEnd + strikeWindow)
        {
            if (!_strikeHit && DistanceToPlayer() <= attackRange + 0.3f)
            {
                _strikeHit = true;
                PerformStrike();
            }
            return;
        }
        // 恢复期：面向玩家站桩至攻击间隔走满再回Chase（间隔=完整周期，含前摇与恢复；
        // 此前窗末直接回Chase导致实际攻击节奏=前摇+窗≈0.7s，间隔1.8s形同虚设——实测修正）
        if (_attackTimer < attackInterval)
        {
            return;
        }
        EnterChase();
    }

    /// <summary>回归：走回出生点；途中玩家再进仇恨范围会重新发现；抵达 → 巡逻。
    /// 脱战即复位"!"播报标记：再次发现时重新淡出播报（用户要求）</summary>
    protected virtual void UpdateReturn()
    {
        Agent.speed = moveSpeed;
        if (IsPlayerAlive() && DistanceToPlayer() <= aggroRadius)
        {
            EnterAlert();
            return;
        }
        if (Agent.remainingDistance <= 0.4f)
        {
            EnterPatrol();
        }
    }

    /// <summary>
    /// 攻击命中结算（判定窗内命中一次）：伤害走玩家Health（护甲修正自动生效）。
    /// 玩家受击无击退（2026-10-10用户裁定取消）。
    /// 骷髅（弹体）/魔狼（扑咬）/大型哥布林（扇形横扫）继承重写本方法替换攻击方式
    /// </summary>
    protected virtual void PerformStrike()
    {
        if (PlayerHealth == null) return;
        PlayerHealth.TakeDamage(attackDamage);
    }

    // ===== 状态进入 =====

    protected virtual void EnterPatrol()
    {
        CurrentState = State.Patrol;
        _alertPlayed = false;      // 回到巡逻后下次发现重新播"!"+音效
        _patrolHasTarget = false;  // 重新选巡逻点
        Agent.isStopped = false;
    }

    protected virtual void EnterAlert()
    {
        CurrentState = State.Alert;
        _alertTimer = 0f;
        Agent.isStopped = true;
        if (!_alertPlayed)
        {
            _alertPlayed = true;
            ShowAlertMark();
            AudioManager.PlaySfx(alertSfx, transform.position);
        }
    }

    protected virtual void EnterChase()
    {
        CurrentState = State.Chase;
        Agent.isStopped = false;
        _leashTimer = 0f;
    }

    protected virtual void EnterAttack()
    {
        CurrentState = State.Attack;
        _attackTimer = 0f;
        _strikeHit = false;
        Agent.isStopped = true;
    }

    protected virtual void EnterReturn()
    {
        CurrentState = State.Return;
        _leashTimer = 0f;
        _alertPlayed = false; // 脱战复位：回归途中/再次发现玩家时"!"+音效重新播报
        Agent.isStopped = false;
        Agent.SetDestination(SpawnPos);
    }

    // ===== 受击与死亡 =====

    /// <summary>是否霸体（大型哥布林重写为true：受击仅白闪不掉动作不被击退）</summary>
    protected virtual bool Superarmor { get { return false; } }

    /// <summary>受击（Health.onDamaged）：白闪；非霸体=打断攻击抬手+被击退远离伤害源</summary>
    protected virtual void OnSelfDamaged(float amount)
    {
        if (CurrentState == State.Dead) return;
        _flash = hurtFlashDuration;
        if (Superarmor) return;
        if (CurrentState == State.Attack)
        {
            _attackTimer = 0f;   // 打断抬手/突进：需重新前摇（走位/连打有收益）
            _strikeHit = false;
        }
        if (Player != null && Agent != null && Agent.enabled && hurtKnockback > 0f)
        {
            var away = transform.position - Player.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.001f)
            {
                Agent.Move(away.normalized * hurtKnockback);
            }
        }
    }

    /// <summary>死亡（Health.onDeath，唯一入口）：停止寻路+关闭碰撞（尸体不阻挡）→倒地0.5s躺平。
    /// 尸体常驻不消失（用户要求），暂无死亡掉落（配置表掉落后续版本接入）</summary>
    [Header("—— 掉落（配置表v0.1） ——")]
    [Tooltip("怪物掉落表（死亡后尸体可搜索，长按F按表roll掉落）")]
    [SerializeField] private MonsterLootTable lootTable;

    [Header("—— 悬赏契约 ——")]
    [Tooltip("怪物类型（悬赏击杀判定用；None=不参与击杀契约）")]
    [SerializeField] private MonsterType monsterType = MonsterType.None;

    protected virtual void OnDeath()
    {
        if (CurrentState == State.Dead) return;
        CurrentState = State.Dead;
        // 悬赏契约：任意方式击杀都计数（拍板口径，死亡即报）
        QuestManager.NotifyKill(monsterType);
        if (Agent != null) Agent.enabled = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        if (_alertMark != null) _alertMark.SetActive(false);
        AudioManager.PlaySfx(deathSfx, transform.position);
        StartCoroutine(DeathFallRoutine());
        // 配置表v0.1：尸体可搜索——挂搜索组件（准星瞄准+长按F，按掉落表roll）
        if (lootTable != null)
        {
            var corpse = gameObject.AddComponent<SearchableCorpse>();
            corpse.LootTable = lootTable;
        }
    }

    /// <summary>倒地表现：0.5s内绕自身前向轴倒平并贴地，之后永久保持（尸体不消失）</summary>
    private IEnumerator DeathFallRoutine()
    {
        Quaternion rotFrom = transform.rotation;
        Quaternion rotTo = Quaternion.Euler(-90f, rotFrom.eulerAngles.y, 0f);
        Vector3 posFrom = transform.position;
        Vector3 posTo = new Vector3(posFrom.x, 0.35f, posFrom.z); // 躺平后胶囊中心贴地高度
        float t = 0f;
        while (t < 0.5f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / 0.5f);
            transform.rotation = Quaternion.Slerp(rotFrom, rotTo, k);
            transform.position = Vector3.Lerp(posFrom, posTo, k);
            yield return null;
        }
        transform.rotation = rotTo;
        transform.position = posTo;
    }

    // ===== 表现辅助 =====

    /// <summary>身体颜色：受击白闪 > 抬手警示色 > 基础色（快速过渡保持可读）</summary>
    protected virtual void UpdateBodyColor()
    {
        if (BodyMat == null) return;
        Color target = BaseColor;
        if (_flash > 0f)
        {
            _flash -= Time.deltaTime;
            target = Color.white;
        }
        else if (CurrentState == State.Attack && _attackTimer < attackWindup)
        {
            target = windupColor;
        }
        BodyMat.color = Color.Lerp(BodyMat.color, target, 20f * Time.deltaTime);
    }

    /// <summary>
    /// 武器前摇动画（抬手可读性）：前摇期举起（绕局部X轴后仰至-60°）→ 突进段保持高举 →
    /// 判定窗快速劈下（至+45°）→ 其余时间回静止位；受击打断抬手后自然归位重举。
    /// 武器未接线（weapon为空）时无动画。骷髅等远程怪重写为各自的举弓/射击动画
    /// </summary>
    protected virtual void UpdateWeapon()
    {
        if (weapon == null) return;
        if (!_weaponRestCaptured)
        {
            _weaponRestRot = weapon.localRotation;
            _weaponRestCaptured = true;
        }

        float lungeEnd = attackWindup + lungeDuration;
        Quaternion target;
        if (CurrentState == State.Attack && _attackTimer < attackWindup)
        {
            float k = Mathf.Clamp01(_attackTimer / attackWindup);
            target = _weaponRestRot * Quaternion.Euler(-60f * k, 0f, 0f); // 举棒后仰（前摇）
        }
        else if (CurrentState == State.Attack && _attackTimer < lungeEnd)
        {
            target = _weaponRestRot * Quaternion.Euler(-60f, 0f, 0f); // 突进段保持高举
        }
        else if (CurrentState == State.Attack && _attackTimer < lungeEnd + strikeWindow)
        {
            float k = Mathf.Clamp01((_attackTimer - lungeEnd) / strikeWindow);
            target = _weaponRestRot * Quaternion.Euler(-60f + 105f * k, 0f, 0f); // 快速劈下至+45°
        }
        else
        {
            target = _weaponRestRot; // 回位（受击打断后自然归位）
        }
        weapon.localRotation = Quaternion.Slerp(weapon.localRotation, target, 25f * Time.deltaTime);
    }

    /// <summary>头顶红色"!"：发现时显示，0.8s淡出后隐藏；存活期间始终朝向相机</summary>
    protected virtual void UpdateAlertMark()
    {
        if (_alertMark == null || !_alertMark.activeSelf) return;
        if (_alertMarkFade > 0f)
        {
            _alertMarkFade -= Time.deltaTime;
            if (_alertText != null)
            {
                var c = _alertText.color;
                c.a = Mathf.Clamp01(_alertMarkFade / 0.8f);
                _alertText.color = c;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                _alertMark.transform.rotation = Quaternion.LookRotation(
                    _alertMark.transform.position - cam.transform.position);
            }
        }
        else
        {
            _alertMark.SetActive(false);
        }
    }

    /// <summary>构建头顶"!"（3D文本，世界空间，红色，随本体移动）</summary>
    private void BuildAlertMark()
    {
        var go = new GameObject("AlertMark", typeof(TextMesh));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var tm = go.GetComponent<TextMesh>();
        tm.text = "!";
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.fontSize = 64;
        tm.characterSize = 0.35f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(0.95f, 0.15f, 0.1f, 1f);
        var mr = go.GetComponent<MeshRenderer>();
        // TextMesh需把字体材质指给渲染器（内置字体材质自带贴图）
        if (tm.font != null) mr.sharedMaterial = tm.font.material;
        go.SetActive(false);
        _alertMark = go;
        _alertText = tm;
    }

    /// <summary>显示"!"并开始淡出</summary>
    protected void ShowAlertMark()
    {
        if (_alertMark == null) return;
        _alertMark.SetActive(true);
        if (_alertText != null)
        {
            var c = _alertText.color;
            c.a = 1f;
            _alertText.color = c;
        }
        _alertMarkFade = 0.8f;
    }

    // ===== 通用工具 =====

    /// <summary>脱战累计：玩家距离>脱战距离时累计计时，达脱战时间 → 回归（返回true表示已切入回归）</summary>
    protected bool AccumulateLeash()
    {
        if (DistanceToPlayer() > leashRadius)
        {
            _leashTimer += Time.deltaTime;
            if (_leashTimer >= leashTime)
            {
                EnterReturn();
                return true;
            }
        }
        else
        {
            _leashTimer = 0f;
        }
        return false;
    }

    /// <summary>平滑面向玩家（水平面）</summary>
    protected void FacePlayer(float turnSpeed)
    {
        if (Player == null) return;
        var toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(toPlayer.normalized, Vector3.up), turnSpeed * Time.deltaTime);
    }

    /// <summary>与玩家的水平距离</summary>
    protected float DistanceToPlayer()
    {
        if (Player == null) return float.MaxValue;
        var d = Player.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    /// <summary>玩家是否存在且存活</summary>
    protected bool IsPlayerAlive()
    {
        return Player != null && PlayerHealth != null && !PlayerHealth.IsDead;
    }
}
