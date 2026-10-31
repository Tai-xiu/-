using UnityEngine;

/// <summary>
/// 商店系统
/// 挂在商店区域标记（ShopZone，场景根）上，交互为准星瞄准制（与宝箱/掉落物同口径）：
/// - 玩家在交互半径内 且 准星射线命中商店（aimRoot下任意碰撞体）→ Aimed=true
///   （InteractPromptUI屏幕正中提示"[F] 打开商店"+方框）
/// - 瞄准+按交互键（默认F）→ 发出 OpenRequested 事件（UI打开商店面板）
/// - 离开半径 → 发出 CloseRequested 事件（UI关闭面板）；Esc关闭已统一由PauseUI分层路由经面板栈处理
/// - 交易逻辑：Buy校验金币并扣款入包（背包满自动退款）；Sell按物品出售价返还金币并从背包移除
/// 购买/出售依赖同场景玩家的 PlayerWallet 与 Inventory 组件（Start时按Tag查找）
/// </summary>
public class ShopSystem : MonoBehaviour
{
    [Tooltip("商店商品配置（ScriptableObject资产）")]
    [SerializeField] private ShopData shopData;

    [Tooltip("交互键（打开商店，默认F）")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    [Tooltip("交互半径（米）：玩家与商店标记的距离门槛")]
    [SerializeField] private float interactRadius = 3f;

    [Tooltip("射线命中根：准星射线命中该节点下任意碰撞体即视为指向商店（Camp=ENV_ShopStall）")]
    [SerializeField] private Transform aimRoot;

    /// <summary>玩家是否在商店范围内</summary>
    public bool PlayerInRange { get; private set; }

    /// <summary>准星是否指向商店（射线命中aimRoot下碰撞体且在交互半径内；
    /// InteractPromptUI提示/方框与F键同口径）</summary>
    public bool Aimed { get; private set; }

    /// <summary>请求打开商店面板（瞄准+按交互键F触发，UI订阅）</summary>
    public event System.Action OpenRequested;
    /// <summary>请求关闭商店面板（离开范围触发，UI订阅；Esc关闭统一走PauseUI分层路由）</summary>
    public event System.Action CloseRequested;
    /// <summary>玩家进出范围变化（true进入/false离开，UI订阅用于提示与强制关闭）</summary>
    public event System.Action<bool> PlayerPresenceChanged;

    // —— 可交互注册表：InteractPromptUI汇集屏幕正中提示/方框目标 ——
    private static readonly System.Collections.Generic.List<ShopSystem> _all
        = new System.Collections.Generic.List<ShopSystem>();

    /// <summary>全场景商店只读视图</summary>
    public static System.Collections.Generic.IReadOnlyList<ShopSystem> All { get { return _all; } }

    /// <summary>商店数据（UI读取商品列表）</summary>
    public ShopData Data => shopData;

    private PlayerWallet _wallet;
    private Inventory _inventory;
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
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            _wallet = playerObj.GetComponent<PlayerWallet>();
            _inventory = playerObj.GetComponent<Inventory>();
            _player = playerObj.transform;
        }
    }

    private void Update()
    {
        // 暂停菜单打开期间不响应交互键（守卫：暂停优先）
        if (PauseUI.IsPaused)
        {
            SetPresence(false);
            Aimed = false;
            return;
        }
        if (_player == null && !RefreshPlayerRef()) return;

        bool inRange = (_player.position - transform.position).sqrMagnitude
            <= interactRadius * interactRadius;
        SetPresence(inRange);             // 进出范围事件（UI强制关面板依赖，保留原语义）
        Aimed = inRange && EvaluateAim(); // 准星指向判定（InteractPromptUI与F键同口径）

        // 瞄准+按交互键 → 请求打开商店面板（不再要求进入过触发器）
        if (Aimed && Input.GetKeyDown(interactKey))
        {
            Open();
        }
    }

    /// <summary>
    /// 准星瞄准判定（射线命中制，与宝箱同手法）：从相机沿视线发射线，命中aimRoot下
    /// 任意碰撞体（含保留的白盒碰撞）即视为指向商店；射线先命中他物=被遮挡不指向。
    /// 触发器不参与命中（QueryTriggerInteraction.Ignore，防ShopZone自身隐形盒挡视线）；
    /// 距离门槛由玩家位置（交互半径）承担——相机高1.6m不能用相机3D距离对比半径（实测坑）
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

    /// <summary>进出范围统一入口：范围变化时发事件；离开时强制请求关面板（原触发器Exit语义）</summary>
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

    /// <summary>打开商店（发出事件，由UI响应）</summary>
    public void Open()
    {
        OpenRequested?.Invoke();
    }

    /// <summary>关闭商店（发出事件，由UI响应）</summary>
    public void Close()
    {
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// 购买指定条目：校验金币 → 扣款 → 物品入包（背包满则自动退款）
    /// </summary>
    public bool Buy(int entryIndex)
    {
        if (shopData == null || entryIndex < 0 || entryIndex >= shopData.entries.Count) return false;
        ShopEntry entry = shopData.entries[entryIndex];
        if (entry.item == null) return false;

        if (_wallet == null || !_wallet.TrySpend(entry.buyPrice))
        {
            Debug.Log("[ShopSystem] 金币不足，无法购买 " + entry.item.itemName + "（需" + entry.buyPrice + "）");
            return false;
        }

        if (_inventory == null || !_inventory.AddItem(entry.item, 1))
        {
            _wallet.Add(entry.buyPrice); // 背包满：退款回滚
            Debug.Log("[ShopSystem] 背包已满，购买 " + entry.item.itemName + " 失败，已退款");
            return false;
        }

        Debug.Log("[ShopSystem] 购买 " + entry.item.itemName + "，花费 " + entry.buyPrice
            + "，剩余金币 " + _wallet.Coins);
        return true;
    }

    /// <summary>
    /// 出售背包指定格的1个物品：按物品出售价返还金币并从背包移除
    /// （inventoryIndex为背包格索引0~19，空格或越界返回false）
    /// </summary>
    public bool Sell(int inventoryIndex)
    {
        if (_inventory == null) return false;
        var stack = _inventory.GetSlot(inventoryIndex);
        if (stack == null) return false;

        // 配置表v0.2：金币/宝石类掉落物(Material)按原价出售；其他物品（装备/消耗品）为原价的70%
        int gain = GetSellGain(inventoryIndex);
        if (gain <= 0)
        {
            Debug.Log("[ShopSystem] " + stack.item.itemName + " 无法出售（无出售价）");
            return false;
        }

        if (!_inventory.RemoveAt(inventoryIndex, 1)) return false;
        _wallet.Add(gain);
        Debug.Log("[ShopSystem] 出售 " + stack.item.itemName + "，获得 " + gain + " 金币，当前 " + _wallet.Coins);
        return true;
    }

    /// <summary>
    /// 预览/获取某背包格物品的实际出售所得（配置表v0.2）：
    /// 金币/宝石类掉落物(Material)按原价出售；其他物品（装备/消耗品）固定为原价的70%
    /// </summary>
    public int GetSellGain(int inventoryIndex)
    {
        if (_inventory == null) return 0;
        var stack = _inventory.GetSlot(inventoryIndex);
        if (stack == null || stack.item == null) return 0;
        return stack.item.type == ItemType.Material
            ? stack.item.sellPrice
            : Mathf.RoundToInt(stack.item.sellPrice * 0.7f);
    }
}
