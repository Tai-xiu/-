using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏全局管理器（单例，跨场景存活）
/// 职责：
/// 1. 跨场景玩家数据保留：场景切换前由Portal捕获（CapturePlayerState），
///    新场景的PlayerWallet/Inventory在Start时消费恢复（ConsumeWallet/ConsumeInventory）。
///    方案选型：静态数据存档而非DontDestroyOnLoad玩家对象——双场景各自持有独立Player实例，
///    数据传递只搬"数值"，避免跨场景对象与场景对象重复冲突，结构最简单。
/// 2. 本次探索战利品清单：进入迷宫时清空（BeginRun），迷宫内拾取时记录（RecordLoot），
///    返回营地时由"探索成功"面板展示。
/// 使用方式：两场景各放置一个挂载本脚本的GameManager空物体；运行时首个Awake的实例存活，
/// 后加载场景中的重复实例自动销毁。
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>全局单例</summary>
    public static GameManager Instance { get; private set; }

    // —— 跨场景玩家数据（Capture时快照，Consume时恢复） ——
    private int _savedCoins;
    private readonly Inventory.ItemStack[] _savedSlots = new Inventory.ItemStack[Inventory.SlotCount];
    private ItemData _savedWeapon;
    private ItemData _savedArmor;
    private readonly ItemData[] _savedHotbar = new ItemData[4]; // 快捷栏绑定（随背包一起跨场景）
    private bool _walletPending;
    private bool _inventoryPending;

    // —— 本次探索战利品清单 ——
    private readonly List<string> _lootLog = new List<string>();

    /// <summary>本次探索的战利品记录（"物品名 x数量"格式，只读）</summary>
    public IReadOnlyList<string> LootLog => _lootLog;

    /// <summary>是否有待恢复的玩家数据</summary>
    public bool HasSavedState => _walletPending || _inventoryPending;

    private UnityEngine.EventSystems.EventSystem _eventSystem;

    private void Awake()
    {
        // 单例：保留首个实例并跨场景存活，销毁后续重复实例
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureSingleEventSystem();

        // 常驻UI根画布：所有面板/漂浮文字统一挂载，避免被场景卸载或传送黑屏画布连带销毁
        if (GameCanvas == null)
        {
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            DontDestroyOnLoad(go);
            GameCanvas = canvas;
        }
        ApplySceneUiState(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureSingleEventSystem();
        ApplySceneUiState(scene);
    }

    /// <summary>跨场景只保留一个EventSystem，避免常驻实例与场景实例同时响应输入</summary>
    private void EnsureSingleEventSystem()
    {
        var systems = FindObjectsOfType<UnityEngine.EventSystems.EventSystem>(true);
        if (_eventSystem == null)
        {
            // 优先复用已经常驻的实例，其次复用当前场景实例，最后才新建
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].gameObject.scene.name == "DontDestroyOnLoad")
                {
                    _eventSystem = systems[i];
                    break;
                }
            }
            if (_eventSystem == null) _eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (_eventSystem == null && systems.Length > 0) _eventSystem = systems[0];
        }

        if (_eventSystem == null)
        {
            var es = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
            _eventSystem = es.GetComponent<UnityEngine.EventSystems.EventSystem>();
        }

        DontDestroyOnLoad(_eventSystem.gameObject);
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] != null && systems[i] != _eventSystem)
                Destroy(systems[i].gameObject);
        }
        _eventSystem.gameObject.SetActive(true);
        _eventSystem.enabled = true;
    }

    /// <summary>主菜单只显示场景手动UI；进入游戏场景时恢复共享HUD画布</summary>
    private void ApplySceneUiState(Scene scene)
    {
        if (GameCanvas != null)
            GameCanvas.gameObject.SetActive(scene.name != "MainMenu");
    }

    /// <summary>全局唯一且跨场景存活的UI根画布（所有uGUI面板/漂浮文字挂在此处）</summary>
    public Canvas GameCanvas { get; private set; }

    /// <summary>
    /// 捕获当前玩家状态（场景切换前由Portal调用）
    /// </summary>
    public void CapturePlayerState(PlayerWallet wallet, Inventory inventory)
    {
        _savedCoins = wallet.Coins;
        // 按槽位原样快照（null=空格），含多格物品的锚点与方向，跨场景传送后摆放格位不丢失
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            var s = inventory.GetSlot(i);
            _savedSlots[i] = s != null
                ? new Inventory.ItemStack
                {
                    item = s.item,
                    count = s.count,
                    anchorRow = s.anchorRow,
                    anchorCol = s.anchorCol,
                    orient = s.orient
                }
                : null;
        }
        _savedWeapon = inventory.EquippedWeapon;
        _savedArmor = inventory.EquippedArmor;
        for (int i = 0; i < 4; i++) _savedHotbar[i] = inventory.GetHotbarItem(i); // 快捷栏绑定随背包捕获
        _walletPending = true;
        _inventoryPending = true;
    }

    /// <summary>
    /// 消费钱包存档：首个调用者获得数据并清除待恢复标记（PlayerWallet.Start调用）
    /// </summary>
    public bool ConsumeWallet(out int coins)
    {
        coins = _savedCoins;
        bool had = _walletPending;
        _walletPending = false;
        return had;
    }

    /// <summary>
    /// 消费背包/装备存档：首个调用者获得数据并清除待恢复标记（Inventory.Start调用）
    /// slots为长度20的数组副本，null=空格，槽位位置与捕获时一致
    /// </summary>
    public bool ConsumeInventory(out Inventory.ItemStack[] slots, out ItemData weapon, out ItemData armorItem)
    {
        slots = (Inventory.ItemStack[])_savedSlots.Clone();
        weapon = _savedWeapon;
        armorItem = _savedArmor;
        bool had = _inventoryPending;
        _inventoryPending = false;
        return had;
    }

    /// <summary>
    /// 消费快捷栏绑定快照（须在ConsumeInventory返回true后调用，随背包一起恢复）
    /// </summary>
    public ItemData[] ConsumeHotbar()
    {
        var arr = new ItemData[4];
        for (int i = 0; i < 4; i++) arr[i] = _savedHotbar[i];
        return arr;
    }

    /// <summary>
    /// 进入迷宫：清空本次探索的战利品记录（由Portal在进入方向传送时调用）
    /// </summary>
    public void BeginRun()
    {
        _lootLog.Clear();
    }

    /// <summary>
    /// 记录本次探索拾取的战利品（PickupItem拾取成功时调用）
    /// </summary>
    public void RecordLoot(ItemData item, int count)
    {
        _lootLog.Add(item.itemName + " x" + count);
    }
}
