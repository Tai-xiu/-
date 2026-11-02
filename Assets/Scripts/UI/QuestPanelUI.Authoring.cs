#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
public partial class QuestPanelUI
{
    private bool BuildAuthoringPanel(Transform canvasRoot)
    {
        if (canvasRoot == null) return false;

        // 孤儿清扫：重复实例（域重载残留等）的旧面板按名销毁
        var old=(Transform)null;
        if (old != null) Destroy(old.gameObject);

        // 根面板：白盒底色+黑色实线边框，左侧镜像定位
        _panel = new GameObject("QuestPanel", typeof(Image), typeof(VerticalLayoutGroup));
        UIKit.EnsureLoaded();
        var panelImg = _panel.GetComponent<Image>();
        panelImg.sprite = UIKit.Panel;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = Color.white;
        var prt = _panel.GetComponent<RectTransform>();
        prt.SetParent(canvasRoot, false);
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = new Vector2(OffsetX, 0f);
        prt.sizeDelta = new Vector2(PanelW, PanelH);
        var pOutline = _panel.AddComponent<Outline>();
        pOutline.enabled = false;
        pOutline.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);

        var vlg = _panel.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.spacing = 10f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        // ── 1. 标题栏（规格同背包/搜索栏标题栏） ──
        var titleBar = CreateBorderedBox("TitleBar");
        var tle = titleBar.GetComponent<LayoutElement>();
        tle.preferredHeight = 40f;
        tle.flexibleWidth = 0f;   // 显式归零：防HLG forceExpand向父级上报flexible=1抢占高度（uGUI嵌套布局老坑）
        tle.flexibleHeight = 0f;
        var titleText = CreateText(titleBar.transform, "任 务", 18, TextAnchor.MiddleCenter);
        titleText.fontStyle = FontStyle.Bold;
        StretchRect(titleText.rectTransform);
        titleText.GetComponent<LayoutElement>().ignoreLayout = true;

        // ── 2. 契约列表区（占满剩余高度；超出滚动，ShopUI出售页同款ScrollRect） ──
        var scrollGo = new GameObject("QuestList", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(_panel.transform, false);
        var sle = scrollGo.GetComponent<LayoutElement>();
        sle.flexibleHeight = 1f;
        sle.flexibleWidth = 0f;
        scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // 透明底仅承接滚轮/拖拽射线
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 8f;

        // 视口：RectMask2D软裁剪超出显示区的条目
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        StretchRect(viewport.GetComponent<RectTransform>());

        // 内容：条目容器，高度随条目数与展开数由ContentSizeFitter驱动
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.offsetMin = new Vector2(0f, 0f);
        crt.offsetMax = new Vector2(-18f, 0f); // 右侧留18给滑动条
        var cvlg = content.GetComponent<VerticalLayoutGroup>();
        cvlg.spacing = 6f;
        cvlg.childControlWidth = true;
        cvlg.childControlHeight = true;  // 布局接管条目高度（折叠/展开高度自适应的关键）
        cvlg.childForceExpandHeight = false;
        cvlg.childForceExpandWidth = true;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = crt;
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.verticalScrollbar = BuildScrollbar(scrollGo.transform);
        _content = crt;

        // ── 3. 底部提示框（固定引导文案） ──
        var hintBox = CreateBorderedBox("HintBox");
        var hle = hintBox.GetComponent<LayoutElement>();
        hle.preferredHeight = 90f;
        hle.flexibleWidth = 0f;
        hle.flexibleHeight = 0f;
        var hvlg = hintBox.AddComponent<VerticalLayoutGroup>();
        hvlg.padding = new RectOffset(10, 10, 8, 8);
        hvlg.childControlWidth = true;
        hvlg.childControlHeight = true;
        hvlg.childForceExpandHeight = false;
        hvlg.childForceExpandWidth = true;
        var hintText = CreateText(hintBox.transform, "接取与领奖请前往\n冒险者协会", 12, TextAnchor.UpperLeft);
        hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
        hintText.GetComponent<LayoutElement>().flexibleHeight = 1f;

        return true;
    }

    private Scrollbar BuildScrollbar(Transform parent)
    {
        var go = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        go.transform.SetParent(parent, false);
        UIKit.EnsureLoaded();
        var trackImg = go.GetComponent<Image>();
        trackImg.sprite = UIKit.BarFrame;
        trackImg.type = Image.Type.Sliced;
        trackImg.color = Color.white;
        var ol = go.AddComponent<Outline>();
        ol.effectColor = LineColor;
        ol.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);
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
        hrt.sizeDelta = new Vector2(-8f, 0f);

        var sb = go.GetComponent<Scrollbar>();
        sb.handleRect = hrt;
        sb.targetGraphic = handle.GetComponent<Image>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        sb.value = 1f;
        return sb;
    }

    private GameObject CreateAuthoringQuestEntry(QuestData card)
    {
        var state = QuestState.Active;
        bool claimable = state == QuestState.Completed;
        Color nameColor = claimable ? GoldColor : LineColor;

        // 条目根（无固定高度：VerticalLayoutGroup作为ILayoutElement向父级上报自适应首选高度）
        var entry = new GameObject("QuestEntry_" + card.questId, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup));
        entry.transform.SetParent(_content, false);
        var entryImg = entry.GetComponent<Image>();
        entryImg.sprite = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/PixelKit/panel_inner");
        entryImg.type = Image.Type.Sliced;
        entryImg.color = Color.white;
        var ol = entry.GetComponent<Outline>();
        ol.effectColor = claimable ? GoldColor : LineColor; // 可领奖整框金色
        ol.effectDistance = new Vector2(InventoryUI.BorderWidth, InventoryUI.BorderWidth);
        var evlg = entry.GetComponent<VerticalLayoutGroup>();
        evlg.padding = new RectOffset(8, 8, 6, 6);
        evlg.spacing = 4f;
        evlg.childControlWidth = true;
        evlg.childControlHeight = true;
        evlg.childForceExpandHeight = false;
        evlg.childForceExpandWidth = true;

        // —— 表头（可点击折叠开关） ——
        var header = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        header.transform.SetParent(entry.transform, false);
        var headImg = header.GetComponent<Image>();
        headImg.sprite = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/PixelKit/panel_inner");
        headImg.type = Image.Type.Sliced;
        headImg.color = Color.white;
        var hle = header.GetComponent<LayoutElement>();
        hle.preferredHeight = 26f;
        hle.flexibleWidth = 1f;
        hle.flexibleHeight = 0f;
        var hhlg = header.GetComponent<HorizontalLayoutGroup>();
        hhlg.spacing = 6f;
        hhlg.childControlWidth = true;
        hhlg.childControlHeight = true;
        hhlg.childForceExpandWidth = false;
        hhlg.childForceExpandHeight = false;

        var nameT = CreateText(header.transform, card.questName, 13, TextAnchor.MiddleLeft, nameColor);
        nameT.fontStyle = FontStyle.Bold;
        nameT.GetComponent<LayoutElement>().flexibleWidth = 1f;

        string compact = claimable ? "可领奖" : 0 + "/" + card.targetCount;
        var compactT = CreateText(header.transform, compact, 12, TextAnchor.MiddleRight, claimable ? GoldColor : LineColor);
        compactT.GetComponent<LayoutElement>().preferredWidth = 56f;

        bool expanded;
        _expanded.TryGetValue(card.questId, out expanded);
        var markT = CreateText(header.transform, expanded ? "−" : "+", 13, TextAnchor.MiddleRight, LineColor);
        markT.GetComponent<LayoutElement>().preferredWidth = 16f;

        // —— 详情组（SetActive折叠/展开；高度由布局自适应，无需手算） ——
        var info = new GameObject("InfoGroup", typeof(RectTransform), typeof(VerticalLayoutGroup));
        info.transform.SetParent(entry.transform, false);
        var ivlg = info.GetComponent<VerticalLayoutGroup>();
        ivlg.padding = new RectOffset(12, 0, 0, 0); // 详情相对表头缩进
        ivlg.spacing = 3f;
        ivlg.childControlWidth = true;
        ivlg.childControlHeight = true;
        ivlg.childForceExpandHeight = false;
        ivlg.childForceExpandWidth = true;

        var goalT = CreateText(info.transform, GoalText(card), 12, TextAnchor.UpperLeft);
        var progT = CreateText(info.transform, "进度 0/3", 12, TextAnchor.UpperLeft);
        var rewardT = CreateText(info.transform, "奖励：" + card.rewardGold + "金币" + (card.rewardItem != null ? "+" + card.rewardItem.itemName : ""), 12, TextAnchor.UpperLeft);
        var statusT = CreateText(info.transform, claimable ? "可领奖（前往协会）" : "进行中", 12, TextAnchor.UpperLeft, claimable ? GoldColor : LineColor);
        statusT.fontStyle = claimable ? FontStyle.Bold : FontStyle.Normal;
        var descT = CreateText(info.transform, card.desc, 11, TextAnchor.UpperLeft);
        descT.horizontalOverflow = HorizontalWrapMode.Wrap;
        info.SetActive(expanded); // 折叠状态按记忆恢复

        // 点击表头=切换展开（多条互不影响；状态按questId记忆跨刷新保持）
        var questId = card.questId;
        header.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick");
            bool now;
            _expanded.TryGetValue(questId, out now);
            now = !now;
            _expanded[questId] = now;
            info.SetActive(now);
            markT.text = now ? "−" : "+";
        });

        return entry;
    }

    private GameObject CreateBorderedBox(string boxName)
    {
        var go = new GameObject(boxName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
        go.transform.SetParent(_panel.transform, false);
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
        t.raycastTarget = false;
        go.AddComponent<LayoutElement>();
        return t;
    }

    private Text CreateText(Transform parent, string label, int size, TextAnchor anchor, Color color)
    {
        var t = CreateText(parent, label, size, anchor);
        t.color = color;
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
        BuildAuthoringPanel(canvas);var v=_panel.AddComponent<UINodeReferences>();v.Add("Content",_content);
        var card=ScriptableObject.CreateInstance<QuestData>();card.questId="preview";card.questName="示例契约";card.desc="点击表头可展开目标、进度与奖励";card.targetCount=3;card.rewardGold=20;
        var row=CreateAuthoringQuestEntry(card);var r=row.AddComponent<UINodeReferences>();r.selectedText=GoldColor;var texts=row.GetComponentsInChildren<Text>(true);
        string[] keys={"Name","Compact","Mark","Goal","Progress","Reward","Status","Description"};for(int i=0;i<keys.Length;i++)r.Add(keys[i],texts[i]);
        r.Add("Header",row.GetComponentInChildren<Button>(true));r.Add("Info",row.transform.Find("InfoGroup").gameObject);v.Add("Row",r);r.Get<GameObject>("Info").SetActive(true);r.Get<Text>("Mark").text="−";
        var empty=CreateBorderedBox("EmptyTemplate");empty.transform.SetParent(_content,false);empty.GetComponent<LayoutElement>().preferredHeight=64;
        var t=CreateText(empty.transform,"暂无契约\n契约列表见冒险者协会悬赏板。",12,TextAnchor.MiddleCenter);StretchRect(t.rectTransform);t.GetComponent<LayoutElement>().ignoreLayout=true;t.horizontalOverflow=HorizontalWrapMode.Wrap;
        var e=empty.AddComponent<UINodeReferences>();e.Add("Text",t);v.Add("Empty",e);empty.SetActive(false);
        foreach(var b in _panel.GetComponentsInChildren<Button>(true))b.onClick.RemoveAllListeners();Object.DestroyImmediate(card);return _panel;
    }

}
#endif
