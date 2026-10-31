using System.Collections;
using UnityEngine;

/// <summary>
/// 玩家受击反馈（白盒原型阶段）
/// 监听自身Health组件的受伤事件：受伤时模型材质短暂变红，recoverTime秒后恢复原色
/// </summary>
[RequireComponent(typeof(Health))]
public class PlayerHitFeedback : MonoBehaviour
{
    [Header("—— 受击反馈 ——")]
    [Tooltip("受击时变红的颜色")]
    [SerializeField] private Color hitColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Tooltip("恢复原色的时长（秒）")]
    [SerializeField] private float recoverTime = 0.2f;

    private Health _health;
    private MeshRenderer _renderer;
    private Color _originalColor;
    private Coroutine _flashRoutine;

    private void Awake()
    {
        _health = GetComponent<Health>();
        // 白盒阶段模型为单个胶囊体，取第一个网格渲染器
        _renderer = GetComponentInChildren<MeshRenderer>();
        if (_renderer != null)
        {
            // 访问.material会生成材质实例（一次性开销），恢复时写回原色
            _originalColor = _renderer.material.color;
        }
    }

    private void OnEnable()
    {
        if (_health != null) _health.onDamaged.AddListener(OnDamaged);
    }

    private void OnDisable()
    {
        if (_health != null) _health.onDamaged.RemoveListener(OnDamaged);
    }

    /// <summary>
    /// 受伤回调：启动变红流程（连续受击时重置上一轮计时）
    /// </summary>
    private void OnDamaged(float amount)
    {
        if (_renderer == null) return;

        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
        }
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        _renderer.material.color = hitColor;
        yield return new WaitForSeconds(recoverTime);
        _renderer.material.color = _originalColor;
        _flashRoutine = null;
    }
}
