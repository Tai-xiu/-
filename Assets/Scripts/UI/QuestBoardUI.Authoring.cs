#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEditor;
public partial class QuestBoardUI
{
    private void BuildAuthoringPanel(Transform canvasRoot)
    {
        // 孤儿清扫：上一场景实例的面板（组件已死但DDOL画布上的节点还在）按名销毁
        var old = (Transform)null;
        if (old != null) Destroy(old.gameObject);

        // 面板底板（白盒底色+黑色实线边框，ShopUI同款480×430）
        _panel = new GameObject("QuestBoardPanel", typeof(Image), typeof(VerticalLayoutGroup));
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

        // ── 1. 顶部标题栏：左「冒险者协会」中警告 右「金币 n」 ──
        var titleBar = CreateBorderedBox(_panel.transform, "TitleBar", 44f);
        var titleLE = titleBar.GetComponent<LayoutElement>();
        titleLE.flexibleWidth = 0f;   // 显式归零：标题栏内HLG forceExpand会向父级上报flexible=1，
        titleLE.flexibleHeight = 0f;  // 抢占列表区高度（uGUI嵌套布局实测踩坑）
        var tilg = titleBar.AddComponent<HorizontalLayoutGroup>();
        tilg.padding = new RectOffset(10, 40, 6, 6); // 右侧留40给右上角退出按钮
        tilg.childControlWidth = true;
        tilg.childControlHeight = true;
        tilg.childForceExpandWidth = true;
        tilg.childForceExpandHeight = true;
        var title = CreateText(titleBar.transform, "冒险者协会", 16, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        title.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _warningText = CreateText(titleBar.transform, "", 12, TextAnchor.MiddleCenter, WarnColor);
        _warningText.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _coinTitleText = CreateText(titleBar.transform, "金币 0", 14, TextAnchor.MiddleRight);
        _coinTitleText.GetComponent<LayoutElement>().flexibleWidth = 1f;

        // ── 2. 契约列表区：全部契约卡各一行（占满剩余高度） ──
        var list = new GameObject("QuestList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        list.transform.SetParent(_panel.transform, false);
        var lle = list.GetComponent<LayoutElement>();
        lle.flexibleWidth = 0f;
        lle.flexibleHeight = 1f;
        var vlg = list.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.spacing = 8f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;  // 布局接管行高（否则行保持新建默认100高，preferredHeight=82被忽略）
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        _list = list.GetComponent<RectTransform>();

        BuildCloseButton(); // 右上角退出按钮（红圆底白叉，ShopUI同款）
    }

    private GameObject CreateAuthoringQuestRow(RectTransform parent, QuestData card)
    {
        var row = CreateBorderedBox(parent, "QuestRow_" + card.questId, RowHeight);
        var le = row.GetComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 8, 8);
        hlg.spacing = 8f;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        var state = QuestState.NotAccepted;

        // 左列：名称(粗)+描述（富文本两行，吸收剩余宽度）
        var mainT = CreateText(row.transform,
            "<b>" + card.questName + "</b>\n<size=11>" + card.desc + "</size>",
            13, TextAnchor.UpperLeft, LineColor);
        mainT.horizontalOverflow = HorizontalWrapMode.Wrap; // 描述过长换行不溢出行
        mainT.verticalOverflow = VerticalWrapMode.Truncate;
        var mle = mainT.GetComponent<LayoutElement>();
        mle.preferredWidth = 150f;
        mle.flexibleWidth = 1f;

        // 中列：状态/进度（可领奖金色高亮）
        string statusText;
        Color statusColor = LineColor;
        switch (state)
        {
            case QuestState.Active:
                statusText = "进度 " + 0 + "/" + card.targetCount;
                break;
            case QuestState.Completed:
                statusText = "可领奖";
                statusColor = GoldColor;
                break;
            case QuestState.Claimed:
                statusText = card.isDaily ? "日契·可刷新" : "已领奖";
                break;
            default:
                statusText = "未接取";
                break;
        }
        var statusT = CreateText(row.transform, statusText, 12, TextAnchor.MiddleLeft, statusColor);
        statusT.fontStyle = state == QuestState.Completed ? FontStyle.Bold : FontStyle.Normal;
        statusT.GetComponent<LayoutElement>().preferredWidth = 84f;

        // 右列：奖励 + 动作按钮（按状态一卡一钮）
        var rewardT = CreateText(row.transform, card.rewardGold + "金币", 12, TextAnchor.MiddleRight, LineColor);
        rewardT.GetComponent<LayoutElement>().preferredWidth = 64f;

        switch (state)
        {
            case QuestState.NotAccepted:
                CreateBorderedButton(row.transform, "接取", new Vector2(64f, 30f), () => OnAcceptClicked(card));
                break;
            case QuestState.Active:
            case QuestState.Completed: // 已完成未领也允许放弃（拒领重接）
                CreateBorderedButton(row.transform, state == QuestState.Active ? "放弃" : "领奖",
                    new Vector2(64f, 30f),
                    state == QuestState.Active ? (UnityAction)(() => OnAbandonClicked(card))
                                               : (UnityAction)(() => OnClaimClicked(card)));
                break;
            case QuestState.Claimed when card.isDaily: // 日契：花金币手动刷新重接（2026-10-21拍板）
                CreateBorderedButton(row.transform, "刷新10金", new Vector2(76f, 30f), () => OnDailyRefreshClicked(card));
                break;
            default: // 已领奖（非日契）：无动作
                var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
                spacer.transform.SetParent(row.transform, false);
                spacer.GetComponent<LayoutElement>().preferredWidth = 64f;
                spacer.GetComponent<LayoutElement>().preferredHeight = 30f;
                break;
        }
        return row;
    }

    private void CreateBorderedButton(Transform parent, string label, Vector2 size, UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement), typeof(Button));
        go.transform.SetParent(parent, false);
        UIKit.EnsureLoaded();
        var bimg = go.GetComponent<Image>();
        bimg.sprite = UIKit.BtnN;
        bimg.type = Image.Type.Sliced;
        bimg.color = Color.white;
        var ol = go.GetComponent<Outline>();
        ol.effectColor = LineColor;
        ol.effectDistance = new Vector2(BorderWidth, BorderWidth);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = size.x;
        le.preferredHeight = size.y;
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick");
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
        ol.effectColor = LineColor;
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

    public GameObject CreateEditablePrefab(Transform canvas)
    {
        BuildAuthoringPanel(canvas);var v=_panel.AddComponent<UINodeReferences>();
        v.Add("Coins",_coinTitleText);v.Add("Warning",_warningText);v.Add("List",_list);v.Add("Close",_panel.transform.Find("Btn_Close").GetComponent<Button>());
        var card=ScriptableObject.CreateInstance<QuestData>();card.questId="preview";card.questName="示例契约";card.desc="契约描述";card.targetCount=3;card.rewardGold=20;
        var row=CreateAuthoringQuestRow(_list,card);var r=row.AddComponent<UINodeReferences>();r.selectedText=GoldColor;var texts=row.GetComponentsInChildren<Text>();
        r.Add("Name",texts[0]);r.Add("Status",texts[1]);r.Add("Reward",texts[2]);r.Add("ActionText",texts[3]);row.GetComponentInChildren<Button>().gameObject.AddComponent<CanvasGroup>();r.Add("Action",row.GetComponentInChildren<Button>());v.Add("Row",r);
        var empty=CreateBorderedBox(_list,"EmptyTemplate",RowHeight);var t=CreateText(empty.transform,"（暂无悬赏契约）",13,TextAnchor.MiddleCenter);StretchRect(t.rectTransform);t.GetComponent<LayoutElement>().ignoreLayout=true;
        var e=empty.AddComponent<UINodeReferences>();e.Add("Text",t);v.Add("Empty",e);empty.SetActive(false);
        foreach(var b in _panel.GetComponentsInChildren<Button>(true))b.onClick.RemoveAllListeners();Object.DestroyImmediate(card);return _panel;
    }

}
#endif
