using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 搜索结果面板（白盒线框风格V4：三角洲行动式隐藏搜索栏，与背包UI统一视觉与生命周期）：
/// 背包右侧的附属竖条面板（不动原背包布局）——标题栏 + 3×4共12格结果区 + 状态行 + 操作提示框
/// 样式复用InventoryUI的白盒常量与占位图标组（白底黑框黑字/类型色剪影图标/品质淡染），与背包视觉完全统一
/// 数据流：_results直接引用尸体的SearchResult列表（单一数据源）——拾取置空/栏内换位实时写回尸体，
/// 关包解绑不清数据（战利品保留在尸体上，重搜继续显示剩余），防反复搜索刷资源；
/// 已拾空的尸体重搜仍弹出背包+空搜索栏（结果0/12），仅从未产出数据的尸体不响应
/// 放入：悬停即选中→按F键放入；拖拽=命中其他搜索格→栏内移动/交换，命中背包格→指定格放置/同类堆叠加，未命中→回弹
/// 不注册全局面板栈：搜索栏是背包的附属面板，Esc/Tab经背包的closeAction关闭后由此处逐帧轮询联动
/// </summary>
public class SearchPanelUI : MonoBehaviour
{
    // 本面板仍是浅色底，用自己的深色文字/线条（不跟随背包的深色 kit 主题）
    private static readonly Color LineColor = new Color(0.85f, 0.75f, 0.53f, 1f);

    public static SearchPanelUI Instance { get; private set; }

    private const int Rows = 4;
    private const int Cols = 3;
    private const int Capacity = Rows * Cols; // 12

    private const float CellSize = 74f;  // 与背包网格同规格
    private const float CellGap = 8f;
    private const float PanelW = Cols * CellSize + (Cols - 1) * CellGap + 20f; // 258（左右padding 10×2）
    private const float PanelH = 544f;   // 与背包面板同高（垂直对齐成一整体）
    private const float BarOffsetX = 330f + 12f + PanelW * 0.5f; // 背包右缘（+330）+12px间隙

    private GameObject _rootPanel;
    private RectTransform _canvasRect;
    private readonly List<GameObject> _cells = new List<GameObject>();
    private readonly List<Outline> _cellOutlines = new List<Outline>();
    private List<(ItemData item, int count)> _results; // 直接引用当前尸体的SearchResult列表（单一数据源：拾取置空/换位实时写回尸体；解绑后为null）
    private Text _statusText;
    private int _hoverIndex = -1;      // 当前悬停（=选中）的结果格（-1=无；指向即选中，移开取消）
    private InventoryUI _invUI;

    // —— 拖拽状态（图标跟随鼠标，释放命中背包格=拾取入包） ——
    private bool _dragging;
    private int _dragIndex = -1;
    private GameObject _ghost;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SearchableCorpse.Searched += OnSearched;
    }

    private static void OnSearched(SearchableCorpse corpse)
    {
        if (Instance == null)
        {
            var go = new GameObject("SearchPanelUI");
            go.AddComponent<SearchPanelUI>();
        }
        Instance.Open(corpse);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_rootPanel == null) return; // 无活动搜索
        if (PauseUI.IsPaused) return;

        // 跟随背包显隐：背包被Tab/Esc/关闭按钮关闭→搜索栏同步隐藏解绑（战利品数据保留在尸体上，重搜继续显示剩余）
        bool invOpen = _invUI != null && _invUI.IsOpen;
        if (!invOpen)
        {
            CloseSearch();
            return;
        }
        if (!_rootPanel.activeSelf) _rootPanel.SetActive(true);

        // F键：放入当前选中的搜索结果
        if (Input.GetKeyDown(KeyCode.F)) TryPickupSelected();
    }

    // ===== 打开/关闭 =====

    /// <summary>
    /// 打开搜索栏（Searched事件入口）：绑定该尸体并显示其剩余战利品（无产出不开包不显示）。
    /// _results直接引用尸体SearchResult列表——拾取置空/栏内换位实时写回尸体（防反复搜索刷资源）
    /// </summary>
    public void Open(SearchableCorpse corpse)
    {
        var canvasRoot = GameManager.Instance != null && GameManager.Instance.GameCanvas != null
            ? GameManager.Instance.GameCanvas.transform
            : null;
        if (canvasRoot == null) return;
        _invUI = FindObjectOfType<InventoryUI>();
        if (_invUI == null || _invUI.GetComponent<Inventory>() == null) return;
        _canvasRect = canvasRoot as RectTransform;

        // 已搜索过的尸体（含已拾空）重搜一律弹出背包+搜索栏：拾空的显示空栏（结果0/12）；
        // 仅从未产出过数据（SearchResult==null，如未配掉落表）不响应
        var loot = corpse != null ? corpse.SearchResult : null;
        if (loot == null) return;

        if (!_invUI.IsOpen) _invUI.OpenPanel(); // 搜索完成自动打开背包（Tab手动开包不受影响）
        CloseSearch(); // 解绑旧尸体/销毁旧面板

        // 孤儿清扫：上一场景实例残留的搜索栏（组件已死但DDOL画布上的节点还在）按名销毁
        var orphanBar = canvasRoot.Find("SearchBarPanel");
        if (orphanBar != null) Destroy(orphanBar.gameObject);

        _results = loot;        // 直接引用尸体战利品列表（单一数据源）
        _hoverIndex = -1;
        BuildPanel(canvasRoot);
        RefreshCells();
    }

    /// <summary>关闭搜索栏（销毁面板并解绑尸体；战利品数据保留在尸体上，重搜继续显示剩余）</summary>
    public void CloseSearch()
    {
        if (_ghost != null) { Destroy(_ghost); _ghost = null; }
        _dragging = false;
        _dragIndex = -1;
        if (_rootPanel != null) { Destroy(_rootPanel); _rootPanel = null; }
        _results = null;      // 仅解绑引用（数据属尸体，不清空）
        _cells.Clear();
        _cellOutlines.Clear();
        _hoverIndex = -1;
        _statusText = null;
    }

    // ===== 面板构建 =====

    private void BuildPanel(Transform canvasRoot)
    {
        _rootPanel = new GameObject("SearchBarPanel", typeof(Image), typeof(VerticalLayoutGroup));
        UIKit.EnsureLoaded();
        var panelImg = _rootPanel.GetComponent<Image>();
        panelImg.sprite = UIKit.Panel;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = Color.white;
        var prt = _rootPanel.GetComponent<RectTransform>();
        prt.SetParent(canvasRoot, false);
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = new Vector2(BarOffsetX, 0f);
        prt.sizeDelta = new Vector2(PanelW, PanelH);
        var pOutline = _rootPanel.AddComponent<Outline>();
        pOutline.enabled = false;
        pOutline.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);

        var vlg = _rootPanel.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 10f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        // ── 1. 标题栏（规格同背包标题栏） ──
        var titleBar = CreateBorderedBox("TitleBar");
        var tle = titleBar.GetComponent<LayoutElement>();
        tle.preferredHeight = 40f;
        tle.flexibleWidth = 0f;
        tle.flexibleHeight = 0f;
        var titleText = CreateText(titleBar.transform, "搜 索", 18, TextAnchor.MiddleCenter);
        titleText.fontStyle = FontStyle.Bold;
        StretchRect(titleText.rectTransform);
        titleText.GetComponent<LayoutElement>().ignoreLayout = true;

        // ── 2. 3×4结果网格（74px格，与背包网格同规格） ──
        var grid = new GameObject("ResultGrid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
        grid.transform.SetParent(_rootPanel.transform, false);
        var gle = grid.GetComponent<LayoutElement>();
        gle.preferredHeight = Rows * CellSize + (Rows - 1) * CellGap; // 320
        gle.flexibleWidth = 0f;
        gle.flexibleHeight = 0f;
        var glg = grid.GetComponent<GridLayoutGroup>();
        glg.padding = new RectOffset(1, 1, 1, 1);
        glg.cellSize = new Vector2(CellSize, CellSize);
        glg.spacing = new Vector2(CellGap, CellGap);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = Cols;
        glg.childAlignment = TextAnchor.UpperLeft;

        for (int i = 0; i < Capacity; i++)
        {
            var cell = new GameObject("Cell_" + i, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
            cell.transform.SetParent(grid.transform, false);
            var cimg = cell.GetComponent<Image>();
            cimg.sprite = UIKit.Slot;
            cimg.type = Image.Type.Sliced;
            cimg.color = Color.white;
            var ol = cell.GetComponent<Outline>();
            ol.effectColor = LineColor;
            ol.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);
            cell.AddComponent<SearchResultCellUI>().Init(this, i);
            _cells.Add(cell);
            _cellOutlines.Add(ol);
        }

        // ── 3. 状态行（左计数/右操作提示，规格同背包底部状态行） ──
        var footer = new GameObject("FooterRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        footer.transform.SetParent(_rootPanel.transform, false);
        var fle = footer.GetComponent<LayoutElement>();
        fle.preferredHeight = 24f;
        fle.flexibleWidth = 0f;
        fle.flexibleHeight = 0f;
        var fhlg = footer.GetComponent<HorizontalLayoutGroup>();
        fhlg.childControlWidth = true;
        fhlg.childControlHeight = true;
        fhlg.childForceExpandWidth = true;
        fhlg.childForceExpandHeight = false;

        _statusText = CreateText(footer.transform, "结果 0/" + Capacity, 16, TextAnchor.MiddleLeft);
        var right = CreateText(footer.transform, "拖拽 / F键拾取", 16, TextAnchor.MiddleRight);
        right.GetComponent<LayoutElement>().flexibleWidth = 1f;

        // ── 4. 操作提示框（占满剩余高度，规格同背包详情框） ──
        var hintBox = CreateBorderedBox("HintBox");
        var hle = hintBox.GetComponent<LayoutElement>();
        hle.flexibleHeight = 1f;
        hle.flexibleWidth = 0f;
        var hvlg = hintBox.AddComponent<VerticalLayoutGroup>();
        hvlg.padding = new RectOffset(10, 10, 6, 6);
        hvlg.childControlWidth = true;
        hvlg.childControlHeight = true;
        hvlg.childForceExpandHeight = false;
        hvlg.childForceExpandWidth = true;
        var hintText = CreateText(hintBox.transform, "指向物品按F键放入背包\n拖到背包格拾取（同类自动叠加）\n拖到其他格换位 · 可重复搜索尸体", 14, TextAnchor.UpperLeft);
        hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        hintText.GetComponent<LayoutElement>().flexibleHeight = 1f;
    }

    /// <summary>刷新格子与状态行：重建图标组/品质底色淡染/选中加粗边框（拾完保留空栏，不自动关闭）</summary>
    private void RefreshCells()
    {
        if (_results == null) return;
        int remaining = 0;
        for (int i = 0; i < _cells.Count; i++)
        {
            for (int c = _cells[i].transform.childCount - 1; c >= 0; c--)
            {
                Destroy(_cells[i].transform.GetChild(c).gameObject);
            }
            bool has = i < _results.Count && _results[i].item != null;
            if (has)
            {
                remaining++;
                var (item, count) = _results[i];
                var stack = new Inventory.ItemStack { item = item, count = count, anchorRow = 0, anchorCol = 0, orient = ItemOrient.Horizontal };
                var icon = InventoryUI.BuildIconGroup(stack, new Vector2(CellSize, CellSize), ItemOrient.Horizontal, false, true); // withName=true：显示物品名称
                icon.name = "Icon_" + i;
                icon.transform.SetParent(_cells[i].transform, false);
            }
            // 品质底色淡染（与背包网格同规则）+ 选中/普通边框
            var img = _cells[i].GetComponent<Image>();
            img.color = has && _results[i].item.rarity != ItemRarity.None
                ? Color.Lerp(InventoryUI.PanelBg, ItemRarityStyle.GetColor(_results[i].item.rarity), 0.45f)
                : InventoryUI.PanelBg;
            bool sel = i == _hoverIndex;
            float border = sel ? InventoryUI.SelectedBorder : InventoryUI.BorderWidth;
            _cellOutlines[i].effectDistance = new Vector2(border, border);
            _cellOutlines[i].effectColor = LineColor;
        }
        if (_statusText != null) _statusText.text = "结果 " + remaining + "/" + Capacity;
    }

    // ===== 悬停选中（指向即选中，移开取消） =====

    /// <summary>搜索格悬停进入：指向有物品的格=选中（加粗边框），F键对该格生效</summary>
    public void OnCellHoverEnter(int idx)
    {
        if (_results == null || idx < 0 || idx >= _results.Count || _results[idx].item == null) return;
        if (_hoverIndex == idx) return;
        _hoverIndex = idx;
        RefreshCells();
    }

    /// <summary>搜索格悬停离开：取消选中</summary>
    public void OnCellHoverExit(int idx)
    {
        if (_hoverIndex == idx)
        {
            _hoverIndex = -1;
            RefreshCells();
        }
    }

    // ===== 放入背包 =====

    /// <summary>F键：放入当前悬停（选中）格（未指向/空格/未绑定不响应）</summary>
    public void TryPickupSelected()
    {
        if (_results == null || _hoverIndex < 0 || _hoverIndex >= _results.Count || _results[_hoverIndex].item == null) return;
        TryPickup(_hoverIndex);
    }

    /// <summary>把第idx格结果放入背包（AddItem自动寻位先堆叠）：满则提示并保留；拾完保留空栏（Tab统一关闭）；置空实时写回尸体</summary>
    private void TryPickup(int idx)
    {
        if (_results == null || idx < 0 || idx >= _results.Count || _results[idx].item == null) return;
        var (item, count) = _results[idx];
        var inv = _invUI.GetComponent<Inventory>();
        if (!inv.AddItem(item, count))
        {
            _invUI.ShowHint("背包空间不足，无法拾取 " + item.itemName);
            return;
        }
        _results[idx] = (null, 0);
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RecordLoot(item, count); // 尸体战利品同样计入探索结算清单（与开箱掉落同口径）
        }
        QuestManager.NotifyPickup(item, count); // 悬赏契约：收集类计入本局清单（撤离成功才入账）
        _invUI.ShowHint("已拾取 " + item.itemName + (count > 1 ? " x" + count : ""));
        RefreshCells(); // 全部拾完保留空搜索栏，不自动关闭（可再次搜索尸体）
    }

    // ===== 拖拽入背包 =====

    /// <summary>开始拖拽（由SearchResultCellUI转发）：创建跟随图标（复用背包Ghost样式）</summary>
    public void OnCellBeginDrag(int idx)
    {
        if (_results == null || idx >= _results.Count || _results[idx].item == null) return;
        _dragging = true;
        _dragIndex = idx;
        var (item, count) = _results[idx];
        var stack = new Inventory.ItemStack { item = item, count = count, anchorRow = 0, anchorCol = 0, orient = ItemOrient.Horizontal };
        _ghost = InventoryUI.BuildIconGroup(stack, new Vector2(CellSize, CellSize), ItemOrient.Horizontal, true, false);
        _ghost.name = "SearchDragGhost";
        _ghost.transform.SetParent(_canvasRect, false);
        _ghost.GetComponent<RectTransform>().anchoredPosition = GetCanvasLocalPos(Input.mousePosition);
    }

    /// <summary>拖拽中：跟随图标按鼠标移动</summary>
    public void OnCellDrag(int idx, PointerEventData eventData)
    {
        if (!_dragging || _ghost == null) return;
        _ghost.GetComponent<RectTransform>().anchoredPosition = GetCanvasLocalPos(eventData.position);
    }

    /// <summary>拖拽结束：释放点命中其他搜索格=栏内移动/交换；命中背包格=指定格放置/同类叠加；否则回弹（无操作）</summary>
    public void OnCellEndDrag(int idx)
    {
        bool wasDragging = _dragging;
        int dragIdx = _dragIndex;
        _dragging = false;
        _dragIndex = -1;
        if (_ghost != null) { Destroy(_ghost); _ghost = null; }
        if (!wasDragging || dragIdx != idx) return;
        if (_results == null || dragIdx >= _results.Count || _results[dragIdx].item == null) return;

        // 优先搜索格：栏内调整位置（同背包拖动整理）
        int targetCell = RaycastSearchCell(Input.mousePosition);
        if (targetCell >= 0 && targetCell != dragIdx)
        {
            MoveResult(dragIdx, targetCell);
            return;
        }
        // 其次背包格：指定格放置 / 同类堆叠加
        var bagCell = RaycastBagCell(Input.mousePosition);
        if (bagCell != null)
        {
            TryDropToBagCell(dragIdx, bagCell);
            return;
        }
        // 未命中：回弹
    }

    /// <summary>
    /// 把搜索栏第idx格物品拖放到背包格上（公开便于测试直调）：
    /// 目标格是同类可堆叠物品→叠加（装不下的部分留在搜索栏）；否则以该格为锚点放置（自动横竖+边界夹取）；
    /// 无法放置→提示并保留在搜索栏（回弹）。置空/减量实时写回尸体数据
    /// </summary>
    public void TryDropToBagCell(int idx, InventoryCellUI bagCell)
    {
        if (_results == null || idx < 0 || idx >= _results.Count || _results[idx].item == null || bagCell == null) return;
        var (item, count) = _results[idx];
        var inv = _invUI.GetComponent<Inventory>();
        int row = bagCell.SlotIndex / Inventory.Cols;
        int col = bagCell.SlotIndex % Inventory.Cols;

        // 1) 目标格是同类可堆叠物品 → 叠加（装不下的部分留在搜索栏）
        var stack = inv.GetStackAtCell(bagCell.SlotIndex);
        if (stack != null && stack.item == item && item.maxStack > 1)
        {
            int moved = inv.MergeIntoStack(stack.AnchorIndex, item, count);
            if (moved <= 0)
            {
                _invUI.ShowHint(item.itemName + " 该堆已满");
                return; // 回弹
            }
            if (moved >= count) _results[idx] = (null, 0);
            else _results[idx] = (item, count - moved);
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RecordLoot(item, moved); // 拖放入包同口径计入结算清单
            }
            QuestManager.NotifyPickup(item, moved); // 悬赏契约：收集类计入本局清单
            _invUI.ShowHint("已拾取 " + item.itemName + " x" + moved);
            RefreshCells();
            return;
        }

        // 2) 以悬停格为锚点放置（自动横竖回退+边界夹取）
        if (inv.TryPlaceAtCell(item, count, row, col))
        {
            _results[idx] = (null, 0);
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RecordLoot(item, count); // 拖放入包同口径计入结算清单
            }
            QuestManager.NotifyPickup(item, count); // 悬赏契约：收集类计入本局清单
            _invUI.ShowHint("已拾取 " + item.itemName + (count > 1 ? " x" + count : ""));
            RefreshCells();
            return;
        }

        _invUI.ShowHint("无法放置到该位置"); // 回弹：保留在搜索栏
    }

    /// <summary>栏内移动/交换（三角洲式整理，1×1恒可交换）：目标空=移动，目标有货=整组交换（实时写回尸体）</summary>
    public void MoveResult(int from, int to)
    {
        if (_results == null || from == to || from < 0 || from >= _results.Count || to < 0 || to >= _results.Count) return;
        if (_results[from].item == null) return;
        (_results[from], _results[to]) = (_results[to], _results[from]);
        RefreshCells();
    }

    /// <summary>屏幕点射线检测命中的搜索结果格索引（-1=未命中；面板关闭的格不参与射线，天然排除）</summary>
    private int RaycastSearchCell(Vector2 screenPos)
    {
        if (EventSystem.current == null) return -1;
        var data = new PointerEventData(EventSystem.current) { position = screenPos };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        foreach (var h in hits)
        {
            var cell = h.gameObject.GetComponent<SearchResultCellUI>();
            if (cell != null) return cell.SlotIndex;
        }
        return -1;
    }

    /// <summary>屏幕点射线检测命中的背包格（null=未命中；面板关闭的格不参与射线，天然排除）</summary>
    private InventoryCellUI RaycastBagCell(Vector2 screenPos)
    {
        if (EventSystem.current == null) return null;
        var data = new PointerEventData(EventSystem.current) { position = screenPos };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        foreach (var h in hits)
        {
            var cell = h.gameObject.GetComponent<InventoryCellUI>();
            if (cell != null && cell.Kind == InventoryCellUI.CellKind.Bag) return cell;
        }
        return null;
    }

    /// <summary>屏幕坐标→Canvas局部坐标（Overlay模式camera传null，同InventoryUI）</summary>
    private Vector2 GetCanvasLocalPos(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out var local);
        return local;
    }

    // ===== 白盒样式构建工具（与InventoryUI同款） =====

    private GameObject CreateBorderedBox(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(_rootPanel.transform, false);
        UIKit.EnsureLoaded();
        var bimg = go.GetComponent<Image>();
        bimg.sprite = UIKit.PanelInner;
        bimg.type = Image.Type.Sliced;
        bimg.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.effectColor = LineColor;
        ol.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);
        return go;
    }

    private Text CreateText(Transform parent, string label, int size, TextAnchor anchor)
    {
        var go = new GameObject("Text", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = label;
        t.font = UIKit.LegacyFont;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = LineColor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.supportRichText = true;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = size + 6f;
        return t;
    }

    private static void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
