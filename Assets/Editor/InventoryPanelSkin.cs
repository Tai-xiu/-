// 背包面板外观改造（只做 UI 结构，功能节点由你接线）
// 菜单：Tools/UI/背包面板外观改造
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class InventoryPanelSkin
{
    const string PATH = "Assets/Prefabs/UI/InventoryPanel.prefab";
    const float Cell = 74f, Gap = 8f;
    const int Cols = 5, VisibleRows = 3;
    const float SlotBox = 96f;

    [MenuItem("Tools/UI/背包面板外观改造")]
    public static void Apply()
    {
        UIKit.EnsureLoaded();
        var root = PrefabUtility.LoadPrefabContents(PATH);
        if (root == null) { Debug.LogError("找不到 " + PATH); return; }

        var rootRt = (RectTransform)root.transform;
        rootRt.sizeDelta = new Vector2(rootRt.sizeDelta.x, 660f);   // 高度足够容纳三行物品栏

        var left = root.transform.Find("MainRow/LeftCol");
        var equipRow = left.Find("EquipRow");
        var bagRow = left.Find("BagRow");

        // ---------- 1) 武器 / 护甲 / 背包：统一方形外观 ----------
        StyleEquipBox(equipRow.Find("武器SlotBox"), "武器", "silhouette_weapon");
        StyleEquipBox(equipRow.Find("护甲SlotBox"), "护甲", "silhouette_armor");
        // 行改为左对齐 + 固定高度
        var ehlg = equipRow.GetComponent<HorizontalLayoutGroup>();
        ehlg.childAlignment = TextAnchor.MiddleLeft;
        equipRow.GetComponent<LayoutElement>().preferredHeight = SlotBox + 16f;

        // ---------- 2) 背包槽：与武器栏同款外观 ----------
        var bagBox = bagRow.Find("背包SlotBox");
        StyleEquipBox(bagBox, "背包", "silhouette_bag");

        // ---------- 3) 快捷栏并入左栏：背包右侧 ----------
        if (bagRow.Find("HotbarBox") == null) BuildHotbarBox(bagRow);
        bagRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
        bagRow.GetComponent<LayoutElement>().preferredHeight = SlotBox + 16f;

        // ---------- 4) 物品栏外框 + 3 行视口 + 右侧滚动条 ----------
        var scroll = left.Find("GridScroll");
        if (scroll == null) scroll = left.Find("GridFrame/GridScroll");   // 已改造过：在框里
        var frame = left.Find("GridFrame");
        if (frame == null)
        {
            var frameGo = new GameObject("GridFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            frame = frameGo.transform;
            frame.SetParent(left, false);
            frame.SetSiblingIndex(scroll.GetSiblingIndex());
            var fimg = frame.GetComponent<Image>();
            fimg.sprite = UIKit.PanelInner;
            fimg.type = Image.Type.Sliced;
            fimg.color = Color.white;
            var fle = frame.GetComponent<LayoutElement>();
            fle.preferredHeight = VisibleRows * Cell + (VisibleRows - 1) * Gap + 24f;
            fle.flexibleHeight = 0f;
            scroll.SetParent(frame.transform, false);
        }

        var frt = (RectTransform)frame.transform;
        var srt = (RectTransform)scroll;
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.offsetMin = new Vector2(8f, 8f);
        srt.offsetMax = new Vector2(-24f, -8f);   // 右侧留出滚动条位置

        // 滚动条
        var sb = frame.GetComponentInChildren<Scrollbar>(true);
        if (sb == null) sb = BuildScrollbar(frame);
        var srect = scroll.GetComponent<ScrollRect>();
        if (srect != null)
        {
            srect.vertical = true;
            srect.horizontal = false;
            srect.verticalScrollbar = sb;
            srect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        PrefabUtility.SaveAsPrefabAsset(root, PATH);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log("[InventoryPanelSkin] 外观改造完成");
    }

    // ---------- 方块化装备栏 ----------
    static void StyleEquipBox(Transform box, string label, string silhouette)
    {
        if (box == null) return;
        var rt = (RectTransform)box;
        rt.sizeDelta = new Vector2(SlotBox, SlotBox);
        var le = box.GetComponent<LayoutElement>();
        if (le == null) le = box.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = SlotBox;
        le.preferredHeight = SlotBox;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;

        var img = box.GetComponent<Image>();
        if (img != null) { img.sprite = UIKit.PanelInner; img.type = Image.Type.Sliced; img.color = Color.white; }

        // 标签
        var textT = box.Find("Text");
        if (textT == null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(box, false);
            textT = go.transform;
        }
        var t = textT.GetComponent<Text>();
        t.text = label;
        t.font = UIKit.LegacyFont;
        t.fontSize = 14;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color32(0xD8, 0xC0, 0x88, 0xFF);
        t.raycastTarget = false;
        var trt = (RectTransform)textT;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.sizeDelta = new Vector2(0f, 20f);
        trt.anchoredPosition = new Vector2(0f, -4f);

        // 内框（棕黑底 + 白印）
        var areaName = label + "DashArea";
        var area = box.Find(areaName);
        if (area == null && label != "武器" && label != "护甲")
        {
            var go = new GameObject(areaName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(box, false);
            area = go.transform;
        }
        if (area != null)
        {
            var art = (RectTransform)area;
            art.anchorMin = new Vector2(0f, 0f);
            art.anchorMax = new Vector2(1f, 1f);
            art.offsetMin = new Vector2(6f, 6f);
            art.offsetMax = new Vector2(-6f, -26f);
            var aimg = area.GetComponent<Image>();
            if (aimg == null) aimg = area.gameObject.AddComponent<Image>();
            aimg.sprite = UIKit.Slot;
            aimg.type = Image.Type.Sliced;
            aimg.color = Color.white;

            // 白印
            var sil = area.Find("Silhouette");
            if (sil == null)
            {
                var go = new GameObject("Silhouette", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(area, false);
                sil = go.transform;
            }
            var srt = (RectTransform)sil;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(40f, 40f);
            srt.anchoredPosition = Vector2.zero;
            var simg = sil.GetComponent<Image>();
            simg.sprite = Resources.Load<Sprite>("UI/PixelKit/" + silhouette);
            simg.color = new Color(1f, 1f, 1f, 0.55f);
            simg.raycastTarget = false;
        }
    }

    // ---------- 快捷栏板块 ----------
    static Transform BuildHotbarBox(Transform parent)
    {
        var box = new GameObject("HotbarBox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        box.transform.SetParent(parent, false);
        var img = box.GetComponent<Image>();
        img.sprite = UIKit.PanelInner;
        img.type = Image.Type.Sliced;
        img.color = Color.white;
        var le = box.GetComponent<LayoutElement>();
        le.preferredWidth = 4 * 52f + 3 * 6f + 16f;
        le.preferredHeight = SlotBox;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;

        var row = new GameObject("Slots", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(box.transform, false);
        var rrt = (RectTransform)row.transform;
        rrt.anchorMin = Vector2.zero;
        rrt.anchorMax = Vector2.one;
        rrt.offsetMin = new Vector2(8f, 8f);
        rrt.offsetMax = new Vector2(-8f, -8f);
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleCenter;

        for (int i = 0; i < 4; i++)
        {
            var s = new GameObject("Hotbar_" + i, typeof(RectTransform), typeof(Image));
            s.transform.SetParent(row.transform, false);
            var srt = s.GetComponent<RectTransform>();
            srt.sizeDelta = new Vector2(52f, 52f);
            var simg = s.GetComponent<Image>();
            simg.sprite = UIKit.Slot;
            simg.type = Image.Type.Sliced;
            simg.color = Color.white;
        }
        return box.transform;
    }

    // ---------- 竖向滚动条 ----------
    static Scrollbar BuildScrollbar(Transform frame)
    {
        var go = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        go.transform.SetParent(frame, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.offsetMin = new Vector2(-20f, 8f);   // 右缘内缩 8
        rt.offsetMax = new Vector2(-8f, -8f);

        var img = go.GetComponent<Image>();
        img.sprite = UIKit.BarFrame;
        img.type = Image.Type.Sliced;
        img.color = Color.white;

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(go.transform, false);
        var hrt = (RectTransform)handle.transform;
        hrt.anchorMin = Vector2.zero;
        hrt.anchorMax = Vector2.one;
        hrt.offsetMin = new Vector2(4f, 2f);
        hrt.offsetMax = new Vector2(-4f, -2f);
        var himg = handle.GetComponent<Image>();
        himg.color = new Color32(0xD8, 0xC0, 0x88, 0xFF);

        var sb = go.GetComponent<Scrollbar>();
        sb.handleRect = hrt;
        sb.targetGraphic = himg;
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.value = 1f;
        return sb;
    }

    /// <summary>只修复 GridScroll 的锚点（拖滑块误改锚点导致物品栏消失时用这个）</summary>
    [MenuItem("Tools/UI/修复物品栏锚点（GridScroll）")]
    public static void RepairGridScroll()
    {
        var root = PrefabUtility.LoadPrefabContents(PATH);
        var frame = root.transform.Find("MainRow/LeftCol/GridFrame");
        var scroll = frame != null ? frame.Find("GridScroll") : null;
        if (scroll == null) { PrefabUtility.UnloadPrefabContents(root); Debug.LogError("[修复] 找不到 GridScroll"); return; }
        var rt = (RectTransform)scroll;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(8f, 8f);
        rt.offsetMax = new Vector2(-24f, -8f);
        PrefabUtility.SaveAsPrefabAsset(root, PATH);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log("[修复] GridScroll 锚点已恢复（铺满 GridFrame）");
    }
}
