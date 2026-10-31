using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 暂停系统（白盒占位）
/// - Esc键全局唯一入口：分层路由——第1层有关面板先关最上层面板；无面板才切换暂停/继续
/// - 暂停面板：继续（Esc或按钮）+ 回主菜单
/// - 挂在Player上：主菜单场景无Player，天然不会在主菜单误触发暂停
/// 暂停时同步禁用玩家移动/攻击输入（timeScale=0虽会停住物理，但CC.Move不受timeScale影响，需显式禁用）
/// </summary>
public class PauseUI : MonoBehaviour
{
    private GameObject _panel;

    /// <summary>当前是否处于暂停（静态：供InventoryUI/Chest/Portal/ShopSystem等Update守卫读取）</summary>
    public static bool IsPaused { get; private set; }

    private PlayerController _pc;
    private PlayerCombat _combat;

    private void Awake()
    {
        _pc = GetComponent<PlayerController>();
        _combat = GetComponent<PlayerCombat>();

        // 防御：场景切换后新场景不应继承暂停态（timeScale残留0会卡死新场景逻辑）
        IsPaused = false;
        Time.timeScale = 1f;
    }

    private void Update()
    {
        // Esc键：全局唯一入口（ShopSystem等自有的Esc监听已移除，统一走分层路由）
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        HandleEscape();
    }

    /// <summary>
    /// Esc分层路由（按优先级，集中处理所有Esc语义）：
    /// 第1层：有任何UI面板打开（背包/商店）→ 关闭最上层（最后打开的）面板，本次不触发暂停；
    /// 第2层：无面板打开 → 打开暂停菜单（暂停游戏）；
    /// 第3层：暂停菜单已打开 → 关闭暂停菜单（继续游戏）。
    /// 抽成独立方法便于测试脚本直调（与Update按键触发语义等价）。
    /// </summary>
    private void HandleEscape()
    {
        if (GameUIState.TryCloseTopPanel()) return;
        TogglePause();
    }

    /// <summary>
    /// 暂停/恢复切换：timeScale置0/1 + 输入禁用/解禁 + 面板显隐
    /// </summary>
    public void TogglePause()
    {
        SetPaused(!IsPaused);
    }

    /// <summary>设置暂停状态（面板与timeScale联动）</summary>
    private void SetPaused(bool paused)
    {
        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;

        if (_panel == null) BuildPanel();
        _panel.SetActive(paused);

        // 暂停时禁用玩家输入（恢复时解禁）
        if (_pc != null) _pc.SetInputEnabled(!paused);
        if (_combat != null) _combat.SetInputEnabled(!paused);
    }

    /// <summary>回主菜单：恢复时间流速后加载主菜单场景</summary>
    public void ReturnToMainMenu()
    {
        SetPaused(false);
        SceneManager.LoadScene("MainMenu");
    }

    /// <summary>构建暂停面板（常驻UI根画布下）</summary>
    private void BuildPanel()
    {
        Transform root;
        if (GameManager.Instance != null && GameManager.Instance.GameCanvas != null)
        {
            root = GameManager.Instance.GameCanvas.transform;
        }
        else
        {
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = go.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            root = go.transform;
            DontDestroyOnLoad(go);
        }

        _panel = new GameObject("PausePanel", typeof(Image), typeof(VerticalLayoutGroup));
        _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
        var prt = _panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(460, 340);
        _panel.transform.SetParent(root, false);

        var pvlg = _panel.GetComponent<VerticalLayoutGroup>();
        pvlg.padding = new RectOffset(16, 16, 16, 16);
        pvlg.spacing = 14f;
        pvlg.childControlWidth = true;
        pvlg.childControlHeight = false;
        pvlg.childForceExpandHeight = false;
        pvlg.childForceExpandWidth = true;

        var title = CreateText(_panel.transform, "游戏已暂停", 30, TextAnchor.MiddleCenter, Color.white);
        title.GetComponent<LayoutElement>().minHeight = 48f;

        CreateButton(_panel.transform, "继 续", new Color(0.3f, 0.5f, 0.8f, 1f), () => TogglePause());
        CreateButton(_panel.transform, "回主菜单", new Color(0.6f, 0.35f, 0.3f, 1f), ReturnToMainMenu);
    }

    private Text CreateText(Transform parent, string label, int size, TextAnchor anchor, Color? color = null)
    {
        var go = new GameObject("Text", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = label;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color ?? Color.white;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = size + 6f;
        return t;
    }

    private void CreateButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        go.GetComponent<LayoutElement>().preferredHeight = 48f;

        var t = CreateText(go.transform, label, 18, TextAnchor.MiddleCenter);
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        t.GetComponent<LayoutElement>().ignoreLayout = true;

        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            AudioManager.PlaySfx("SFX_UIClick"); // UI点击音效
            onClick.Invoke();
        });
    }
}
