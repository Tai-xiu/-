using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 光标状态机 + 第一人称准星（自举DDOL单例，集中每帧设置避免多脚本互踩）：
/// - 锁定（Locked+隐藏光标）：游戏进行中（无面板/未暂停/未死亡）——显示屏幕中央十字准星
///   （用户裁定十字线；可交互方框/进度环/交互提示由InteractPromptUI在同锚点叠加）；
/// - 解锁（None+显示光标）：背包/商店任一面板（GameUIState）、暂停菜单（PauseUI.IsPaused）、
///   玩家死亡（死亡结算面板需点按钮）——恢复鼠标操作UI，准星隐藏。
/// FirstPersonLook依据IsLocked决定是否响应视角输入；准星挂在常驻GameCanvas下跨场景存活。
/// </summary>
public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance { get; private set; }

    /// <summary>当前光标是否锁定（true=游戏进行中，第一人称视角输入生效、准星显示）</summary>
    public static bool IsLocked { get; private set; }

    private Transform _playerTf;
    private Health _playerHealth;
    private RectTransform _crosshair;
    /// <summary>解锁→锁定过渡后的强制重锁剩余帧数（编辑器Esc吞锁兜底，见Update锁定分支）</summary>
    private int _relockBurst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= BootstrapOnSceneLoaded;
        SceneManager.sceneLoaded += BootstrapOnSceneLoaded;
        BootstrapOnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void BootstrapOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // UI.scene不创建准星和光标控制器，避免手动制作UI时鼠标被锁定
        if (scene.name == "UI" || scene.name == "MainMenu" || Instance != null) return;
        var go = new GameObject("CursorManager");
        go.AddComponent<CursorManager>();
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildCrosshair();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // 非游戏场景只保证鼠标可操作，不创建/显示第一人称准星
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName == "MainMenu" || sceneName == "UI")
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            IsLocked = false;
            if (_crosshair != null && _crosshair.gameObject.activeSelf) _crosshair.gameObject.SetActive(false);
            return;
        }

        // MainMenu中GameUIRefs尚未就绪时不会强行构建；进入游戏场景后每帧检测并重试一次成功的绑定
        if (_crosshair == null && GameUIRefs.Instance != null
            && GameManager.Instance != null && GameManager.Instance.GameCanvas != null)
        {
            BuildCrosshair();
        }

        // 玩家引用：场景切换后重查（Player为场景对象）
        if (_playerTf == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null)
            {
                _playerTf = p.transform;
                _playerHealth = p.GetComponent<Health>();
            }
        }

        bool dead = _playerHealth != null && _playerHealth.IsDead;
        bool shouldUnlock = GameUIState.IsAnyPanelOpen || PauseUI.IsPaused || dead;

        if (shouldUnlock)
        {
            if (IsLocked || Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            IsLocked = false;
            if (_crosshair != null && _crosshair.gameObject.activeSelf) _crosshair.gameObject.SetActive(false);
        }
        else
        {
            bool wasUnlocked = !IsLocked;
            // 无条件每帧重发锁定（不能加等值守卫）：编辑器GameView按Esc由编辑器解锁鼠标，
            // 但lockState属性仍读Locked，等值守卫会漏判"假锁定"导致需点击才能恢复
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (wasUnlocked) _relockBurst = 40; // 解锁→锁定过渡：爆发期强制重锁
            IsLocked = true;
            if (_crosshair != null && !_crosshair.gameObject.activeSelf) _crosshair.gameObject.SetActive(true);

            // 爆发期内每帧先归零再上锁：同值赋值会被Unity内部忽略，先None再Locked强制重新申请鼠标约束
            if (_relockBurst > 0)
            {
                _relockBurst--;
                Cursor.lockState = CursorLockMode.None;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    /// <summary>构建屏幕中央十字准星（横竖两条细线；用户裁定十字线——可交互方框/进度环/
    /// 交互提示由InteractPromptUI叠加），挂常驻GameCanvas，仅锁定时显示</summary>
    private void BuildCrosshair()
    {
        var refs = GameUIRefs.Instance;
        var canvas = GameManager.Instance != null ? GameManager.Instance.GameCanvas : null;
        // 首场景/场景切换早期尚未就绪属于正常时序，交给Update在后续帧重试
        if (refs == null || canvas == null) return;
        _crosshair = refs.EnsureCrosshair(canvas) as RectTransform;
        if (_crosshair != null) _crosshair.gameObject.SetActive(false);
    }

    /// <summary>单条准星线（白色细线，raycastTarget=false不挡UI点击）</summary>
    private void MakeLine(RectTransform parent, Vector2 size)
    {
        var line = new GameObject("Line", typeof(Image)).GetComponent<RectTransform>();
        line.SetParent(parent, false);
        line.anchorMin = line.anchorMax = line.pivot = new Vector2(0.5f, 0.5f);
        line.sizeDelta = size;
        var img = line.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.85f);
        img.raycastTarget = false;
    }
}
