using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD血条（规格v0.1组件1：左下血条，最高优先级）
/// - 布局：左下角32/32，320×16px含4px像素描边金属卡槽外框
/// - 双层条：前景红色填充掉血瞬切（要疼）；白色50%残影条延迟0.5s后0.3s追赶（看清这一刀掉了多少）；损失部分露出暗灰卡槽底
/// - 分段刻度：每25点一格，2px深色刻度线盖在填充上
/// - 数值：条外左上方贴条顶左对齐，富文本双色——实时值白（LowHP变黄），上限值灰弱化
/// - 状态：Normal红色 / Hit受击闪白0.1s→闪红0.1s（数值瞬间刷新）/ LowHP≤30%深红#B3261E呼吸闪烁1Hz
/// - 体力条（组件2）：血条正下方8px，240×10黄#F5C518含2px描边，无数字；<20闪黄；消耗/恢复规则见Stamina.cs
/// - 快捷栏（组件3）：堆叠最底部（HotbarController自动补挂），血条/体力条随其上移保持堆叠
/// - 掉血禁用平滑动画（瞬切），回血平滑0.5s（掉血要疼，回血要暖）
/// - 挂在Player上；运行时构建于GameManager常驻UI根画布下（跨场景存活）
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("—— 布局（参考系像素） ——")]
    [SerializeField] private Vector2 barSize = new Vector2(320f, 16f);   // 总尺寸含外框
    [SerializeField] private Vector2 barMargin = new Vector2(32f, 32f);  // 左下角边距（堆叠底边）
    [SerializeField] private float frameThickness = 4f;                  // 金属卡槽外框厚度
    [SerializeField] private float tickStepHP = 25f;                     // 每25点一格刻度线
    [SerializeField] private float hotbarGap = 12f;                      // 快捷栏与体力条间距（组件3）

    [Header("—— 配色（规格定稿） ——")]
    [SerializeField] private Color fillColor = new Color32(0xE5, 0x48, 0x4D, 0xFF);   // 红色填充
    [SerializeField] private Color trackColor = new Color32(0x2A, 0x2A, 0x35, 0xFF);  // 损失部分暗灰卡槽底
    [SerializeField] private Color ghostColor = new Color(1f, 1f, 1f, 0.5f);          // 残影白50%
    [SerializeField] private Color frameColor = new Color32(0x1A, 0x1A, 0x22, 0xFF);  // 4px像素描边
    [SerializeField] private Color frameMetal = new Color32(0x7A, 0x84, 0x96, 0xFF);  // 卡槽内缘金属亮线
    [SerializeField] private Color tickColor = new Color32(0x1A, 0x1A, 0x22, 0xFF);   // 刻度线
    [SerializeField] private Color lowFillColor = new Color32(0xB3, 0x26, 0x1E, 0xFF);// LowHP深红
    [SerializeField] private Color lowFillDark = new Color32(0x6E, 0x14, 0x10, 0xFF); // LowHP呼吸暗端
    [SerializeField] private Color hitWhiteColor = Color.white;                        // 受击闪白
    [SerializeField] private Color hitRedColor = new Color32(0xFF, 0x52, 0x52, 0xFF); // 受击闪红
    [SerializeField] private Color textColor = Color.white;                            // 实时数值白
    [SerializeField] private Color textLowColor = new Color32(0xF5, 0xC5, 0x18, 0xFF);// LowHP数值黄
    [SerializeField] private Color textMaxColor = new Color32(0xA0, 0xA4, 0xAE, 0xFF);// 上限数值灰（固定部分弱化）

    [Header("—— 体力条（规格组件2：血条下8px，240×10，黄#F5C518，无数字） ——")]
    [SerializeField] private Vector2 staminaBarSize = new Vector2(240f, 10f);          // 总尺寸含描边
    [SerializeField] private float staminaGap = 8f;                                     // 与血条间距
    [SerializeField] private float staminaFrameThickness = 2f;                          // 风格规范：HUD控件2px描边
    [SerializeField] private Color staminaFillColor = new Color32(0xF5, 0xC5, 0x18, 0xFF); // 体力黄
    [SerializeField] private Color staminaLowDark = new Color32(0x8F, 0x72, 0x0D, 0xFF);   // 闪黄暗端
    [SerializeField] private Color staminaLowBright = new Color32(0xFF, 0xE2, 0x62, 0xFF); // 闪黄亮端
    [SerializeField] private float staminaLowThreshold = 20f;                           // 不足一次挥砍阈值
    [SerializeField] private float staminaFlashHz = 2f;                                 // 闪黄频率

    [Header("—— 节奏（规格：掉血瞬切/回血0.5s/残影0.5s+0.3s） ——")]
    [SerializeField] private float ghostDelay = 0.5f;    // 残影延迟
    [SerializeField] private float ghostCatchUp = 0.3f;  // 残影追赶时长
    [SerializeField] private float hitFlashStep = 0.1f;  // 受击闪白/闪红各持续
    [SerializeField] private float healSmoothTime = 0.5f;// 回血平滑时长
    [SerializeField] private float lowHpThreshold = 0.3f;// LowHP阈值（比例）

    private Health _health;
    private RectTransform _root;
    private RectTransform _fillRect;
    private RectTransform _ghostRect;
    private Image _fillImg;
    private Text _valueText;

    private float _innerWidth;        // 去除外框后的填充区宽度
    private float _displayHp;         // 前景显示血量（掉血瞬切，回血平滑）
    private float _ghostHp;           // 残影血量（延迟后追赶）
    private float _ghostDelayTimer;   // 残影延迟倒计时（>0=延迟中）
    private float _ghostCatchSpeed;   // 残影追赶速度（进入追赶瞬间锁定）
    private float _hitTimer;          // 受击表现剩余时间
    private float _healSpeed;         // 回血速度（进入平滑回血瞬间锁定）
    private bool _healing;            // 平滑回血中
    private string _maxHpHex;         // 上限值灰色hex（富文本用，构建时缓存）

    private Stamina _stamina;             // 体力数据源（PlayerCombat.Awake自动补挂，缺失则不建条）
    private RectTransform _staminaFillRect;
    private Image _staminaFillImg;
    private float _staminaInnerW;         // 体力条去描边后的填充区宽度

    private HotbarController _hotbar;     // 快捷栏（组件3，Start自动补挂；堆叠定位依赖其尺寸）

    private static readonly Color32 OutlineColor = new Color32(0x1A, 0x1A, 0x22, 0xFF);

    private void Start()
    {
        _health = GetComponent<Health>();
        if (_health == null) { enabled = false; return; }

        // 常驻UI根画布（GameManager优先，缺失兜底自建）
        Transform canvasRoot;
        if (GameManager.Instance != null && GameManager.Instance.GameCanvas != null)
        {
            canvasRoot = GameManager.Instance.GameCanvas.transform;
        }
        else
        {
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = go.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasRoot = go.transform;
            DontDestroyOnLoad(go);
        }

        // 清理旧版HUD元素（历史角标/血条残留；不含本版根名HPBar_Spec）
        // 清理旧版HUD元素（历史角标/血条残留）+ 现役三代根（场景切换后旧场景玩家的
        // HPBar_Spec/StaminaBar_Spec/HotbarBar_Spec会残留在常驻GameCanvas上，不清理则与新HUD重叠——
        // 实测踩坑：血条数值两层文字叠在一起）。Find只搜画布直接子级，深层同名不受影响
        foreach (var n in new[] { "HPBackground", "HPSlider", "Fill Area", "Fill", "HPBar", "HPFill", "HPValue", "CoinText", "HPBar_Root", "HPBar_Spec", "StaminaBar_Spec", "HotbarBar_Spec" })
        {
            var old = canvasRoot.Find(n);
            if (old != null) Destroy(old.gameObject);
        }

        // 快捷栏组件（组件3）：自动补挂（含HUD栏与食用通道）；血条/体力条堆叠位置依赖其尺寸
        _hotbar = GetComponent<HotbarController>();
        if (_hotbar == null) _hotbar = gameObject.AddComponent<HotbarController>();

        BuildBar(canvasRoot);

        // 体力条（规格组件2）：数据层Stamina由PlayerCombat.Awake自动补挂，存在才建UI
        _stamina = GetComponent<Stamina>();
        if (_stamina != null) BuildStaminaBar(canvasRoot);

        _health.onDamaged.AddListener(OnDamaged);
        _displayHp = _health.Current;
        _ghostHp = _health.Current;
    }

    private void OnDestroy()
    {
        if (_health != null) _health.onDamaged.RemoveListener(OnDamaged);
    }

    /// <summary>构建血条层级：根(定位)→卡槽底/残影/前景/刻度/外框/数值</summary>
    private void BuildBar(Transform canvasRoot)
    {
        _root = new GameObject("HPBar_Spec", typeof(RectTransform)).GetComponent<RectTransform>();
        _root.SetParent(canvasRoot, false);
        _root.anchorMin = new Vector2(0f, 0f);
        _root.anchorMax = new Vector2(0f, 0f);
        _root.pivot = new Vector2(0f, 0f);
        // 堆叠布局（规格示意图自上而下：血条/体力条/快捷栏，底边统一barMargin）：
        // 血条y = 32 + 快捷栏48 + gap12 + 体力条10 + gap8 = 110
        _root.anchoredPosition = new Vector2(barMargin.x,
            barMargin.y + _hotbar.BarSize.y + hotbarGap + staminaBarSize.y + staminaGap);
        _root.sizeDelta = barSize;

        float f = frameThickness;
        var inner = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
        inner.SetParent(_root, false);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(f, f);
        inner.offsetMax = new Vector2(-f, -f);
        _innerWidth = barSize.x - f * 2f;

        MakeImage(inner, "Track", trackColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _ghostRect = MakeImage(inner, "GhostFill", ghostColor,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f)).rectTransform;
        // 动态宽度条必须左缘pivot：默认(0.5,0.5)会让sizeDelta以锚线为中心对称生长，一半伸到条外
        _ghostRect.pivot = new Vector2(0f, 0.5f);
        _ghostRect.sizeDelta = new Vector2(_innerWidth, 0f);
        _fillImg = MakeImage(inner, "Fill", fillColor,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f));
        _fillRect = _fillImg.rectTransform;
        _fillRect.pivot = new Vector2(0f, 0.5f);
        _fillRect.sizeDelta = new Vector2(_innerWidth, 0f);

        // 分段刻度线（每tickStepHP一格，2px深色，盖在填充上保证可读）
        int ticks = Mathf.Max(0, Mathf.RoundToInt(_health.Max / tickStepHP) - 1);
        for (int i = 1; i <= ticks; i++)
        {
            float ratio = i * tickStepHP / _health.Max;
            var tick = MakeImage(inner, "Tick_" + i, tickColor,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f)).rectTransform;
            tick.anchorMin = new Vector2(ratio, 0f);
            tick.anchorMax = new Vector2(ratio, 1f);
            tick.pivot = Vector2.zero;
            tick.sizeDelta = new Vector2(2f, 0f);
        }

        // 4px金属卡槽外框（4条边）+ 内缘1px金属亮线（上边）
        MakeStrip(_root, "Frame_Top", frameColor, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -f), 0f, f);
        MakeStrip(_root, "Frame_Bottom", frameColor, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, f), 0f, f);
        MakeStrip(_root, "Frame_Left", frameColor, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(f, 0f), f, 0f);
        MakeStrip(_root, "Frame_Right", frameColor, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(-f, 0f), f, 0f);
        var metalTop = MakeStrip(_root, "Metal_Top", frameMetal, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -(f + 1f)), 0f, 1f);
        metalTop.offsetMin = new Vector2(f, -(f + 1f));
        metalTop.offsetMax = new Vector2(-f, -f);

        // 数值：条外左上方（贴条顶、与条左缘对齐），富文本双色——实时值白（LowHP黄），上限值灰弱化
        var textGo = new GameObject("Value", typeof(Text));
        textGo.transform.SetParent(_root, false);
        _valueText = textGo.GetComponent<Text>();
        _valueText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _valueText.fontSize = 14;
        _valueText.alignment = TextAnchor.MiddleLeft;
        _valueText.color = textColor;
        _valueText.raycastTarget = false;
        _maxHpHex = ColorUtility.ToHtmlStringRGB(textMaxColor);
        var trt = _valueText.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(0f, 1f);
        trt.pivot = new Vector2(0f, 0f);
        trt.sizeDelta = new Vector2(110f, 16f);
        trt.anchoredPosition = new Vector2(0f, 2f);
        var ol = textGo.AddComponent<Outline>();
        ol.effectColor = OutlineColor;
        ol.effectDistance = new Vector2(1.5f, -1.5f);
    }

    /// <summary>构建体力条层级（血条正下方8px）：根→卡槽底/前景/2px描边外框；无残影无刻度无数字</summary>
    private void BuildStaminaBar(Transform canvasRoot)
    {
        float f = staminaFrameThickness;
        var root = new GameObject("StaminaBar_Spec", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasRoot, false);
        root.anchorMin = new Vector2(0f, 0f);
        root.anchorMax = new Vector2(0f, 0f);
        root.pivot = new Vector2(0f, 0f);
        root.anchoredPosition = new Vector2(barMargin.x, barMargin.y + _hotbar.BarSize.y + hotbarGap); // 血条下方：快捷栏48+gap12=92
        root.sizeDelta = staminaBarSize;

        var inner = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
        inner.SetParent(root, false);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(f, f);
        inner.offsetMax = new Vector2(-f, -f);
        _staminaInnerW = staminaBarSize.x - f * 2f;

        MakeImage(inner, "Track", trackColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _staminaFillImg = MakeImage(inner, "StaminaFill", staminaFillColor,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f));
        _staminaFillRect = _staminaFillImg.rectTransform;
        // 同血条教训：动态宽度必须左缘pivot，默认(0.5,0.5)会对称生长一半外溢
        _staminaFillRect.pivot = new Vector2(0f, 0.5f);
        _staminaFillRect.sizeDelta = new Vector2(_staminaInnerW, 0f);

        // 2px像素描边外框（风格规范：所有HUD控件2px深色描边）
        MakeStrip(root, "Frame_Top", frameColor, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -f), 0f, f);
        MakeStrip(root, "Frame_Bottom", frameColor, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, f), 0f, f);
        MakeStrip(root, "Frame_Left", frameColor, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(f, 0f), f, 0f);
        MakeStrip(root, "Frame_Right", frameColor, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(-f, 0f), f, 0f);
    }

    /// <summary>在parent下创建拉伸/锚点Image（快捷工具）</summary>
    private Image MakeImage(RectTransform parent, string name, Color color,
        Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
        return img;
    }

    /// <summary>创建一条边框条（锚定边+厚度）</summary>
    private RectTransform MakeStrip(RectTransform parent, string name, Color color,
        Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, float w, float h)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = pos;
        return rt;
    }

    /// <summary>受击事件：掉血瞬切+受击闪白闪红+残影延迟追赶启动</summary>
    private void OnDamaged(float damage)
    {
        _hitTimer = hitFlashStep * 2f;          // 闪白0.1s+闪红0.1s
        _ghostDelayTimer = ghostDelay;          // 残影先停0.5s
        _ghostCatchSpeed = Mathf.Max(0.01f, (_ghostHp - _health.Current) / ghostCatchUp); // 追赶速度按当前差值锁定
    }

    private void Update()
    {
        if (_health == null || _root == null) return;
        float current = _health.Current;
        float max = Mathf.Max(1f, _health.Max);

        // —— 前景：掉血瞬切 / 回血平滑0.5s（规格关键规则3） ——
        if (current < _displayHp)
        {
            _displayHp = current; // 掉血瞬切（要疼）
        }
        else if (current > _displayHp)
        {
            if (!_healing)
            {
                _healing = true;
                _healSpeed = (current - _displayHp) / Mathf.Max(0.05f, healSmoothTime);
            }
            _displayHp = Mathf.MoveTowards(_displayHp, current, _healSpeed * Time.deltaTime);
            if (_displayHp >= current) _healing = false;
        }
        else
        {
            _healing = false;
        }

        // —— 残影：延迟后按锁定速度0.3s追赶 ——
        if (_ghostDelayTimer > 0f)
        {
            _ghostDelayTimer -= Time.deltaTime;
        }
        else if (_ghostHp > current)
        {
            _ghostHp = Mathf.MoveTowards(_ghostHp, current, _ghostCatchSpeed * Time.deltaTime);
        }
        else
        {
            _ghostHp = current; // 回血场景残影直接跟随
        }

        float innerW = _innerWidth;
        _fillRect.sizeDelta = new Vector2(Mathf.Clamp01(_displayHp / max) * innerW, 0f);
        _ghostRect.sizeDelta = new Vector2(Mathf.Clamp01(_ghostHp / max) * innerW, 0f);

        // —— 受击闪白0.1s→闪红0.1s（任何状态受击优先表现） ——
        bool low = current / max <= lowHpThreshold;
        if (_hitTimer > 0f)
        {
            _hitTimer -= Time.deltaTime;
            float elapsed = hitFlashStep * 2f - _hitTimer;
            _fillImg.color = elapsed < hitFlashStep ? hitWhiteColor : hitRedColor;
        }
        else if (low)
        {
            // LowHP：深红呼吸闪烁1Hz
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f);
            _fillImg.color = Color.Lerp(lowFillDark, lowFillColor, t);
        }
        else
        {
            _fillImg.color = fillColor;
        }

        // —— 数值：瞬间刷新；富文本双色，LowHP实时值变黄 ——
        string liveHex = ColorUtility.ToHtmlStringRGB(low ? textLowColor : textColor);
        _valueText.text = $"<color=#{liveHex}>{Mathf.RoundToInt(current)}</color><color=#{_maxHpHex}>/{Mathf.RoundToInt(max)}</color>";

        // —— 体力条：无数字（体力靠感觉），<20不足一次挥砍时闪黄（规格组件2） ——
        if (_staminaFillRect != null)
        {
            float sMax = Mathf.Max(1f, _stamina.Max);
            _staminaFillRect.sizeDelta = new Vector2(Mathf.Clamp01(_stamina.Current / sMax) * _staminaInnerW, 0f);
            if (_stamina.Current < staminaLowThreshold)
            {
                float st = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * staminaFlashHz);
                _staminaFillImg.color = Color.Lerp(staminaLowDark, staminaLowBright, st);
            }
            else
            {
                _staminaFillImg.color = staminaFillColor;
            }
        }
    }
}
