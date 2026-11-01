using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 统一 UI 引用入口（挂在 GameManager 上）：
/// - 保存各面板 Prefab 引用
/// - 在 Camp / Dungeon 把 HUDRoot 实例化到常驻 GameCanvas 下（只一次，跨场景复用）
/// - MainMenu / UI.scene 不实例化
/// - 自动按画布基准缩放：Prefab 按 1280×720 设计，画布基准同为 1280 时系数为 1；
///   将来若把 GameCanvas 换成 1920，系数自动变 1.5，Prefab 不用重做
/// </summary>
public class GameUIRefs : MonoBehaviour
{
    [Header("—— 面板 Prefab ——")]
    [Tooltip("HUD 根（血条/体力条/快捷栏/技能栏）")]
    public GameObject hudPrefab;

    [Tooltip("准星（屏幕正中十字）")]
    public GameObject crosshairPrefab;

    [Tooltip("交互提示（四角框 + 蓄力环 + 提示行）")]
    public GameObject interactPromptPrefab;

    public static GameUIRefs Instance { get; private set; }

    /// <summary>当前场景使用的 HUDRoot 实例</summary>
    public Transform Hud { get; private set; }

    /// <summary>准星实例</summary>
    public Transform Crosshair { get; private set; }

    /// <summary>交互提示实例</summary>
    public Transform InteractPrompt { get; private set; }

    const float DesignWidth = 1280f;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>确保 HUDRoot 存在（幂等）。canvas 为常驻 GameCanvas。</summary>
    public Transform EnsureHud(Canvas canvas)
    {
        if (canvas == null) return null;
        Instance = this;   // 以"存活的那个 GameManager"为准（重复实例销毁时会把 Instance 清空）

        // 已有实例且结构与 Prefab 一致 → 跨场景复用
        if (Hud != null && Hud.gameObject != null && Hud.parent == canvas.transform
            && hudPrefab != null && Hud.childCount == hudPrefab.transform.childCount)
            return Hud;

        // 清理旧 HUD 根 / 游离的旧技能栏（避免与 Prefab 版本并存）
        var existing = canvas.transform.Find("HUDRoot");
        if (existing != null)
        {
            Hud = null;
            Destroy(existing.gameObject);
        }
        var orphan = canvas.transform.Find("SkillBar_Spec");
        if (orphan != null) Destroy(orphan.gameObject);

        if (hudPrefab == null)
        {
            Debug.LogError("[GameUIRefs] hudPrefab 未绑定，HUD 不会出现");
            return null;
        }

        var go = Instantiate(hudPrefab, canvas.transform, false);
        go.name = "HUDRoot";
        ApplyScale(canvas, go.transform);
        Hud = go.transform;
        return Hud;
    }

    /// <summary>准星（幂等；Prefab 默认隐藏，由调用方控制显隐）</summary>
    public Transform EnsureCrosshair(Canvas canvas)
    {
        if (canvas == null) return null;
        Instance = this;
        if (Crosshair != null && Crosshair.gameObject != null && Crosshair.parent == canvas.transform) return Crosshair;
        var old = canvas.transform.Find("Crosshair");
        if (old != null) Destroy(old.gameObject);
        if (crosshairPrefab == null) { Debug.LogError("[GameUIRefs] crosshairPrefab 未绑定"); return null; }
        var go = Instantiate(crosshairPrefab, canvas.transform, false);
        go.name = "Crosshair";
        Crosshair = go.transform;
        return Crosshair;
    }

    /// <summary>交互提示（幂等）</summary>
    public Transform EnsureInteractPrompt(Canvas canvas)
    {
        if (canvas == null) return null;
        Instance = this;
        if (InteractPrompt != null && InteractPrompt.gameObject != null && InteractPrompt.parent == canvas.transform) return InteractPrompt;
        var old = canvas.transform.Find("InteractPrompt");
        if (old != null) Destroy(old.gameObject);
        if (interactPromptPrefab == null) { Debug.LogError("[GameUIRefs] interactPromptPrefab 未绑定"); return null; }
        var go = Instantiate(interactPromptPrefab, canvas.transform, false);
        go.name = "InteractPrompt";
        InteractPrompt = go.transform;
        return InteractPrompt;
    }

    /// <summary>设计尺寸 1920 → 画布基准宽度的补偿缩放</summary>
    private static void ApplyScale(Canvas canvas, Transform t)
    {
        var scaler = canvas.GetComponent<CanvasScaler>();
        float refW = scaler != null ? scaler.referenceResolution.x : DesignWidth;
        float k = refW / DesignWidth;
        t.localScale = new Vector3(k, k, 1f);
    }
}
