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

    /// <summary>绑定暂停面板 Prefab（不再创建 UI）</summary>
    private void BuildPanel()
    {
        var refs = GameUIRefs.Instance;
        var canvas = GameManager.Instance != null ? GameManager.Instance.GameCanvas : null;
        if (refs == null || canvas == null)
        {
            Debug.LogError("[PauseUI] GameUIRefs/GameCanvas 未就绪，暂停面板无法绑定");
            return;
        }
        var t = refs.EnsurePausePanel(canvas);
        if (t == null) return;
        _panel = t.gameObject;

        var btnContinue = t.Find("Btn_Continue") != null ? t.Find("Btn_Continue").GetComponent<Button>() : null;
        var btnMenu = t.Find("Btn_MainMenu") != null ? t.Find("Btn_MainMenu").GetComponent<Button>() : null;
        if (btnContinue != null)
        {
            btnContinue.onClick.RemoveAllListeners();
            btnContinue.onClick.AddListener(() =>
            {
                AudioManager.PlaySfx("SFX_UIClick");
                TogglePause();
            });
        }
        if (btnMenu != null)
        {
            btnMenu.onClick.RemoveAllListeners();
            btnMenu.onClick.AddListener(() =>
            {
                AudioManager.PlaySfx("SFX_UIClick");
                ReturnToMainMenu();
            });
        }
    }




}
