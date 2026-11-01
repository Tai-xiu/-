// 批次2 静态面板 Prefab（暂停 / 阵亡）。1280×720 基准。
// 菜单：Tools/UI/构建 暂停与阵亡面板 Prefab
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class UIPanelPrefabBuilder
{
    const string DIR = "Assets/Prefabs/UI/";
    const string PAUSE = DIR + "PausePanel.prefab";
    const string DEATH = DIR + "DeathPanel.prefab";
    const string VICTORY = DIR + "VictoryPanel.prefab";

    [MenuItem("Tools/UI/构建 暂停与阵亡面板 Prefab")]
    public static void Build()
    {
        UIKit.EnsureLoaded();
        System.IO.Directory.CreateDirectory(DIR);
        AssetDatabase.Refresh();

        BuildPause();
        BuildDeath();
        BuildVictory();
        Debug.Log("[UIPanel] 生成 " + PAUSE + " / " + DEATH + " / " + VICTORY + "；" + SetupScene());
    }

    // ---------- 探索成功面板 520×420 ----------
    static void BuildVictory()
    {
        var panel = NewPanel("VictoryPanel", new Vector2(520f, 420f), 16, 8);
        AddTitle(panel.transform, "探索成功！", 30, new Color(1f, 0.9f, 0.3f, 1f), 44f);
        AddTitle(panel.transform, "本次带回的战利品：", 12, Color.white, 26f);

        // 战利品清单：运行时代码填 text（flexibleHeight=1 撑满剩余空间）
        var listGo = new GameObject("LootList", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        listGo.transform.SetParent(panel.transform, false);
        var lt = listGo.GetComponent<Text>();
        lt.text = "";
        lt.font = UIKit.LegacyFont;
        lt.fontSize = 17;
        lt.alignment = TextAnchor.UpperLeft;
        lt.color = Color.white;
        lt.horizontalOverflow = HorizontalWrapMode.Overflow;
        lt.supportRichText = true;
        listGo.GetComponent<LayoutElement>().flexibleHeight = 1f;

        AddTitle(panel.transform, "已返回营地，可前往商店出售战利品换取金币", 12,
            new Color(1f, 1f, 1f, 0.85f), 24f);
        AddButton(panel.transform, "Btn_Continue", "继 续", UIKit.BtnN, Color.white);
        panel.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(panel, VICTORY);
        Object.DestroyImmediate(panel);
    }

    // ---------- 暂停面板 460×340 ----------
    static void BuildPause()
    {
        var panel = NewPanel("PausePanel", new Vector2(460f, 248f), 16, 14);
        AddTitle(panel.transform, "游戏已暂停", 24, Color.white, 48f);
        AddButton(panel.transform, "Btn_Continue", "继 续", UIKit.BtnN, Color.white);
        AddButton(panel.transform, "Btn_MainMenu", "回 主 菜 单", UIKit.BtnP, new Color(1f, 0.82f, 0.78f, 1f));
        panel.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(panel, PAUSE);
        Object.DestroyImmediate(panel);
    }

    // ---------- 阵亡面板 520×360 ----------
    static void BuildDeath()
    {
        var panel = NewPanel("DeathPanel", new Vector2(520f, 250f), 16, 12);
        AddTitle(panel.transform, "你 已 阵 亡", 24, new Color(0.9f, 0.25f, 0.22f, 1f), 52f);
        AddTitle(panel.transform, "装备与物品栏已被清空（金币保留）", 16,
            new Color(1f, 1f, 1f, 0.8f), 26f);
        AddButton(panel.transform, "Btn_ReturnCamp", "返 回 营 地", UIKit.BtnP, new Color(1f, 0.76f, 0.76f, 1f));
        panel.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(panel, DEATH);
        Object.DestroyImmediate(panel);
    }

    // ---------- 共用部件 ----------
    static GameObject NewPanel(string name, Vector2 size, int pad, int spacing)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        img.sprite = UIKit.Panel;            // panel_frame 九宫格
        img.type = Image.Type.Sliced;
        img.color = Color.white;
        img.raycastTarget = true;

        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(pad, pad, pad, pad);
        vlg.spacing = spacing;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childAlignment = TextAnchor.UpperCenter;
        return go;
    }

    static void AddTitle(Transform parent, string text, int size, Color color, float minHeight)
    {
        var go = new GameObject("Title", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
        t.font = UIKit.FontCN;
        t.text = text;
        t.fontSize = size;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.color = color;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.overflowMode = TMPro.TextOverflowModes.Overflow;
        go.GetComponent<LayoutElement>().minHeight = minHeight;
    }

    static void AddButton(Transform parent, string name, string label, Sprite sprite, Color tint)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.color = tint;
        go.GetComponent<LayoutElement>().preferredHeight = 48f;
        go.GetComponent<LayoutElement>().minHeight = 48f;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.SpriteSwap;
        var st = new SpriteState();
        st.highlightedSprite = UIKit.BtnH;
        st.selectedSprite = UIKit.BtnH;
        st.pressedSprite = UIKit.BtnP;
        st.disabledSprite = UIKit.BtnD;
        btn.spriteState = st;

        var txtGo = new GameObject("Label", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        var t = txtGo.AddComponent<TMPro.TextMeshProUGUI>();
        t.font = UIKit.FontCN;
        t.text = label;
        t.fontSize = 12;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.color = new Color32(0xF2, 0xD0, 0x7C, 0xFF);
        t.raycastTarget = false;
        t.enableWordWrapping = false;
    }

    // ---------- 场景接线 ----------
    static string SetupScene()
    {
        var pause = AssetDatabase.LoadAssetAtPath<GameObject>(PAUSE);
        var death = AssetDatabase.LoadAssetAtPath<GameObject>(DEATH);
        var gm = Object.FindObjectOfType<GameManager>();
        if (gm == null) return "场景里没有 GameManager";
        var refs = gm.GetComponent<GameUIRefs>();
        if (refs == null) return "GameManager 上没有 GameUIRefs";
        refs.pausePanelPrefab = pause;
        refs.deathPanelPrefab = death;
        refs.victoryPanelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VICTORY);
        EditorUtility.SetDirty(refs);
        var scene = gm.gameObject.scene;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        return "已写入 " + scene.name + " 的 GameUIRefs";
    }
}
