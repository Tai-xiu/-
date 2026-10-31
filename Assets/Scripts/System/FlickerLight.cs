using UnityEngine;

/// <summary>
/// 火光闪烁（营火/火把通用）：对点光源的强度与范围做低频Perlin噪声微扰。
/// 设计要点：
/// 1. 用Perlin噪声而非随机抖动——明暗缓变连续，更像火焰摇曳而非频闪；
/// 2. 幅度默认±15%（0.15），频率可调（默认0.9，值越大闪烁越快）；
/// 3. 相位：phaseOffset<0时按GetInstanceID自动派生——场景里多盏灯互不同步，
///    营火与火把天然错相，无需逐灯手工配置；
/// 4. Awake记录初始强度/范围，重复启用不会累积漂移；组件禁用时灯光保持原值。
/// 用法：挂在与点光源同一物体上即可（营火半径6、火把半径4各自配好初始值）。
/// </summary>
public class FlickerLight : MonoBehaviour
{
    [Header("闪烁幅度（0~1，相对初始值的扰动比例）")]
    [SerializeField] private float amplitude = 0.15f;

    [Header("噪声频率（值越大闪烁越快）")]
    [SerializeField] private float frequency = 0.9f;

    [Header("相位偏移（<0=按实例ID自动派生，保证多灯不同步）")]
    [SerializeField] private float phaseOffset = -1f;

    [Header("范围是否随强度一起扰动（幅度减半，避免光圈跳变过强）")]
    [SerializeField] private bool flickerRange = true;

    private Light _light;
    private float _baseIntensity;
    private float _baseRange;
    private float _phase;

    private void Awake()
    {
        _light = GetComponent<Light>();
        if (_light == null)
        {
            enabled = false; // 没有灯可闪，自禁用避免空转
            return;
        }
        _baseIntensity = _light.intensity;
        _baseRange = _light.range;
        // 自动相位：实例ID散列到0~173弧度段，同帧创建的多盏灯也互不同步
        _phase = phaseOffset >= 0f ? phaseOffset : (Mathf.Abs(GetInstanceID()) % 1000) * 0.173f;
    }

    private void Update()
    {
        if (_light == null) return;
        // Perlin噪声映射到-1~1：低频缓变的双向扰动
        float n = Mathf.PerlinNoise(_phase, Time.time * frequency) * 2f - 1f;
        _light.intensity = _baseIntensity * (1f + amplitude * n);
        if (flickerRange) _light.range = _baseRange * (1f + amplitude * 0.5f * n);
    }
}
