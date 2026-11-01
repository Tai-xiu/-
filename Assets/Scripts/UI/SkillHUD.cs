using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 技能栏HUD（技能系统机制设计v0.1：Q/E技能槽，自举DDOL挂常驻GameCanvas）
/// - 布局：快捷栏（HotbarBar_Spec，(32,32)起216宽）右侧16px，两格48×48格距8，2px像素描边
/// - 槽位构成：暗底+描边 / 技能图标（无图标=名称首字占位）/ 键位角标Q·E /
///   CD转环（黑色Radial360遮罩，后摇结束CD开始时满环随时间扫空）/ CD剩余秒数角标
/// - 状态：体力不足→图标灰显；施法前摇→槽位脉冲；无技能卡→整槽隐藏
/// - 数据源：当前场景Player的SkillExecutor（跨场景由sceneLoaded重绑；
///   主菜单无Player→整栏隐藏）
/// - 画布归属：优先GameManager常驻GameCanvas；主菜单期复用同名兜底画布，
///   进游戏后由Update迁移守卫迁回权威画布（防双画布参考分辨率错位重叠）
/// 挂载：RuntimeInitializeOnLoadMethod自举（InteractPromptUI同模式，零场景接线）
/// </summary>
public class SkillHUD : MonoBehaviour
{
    private const int SlotCount = 2;
    private const float SlotSize = 48f;
    private const float SlotGap = 8f;
    private static readonly Vector2 BarOrigin = new Vector2(264f, 32f); // 快捷栏右侧16px
    private static readonly Color32 SlotBg = new Color32(0x1A, 0x1A, 0x22, 0xFF);
    private static readonly Color32 OutlineCol = new Color32(0x00, 0x00, 0x00, 0xFF);
    private static readonly Color IconNormal = Color.white;
    private static readonly Color IconGray = new Color(0.4f, 0.4f, 0.4f, 1f);

    private static readonly string[] KeyLabels = { "Q", "E" };

    private RectTransform _root;
    private readonly Image[] _cdOverlays = new Image[SlotCount];
    private readonly Image[] _icons = new Image[SlotCount];
    private readonly TMPro.TextMeshProUGUI[] _cdTexts = new TMPro.TextMeshProUGUI[SlotCount];
    private readonly RectTransform[] _slotRects = new RectTransform[SlotCount];
    private readonly GameObject[] _slotGos = new GameObject[SlotCount];

    private SkillExecutor _skills;   // 每场景重绑（Player是场景对象，本组件DDOL）
    private bool _iconsBound;

    /// <summary>自举入口：UI制作场景跳过，进入游戏场景后按需补建</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= BootstrapOnSceneLoaded;
        SceneManager.sceneLoaded += BootstrapOnSceneLoaded;
        BootstrapOnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void BootstrapOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "UI" || UnityEngine.Object.FindObjectOfType<SkillHUD>() != null) return;
        var host = new GameObject("SkillHUD_Host", typeof(SkillHUD));
        DontDestroyOnLoad(host);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>场景切换：重绑当前场景Player的SkillExecutor（跨场景孤儿自清理）</summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // SkillBar 归 HUDRoot Prefab 所有，由 GameUIRefs 保证唯一实例；
        // 这里不再自删节点（旧实现会删掉 Prefab 里的 SkillBar_Spec）
        _skills = null;
        _iconsBound = false;
    }

    private void Update()
    {
        // 主菜单没有GameManager时不构建技能栏或兜底GameCanvas，
        // 让主菜单只保留场景中手动搭建的Canvas；进入游戏场景后下帧即可继续构建
        if (GameManager.Instance == null || GameManager.Instance.GameCanvas == null)
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            return;
        }

        // 注：旧"权威画布迁移守卫"已删除——SkillBar 现在是 HUDRoot Prefab 的固定子节点，
        // 不应再被 SetParent 到 GameCanvas（那会把节点从 HUDRoot 里拖走）。
        if (_root == null)
        {
            BindBar();
            RebindPlayer();
        }

        // 懒重绑：场景切换/Player未就绪时逐帧补取（命中后引用稳定）
        if (_skills == null)
        {
            RebindPlayer();
        }
        if (_skills == null)
        {
            if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false); // 主菜单无Player→隐藏
            return;
        }
        if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

        if (!_iconsBound) BindIcons();

        for (int i = 0; i < SlotCount; i++)
        {
            var skill = _skills.ActiveSkills[i];
            if (_slotGos[i] != null && _slotGos[i].activeSelf != (skill != null))
            {
                _slotGos[i].SetActive(skill != null); // 技能卡缺失→整槽隐藏
            }
            if (skill == null) continue;

            // CD转环：CD中=遮罩按剩余比例顺时针扫空；CD结束=遮罩归零
            bool onCd = _skills.IsOnCd(i);
            float rem = _skills.GetCdRemaining(i);
            float dur = _skills.GetCdDuration(i);
            if (_cdOverlays[i] != null)
            {
                _cdOverlays[i].fillAmount = onCd && dur > 0f ? rem / dur : 0f;
            }
            if (_cdTexts[i] != null)
            {
                _cdTexts[i].text = onCd && rem > 0.05f ? rem.ToString("0.0") : "";
            }

            // 图标灰显：体力不足时（CD中已有黑遮罩表达不可用，仅未CD时灰显区分原因）
            if (_icons[i] != null)
            {
                _icons[i].color = !_skills.HasEnoughStamina(i) && !onCd ? IconGray : IconNormal;
            }

            // 施法脉冲：本技能施法前摇→槽位轻微呼吸放大
            float targetScale = 1f;
            if (_skills.IsCasting && _skills.CastingSkill == skill && _skills.Phase == SkillExecutor.CastPhase.Windup)
            {
                targetScale = 1f + 0.06f * Mathf.Sin(Time.time * 20f);
            }
            if (_slotRects[i] != null)
            {
                var s = Vector3.one * targetScale;
                _slotRects[i].localScale = s;
            }
        }
    }

    /// <summary>重绑当前场景Player（Tag查找，主菜单无Player静默）</summary>
    private void RebindPlayer()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) { _skills = null; return; }
        _skills = player.GetComponent<SkillExecutor>();
        _iconsBound = false; // 新Player→重新按技能卡刷图标
    }

    /// <summary>按技能卡刷新槽位图标（技能卡运行时Resources加载，首次就绪后执行一次）</summary>
    private void BindIcons()
    {
        if (_skills == null) return;
        for (int i = 0; i < SlotCount; i++)
        {
            var skill = _skills.ActiveSkills[i];
            if (skill == null || _icons[i] == null) continue;
            if (skill.icon != null)
            {
                _icons[i].sprite = skill.icon;
                _icons[i].enabled = true;
                var t = _icons[i].GetComponentInChildren<Text>();
                if (t != null) Destroy(t.gameObject); // 有图标→移除文字占位
            }
            else
            {
                // 无图标→名称首字占位（一次性挂子Text）
                if (_icons[i].GetComponentInChildren<Text>() == null && !string.IsNullOrEmpty(skill.displayName))
                {
                    var go = new GameObject("FallbackText", typeof(Text));
                    go.transform.SetParent(_icons[i].transform, false);
                    var t = go.GetComponent<Text>();
                    t.text = skill.displayName.Substring(0, 1);
                    t.font = UIKit.LegacyFont;
                    t.fontSize = 22;
                    t.color = Color.white;
                    t.alignment = TextAnchor.MiddleCenter;
                    var rt = t.rectTransform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }
        }
        _iconsBound = true;
    }

    /// <summary>构建整栏（一次性；DDOL宿主下挂常驻GameCanvas）</summary>
    /// <summary>绑定 HUDRoot 里的 SkillBar 节点（不再创建 UI）</summary>
    private void BindBar()
    {
        var refs = GameUIRefs.Instance;
        if (refs == null || refs.Hud == null)
        {
            Debug.LogError("[SkillHUD] HUDRoot 未就绪");
            return;
        }
        _root = refs.Hud.Find("SkillBar_Spec") as RectTransform;
        if (_root == null)
        {
            Debug.LogError("[SkillHUD] SkillBar_Spec 缺失");
            return;
        }
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = _root.Find("SkillSlot_" + KeyLabels[i]) as RectTransform;
            if (slot == null) { Debug.LogError("[SkillHUD] 缺 SkillSlot_" + KeyLabels[i]); continue; }
            _slotRects[i] = slot;
            _slotGos[i] = slot.gameObject;

            var icon = slot.Find("Icon");
            _icons[i] = icon != null ? icon.GetComponent<Image>() : null;
            var cd = slot.Find("CdOverlay");
            _cdOverlays[i] = cd != null ? cd.GetComponent<Image>() : null;
            var cdt = slot.Find("CdText");
            _cdTexts[i] = cdt != null ? cdt.GetComponent<TMPro.TextMeshProUGUI>() : null;
        }
    }

}
