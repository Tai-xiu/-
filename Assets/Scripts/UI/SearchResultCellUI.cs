using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 搜索结果格交互组件（同InventoryCellUI转发模式：只转发事件，数据操作在SearchPanelUI）
/// - 悬停：进入/离开 → SearchPanelUI.OnCellHoverEnter/Exit（指向即选中，移开取消）
/// - 拖拽：位移超过EventSystem像素拖拽阈值才触发BeginDrag（BeginDrag时置_dragConsumed标记）；
///         释放点由SearchPanelUI判定（搜索格=换位/背包格=拾取/其他=回弹）
/// </summary>
public class SearchResultCellUI : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>所属搜索面板UI（事件转发目标）</summary>
    private SearchPanelUI _owner;

    /// <summary>结果格索引0~11</summary>
    public int SlotIndex { get; private set; } = -1;

    /// <summary>初始化（创建格子时调用一次）</summary>
    public void Init(SearchPanelUI owner, int slotIndex)
    {
        _owner = owner;
        SlotIndex = slotIndex;
    }

    /// <summary>指针进入：通知Owner悬停选中该格</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_owner != null) _owner.OnCellHoverEnter(SlotIndex);
    }

    /// <summary>指针离开：通知Owner取消选中</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (_owner != null) _owner.OnCellHoverExit(SlotIndex);
    }

    /// <summary>开始拖拽（位移超过EventSystem像素阈值才触发）：通知Owner拿起结果物品</summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (_owner != null) _owner.OnCellBeginDrag(SlotIndex);
    }

    /// <summary>拖拽中：通知Owner移动跟随图标</summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (_owner != null) _owner.OnCellDrag(SlotIndex, eventData);
    }

    /// <summary>拖拽结束：通知Owner判定释放目标并提交（拾取入包/换位/回弹）</summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (_owner != null) _owner.OnCellEndDrag(SlotIndex);
    }
}
