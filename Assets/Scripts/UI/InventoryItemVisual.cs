using UnityEngine;
using UnityEngine.UI;

/// <summary>可编辑的物品图标模板，供背包、拖拽和已有 HUD 调用复用。</summary>
public class InventoryItemVisual : MonoBehaviour
{
    public Image background, icon;
    public Text fallback, itemName, count;
    public CanvasGroup group;
    [Tooltip("开启后用物品类型颜色更新底色；关闭则保留模板颜色")]
    public bool tintByType = true;
    [Range(0, 1)] public float ghostAlpha = 0.55f;

    public void Apply(Inventory.ItemStack stack, Vector2 size, ItemOrient orient, bool ghost, bool withName)
    {
        ((RectTransform)transform).sizeDelta = size;
        var item = stack.item;
        Color tint = item.type == ItemType.Weapon ? new Color(.85f,.55f,.2f) :
            item.type == ItemType.Armor ? new Color(.35f,.55f,.85f) :
            item.type == ItemType.Consumable ? new Color(.35f,.75f,.4f) : new Color(.6f,.6f,.6f);
        if (tintByType) background.color = new Color(tint.r, tint.g, tint.b, background.color.a);
        icon.sprite = item.icon;
        icon.gameObject.SetActive(item.icon != null);
        fallback.gameObject.SetActive(item.icon == null);
        fallback.text = item.type == ItemType.Weapon ? "剑" : item.type == ItemType.Armor ? "甲" :
            item.type == ItemType.Consumable ? "瓶" : item.type == ItemType.Backpack ? "包" : "币";
        var rotation = Quaternion.Euler(0, 0, orient == ItemOrient.Vertical ? -90 : 0);
        icon.rectTransform.localRotation = rotation;
        fallback.rectTransform.localRotation = rotation;
        itemName.text = item.itemName;
        itemName.gameObject.SetActive(withName);
        count.text = "x" + stack.count;
        count.gameObject.SetActive(stack.count > 1);
        group.alpha = ghost ? ghostAlpha : 1;
        group.blocksRaycasts = false;
        group.interactable = false;
    }
}
