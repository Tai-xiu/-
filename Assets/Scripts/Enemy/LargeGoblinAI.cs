using UnityEngine;

/// <summary>
/// 大型哥布林（精英怪，基础怪物设计配置表第四怪）——继承MonsterAI通用状态机，差异：
/// - 霸体：重写Superarmor=true（受击仅白闪，攻击循环不中断、不被击退不掉动作）
/// - 扇形横扫：重写PerformStrike——触及2.5m内且在身前120°弧内的玩家才命中（攻25），
///   侧向走位躲开弧线即无伤（追击转向慢10°/s，绕背收益高）
/// - 重型站桩：attackLungeDistance=0取消突进（大体型原地横扫，前摇1.2s长抬手可读，
///   间隔3.0s走满才追击——拉扯窗口大但一次挨25很痛，教玩家读前摇绕侧背）
/// - 武器：木质大棒（场景接线子物体），重写UpdateWeapon为横向挥扫动画
///   （前摇向右举棒→判定窗快速横扫到左侧→回位），配合警示色提高可读性
/// - 数值：HP120（场景Health）/攻25/速2.4/前摇1.2/间隔3.0/触及2.5/仇恨10/脱战20m·8s
/// 模型：苔绿胶囊放大（高1.4m=哥布林两倍）+同族深色，场景实例手动/脚本摆位
/// </summary>
public class LargeGoblinAI : MonsterAI
{
    [Header("—— 大型哥布林专属 ——")]
    [Tooltip("横扫半角（度）：身前±60°=120°弧内命中")]
    [SerializeField] private float sweepHalfAngle = 60f;
    [Tooltip("横扫举棒侧摆角（度）：前摇向右举到此角，判定窗扫到左侧对称角（表现用）")]
    [SerializeField] private float sweepVisualYaw = 70f;

    /// <summary>霸体（用户规格：大型哥布林受击仅白闪，不掉动作不被击退不被打断）</summary>
    protected override bool Superarmor { get { return true; } }

    protected override void Awake()
    {
        // 配置表数值覆盖（在base.Awake使用前赋值；Inspector可再调）
        moveSpeed = 2.4f;
        aggroRadius = 10f;
        leashRadius = 20f;
        leashTime = 8f;
        attackRange = 2.5f;      // 横扫触及2.5m
        attackDamage = 25f;
        attackWindup = 1.2f;     // 长前摇：大体型抬棒可读，走位绕侧背有收益
        attackInterval = 3.0f;   // 重击长间隔（完整周期）
        strikeWindow = 0.15f;    // 横扫判定窗略长于轻击
        attackLungeDistance = 0f; // 重型站桩：无突进（原地横扫，距离边缘可走位躲开）
        bodyColor = new Color(0.16f, 0.42f, 0.20f, 1f); // 深苔绿（比普通哥布林暗，体型两倍）
        windupColor = new Color(0.90f, 0.25f, 0.12f, 1f); // 深红警示（精英级危险信号）
        base.Awake();
    }

    /// <summary>
    /// 扇形横扫结算（判定窗内一次）：触及2.5m+0.3余量 且 玩家在身前±60°弧内才命中。
    /// 弧外（侧/后方）挥空——追击转向仅10°/s，绕背走位是核心解法。
    /// 注意：基类判定窗在调用前已置_strikeHit=true，故弧外挥空同样消耗本轮攻击（一挥一次）
    /// </summary>
    protected override void PerformStrike()
    {
        if (PlayerHealth == null || Player == null) return;
        var to = Player.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.001f) return; // 贴身重叠：视为正前方命中
        var fwd = transform.forward;
        fwd.y = 0f;
        if (Vector3.Angle(fwd, to) > sweepHalfAngle) return; // 弧外：挥空无伤
        PlayerHealth.TakeDamage(attackDamage);
        AudioManager.PlaySfx("SFX_HitImpact", transform.position);
    }

    /// <summary>
    /// 横扫武器动画（重写竖劈）：前摇向右举棒（+70°偏航可读蓄力）→ 判定窗快速横扫到
    /// 左侧对称角（一扫跨~140°）→ 其余时间回正。武器未接线无动画
    /// </summary>
    protected override void UpdateWeapon()
    {
        if (weapon == null) return;
        if (!_weaponRestCaptured)
        {
            _weaponRestRot = weapon.localRotation;
            _weaponRestCaptured = true;
        }

        float lungeEnd = attackWindup + lungeDuration; // lungeDistance=0时lungeEnd==前摇末
        float yaw;
        if (CurrentState == State.Attack && _attackTimer < attackWindup)
        {
            // 前摇：向右缓举（可读蓄力）
            float k = Mathf.Clamp01(_attackTimer / attackWindup);
            yaw = sweepVisualYaw * k;
        }
        else if (CurrentState == State.Attack && _attackTimer < lungeEnd + strikeWindow)
        {
            // 判定窗：快速横扫到左侧对称角
            float k = Mathf.Clamp01((_attackTimer - lungeEnd) / strikeWindow);
            yaw = Mathf.Lerp(sweepVisualYaw, -sweepVisualYaw, k);
        }
        else
        {
            yaw = 0f; // 回正
        }
        weapon.localRotation = _weaponRestRot * Quaternion.Euler(0f, yaw, 0f);
    }
}
