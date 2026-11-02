#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEditor;
public partial class ShopUI
{
    private void BuildAuthoringPanel(Transform canvasRoot)
    {
        // 面板底板（竖版，白盒底色+黑色实线边框）
        _panel = new GameObject("ShopPanel", typeof(Image), typeof(VerticalLayoutGroup));
        UIKit.EnsureLoaded();
        var panelImg = _panel.GetComponent<Image>();
        panelImg.sprite = UIKit.Panel;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = Color.white;
        var prt = _panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(480f, 430f);
        _panel.transform.SetParent(canvasRoot, false);
        var pOutline = _panel.AddComponent<Outline>();
        pOutline.enabled = false;
        pOutline.effectDistance = new Vector2(BorderWidth, BorderWidth);

        var pvlg = _panel.GetComponent<VerticalLayoutGroup>();
        pvlg.padding = new RectOffset(10, 10, 8, 8);
        pvlg.spacing = 8f;
        pvlg.childControlWidth = true;
        pvlg.childControlHeight = true;
        pvlg.childForceExpandHeight = false;
        pvlg.childForceExpandWidth = true;

        // ── 1. 顶部标题栏：实线方框，左「商店」中警告 右「金币 n」 ──
        var titleBar = CreateBorderedBox(_panel.transform, "TitleBar", 44f);
        var titleLE = titleBar.GetComponent<LayoutElement>();
        titleLE.flexibleWidth = 0f;   // 显式归零：标题栏内HLG forceExpand会向父级上报flexible=1，
        titleLE.flexibleHeight = 0f;  // 抢占列表区高度（uGUI嵌套布局实测踩坑，同Tabs行处理）
        var tilg = titleBar.AddComponent<HorizontalLayoutGroup>();
        tilg.padding = new RectOffset(10, 40, 6, 6); // 右侧留40给右上角退出按钮，避免金币文字被盖
        tilg.childControlWidth = true;
        tilg.childControlHeight = true;
        tilg.childForceExpandWidth = true;
        tilg.childForceExpandHeight = true;
        var title = CreateText(titleBar.transform, "商店", 16, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        title.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _warningText = CreateText(titleBar.transform, "", 12, TextAnchor.MiddleCenter, WarnColor); // 警告上移标题栏中部（底部信息条已取消）
        _warningText.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _coinTitleText = CreateText(titleBar.transform, "金币 50", 14, TextAnchor.MiddleRight);
        _coinTitleText.GetComponent<LayoutElement>().flexibleWidth = 1f;

        // ── 2. Tab切换区：两个并排方框 ──
        var tabRow = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        tabRow.transform.SetParent(_panel.transform, false);
        var tle = tabRow.GetComponent<LayoutElement>();
        tle.preferredHeight = 36f;
        tle.flexibleWidth = 0f;
        tle.flexibleHeight = 0f;
        var thlg = tabRow.GetComponent<HorizontalLayoutGroup>();
        thlg.spacing = 8f;
        thlg.childControlWidth = true;
        thlg.childControlHeight = true;
        thlg.childForceExpandWidth = true;
        thlg.childForceExpandHeight = true;

        var buyTab = CreateTabBox(tabRow.transform, "购买");
        _tabBuyImage = buyTab.GetComponent<Image>();
        _tabBuyText = buyTab.GetComponentInChildren<Text>();
        var sellTab = CreateTabBox(tabRow.transform, "出售");
        _tabSellImage = sellTab.GetComponent<Image>();
        _tabSellText = sellTab.GetComponentInChildren<Text>();
        // 点击切页（Tab本身即按钮）
        buyTab.AddComponent<Button>().onClick.AddListener(() => { AudioManager.PlaySfx("SFX_UIClick"); SwitchPage(0); });
        sellTab.AddComponent<Button>().onClick.AddListener(() => { AudioManager.PlaySfx("SFX_UIClick"); SwitchPage(1); });

        // ── 3. 商品列表区：购买页（左分类栏+右列表）/ 出售页（固定显示框+超出滚动） ──
        BuildBuyPage();
        BuildSellPage();

        _sellPage.SetActive(false); // 编辑预览默认购买页

        BuildCloseButton(); // 右上角退出按钮（红圆底白叉）
    }

    private void BuildCloseButton()
    {
        var btn = new GameObject("Btn_Close", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btn.transform.SetParent(_panel.transform, false);
        var img = btn.GetComponent<Image>();
        UIKit.EnsureLoaded();
        img.sprite = UIKit.CircleBtn;
        img.type = Image.Type.Simple;
        img.color = Color.white;
        var rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-8f, -8f);
        rt.sizeDelta = new Vector2(30f, 30f);
        btn.GetComponent<LayoutElement>().ignoreLayout = true;
        btn.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick");
            ClosePanel();
        });

        MakeXBar(btn.transform, 45f);
        MakeXBar(btn.transform, -45f);
    }

    private void MakeXBar(Transform parent, float zAngle)
    {
        var bar = new GameObject("X", typeof(Image));
        bar.transform.SetParent(parent, false);
        var img = bar.GetComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = false;
        var rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(16f, 3f);
        rt.localRotation = Quaternion.Euler(0f, 0f, zAngle);
    }

    private void BuildBuyPage()
    {
        _buyPage = new GameObject("BuyPage", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        _buyPage.transform.SetParent(_panel.transform, false);
        var ple = _buyPage.GetComponent<LayoutElement>();
        ple.flexibleHeight = 1f;
        ple.flexibleWidth = 0f;
        var hlg = _buyPage.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // 左侧分类栏：三个Tab上下排列，均分列高（选中=黑底白字，与页签样式一致）
        var col = new GameObject("CategoryCol", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        col.transform.SetParent(_buyPage.transform, false);
        var cle = col.GetComponent<LayoutElement>();
        cle.preferredWidth = 76f;
        cle.flexibleWidth = 0f;
        cle.flexibleHeight = 1f;
        var cvlg = col.GetComponent<VerticalLayoutGroup>();
        cvlg.spacing = 8f;
        cvlg.childControlWidth = true;
        cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true;
        cvlg.childForceExpandHeight = true;
        for (int i = 0; i < CategoryLabels.Length; i++)
        {
            int idx = i; // 闭包值捕获（for循环变量共享，必须拷贝到局部）
            var tab = CreateTabBox(col.transform, CategoryLabels[i]);
            _catImages[i] = tab.GetComponent<Image>();
            _catTexts[i] = tab.GetComponentInChildren<Text>();
            tab.AddComponent<Button>().onClick.AddListener(() =>
            {
                AudioManager.PlaySfx("SFX_UIClick");
                SwitchCategory(idx);
            });
        }


        // 右侧商品列表：当前选中类别的行容器
        var list = new GameObject("BuyList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        list.transform.SetParent(_buyPage.transform, false);
        var lle = list.GetComponent<LayoutElement>();
        lle.flexibleWidth = 1f;
        lle.flexibleHeight = 1f;
        var vlg = list.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true; // 必须由布局接管行高，否则行保持新建默认100高，LayoutElement.preferredHeight=48会被忽略
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        _buyList = list.GetComponent<RectTransform>();
    }

    private void BuildSellPage()
    {
        _sellPage = new GameObject("SellPage", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        _sellPage.transform.SetParent(_panel.transform, false);
        var le = _sellPage.GetComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.flexibleWidth = 0f;
        _sellPage.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // 透明底仅承接拖拽/滚轮射线（视觉与原列表页一致）
        _sellScroll = _sellPage.GetComponent<ScrollRect>();
        _sellScroll.horizontal = false;
        _sellScroll.vertical = true;
        _sellScroll.movementType = ScrollRect.MovementType.Clamped;
        _sellScroll.scrollSensitivity = 8f;

        // 视口：RectMask2D软裁剪超出显示框的行
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(_sellPage.transform, false);
        StretchRect(viewport.GetComponent<RectTransform>());

        // 内容：行容器，高度随行数由ContentSizeFitter驱动，ScrollRect据其计算滚动范围
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = new Vector2(0f, 0f);
        crt.offsetMax = new Vector2(-18f, 0f); // 右侧留18给滑动条，行按钮不被盖
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true; // 必须由布局接管行高，否则行保持新建默认100高，LayoutElement.preferredHeight=48会被忽略
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _sellList = crt;
        _sellScroll.content = crt;
        _sellScroll.viewport = viewport.GetComponent<RectTransform>();
        _sellScroll.verticalScrollbar = BuildScrollbar(_sellPage.transform);
    }

    private Scrollbar BuildScrollbar(Transform parent)
    {
        UIKit.EnsureLoaded();
        var go = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        go.transform.SetParent(parent, false);
        // 轨道：kit 条框（深色内凹），与面板同族，不再是白块
        var trackImg = go.GetComponent<Image>();
        trackImg.sprite = UIKit.BarFrame;
        trackImg.type = Image.Type.Sliced;
        trackImg.color = Color.white;
        var ol = go.AddComponent<Outline>();
        ol.enabled = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(14f, 0f);
        rt.anchoredPosition = new Vector2(-2f, 0f);

        var handle = new GameObject("Sliding Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(go.transform, false);
        handle.GetComponent<Image>().color = LineColor;
        handle.GetComponent<Image>().raycastTarget = false;
        var hrt = handle.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0f, 0f);
        hrt.anchorMax = new Vector2(1f, 1f);
        hrt.sizeDelta = new Vector2(-10f, -6f); // 横向/纵向内缩，露出 kit 轨道边框

        var sb = go.GetComponent<Scrollbar>();
        sb.handleRect = hrt;
        sb.targetGraphic = handle.GetComponent<Image>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.value = 1f;
        return sb;
    }

    private GameObject CreateTabBox(Transform parent, string label)
    {
        var go = new GameObject("Tab_" + label, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.enabled = false;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var t = CreateText(go.transform, label, 14, TextAnchor.MiddleCenter, TabSelectedText);
        t.fontStyle = FontStyle.Bold;
        StretchRect(t.rectTransform);
        t.GetComponent<LayoutElement>().ignoreLayout = true;
        return go;
    }

    private GameObject CreateAuthoringEmptyRow(RectTransform parent, string label)
    {
        var row = CreateBorderedBox(parent, "EmptyRow", RowHeight);
        var t = CreateText(row.transform, label, 13, TextAnchor.MiddleCenter, LineColor);
        StretchRect(t.rectTransform);
        t.GetComponent<LayoutElement>().ignoreLayout = true;
        return row;
    }

    private GameObject CreateAuthoringShopRow(RectTransform parent, string name, string attr, string price, string btnLabel, UnityAction onBtn)
    {
        var row = CreateBorderedBox(parent, "ShopRow", RowHeight);
        var le = row.GetComponent<LayoutElement>();
        le.flexibleWidth = 1f; // 等宽
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 8, 8);
        hlg.spacing = 8f;
        hlg.childControlWidth = true; // 必须由布局接管子项尺寸，否则文本保持新建默认100宽，按钮会被推出行/面板右缘
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        CreateDashedIconBox(row.transform, 32f); // 虚线图标占位

        var nameT = CreateText(row.transform, name, 14, TextAnchor.MiddleLeft, LineColor);
        var nle = nameT.GetComponent<LayoutElement>();
        nle.preferredWidth = 84f;

        var attrT = CreateText(row.transform, attr, 12, TextAnchor.MiddleLeft, LineColor);
        var ale = attrT.GetComponent<LayoutElement>();
        ale.preferredWidth = 100f;
        ale.flexibleWidth = 1f; // 吸收剩余宽度，保证价格/按钮右端对齐

        var priceT = CreateText(row.transform, price, 12, TextAnchor.MiddleRight, LineColor);
        priceT.GetComponent<LayoutElement>().preferredWidth = 66f;

        CreateBorderedButton(row.transform, btnLabel, new Vector2(64f, 30f), onBtn);
        return row;
    }

    private GameObject CreateDashedIconBox(Transform parent, float size)
    {
        UIKit.EnsureLoaded();
        var go = new GameObject("IconBox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = size;
        le.preferredHeight = size;

        var img = go.GetComponent<Image>();
        img.sprite = UIKit.Slot;
        img.type = Image.Type.Sliced;
        img.color = Color.white;
        img.raycastTarget = false;
        return go;
    }

    private void AddDash(RectTransform parent, Vector2 pos, Vector2 size, Vector2 anchor)
    {
        var seg = new GameObject("Dash", typeof(Image));
        seg.transform.SetParent(parent, false);
        seg.GetComponent<Image>().color = LineColor;
        seg.GetComponent<Image>().raycastTarget = false;
        var srt = seg.GetComponent<RectTransform>();
        srt.anchorMin = anchor;
        srt.anchorMax = anchor;
        srt.pivot = anchor;
        srt.sizeDelta = size;
        srt.anchoredPosition = pos;
    }

    private void CreateBorderedButton(Transform parent, string label, Vector2 size, UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement), typeof(Button));
        go.transform.SetParent(parent, false);
        UIKit.EnsureLoaded();
        var bimg = go.GetComponent<Image>();
        bimg.sprite = UIKit.BtnN;          // kit 按钮框
        bimg.type = Image.Type.Sliced;
        bimg.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.enabled = false;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = size.x;
        le.preferredHeight = size.y;
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick"); // UI点击音效
            onClick.Invoke();
        });

        var t = CreateText(go.transform, label, 13, TextAnchor.MiddleCenter, LineColor);
        StretchRect(t.rectTransform);
    }

    private GameObject CreateBorderedBox(Transform parent, string boxName, float height)
    {
        var go = new GameObject(boxName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        UIKit.EnsureLoaded();
        var bimg = go.GetComponent<Image>();
        bimg.sprite = UIKit.PanelInner;
        bimg.type = Image.Type.Sliced;
        bimg.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.enabled = false;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        return go;
    }

    private Text CreateText(Transform parent, string label, int size, TextAnchor anchor, Color? color = null)
    {
        var go = new GameObject("Text", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = label;
        t.font = UIKit.LegacyFont;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color ?? LineColor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        go.AddComponent<LayoutElement>();
        return t;
    }

    private void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public GameObject CreateEditablePrefab(Transform canvas)
    {
        BuildAuthoringPanel(canvas);
        var v=_panel.AddComponent<UINodeReferences>();
        v.Add("Coins",_coinTitleText);v.Add("Warning",_warningText);
        v.Add("BuyTab",_tabBuyImage.GetComponent<Button>());v.Add("SellTab",_tabSellImage.GetComponent<Button>());
        v.Add("BuyTabText",_tabBuyText);v.Add("SellTabText",_tabSellText);
        v.Add("BuyPage",_buyPage);v.Add("SellPage",_sellPage);v.Add("BuyList",_buyList);v.Add("SellList",_sellList);v.Add("SellScroll",_sellScroll);
        for(int i=0;i<_catImages.Length;i++){v.Add("Category"+i,_catImages[i].GetComponent<Button>());v.Add("CategoryText"+i,_catTexts[i]);}
        v.Add("Close",_panel.transform.Find("Btn_Close").GetComponent<Button>());
        var row=CreateAuthoringShopRow(_buyList,"商品名称","属性说明","10金币","购买",()=>{});
        var r=row.AddComponent<UINodeReferences>();var texts=row.GetComponentsInChildren<Text>();
        r.Add("Name",texts[0]);r.Add("Attribute",texts[1]);r.Add("Price",texts[2]);r.Add("ActionText",texts[3]);r.Add("Action",row.GetComponentInChildren<Button>());v.Add("Row",r);
        var empty=CreateAuthoringEmptyRow(_sellList,"（暂无可出售物品）");
        var e=empty.AddComponent<UINodeReferences>();e.Add("Text",empty.GetComponentInChildren<Text>());v.Add("Empty",e);
        v.selectedSprite=UIKit.BtnN;v.inactiveSprite=UIKit.BtnP;
        foreach(var b in _panel.GetComponentsInChildren<Button>(true))b.onClick.RemoveAllListeners();
        return _panel;
    }

}
#endif
