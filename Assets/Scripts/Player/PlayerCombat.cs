using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 玩家战斗组件（第一人称版）
/// 鼠标左键攻击（攻击方向=角色朝向=相机水平朝向）：
/// 1. 左键点击直接以玩家朝向为中轴做扇形判定（attackRange半径、attackAngle角度，均可调），
///    第一人称下朝向即屏幕正前方，无需射线取地面点
/// 2. 满足攻击间隔后出手；只对Tag为Enemy且带Health组件的目标生效
/// 攻击力：最终攻击力 = 基础attackDamage + 背包装备的武器加成（由同物体Inventory组件提供）
/// 攻击表现：第一人称手臂/武器动画（FirstPersonHands，DoAttack触发）+ 挥击音效；
/// 命中反馈：受击目标打击圈 + 命中音效（世界空间刀光已取消，避免与视角内武器动画冲突）
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("—— 攻击设置 ——")]
    [Tooltip("基础攻击力（每次造成的伤害，武器加成另计）")]
    [SerializeField] private float attackDamage = 10f;

    [Tooltip("攻击间隔（秒）")]
    [SerializeField] private float attackInterval = 0.5f;

    [Tooltip("攻击判定半径（米）")]
    [SerializeField] private float attackRange = 2f;

    [Tooltip("攻击扇形总角度（度，以角色朝向为中轴）")]
    [SerializeField, Range(30f, 360f)] private float attackAngle = 120f;

    /// <summary>输入开关（死亡时禁用攻击）</summary>
    private bool _inputEnabled = true;
    /// <summary>攻击冷却计时器</summary>
    private float _attackTimer;
    /// <summary>上次血量快照（受伤音效监听用，-1=未初始化）</summary>
    private float _lastHp = -1f;
    /// <summary>自身生命组件（监听死亡）</summary>
    private Health _health;
    /// <summary>同物体上的PlayerController引用（死亡时联动禁用移动）</summary>
    private PlayerController _playerController;
    /// <summary>同物体上的背包组件（提供武器攻击加成，可为null）</summary>
    private Inventory _inventory;
    /// <summary>第一人称手臂动画（Awake自动补挂，DoAttack时触发拳/剑动作）</summary>
    private FirstPersonHands _hands;
    /// <summary>体力组件（Awake自动补挂，挥砍消耗）</summary>
    private Stamina _stamina;
    /// <summary>技能执行器（Awake自动补挂，Q/E技能；IsCasting期间拦截普攻+击杀通知入口）</summary>
    private SkillExecutor _skills;
    /// <summary>当前攻击间隔（秒）——有武器=1/攻速，无武器=默认0.5</summary>
    private float _curInterval = 0.5f;
    /// <summary>当前挥砍体力消耗——有武器=武器体力消耗/击，无武器=默认12</summary>
    private float _curSwingCost = 12f;

    /// <summary>
    /// 当前攻击力 = 基础攻击力 + 背包已装备武器的攻击加成（实时生效）
    /// </summary>
    public float AttackDamage
    {
        get
        {
            var w = _inventory != null ? _inventory.EquippedWeapon : null;
            return w != null ? w.attackPower : attackDamage; // 配置表v0.1：普攻伤害=当前武器伤害
        }
    }

    private void Awake()
    {
        _health = GetComponent<Health>();
        _playerController = GetComponent<PlayerController>();
        _inventory = GetComponent<Inventory>();
        // 第一人称手臂动画：自动补挂（两场景Player通用，无需场景手动接线）
        _hands = GetComponent<FirstPersonHands>();
        if (_hands == null)
        {
            _hands = gameObject.AddComponent<FirstPersonHands>();
        }
        // 体力组件：自动补挂（HUD组件2数据层，两场景Player通用零接线）
        _stamina = GetComponent<Stamina>();
        if (_stamina == null)
        {
            _stamina = gameObject.AddComponent<Stamina>();
        }
        // 技能执行器：自动补挂（技能系统v0.1，两场景Player通用零接线）
        _skills = GetComponent<SkillExecutor>();
        if (_skills == null)
        {
            _skills = gameObject.AddComponent<SkillExecutor>();
        }
    }

    private void Start()
    {
        // 监听自身死亡：禁用移动与攻击输入（死亡结算留待后续步骤，此处仅留接口）
        if (_health != null)
        {
            _health.onDeath.AddListener(OnPlayerDeath);
        // 装备变化→刷新攻击间隔/挥砍消耗（配置表v0.1）
        if (_inventory != null)
        {
            RefreshWeaponStats();
            _inventory.Changed += RefreshWeaponStats;
        }
        }
    }

    private void OnDestroy()
    {
        // 移除监听，防止对象销毁后回调悬空
        if (_health != null)
        {
            _health.onDeath.RemoveListener(OnPlayerDeath);
        if (_inventory != null) _inventory.Changed -= RefreshWeaponStats;
        }
    }

    private void Update()
    {
        // 受伤音效：监听血量下降（与输入冻结无关，面板打开期间被打仍应出声）
        if (_health != null)
        {
            if (_lastHp >= 0f && _health.Current < _lastHp - 0.01f)
            {
                AudioManager.PlaySfx("SFX_Hurt", transform.position);
            }
            _lastHp = _health.Current;
        }

        if (!_inputEnabled) return;

        // 打开背包/商店等面板时冻结攻击输入（第一人称下亦无需鼠标取地面点）
        if (GameUIState.IsAnyPanelOpen) return;

        // 技能施法中（前摇/生效/后摇）拦截普攻：后摇结束前不能出手，杜绝技能接普攻抢节奏（设计文档四）
        if (_skills != null && _skills.IsCasting) return;

        // 冷却计时
        _attackTimer += Time.deltaTime;
        if (Input.GetMouseButtonDown(0))
        {
            HandleClickAttack();
        }
    }

    /// <summary>
    /// 左键点击处理（第一人称）：以玩家朝向（相机水平朝向）为中轴直接判定，无需射线取点；
    /// 光标点在UI上时跳过（防点UI误出手）
    /// </summary>
    private void HandleClickAttack()
    {
        // 鼠标点在任一UI元素上（按钮/面板等）时不触发攻击——
        // 防止点击UI界面时角色同时转身/出手（隐藏Bug修复）
        var es = EventSystem.current;
        if (es != null && es.IsPointerOverGameObject()) return;

        // 满足攻击间隔才出手，攻击扇形以玩家正方向（屏幕正前方）判定
        if (_attackTimer >= _curInterval)
        {
            _attackTimer = 0f;
            DoAttack();
        }
    }

    /// <summary>
    /// 执行一次攻击：球形检测 → 扇形过滤 → 对敌人造成伤害 → 播放刀光
    /// </summary>
    private void DoAttack()
    {
        // 技能施法中（前摇/生效/后摇）不可普攻——守卫下沉到DoAttack入口，
        // 与Update层输入拦截双保险（设计文档：Casting覆盖攻击优先级，后摇结束前不能出手）
        if (_skills != null && _skills.IsCasting)
        {
            return;
        }

        // 体力门槛：体力不满足一次挥砍消耗时无法攻击（2026-10-09修订，原"不足清零仍可攻击"取消）
        if (_stamina != null && !_stamina.CanSwingCost(_curSwingCost))
        {
            return;
        }

        // 挥砍消耗体力（HUD规格组件2：12/次）
        if (_stamina != null) _stamina.SpendSwing(_curSwingCost);

        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;

        // 以自身为圆心的球形检测，再手动做扇形角度过滤
        Collider[] hits = Physics.OverlapSphere(origin, attackRange);
        bool anyHit = false;
        foreach (Collider hit in hits)
        {
            // 只处理Tag为Enemy的对象
            if (!hit.CompareTag("Enemy")) continue;

            // 必须带Health组件
            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth == null) continue;

            // 扇形判定：目标方向与角色朝向的夹角需在attackAngle的一半以内
            Vector3 toTarget = hit.transform.position - origin;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) continue; // 与自身位置重合时跳过
            if (Vector3.Angle(forward, toTarget.normalized) > attackAngle * 0.5f) continue;

            targetHealth.TakeDamage(AttackDamage);
            anyHit = true;

            // 击杀归因：玩家普攻造成的击杀通知嗜血被动（技能命中侧的通知在SkillExecutor内）
            if (targetHealth.IsDead && _skills != null)
            {
                _skills.NotifyKill();
            }

            // 命中反馈特效：红色打击圈在受击目标位置（特效库加载，自动播放后销毁）
            var hitFx = Resources.Load<GameObject>("Effects/RoundHitRed");
            if (hitFx != null)
            {
                var fx = Instantiate(hitFx, hit.transform.position + Vector3.up * 0.6f, Quaternion.identity);
                Destroy(fx, 1.2f);
            }

            Debug.Log("[PlayerCombat] 命中 " + hit.name + "，造成 " + AttackDamage
                + " 伤害，剩余 " + targetHealth.Current + "/" + targetHealth.Max
                + (targetHealth.IsDead ? "（目标已死亡）" : ""));
        }

        // 命中音效：本次攻击至少命中一个目标才播（3D定位在玩家处）
        if (anyHit)
        {
            AudioManager.PlaySfx("SFX_HitImpact", origin);
        }

        // 挥击音效（3D，跟随玩家位置）；视觉刀光已取消（第一人称武器动画替代）
        AudioManager.PlaySfx("SFX_SwordSwing", transform.position);

        // 第一人称手臂动画：有武器=持剑横劈，无武器=出拳（纯视觉，与判定无关）
        if (_hands != null)
        {
            _hands.PlayAttack(_inventory != null && _inventory.EquippedWeapon != null);
        }
    }

    /// <summary>
    /// 外部开关攻击输入（死亡时由死亡流程调用）
    /// </summary>
    public void SetInputEnabled(bool value)
    {
        _inputEnabled = value;
    }

    /// <summary>
    /// 玩家死亡回调：禁用移动与攻击输入，输出日志（死亡结算将在后续步骤实现）
    /// </summary>
    private void OnPlayerDeath()
    {
        SetInputEnabled(false);
        if (_playerController != null)
        {
            _playerController.SetInputEnabled(false);
        }
        Debug.Log("[PlayerCombat] 玩家已死亡：移动与攻击输入已禁用（死亡结算将在后续步骤实现）");
    }
    /// <summary>按当前装备武器刷新攻击间隔/挥砍消耗（配置表v0.1：间隔=1/攻速，消耗=武器体力/击）</summary>
    private void RefreshWeaponStats()
    {
        var w = _inventory != null ? _inventory.EquippedWeapon : null;
        if (w != null && w.attackSpeed > 0f)
        {
            _curInterval = 1f / w.attackSpeed;
            _curSwingCost = w.staminaCostPerSwing;
        }
        else
        {
            _curInterval = attackInterval; // 拳头：默认间隔
            _curSwingCost = 12f;           // 拳头：默认消耗
        }
        if (_stamina != null) _stamina.ConfigureSwingCost(_curSwingCost);
    }
}

