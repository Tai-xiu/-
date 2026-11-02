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

    private EnemyHealthBarVisual _visual;
    private Vector3 _fillFullScale, _fillFullPosition;
    private Color _fullColor;
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
        _visual = Instantiate(Resources.Load<EnemyHealthBarVisual>("UI/Panels/EnemyHealthBar"));
        _barRoot = _visual.transform; _barRoot.name="HPBar_"+gameObject.name;
        // 保留原有逐怪物尺寸配置，同时允许模板编辑整体和子节点外观。
        _barRoot.localScale=Vector3.Scale(_barRoot.localScale,new Vector3(barWidth/1.3f,barHeight/.2f,1));
        _bgMat=_visual.background.material; _fillMat=_visual.fill.material;
        _fillTf=_visual.fill.transform; _fillFullScale=_fillTf.localScale;
        _fillFullPosition=_fillTf.localPosition; _fullColor=_fillMat.color;
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
        float w = _fillFullScale.x * ratio;
        _fillTf.localScale = new Vector3(w, _fillFullScale.y, _fillFullScale.z);
        // 左对齐：填充从左边缘开始
        _fillTf.localPosition = _fillFullPosition + Vector3.left * ((_fillFullScale.x - w) * .5f);
        _fillMat.color = Color.Lerp(_visual.lowHealthColor, _fullColor, ratio);
    }

    /// <summary>设置背景与填充的透明度</summary>
    private void ApplyAlpha(float a)
    {
        if (_bgMat != null) { var c = _bgMat.color; c.a = a * _visual.backgroundOpacity; _bgMat.color = c; }
        if (_fillMat != null) { var c = _fillMat.color; c.a = a; _fillMat.color = c; }
    }

    /// <summary>敌人销毁时清理血条物体</summary>
    private void OnDestroy()
    {
        if (_barRoot != null) Destroy(_barRoot.gameObject);
        if (_bgMat != null) Destroy(_bgMat);
        if (_fillMat != null) Destroy(_fillMat);
    }
}
