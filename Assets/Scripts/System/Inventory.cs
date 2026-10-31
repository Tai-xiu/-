using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背包与装备系统 V3（多格物品占位 + 方向自动判断）
/// - 网格：4行×5列=20格；占用数组 bool[4,5] 标记每格是否被占用，与槽位数组 _slots 保持同步
/// - 物品占位：ItemData.width/height 定义尺寸；ItemStack 记录锚点(anchorRow,anchorCol)与方向(横/竖)
/// - 自动寻位：FindSlot 从左上角按行扫描第一个能完整放下尺寸的空白区域；
///   非等比物品（如铁剑2格）自动尝试两方向，优先级可配置（默认横放优先，见 preferHorizontal）
/// - 多格规则：放置/移动前目标区域必须全部为空（CanPlace）；
///   交换要求两物品占用宽高与方向一致（区域形状相同，1×1即原有交换）
/// - 装备：类型校验（武器→武器槽/护甲→护甲槽）；卸下经FindSlot重新寻位；
///   换装时旧装备重寻位失败则整体回滚（背包空间不足）
/// - 保留功能：拾取自动入包（先堆叠后寻位）、血瓶使用、丢弃、死亡清空、跨场景快照（含锚点方向）
/// 数据与表现分离：UI只读，所有修改经本类方法完成并通过Changed事件通知刷新
/// </summary>
public class Inventory : MonoBehaviour
{
    public const int Rows = 4;                 // 网格行数
    public const int Cols = 5;                 // 网格列数
    public const int SlotCount = Rows * Cols;  // 20格

    [Tooltip("自动寻位方向优先级：勾选=横放优先，不勾=竖放优先（非等比物品生效）")]
    [SerializeField] private bool preferHorizontal = true;

    /// <summary>物品堆（占位实例）：锚点=占位区域左上角，方向=横/竖</summary>
    [Serializable]
    public class ItemStack
    {
        public ItemData item;
        public int count;
        public int anchorRow;       // 锚点所在行
        public int anchorCol;       // 锚点所在列
        public ItemOrient orient;   // 当前方向（横/竖）

        /// <summary>定义宽（防旧资产漏配0值，按1处理）</summary>
        public int DefW { get { return Max1(item != null ? item.width : 1); } }
        /// <summary>定义高</summary>
        public int DefH { get { return Max1(item != null ? item.height : 1); } }
        /// <summary>当前方向实际占用宽</summary>
        public int OccW { get { return orient == ItemOrient.Horizontal ? DefW : DefH; } }
        /// <summary>当前方向实际占用高</summary>
        public int OccH { get { return orient == ItemOrient.Horizontal ? DefH : DefW; } }
        /// <summary>锚点格的一维槽索引</summary>
        public int AnchorIndex { get { return anchorRow * Cols + anchorCol; } }

        /// <summary>是否覆盖指定格（多格物品从任意被覆盖格都可拖起/点击）</summary>
        public bool Covers(int row, int col)
        {
            return row >= anchorRow && row < anchorRow + OccH
                && col >= anchorCol && col < anchorCol + OccW;
        }
    }

    // ===== 核心数据（私有，只能经本类公开方法修改） =====
    /// <summary>槽位数组：索引=锚点行*Cols+列，仅锚点格存放物品堆</summary>
    private readonly ItemStack[] _slots = new ItemStack[SlotCount];
    /// <summary>占用数组：[row,col]=true表示该格被某物品占用（与锚点保持同步）</summary>
    private readonly bool[,] _occupied = new bool[Rows, Cols];
    /// <summary>已装备武器</summary>
    private ItemData _equippedWeapon;
    /// <summary>已装备护甲</summary>
    private ItemData _equippedArmor;

    /// <summary>数据变化事件（UI订阅刷新）</summary>
    public event Action Changed;

    /// <summary>槽位数组（只读视图；仅锚点格非null）</summary>
    public IReadOnlyList<ItemStack> Slots => _slots;
    /// <summary>已装备武器（可为null）</summary>
    public ItemData EquippedWeapon => _equippedWeapon;
    /// <summary>已装备护甲（可为null）</summary>
    public ItemData EquippedArmor => _equippedArmor;
    /// <summary>格数上限（=SlotCount）</summary>
    public int MaxSlots => SlotCount;
    /// <summary>自动寻位方向优先级（true=横放优先，可配置参数）</summary>
    public bool PreferHorizontal { get { return preferHorizontal; } set { preferHorizontal = value; } }
    /// <summary>已被占用的格子数（多格物品按实际占用格计）</summary>
    public int OccupiedCells { get { int n = 0; for (int r = 0; r < Rows; r++) for (int c = 0; c < Cols; c++) if (_occupied[r, c]) n++; return n; } }
    /// <summary>已放置的物品堆数（状态行兼容用）</summary>
    public int UsedSlots { get { int n = 0; foreach (var s in _slots) if (s != null) n++; return n; } }

    /// <summary>当前武器攻击加成</summary>
    /// <summary>已装备护甲总重（配置表v0.1：移速=基础×max(0.6,1-总重×0.04)）</summary>
    public float ArmorWeight => _equippedArmor != null ? _equippedArmor.weight : 0f;
    public float WeaponBonus => _equippedWeapon != null ? _equippedWeapon.attackPower : 0f;

    /// <summary>
    /// 当前实际移速（配置表v0.2：基础移速4.0×max(0.6, 1−总重×0.04)）——
    /// 背包面板属性汇总显示用；基础移速从PlayerController.moveSpeed读取（同物体）
    /// </summary>
    public float MoveSpeed
    {
        get
        {
            var pc = GetComponent<PlayerController>();
            float baseSpeed = pc != null ? pc.BaseMoveSpeed : 4f;
            return baseSpeed * Mathf.Max(0.6f, 1f - ArmorWeight * 0.04f);
        }
    }
    /// <summary>当前护甲减伤值</summary>
    public float ArmorDefense => _equippedArmor != null ? _equippedArmor.defensePower : 0f;

    /// <summary>自身生命组件（血瓶回复/减伤注入用）</summary>
    private Health _health;

    private void Awake()
    {
        _health = GetComponent<Health>();
    }

    private void Start()
    {
        // 注入减伤公式：玩家受击 = max(1, 原伤害 - 护甲减伤)
        if (_health != null)
        {
            _health.DamageModifier = amount => Mathf.Max(1f, amount - ArmorDefense);
        }

        // 跨场景数据恢复：GameManager存有上个场景捕获的数据时优先恢复（传送流程）
        if (GameManager.Instance != null && GameManager.Instance.ConsumeInventory(out var slots, out var weapon, out var armorItem))
        {
            RestoreState(slots, weapon, armorItem);
            ApplyHotbar(GameManager.Instance.ConsumeHotbar()); // 快捷栏绑定随背包一起跨场景
        }
        Notify();
    }

    // ===== 读取 =====

    /// <summary>读取指定锚点格的物品堆（越界或空格返回null）</summary>
    public ItemStack GetSlot(int index)
    {
        return index >= 0 && index < SlotCount ? _slots[index] : null;
    }

    /// <summary>返回覆盖指定格的物品堆（多格物品从任意被覆盖格拖起/点击时定位用）</summary>
    public ItemStack GetStackAtCell(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return null;
        int row = slotIndex / Cols, col = slotIndex % Cols;
        foreach (var s in _slots)
        {
            if (s != null && s.Covers(row, col)) return s;
        }
        return null;
    }

    /// <summary>
    /// 恢复跨场景快照：按锚点与方向原样还原（并重建占用数组）
    /// </summary>
    public void RestoreState(ItemStack[] slots, ItemData weapon, ItemData armorItem)
    {
        for (int i = 0; i < SlotCount; i++) _slots[i] = null;
        Array.Clear(_occupied, 0, _occupied.Length);
        if (slots != null)
        {
            foreach (var s in slots)
            {
                if (s == null || s.item == null) continue;
                int row = Mathf.Clamp(s.anchorRow, 0, Rows - 1);
                int col = Mathf.Clamp(s.anchorCol, 0, Cols - 1);
                var copy = new ItemStack { item = s.item, count = s.count, anchorRow = row, anchorCol = col, orient = s.orient };
                int idx = row * Cols + col;
                if (_slots[idx] != null) continue; // 防御：快照异常重叠时丢弃后者
                _slots[idx] = copy;
                Occupy(copy);
            }
        }
        _equippedWeapon = weapon;
        _equippedArmor = armorItem;
        Notify();
    }

    // ===== 占位核心（CanPlace / FindSlot） =====

    private static int Max1(int v)
    {
        return v < 1 ? 1 : v;
    }

    /// <summary>物品在指定方向的实际占用宽</summary>
    public static int OccWOf(ItemData item, ItemOrient orient)
    {
        int w = Max1(item.width), h = Max1(item.height);
        return orient == ItemOrient.Horizontal ? w : h;
    }

    /// <summary>物品在指定方向的实际占用高</summary>
    public static int OccHOf(ItemData item, ItemOrient orient)
    {
        int w = Max1(item.width), h = Max1(item.height);
        return orient == ItemOrient.Horizontal ? h : w;
    }

    /// <summary>
    /// CanPlace：检测以(row,col)为锚点、按orient方向放置item是否可行——
    /// 占用区域必须整体不越界，且区域内所有格子当前都为空
    /// </summary>
    public bool CanPlace(ItemData item, int row, int col, ItemOrient orient)
    {
        return CanPlaceOn(_occupied, item, row, col, orient);
    }

    /// <summary>
    /// CanPlaceExcluding：放置检测（拖拽移动预览专用）——
    /// 目标区域内"被self自身覆盖"的格子不算阻挡（物品新位置与自身原占位部分重叠时仍可放置），
    /// 其余占用格与越界判定与CanPlace保持一致
    /// </summary>
    public bool CanPlaceExcluding(ItemStack self, int row, int col, ItemOrient orient)
    {
        if (self == null || self.item == null) return false;
        int w = OccWOf(self.item, orient), h = OccHOf(self.item, orient);
        if (row < 0 || col < 0 || row + h > Rows || col + w > Cols) return false; // 越界
        for (int r = row; r < row + h; r++)
        {
            for (int c = col; c < col + w; c++)
            {
                if (_occupied[r, c] && !self.Covers(r, c)) return false; // 他人物品的格子才算阻挡
            }
        }
        return true;
    }

    /// <summary>在指定占用数组上检测（供HasSpaceFor模拟扫描复用）</summary>
    private bool CanPlaceOn(bool[,] occ, ItemData item, int row, int col, ItemOrient orient)
    {
        int w = OccWOf(item, orient), h = OccHOf(item, orient);
        if (row < 0 || col < 0 || row + h > Rows || col + w > Cols) return false; // 越界
        for (int r = row; r < row + h; r++)
        {
            for (int c = col; c < col + w; c++)
            {
                if (occ[r, c]) return false; // 区域内存在占用格
            }
        }
        return true;
    }

    /// <summary>
    /// FindSlot：从左上角开始按行扫描，寻找第一个能完整放下item尺寸的空白区域；
    /// 非等比物品自动尝试两个方向（优先级由preferHorizontal配置，默认横放优先）；
    /// 找到返回true并输出锚点与方向，找不到（背包空间不足）返回false
    /// </summary>
    public bool FindSlot(ItemData item, out int row, out int col, out ItemOrient orient)
    {
        return FindSlotOn(_occupied, item, out row, out col, out orient);
    }

    /// <summary>在指定占用数组上扫描（供HasSpaceFor模拟复用）</summary>
    private bool FindSlotOn(bool[,] occ, ItemData item, out int row, out int col, out ItemOrient orient)
    {
        row = 0; col = 0; orient = ItemOrient.Horizontal;
        if (item == null) return false;

        int w = Max1(item.width), h = Max1(item.height);
        // 等比物品两方向等价只测一次；非等比按配置优先级依次尝试
        ItemOrient first, second;
        if (w == h) { first = ItemOrient.Horizontal; second = ItemOrient.Horizontal; }
        else if (preferHorizontal) { first = ItemOrient.Horizontal; second = ItemOrient.Vertical; }
        else { first = ItemOrient.Vertical; second = ItemOrient.Horizontal; }

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                if (occ[r, c]) continue; // 锚点格已被占用，跳过
                if (CanPlaceOn(occ, item, r, c, first)) { row = r; col = c; orient = first; return true; }
                if (!ReferenceEquals(first, second) && first != second && CanPlaceOn(occ, item, r, c, second))
                {
                    row = r; col = c; orient = second; return true;
                }
            }
        }
        return false; // 全图扫描无足够空白区域
    }

    // ===== 占用标记（与锚点保持同步的底层工具） =====

    /// <summary>把物品堆的占位区域标记为占用</summary>
    private void Occupy(ItemStack s)
    {
        for (int r = s.anchorRow; r < s.anchorRow + s.OccH; r++)
        {
            for (int c = s.anchorCol; c < s.anchorCol + s.OccW; c++)
            {
                _occupied[r, c] = true;
            }
        }
    }

    /// <summary>清除物品堆的占位区域标记</summary>
    private void ClearOccupy(ItemStack s)
    {
        for (int r = s.anchorRow; r < s.anchorRow + s.OccH; r++)
        {
            for (int c = s.anchorCol; c < s.anchorCol + s.OccW; c++)
            {
                _occupied[r, c] = false;
            }
        }
    }

    // ===== 放置 / 移除 / 移动 / 交换 =====

    /// <summary>
    /// PlaceItem：在(row,col)锚点按orient放置count个item——
    /// 区域可放置时创建物品堆、标记占用并写入槽位（拾取寻位/卸下寻位的落点）
    /// </summary>
    public bool PlaceItem(ItemData item, int count, int row, int col, ItemOrient orient)
    {
        if (item == null || count <= 0 || !CanPlace(item, row, col, orient)) return false;
        var stack = new ItemStack { item = item, count = count, anchorRow = row, anchorCol = col, orient = orient };
        _slots[row * Cols + col] = stack;
        Occupy(stack);
        Notify();
        return true;
    }

    /// <summary>
    /// RemoveItem：移除指定锚点格的整组物品（清空其占位区域与槽位）
    /// </summary>
    public bool RemoveItem(int anchorIndex)
    {
        var s = GetSlot(anchorIndex);
        if (s == null) return false;
        ClearOccupy(s);
        _slots[anchorIndex] = null;
        Notify();
        return true;
    }

    /// <summary>
    /// MoveItem：把from锚点的物品整体移动到to锚点（保持物品当前方向）；
    /// 检测时先腾空自身占位（允许新位置覆盖自己原来的格子），失败则还原回弹
    /// </summary>
    public bool MoveItem(int fromIndex, int toIndex)
    {
        var s = GetSlot(fromIndex);
        return s != null && MoveItem(fromIndex, toIndex, s.orient);
    }

    /// <summary>
    /// MoveItem重载：移动到to锚点并按orient方向放置（拖拽中按R旋转后的提交入口）；
    /// from==to且orient不同 = 原地旋转，同样支持（不会被fromIndex==toIndex拦截）；
    /// 检测时先按旧方向腾空自身占位，失败则按旧方向还原（保证占用数组不失同步）
    /// </summary>
    public bool MoveItem(int fromIndex, int toIndex, ItemOrient orient)
    {
        var s = GetSlot(fromIndex);
        if (s == null) return false;
        if (toIndex < 0 || toIndex >= SlotCount) return false;
        if (fromIndex == toIndex && s.orient == orient) return false; // 锚点与方向均无变化

        int toRow = toIndex / Cols, toCol = toIndex % Cols;
        ClearOccupy(s);                                // 先按旧方向腾空自身
        bool ok = CanPlace(s.item, toRow, toCol, orient);
        if (ok)
        {
            _slots[fromIndex] = null;
            s.anchorRow = toRow;
            s.anchorCol = toCol;
            s.orient = orient;                         // 方向更新（原地旋转也走这里）
            _slots[toIndex] = s;
            Occupy(s);
            Notify();
            return true;
        }
        Occupy(s);                                     // 目标不可放置：按旧方向还原占用（回弹）
        return false;
    }

    /// <summary>
    /// SwapItems：交换两个锚点格的物品；
    /// 仅当两者占用宽高与方向一致（区域形状相同，可完全重合）才允许，否则返回false由UI回弹
    /// （1×1物品形状恒相同，即保留原有的任意交换）
    /// </summary>
    public bool SwapItems(int fromIndex, int toIndex)
    {
        var a = GetSlot(fromIndex);
        var b = GetSlot(toIndex);
        if (a == null || b == null || fromIndex == toIndex) return false;
        if (a.OccW != b.OccW || a.OccH != b.OccH || a.orient != b.orient) return false;

        int ar = a.anchorRow, ac = a.anchorCol;
        ItemOrient ao = a.orient;
        a.anchorRow = b.anchorRow; a.anchorCol = b.anchorCol; a.orient = b.orient;
        b.anchorRow = ar; b.anchorCol = ac; b.orient = ao;
        _slots[fromIndex] = b;
        _slots[toIndex] = a;
        // 两形状相同，互换后整体占用不变，占用数组无需改动
        Notify();
        return true;
    }

    /// <summary>
    /// MergeStacks：把from堆叠加合并到同类to堆上（拖拽叠加入口）；
    /// 仅同类物品（同SO引用）且maxStack>1允许；目标最多补到maxStack，
    /// 装不下的数量留在from堆（部分合并）；from堆并空则移除其占位；
    /// 返回实际合并数量（0=无法合并，如目标已满，由UI回退为交换）
    /// </summary>
    public int MergeStacks(int fromIndex, int toIndex)
    {
        var a = GetSlot(fromIndex);
        var b = GetSlot(toIndex);
        if (a == null || b == null || fromIndex == toIndex) return 0;
        if (a.item == null || a.item != b.item || a.item.maxStack <= 1) return 0;

        int moved = Mathf.Min(a.item.maxStack - b.count, a.count);
        if (moved <= 0) return 0; // 目标堆已满
        b.count += moved;
        if (moved >= a.count)
        {
            RemoveItem(fromIndex); // 源堆并空：清占位+清槽位+Notify
        }
        else
        {
            a.count -= moved;      // 部分合并：源堆保留剩余数量（占位不变）
            Notify();
        }
        return moved;
    }

    /// <summary>
    /// TryPlaceAtCell：外部容器指定格放置（搜索栏拖入背包格专用）——
    /// 以悬停格为锚点尝试放置整组物品：足迹越界时锚点自动夹取到边界内；
    /// 先横放、非等比物品再以悬停格为锚试竖放；成功返回true（数据已写入+Notify），失败false不动数据
    /// </summary>
    public bool TryPlaceAtCell(ItemData item, int count, int row, int col)
    {
        if (item == null || count <= 0) return false;
        int w = Max1(item.width), h = Max1(item.height);
        int aRow = Mathf.Clamp(row, 0, Rows - h);
        int aCol = Mathf.Clamp(col, 0, Cols - w);
        if (CanPlace(item, aRow, aCol, ItemOrient.Horizontal))
            return PlaceItem(item, count, aRow, aCol, ItemOrient.Horizontal);
        if (w != h)
        {
            int vRow = Mathf.Clamp(row, 0, Rows - w);
            int vCol = Mathf.Clamp(col, 0, Cols - h);
            if (CanPlace(item, vRow, vCol, ItemOrient.Vertical))
                return PlaceItem(item, count, vRow, vCol, ItemOrient.Vertical);
        }
        return false;
    }

    /// <summary>
    /// MergeIntoStack：外部容器叠加（搜索栏物品拖到同类堆上专用）——
    /// 把count个item并入anchorIndex堆（必须是同类且可叠加），返回实际并入数；
    /// 目标已满返回0（由调用方提示回弹）；部分装入返回小于count（剩余留在外部容器）
    /// </summary>
    public int MergeIntoStack(int anchorIndex, ItemData item, int count)
    {
        var s = GetSlot(anchorIndex);
        if (s == null || item == null || count <= 0 || s.item != item || item.maxStack <= 1) return 0;
        int moved = Mathf.Min(item.maxStack - s.count, count);
        if (moved <= 0) return 0;
        s.count += moved;
        Notify();
        return moved;
    }

    // ===== 拾取与容量（保留原功能：先堆叠，再自动寻位） =====

    /// <summary>
    /// 拾取前检查：背包是否能容纳count个item——
    /// 同类堆剩余空间 + 在占用数组副本上模拟连续FindSlot计算可新开的堆数
    /// </summary>
    public bool HasSpaceFor(ItemData item, int count)
    {
        if (item == null || count <= 0) return true;

        int remaining = count;
        if (item.maxStack > 1)
        {
            foreach (var s in _slots)
            {
                if (s != null && s.item == item && s.count < item.maxStack)
                {
                    remaining -= item.maxStack - s.count;
                }
            }
        }
        if (remaining <= 0) return true;

        // 模拟扫描：不动真实数据，在副本上反复寻位（多格物品需整块空白）
        var temp = (bool[,])_occupied.Clone();
        while (remaining > 0)
        {
            if (!FindSlotOn(temp, item, out int r, out int c, out ItemOrient o)) return false;
            remaining -= item.maxStack;
            int w = OccWOf(item, o), h = OccHOf(item, o);
            for (int rr = r; rr < r + h; rr++)
            {
                for (int cc = c; cc < c + w; cc++)
                {
                    temp[rr, cc] = true;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// 添加物品：优先堆入同类堆，剩余数量经FindSlot自动寻位（自动判断横/竖方向）开新堆；
    /// 空间不足返回false（「背包空间不足」，物品不消耗）
    /// </summary>
    public bool AddItem(ItemData item, int count = 1)
    {
        if (item == null || count <= 0) return false;
        if (!HasSpaceFor(item, count))
        {
            Debug.LogWarning("[Inventory] 背包空间不足，无法添加 " + item.itemName + " x" + count);
            return false;
        }

        int remaining = count;
        // 1. 堆入已有同类堆（堆叠不改变占位）
        if (item.maxStack > 1)
        {
            for (int i = 0; i < SlotCount && remaining > 0; i++)
            {
                var s = _slots[i];
                if (s != null && s.item == item && s.count < item.maxStack)
                {
                    int add = Mathf.Min(remaining, item.maxStack - s.count);
                    s.count += add;
                    remaining -= add;
                }
            }
        }
        // 2. 剩余数量自动寻位开新堆（已放置物品不受影响）
        while (remaining > 0)
        {
            FindSlot(item, out int r, out int c, out ItemOrient o);
            int add = Mathf.Min(remaining, item.maxStack);
            PlaceItem(item, add, r, c, o); // HasSpaceFor已保证可放置
            remaining -= add;
        }

        Notify();
        return true;
    }

    /// <summary>
    /// 从指定格移除count个（数量归零则清空该物品的整个占位区域；使用/丢弃/出售共用）
    /// </summary>
    public bool RemoveAt(int index, int count = 1)
    {
        var stack = GetSlot(index);
        if (stack == null || count <= 0 || stack.count < count) return false;

        stack.count -= count;
        if (stack.count <= 0)
        {
            ClearOccupy(stack);
            _slots[stack.AnchorIndex] = null; // 按锚点清槽（index可能是覆盖格）
        }
        Notify();
        return true;
    }

    /// <summary>
    /// 拆分物品堆：从anchorIndex堆中拆出splitCount个，经FindSlot自动寻位开新堆；
    /// 校验：堆存在、物品可叠加(maxStack>1)、1<=splitCount<count（原堆至少保留1个）、
    /// 新堆有放置空间；任一不满足返回false（不做任何修改）
    /// </summary>
    public bool SplitItem(int anchorIndex, int splitCount)
    {
        var s = GetSlot(anchorIndex);
        if (s == null || s.item == null) return false;
        if (s.item.maxStack <= 1) return false;                      // 不可叠加物品无拆分
        if (splitCount < 1 || splitCount >= s.count) return false;   // 拆出数量范围 1~count-1
        if (!FindSlot(s.item, out int r, out int c, out ItemOrient o)) return false; // 新堆无空间

        if (!RemoveAt(anchorIndex, splitCount)) return false;        // 原堆扣减（count-N>=1，占位区域不变）
        return PlaceItem(s.item, splitCount, r, c, o);               // 新堆寻位放置（内部Notify）
    }

    // ===== 装备 =====

    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < SlotCount;
    }

    /// <summary>
    /// 装备类型校验：武器只能进武器槽，护甲只能进护甲槽（其余类型不可装备）
    /// </summary>
    public bool CanEquip(ItemData item, EquipSlotType slotType)
    {
        if (item == null) return false;
        return slotType == EquipSlotType.Weapon
            ? item.type == ItemType.Weapon
            : item.type == ItemType.Armor;
    }

    /// <summary>
    /// 装备校验+直接装备（不含背包格操作；校验失败返回false不做任何修改）
    /// </summary>
    public bool TryEquip(ItemData item, EquipSlotType slotType)
    {
        if (!CanEquip(item, slotType)) return false;
        SetEquipped(slotType, item);
        Notify();
        return true;
    }

    /// <summary>
    /// TryEquipFromBag：从背包锚点格装备到指定槽位（拖拽到装备槽/操作菜单共用）；
    /// 类型校验通过 → 背包物品移除（占位清空）→ 换下的旧装备经FindSlot重新寻位放回背包；
    /// 旧装备找不到空间（背包空间不足）则整体回滚返回false
    /// </summary>
    public bool TryEquipFromBag(int bagIndex, EquipSlotType slotType)
    {
        var stack = GetSlot(bagIndex);
        if (stack == null || !CanEquip(stack.item, slotType)) return false;

        var newItem = stack.item;
        var old = slotType == EquipSlotType.Weapon ? _equippedWeapon : _equippedArmor;

        // 先把新装备从背包移除（清空其占位区域）
        ClearOccupy(stack);
        _slots[stack.AnchorIndex] = null;

        // 旧装备重新寻位放回背包
        if (old != null)
        {
            if (!FindSlot(old, out int r, out int c, out ItemOrient o))
            {
                // 空间不足：回滚（背包物品还原回原格）
                _slots[stack.AnchorIndex] = stack;
                Occupy(stack);
                Debug.LogWarning("[Inventory] 背包空间不足，无法换装 " + newItem.itemName);
                return false;
            }
            PlaceItem(old, 1, r, c, o);
        }

        SetEquipped(slotType, newItem);
        Debug.Log("[Inventory] 装备了 " + newItem.itemName);
        Notify();
        return true;
    }

    /// <summary>
    /// Unequip：卸下指定装备槽物品，经FindSlot重新寻位放回背包；
    /// 背包空间不足返回false（物品留在装备槽，由UI提示）
    /// </summary>
    public bool Unequip(EquipSlotType slotType)
    {
        var current = slotType == EquipSlotType.Weapon ? _equippedWeapon : _equippedArmor;
        if (current == null) return false;
        if (!FindSlot(current, out int r, out int c, out ItemOrient o))
        {
            Debug.LogWarning("[Inventory] 背包空间不足，无法卸下 " + current.itemName);
            return false;
        }
        SetEquipped(slotType, null);
        PlaceItem(current, 1, r, c, o);
        Debug.Log("[Inventory] 卸下了 " + current.itemName);
        return true;
    }

    /// <summary>写入装备槽（内部工具）</summary>
    private void SetEquipped(EquipSlotType slotType, ItemData item)
    {
        if (slotType == EquipSlotType.Weapon) _equippedWeapon = item;
        else _equippedArmor = item;
    }

    // ===== 使用/丢弃/死亡清空 =====

    /// <summary>
    /// 使用消耗品：回复生命并扣减1个；满血或死亡时不消耗
    /// </summary>
    public void UseItem(int index)
    {
        var stack = GetStackAtCell(index); // 点击覆盖格也能用
        if (stack == null) return;
        if (stack.item.type != ItemType.Consumable)
        {
            Debug.Log("[Inventory] " + stack.item.itemName + " 不是消耗品");
            return;
        }
        if (_health == null || _health.IsDead) return;
        // 配置表v0.1：满血仅拒绝纯回血物品；回体力物品（healStamina>0）满血也可用
        if (_health.Current >= _health.Max && stack.item.healStamina <= 0f)
        {
            Debug.Log("[Inventory] 生命已满，无需使用 " + stack.item.itemName);
            return;
        }

        if (stack.item.healAmount > 0f) _health.Heal(stack.item.healAmount);
        if (stack.item.healStamina > 0f)
        {
            var st = GetComponent<Stamina>();
            if (st != null) st.Restore(stack.item.healStamina);
        }
        AudioManager.PlaySfx("SFX_DrinkPotion"); // 喝药音效（2D，玩家自身动作）
        RemoveAt(stack.AnchorIndex, 1);
        Debug.Log("[Inventory] 使用 " + stack.item.itemName + "，回复 " + stack.item.healAmount
            + "，当前生命 " + _health.Current + "/" + _health.Max);
    }

    /// <summary>
    /// 丢弃指定物品整组：从背包移除，并在玩家前方生成世界掉落物（可重新瞄准拾取）
    /// </summary>
    public void DropItem(int index)
    {
        var stack = GetStackAtCell(index);
        if (stack == null || stack.item == null) return;
        var item = stack.item;
        int count = stack.count;

        RemoveAt(stack.AnchorIndex, stack.count);

        SpawnWorldDrop(item, count);
        Debug.Log("[Inventory] 丢弃了 " + item.itemName + (count > 1 ? " x" + count : "") + "（已掉落至地面）");
    }

    /// <summary>
    /// 生成世界掉落物：玩家前方1.2m处抛出（无碰撞体+抛物线toss，与开箱掉落同管线），
    /// 落地后可十字准星瞄准按F重新拾取。重拾不重复计入探索战利品结算（避免结算面板重复计数）
    /// </summary>
    private void SpawnWorldDrop(ItemData item, int count)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Drop_" + item.itemName;
        Destroy(go.GetComponent<Collider>()); // 掉落物无碰撞体（与开箱掉落一致）

        // 生成点防穿墙：正前方1.2m内有实体碰撞体（贴墙/面墙）时贴墙前生成，否则物品会生成在墙体内不可见
        var spawnPos = transform.position + transform.forward * 1.2f;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, transform.forward, out var wallHit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            spawnPos = transform.position + transform.forward * Mathf.Max(0.3f, wallHit.distance - 0.2f);
        }
        spawnPos.y = transform.position.y - 0.55f; // 腿部高度弹出（玩家脚下偏上）
        go.transform.position = spawnPos;
        go.transform.localScale = Vector3.one * 0.3f;

        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Unlit/Color"));
        mr.material.color = ItemRarityStyle.GetColor(item.rarity);

        var loot = go.AddComponent<LootItemCube>();
        loot.Init(item, count);
        loot.countTowardsLootLog = false; // 背包丢弃重拾不重复计入战利品结算
        loot.BeginToss();
    }

    /// <summary>
    /// 死亡结算：清空物品栏（含占位数组）与装备栏（搜打撤规则：金币保留在钱包）
    /// </summary>
    public void ClearOnDeath()
    {
        for (int i = 0; i < SlotCount; i++) _slots[i] = null;
        Array.Clear(_occupied, 0, _occupied.Length);
        _equippedWeapon = null;
        _equippedArmor = null;
        Debug.Log("[Inventory] 死亡结算：物品栏与装备栏已清空");
        Notify();
    }

    // ===== 快捷栏（HUD组件3：4槽手动分配，绑定按物品引用跟随，背包内移动不掉绑定） =====

    /// <summary>快捷栏槽位（4格，存物品引用；数量实时按背包统计，物品耗尽自然归零）</summary>
    private readonly ItemData[] _hotbar = new ItemData[4];

    /// <summary>快捷栏变化事件（分配/取下/跨场景应用时触发，HUD栏与背包镜像订阅刷新）</summary>
    public event Action HotbarChanged;

    /// <summary>读取快捷栏槽位绑定的物品（空槽返回null）</summary>
    public ItemData GetHotbarItem(int slot)
    {
        return slot >= 0 && slot < _hotbar.Length ? _hotbar[slot] : null;
    }

    /// <summary>分配物品到快捷栏槽位（重复分配=覆盖；同一物品全栏唯一——已在其它槽时自动清旧槽=更换位置）</summary>
    public void SetHotbarItem(int slot, ItemData item)
    {
        if (slot < 0 || slot >= _hotbar.Length || item == null) return;
        for (int i = 0; i < _hotbar.Length; i++)
        {
            if (i != slot && _hotbar[i] == item) _hotbar[i] = null;
        }
        _hotbar[slot] = item;
        HotbarChanged?.Invoke();
    }

    /// <summary>取下快捷栏槽位绑定</summary>
    public void ClearHotbarItem(int slot)
    {
        if (slot < 0 || slot >= _hotbar.Length) return;
        _hotbar[slot] = null;
        HotbarChanged?.Invoke();
    }

    /// <summary>物品当前被分配到的快捷栏槽位（未分配返回-1）</summary>
    public int GetHotbarSlotOfItem(ItemData item)
    {
        if (item == null) return -1;
        for (int i = 0; i < _hotbar.Length; i++)
        {
            if (_hotbar[i] == item) return i;
        }
        return -1;
    }

    /// <summary>快捷栏槽位对应物品在背包中的总数量（跨堆叠求和；物品耗尽自然归零）</summary>
    public int GetHotbarCount(int slot)
    {
        var item = GetHotbarItem(slot);
        if (item == null) return 0;
        int n = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            var s = _slots[i];
            if (s != null && s.item == item) n += s.count;
        }
        return n;
    }

    /// <summary>使用快捷栏槽位：定位背包中该物品的第一堆并走UseItem（回血+扣1；无货静默返回）</summary>
    public void UseHotbar(int slot)
    {
        var item = GetHotbarItem(slot);
        if (item == null) return;
        for (int i = 0; i < SlotCount; i++)
        {
            var s = _slots[i];
            if (s != null && s.item == item)
            {
                UseItem(i); // _slots仅锚点非空，i即锚点索引
                return;
            }
        }
    }

    /// <summary>应用跨场景快捷栏快照（Portal传送链：ConsumeInventory成功后调用）</summary>
    public void ApplyHotbar(ItemData[] hotbar)
    {
        if (hotbar == null || hotbar.Length != _hotbar.Length) return;
        for (int i = 0; i < _hotbar.Length; i++) _hotbar[i] = hotbar[i];
        HotbarChanged?.Invoke();
    }

    /// <summary>导出快捷栏快照（GameManager.CapturePlayerState调用；元素可为null）</summary>
    public ItemData[] ExportHotbar()
    {
        return (ItemData[])_hotbar.Clone();
    }

    /// <summary>通知UI刷新</summary>
    private void Notify()
    {
        Changed?.Invoke();
    }
}
