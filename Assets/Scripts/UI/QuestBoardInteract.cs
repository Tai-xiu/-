using System;
using UnityEngine;

/// <summary>
/// 冒险者协会悬赏板交互（挂Camp协会白模上）
/// 交互层与ShopSystem完全同款（准星瞄准制，2026-10-21拍板：F键+射线命中制）：
/// - 玩家在交互半径内 且 准星射线命中协会（aimRoot下任意碰撞体）→ Aimed=true
///   （InteractPromptUI屏幕正中提示"[F] 冒险者协会"+方框；优先级插在商店之后）
/// - 瞄准+按交互键（默认F）→ 发出 OpenRequested 事件（QuestBoardUI打开悬赏板面板）
/// - 离开半径 → 发出 CloseRequested 事件（UI关闭面板）；Esc关闭统一由PauseUI分层路由经面板栈处理
/// 本类只管交互与事件，不含任何UI构建（面板归QuestBoardUI，由本类Start时懒创建）
/// </summary>
public class QuestBoardInteract : MonoBehaviour
{
    [Tooltip("交互键（打开悬赏板，默认F）")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Tooltip("交互半径（米）：玩家与协会的距离门槛")]
    [SerializeField] private float interactRadius = 3f;

    [Tooltip("射线命中根：准星射线命中该节点下任意碰撞体即视为指向协会（场景接线=协会白模自身）")]
    [SerializeField] private Transform aimRoot;

    /// <summary>玩家是否在协会范围内</summary>
    public bool PlayerInRange { get; private set; }

    /// <summary>准星是否指向协会（射线命中aimRoot下碰撞体且在交互半径内）</summary>
    public bool Aimed { get; private set; }

    /// <summary>请求打开悬赏板面板（瞄准+按交互键F触发，QuestBoardUI订阅）</summary>
    public event Action OpenRequested;

    /// <summary>请求关闭悬赏板面板（离开范围触发，QuestBoardUI订阅）</summary>
    public event Action CloseRequested;

    /// <summary>玩家进出范围变化（true进入/false离开，QuestBoardUI订阅用于强制关闭）</summary>
    public event Action<bool> PlayerPresenceChanged;

    // —— 可交互注册表：InteractPromptUI汇集屏幕正中提示/方框目标 ——
    private static readonly System.Collections.Generic.List<QuestBoardInteract> _all
        = new System.Collections.Generic.List<QuestBoardInteract>();

    /// <summary>全场景协会只读视图</summary>
    public static System.Collections.Generic.IReadOnlyList<QuestBoardInteract> All { get { return _all; } }

    private Transform _player;

    private void OnEnable()
    {
        _all.Add(this);
    }

    private void OnDisable()
    {
        _all.Remove(this);
    }

    private void Start()
    {
        RefreshPlayerRef();
        // 懒创建悬赏板UI（场景物体，随场景卸载销毁；面板本体挂常驻GameCanvas，重建时自清旧面板）
        QuestBoardUI.EnsureInstance(this);
    }

    private void Update()
    {
        // 暂停菜单打开期间不响应交互键（守卫：暂停优先，语义同ShopSystem）
        if (PauseUI.IsPaused)
        {
            SetPresence(false);
            Aimed = false;
            return;
        }
        if (_player == null && !RefreshPlayerRef()) return;

        bool inRange = (_player.position - transform.position).sqrMagnitude
            <= interactRadius * interactRadius;
        SetPresence(inRange);             // 进出范围事件（UI强制关面板依赖）
        Aimed = inRange && EvaluateAim(); // 准星指向判定（InteractPromptUI与F键同口径）

        // 瞄准+按交互键 → 请求打开悬赏板面板
        if (Aimed && Input.GetKeyDown(interactKey))
        {
            Open();
        }
    }

    /// <summary>
    /// 准星瞄准判定（射线命中制，ShopSystem同手法）：从相机沿视线发射线，命中aimRoot下
    /// 任意碰撞体即视为指向协会；射线先命中他物=被遮挡不指向。
    /// 触发器不参与命中（QueryTriggerInteraction.Ignore）；距离门槛由玩家位置承担
    /// （相机高1.6m不能用相机3D距离对比半径，实测坑）
    /// </summary>
    private bool EvaluateAim()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        var root = aimRoot != null ? aimRoot
            : (transform.parent != null ? transform.parent : transform);
        RaycastHit hit;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out hit,
                interactRadius + 2f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        return hit.collider.transform.IsChildOf(root);
    }

    /// <summary>进出范围统一入口：范围变化时发事件；离开时强制请求关面板</summary>
    private void SetPresence(bool inRange)
    {
        if (PlayerInRange == inRange) return;
        PlayerInRange = inRange;
        PlayerPresenceChanged?.Invoke(inRange);
        if (!inRange) Close();
    }

    private bool RefreshPlayerRef()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) _player = p.transform;
        return _player != null;
    }

    /// <summary>打开悬赏板（发出事件，由UI响应；测试脚本可直接调用）</summary>
    public void Open()
    {
        OpenRequested?.Invoke();
    }

    /// <summary>关闭悬赏板（发出事件，由UI响应）</summary>
    public void Close()
    {
        CloseRequested?.Invoke();
    }
}
