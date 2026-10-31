using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 主菜单场景交互（手动UI版）
/// - 挂在MainMenu场景的MainMenuRoot上，只提供按钮动作，不创建任何Canvas或面板
/// - 标题、背景、按钮等视觉内容全部在MainMenu场景中手动搭建
/// - 开始/退出按钮在Inspector的OnClick中分别绑定StartGame/QuitGame
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    private void Start()
    {
        EnsureEventSystem();

        // 从游戏场景返回主菜单时隐藏旧HUD；主菜单自己的Canvas不受影响
        var oldHud = GameObject.Find("HUD");
        if (oldHud != null) oldHud.SetActive(false);

        // 主菜单没有Player，注册面板让CursorManager保持鼠标解锁，按钮才能点击
        GameUIState.RegisterPanel("MainMenu", null);
    }

    /// <summary>
    /// 主菜单首次启动时启用场景EventSystem；从游戏场景返回时复用已常驻的实例。
    /// 场景中的EventSystem默认关闭，避免加载瞬间与常驻实例同时激活
    /// </summary>
    private void EnsureEventSystem()
    {
        var systems = FindObjectsOfType<UnityEngine.EventSystems.EventSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].gameObject.activeInHierarchy && systems[i].enabled)
                return;
        }

        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i].gameObject.scene.name != "MainMenu") continue;
            systems[i].gameObject.SetActive(true);
            systems[i].enabled = true;
            return;
        }

        // 场景未配置EventSystem时的兜底，仅首次进入主菜单会走到这里
        new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
    }

    private void OnDestroy()
    {
        // 场景UI会随MainMenu卸载，无需也不能在这里额外创建或销毁常驻对象
        GameUIState.UnregisterPanel("MainMenu");
    }

    /// <summary>开始游戏按钮：在Button.onClick中绑定此方法</summary>
    public void StartGame()
    {
        AudioManager.PlaySfx("SFX_UIClick");
        SceneManager.LoadScene("Camp");
    }

    /// <summary>退出游戏按钮：在Button.onClick中绑定此方法</summary>
    public void QuitGame()
    {
        AudioManager.PlaySfx("SFX_UIClick");
        Application.Quit();
    }
}
