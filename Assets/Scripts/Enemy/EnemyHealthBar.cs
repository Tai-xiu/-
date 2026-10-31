using UnityEngine;

/// <summary>
/// 怪物头顶血条（白盒：双Cube构成，透明材质控制淡入淡出）
/// - 显示条件：玩家进入侦测半径内且怪物存活
/// - 淡入淡出：透明材质alpha渐进过渡（非瞬变）
/// - 血量变化：Mathf.Lerp插值缓冲（显示比例渐进趋近实际比例，非跳变）
/// - Billboard：每帧朝向主相机
/// - 血量颜色：高→绿 / 中→黄 / 低→红 渐变
/// - 挂载方式：场景根级物体（非敌人子级），每帧跟随敌人位置，
///   不受敌人Transform缩放影响（史莱姆压扁不会压扁血条）
/// - 敌人销毁时OnDestroy自动清理血条
/// </summary>
public class EnemyHealthBar : MonoBehaviour
{
    [Header("—— 显示条件 ——")]
    [Tooltip("玩家进入此水平距离时显示血条（米）")]
    [SerializeField] private float showRadius = 8f;

    [Tooltip("淡入淡出速度（alpha/秒）")]
    [SerializeField] private float fadeSpeed = 5f;

    [Header("—— 血条外观 ——")]
    [Tooltip("血条总宽（米）")]
    [SerializeField] private float barWidth = 1.3f;

    [Tooltip("血条总高（米）")]
    [SerializeField] private float barHeight = 0.2f;

    [Tooltip("头顶偏移（世界Y，血条中心距地面高度）")]
    [SerializeField] private float headOffsetY = 1.5f;

    [Header("—— 插值缓冲 ——")]
    [Tooltip("血量变化插值速度（越大跟随越快，越小越平滑）")]
    [SerializeField] private float fillLerpSpeed = 8f;

    private Health _health;
    private Transform _playerTf;
    private Camera _cam;
    private Transform _barRoot;
    private Transform _fillTf;
    private Material _bgMat;
    private Material _fillMat;
    private float _displayRatio = 1f;   // 插值缓冲的当前显示比例
    private float _currentAlpha;         // 当前透明度（0~1）
    private bool _barVisible;            // 当前血条物体是否激活

    private static readonly Color BgColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    private static readonly Color FillGreen = new Color(0.3f, 0.85f, 0.3f, 1f);
    private static readonly Color FillRed = new Color(0.9f, 0.2f, 0.2f, 1f);

    private void Start()
    {
        _health = GetComponent<Health>();
        _cam = Camera.main;
        var p = GameObject.FindWithTag("Player");
        if (p != null) _playerTf = p.transform;

        BuildBar();
    }

    /// <summary>构建血条：场景根级物体（不受敌人缩放/旋转影响）</summary>
    private void BuildBar()
    {
        _barRoot = new GameObject("HPBar_" + gameObject.name).transform;

        // 背景（深灰，Trigger无碰撞）
        var bgGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bgGo.name = "Background";
        Object.Destroy(bgGo.GetComponent<Collider>());
        bgGo.transform.SetParent(_barRoot, false);
        bgGo.transform.localPosition = Vector3.zero;
        bgGo.transform.localScale = new Vector3(barWidth, barHeight, 0.02f);
        var bgR = bgGo.GetComponent<Renderer>();
        bgR.material = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")) { color = BgColor };
        _bgMat = bgR.material;

        // 填充（绿色→红色，左对齐缩放）
        var fillGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fillGo.name = "Fill";
        Object.Destroy(fillGo.GetComponent<Collider>());
        fillGo.transform.SetParent(_barRoot, false);
        fillGo.transform.localPosition = Vector3.zero;
        fillGo.transform.localScale = new Vector3(barWidth - 0.03f, barHeight - 0.02f, 0.01f);
        var fillR = fillGo.GetComponent<Renderer>();
        fillR.material = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")) { color = FillGreen };
        _fillMat = fillR.material;
        _fillTf = fillGo.transform;
    }

    private void Update()
    {
        if (_health == null || _playerTf == null || _barRoot == null) return;

        // ===== 显示条件：玩家水平距离≤showRadius 且怪物存活 =====
        float dxz = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(_playerTf.position.x, _playerTf.position.z));
        bool shouldShow = dxz <= showRadius && !_health.IsDead;

        // ===== 淡入淡出：alpha渐进过渡 =====
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, shouldShow ? 1f : 0f, fadeSpeed * Time.deltaTime);
        ApplyAlpha(_currentAlpha);

        // 完全透明时隐藏物体（节省渲染），可见时确保激活
        if (_currentAlpha <= 0f)
        {
            if (_barRoot.gameObject.activeSelf) _barRoot.gameObject.SetActive(false);
            return;
        }
        if (!_barRoot.gameObject.activeSelf) _barRoot.gameObject.SetActive(true);

        // ===== 血量插值缓冲：显示比例渐进趋近实际比例 =====
        float targetRatio = _health.Max > 0 ? _health.Current / _health.Max : 0f;
        _displayRatio = Mathf.Lerp(_displayRatio, targetRatio, fillLerpSpeed * Time.deltaTime);

        // ===== 填充宽度与颜色 =====
        ApplyFill(_displayRatio);

        // ===== Billboard：朝向主相机 =====
        if (_cam != null)
        {
            _barRoot.rotation = _cam.transform.rotation;
        }

        // ===== 跟随敌人位置 =====
        _barRoot.position = transform.position + Vector3.up * headOffsetY;
    }

    /// <summary>设置填充宽度与颜色（按血量比例绿→红渐变）</summary>
    private void ApplyFill(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        float w = (barWidth - 0.04f) * ratio;
        _fillTf.localScale = new Vector3(w, barHeight - 0.02f, 0.01f);
        // 左对齐：填充从左边缘开始
        _fillTf.localPosition = new Vector3(-barWidth / 2f + w / 2f, 0f, 0f);
        _fillMat.color = Color.Lerp(FillRed, FillGreen, ratio);
    }

    /// <summary>设置背景与填充的透明度</summary>
    private void ApplyAlpha(float a)
    {
        if (_bgMat != null) { var c = _bgMat.color; c.a = a * 0.8f; _bgMat.color = c; }
        if (_fillMat != null) { var c = _fillMat.color; c.a = a; _fillMat.color = c; }
    }

    /// <summary>敌人销毁时清理血条物体</summary>
    private void OnDestroy()
    {
        if (_barRoot != null) Destroy(_barRoot.gameObject);
    }
}
