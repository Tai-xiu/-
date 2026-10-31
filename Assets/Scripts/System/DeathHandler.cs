using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 死亡结算处理（挂Player）
/// 监听Health.onDeath → 弹出"你已阵亡"面板 →
/// 点击"返回营地"按钮后：
///   1. 清空装备栏与物品栏（调用Inventory.ClearOnDeath；金币在Wallet中不受影响）
///   2. 血量回满并解除死亡状态（Health.ReviveFull）
///   3. 解除移动/攻击输入禁用
///   4. 加载Camp场景并摆放到出生点（跨场景加载与摆位由DDOL Runner执行，
///      避免协程宿主随场景卸载而中断——与Portal同一模式）
/// </summary>
public class DeathHandler : MonoBehaviour
{
    [Header("—— 死亡结算配置 ——")]
    [Tooltip("营地场景名")]
    [SerializeField] private string campSceneName = "Camp";

    [Tooltip("营地出生点标记名（返回后摆放到此位置）")]
    [SerializeField] private string campSpawnName = "SpawnPoint";

    private Health _health;
    private Inventory _inventory;
    private PlayerCombat _combat;
    private PlayerController _pc;
    private GameObject _panel;

    private void Awake()
    {
        _health = GetComponent<Health>();
        _inventory = GetComponent<Inventory>();
        _combat = GetComponent<PlayerCombat>();
        _pc = GetComponent<PlayerController>();
    }

    private void Start()
    {
        if (_health != null) _health.onDeath.AddListener(OnPlayerDeath);
    }

    private void OnDestroy()
    {
        if (_health != null) _health.onDeath.RemoveListener(OnPlayerDeath);
    }

    /// <summary>死亡事件回调：弹出阵亡面板（幂等）</summary>
    private void OnPlayerDeath()
    {
        if (_panel != null) return;
        BuildDeathPanel();
    }

    /// <summary>构建"你已阵亡"面板（常驻UI根画布下）</summary>
    private void BuildDeathPanel()
    {
        Transform root;
        if (GameManager.Instance != null && GameManager.Instance.GameCanvas != null)
        {
            root = GameManager.Instance.GameCanvas.transform;
        }
        else
        {
            // 兜底：GameManager缺失时自建常驻画布
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = go.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            root = go.transform;
            DontDestroyOnLoad(go);
        }

        _panel = new GameObject("DeathPanel", typeof(Image), typeof(VerticalLayoutGroup));
        _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
        var prt = _panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(520, 360);
        _panel.transform.SetParent(root, false);

        var pvlg = _panel.GetComponent<VerticalLayoutGroup>();
        pvlg.padding = new RectOffset(16, 16, 16, 16);
        pvlg.spacing = 12f;
        pvlg.childControlWidth = true;
        pvlg.childControlHeight = false;
        pvlg.childForceExpandHeight = false;
        pvlg.childForceExpandWidth = true;

        // 标题（红色）
        var title = CreateText(_panel.transform, "你已阵亡", 34, TextAnchor.MiddleCenter, new Color(0.9f, 0.2f, 0.2f, 1f));
        title.GetComponent<LayoutElement>().minHeight = 52f;

        // 说明
        var sub = CreateText(_panel.transform, "装备与物品栏已被清空（金币保留）", 16, TextAnchor.MiddleCenter,
            new Color(1f, 1f, 1f, 0.8f));
        sub.GetComponent<LayoutElement>().minHeight = 26f;

        // 返回营地按钮（红底）
        CreateButton(_panel.transform, "返 回 营 地", new Color(0.7f, 0.3f, 0.3f, 1f), OnReturnCampButton);
    }

    /// <summary>
    /// "返回营地"按钮回调：执行死亡惩罚与状态恢复，然后传送回营地出生点
    /// </summary>
    private void OnReturnCampButton()
    {
        // 1. 死亡惩罚：清空装备与物品栏（金币保留在Wallet中，不受影响）
        if (_inventory != null) _inventory.ClearOnDeath();

        // 2. 血量回满并解除死亡状态
        if (_health != null) _health.ReviveFull();

        // 3. 解除移动/攻击输入禁用
        if (_combat != null) _combat.SetInputEnabled(true);
        if (_pc != null) _pc.SetInputEnabled(true);

        // 4. 关闭阵亡面板
        if (_panel != null) Destroy(_panel);

        // 5. 场景传送：不在营地则加载Camp并摆放到出生点（跨场景部分交给DDOL Runner）
        if (SceneManager.GetActiveScene().name != campSceneName)
        {
            var runnerGo = new GameObject("DeathReturnRunner");
            var runner = runnerGo.AddComponent<DeathReturnRunner>();
            DontDestroyOnLoad(runnerGo);
            runner.Run(campSceneName, campSpawnName);
        }
        else
        {
            // 已在营地：直接摆放到出生点附近
            var spawn = GameObject.Find(campSpawnName);
            if (spawn != null)
            {
                transform.position = spawn.transform.position + Vector3.up * 0.1f;
                transform.rotation = spawn.transform.rotation;
            }
        }
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
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = size + 6f;
        return t;
    }

    private void CreateButton(Transform parent, string label, Color color, UnityAction onClick)
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

        go.GetComponent<Button>().onClick.AddListener(onClick);
    }
}

/// <summary>
/// 死亡返回执行器：挂载在DontDestroyOnLoad物体上，跨场景存活完成加载与摆位
/// </summary>
public class DeathReturnRunner : MonoBehaviour
{
    public void Run(string sceneName, string spawnName)
    {
        StartCoroutine(LoadAndPlace(sceneName, spawnName));
    }

    private IEnumerator LoadAndPlace(string sceneName, string spawnName)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone)
        {
            yield return null;
        }
        yield return null; // 等新场景Player的Start执行完（数据恢复等）

        var spawn = GameObject.Find(spawnName);
        var pl = GameObject.FindWithTag("Player");
        if (spawn != null && pl != null)
        {
            pl.transform.position = spawn.transform.position + Vector3.up * 0.1f;
            // 朝向对齐出生点标记（SpawnPoint旋转=面朝营火）
            pl.transform.rotation = spawn.transform.rotation;
        }
        Destroy(gameObject);
    }
}
