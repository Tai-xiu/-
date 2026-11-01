using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 传送门/传送阵（通用，白盒原型阶段）
/// 挂在场景中的传送触发器物体上，参数化配置：
/// - Camp的"入口传送门"：targetScene=Dungeon, targetSpawn=PlayerEnterPoint, isExitPortal=false
/// - Dungeon的"返回传送阵"：targetScene=Camp, targetSpawn=SpawnPoint, isExitPortal=true
/// 范围判定：距离轮询（以自身BoxCollider包围盒为界），不依赖触发器事件——
/// transform直移/瞬移进入的玩家同样能被检测到（触发器事件对瞬移不可靠，实测踩坑）
/// 流程：范围内按交互键F → 捕获玩家数据到GameManager → 黑屏淡入 →
///       异步加载目标场景 → 玩家摆放到目标进入点附近（重力自动落稳）→
///       （返回营地时）弹出探索成功结算面板
/// 关键实现：传送协程运行在DontDestroyOnLoad的Runner物体上——协程宿主若留在场景内，
/// 场景卸载时会被销毁导致协程中断（摆位/结算面板/淡出全部丢失，实测踩坑）
/// </summary>
public class Portal : MonoBehaviour
{
    [Header("—— 传送配置 ——")]
    [Tooltip("目标场景名（Build Settings中已加入）")]
    [SerializeField] private string targetScene = "Dungeon";

    [Tooltip("目标场景的进入点标记名（玩家将被传送到此物体附近）")]
    [SerializeField] private string targetSpawnName = "PlayerEnterPoint";

    [Tooltip("是否为迷宫返回营地的胜利传送门（传送完成后弹出探索成功面板）")]
    [SerializeField] private bool isExitPortal;

    [Tooltip("交互键（激活传送，默认F）")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    /// <summary>玩家是否在传送范围内（距离轮询判定）</summary>
    public bool PlayerInRange { get; private set; }

    private bool _activated;
    private Transform _playerTf;
    /// <summary>交互范围中心（Update首次轮询时取自身BoxCollider的世界中心，忽略父节点缩放）</summary>
    private Vector3 _interactCenter;
    private bool _boundsReady;

    private void Start()
    {
        var playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null) _playerTf = playerObj.transform;
    }

    private void Update()
    {
        // 暂停菜单打开期间不响应交互键（守卫：暂停优先）
        if (PauseUI.IsPaused) return;

        if (_activated) return;
        if (_playerTf == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) _playerTf = p.transform;
            if (_playerTf == null) return;
        }

        // 懒加载交互中心（首次轮询时物理已初始化，bounds有效）
        if (!_boundsReady)
        {
            var col = GetComponent<BoxCollider>();
            if (col != null)
            {
                _interactCenter = col.bounds.center;
                _boundsReady = true;
            }
        }

        // 水平距离轮询判定范围（不受父节点缩放导致的扁平包围盒影响）
        Vector3 pp = _playerTf.position; pp.y = 0f;
        Vector3 cc = _interactCenter; cc.y = 0f;
        bool inRange = _boundsReady && (pp - cc).sqrMagnitude <= 2.5f * 2.5f;
        if (inRange != PlayerInRange)
        {
            PlayerInRange = inRange;
            if (inRange)
            {
                FloatingText.Show("按F激活传送阵", transform.position + Vector3.up * 1.6f, 1.2f);
            }
        }

        if (PlayerInRange && Input.GetKeyDown(interactKey))
        {
            Activate();
        }
    }

    /// <summary>
    /// 激活传送（按交互键F由Update触发；测试脚本可直接调用）
    /// 同步完成数据捕获，随后把全流程交给跨场景存活的Runner执行
    /// </summary>
    public void Activate()
    {
        if (_activated) return;
        _activated = true;
        PlayerInRange = false;

        // 数据捕获用本场景引用，必须在场景卸载前完成
        var playerObj = GameObject.FindWithTag("Player");
        var wallet = playerObj.GetComponent<PlayerWallet>();
        var inventory = playerObj.GetComponent<Inventory>();
        GameManager.Instance.CapturePlayerState(wallet, inventory);

        // 传送流程交给跨场景Runner执行（场景卸载不影响）
        var runnerGo = new GameObject("PortalTeleportRunner");
        var runner = runnerGo.AddComponent<PortalTeleportRunner>();
        DontDestroyOnLoad(runnerGo);
        runner.Run(targetScene, targetSpawnName, isExitPortal);
    }
}

/// <summary>
/// 传送流程执行器：挂载在DontDestroyOnLoad物体上，跨场景存活驱动整个传送协程
/// </summary>
public class PortalTeleportRunner : MonoBehaviour
{
    private Transform _victoryFaderRoot;
    public void Run(string targetScene, string targetSpawnName, bool isExitPortal)
    {
        StartCoroutine(TeleportRoutine(targetScene, targetSpawnName, isExitPortal));
    }

    private IEnumerator TeleportRoutine(string targetScene, string targetSpawnName, bool isExitPortal)
    {
        // 1. 方向处理：进入迷宫→清空本次战利品记录；返回营地→保留（结算面板展示）
        if (!isExitPortal)
        {
            GameManager.Instance.BeginRun();
            QuestManager.OnRunStart(); // 悬赏契约：进入迷宫=开局，清本局收集清单（死亡作废同路径）
        }

        // 传送音效（2D：Runner为DDOL物体无空间位置意义，淡入淡出全程可闻）
        AudioManager.PlaySfx("SFX_Teleport");

        // 2. 黑屏淡入（DDOL独立Canvas，跨场景存活）
        var faderRoot = CreateFader();
        var faderImg = faderRoot.GetComponent<Image>();
        yield return Fade(faderImg, 1f, 0.4f);

        // 3. 异步加载目标场景
        AsyncOperation op = SceneManager.LoadSceneAsync(targetScene);
        while (!op.isDone)
        {
            yield return null;
        }

        // 4. 等一帧让新场景Player的Start完成数据恢复，再把玩家摆到进入点附近（高度低放，重力自动落稳）
        yield return null;
        var player = GameObject.FindWithTag("Player");
        var spawn = GameObject.Find(targetSpawnName);
        if (player != null && spawn != null)
        {
            player.transform.position = spawn.transform.position + Vector3.up * 0.1f;
            // 朝向对齐进入点标记（SpawnPoint旋转=面朝营火），避免回城后保持迷宫里的随机朝向
            player.transform.rotation = spawn.transform.rotation;
        }
        else
        {
            Debug.LogError("[Portal] 目标场景缺少Player(tag)或进入点 " + targetSpawnName);
        }

        // 5. 返回营地：探索成功结算面板（黑屏上显示，点继续后淡出）；runner与fader均DDOL存活
        if (isExitPortal)
        {
            QuestManager.NotifyEvacuate(); // 悬赏契约：撤离成功=Evacuate计数+Collect本局清单结算
            ShowVictoryPanel(faderRoot.transform);
            yield break; // 面板驻留，由"继续"按钮销毁fader并淡回营地
        }

        // 6. 黑屏淡出并清理
        yield return Fade(faderImg, 0f, 0.4f);
        Destroy(faderRoot);
        Destroy(gameObject);
    }

    private GameObject CreateFader()
    {
        // GraphicRaycaster必须存在：胜利面板"继续"按钮需要它才能响应点击
        var go = new GameObject("SceneFader", typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999; // 盖住所有UI
        var img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        DontDestroyOnLoad(go); // 黑屏与胜利面板必须跨场景存活
        return go;
    }

    private IEnumerator Fade(Image img, float targetAlpha, float duration)
    {
        float start = img.color.a;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            var c = img.color;
            c.a = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t / duration));
            img.color = c;
            yield return null;
        }
        var end = img.color;
        end.a = targetAlpha;
        img.color = end;
    }

    /// <summary>
    /// 在黑屏上显示"探索成功"面板：本次带回战利品清单 + 引导去商店出售；点"继续"返回营地
    /// </summary>
    private void ShowVictoryPanel(Transform faderRoot)
    {
        _victoryFaderRoot = faderRoot;
        var lootLog = GameManager.Instance.LootLog;

        var refs = GameUIRefs.Instance;
        if (refs == null)
        {
            Debug.LogError("[Portal] GameUIRefs 未就绪，无法显示结算面板");
            return;
        }
        var panel = refs.CreateVictoryPanel(faderRoot);
        if (panel == null) return;

        // 战利品清单仍由代码填（动态内容不烤进 Prefab）
        var lootTr = panel.transform.Find("LootList");
        if (lootTr != null)
        {
            var lootText = lootLog.Count > 0
                ? "  · " + string.Join("\n  · ", lootLog)
                : "  （未拾取战利品）";
            lootTr.GetComponent<Text>().text = lootText;
        }

        // 按钮绑定（Prefab 里的 Btn_Continue）
        var btnTr = panel.transform.Find("Btn_Continue");
        var btn = btnTr != null ? btnTr.GetComponent<Button>() : null;
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(CloseVictory);
        }

        // 注册进全局UI状态门：CursorManager据此解锁鼠标（否则光标锁定无法点击"继续"）；
        // Esc分层路由也会命中本面板（TryCloseTopPanel→closeAction：等效点击"继续"）
        GameUIState.RegisterPanel("VictoryPanel", CloseVictory);
    }

    /// <summary>关闭结算面板：注销UI状态+销毁黑屏与runner（Esc与点"继续"共用，判空防重复销毁）</summary>
    private void CloseVictory()
    {
        GameUIState.UnregisterPanel("VictoryPanel");
        if (_victoryFaderRoot == null) return;
        Destroy(_victoryFaderRoot.gameObject);
        Destroy(gameObject);
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
        t.color = color ?? Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.supportRichText = true;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = size + 6f;
        return t;
    }
}

