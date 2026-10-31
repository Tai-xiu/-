using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 准星交互（HUD规格v0.1组件4）：
/// - 可交互方框：24px四角括号叠加在准星（十字线）外圈，准星指向可交互目标时显示——
///   宝箱与掉落物均复用各自的每帧瞄准评估（与交互键同口径）
/// - 交互提示：准星正下方一行文字，与按键判定同口径——商店/宝箱/掉落物均为
///   准星瞄准判定（各自Update每帧评估），瞄准才提示、瞄准才能交互
///   （优先级：商店 > 协会悬赏板 > 宝箱 > 尸体 > 掉落物）
/// - 进度环：长按类交互（宝箱长按F 2秒开启）蓄力时绕准星44px白环径向填充
/// - 打断反馈：松键/离开范围/受伤打断蓄力→环变红冻结进度0.3s淡出
/// - 显示门控：CursorManager.IsLocked（面板打开/暂停/死亡自动隐藏）
/// 自举DDOL单例，UI挂常驻GameCanvas跨场景存活
/// </summary>
public class InteractPromptUI : MonoBehaviour
{
    public static InteractPromptUI Instance { get; private set; }

    private RectTransform _bracket;   // 四角括号（瞄准可交互目标时显示）
    private Text _hint;               // 交互提示行（准星下方）
    private Image _ring;              // 长按进度环（蓄力时绕准星径向填充）
    private float _flash;             // 打断反馈剩余时长（>0=红环淡出中）
    private float _flashProgress;     // 打断时刻冻结的进度（红环显示用）
    private string _lastHint = "";    // 提示文本去重（避免每帧重复SetText脏化画布）

    private static Sprite _ringSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= BootstrapOnSceneLoaded;
        SceneManager.sceneLoaded += BootstrapOnSceneLoaded;
        BootstrapOnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void BootstrapOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "UI" || Instance != null) return;
        var go = new GameObject("InteractPromptUI");
        go.AddComponent<InteractPromptUI>();
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
        LootChest.HoldInterrupted += OnHoldInterrupted; // 蓄力打断→红环反馈
        SearchableCorpse.HoldInterrupted += OnHoldInterrupted; // 尸体搜索打断同款红环
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            LootChest.HoldInterrupted -= OnHoldInterrupted;
            SearchableCorpse.HoldInterrupted -= OnHoldInterrupted;
            Instance = null;
        }
    }

    /// <summary>长按蓄力被打断（松键/离开范围/受伤）：红环冻结打断时刻进度0.3s淡出</summary>
    private void OnHoldInterrupted(float progress)
    {
        _flash = 0.3f;
        _flashProgress = progress;
    }

    private void Update()
    {
        // GameCanvas就绪后懒建（未就绪则下帧重试）
        if (_bracket == null && !BuildUI()) return;

        if (CursorManager.IsLocked)
        {
            string hint;
            bool bracket = EvaluateInteraction(out hint);
            _bracket.gameObject.SetActive(bracket);
            if (hint != _lastHint)
            {
                _lastHint = hint;
                _hint.text = hint;
            }
            _hint.gameObject.SetActive(hint != "");
        }
        else
        {
            _bracket.gameObject.SetActive(false);
            _hint.gameObject.SetActive(false);
            _lastHint = "";
            _flash = 0f; // 解锁态清打断反馈（再锁定不残留红闪）
        }
        UpdateRing();
    }

    /// <summary>
    /// 长按进度环：有蓄力目标（当前仅宝箱）→白环按HoldProgress径向填充；
    /// 刚被打断→红环冻结进度0.3s淡出；其余隐藏（完成开箱自然消失，不闪红）
    /// </summary>
    private void UpdateRing()
    {
        LootChest chestHold = LootChest.CurrentHold;
        SearchableCorpse corpseHold = SearchableCorpse.CurrentHold;
        if (chestHold != null)
        {
            _flash = 0f;
            _ring.color = new Color(1f, 1f, 1f, 0.9f);
            _ring.fillAmount = chestHold.HoldProgress;
            _ring.gameObject.SetActive(true);
        }
        else if (corpseHold != null)
        {
            _flash = 0f;
            _ring.color = new Color(1f, 1f, 1f, 0.9f);
            _ring.fillAmount = corpseHold.HoldProgress;
            _ring.gameObject.SetActive(true);
        }
        else if (_flash > 0f)
        {
            _flash -= Time.deltaTime;
            float k = Mathf.Clamp01(_flash / 0.3f);
            _ring.color = new Color(0.95f, 0.25f, 0.2f, 0.9f * k); // 打断反馈：红环淡出
            _ring.gameObject.SetActive(true);
        }
        else
        {
            _ring.gameObject.SetActive(false);
        }
    }

    // ===== UI构建 =====

    /// <summary>GameCanvas下挂方框、进度环与提示行（GameManager先于本组件时首帧即就绪）</summary>
    private bool BuildUI()
    {
        Transform canvasRoot = GameManager.Instance != null && GameManager.Instance.GameCanvas != null
            ? GameManager.Instance.GameCanvas.transform
            : null;
        if (canvasRoot == null) return false;

        _bracket = BuildBracket(canvasRoot);
        _ring = BuildRing(canvasRoot);
        _hint = BuildHint(canvasRoot);
        return true;
    }

    /// <summary>四角括号：24px方框四角各一条横臂+竖臂（2px像素线，与HUD描边规格同族）</summary>
    private RectTransform BuildBracket(Transform canvas)
    {
        var root = new GameObject("InteractBracket", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvas, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f); // 屏幕正中
        root.sizeDelta = Vector2.zero;
        root.gameObject.SetActive(false);

        const float box = 24f; // 方框边长
        const float arm = 7f;  // 角臂长
        const float th = 2f;   // 线厚（规格2px）
        float hx = box * 0.5f, hy = box * 0.5f;
        // 左上/右上/左下/右下，各=横臂+竖臂
        MakeBar(root, new Vector2(-hx + arm * 0.5f, hy - th * 0.5f), new Vector2(arm, th));
        MakeBar(root, new Vector2(-hx + th * 0.5f, hy - arm * 0.5f), new Vector2(th, arm));
        MakeBar(root, new Vector2(hx - arm * 0.5f, hy - th * 0.5f), new Vector2(arm, th));
        MakeBar(root, new Vector2(hx - th * 0.5f, hy - arm * 0.5f), new Vector2(th, arm));
        MakeBar(root, new Vector2(-hx + arm * 0.5f, -hy + th * 0.5f), new Vector2(arm, th));
        MakeBar(root, new Vector2(-hx + th * 0.5f, -hy + arm * 0.5f), new Vector2(th, arm));
        MakeBar(root, new Vector2(hx - arm * 0.5f, -hy + th * 0.5f), new Vector2(arm, th));
        MakeBar(root, new Vector2(hx - th * 0.5f, -hy + arm * 0.5f), new Vector2(th, arm));
        return root;
    }

    /// <summary>单条括号臂（白色2px线，全raycastTarget=false不挡准星射击点击）</summary>
    private void MakeBar(RectTransform parent, Vector2 center, Vector2 size)
    {
        var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        bar.SetParent(parent, false);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
        bar.anchoredPosition = center;
        bar.sizeDelta = size;
        var img = bar.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.9f);
        img.raycastTarget = false;
    }

    /// <summary>
    /// 长按进度环：44px白色圆环Radial360顶点起顺时针径向填充（与食用进度圈同手法），
    /// 绕准星外圈（方框24px在其内），raycastTarget=false
    /// </summary>
    private Image BuildRing(Transform canvas)
    {
        var go = new GameObject("HoldRing", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvas, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); // 屏幕正中
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(44f, 44f);
        var img = go.GetComponent<Image>();
        img.sprite = RingSprite();
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Top;
        img.fillClockwise = true;
        img.color = new Color(1f, 1f, 1f, 0.9f);
        img.raycastTarget = false;
        go.SetActive(false); // 无蓄力隐藏
        return img;
    }

    /// <summary>白色圆环贴图（96px，环半径40厚8，边缘1px抗锯齿；与HotbarController食用圈同源手法）</summary>
    private static Sprite RingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        const int n = 96;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        float c = (n - 1) * 0.5f;
        float outer = 44f, inner = 36f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Clamp01(Mathf.Min(d - (inner - 0.5f), (outer + 0.5f) - d));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 48f);
        return _ringSprite;
    }

    /// <summary>交互提示行：准星正下方34px，14px白字黑描边（与血条数值同风格）</summary>
    private Text BuildHint(Transform canvas)
    {
        var go = new GameObject("InteractHint", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvas, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -34f);
        rt.sizeDelta = new Vector2(400f, 22f);
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 14;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.text = "";
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; // 长物品名不换行
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color32(0x1A, 0x1A, 0x22, 0xFF);
        ol.effectDistance = new Vector2(1f, -1f);
        go.SetActive(false);
        return t;
    }

    // ===== 目标汇集 =====

    /// <summary>
    /// 汇集当前可交互目标：优先瞄准中的未开启宝箱（取最近），其次被瞄准的掉落物。
    /// 宝箱与掉落物均为瞄准判定（各自Update每帧评估，与交互键同口径）——
    /// 返回是否显示方框；hint=提示文案（空串=无提示）
    /// </summary>
    private bool EvaluateInteraction(out string hint)
    {
        var cam = Camera.main;

        // 0. 商店：范围内+准星指向（射线命中商店，ShopSystem自评估）
        ShopSystem shop = null;
        if (cam != null)
        {
            float bestSqrShop = float.MaxValue;
            for (int i = 0; i < ShopSystem.All.Count; i++)
            {
                var s = ShopSystem.All[i];
                if (s == null || !s.Aimed) continue;
                float sqr = (s.transform.position - cam.transform.position).sqrMagnitude;
                if (sqr < bestSqrShop)
                {
                    bestSqrShop = sqr;
                    shop = s;
                }
            }
        }
        if (shop != null)
        {
            hint = "[F] 打开商店";
            return true;
        }

        // 0.5 冒险者协会悬赏板：范围内+准星指向（射线命中协会白模，QuestBoardInteract自评估；
        // 优先级插在商店之后——2026-10-21拍板）
        QuestBoardInteract board = null;
        if (cam != null)
        {
            float bestSqrBoard = float.MaxValue;
            for (int i = 0; i < QuestBoardInteract.All.Count; i++)
            {
                var b = QuestBoardInteract.All[i];
                if (b == null || !b.Aimed) continue;
                float sqr = (b.transform.position - cam.transform.position).sqrMagnitude;
                if (sqr < bestSqrBoard)
                {
                    bestSqrBoard = sqr;
                    board = b;
                }
            }
        }
        if (board != null)
        {
            hint = "[F] 冒险者协会";
            return true;
        }

        // 1. 宝箱：范围内+准星指向+未开启取最近（Aimed即F键交互门槛，提示同口径）
        LootChest chest = null;
        if (cam != null)
        {
            float bestSqr = float.MaxValue;
            for (int i = 0; i < LootChest.All.Count; i++)
            {
                var c = LootChest.All[i];
                if (c == null || c.IsOpen || !c.Aimed) continue;
                float sqr = (c.transform.position - cam.transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    chest = c;
                }
            }
        }
        if (chest != null)
        {
            hint = "[F] 开启宝箱";
            return true;
        }

        // 1.5 尸体：范围内+准星指向取最近（长按F搜索，提示与F键同口径；尸体可重复搜索故已搜索的仍提示）
        SearchableCorpse corpse = null;
        if (cam != null)
        {
            float bestSqrCorpse = float.MaxValue;
            for (int i = 0; i < SearchableCorpse.All.Count; i++)
            {
                var c = SearchableCorpse.All[i];
                if (c == null || !c.Aimed) continue;
                float sqr = (c.transform.position - cam.transform.position).sqrMagnitude;
                if (sqr < bestSqrCorpse) { bestSqrCorpse = sqr; corpse = c; }
            }
        }
        if (corpse != null)
        {
            hint = "[F] 搜索";
            return true;
        }

        // 2. 掉落物：复用LootItemCube的每帧瞄准评估（提示与F同口径=瞄准判定）
        var cube = LootItemCube.CurrentAimed;
        if (cube != null)
        {
            hint = "[F] 拾取 " + cube.PromptLabel;
            return true;
        }

        hint = "";
        return false;
    }
}
