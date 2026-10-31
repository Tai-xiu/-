using UnityEngine;

/// <summary>
/// 玩家角色控制器（第一人称版）
/// 功能：
/// 1. WASD/方向键相对玩家身体朝向移动：前进=玩家forward、横移=玩家right（W前S后A左D右）；
///    身体朝向由FirstPersonLook的鼠标水平输入控制，本脚本不再管理朝向
/// 2. 重力：始终对CC施加重力位移（贴地下压/空中累积下落），角色落地不悬浮
///    （传送/摆放到空中时也能自动落回地面）
///
/// 选型说明（CharacterController vs Rigidbody）：
/// 第一人称探索的移动属于"精确控制"型——走停干脆、不希望被物理力干扰。
/// CharacterController由代码直接驱动位移，自带胶囊碰撞与Cast检测，手感稳定不滑动；
/// 重力以自管理的竖直速度实现（等效Rigidbody但完全可控）。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("—— 移动设置 ——")]
    [Tooltip("移动速度（米/秒），可在Inspector调整")]
    [SerializeField] private float moveSpeed = 4f;

    /// <summary>基础移速（背包面板属性汇总显示用，配置表v0.2移速公式基数）</summary>
    public float BaseMoveSpeed => moveSpeed;

    [Tooltip("冲刺速度倍率（按住LeftShift移动时，消耗体力）")]
    [SerializeField] private float sprintMultiplier = 1.6f;

    /// <summary>角色控制器组件缓存</summary>
    private CharacterController _controller;
    /// <summary>体力组件（Start缓存——Awake阶段可能尚未被PlayerCombat自动补挂）</summary>
    private Stamina _stamina;
    /// <summary>快捷栏组件（食用硬直时移动减速；HUDController.Start自动补挂，
    /// 补挂时序不保证先于本类Start——HandleMove内懒获取兜底）</summary>
    private HotbarController _hotbar;
    /// <summary>背包组件（装备重量→移速系数，配置表v0.1）</summary>
    private Inventory _inventory;
    /// <summary>技能执行器（施法移速因子；PlayerCombat.Awake自动补挂，HandleMove内懒获取）</summary>
    private SkillExecutor _skills;
    /// <summary>竖直速度（重力用；贴地时保持-2下压保证isGrounded判定）</summary>
    private float _verticalVelocity = -2f;
    /// <summary>输入开关（死亡时由PlayerCombat通过SetInputEnabled禁用）</summary>
    private bool _inputEnabled = true;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        // 体力组件由PlayerCombat.Awake自动补挂，Start阶段所有Awake已完成，可安全获取
        _stamina = GetComponent<Stamina>();
        // 快捷栏组件由HUDController.Start自动补挂（含食用硬直减速状态）
        _hotbar = GetComponent<HotbarController>();
        _inventory = GetComponent<Inventory>();
    }

    private void Update()
    {
        // 死亡等状态下禁用输入（重力仍由下方Move应用？否——禁用即完全冻结，白盒阶段可接受）
        if (!_inputEnabled) return;

        // 打开背包/商店等面板时冻结移动：直接return（CC由代码驱动无惯性，角色立即停住无需减速）；
        // 例外：食用硬直进行中放行（背包右键"使用"时面板必然开着，完全冻结会让50%减速永远
        // 感受不到）——减速倍率在HandleMove内经eatFactor生效，通道结束后自动回到面板冻结语义
        bool eating = _hotbar != null && _hotbar.IsEating;
        if (GameUIState.IsAnyPanelOpen && !eating) return;

        HandleMove();
    }

    /// <summary>
    /// 移动：WASD/方向键相对玩家身体朝向 → XZ平面位移 + 重力竖直位移（朝向由鼠标视角控制）
    /// </summary>
    private void HandleMove()
    {
        // 读取旧版Input Manager输入轴（本项目未引入新Input System）
        float h = Input.GetAxisRaw("Horizontal"); // A/D、左右方向键（横移）
        float v = Input.GetAxisRaw("Vertical");   // W/S、上下方向键（前后）

        // 相对玩家身体朝向：前进=玩家forward、横移=玩家right（投影到XZ平面防俯视时斜向移动）
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();

        // 合成移动方向；GetAxisRaw返回-1/0/1，斜向输入归一化避免对角移动更快
        Vector3 lateral = right * h + fwd * v;
        if (lateral.sqrMagnitude > 1f)
        {
            lateral.Normalize();
        }

        // 重力：贴地时给固定下压（保证isGrounded判定稳定）；空中累积下落速度
        if (_controller.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }
        else
        {
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
        }

        // 冲刺：按住LeftShift且有移动输入时请求；是否生效由Stamina裁定（起步需≥20，可消耗到0）
        bool wantsSprint = Input.GetKey(KeyCode.LeftShift) && lateral.sqrMagnitude > 0.01f;
        bool sprinting = _stamina != null && _stamina.ConsumeSprintFrame(wantsSprint);

        // 食用硬直：吃食物通道进行中移动减速50%（与冲刺倍率乘算叠加）。
        // 懒获取：HotbarController由HUDController.Start补挂，本类Start可能先执行而缓存到null
        // （实测发生过，减速全路径失效）——为空时逐帧补取，命中后引用稳定无后续查询开销
        if (_hotbar == null) _hotbar = GetComponent<HotbarController>();
        float eatFactor = _hotbar != null ? _hotbar.MoveSpeedFactor : 1f;

        // 技能施法移速因子（技能系统v0.1）：前摇缓动×0.5/站定0/后摇×0.5，由SkillExecutor裁定；
        // 施法期间禁用冲刺请求（站定阶段冲刺毫无意义且白白消耗体力）
        if (_skills == null) _skills = GetComponent<SkillExecutor>();
        float castFactor = _skills != null ? _skills.MoveSpeedFactor : 1f;
        if (castFactor < 1f)
        {
            sprinting = false;
            if (_stamina != null) _stamina.ConsumeSprintFrame(false); // 复位冲刺状态防跨阶段残留
        }

        // 装备重量→移速系数（配置表v0.1：移速=基础×max(0.6,1-总重×0.04)，无装备=1）
        float weightFactor = 1f;
        if (_inventory != null && _inventory.EquippedArmor != null)
        {
            weightFactor = Mathf.Max(0.6f, 1f - _inventory.EquippedArmor.weight * 0.04f);
        }
        float baseSpeed = moveSpeed * weightFactor;
        float speed = baseSpeed * (sprinting ? sprintMultiplier : 1f) * eatFactor * castFactor;

        // 始终调用Move（含零输入帧）：竖直位移依赖每帧应用，角色传送/摆放到空中会自然落地
        Vector3 motion = lateral * speed + Vector3.up * _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
        // 注：朝向由FirstPersonLook的鼠标水平输入控制，此处不再做朝向插值
    }

    /// <summary>
    /// 外部开关输入（死亡时由PlayerCombat调用，禁用移动）
    /// </summary>
    public void SetInputEnabled(bool value)
    {
        _inputEnabled = value;
    }
}
