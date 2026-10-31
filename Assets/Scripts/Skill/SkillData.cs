using UnityEngine;

/// <summary>
/// 技能数据卡（ScriptableObject，技能系统机制设计v0.1）
/// ─────────────────────────────────────────
/// 一条技能=一张可配置的数据卡：策划在Inspector填数值、不改代码；
/// 代码侧只有SkillExecutor一个"读卡→跑生命周期"的通用执行器。
/// 加新技能=新建SO资产填字段（若判定方式是已有类型），逻辑零改动。
///
/// 字段对位（设计文档1.1）：
/// - 类型：瞬发（原地生效）/ 位移（自身突进）/ 被动（无按键常驻）
/// - 判定：isMeleeRange=近战范围（以自身为心range半径全向OverlapSphere）
///          位移类固定沿玩家朝向dashDistance直线突进（路径命中检测）
/// - 消耗：Demo统一体力（用户2026-10-21裁定：取消怒气）；后续法系可加资源枚举
/// - 三段时长：windup前摇（可被打断）/ activate生效 / recovery后摇（收招硬直）
/// - 数值：伤害=当前武器伤害（PlayerCombat.AttackDamage口径）×damageMultiplier；
///          被动=击杀回血passiveHealOnKill
/// - 表现：前摇是否可缓慢移动（瞬发×0.5/位移类站定）；特效走Resources/Effects/名；
///          icon为HUD技能槽图标（空则显示名称首字）
/// 资产放Assets/Resources/Skills/（SkillExecutor运行时Resources.Load——
/// 自动补挂组件无法序列化引用资产，走Resources是项目现役模式）
/// </summary>
[CreateAssetMenu(fileName = "Skill_", menuName = "Roguelite/SkillData")]
public class SkillData : ScriptableObject
{
    /// <summary>技能类型：瞬发（原地生效）/ 位移（自身突进）/ 被动（无按键常驻）</summary>
    public enum SkillType
    {
        Instant,    // 瞬发：前摇结束在原地立即生效（旋风斩）
        Dash,       // 位移：生效期=突进本身（冲锋）
        Passive,    // 被动：无按键无生命周期，事件驱动（嗜血）
    }

    [Header("—— 标识 ——")]
    [Tooltip("技能唯一id")]
    public string skillId = "";

    [Tooltip("显示名称（HUD槽位无图标时取首字显示）")]
    public string displayName = "";

    [TextArea(1, 3)]
    [Tooltip("描述（提示用）")]
    public string description = "";

    [Header("—— 类型与判定 ——")]
    [Tooltip("技能类型：瞬发/位移/被动")]
    public SkillType skillType = SkillType.Instant;

    [Tooltip("瞬发判定=近战范围：以自身为心range半径全向命中（Tag=Enemy且带Health）")]
    public float range = 2f;

    [Tooltip("位移突进距离（米），仅Dash类型使用")]
    public float dashDistance = 4f;

    [Tooltip("位移路径命中检测半径（米），仅Dash类型使用")]
    public float dashHitRadius = 1.2f;

    [Header("—— 消耗与冷却 ——")]
    [Tooltip("体力消耗（Demo统一体力，取消怒气）")]
    public float staminaCost = 30f;

    [Tooltip("冷却时长（秒），后摇结束才开始计（设计文档生命周期）")]
    public float cooldown = 6f;

    [Header("—— 三段时长（秒） ——")]
    [Tooltip("前摇：可被受击打断")]
    public float windup = 0.2f;

    [Tooltip("生效：瞬发=判定即时执行后停留时长；位移=突进总时长")]
    public float activate = 0.3f;

    [Tooltip("后摇：收招硬直，不能放技能/普攻，移速×0.5")]
    public float recovery = 0.4f;

    [Header("—— 数值 ——")]
    [Tooltip("伤害倍率（×当前武器伤害；0=无伤害）")]
    public float damageMultiplier = 1f;

    [Tooltip("被动：玩家造成击杀时回复生命（仅Passive类型）")]
    public float passiveHealOnKill = 0f;

    [Header("—— 表现 ——")]
    [Tooltip("前摇期间是否允许缓慢移动（×0.5）；位移类建议false（站定蓄势）")]
    public bool canMoveDuringWindup = true;

    [Tooltip("特效prefab名（Resources/Effects/下，空=无特效），如SwordWhirlwindWhite")]
    public string vfxResourceName = "";

    [Tooltip("HUD技能槽图标（空=显示名称首字）")]
    public Sprite icon;
}
