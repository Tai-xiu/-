#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
public partial class SearchPanelUI
{
    private void BuildAuthoringPanel(Transform canvasRoot)
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

    public GameObject CreateEditablePrefab(Transform canvas)
    {
        BuildAuthoringPanel(canvas);var v=_rootPanel.AddComponent<UINodeReferences>();v.Add("Status",_statusText);
        for(int i=0;i<_cells.Count;i++)v.Add("Cell"+i,_cells[i].GetComponent<SearchResultCellUI>());
        return _rootPanel;
    }

}
#endif
