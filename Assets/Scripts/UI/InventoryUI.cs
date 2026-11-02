using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 背包UI控制器：绑定可编辑 Prefab，更新数据并处理交互。
/// ─────────────────────────────────────────────
/// 布局由 InventoryPanel.prefab 编辑；网格基础10格，装备背包后最多40格。
/// 多格渲染：物品名文字为跨格覆盖层（按锚点与方向铺满整个占位区域，居中显示）
/// 拖拽规则：
/// - 多格物品从其占位区域的任意格都可以拖起；图标按物品实际尺寸跟随鼠标并半透明
/// - 悬停预览：按抓取偏移计算目标锚点（自动夹取边界），将被占用的全部格子整组高亮——
///   合法区域绿框、非法区域红框
/// - 放置：目标区域全部为空才可移动；与另一物品交换要求占用形状（宽高+方向）一致，
///   一致时预览自动吸附到目标锚点（区域完全重合），否则红框拒绝并回弹
/// - 装备槽：类型校验（绿=可装备红=不可）；装备槽拖出=卸下重新寻位（有空间才显示绿框）
/// - 快捷栏（详情框顶部4格，数字键1~4使用）：拖入消耗品=绑定该槽；快捷栏间拖动=换位；
///   拖回背包=解除绑定（2026-10-09改版：取消右键菜单分配，改为拖拽放置）
/// - 拖拽与点击互斥：位移超过EventSystem像素阈值判定为拖拽，松手不触发点击菜单
/// 数据层为 Inventory；InventoryPanelView 保存序列化节点引用，本类只刷新与转发。
/// </summary>
public partial class InventoryUI : MonoBehaviour
{
    [Header("—— 按键 ——")]
    [Tooltip("开关背包面板（默认Tab）")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    /// <summary>本面板在全局UI状态门中的注册id（面板栈唯一标识）</summary>
    private const string PanelId = "Inventory";

    // ===== 组件引用 =====
    private InventoryPanelView _view;
    private Color _hintColor, _emptyBagColor;
    private Vector2 _hpAnchorMin, _hpAnchorMax, _hpOffsetMin, _hpOffsetMax;

    private Inventory _inventory;
    private Health _health;

    private GameObject _rootPanel;
    private bool _panelOpen; // 面板当前是否打开（与全局面板栈注册严格一一对应）
    private RectTransform _canvasRect; // GameCanvas矩形（拖拽图标坐标换算用）

    // —— 装备槽 ——
    private Text _weaponSlotText;
    private Image _weaponSil;
    private Image _armorSil;
    private Image _bagSil;
    private Sprite _bagSilSprite;
    private Text _armorSlotText;
    private InventoryCellUI _weaponCell;
    private InventoryCellUI _armorCell;
    private RectTransform _weaponDashRt;   // 武器槽虚线区（已装备图标块挂载点）
    private RectTransform _armorDashRt;
    private GameObject _weaponEquipIcon;   // 已装备物品的小图标块
    private GameObject _armorEquipIcon;

    // —— 属性框 ——
    private Text _statText;
    private Image _hpFill;

    // —— 网格（20个交互格常驻 + 物品名跨格覆盖层动态重建） ——
    private RectTransform _gridRoot;
    private readonly List<InventoryCellUI> _cells = new List<InventoryCellUI>();
    private readonly Dictionary<int, GameObject> _overlayByAnchor = new Dictionary<int, GameObject>();

    // —— 详情面板（只读信息 + 操作反馈提示行；操作入口已移至右键菜单） ——
    private Text _detailTitle;
    private Text _detailDesc;

    // —— 右键菜单与拆分弹窗 ——
    private GameObject _menuRoot;      // 右键操作菜单（使用/装备/拆分/丢弃）
    private GameObject _splitRoot;     // 拆分弹窗（滑块）
    private Slider _splitSlider;       // 拆分数量滑块
    private Text _splitQtyText;        // 拆分数量显示
    private Text _splitTitleText;      // 拆分弹窗标题（含物品名）
    private int _splitAnchor = -1;     // 拆分目标物品锚点

    // —— 状态行与提示 ——
    private Text _slotCountText;
    private Text _hintText;
    private Coroutine _hintRoutine;

    /// <summary>当前选中的物品（按锚点格索引记录，-1=未选中）</summary>
    private int _selectedIndex = -1;

    // ===== 快捷栏（详情框顶部4格；拖拽放置/换位/拖回背包解除，数字键1~4由HotbarController使用） =====
    private readonly GameObject[] _hotbarIcons = new GameObject[4];
    private readonly Text[] _hotbarCounts = new Text[4];
    private readonly InventoryCellUI[] _hotbarCellUIs = new InventoryCellUI[4];

    // ===== 拖拽状态 =====
    private bool _dragging;                     // 是否正在拖拽
    private InventoryCellUI _dragSource;        // 拖拽源格子
    private Inventory.ItemStack _dragStack;     // 拖拽中的物品堆（含锚点/方向/尺寸）
    private bool _dragFromEquip;                // 拖拽源是否为装备槽（卸下走重新寻位）
    private bool _dragFromHotbar;               // 拖拽源是否为快捷栏槽（仅移动绑定，物品本体始终在背包）
    private bool _dragEquipPossible;            // 装备槽拖出时背包是否有卸下空间（预览统一绿/红）
    private int _grabRow, _grabCol;             // 抓取点相对锚点的偏移（保持手感）
    private InventoryCellUI _hoverCell;         // 当前悬停目标格
    private int _previewAnchor = -1;            // 预览锚点（-1=无）
    private bool _previewLegal;                 // 预览是否合法（绿/红）
    private Inventory.ItemStack _previewSwap;   // 预览交换目标（null=移动/装备）
    private bool _previewMerge;                 // 预览语义=同类可堆叠物品叠加合并（提交时走MergeStacks）
    private GameObject _ghost;                  // 半透明跟随图标
    private ItemOrient _dragOrient;             // 拖拽中的方向（独立字段：严禁直接改数据层栈的orient，避免占用数组失同步）

    // ===== 白盒样式常量 =====
    public const float BorderWidth = 2f;      // 实线边框粗细（全UI统一）
    public const float SelectedBorder = 3.5f; // 选中边框粗细
    public static readonly Color PanelBg = new Color(0.94f, 0.94f, 0.94f, 1f);  // 面板底色（框体填充同色→视觉无色块；SearchPanelUI复用）
    public static readonly Color LineColor = new Color(0.85f, 0.75f, 0.53f, 1f);   // 深色 kit 面板上的文字/线条色   // 统一线条/文字黑色（SearchPanelUI复用）
    private static readonly Color HintColor = new Color(0.65f, 0.12f, 0.12f, 1f);// 操作失败提示（深红）
    private static readonly Color LegalColor = new Color(0.15f, 0.7f, 0.2f, 1f); // 拖拽预览合法=绿框
    private static readonly Color IllegalColor = new Color(0.85f, 0.15f, 0.15f, 1f); // 拖拽预览非法=红框

    /// <summary>登记待绘制虚线边框的矩形（布局定稿后统一绘制）</summary>


    private void Start()
    {
        _inventory = GetComponent<Inventory>();
        _health = GetComponent<Health>();
        if (_inventory == null)
        {
            Debug.LogError("[InventoryUI] 缺少同物体上的Inventory组件");
            enabled = false;
            return;
        }

        // 确保EventSystem存在（拖拽/点击事件派发的前提）
        EnsureEventSystem();

        // 获取常驻UI根画布（GameManager优先，缺失时兜底自建）
        Transform canvasRoot;
        if (GameManager.Instance != null && GameManager.Instance.GameCanvas != null)
        {
            canvasRoot = GameManager.Instance.GameCanvas.transform;
        }
        else
        {
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = go.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasRoot = go.transform;
            DontDestroyOnLoad(go);
        }
        _canvasRect = canvasRoot as RectTransform;

        // 清理旧面板（跨场景回来时避免重复）
        var old = canvasRoot != null ? canvasRoot.Find("InventoryPanel") : null;
        if (old != null) Destroy(old.gameObject);

        if (canvasRoot == null)
        {
            Debug.LogWarning("[InventoryUI] canvasRoot为null，跳过面板构建");
            return;
        }

        if (!BindPanel(canvasRoot)) { enabled = false; return; }

        // 先强制完成一次布局拿到各框最终尺寸，再统一绘制虚线边框
        LayoutRebuilder.ForceRebuildLayoutImmediate(_rootPanel.GetComponent<RectTransform>());


        _inventory.Changed += RefreshAll;
        _inventory.HotbarChanged += RefreshAll; // 快捷栏分配变化同步详情框快捷栏
        RefreshAll();
        _rootPanel.SetActive(false); // 默认收起，按B打开
    }

    private void OnDestroy()
    {
        HideMenu();
        if (_ghost != null) Destroy(_ghost);
        if (_rootPanel != null) Destroy(_rootPanel);
        if (_inventory != null) _inventory.Changed -= RefreshAll;
        if (_inventory != null) _inventory.HotbarChanged -= RefreshAll;

        // 防计数泄漏：面板开着时组件随场景/玩家销毁（如传送切换），从全局面板栈注销，
        // 否则栈永久残留导致后续冻结/路由异常
        if (_panelOpen)
        {
            _panelOpen = false;
            GameUIState.UnregisterPanel(PanelId);
        }
    }

    private void Update()
    {
        // 暂停菜单打开期间不响应（Tab/R等UI按键守卫：暂停优先）
        if (PauseUI.IsPaused) return;

        // Tab键：开关背包面板（开关联动全局面板栈，见TogglePanel）
        if (Input.GetKeyDown(toggleKey))
        {
            TogglePanel();
        }

        // 右键菜单打开时：点击菜单外任意处收起（右键另一格会直接重建菜单）
        if (_menuRoot != null && Input.GetMouseButtonDown(0))
        {
            var mrt = _menuRoot.GetComponent<RectTransform>();
            if (!RectTransformUtility.RectangleContainsScreenPoint(mrt, Input.mousePosition, null))
                HideMenu();
        }

        // 拖拽中按R手动旋转（仅背包内拖拽的非等比物品；装备槽拖出不响应，卸下走自动寻位）
        if (_dragging && !_dragFromEquip && _dragStack != null && Input.GetKeyDown(KeyCode.R))
        {
            TryRotateDragged();
        }

        // 面板打开期间实时同步HP血条
        if (_rootPanel != null && _rootPanel.activeSelf && _hpFill != null && _health != null && _health.Max > 0)
        {
            SetHpFill(_health.Current / _health.Max);
        }
    }

    /// <summary>
    /// Tab键开关背包面板（公共入口：Update按键触发，测试脚本亦可直调）；
    /// 以本类维护的_panelOpen为准向全局面板栈注册/注销（打开入栈、关闭出栈，
    /// closeAction=ClosePanel供Esc分层路由调用），保证面板激活状态与面板栈严格同步
    /// </summary>
    public void TogglePanel()
    {
        if (_panelOpen)
        {
            ClosePanel();
            return;
        }
        OpenPanel();
    }

    /// <summary>背包面板当前是否打开（SearchPanelUI等外部联动读取）</summary>
    public bool IsOpen => _panelOpen;

    /// <summary>
    /// 打开背包面板（公共入口：Tab开包与搜索完成联动共用；已开无操作）。
    /// 注册全局面板栈（closeAction=ClosePanel供Esc分层路由）→置顶→刷新→激活
    /// </summary>
    public void OpenPanel()
    {
        if (_panelOpen || _rootPanel == null) return;
        _panelOpen = true;
        GameUIState.RegisterPanel(PanelId, ClosePanel);
        // 置顶到Canvas最上层：头顶血条等常驻UI同挂GameCanvas且序号更靠后会盖住面板
        _rootPanel.transform.SetAsLastSibling();
        RefreshAll();
        _rootPanel.SetActive(true);
    }

    /// <summary>关闭背包面板（Esc分层路由经面板栈closeAction调用；守卫防重复注销）</summary>
    private void ClosePanel()
    {
        if (!_panelOpen) return;
        _panelOpen = false;
        GameUIState.UnregisterPanel(PanelId);
        HideMenu();
        HideSplitPopup();
        _rootPanel.SetActive(false);
    }

    // ===== 面板构建 =====



    /// <summary>构建单个装备槽（挂InventoryCellUI成为拖拽源/目标，登记槽文字与格子引用）</summary>


    /// <summary>创建单个背包交互格（实线边框无填充；物品名由覆盖层渲染，格内不再放文字）</summary>
    private InventoryCellUI CreateGridCell(int index)
    {
        GameObject template = _view != null ? _view.slotTemplate : null;
#if UNITY_EDITOR
        if (template == null && !Application.isPlaying)
            template = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Widgets/InventorySlot.prefab");
#endif
        if (template == null) throw new System.InvalidOperationException("背包格子模板未配置");
        GameObject cell;
#if UNITY_EDITOR
        if (!Application.isPlaying) cell = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(template, _gridRoot);
        else
#endif
            cell = Instantiate(template, _gridRoot, false);
        cell.name = "Cell_" + index;
        cell.SetActive(true);
        var cellUI = cell.GetComponent<InventoryCellUI>() ?? cell.AddComponent<InventoryCellUI>();
        cellUI.Init(this, InventoryCellUI.CellKind.Bag, index);
        return cellUI;
    }

    // ===== 刷新 =====


    /// <summary>装备背包（由背包槽拖放/右键菜单调用）：提升容量并刷新；失败（缩容放不下）返回false</summary>
    private void TryEquipBackpack(Inventory.ItemStack stack)
    {
        if (stack == null || stack.item == null) return;
        if (stack.item.type != ItemType.Backpack)
        {
            ShowHint(stack.item.itemName + " 不是背包，无法放入");
            return;
        }
        var newBag = stack.item;
        var oldBag = _inventory.Backpack;
        bool replacing = oldBag != null && oldBag != newBag;

        // 记录新背包原位：装备缩容会重排物品锚点，失败时按原位放回
        int count = stack.count;
        int row = stack.anchorRow, col = stack.anchorCol;
        var orient = stack.orient;
        _inventory.RemoveItem(stack.AnchorIndex);

        // 更换时旧背包先放回物品栏（放不下=空间不足，整体回退）
        if (replacing && !_inventory.AddItem(oldBag, 1))
        {
            _inventory.PlaceItem(newBag, count, row, col, orient);
            RefreshAll();
            ShowHint("背包空间不足，放不下换下的 " + oldBag.itemName);
            return;
        }

        if (!EquipBackpack(newBag))
        {
            // 装备失败（物品放不进新容量）：撤回旧背包归位、恢复原装备、新背包放回原位
            if (replacing)
            {
                for (int i = 0; i < _inventory.Slots.Count; i++)
                {
                    var s = _inventory.GetSlot(i);
                    if (s != null && s.item == oldBag) { _inventory.RemoveItem(i); break; }
                }
                _inventory.SetBackpack(oldBag); // 恢复原容量（原状态有效必然成功）
            }
            _inventory.PlaceItem(newBag, count, row, col, orient);
            RefreshAll();
            return; // 失败原因已由EquipBackpack提示
        }

        RefreshAll();
        ShowHint("已装备 " + newBag.itemName + "：物品栏 " + _inventory.Capacity + " 格"
            + (replacing ? "（" + oldBag.itemName + "已放回物品栏）" : ""));
    }

    /// <summary>装备背包入口（数据层SetBackpack+全量刷新）；缩容放不下物品时提示并返回false</summary>
    public bool EquipBackpack(ItemData bag)
    {
        if (_inventory == null) return false;
        if (!_inventory.SetBackpack(bag))
        {
            ShowHint("物品栏内的物品放不进新容量，无法更换背包");
            return false;
        }
        RefreshAll();
        return true;
    }

    /// <summary>右键或拖出背包槽：原子地取下背包，失败时显示原因。</summary>
    public bool UnequipBackpack()
    {
        if (_inventory.Backpack == null) return false;
        var name = _inventory.Backpack.itemName;
        if (!_inventory.TryUnequipBackpack())
        {
            ShowHint("基础10格放不下现有物品和背包，无法取下");
            return false;
        }
        _selectedIndex = -1;
        ShowHint("已取下 " + name + "，放回物品栏");
        return true;
    }

    /// <summary>容量变化（装/卸背包）时重建格子：多退少补，保持与 Inventory.Capacity 一致</summary>
    private void RebuildGridIfNeeded()
    {
        if (_gridRoot == null) return;
        int want = _inventory.Capacity;
        if (_cells.Count == want) return;

        while (_cells.Count > want)
        {
            var last = _cells[_cells.Count - 1];
            _cells.RemoveAt(_cells.Count - 1);
            if (last != null) { last.gameObject.SetActive(false); Destroy(last.gameObject); }
        }
        for (int i = _cells.Count; i < want; i++) _cells.Add(CreateGridCell(i));
        // 容量变化后强制重排：ContentSizeFitter 依赖 layout 的 preferredHeight 才会更新 content 高度
        if (_gridRoot != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_gridRoot);
        Debug.Log("[InventoryUI] 容量变化 -> " + want + " 格");
    }


    /// <summary>背包槽显示：无背包=白印，有背包=物品图标</summary>
    private void RefreshBagSlot()
    {
        if (_bagSil == null) return;
        var bag = _inventory.Backpack;
        if (_view.bagName != null) _view.bagName.text = bag != null ? bag.itemName : "背包";
        if (bag == null)
        {
            _bagSil.sprite = _bagSilSprite;
            _bagSil.color = _emptyBagColor;
        }
        else
        {
            _bagSil.sprite = bag.icon != null ? bag.icon : _bagSilSprite;
            _bagSil.color = Color.white;
        }
    }

    /// <summary>全量刷新：装备槽、属性、网格（边框+覆盖层）、详情、状态行、快捷栏</summary>
    private void RefreshAll()
    {
        RefreshEquipSlots();
        RefreshStats();
        RebuildGridIfNeeded();
        RefreshBagSlot();
        RefreshGrid();
        RefreshDetail();
        RefreshSlotCount();
        RefreshHotbarSlots();
    }

    /// <summary>刷新详情框快捷栏4槽（图标+数量角标；空槽=白底序号格；绑定变化/背包变化时重建）</summary>
    private void RefreshHotbarSlots()
    {
        if (_hotbarCellUIs[0] == null) return; // 面板未构建（异常兜底）
        for (int i = 0; i < 4; i++)
        {
            if (_hotbarIcons[i] != null)
            {
                Destroy(_hotbarIcons[i]);
                _hotbarIcons[i] = null;
            }
            var item = _inventory.GetHotbarItem(i);
            int count = _inventory.GetHotbarCount(i);
            if (item == null || count <= 0)
            {
                _hotbarCounts[i].text = "";
                continue;
            }
            // 虚拟堆count=1：角标由独立数量文字绘制（总数跨堆叠统计）
            var virt = new Inventory.ItemStack { item = item, count = 1, anchorRow = 0, anchorCol = 0, orient = ItemOrient.Horizontal };
            var go = BuildIconGroup(virt, new Vector2(32f, 32f), ItemOrient.Horizontal, false, false);
            go.name = "HotbarIcon_" + i;
            go.transform.SetParent(_hotbarCellUIs[i].transform, false);
            _hotbarIcons[i] = go;
            _hotbarCounts[i].text = count > 1 ? "×" + count : "×1";
        }
    }

    /// <summary>刷新装备槽：槽文字（名称/空）+ 已装备物品的小尺寸图标块（类型色剪影+单字）</summary>
    private void RefreshEquipSlots()
    {
        if (_weaponSil != null) _weaponSil.gameObject.SetActive(_inventory.EquippedWeapon == null);
        if (_armorSil != null) _armorSil.gameObject.SetActive(_inventory.EquippedArmor == null);
        RefreshEquipSlotVisual(ref _weaponEquipIcon, _weaponDashRt, _weaponSlotText, _inventory.EquippedWeapon);
        RefreshEquipSlotVisual(ref _armorEquipIcon, _armorDashRt, _armorSlotText, _inventory.EquippedArmor);
    }

    /// <summary>重建单个装备槽的图标块（小尺寸版：36×36剪影+单字，名称由槽文字显示）</summary>
    private void RefreshEquipSlotVisual(ref GameObject iconObj, RectTransform dashRt, Text slotText, ItemData item)
    {
        if (iconObj != null)
        {
            Destroy(iconObj);
            iconObj = null;
        }
        if (item == null)
        {
            slotText.text = "空";
            slotText.rectTransform.offsetMin = Vector2.zero; // 名称恢复居中
            return;
        }
        slotText.text = item.itemName;
        slotText.rectTransform.offsetMin = new Vector2(46f, 0f); // 名称右移让位给图标块

        var tmp = new Inventory.ItemStack { item = item, count = 1, anchorRow = 0, anchorCol = 0, orient = ItemOrient.Horizontal };
        iconObj = BuildIconGroup(tmp, new Vector2(36f, 36f), ItemOrient.Horizontal, false, false);
        iconObj.name = "EquipIcon";
        iconObj.transform.SetParent(dashRt, false);
        var rt = iconObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(4f, 0f);
    }

    private void RefreshStats()
    {
        // 配置表v0.2表七：普攻总伤害=当前武器伤害（无武器=基础10）；移速=基础×max(0.6,1-总重×0.04)——实时反映总攻/防/移速/重量
        var combat = GetComponent<PlayerCombat>();
        float atk = combat != null ? combat.AttackDamage : 10f + _inventory.WeaponBonus;
        float def = _inventory.ArmorDefense;
        float speed = _inventory.MoveSpeed;
        float weight = _inventory.ArmorWeight;
        _statText.text = "攻击 <b>" + atk.ToString("F0") + "</b>  减伤 <b>" + def.ToString("F0") + "</b>\n"
            + "移速 <b>" + speed.ToString("F1") + "</b>  重量 <b>" + weight.ToString("F1") + "</b>";
        SetHpFill(_health != null && _health.Max > 0 ? _health.Current / _health.Max : 0f);
    }

    /// <summary>按比例伸缩HP血条填充（0~1；过低时隐藏填充避免负宽翻转）</summary>
    private void SetHpFill(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        var rt = _hpFill.rectTransform;
        if (ratio <= 0.02f)
        {
            _hpFill.enabled = false;
            return;
        }
        _hpFill.enabled = true;
        rt.anchorMin = _hpAnchorMin;
        rt.anchorMax = new Vector2(Mathf.Lerp(_hpAnchorMin.x, _hpAnchorMax.x, ratio), _hpAnchorMax.y);
        rt.offsetMin = _hpOffsetMin;
        rt.offsetMax = _hpOffsetMax;
    }

    /// <summary>刷新网格 = 边框（普通/选中）+ 物品名覆盖层（跨格渲染）</summary>
    private void RefreshGrid()
    {
        RefreshGridBorders();
        RefreshOverlays();
    }

    /// <summary>恢复所有格子与装备槽的默认/选中边框（拖拽预览高亮也靠它复位）</summary>
    private void RefreshGridBorders()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            _cells[i].SetBorder(IsCellOfSelected(i) ? SelectedBorder : BorderWidth, LineColor);
        }
        if (_weaponCell != null) _weaponCell.SetBorder(BorderWidth, LineColor);
        if (_armorCell != null) _armorCell.SetBorder(BorderWidth, LineColor);
        if (_view != null && _view.bagCell != null) _view.bagCell.SetBorder(BorderWidth, LineColor);
        for (int i = 0; i < _hotbarCellUIs.Length; i++)
        {
            if (_hotbarCellUIs[i] != null) _hotbarCellUIs[i].SetBorder(BorderWidth, LineColor);
        }
    }

    /// <summary>指定格是否属于当前选中的物品（多格物品全部覆盖格加粗边框）</summary>
    private bool IsCellOfSelected(int slotIndex)
    {
        if (_selectedIndex < 0) return false;
        var s = _inventory.GetSlot(_selectedIndex);
        if (s == null) return false;
        return s.Covers(slotIndex / Inventory.Cols, slotIndex % Inventory.Cols);
    }

    /// <summary>
    /// 重建物品覆盖层：多格物品按锚点与方向铺满占位区域的"占位图标块"——
    /// 类型色剪影 + 中央单字/Sprite + 底部名称 + 右下角标；只在锚点渲染一次
    /// </summary>
    private void RefreshOverlays()
    {
        foreach (var kv in _overlayByAnchor)
        {
            if (kv.Value != null) { kv.Value.SetActive(false); Destroy(kv.Value); }
        }
        _overlayByAnchor.Clear();

        for (int i = 0; i < _cells.Count; i++)
        {
            var s = _inventory.GetSlot(i);
            if (s == null || s.AnchorIndex != i) continue; // 只在锚点格渲染

            var go = BuildIconGroup(s, FootprintSize(s.OccW, s.OccH), s.orient, false);
            go.name = "Item_" + i;
            go.transform.SetParent(_gridRoot, false);
            // 关键：脱离GridLayoutGroup控制——否则覆盖层会被当成网格子项强制74×74并排到第20格之后
            var ign = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            ign.ignoreLayout = true;

            // 覆盖区域几何：左上角起点=padding+锚点*(格+间距)
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(
                _view.grid.padding.left + s.anchorCol * (_view.grid.cellSize.x + _view.grid.spacing.x),
                -(_view.grid.padding.top + s.anchorRow * (_view.grid.cellSize.y + _view.grid.spacing.y)));
            _overlayByAnchor[i] = go;
        }

        // 品质背景：格子底色按物品品质淡染（None=默认面板白；颜色与开箱光柱同族）
        // 按_cells.Count遍历而非SlotCount：动态容量下格子数=当前Capacity（无背包10/装背包20），
        // SlotCount是数组上限，用它做下标会在容量未满时越界（实测开包即崩的根因）
        for (int i = 0; i < _cells.Count; i++)
        {
            var s = _inventory.GetStackAtCell(i);
            var img = _cells[i].GetComponent<Image>();
            bool tinted = s != null && s.item != null && s.item.rarity != ItemRarity.None;
            img.color = tinted
                ? Color.Lerp(_cells[i].BaseColor, ItemRarityStyle.GetColor(s.item.rarity), _view.rarityTint)
                : _cells[i].BaseColor;
        }
    }

    private void RefreshDetail()
    {
        var stack = _selectedIndex >= 0 ? _inventory.GetSlot(_selectedIndex) : null;
        if (stack == null)
        {
            _detailTitle.text = "未选中物品";
            _detailDesc.text = "悬停查看详情\n右键 → 使用/装备/拆分/丢弃\n左键拖拽 → 移动/交换/装备\n拖到快捷栏 → 绑定消耗品\n快捷栏拖回背包 → 解除绑定\n拖到面板右外 → 丢弃\n拖拽中按R键旋转";
            return;
        }

        _detailTitle.text = stack.item.itemName;
        var sb = new StringBuilder();
        sb.AppendLine(GetTypeLabel(stack.item.type));
        if (stack.item.rarity != ItemRarity.None)
        {
            sb.AppendLine("品质: " + ItemRarityStyle.GetName(stack.item.rarity));
        }
        sb.AppendLine("尺寸: " + stack.OccW + "×" + stack.OccH
            + (stack.OccW != stack.DefW || stack.OccH != stack.DefH ? "（竖放）" : ""));
        if (stack.item.attackPower > 0) sb.AppendLine("攻击 +" + stack.item.attackPower);
        if (stack.item.defensePower > 0) sb.AppendLine("防御 +" + stack.item.defensePower);
        if (stack.item.healAmount > 0) sb.AppendLine("回复 +" + stack.item.healAmount);
        if (stack.item.type == ItemType.Backpack)
            sb.AppendLine("物品栏: 基础10 +" + stack.item.capacityBonus + "格");
        sb.AppendLine("数量: " + stack.count);
        sb.AppendLine("位置: 行" + stack.anchorRow + " 列" + stack.anchorCol);
        _detailDesc.text = sb.ToString();
    }

    private void RefreshSlotCount()
    {
        // 多格物品按实际占用格计数（与bool[4,5]占用数组一致）
        _slotCountText.text = "格子 " + _inventory.OccupiedCells + "/" + _inventory.Capacity;
    }

    // ===== 右键操作菜单（使用/装备/拆分/丢弃） =====
    // （左键点击不再承担选中——选中由悬停驱动，见OnCellHover）

    // ===== 右键操作菜单（使用/装备/拆分/丢弃） =====

    /// <summary>格子右键：在鼠标处弹出操作菜单（按物品类型与堆叠数量显示可用项）</summary>
    public void OnCellRightClick(InventoryCellUI cell)
    {
        if (cell.Kind == InventoryCellUI.CellKind.EquipBag)
        {
            HideMenu();
            if (_inventory.Backpack == null) return;
            var menu = BuildMenuShell();
            AddMenuItem(true, "取 下", () => UnequipBackpack());
            PositionMenu(menu);
            return;
        }
        if (cell.Kind != InventoryCellUI.CellKind.Bag) return;
        var stack = _inventory.GetStackAtCell(cell.SlotIndex);
        if (stack == null) { HideMenu(); return; } // 空格：收起菜单
        ShowMenu(cell, stack);
    }

    /// <summary>弹出操作菜单：使用(消耗品)/装备(武器护甲)/拆分(可叠加且数量>1)/丢弃(恒有)；快捷栏放置改走拖拽（2026-10-09取消菜单分配）</summary>
    private void ShowMenu(InventoryCellUI cell, Inventory.ItemStack stack)
    {
        HideMenu();
        int anchor = stack.AnchorIndex;
        var item = stack.item;

        var rt = BuildMenuShell();

        AddMenuItem(item.type == ItemType.Consumable, "使 用", () => UseWithChannel(anchor));
        AddMenuItem(item.type == ItemType.Weapon, "装 备", () => TryEquipFromMenu(anchor, EquipSlotType.Weapon));
        AddMenuItem(item.type == ItemType.Armor, "装 备", () => TryEquipFromMenu(anchor, EquipSlotType.Armor));
        AddMenuItem(item.type == ItemType.Backpack, "装 备", () => TryEquipBackpackFromMenu(anchor));
        AddMenuItem(item.maxStack > 1 && stack.count > 1, "拆 分", () => OpenSplitPopup(anchor));
        AddMenuItem(true, "丢 弃", () => _inventory.DropItem(anchor));

        PositionMenu(rt);
    }

    /// <summary>重建菜单壳（白底黑框竖排容器，挂在画布顶层）；返回其RectTransform供定位</summary>


    /// <summary>菜单定位到鼠标处（夹取在画布内不出屏）并置顶</summary>
    private void PositionMenu(RectTransform rt)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        var local = GetCanvasLocalPos(Input.mousePosition);
        float halfW = rt.rect.width * 0.5f + 4f;
        float halfH = rt.rect.height * 0.5f + 4f;
        local.x = Mathf.Clamp(local.x, -_canvasRect.rect.width * 0.5f + halfW, _canvasRect.rect.width * 0.5f - halfW);
        local.y = Mathf.Clamp(local.y, -_canvasRect.rect.height * 0.5f + halfH, _canvasRect.rect.height * 0.5f - halfH);
        rt.anchoredPosition = local;
        _menuRoot.transform.SetAsLastSibling();
    }

    /// <summary>添加菜单项（visible=false不创建；点击=音效+动作+收起菜单）</summary>
    private void AddMenuItem(bool visible, string label, System.Action action)
    {
        if (!visible) return;
        var btn = Instantiate(_view.menuButtonTemplate, _menuRoot.transform, false);
        btn.gameObject.SetActive(true);
        btn.GetComponentInChildren<Text>(true).text = label;
        btn.onClick.AddListener(() => { AudioManager.PlaySfx("SFX_UIClick"); HideMenu(); action(); });
    }

    /// <summary>收起右键菜单</summary>
    private void HideMenu()
    {
        if (_menuRoot != null)
        {
            Destroy(_menuRoot);
            _menuRoot = null;
        }
    }

    /// <summary>菜单装备背包动作：取出堆后走TryEquipBackpack（换包回收旧背包）</summary>
    private void TryEquipBackpackFromMenu(int anchor)
    {
        var stack = _inventory.GetSlot(anchor);
        if (stack != null) TryEquipBackpack(stack);
    }

    /// <summary>菜单装备动作：类型校验+换装（失败走提示行）</summary>
    private void TryEquipFromMenu(int anchor, EquipSlotType slotType)
    {
        var stack = _inventory.GetSlot(anchor);
        if (stack == null) return;
        if (!_inventory.CanEquip(stack.item, slotType))
        {
            ShowHint(stack.item.itemName + " 无法装备");
            return;
        }
        if (!_inventory.TryEquipFromBag(anchor, slotType))
        {
            ShowHint("背包空间不足，无法换装");
            return;
        }
        _selectedIndex = -1;
        RefreshAll();
    }

    /// <summary>
    /// 背包使用消耗品（右键菜单入口）：走HotbarController的1.5s食用通道——
    /// 进度圈绕背包图标、移动减速50%、受伤/物品被移动打断不消耗；
    /// 被拒绝（满血/正在食用等）走提示行；HotbarController缺失时回退直用（防御）
    /// </summary>
    private void UseWithChannel(int anchor)
    {
        var hotbar = GetComponent<HotbarController>(); // 懒取：HotbarController由HUDController.Start自动补挂，时序无保证
        if (hotbar == null)
        {
            _inventory.UseItem(anchor);
            return;
        }
        Transform icon = TryGetOverlayIcon(anchor, out var t) ? t : null;
        var reason = hotbar.TryUseBagAnchor(anchor, icon);
        if (reason != null) ShowHint(reason);
    }

    /// <summary>获取背包物品覆盖层图标（食用通道把进度圈挂上去用；无覆盖层返回false）</summary>
    public bool TryGetOverlayIcon(int anchor, out Transform icon)
    {
        if (_overlayByAnchor.TryGetValue(anchor, out var go) && go != null)
        {
            icon = go.transform;
            return true;
        }
        icon = null;
        return false;
    }

    // ===== 右上角关闭按钮 =====

    // ===== 拆分弹窗（滑块：－ ---o--- ＋，取消/确定） =====

    /// <summary>打开拆分弹窗：滑块范围1~count-1（位置与物品数量相关），标题带物品名</summary>
    private void OpenSplitPopup(int anchor)
    {
        var stack = _inventory.GetSlot(anchor);
        if (stack == null || stack.count < 2 || stack.item.maxStack <= 1) return;
        _splitAnchor = anchor;
        if (_splitRoot == null) BuildSplitPopup();
        int max = stack.count - 1;
        _splitTitleText.text = "拆分 - " + stack.item.itemName;
        _splitSlider.minValue = 1;
        _splitSlider.maxValue = max;
        _splitSlider.value = Mathf.Max(1, max / 2);
        RefreshSplitQty();
        _splitRoot.SetActive(true);
        _splitRoot.transform.SetAsLastSibling();
    }

    /// <summary>构建拆分弹窗（懒加载一次；挂在背包面板中央，ignoreLayout脱离竖排流）</summary>


    /// <summary>构建白盒风格滑块（背景条+黑色填充+白底黑框方形把手）</summary>


    /// <summary>－/＋按键步进拆分数量（夹取在滑块范围内）</summary>
    private void StepSplit(int delta)
    {
        if (_splitSlider == null) return;
        _splitSlider.value = Mathf.Clamp(_splitSlider.value + delta, _splitSlider.minValue, _splitSlider.maxValue);
    }

    /// <summary>滑块变化：刷新数量显示（拆分数量: N / max）</summary>
    private void RefreshSplitQty()
    {
        if (_splitQtyText != null && _splitSlider != null)
            _splitQtyText.text = "拆分数量: " + (int)_splitSlider.value + " / " + (int)_splitSlider.maxValue;
    }

    /// <summary>确定：调用Inventory.SplitItem拆出新堆（空间不足走提示行）</summary>
    private void OnSplitConfirm()
    {
        if (_splitAnchor < 0 || _splitSlider == null) return;
        int n = (int)_splitSlider.value;
        if (!_inventory.SplitItem(_splitAnchor, n))
        {
            ShowHint("背包空间不足，无法拆分");
            return;
        }
        HideSplitPopup();
        ShowHint("已拆分出 x" + n);
    }

    /// <summary>取消/完成后收起弹窗</summary>
    private void HideSplitPopup()
    {
        if (_splitRoot != null) _splitRoot.SetActive(false);
        _splitAnchor = -1;
    }

    // ===== 拖拽系统（多格占位） =====

    /// <summary>开始拖拽：定位被拖物品堆（多格物品任意覆盖格可拖），
    /// 记录抓取偏移，创建按物品尺寸的半透明跟随图标</summary>
    public void OnCellBeginDrag(InventoryCellUI cell)
    {
        _dragStack = null;
        _dragFromEquip = false;
        _dragFromHotbar = false;

        if (cell.Kind == InventoryCellUI.CellKind.Hotbar)
        {
            // 快捷栏槽拖起=拿起"绑定"（物品本体始终在背包不移动）；空槽不可拖
            var hbItem = _inventory.GetHotbarItem(cell.SlotIndex);
            if (hbItem == null) return;
            _dragFromHotbar = true;
            _dragStack = new Inventory.ItemStack
            {
                item = hbItem,
                count = _inventory.GetHotbarCount(cell.SlotIndex), // 角标显示背包内总数
                anchorRow = 0,
                anchorCol = 0,
                orient = ItemOrient.Horizontal
            };
        }
        else if (cell.Kind == InventoryCellUI.CellKind.Bag)
        {
            _dragStack = _inventory.GetStackAtCell(cell.SlotIndex); // 覆盖格→所属物品
        }
        else
        {
            var item = cell.Kind == InventoryCellUI.CellKind.EquipBag ? _inventory.Backpack
                : cell.Kind == InventoryCellUI.CellKind.EquipWeapon ? _inventory.EquippedWeapon : _inventory.EquippedArmor;
            if (item != null)
            {
                _dragFromEquip = true;
                _dragEquipPossible = cell.Kind == InventoryCellUI.CellKind.EquipBag
                    ? _inventory.CanUnequipBackpack() : _inventory.HasSpaceFor(item, 1); // 卸下重寻位是否有空间
                // 虚拟堆仅用于尺寸/方向显示（不入槽位）
                _dragStack = new Inventory.ItemStack { item = item, count = 1, anchorRow = 0, anchorCol = 0, orient = ItemOrient.Horizontal };
            }
        }
        if (_dragStack == null) return; // 空格/空装备槽不可拖

        // 方向记录在独立字段_dragOrient（严禁直接改数据层栈的orient，否则占用数组失同步）
        _dragOrient = _dragFromEquip ? ItemOrient.Horizontal : _dragStack.orient;

        // 抓取偏移（装备槽拖出视为抓锚点）
        int grabIndex = cell.Kind == InventoryCellUI.CellKind.Bag ? cell.SlotIndex : _dragStack.AnchorIndex;
        _grabRow = grabIndex / Inventory.Cols - _dragStack.anchorRow;
        _grabCol = grabIndex % Inventory.Cols - _dragStack.anchorCol;

        _dragging = true;
        _dragSource = cell;
        CreateGhost(_dragStack, _dragOrient);
        SetSourceDim(cell, 0.3f); // 源物品文字变暗表示"已被拿起"
    }

    /// <summary>拖拽中：半透明图标跟随鼠标（屏幕坐标→Canvas局部坐标）</summary>
    public void OnCellDrag(InventoryCellUI cell, PointerEventData eventData)
    {
        if (!_dragging || _ghost == null) return;
        _ghost.GetComponent<RectTransform>().anchoredPosition = GetCanvasLocalPos(eventData.position);
    }

    /// <summary>拖拽结束：按预览结果提交移动/交换/装备/卸下，否则物品回弹原位</summary>
    public void OnCellEndDrag(InventoryCellUI cell)
    {
        if (!_dragging) return;

        // 拖出面板右侧外部 = 丢弃整组物品（仅背包内拖拽；装备槽拖出只支持卸下）
        bool discard = TryDiscardOutsideRight();
        if (!discard)
        {
            if (_dragSource.Kind == InventoryCellUI.CellKind.Bag && _hoverCell != null && _hoverCell.Kind == InventoryCellUI.CellKind.EquipBag)
                TryEquipBackpack(_dragStack);
            else
                CommitDrop(_dragSource, _hoverCell);
        }

        // 清理拖拽表现（无论提交成功与否）
        Destroy(_ghost);
        _ghost = null;
        SetSourceDim(_dragSource, 1f);
        ClearPreviewHighlights();
        _hoverCell = null;
        _dragSource = null;
        _dragStack = null;
        _dragFromEquip = false;
        _dragFromHotbar = false;
        _dragOrient = ItemOrient.Horizontal;
        _previewAnchor = -1;
        _previewSwap = null;
        _previewMerge = false;
        _dragging = false;

        RefreshAll();
    }

    /// <summary>
    /// 悬停进入/离开（配置表交互优化：悬停即选中，不需要点击）——
    /// 非拖拽时：指向有物品的格=选中该物品（加粗边框+详情框实时显示），移开=取消选中；
    /// 拖拽中：实时计算预览区域并整组上色（绿=合法/红=非法）
    /// </summary>
    public void OnCellHover(InventoryCellUI cell, bool enter)
    {
        if (!_dragging)
        {
            var stack = enter && cell.Kind == InventoryCellUI.CellKind.Bag
                ? _inventory.GetStackAtCell(cell.SlotIndex) : null;
            int sel = stack != null ? stack.AnchorIndex : -1;
            if (sel != _selectedIndex)
            {
                _selectedIndex = sel;
                RefreshGrid();
                RefreshDetail();
            }
            return;
        }

        if (enter)
        {
            _hoverCell = cell;
            EvaluatePreview(cell);
        }
        else
        {
            if (_hoverCell == cell)
            {
                _hoverCell = null;
                ClearPreviewHighlights();
                _previewAnchor = -1;
                _previewSwap = null;
                _previewMerge = false;
            }
        }
    }

    /// <summary>清除预览高亮：全部格子/装备槽恢复默认或选中边框</summary>
    private void ClearPreviewHighlights()
    {
        RefreshGridBorders();
    }

    /// <summary>
    /// 评估拖拽预览：计算目标锚点（抓取偏移+边界夹取），
    /// 空白区域→CanPlace判定；覆盖单件物品→形状一致则吸附交换、否则红框拒绝；
    /// 装备槽目标→类型校验；装备槽拖出→按是否有卸下空间统一绿/红
    /// </summary>
    private void EvaluatePreview(InventoryCellUI hover)
    {
        ClearPreviewHighlights();
        _previewSwap = null;
        _previewMerge = false;
        _previewAnchor = -1;
        _previewLegal = false;

        if (hover.Kind == InventoryCellUI.CellKind.EquipBag)
        {
            bool allowed = !_dragFromEquip && !_dragFromHotbar && _dragStack != null && _dragStack.item.type == ItemType.Backpack;
            hover.SetHighlight(SelectedBorder, allowed ? LegalColor : IllegalColor);
            _previewLegal = allowed;
            return;
        }
        var toSlot = CellEquipSlot(hover);

        // —— 目标是装备槽（只能从背包拖入） ——
        if (toSlot != null)
        {
            bool ok = !_dragFromEquip && !_dragFromHotbar && _dragStack != null && _inventory.CanEquip(_dragStack.item, toSlot.Value);
            hover.SetHighlight(SelectedBorder, ok ? LegalColor : IllegalColor); // 合法绿框加粗/非法红框
            _previewLegal = ok;
            return;
        }

        // —— 目标是快捷栏槽：消耗品绿框（快捷栏间拖动恒绿），其它类型红框 ——
        if (hover.Kind == InventoryCellUI.CellKind.Hotbar)
        {
            bool ok = _dragFromHotbar
                || (_dragStack != null && _dragStack.item.type == ItemType.Consumable);
            hover.SetHighlight(SelectedBorder, ok ? LegalColor : IllegalColor);
            _previewLegal = ok;
            return;
        }

        // —— 源是装备槽：卸下走FindSlot重新寻位，悬停格统一绿/红 ——
        if (_dragFromEquip)
        {
            hover.SetHighlight(SelectedBorder, _dragEquipPossible ? LegalColor : IllegalColor);
            _previewLegal = _dragEquipPossible;
            return;
        }

        // —— 源是快捷栏槽：拖回背包=解除绑定，恒绿（物品本体不动，只清槽位） ——
        if (_dragFromHotbar)
        {
            hover.SetHighlight(SelectedBorder, LegalColor);
            _previewLegal = true;
            return;
        }

        // —— 背包内移动/交换 ——
        int hRow = hover.SlotIndex / Inventory.Cols, hCol = hover.SlotIndex % Inventory.Cols;
        // 占用宽高按拖拽方向（_dragOrient，R键可切换）计算，不读数据层栈的orient
        int occW = Inventory.OccWOf(_dragStack.item, _dragOrient);
        int occH = Inventory.OccHOf(_dragStack.item, _dragOrient);
        if (occW > Inventory.Cols || occH > Inventory.Rows)
        {
            // 物品比整张网格还大（异常配置）：整图红框拒绝
            HighlightRegion(0, 0, Inventory.Rows, Inventory.Cols, false);
            return;
        }

        // 抓取偏移保持手感：被抓住的格子对准悬停格，锚点夹取到边界内
        int aRow = Mathf.Clamp(hRow - _grabRow, 0, Inventory.Rows - occH);
        int aCol = Mathf.Clamp(hCol - _grabCol, 0, Inventory.Cols - occW);

        // 目标区域覆盖到的其它物品堆（不含被拖物品自身）
        var others = StacksInRegion(aRow, aCol, occH, occW, _dragStack);
        if (others.Count == 0)
        {
            // 空白区域（或仅与自身原占位重叠）：用CanPlaceExcluding排除自身格子后判定——
            // 移动到与自身原占位部分重叠的位置是合法的（提交时物品会先腾空原占位）
            _previewLegal = _inventory.CanPlaceExcluding(_dragStack, aRow, aCol, _dragOrient);
            _previewAnchor = aRow * Inventory.Cols + aCol;
        }
        else if (others.Count == 1
            && others[0].OccW == occW && others[0].OccH == occH && others[0].orient == _dragOrient)
        {
            // 形状一致：吸附到目标物品锚点（预览区域与其占位完全重合）→允许交换
            _previewSwap = others[0];
            // 同类可堆叠物品：本次拖拽语义为叠加合并（目标已满时提交端回退交换）
            _previewMerge = _dragStack.item == others[0].item && _dragStack.item.maxStack > 1;
            _previewAnchor = others[0].AnchorIndex;
            _previewLegal = true;
            aRow = others[0].anchorRow;
            aCol = others[0].anchorCol;
        }
        else
        {
            _previewLegal = false; // 区域内有多件物品或形状不同：红框拒绝
        }

        HighlightRegion(aRow, aCol, occH, occW, _previewLegal);
    }

    /// <summary>给预览占位区域的每个格子上色（合法绿框/非法红框）</summary>
    private void HighlightRegion(int row, int col, int occH, int occW, bool legal)
    {
        Color color = legal ? LegalColor : IllegalColor;
        for (int r = row; r < row + occH; r++)
        {
            for (int c = col; c < col + occW; c++)
            {
                int idx = r * Inventory.Cols + c;
                if (idx >= 0 && idx < _cells.Count)
                {
                    _cells[idx].SetHighlight(SelectedBorder, color);
                }
            }
        }
    }

    /// <summary>收集预览区域覆盖到的物品堆（矩形重叠用四角覆盖判定，排除被拖物品自身）</summary>
    private List<Inventory.ItemStack> StacksInRegion(int row, int col, int occH, int occW, Inventory.ItemStack exclude)
    {
        var list = new List<Inventory.ItemStack>();
        int rMax = row + occH - 1, cMax = col + occW - 1;
        for (int i = 0; i < (Inventory.SlotCount); i++)
        {
            var s = _inventory.GetSlot(i);
            if (s == null || ReferenceEquals(s, exclude)) continue;
            if (s.Covers(row, col) || s.Covers(row, cMax) || s.Covers(rMax, col) || s.Covers(rMax, cMax))
            {
                list.Add(s);
            }
        }
        return list;
    }

    /// <summary>
    /// 提交拖拽结果：所有数据修改经Inventory核心方法完成；
    /// 无预览/预览非法/未命中目标 = 物品回弹原位（不做任何修改）
    /// </summary>
    private void CommitDrop(InventoryCellUI from, InventoryCellUI to)
    {
        if (from == null || to == null) return; // 拖出背包：回弹

        // —— 背包 → 快捷栏：拖放绑定消耗品（绑定按物品引用跟随；同槽已绑定同一物品=无操作） ——
        if (from.Kind == InventoryCellUI.CellKind.Bag && to.Kind == InventoryCellUI.CellKind.Hotbar)
        {
            if (_dragStack == null) return;
            if (_dragStack.item.type != ItemType.Consumable)
            {
                ShowHint("只有消耗品能放入快捷栏"); // 回弹
                return;
            }
            int hb = to.SlotIndex;
            if (_inventory.GetHotbarItem(hb) == _dragStack.item)
            {
                ShowHint(_dragStack.item.itemName + " 已在快捷栏" + (hb + 1));
                return;
            }
            int prevSlot = _inventory.GetHotbarSlotOfItem(_dragStack.item);
            _inventory.SetHotbarItem(hb, _dragStack.item); // 数据层同物品唯一：旧槽绑定自动清除=更换位置
            ShowHint(prevSlot >= 0
                ? _dragStack.item.itemName + " 已从快捷栏" + (prevSlot + 1) + "换到快捷栏" + (hb + 1)
                : _dragStack.item.itemName + " → 快捷栏" + (hb + 1) + "（按数字键" + (hb + 1) + "使用）");
            return;
        }

        // —— 快捷栏 → 背包：解除绑定（物品本体始终在背包，不发生位移） ——
        if (from.Kind == InventoryCellUI.CellKind.Hotbar && to.Kind == InventoryCellUI.CellKind.Bag)
        {
            int src = from.SlotIndex;
            if (_inventory.GetHotbarItem(src) == null) return;
            _inventory.ClearHotbarItem(src);
            ShowHint("快捷栏" + (src + 1) + "已解除绑定");
            return;
        }

        // —— 快捷栏 → 快捷栏：换位（清空源槽，覆盖目标槽原绑定） ——
        if (from.Kind == InventoryCellUI.CellKind.Hotbar && to.Kind == InventoryCellUI.CellKind.Hotbar)
        {
            int src = from.SlotIndex, dst = to.SlotIndex;
            var hbItem = _inventory.GetHotbarItem(src);
            if (hbItem == null || src == dst) return;
            _inventory.SetHotbarItem(dst, hbItem);
            _inventory.ClearHotbarItem(src);
            return;
        }

        if (from.Kind == InventoryCellUI.CellKind.EquipBag)
        {
            if (to.Kind == InventoryCellUI.CellKind.Bag) UnequipBackpack();
            return;
        }
        var fromSlot = CellEquipSlot(from);
        var toSlot = CellEquipSlot(to);

        // —— 装备槽 → 背包：卸下（FindSlot重新寻位） ——
        if (fromSlot != null && to.Kind == InventoryCellUI.CellKind.Bag)
        {
            if (!_previewLegal)
            {
                ShowHint("背包空间不足，无法卸下"); // 回弹
                return;
            }
            if (!_inventory.Unequip(fromSlot.Value))
            {
                ShowHint("背包空间不足，无法卸下");
                return;
            }
            _selectedIndex = -1;
            return;
        }

        // —— 背包 → 装备槽：类型校验装备（旧装备重新寻位） ——
        if (from != null && from.Kind == InventoryCellUI.CellKind.Bag && toSlot != null)
        {
            if (_dragStack == null) return;
            if (!_inventory.CanEquip(_dragStack.item, toSlot.Value))
            {
                ShowHint(_dragStack.item.itemName + " 无法放入该槽位（类型不匹配）"); // 回弹
                return;
            }
            if (!_inventory.TryEquipFromBag(_dragStack.AnchorIndex, toSlot.Value))
            {
                ShowHint("背包空间不足，无法换装"); // 旧装备重寻位失败，回弹
                return;
            }
            _selectedIndex = -1;
            return;
        }

        // —— 背包 → 背包：按预览结果移动或交换 ——
        if (from != null && from.Kind == InventoryCellUI.CellKind.Bag && to.Kind == InventoryCellUI.CellKind.Bag)
        {
            if (_previewAnchor < 0 || !_previewLegal) return; // 红框/无预览：回弹

            if (_previewSwap != null)
            {
                if (_previewMerge)
                {
                    int before = _dragStack.count; // MergeStacks可能部分合并（_dragStack即源堆对象，count会被改），先留旧值算剩余
                    int moved = _inventory.MergeStacks(_dragStack.AnchorIndex, _previewSwap.AnchorIndex);
                    if (moved > 0)
                    {
                        ShowHint(moved < before
                            ? "目标堆已满，叠加 x" + moved + "（原堆剩余 x" + (before - moved) + "）"
                            : "已叠加 x" + moved);
                        _selectedIndex = -1; // 合并后清除选中，避免菜单指向错位
                        return;
                    }
                    ShowHint("目标堆已满，无法叠加"); // 目标已满：回退走交换位置
                }
                if (!_inventory.SwapItems(_dragStack.AnchorIndex, _previewSwap.AnchorIndex))
                {
                    ShowHint("物品形状不同，无法交换"); // 兜底（预览已过滤，理论上不达）
                    return;
                }
            }
            else
            {
                // 带方向提交：R键原地旋转（from==to）与移动/旋转一体提交都走这里
                if (!_inventory.MoveItem(_dragStack.AnchorIndex, _previewAnchor, _dragOrient))
                {
                    ShowHint("无法放置到该位置"); // 兜底回弹
                    return;
                }
            }
            _selectedIndex = -1; // 物品换位后清除选中，避免菜单指向错位
            return;
        }

        // —— 装备槽 → 装备槽：不支持互换 ——
        if (fromSlot != null && toSlot != null && fromSlot.Value != toSlot.Value)
        {
            ShowHint("武器槽与护甲槽不能互换");
        }
    }

    /// <summary>
    /// 拖拽结束时鼠标在面板右缘之外（+8px防误丢缓冲）= 丢弃整组物品；
    /// 仅限背包内拖拽（装备槽拖出走卸下重寻位，不支持丢弃）；
    /// 丢弃语义与"丢弃"按钮一致（Inventory.DropItem整组移除），反馈走提示行
    /// </summary>
    private bool TryDiscardOutsideRight()
    {
        if (_dragFromEquip || _dragStack == null || _dragSource == null
            || _dragSource.Kind != InventoryCellUI.CellKind.Bag) return false;

        // Overlay画布的世界坐标==屏幕像素，面板右上角世界x即屏幕右缘
        var corners = new Vector3[4];
        _rootPanel.GetComponent<RectTransform>().GetWorldCorners(corners);
        float rightEdge = corners[2].x;
        if (Input.mousePosition.x <= rightEdge + 8f) return false;

        string name = _dragStack.item.itemName;
        int count = _dragStack.count;
        _inventory.DropItem(_dragStack.AnchorIndex);
        ShowHint("已丢弃 " + name + (count > 1 ? " x" + count : ""));
        return true;
    }

    /// <summary>格子对应的装备槽类型（背包格返回null）</summary>
    private EquipSlotType? CellEquipSlot(InventoryCellUI cell)
    {
        switch (cell.Kind)
        {
            case InventoryCellUI.CellKind.EquipWeapon: return EquipSlotType.Weapon;
            case InventoryCellUI.CellKind.EquipArmor: return EquipSlotType.Armor;
            default: return null;
        }
    }

    /// <summary>是否为当前选中物品的覆盖格（取消高亮时用于恢复选中边框）</summary>
    public bool IsCellSelected(InventoryCellUI cell)
    {
        return cell.Kind == InventoryCellUI.CellKind.Bag && IsCellOfSelected(cell.SlotIndex);
    }

    /// <summary>屏幕坐标→Canvas局部坐标（Overlay模式camera传null）</summary>
    private Vector2 GetCanvasLocalPos(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out var local);
        return local;
    }

    /// <summary>
    /// 创建半透明跟随图标：复用占位图标组样式（剪影+单字/Sprite+名称/角标），
    /// 尺寸=按当前拖拽方向的占位区域；CanvasGroup整体半透明；全部raycastTarget关闭
    /// </summary>
    private void CreateGhost(Inventory.ItemStack stack, ItemOrient orient)
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost = BuildIconGroup(stack,
            FootprintSize(Inventory.OccWOf(stack.item, orient), Inventory.OccHOf(stack.item, orient)),
            orient, true);
        _ghost.name = "DragGhost";
        _ghost.transform.SetParent(_canvasRect, false);
        _ghost.GetComponent<RectTransform>().anchoredPosition = GetCanvasLocalPos(Input.mousePosition);
    }

    /// <summary>按R旋转后重建Ghost：尺寸宽高互换、中央图标旋转90°，位置保持在鼠标处</summary>
    private void RefreshGhostForOrient()
    {
        if (_ghost == null || _dragStack == null) return;
        var pos = _ghost.GetComponent<RectTransform>().anchoredPosition;
        CreateGhost(_dragStack, _dragOrient);
        _ghost.GetComponent<RectTransform>().anchoredPosition = pos;
    }

    /// <summary>
    /// 拖拽中按R：切换拖拽方向（仅非等比物品；等比物品提示无需旋转）；
    /// 只改独立字段_dragOrient（数据层栈的orient由MoveItem提交时才更新）；
    /// 旋转后重算占用宽高→夹取锚点/抓取偏移→清除旧吸附目标→重新评估预览→Ghost同步翻转
    /// </summary>
    private void TryRotateDragged()
    {
        if (_dragFromEquip || _dragFromHotbar) return; // 装备槽/快捷栏槽拖出不响应手动旋转（快捷栏绑定恒横向单格）
        var item = _dragStack.item;
        if (item.width == item.height)
        {
            ShowHint("该物品无需旋转"); // 等比物品（如护甲2×2）旋转无意义
            return;
        }
        var newOrient = _dragOrient == ItemOrient.Horizontal ? ItemOrient.Vertical : ItemOrient.Horizontal;
        int nw = Inventory.OccWOf(item, newOrient), nh = Inventory.OccHOf(item, newOrient);
        if (nw > Inventory.Cols || nh > Inventory.Rows)
        {
            ShowHint("旋转后超出背包边界"); // 方向判断不得越界
            return;
        }

        _dragOrient = newOrient;
        _grabRow = Mathf.Clamp(_grabRow, 0, nh - 1); // 抓取偏移按新足迹夹取
        _grabCol = Mathf.Clamp(_grabCol, 0, nw - 1);
        _previewSwap = null;                          // 方向变了，旧吸附目标失效
        _previewMerge = false;
        RefreshGhostForOrient();                      // Ghost尺寸宽高互换+图标旋转90°
        if (_hoverCell != null) EvaluatePreview(_hoverCell); // 重新评估预览（绿/红按新方向）
    }

    /// <summary>物品类型对应的颜色（剪影浅底/单字深色均由它派生）</summary>
    private static Color TypeColor(ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon: return new Color(0.85f, 0.55f, 0.2f, 1f);  // 武器=橙
            case ItemType.Armor: return new Color(0.35f, 0.55f, 0.85f, 1f);  // 防具=蓝
            case ItemType.Consumable: return new Color(0.35f, 0.75f, 0.4f, 1f); // 消耗品=绿
            default: return new Color(0.6f, 0.6f, 0.6f, 1f);                 // 材料=灰
        }
    }

    /// <summary>物品类型对应的中央单字图标</summary>
    private static string TypeIconChar(ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon: return "剑";
            case ItemType.Armor: return "甲";
            case ItemType.Consumable: return "瓶";
            default: return "币";
        }
    }

    /// <summary>占位区域的像素尺寸（格数展开含间距）</summary>
    private Vector2 FootprintSize(int occW, int occH)
    {
        var grid = _gridRoot.GetComponent<GridLayoutGroup>();
        return new Vector2(occW * grid.cellSize.x + (occW - 1) * grid.spacing.x, occH * grid.cellSize.y + (occH - 1) * grid.spacing.y);
    }

    /// <summary>
    /// 构建物品图标组（占位图标块）：类型色浅底剪影（铺满全区）+ 中央单字/Sprite（竖放旋转90°）
    /// + 底部名称小字 + 右下数量角标（仅count>1）；root带CanvasGroup（Ghost靠它整体半透明）；
    /// 所有Graphic的raycastTarget关闭，不挡格子的点击/拖拽/悬停事件
    /// （公开静态：快捷栏HUD/背包镜像复用同一占位图标风格）
    /// </summary>


    /// <summary>设置拖拽源的整体透明度（拿起变暗0.3/放下恢复1）：
    /// 背包物品=覆盖层CanvasGroup（剪影+文字一起变暗）；装备槽=图标块CanvasGroup+槽内文字</summary>
    private void SetSourceDim(InventoryCellUI cell, float alpha)
    {
        if (_dragStack == null) return;
        if (cell.Kind == InventoryCellUI.CellKind.Bag)
        {
            if (_overlayByAnchor.TryGetValue(_dragStack.AnchorIndex, out var ov) && ov != null)
            {
                var cg = ov.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = alpha;
            }
        }
        else if (cell.Kind == InventoryCellUI.CellKind.Hotbar)
        {
            var icon = _hotbarIcons[cell.SlotIndex];
            if (icon != null)
            {
                var cg = icon.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = alpha;
            }
        }
        else if (cell.Kind == InventoryCellUI.CellKind.EquipBag)
        {
            var color = _bagSil.color;
            color.a = alpha;
            _bagSil.color = color;
        }
        else
        {
            var icon = cell.Kind == InventoryCellUI.CellKind.EquipWeapon ? _weaponEquipIcon : _armorEquipIcon;
            if (icon != null)
            {
                var cg = icon.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = alpha;
            }
            SetGoTextAlpha(cell.gameObject, alpha);
        }
    }

    /// <summary>设置物体下所有Text的透明度（文字均为LineColor黑色）</summary>
    private void SetGoTextAlpha(GameObject go, float alpha)
    {
        foreach (var t in go.GetComponentsInChildren<Text>(true))
        {
            t.color = new Color(t.color.r, t.color.g, t.color.b, alpha);
        }
    }

    /// <summary>面板内操作提示（空间不足/类型不匹配等），显示1.6秒后淡出</summary>
    public void ShowHint(string message)
    {
        if (_hintRoutine != null) StopCoroutine(_hintRoutine);
        _hintText.text = message;
        _hintText.color = _hintColor;
        _hintRoutine = StartCoroutine(HintFadeRoutine());
    }

    /// <summary>提示淡出协程</summary>
    private IEnumerator HintFadeRoutine()
    {
        yield return new WaitForSeconds(1.6f);
        const float fade = 0.4f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / fade)
        {
            _hintText.color = new Color(_hintColor.r, _hintColor.g, _hintColor.b, _hintColor.a * (1f - t));
            yield return null;
        }
        _hintText.text = "";
        _hintText.color = _hintColor;
        _hintRoutine = null;
    }

    // ===== 辅助 =====

    private static string GetTypeLabel(ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon: return "类型: 武器";
            case ItemType.Armor: return "类型: 防具";
            case ItemType.Consumable: return "类型: 消耗品";
            default: return "类型: 材料";
        }
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }

    // ===== UI构建工具（白盒线框风格：黑字 + 统一黑色线条 + 无填充色块） =====

    /// <summary>创建实线边框容器：填充与面板同色（视觉无色块）+ 统一粗细黑色边框</summary>


    /// <summary>将矩形锚点拉伸铺满父级</summary>
    private static void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private bool BindPanel(Transform canvasRoot)
    {
        var refs = GameUIRefs.Instance;
        if (refs == null || refs.inventoryPanelPrefab == null)
        { Debug.LogError("[InventoryUI] GameUIRefs.inventoryPanelPrefab 未配置"); return false; }
        _rootPanel = Instantiate(refs.inventoryPanelPrefab, canvasRoot, false);
        _rootPanel.name = "InventoryPanel";
        _view = _rootPanel.GetComponent<InventoryPanelView>();
        if (_view == null || !_view.ValidateReferences())
        { Debug.LogError("[InventoryUI] 背包 Prefab 节点引用不完整"); Destroy(_rootPanel); return false; }
        _gridRoot = _view.grid.GetComponent<RectTransform>();
        _weaponSlotText = _view.weaponText; _armorSlotText = _view.armorText;
        _weaponSil = _view.weaponSilhouette; _armorSil = _view.armorSilhouette;
        _bagSil = _view.bagIcon; _bagSilSprite = _bagSil.sprite;
        _weaponCell = _view.weaponCell; _armorCell = _view.armorCell;
        _weaponDashRt = _view.weaponIconRoot; _armorDashRt = _view.armorIconRoot;
        _statText = _view.stats; _hpFill = _view.hpFill;
        _detailTitle = _view.detailTitle; _detailDesc = _view.detailDescription;
        _slotCountText = _view.capacityText; _hintText = _view.hint;
        _hintColor = _hintText.color; _emptyBagColor = _bagSil.color;
        var hpRect = _hpFill.rectTransform;
        _hpAnchorMin=hpRect.anchorMin; _hpAnchorMax=hpRect.anchorMax;
        _hpOffsetMin=hpRect.offsetMin; _hpOffsetMax=hpRect.offsetMax;
        _weaponCell.Init(this, InventoryCellUI.CellKind.EquipWeapon, -1);
        _armorCell.Init(this, InventoryCellUI.CellKind.EquipArmor, -1);
        _view.bagCell.Init(this, InventoryCellUI.CellKind.EquipBag, -1);
        foreach (var cell in _view.previewCells)
        { cell.Init(this, InventoryCellUI.CellKind.Bag, _cells.Count); _cells.Add(cell); }
        for (int i=0; i<4; i++)
        {
            _hotbarCellUIs[i] = _view.hotbarCells[i]; _hotbarCounts[i] = _view.hotbarCounts[i];
            _hotbarCellUIs[i].Init(this, InventoryCellUI.CellKind.Hotbar, i);
        }
        _view.closeButton.onClick.AddListener(() => { AudioManager.PlaySfx("SFX_UIClick"); ClosePanel(); });
        _splitRoot = _view.splitRoot; _splitSlider = _view.splitSlider;
        _splitTitleText = _view.splitTitle; _splitQtyText = _view.splitQuantity;
        _view.splitMinus.onClick.AddListener(() => StepSplit(-1));
        _view.splitPlus.onClick.AddListener(() => StepSplit(1));
        _view.splitCancel.onClick.AddListener(HideSplitPopup);
        _view.splitConfirm.onClick.AddListener(OnSplitConfirm);
        _splitSlider.onValueChanged.AddListener(_ => RefreshSplitQty());
        _splitRoot.SetActive(false);
        _view.menuTemplate.SetActive(false);
        return true;
    }

    private RectTransform BuildMenuShell()
    {
        _menuRoot = Instantiate(_view.menuTemplate, _canvasRect, false);
        _menuRoot.SetActive(true);
        return (RectTransform)_menuRoot.transform;
    }

    private void BuildSplitPopup() { _splitRoot = _view.splitRoot; }

    /// <summary>共享物品显示模板：运行时只更新物品数据，不构建 UI 节点。</summary>
    public static GameObject BuildIconGroup(Inventory.ItemStack stack, Vector2 size, ItemOrient orient, bool ghost, bool withName = true)
    {
        var template = Resources.Load<InventoryItemVisual>("UI/InventoryItemVisual");
        if (template == null) throw new System.InvalidOperationException("缺少 UI/InventoryItemVisual 模板");
        var visual = Instantiate(template);
        visual.Apply(stack, size, orient, ghost, withName);
        visual.gameObject.SetActive(true);
        return visual.gameObject;
    }
}
