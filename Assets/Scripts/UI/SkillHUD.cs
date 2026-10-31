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
    private readonly Text[] _cdTexts = new Text[SlotCount];
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
        var stale = transform.Find("SkillBar_Spec");
        if (stale != null) Destroy(stale.gameObject); // 跨场景重建防重
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

        // 权威画布迁移守卫：进Camp/Dungeon后GameManager的常驻GameCanvas就绪，
        // 把栏从主菜单期复用的兜底画布迁过去（归一到HUD同画布，杜绝双画布错位）
        if (_root != null && GameManager.Instance != null && GameManager.Instance.GameCanvas != null
            && _root.parent != GameManager.Instance.GameCanvas.transform)
        {
            _root.SetParent(GameManager.Instance.GameCanvas.transform, false);
        }

        if (_root == null)
        {
            BuildBar();
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
                    t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
    private void BuildBar()
    {
        // 画布归属三级选择：GameManager常驻GameCanvas优先 → 复用场景已自建的同名常驻画布 →
        // 兜底自建。主菜单开局时GameManager尚未运行，若直接私建画布，参考分辨率与游戏内
        // GameCanvas(1280×720)不一致，进Camp后技能槽会缩放错位压到快捷栏下（实测踩坑）
        Transform canvasRoot = null;
        if (GameManager.Instance != null && GameManager.Instance.GameCanvas != null)
        {
            canvasRoot = GameManager.Instance.GameCanvas.transform;
        }
        if (canvasRoot == null)
        {
            var existing = GameObject.Find("GameCanvas"); // 主菜单等兜底自建的常驻画布同名
            if (existing != null && existing.GetComponent<Canvas>() != null)
                canvasRoot = existing.transform;
        }
        if (canvasRoot == null)
        {
            // 兜底自建常驻画布（与HUDController同策略，理论上不触发）
            var go = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = go.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasRoot = go.transform;
            DontDestroyOnLoad(go);
        }

        _root = new GameObject("SkillBar_Spec", typeof(RectTransform)).GetComponent<RectTransform>();
        _root.SetParent(canvasRoot, false);
        _root.anchorMin = new Vector2(0f, 0f);
        _root.anchorMax = new Vector2(0f, 0f);
        _root.pivot = new Vector2(0f, 0f);
        _root.anchoredPosition = BarOrigin;
        _root.sizeDelta = new Vector2(SlotCount * SlotSize + (SlotCount - 1) * SlotGap, SlotSize);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = new GameObject("SkillSlot_" + KeyLabels[i], typeof(RectTransform), typeof(Image), typeof(Outline));
            slot.transform.SetParent(_root, false);
            var rt = slot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(i * (SlotSize + SlotGap), 0f);
            rt.sizeDelta = new Vector2(SlotSize, SlotSize);
            slot.GetComponent<Image>().color = SlotBg;
            var ol = slot.GetComponent<Outline>();
            ol.effectColor = OutlineCol;
            ol.effectDistance = new Vector2(2f, -2f); // 2px像素描边（HUD风格规范）
            _slotRects[i] = rt;
            _slotGos[i] = slot;

            // 图标（Sprite由技能卡绑定；无图标时 BindIcons 挂首字Text）
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(slot.transform, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(SlotSize - 12f, SlotSize - 12f);
            irt.anchoredPosition = Vector2.zero;
            var img = iconGo.GetComponent<Image>();
            img.raycastTarget = false; // 不挡事件
            img.preserveAspect = true;
            _icons[i] = img;

            // 键位角标（左上）
            var keyGo = new GameObject("KeyLabel", typeof(RectTransform), typeof(Text));
            keyGo.transform.SetParent(slot.transform, false);
            var krt = keyGo.GetComponent<RectTransform>();
            krt.anchorMin = new Vector2(0f, 1f);
            krt.anchorMax = new Vector2(0f, 1f);
            krt.pivot = new Vector2(0f, 1f);
            krt.anchoredPosition = new Vector2(3f, -2f);
            krt.sizeDelta = new Vector2(20f, 16f);
            var kt = keyGo.GetComponent<Text>();
            kt.text = KeyLabels[i];
            kt.font = font;
            kt.fontSize = 13;
            kt.color = new Color32(0xF5, 0xC5, 0x18, 0xFF); // 体力黄（键位与资源同色系）
            kt.alignment = TextAnchor.UpperLeft;
            kt.raycastTarget = false;

            // CD黑色转环遮罩（Radial360顶点顺时针，InteractPromptUI进度环同手法）
            var cdGo = new GameObject("CdOverlay", typeof(RectTransform), typeof(Image));
            cdGo.transform.SetParent(slot.transform, false);
            var crt = cdGo.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var cdImg = cdGo.GetComponent<Image>();
            cdImg.color = new Color(0f, 0f, 0f, 0.65f);
            cdImg.type = Image.Type.Filled;
            cdImg.fillMethod = Image.FillMethod.Radial360;
            cdImg.fillOrigin = (int)Image.Origin360.Top;
            cdImg.fillClockwise = true;
            cdImg.fillAmount = 0f;
            cdImg.raycastTarget = false;
            _cdOverlays[i] = cdImg;

            // CD剩余秒数（右下小字）
            var cdTextGo = new GameObject("CdText", typeof(RectTransform), typeof(Text));
            cdTextGo.transform.SetParent(slot.transform, false);
            var trt = cdTextGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(1f, 0f);
            trt.anchorMax = new Vector2(1f, 0f);
            trt.pivot = new Vector2(1f, 0f);
            trt.anchoredPosition = new Vector2(-3f, 2f);
            trt.sizeDelta = new Vector2(30f, 14f);
            var tt = cdTextGo.GetComponent<Text>();
            tt.font = font;
            tt.fontSize = 13;
            tt.color = Color.white;
            tt.alignment = TextAnchor.LowerRight;
            tt.raycastTarget = false;
            _cdTexts[i] = tt;
        }
    }
}
