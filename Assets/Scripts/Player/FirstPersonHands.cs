using System.Collections;
using UnityEngine;

/// <summary>
/// 第一人称攻击动画挂接（V2：动画本体=场景手动搭建的Animator Controller，本组件只负责触发与显隐）
/// ─────────────────────────────────────────────
/// punchRoot（拳头节点，相机子级"Punch"）：Animator播Hands_Punch——
///     仅攻击期间激活显现，动画播完（=一次完整攻击）自动隐藏；
/// swordRoot（持剑节点，相机子级"Knife"）：Animator播Hands_Slash——
///     装备武器时常驻右手边，攻击时从头重播横劈，无武器时隐藏；
/// 拳/剑切换由Inventory.Changed驱动；PlayerCombat在DoAttack时调用PlayAttack。
/// 动画剪辑与关键帧全部在编辑器Animation窗口手动制作，本组件不参与动作插值
/// </summary>
public class FirstPersonHands : MonoBehaviour
{
    [Header("—— 场景引用（留空则按名查找相机子级Punch/Knife） ——")]
    [Tooltip("空手拳头节点（含Animator，播Hands_Punch）")]
    [SerializeField] private GameObject punchRoot;
    [Tooltip("持剑节点（含Animator，播Hands_Slash）")]
    [SerializeField] private GameObject swordRoot;

    [Header("—— 动画状态名（与Controller内状态一致） ——")]
    [SerializeField] private string punchState = "Hands_Punch";

    private Inventory _inventory;   // 装备状态来源（同物体）
    private Animator _punchAnimator;
    private Animator _swordAnimator;
    private float _punchLength = 0.42f; // 出拳动画时长（Awake从Controller读取）
    private float _slashLength = 0.52f; // 横劈动画时长（Awake从Controller读取）
    private Coroutine _hideRoutine;     // 播完延迟处理协程（连打时重启）

    private void Awake()
    {
        var cam = GetComponentInChildren<Camera>();
        if (cam == null)
        {
            Debug.LogError("[FirstPersonHands] 未找到第一人称相机（Player子级），组件禁用");
            enabled = false;
            return;
        }

        // 场景引用未手动指定时按名查找相机子级（Camp/Dungeon节点命名约定）
        if (punchRoot == null)
        {
            var t = cam.transform.Find("Punch");
            if (t != null) punchRoot = t.gameObject;
        }
        if (swordRoot == null)
        {
            var t = cam.transform.Find("Knife");
            if (t != null) swordRoot = t.gameObject;
        }
        if (punchRoot == null && swordRoot == null)
        {
            Debug.LogError("[FirstPersonHands] 相机下未找到Punch/Knife节点（该场景未配置手部动画），组件禁用");
            enabled = false;
            return;
        }

        if (punchRoot != null)
        {
            _punchAnimator = punchRoot.GetComponent<Animator>();
            var ctrl = _punchAnimator != null ? _punchAnimator.runtimeAnimatorController : null;
            if (ctrl != null && ctrl.animationClips.Length > 0)
            {
                _punchLength = ctrl.animationClips[0].length; // 播完自动隐藏的等待时长
            }
            punchRoot.SetActive(false); // 拳头默认不显现（仅出拳期间显示）
        }

        if (swordRoot != null)
        {
            _swordAnimator = swordRoot.GetComponent<Animator>();
            var ctrl = _swordAnimator != null ? _swordAnimator.runtimeAnimatorController : null;
            if (ctrl != null && ctrl.animationClips.Length > 0)
            {
                _slashLength = ctrl.animationClips[0].length; // 横劈播完冻结的等待时长
            }
            // 剑的Animator默认冻结：若Clip勾了Loop Time，启用状态会不停循环挥砍——
            // 冻结后定格在节点摆好的"常驻右手边"姿态，攻击时才恢复播放
            _swordAnimator.enabled = false;
        }

        _inventory = GetComponent<Inventory>();
        if (_inventory != null)
        {
            _inventory.Changed += RefreshHeldVisual;
        }

        RefreshHeldVisual();
    }

    private void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.Changed -= RefreshHeldVisual;
        }
    }

    /// <summary>
    /// 按装备状态切换拳/剑显隐：拳头只在出拳动画期间显现；
    /// 剑装备时常驻右手边、无武器时隐藏
    /// </summary>
    private void RefreshHeldVisual()
    {
        bool hasWeapon = _inventory != null && _inventory.EquippedWeapon != null;
        StopHideRoutine();
        if (punchRoot != null) punchRoot.SetActive(false);
        if (swordRoot != null) swordRoot.SetActive(hasWeapon);
        // 剑的Animator回到冻结态：定格在场景摆好的常驻姿态（Clip若是Loop也不会自行挥动）
        if (_swordAnimator != null) _swordAnimator.enabled = false;
    }

    /// <summary>
    /// 触发一次攻击动画（PlayerCombat在DoAttack时调用）：
    /// 有武器=持剑横劈（Animator已冻结，Stop+Play保证每次都从头重播完整一击，播完再冻结收势）；
    /// 无武器=激活拳头播出拳（激活即重置动画），播完自动隐藏
    /// </summary>
    public void PlayAttack(bool hasWeapon)
    {
        if (hasWeapon)
        {
            StopHideRoutine();
            if (punchRoot != null) punchRoot.SetActive(false);
            if (_swordAnimator != null)
            {
                // 同状态Play/暂停恢复都不会重播（normalizedTime持续累积）——
                // 用Rebind硬复位状态机：回默认态(Hands_Slash)开头，从头播完整一击
                _swordAnimator.enabled = true;
                _swordAnimator.Rebind();
                _hideRoutine = StartCoroutine(FreezeSlashAfter(_slashLength));
            }
        }
        else
        {
            if (punchRoot == null || _punchAnimator == null) return;
            if (swordRoot != null) swordRoot.SetActive(false); // 空手时剑必然隐藏（兜底）
            punchRoot.SetActive(true);                          // 出拳期间拳头显现（激活即动画从头播）
            _punchAnimator.Play(punchState, 0, 0f);
            StopHideRoutine();
            _hideRoutine = StartCoroutine(HidePunchAfter(_punchLength));
        }
    }

    /// <summary>出拳动画播完（一次完整攻击结束）后隐藏拳头</summary>
    private IEnumerator HidePunchAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (punchRoot != null) punchRoot.SetActive(false);
        _hideRoutine = null;
    }

    /// <summary>横劈播完后冻结Animator：定格在收势姿态=不攻击时武器常驻右手边</summary>
    private IEnumerator FreezeSlashAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_swordAnimator != null) _swordAnimator.enabled = false;
        _hideRoutine = null;
    }

    private void StopHideRoutine()
    {
        if (_hideRoutine != null)
        {
            StopCoroutine(_hideRoutine);
            _hideRoutine = null;
        }
    }
}
