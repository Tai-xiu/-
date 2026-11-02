using UnityEngine;
using UnityEngine.UI;

/// <summary>背包界面的序列化节点引用。布局与外观由 Prefab 编辑，控制器只更新数据。</summary>
public class InventoryPanelView : MonoBehaviour
{
    [Header("物品网格：保持从左上角开始、按行排列、固定五列")]
    public GridLayoutGroup grid;
    [Range(0, 1)] public float rarityTint = 0.45f;
    public GameObject slotTemplate;
    public InventoryCellUI[] previewCells;
    [Header("装备区")]
    public InventoryCellUI weaponCell, armorCell, bagCell;
    public Text weaponText, armorText;
    public Text bagName;
    public Image weaponSilhouette, armorSilhouette, bagIcon;
    public RectTransform weaponIconRoot, armorIconRoot;
    [Header("信息区")]
    public Text stats, detailTitle, detailDescription, capacityText, hint;
    public Image hpFill;
    public InventoryCellUI[] hotbarCells;
    public Text[] hotbarCounts;
    public Button closeButton;
    [Header("弹窗与模板：预览时可以临时勾选 Active")]
    public GameObject menuTemplate;
    public Button menuButtonTemplate;
    public GameObject splitRoot;
    public Slider splitSlider;
    public Text splitTitle, splitQuantity;
    public Button splitMinus, splitPlus, splitCancel, splitConfirm;

    public bool ValidateReferences()
    {
        if (!grid || !slotTemplate || !weaponCell || !armorCell || !bagCell ||
            !weaponText || !armorText || !weaponIconRoot || !armorIconRoot || !bagIcon ||
            !stats || !detailTitle || !detailDescription || !capacityText || !hint || !hpFill ||
            !closeButton || !menuTemplate || !menuButtonTemplate || !splitRoot || !splitSlider ||
            !splitTitle || !splitQuantity || !splitMinus || !splitPlus || !splitCancel || !splitConfirm)
            return false;
        if (previewCells == null || hotbarCells == null || hotbarCells.Length != 4 ||
            hotbarCounts == null || hotbarCounts.Length != 4) return false;
        foreach (var cell in previewCells) if (!cell) return false;
        for (int i = 0; i < 4; i++) if (!hotbarCells[i] || !hotbarCounts[i]) return false;
        return grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount &&
            grid.constraintCount == Inventory.Cols && grid.startAxis == GridLayoutGroup.Axis.Horizontal &&
            grid.startCorner == GridLayoutGroup.Corner.UpperLeft && grid.childAlignment == TextAnchor.UpperLeft;
    }
}
