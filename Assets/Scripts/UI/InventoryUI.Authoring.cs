#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEditor;

// 仅供首次迁移使用；游戏启动不会调用这些构建函数。
public partial class InventoryUI
{
    private const float CellSize = 74f;
    private const float CellGap = 8f;
    private readonly System.Collections.Generic.List<RectTransform> _dashedRects = new System.Collections.Generic.List<RectTransform>();
private void BuildAuthoringPanel(Transform canvasRoot)
    {
        if (canvasRoot == null)
        {
            Debug.LogWarning("[InventoryUI] BuildPanel: canvasRoot为null，跳过");
            return;
        }

        // 根面板（实线边框，填充与底色一致→视觉上无色块）
        UIKit.EnsureLoaded();
        _rootPanel = new GameObject("InventoryPanel", typeof(Image), typeof(VerticalLayoutGroup));
        var pimg = _rootPanel.GetComponent<Image>();
        pimg.sprite = UIKit.Panel;          // kit 九宫格面板框
        pimg.type = Image.Type.Sliced;
        pimg.color = Color.white;
        var prt = _rootPanel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(660f, 544f);
        _rootPanel.transform.SetParent(canvasRoot, false);
        var pOutline = _rootPanel.AddComponent<Outline>();
        pOutline.enabled = false;   // kit 面板框自带边框，Outline 关闭
        pOutline.effectDistance = new Vector2(BorderWidth, BorderWidth);

        var pvlg = _rootPanel.GetComponent<VerticalLayoutGroup>();
        pvlg.padding = new RectOffset(10, 10, 8, 8);
        pvlg.spacing = 10f;
        pvlg.childControlWidth = true;
        pvlg.childControlHeight = true;
        pvlg.childForceExpandHeight = false;
        pvlg.childForceExpandWidth = true;

        // ── 1. 顶部标题栏 ──
        var titleBar = CreateBorderedBox(_rootPanel.transform, "TitleBar");
        var tle = titleBar.GetComponent<LayoutElement>();
        tle.preferredHeight = 40f;
        tle.flexibleWidth = 0f;   // 显式归零：防止LayoutGroup的flexible=1逐级上报（实测踩坑，下同）
        tle.flexibleHeight = 0f;
        var titleText = CreateText(titleBar.transform, "背包(Tab键开关)", 18, TextAnchor.MiddleCenter, LineColor);
        titleText.fontStyle = FontStyle.Bold;
        StretchRect(titleText.rectTransform);
        titleText.GetComponent<LayoutElement>().ignoreLayout = true;

        BuildCloseButton(); // 右上角关闭按钮（红圆白叉，风格同ShopUI）

        // ── 2. 主行：左半区 + 右半区 ──
        var mainRow = new GameObject("MainRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        mainRow.transform.SetParent(_rootPanel.transform, false);
        var mle = mainRow.GetComponent<LayoutElement>();
        mle.flexibleHeight = 1f;
        mle.flexibleWidth = 0f;
        var mhlg = mainRow.GetComponent<HorizontalLayoutGroup>();
        mhlg.spacing = 10f;
        mhlg.childControlWidth = true;
        mhlg.childControlHeight = true;
        mhlg.childForceExpandWidth = false;
        mhlg.childForceExpandHeight = true;

        // —— 左半区：宽度与网格严格等宽（5×74 + 4×8 + 左右padding 1×2 = 404） ——
        var leftCol = new GameObject("LeftCol", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        leftCol.transform.SetParent(mainRow.transform, false);
        var lle = leftCol.GetComponent<LayoutElement>();
        lle.preferredWidth = 404f;
        lle.flexibleWidth = 0f;
        lle.flexibleHeight = 1f;
        var lvlg = leftCol.GetComponent<VerticalLayoutGroup>();
        lvlg.spacing = 10f;
        lvlg.childControlWidth = true;
        lvlg.childControlHeight = true;
        lvlg.childForceExpandHeight = false;
        lvlg.childForceExpandWidth = true;

        // —— 2a. 装备槽行 ——
        var equipRow = new GameObject("EquipRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        equipRow.transform.SetParent(leftCol.transform, false);
        var ele = equipRow.GetComponent<LayoutElement>();
        ele.preferredHeight = 112f;
        ele.flexibleWidth = 0f;
        ele.flexibleHeight = 0f;
        var ehlg = equipRow.GetComponent<HorizontalLayoutGroup>();
        ehlg.spacing = 8f;
        ehlg.childControlWidth = true;
        ehlg.childControlHeight = true;
        ehlg.childForceExpandWidth = true;
        ehlg.childForceExpandHeight = true;

        BuildEquipSlot(equipRow.transform, "武器", InventoryCellUI.CellKind.EquipWeapon);
        BuildEquipSlot(equipRow.transform, "护甲", InventoryCellUI.CellKind.EquipArmor);

        // —— 2a-2. 背包栏（装备槽下方居中；装背包后提升格数上限） ——
        var bagRow = new GameObject("BagRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        bagRow.transform.SetParent(leftCol.transform, false);
        var bagLe = bagRow.GetComponent<LayoutElement>();
        bagLe.preferredHeight = 96f;
        bagLe.flexibleWidth = 0f;
        bagLe.flexibleHeight = 0f;
        var bhlg = bagRow.GetComponent<HorizontalLayoutGroup>();
        bhlg.spacing = 8f;
        bhlg.childControlWidth = true;
        bhlg.childControlHeight = true;
        bhlg.childForceExpandWidth = false;
        bhlg.childForceExpandHeight = true;
        bhlg.childAlignment = TextAnchor.MiddleCenter;

        var bagBox = CreateBorderedBox(bagRow.transform, "背包SlotBox");
        var bagCellUI = bagBox.AddComponent<InventoryCellUI>();
        bagCellUI.Init(this, InventoryCellUI.CellKind.EquipBag, -1);
        var bagBoxLe = bagBox.GetComponent<LayoutElement>();
        bagBoxLe.preferredWidth = 96f;
        bagBoxLe.flexibleWidth = 0f;
        bagBoxLe.flexibleHeight = 0f;

        var bagIconGo = new GameObject("Silhouette", typeof(RectTransform), typeof(Image));
        bagIconGo.transform.SetParent(bagBox.transform, false);
        var bagRt = bagIconGo.GetComponent<RectTransform>();
        bagRt.anchorMin = bagRt.anchorMax = new Vector2(0.5f, 0.5f);
        bagRt.pivot = new Vector2(0.5f, 0.5f);
        bagRt.sizeDelta = new Vector2(44f, 44f);
        bagRt.anchoredPosition = Vector2.zero;
        UIKit.EnsureLoaded();
        var bagImg = bagIconGo.GetComponent<Image>();
        _bagSilSprite = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/PixelKit/silhouette_bag");
        bagImg.sprite = _bagSilSprite;
        bagImg.color = new Color(1f, 1f, 1f, 0.55f);
        bagImg.raycastTarget = false;
        _bagSil = bagImg;

        // —— 2b. 物品网格：4行×5列=20格 ——
        var gridGo = new GameObject("GridArea", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
        gridGo.transform.SetParent(leftCol.transform, false);
        var gle0 = gridGo.GetComponent<LayoutElement>();
        gle0.preferredHeight = 322f; // 4×74 + 3×8 + 上下padding 1×2
        gle0.flexibleWidth = 0f;
        gle0.flexibleHeight = 0f;
        var glg = gridGo.GetComponent<GridLayoutGroup>();
        glg.padding = new RectOffset(1, 1, 1, 1);
        glg.cellSize = new Vector2(CellSize, CellSize);
        glg.spacing = new Vector2(CellGap, CellGap);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = Inventory.Cols;
        glg.childAlignment = TextAnchor.UpperLeft;
        _gridRoot = gridGo.GetComponent<RectTransform>();

        // —— 滚动容器：GridArea 作为 content，超出视口可滚动 ——
        var scrollGo = new GameObject("GridScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(leftCol.transform, false);
        var scrollLe = scrollGo.GetComponent<LayoutElement>();
        scrollLe.preferredHeight = 240f;
        scrollLe.flexibleHeight = 0f;
        var simg = scrollGo.GetComponent<Image>();
        simg.color = new Color(0f, 0f, 0f, 0f);
        var srt3 = scrollGo.GetComponent<RectTransform>();

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        vrt.pivot = new Vector2(0f, 1f);

        gridGo.transform.SetParent(viewport.transform, false);
        var gleScroll = gridGo.GetComponent<LayoutElement>();
        if (gleScroll != null) DestroyImmediate(gleScroll);   // 交给 ContentSizeFitter 计算高度

        _gridRoot.anchorMin = new Vector2(0f, 1f);
        _gridRoot.anchorMax = new Vector2(1f, 1f);
        _gridRoot.pivot = new Vector2(0.5f, 1f);
        _gridRoot.anchoredPosition = Vector2.zero;
        // 拉伸锚点下宽度=视口宽+sizeDelta.x：reparent残留的默认sizeDelta.x=100会让网格
        // 左右各溢出视口50px（实测物品格整排左移出面板约39单位）。显式归零=与视口同宽；
        // 高度留给ContentSizeFitter按行数驱动，此处只清水平残差
        _gridRoot.sizeDelta = new Vector2(0f, _gridRoot.sizeDelta.y);

        var fitter = gridGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var srect = scrollGo.GetComponent<ScrollRect>();
        srect.viewport = vrt;
        srect.content = _gridRoot;
        srect.horizontal = false;
        srect.vertical = true;
        srect.movementType = ScrollRect.MovementType.Clamped;
        srect.scrollSensitivity = 30f;

        // 创建20个交互格（常驻不重建；物品名由覆盖层单独渲染）
        for (int i = 0; i < _inventory.Capacity; i++)
        {
            _cells.Add(CreateGridCell(i));
        }

        // —— 2c. 网格底部状态行 ——
        var footer = new GameObject("FooterRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        footer.transform.SetParent(leftCol.transform, false);
        var fle = footer.GetComponent<LayoutElement>();
        fle.preferredHeight = 24f;
        fle.flexibleWidth = 0f;
        fle.flexibleHeight = 0f;
        var fhlg = footer.GetComponent<HorizontalLayoutGroup>();
        fhlg.childControlWidth = true;
        fhlg.childControlHeight = true;
        fhlg.childForceExpandWidth = true;
        fhlg.childForceExpandHeight = false;

        _slotCountText = CreateText(footer.transform, "格子 0/" + _inventory.Capacity, 16, TextAnchor.MiddleLeft, LineColor);
        var drag = CreateText(footer.transform, "拖动整理", 16, TextAnchor.MiddleRight, LineColor);
        drag.GetComponent<LayoutElement>().flexibleWidth = 1f;

        // —— 右半区 ——
        var rightCol = new GameObject("RightCol", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        rightCol.transform.SetParent(mainRow.transform, false);
        var rle = rightCol.GetComponent<LayoutElement>();
        rle.flexibleWidth = 1f;
        rle.flexibleHeight = 1f;
        var rvlg = rightCol.GetComponent<VerticalLayoutGroup>();
        rvlg.spacing = 10f;
        rvlg.childControlWidth = true;
        rvlg.childControlHeight = true;
        rvlg.childForceExpandHeight = false;
        rvlg.childForceExpandWidth = true;

        // —— 3a. 属性框（攻击/减伤/HP；快捷栏已移至下方详情框HotbarRow） ——
        var statBox = CreateBorderedBox(rightCol.transform, "StatBox");
        var sle = statBox.GetComponent<LayoutElement>();
        sle.preferredHeight = 90f;
        sle.flexibleWidth = 0f;
        sle.flexibleHeight = 0f;
        var shlg = statBox.AddComponent<HorizontalLayoutGroup>();
        shlg.padding = new RectOffset(10, 8, 6, 6);
        shlg.spacing = 10f;
        shlg.childControlWidth = true;
        shlg.childControlHeight = true;
        shlg.childForceExpandHeight = false;
        shlg.childForceExpandWidth = true;

        // 左半：属性文本 + HP行
        var statLeft = new GameObject("StatLeft", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        statLeft.transform.SetParent(statBox.transform, false);
        var slle = statLeft.GetComponent<LayoutElement>();
        slle.flexibleWidth = 1f;
        slle.flexibleHeight = 1f;
        var slvlg = statLeft.GetComponent<VerticalLayoutGroup>();
        slvlg.spacing = 4f;
        slvlg.childControlWidth = true;
        slvlg.childControlHeight = true;
        slvlg.childForceExpandHeight = false;
        slvlg.childForceExpandWidth = true;

        _statText = CreateText(statLeft.transform, "攻击 <b>10</b>\n减伤 <b>0</b>", 14, TextAnchor.UpperLeft, LineColor);
        var stle = _statText.GetComponent<LayoutElement>();
        stle.preferredHeight = 40f;
        stle.flexibleWidth = 0f;
        stle.flexibleHeight = 0f;

        // HP行（纯锚点布局：血条大黑块Bug的修复方案，勿改回嵌套LayoutGroup）
        var hpRow = new GameObject("HpRow", typeof(RectTransform), typeof(LayoutElement));
        hpRow.transform.SetParent(statLeft.transform, false);
        var hple = hpRow.GetComponent<LayoutElement>();
        hple.preferredHeight = 26f;
        hple.flexibleWidth = 0f;
        hple.flexibleHeight = 0f;

        var hpLabel = CreateText(hpRow.transform, "HP", 14, TextAnchor.MiddleLeft, LineColor);
        var lrt = hpLabel.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0.5f);
        lrt.anchorMax = new Vector2(0f, 0.5f);
        lrt.pivot = new Vector2(0f, 0.5f);
        lrt.sizeDelta = new Vector2(30f, 20f);
        lrt.anchoredPosition = Vector2.zero;

        var hpBar = CreateBorderedBox(hpRow.transform, "HpBar");
        var brt = hpBar.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 0f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.offsetMin = new Vector2(36f, 3f);
        brt.offsetMax = new Vector2(0f, -3f);

        _hpFill = new GameObject("HpFill", typeof(Image)).GetComponent<Image>();
        _hpFill.color = LineColor;
        var frt = _hpFill.rectTransform;
        frt.SetParent(hpBar.transform, false);
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(1f, 1f);
        frt.offsetMin = new Vector2(3f, 3f);
        frt.offsetMax = new Vector2(-3f, -3f);

        // —— 3b. 详情框 ——
        var detailBox = CreateBorderedBox(rightCol.transform, "DetailBox");
        var dle2 = detailBox.GetComponent<LayoutElement>();
        dle2.flexibleHeight = 1f;
        dle2.flexibleWidth = 0f;
        var dvlg = detailBox.AddComponent<VerticalLayoutGroup>();
        dvlg.padding = new RectOffset(10, 10, 6, 6);
        dvlg.spacing = 6f;
        dvlg.childControlWidth = true;
        dvlg.childControlHeight = true;
        dvlg.childForceExpandHeight = false;
        dvlg.childForceExpandWidth = true;

        _detailTitle = CreateText(detailBox.transform, "未选中物品", 15, TextAnchor.MiddleLeft, LineColor);
        _detailTitle.fontStyle = FontStyle.Bold;
        _detailTitle.GetComponent<LayoutElement>().preferredHeight = 24f;

        // —— 快捷栏行（规格组件3的背包侧入口：4格横排均分行宽，拖拽放置；数字键1~4使用） ——
        var hotbarRow = new GameObject("HotbarRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        hotbarRow.transform.SetParent(detailBox.transform, false);
        var hrle = hotbarRow.GetComponent<LayoutElement>();
        hrle.preferredHeight = 48f;
        hrle.flexibleWidth = 0f;
        hrle.flexibleHeight = 0f;
        var hhlg = hotbarRow.GetComponent<HorizontalLayoutGroup>();
        hhlg.spacing = 6f;
        hhlg.childControlWidth = true;
        hhlg.childControlHeight = true;
        hhlg.childForceExpandWidth = false;
        hhlg.childForceExpandHeight = false;

        for (int i = 0; i < 4; i++)
        {
            var cell = new GameObject("Hotbar_" + i, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
            cell.transform.SetParent(hotbarRow.transform, false);
            UIKit.EnsureLoaded();
            var hbImg = cell.GetComponent<Image>();
            hbImg.sprite = UIKit.Slot;
            hbImg.type = Image.Type.Sliced;
            hbImg.color = Color.white;
            var ol = cell.GetComponent<Outline>();
            ol.enabled = false;
            var cle = cell.GetComponent<LayoutElement>();
            cle.flexibleWidth = 1f;      // 四格均分行宽（约47px近方形）
            cle.preferredHeight = 48f;
            cle.flexibleHeight = 0f;
            var cellUI = cell.AddComponent<InventoryCellUI>();
            cellUI.Init(this, InventoryCellUI.CellKind.Hotbar, i);
            _hotbarCellUIs[i] = cellUI;

            // 左上角槽位序号（对应HUD数字键1~4）；raycastTarget必须关闭，否则挡住格子的拖拽/悬停事件
            var num = CreateText(cell.transform, (i + 1).ToString(), 12, TextAnchor.UpperLeft,
                new Color(LineColor.r, LineColor.g, LineColor.b, 0.55f));
            var nrt = num.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0f, 1f);
            nrt.pivot = new Vector2(0f, 1f);
            nrt.sizeDelta = new Vector2(16f, 15f);
            nrt.anchoredPosition = new Vector2(2f, -1f);
            num.GetComponent<LayoutElement>().ignoreLayout = true;
            num.raycastTarget = false;

            // 右下角数量角标（RefreshHotbarSlots填充）；同上关闭射线
            var count = CreateText(cell.transform, "", 12, TextAnchor.LowerRight, LineColor);
            var crt = count.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(1f, 0f);
            crt.sizeDelta = new Vector2(32f, 15f);
            crt.anchoredPosition = new Vector2(-1f, 1f);
            count.GetComponent<LayoutElement>().ignoreLayout = true;
            count.raycastTarget = false;
            _hotbarCounts[i] = count;
        }

        _detailDesc = CreateText(detailBox.transform, "", 14, TextAnchor.UpperLeft, LineColor);
        _detailDesc.GetComponent<LayoutElement>().flexibleHeight = 1f;

        // 操作反馈提示行（平时为空文本不占视觉；操作入口已移至右键菜单）
        _hintText = CreateText(detailBox.transform, "", 13, TextAnchor.MiddleLeft, HintColor);
        _hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        var hle = _hintText.GetComponent<LayoutElement>();
        hle.preferredHeight = 20f;
        hle.flexibleWidth = 0f;
        hle.flexibleHeight = 0f;
    }

private void BuildEquipSlot(Transform parent, string label, InventoryCellUI.CellKind kind)
    {
        var box = CreateBorderedBox(parent, label + "SlotBox");
        var ble = box.GetComponent<LayoutElement>();
        ble.flexibleWidth = 0f;
        ble.flexibleHeight = 0f;
        var vlg = box.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 6, 6);
        vlg.spacing = 4f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        var labelText = CreateText(box.transform, label, 16, TextAnchor.MiddleCenter, LineColor);
        labelText.fontStyle = FontStyle.Bold;
        labelText.GetComponent<LayoutElement>().preferredHeight = 24f;

        var dashed = new GameObject(label + "DashArea", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        dashed.transform.SetParent(box.transform, false);
        UIKit.EnsureLoaded();
        var dashImg = dashed.GetComponent<Image>();
        dashImg.sprite = UIKit.Slot;              // kit 槽位（替代虚线）
        dashImg.type = Image.Type.Sliced;
        dashImg.color = Color.white;
        dashImg.raycastTarget = false;
        var dle = dashed.GetComponent<LayoutElement>();
        dle.flexibleHeight = 1f;
        dle.minHeight = 40f;
        _dashedRects.Add(dashed.GetComponent<RectTransform>());

        var emptyText = CreateText(dashed.transform, "", 14, TextAnchor.MiddleCenter, LineColor);
        StretchRect(emptyText.rectTransform);
        emptyText.GetComponent<LayoutElement>().ignoreLayout = true;

        // 空槽白印：武器/护甲剪影（装备后隐藏，由 RefreshEquipSlots 控制）
        UIKit.EnsureLoaded();
        var silGo = new GameObject("Silhouette", typeof(RectTransform), typeof(Image));
        silGo.transform.SetParent(dashed.transform, false);
        var srt2 = silGo.GetComponent<RectTransform>();
        srt2.anchorMin = srt2.anchorMax = new Vector2(0.5f, 0.5f);
        srt2.pivot = new Vector2(0.5f, 0.5f);
        srt2.sizeDelta = new Vector2(40f, 40f);
        srt2.anchoredPosition = Vector2.zero;
        var silImg = silGo.GetComponent<Image>();
        silImg.sprite = UnityEngine.Resources.Load<UnityEngine.Sprite>(
            kind == InventoryCellUI.CellKind.EquipWeapon ? "UI/PixelKit/silhouette_weapon" : "UI/PixelKit/silhouette_armor");
        silImg.color = new Color(1f, 1f, 1f, 0.55f);
        silImg.raycastTarget = false;
        if (kind == InventoryCellUI.CellKind.EquipWeapon) _weaponSil = silImg;
        else _armorSil = silImg;

        var cellUI = box.AddComponent<InventoryCellUI>();
        cellUI.Init(this, kind, -1);
        if (kind == InventoryCellUI.CellKind.EquipWeapon)
        {
            _weaponSlotText = emptyText;
            _weaponCell = cellUI;
            _weaponDashRt = dashed.GetComponent<RectTransform>();
        }
        else
        {
            _armorSlotText = emptyText;
            _armorCell = cellUI;
            _armorDashRt = dashed.GetComponent<RectTransform>();
        }
    }

private RectTransform BuildAuthoringMenuShell()
    {
        _menuRoot = new GameObject("CellMenu", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(Outline));
        _menuRoot.transform.SetParent(_canvasRect, false);
        _menuRoot.GetComponent<Image>().color = PanelBg;
        var mol = _menuRoot.GetComponent<Outline>();
        mol.effectColor = LineColor;
        mol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var vlg = _menuRoot.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 4f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        return _menuRoot.GetComponent<RectTransform>();
    }

private void BuildCloseButton()
    {
        var btn = new GameObject("Btn_Close", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btn.transform.SetParent(_rootPanel.transform, false);
        UIKit.EnsureLoaded();
        var img = btn.GetComponent<Image>();
        img.sprite = UIKit.CircleBtn;
        img.type = Image.Type.Simple;
        img.color = Color.white;
        var rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-8f, -8f);
        rt.sizeDelta = new Vector2(34f, 34f);
        btn.GetComponent<LayoutElement>().ignoreLayout = true;

        // kit 关闭图标
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(btn.transform, false);
        var irt = iconGo.GetComponent<RectTransform>();
        irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
        irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = Vector2.zero;
        irt.sizeDelta = new Vector2(16f, 16f);
        var iconImg = iconGo.GetComponent<Image>();
        iconImg.sprite = UIKit.IconClose;
        iconImg.raycastTarget = false;
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

private void BuildAuthoringSplitPopup()
    {
        _splitRoot = new GameObject("SplitPopup", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        _splitRoot.transform.SetParent(_rootPanel.transform, false);
        _splitRoot.GetComponent<Image>().color = PanelBg;
        var ol = _splitRoot.GetComponent<Outline>();
        ol.effectColor = LineColor;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var rt = _splitRoot.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(280f, 170f);
        _splitRoot.GetComponent<LayoutElement>().ignoreLayout = true;
        var vlg = _splitRoot.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 10, 10);
        vlg.spacing = 10f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        _splitTitleText = CreateText(_splitRoot.transform, "拆分", 15, TextAnchor.MiddleLeft, LineColor);
        _splitTitleText.fontStyle = FontStyle.Bold;
        _splitTitleText.GetComponent<LayoutElement>().preferredHeight = 22f;

        _splitQtyText = CreateText(_splitRoot.transform, "拆分数量: 1", 14, TextAnchor.MiddleCenter, LineColor);
        _splitQtyText.GetComponent<LayoutElement>().preferredHeight = 22f;

        // 滑块行：[－] [slider] [＋]
        var row = new GameObject("SliderRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(_splitRoot.transform, false);
        var rle = row.GetComponent<LayoutElement>();
        rle.preferredHeight = 30f;
        rle.flexibleWidth = 0f;
        var rhlg = row.GetComponent<HorizontalLayoutGroup>();
        rhlg.spacing = 8f;
        rhlg.childControlWidth = true;
        rhlg.childControlHeight = true;
        rhlg.childForceExpandWidth = false;
        rhlg.childForceExpandHeight = true;

        CreateActionButton(row.transform, "－", () => StepSplit(-1)).GetComponent<LayoutElement>().preferredWidth = 34f;
        _splitSlider = BuildSlider(row.transform);
        var sle = _splitSlider.GetComponent<LayoutElement>();
        sle.flexibleWidth = 1f;
        sle.preferredHeight = 22f;
        CreateActionButton(row.transform, "＋", () => StepSplit(1)).GetComponent<LayoutElement>().preferredWidth = 34f;
        _splitSlider.onValueChanged.AddListener(_ => RefreshSplitQty());

        // 取消/确定行
        var btnRow = new GameObject("BtnRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        btnRow.transform.SetParent(_splitRoot.transform, false);
        var ble = btnRow.GetComponent<LayoutElement>();
        ble.preferredHeight = 30f;
        ble.flexibleWidth = 0f;
        var bhlg = btnRow.GetComponent<HorizontalLayoutGroup>();
        bhlg.spacing = 10f;
        bhlg.childControlWidth = true;
        bhlg.childControlHeight = true;
        bhlg.childForceExpandWidth = true;
        bhlg.childForceExpandHeight = true;
        CreateActionButton(btnRow.transform, "取消", HideSplitPopup);
        CreateActionButton(btnRow.transform, "确定", OnSplitConfirm);

        _splitRoot.SetActive(false);
    }

private Slider BuildSlider(Transform parent)
    {
        var go = new GameObject("QtySlider", typeof(RectTransform), typeof(Image), typeof(Slider), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.8f, 0.8f, 0.8f, 1f);
        var slider = go.GetComponent<Slider>();

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.25f);
        faRt.anchorMax = new Vector2(1f, 0.75f);
        faRt.offsetMin = Vector2.zero; // 默认sizeDelta=(100,100)会使Fill溢出轨道形成黑块
        faRt.offsetMax = Vector2.zero;
        var fill = new GameObject("Fill", typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        var fImg = fill.GetComponent<Image>();
        fImg.color = LineColor;
        fImg.raycastTarget = false;
        StretchRect(fill.GetComponent<RectTransform>());
        slider.fillRect = fill.GetComponent<RectTransform>();

        var handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var haRt = handleArea.GetComponent<RectTransform>();
        haRt.anchorMin = new Vector2(0f, 0f);
        haRt.anchorMax = new Vector2(1f, 1f);
        haRt.offsetMin = new Vector2(8f, 0f);
        haRt.offsetMax = new Vector2(-8f, 0f);
        var handle = new GameObject("Handle", typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var hImg = handle.GetComponent<Image>();
        hImg.color = Color.white;
        var hOl = handle.AddComponent<Outline>();
        hOl.effectColor = LineColor;
        hOl.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var hRt = handle.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(16f, 0f);
        hRt.anchorMin = new Vector2(0f, 0f);
        hRt.anchorMax = new Vector2(0f, 1f);
        slider.handleRect = hRt;
        slider.targetGraphic = hImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 1f;
        slider.wholeNumbers = true;
        return slider;
    }

private GameObject CreateBorderedBox(Transform parent, string name)
    {
        UIKit.EnsureLoaded();
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = UIKit.PanelInner;      // kit 内嵌框
        img.type = Image.Type.Sliced;
        img.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.enabled = false;                  // kit 框自带边框
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
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = size + 6f;
        return t;
    }

private Button CreateActionButton(Transform parent, string label, UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = PanelBg;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = LineColor;
        outline.effectDistance = new Vector2(BorderWidth, BorderWidth);
        go.GetComponent<LayoutElement>().preferredHeight = 30f;

        var t = CreateText(go.transform, label, 14, TextAnchor.MiddleCenter, LineColor);
        StretchRect(t.rectTransform);
        t.GetComponent<LayoutElement>().ignoreLayout = true;

        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick"); // UI点击音效
            onClick.Invoke();
        });
        return go.GetComponent<Button>();
    }

public static GameObject BuildAuthoringIconGroup(Inventory.ItemStack stack, Vector2 size, ItemOrient orient, bool ghost, bool withName = true)
    {
        var item = stack.item;
        var baseColor = TypeColor(item.type);
        var root = new GameObject("IconGroup", typeof(RectTransform));
        var rr = root.GetComponent<RectTransform>();
        rr.sizeDelta = size;
        var cg = root.AddComponent<CanvasGroup>();

        // a. 类型色浅底剪影（铺满整个占位区域，让"这几格是一个整体"一眼可辨）
        var sil = new GameObject("Silhouette", typeof(Image));
        sil.transform.SetParent(root.transform, false);
        var simg = sil.GetComponent<Image>();
        simg.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.25f);
        simg.raycastTarget = false;
        StretchRect(simg.GetComponent<RectTransform>());

        // b. 中央图标：icon字段非空优先Sprite（保持宽高比），否则类型单字深色版；竖放旋转90°
        //    （Image与Text互斥创建：一个GameObject只能挂一个Graphic）
        var center = new GameObject("Icon", typeof(RectTransform));
        center.transform.SetParent(root.transform, false);
        var crt = center.GetComponent<RectTransform>();
        float iconSize = Mathf.Min(size.x, size.y) * 0.55f;
        crt.sizeDelta = new Vector2(iconSize, iconSize);
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        if (item.icon != null)
        {
            var img = center.AddComponent<Image>();
            img.sprite = item.icon;
            img.preserveAspect = true; // Sprite保持宽高比
            img.raycastTarget = false;
        }
        else
        {
            var ct = center.AddComponent<Text>();
            ct.font = UIKit.LegacyFont;
            ct.fontSize = Mathf.Clamp(Mathf.RoundToInt(iconSize * 0.8f), 18, 44);
            ct.alignment = TextAnchor.MiddleCenter;
            ct.color = new Color(baseColor.r * 0.55f, baseColor.g * 0.55f, baseColor.b * 0.55f, 1f); // 类型色深色版
            ct.text = TypeIconChar(item.type);
            ct.raycastTarget = false;
        }
        if (orient == ItemOrient.Vertical)
        {
            crt.localRotation = Quaternion.Euler(0f, 0f, -90f); // 竖放：图标旋转90°保证方向可读
        }

        // d. 名称小字（底部，不随方向旋转）
        if (withName)
        {
            var nameGo = new GameObject("Name", typeof(Text));
            nameGo.transform.SetParent(root.transform, false);
            var nt = nameGo.GetComponent<Text>();
            nt.font = UIKit.LegacyFont;
            nt.fontSize = 11;
            nt.alignment = TextAnchor.MiddleCenter;
            nt.color = LineColor;
            nt.text = item.itemName;
            nt.raycastTarget = false;
            var nrt = nt.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0f);
            nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot = new Vector2(0.5f, 0f);
            nrt.sizeDelta = new Vector2(-6f, 15f);
            nrt.anchoredPosition = new Vector2(0f, 1f);
        }

        // d2. 数量角标（右下角，仅count>1显示；不随方向旋转）
        if (stack.count > 1)
        {
            var badge = new GameObject("Count", typeof(Text));
            badge.transform.SetParent(root.transform, false);
            var bt = badge.GetComponent<Text>();
            bt.font = UIKit.LegacyFont;
            bt.fontSize = 12;
            bt.fontStyle = FontStyle.Bold;
            bt.alignment = TextAnchor.MiddleRight;
            bt.color = LineColor;
            bt.text = "x" + stack.count;
            bt.raycastTarget = false;
            var brt = bt.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(1f, 0f);
            brt.sizeDelta = new Vector2(34f, 15f);
            brt.anchoredPosition = new Vector2(-2f, 1f);
        }

        // f. 全部关闭射线检测 + Ghost整体半透明
        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
        {
            g.raycastTarget = false;
        }
        if (ghost) cg.alpha = 0.55f;
        return root;
    }
    public GameObject CreateAuthoringPanel(Transform canvasRoot)
    {
        _inventory = GetComponent<Inventory>(); _canvasRect = (RectTransform)canvasRoot;
        BuildAuthoringPanel(canvasRoot);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_rootPanel.transform);
        var view = _rootPanel.AddComponent<InventoryPanelView>();
        view.grid = _gridRoot.GetComponent<GridLayoutGroup>();
        view.slotTemplate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Widgets/InventorySlot.prefab");
        view.previewCells = _cells.ToArray();
        view.weaponCell = _weaponCell; view.armorCell = _armorCell;
        view.bagCell = System.Array.Find(_rootPanel.GetComponentsInChildren<InventoryCellUI>(true), c => c.Kind == InventoryCellUI.CellKind.EquipBag);
        view.weaponText = _weaponSlotText; view.armorText = _armorSlotText;
        view.weaponSilhouette = _weaponSil; view.armorSilhouette = _armorSil; view.bagIcon = _bagSil;
        view.weaponIconRoot = _weaponDashRt; view.armorIconRoot = _armorDashRt;
        view.stats = _statText; view.hpFill = _hpFill; view.detailTitle = _detailTitle;
        view.detailDescription = _detailDesc; view.capacityText = _slotCountText; view.hint = _hintText;
        view.hotbarCells = _hotbarCellUIs; view.hotbarCounts = _hotbarCounts;
        view.closeButton = _rootPanel.transform.Find("Btn_Close").GetComponent<Button>();
        BuildAuthoringSplitPopup();
        view.splitRoot = _splitRoot; view.splitSlider = _splitSlider;
        view.splitTitle = _splitTitleText; view.splitQuantity = _splitQtyText;
        var buttons = _splitRoot.GetComponentsInChildren<Button>(true);
        view.splitMinus=buttons[0]; view.splitPlus=buttons[1]; view.splitCancel=buttons[2]; view.splitConfirm=buttons[3];
        BuildAuthoringMenuShell();
        _menuRoot.transform.SetParent(_rootPanel.transform,false);
        _menuRoot.AddComponent<LayoutElement>().ignoreLayout = true;
        var menuSize = _menuRoot.AddComponent<ContentSizeFitter>();
        menuSize.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ((RectTransform)_menuRoot.transform).sizeDelta = new Vector2(150,100);
        view.menuTemplate = _menuRoot;
        var button = CreateActionButton(_rootPanel.transform,"操作",()=>{});
        button.name="MenuButtonTemplate";
        button.GetComponent<LayoutElement>().ignoreLayout=true;
        view.menuButtonTemplate=button; button.gameObject.SetActive(false);
        _menuRoot.SetActive(false);
        // 运行时事件仅由控制器绑定，Prefab 中不保存临时构建器监听。
        foreach(var b in _rootPanel.GetComponentsInChildren<Button>(true)) b.onClick.RemoveAllListeners();
        _splitSlider.onValueChanged.RemoveAllListeners();
        _detailDesc.text="物品详情与操作提示";
        _slotCountText.text="格子 0/10";
        _rootPanel.SetActive(true);
        return _rootPanel;
    }
}
#endif
