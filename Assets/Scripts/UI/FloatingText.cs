using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 世界坐标漂浮文字（uGUI Text占位）
/// 用法：FloatingText.Show("获得：铁剑 x1", 宝箱上方世界坐标, 2秒)
/// 宿主物体DontDestroyOnLoad：保证清理协程跨场景存活（修复提示文字跨场景残留问题）；
/// 同时监听场景加载事件：新场景载入时立即清理上一场景的提示文字
/// </summary>
public class FloatingText : MonoBehaviour
{
    private GameObject _labelGo;

    /// <summary>在worldPos处显示text，duration秒后自动销毁</summary>
    public static void Show(string text, Vector3 worldPos, float duration = 2f)
    {
        var host = new GameObject("FloatingTextHost").AddComponent<FloatingText>();
        DontDestroyOnLoad(host); // 宿主跨场景存活，保证清理协程执行完毕（2秒后自毁）
        host.StartCoroutine(host.Run(text, worldPos, duration));
    }

    private void OnEnable()
    {
        // 场景加载时立即清理上一场景的提示文字（宿主与文字一并销毁）
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_labelGo != null) Destroy(_labelGo);
        Destroy(gameObject);
    }

    private IEnumerator Run(string text, Vector3 worldPos, float duration)
    {
        Canvas canvas = EnsureCanvas();

        _labelGo = new GameObject("FloatingTextLabel", typeof(Text));
        _labelGo.transform.SetParent(canvas.transform, false);
        var t = _labelGo.GetComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 22;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(1f, 0.95f, 0.6f, 1f); // 淡黄色
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var rt = t.rectTransform;
        rt.sizeDelta = new Vector2(620, 40);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // 世界坐标上浮 → 屏幕投影
            worldPos += Vector3.up * (Time.deltaTime * 0.6f);
            rt.position = Camera.main.WorldToScreenPoint(worldPos);

            // 后半段线性淡出
            var c = t.color;
            c.a = 1f - Mathf.Clamp01((elapsed - duration * 0.5f) / (duration * 0.5f));
            t.color = c;

            yield return null;
        }

        Destroy(_labelGo);
        Destroy(gameObject);
    }

    private Canvas EnsureCanvas()
    {
        // 统一使用GameManager的常驻UI根画布（避免漂浮文字挂到会被销毁的传送黑屏/场景画布上）
        if (GameManager.Instance != null && GameManager.Instance.GameCanvas != null)
        {
            return GameManager.Instance.GameCanvas;
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
        }
        // UI根画布常驻：跨场景不销毁（漂浮文字跨场景仍可显示）
        DontDestroyOnLoad(canvas.gameObject);
        return canvas;
    }
}
