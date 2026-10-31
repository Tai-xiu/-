using UnityEngine;

/// <summary>
/// 玩家体力组件（HUD规格v0.1组件2数据层，挂在Player上）
/// 消耗/恢复规则（2026-10-09修订定稿）：
/// - 冲刺消耗 15/s：起步需体力≥20（"下一次冲刺需要体力不低于20"），起步后可一路消耗到0才断
/// - 挥砍消耗 12/次：体力不足一次消耗时无法攻击（DoAttack前置门禁，满足则扣12）
/// - 恢复 20/s；收到伤害后2秒内恢复速度降低50%（0.5倍），2秒未再受伤即恢复正常速度（2026-10-09二次修订）
/// UI：HUDController体力条（血条正下方240×10黄条，无数字，<20闪黄）
/// 挂载：PlayerCombat.Awake自动补挂（两场景Player通用，无需场景手动接线）
/// </summary>
[RequireComponent(typeof(Health))]
public class Stamina : MonoBehaviour
{
    [Header("—— 体力设置 ——")]
    [Tooltip("体力上限")]
    [SerializeField] private float maxStamina = 100f;

    [Tooltip("冲刺消耗（每秒）")]
    [SerializeField] private float sprintDrainPerSec = 15f;

    [Tooltip("每次挥砍消耗")]
    [SerializeField] private float swingCost = 12f;

    [Tooltip("起步冲刺所需最低体力（已在冲刺中则可消耗到0才断）")]
    [SerializeField] private float sprintStartThreshold = 20f;

    [Tooltip("恢复速度（每秒）")]
    [SerializeField] private float regenPerSec = 5f;

    [Tooltip("受伤后恢复速度倍率（0.5=降低50%）")]
    [SerializeField] private float hurtRegenMultiplier = 0.5f;

    [Tooltip("受伤降速持续时长（秒），期间再受伤重新计时，超时恢复常速")]
    [SerializeField] private float hurtRegenDuration = 2f;

    private Health _health;
    /// <summary>受伤降速窗口倒计时（受伤置为时长，归零后恢复常速；期间再受伤重新计时）</summary>
    private float _hurtRegenTimer;
    /// <summary>本帧是否发生了冲刺消耗（LateUpdate恢复时排除当帧，消耗与恢复不同帧叠加）</summary>
    private bool _drainedThisFrame;
    /// <summary>冲刺进行中（起步后跌破阈值也可持续消耗，直到体力到底才断）</summary>
    private bool _sprintingActive;

    /// <summary>当前体力（只读，经ConsumeSprintFrame/SpendSwing修改）</summary>
    public float Current { get; private set; }
    /// <summary>体力上限</summary>
    public float Max => maxStamina;
    /// <summary>能否起步冲刺（低于起步阈值禁止；已在冲刺中的持续消耗不走此判定）</summary>
    public bool CanSprint => Current >= sprintStartThreshold;
    /// <summary>能否挥砍（体力不满足一次消耗则无法攻击）</summary>
    public bool CanSwing => Current >= swingCost;

    private void Awake()
    {
        Current = maxStamina; // 初始满体力
    }

    private void OnEnable()
    {
        _health = GetComponent<Health>();
        if (_health != null) _health.onDamaged.AddListener(OnDamaged);
    }

    private void OnDisable()
    {
        if (_health != null) _health.onDamaged.RemoveListener(OnDamaged);
    }

    /// <summary>受伤回调：进入/重置2秒降速窗口（期间再受伤重新计时）</summary>
    private void OnDamaged(float amount)
    {
        _hurtRegenTimer = hurtRegenDuration;
    }

    /// <summary>
    /// 冲刺逐帧请求（PlayerController.Update每帧调用，先于移动速度计算）：
    /// - 起步：未冲刺时需体力≥起步阈值（下一次冲刺需要体力不低于20）
    /// - 持续：已起步则一路消耗直到体力到底（0）才断，不受阈值拦截
    /// 生效时按15/s扣减并返回true；未请求/体力耗尽/起步被拒返回false
    /// </summary>
    public bool ConsumeSprintFrame(bool wantsSprint)
    {
        if (!wantsSprint)
        {
            _sprintingActive = false;
            return false;
        }
        if (Current <= 0f)
        {
            _sprintingActive = false;
            return false; // 体力到底，冲刺中断（恢复到起步阈值后才能再次冲刺）
        }
        if (!_sprintingActive && Current < sprintStartThreshold)
        {
            return false; // 起步被拒：体力低于阈值
        }
        Current = Mathf.Max(0f, Current - sprintDrainPerSec * Time.deltaTime);
        _drainedThisFrame = true;
        _sprintingActive = true;
        return true;
    }

    /// <summary>挥砍消耗（PlayerCombat.DoAttack在CanSwing通过后调用）：扣swingCost</summary>
    /// <summary>挥砍消耗（参数化：按当前武器的体力消耗/击扣减，配置表v0.1）</summary>
    /// <summary>配置挥砍消耗（PlayerCombat按当前装备武器刷新，配置表v0.1）</summary>
    public void ConfigureSwingCost(float cost)
    {
        swingCost = cost;
    }

    public void SpendSwing(float cost)
    {
        Current = Mathf.Max(0f, Current - cost);
    }

    /// <summary>能否挥砍（按指定消耗判定，配置表v0.1）</summary>
    public bool CanSwingCost(float cost)
    {
        return Current >= cost;
    }

    /// <summary>恢复体力（消耗品类物品：能量饮料/药剂，配置表v0.1）</summary>
    public void Restore(float amount)
    {
        Current = Mathf.Min(maxStamina, Current + amount);
    }

    public void SpendSwing()
    {
        Current = Mathf.Max(0f, Current - swingCost);
    }

    private void LateUpdate()
    {
        // 受伤降速窗口倒计时：实时流逝（与是否在冲刺消耗无关），归零后恢复常速
        if (_hurtRegenTimer > 0f)
        {
            _hurtRegenTimer -= Time.deltaTime;
        }

        // 恢复判定放LateUpdate：晚于PlayerController.Update的当帧冲刺请求，时序确定
        if (!_drainedThisFrame && Current < maxStamina)
        {
            float rate = _hurtRegenTimer > 0f ? regenPerSec * hurtRegenMultiplier : regenPerSec;
            Current = Mathf.Min(maxStamina, Current + rate * Time.deltaTime);
        }
        _drainedThisFrame = false; // 帧尾复位，防Controller停调后标志残留卡死恢复
    }
}
