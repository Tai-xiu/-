using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 背包格子/装备槽交互组件（数据与表现分离：本组件只转发事件，不直接操作数据）
/// 挂在每个背包格与装备槽物体上（由InventoryUI构建时创建并调用Init）
/// - 点击：未发生拖拽的"纯点击" → InventoryUI.OnCellClicked（弹出操作菜单）
/// - 拖拽：位移超过EventSystem像素拖拽阈值才触发BeginDrag（拖拽与点击互斥的物理基础）
///         BeginDrag时置_dragConsumed标记，同一按下周期内的click事件被抑制
/// - 悬停：拖拽过程中进入/离开格子 → InventoryUI.OnCellHover 控制预览区域高亮
///         （多格物品由Owner给覆盖到的每个格子统一上色：合法绿框/非法红框）
/// </summary>
public class InventoryCellUI : MonoBehaviour,
    IPointerDownHandler, IPointerClickHandler,
    IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>格子类型：背包格 / 武器槽 / 护甲槽 / 快捷栏槽（详情框）</summary>
    public enum CellKind { Bag, EquipWeapon, EquipArmor, Hotbar, EquipBag }

    /// <summary>格子类型</summary>
    public CellKind Kind = CellKind.Bag;

    /// <summary>背包格索引0~19 / 快捷栏槽索引0~3（装备槽忽略此值）</summary>
    public int SlotIndex = -1;

    /// <summary>所属背包UI（事件转发目标）</summary>
    private InventoryUI _owner;

    /// <summary>本次按下周期内是否已发生拖拽（拖拽与点击互斥标记）</summary>
    private bool _dragConsumed;

    /// <summary>边框组件（高亮切换用）</summary>
    private Outline _outline;
    private Vector2 _baseBorder;
    private Color _baseBorderColor;
    public Color BaseColor { get; private set; }


    /// <summary>初始化（创建格子时调用一次）</summary>
    public void Init(InventoryUI owner, CellKind kind, int slotIndex)
    {
        _owner = owner;
        Kind = kind;
        SlotIndex = slotIndex;
        _outline = GetComponent<Outline>();
        BaseColor = GetComponent<Image>() != null ? GetComponent<Image>().color : Color.white;
        if (_outline != null) { _baseBorder = _outline.effectDistance; _baseBorderColor = _outline.effectColor; }
    }

    /// <summary>按下：重置拖拽标记（新一轮点击/拖拽判定开始）</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        _dragConsumed = false;
    }

    /// <summary>点击（松手时触发）：已拖拽过则抑制；右键转发操作菜单（左键无操作——选中已由悬停驱动）</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_dragConsumed) return; // 拖拽与点击互斥：拖拽过的按下周期不触发菜单
        if (_owner == null) return;
        if (eventData.button == PointerEventData.InputButton.Right)
            _owner.OnCellRightClick(this); // 右键：使用/装备/拆分/丢弃菜单
    }

    /// <summary>开始拖拽（位移超过EventSystem像素阈值才触发）：标记并通知Owner拿起物品</summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        _dragConsumed = true; // 本周期松手时的click将被抑制
        if (_owner != null) _owner.OnCellBeginDrag(this);
    }

    /// <summary>拖拽中：通知Owner移动半透明跟随图标</summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (_owner != null) _owner.OnCellDrag(this, eventData);
    }

    /// <summary>拖拽结束：通知Owner判定放置目标并提交（移动/交换/装备/回弹）</summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (_owner != null) _owner.OnCellEndDrag(this);
    }

    /// <summary>指针进入：拖拽过程中通知Owner做预览区域高亮判定</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_owner != null) _owner.OnCellHover(this, true);
    }

    /// <summary>指针离开：通知Owner清除高亮</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (_owner != null) _owner.OnCellHover(this, false);
    }

    /// <summary>
    /// 设置拖拽悬停高亮：指定粗细与颜色（合法=绿框，非法=红框，见InventoryUI）
    /// </summary>
    public void SetHighlight(float thickness, Color color)
    {
        SetBorder(thickness, color);
    }

    /// <summary>
    /// 设置边框粗细与颜色（恢复默认=黑色普通框；选中态由Owner判定恢复加粗）
    /// </summary>
    public void SetBorder(float thickness, Color color)
    {
        if (_outline == null) _outline = GetComponent<Outline>();
        if (_outline != null)
        {
            bool normal = color == InventoryUI.LineColor;
            _outline.effectDistance = normal ? _baseBorder * (thickness / InventoryUI.BorderWidth) : new Vector2(thickness, thickness);
            _outline.effectColor = normal ? _baseBorderColor : color;
        }
    }
}
