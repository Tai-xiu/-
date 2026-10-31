using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 快捷物品栏（HUD规格v0.1组件3）：血条/体力条堆叠底部的4格栏 + 食用通道
/// - 布局：体力条下方12px，4格×48px，格距8px，2px像素描边；空格=暗色槽位
/// - 分配：背包详情框快捷栏4格拖拽放置（Inventory.SetHotbarItem，按物品引用跟随，
///   背包内移动/堆叠不掉绑定）；拖回背包解除绑定；数量角标=背包内该物品总数
/// - 使用：数字键1~4触发吃食物通道——1.5s食用硬直（图标外进度圈），期间移动减速50%
///   （PlayerController读MoveSpeedFactor），被打断（受伤）需重新；完成时UseHotbar回血扣1，
///   图标弹跳0.2s+数量滚动减一
/// - 背包右键"使用"同走此通道（TryUseBagAnchor，InventoryUI菜单调用）：进度圈绕背包图标，
///   减速/打断/单通道与快捷栏共用；完成时UseItem(锚点)回血扣1
/// - 背包面板详情框顶部的快捷栏4格由InventoryUI构建（同数据源，拖拽交互）
/// 挂载：HUDController.Start自动补挂（与Stamina同模式，两场景Player通用零接线）
/// </summary>
public class HotbarController : MonoBehaviour
{
    public const int SlotCount = 4;
    public const float SlotSize = 48f;   // 单格尺寸
    public const float SlotGap = 8f;     // 格距

    [Header("—— 食用通道（规格：1.5s硬直，移动减速50%，受伤打断） ——")]
    [Tooltip("食用硬直时长（秒），完成后才真正使用物品")]
    [SerializeField] private float eatDuration = 1.5f;
    /// <summary>本次通道的实际硬直时长（开始时按物品设定，配置表v0.1）</summary>
    private float _eatDuration = 1.5f;

    [Tooltip("食用期间移动速度倍率（0.5=减速50%）")]
    [SerializeField] private float eatMoveSlow = 0.5f;

    private Inventory _inventory;
    private Health _health;
    private InventoryUI _invUI;     // 背包食用时查找物品覆盖层图标（进度圈挂载点）

    private RectTransform _root;
    private readonly Image[] _slotBgs = new Image[SlotCount];   // 槽位底（暗色+描边）
    private readonly GameObject[] _icons = new GameObject[SlotCount];   // 占位图标组（刷新重建）
    private readonly TMPro.TextMeshProUGUI[] _countTexts = new TMPro.TextMeshProUGUI[SlotCount];  // ×N数量角标（可滚动）
    private Image _ring;                                        // 食用进度圈（单通道，吃时挂到对应槽）
    private Coroutine _bounceRoutine;

    /// <summary>食用通道来源：HUD快捷栏槽 / 背包格（右键菜单）</summary>
    private enum EatMode { Hotbar, Bag }

    private bool _eating;             // 食用通道进行中
    private EatMode _eatMode = EatMode.Hotbar; // 通道来源（决定完成时走UseHotbar还是UseItem）
    private int _eatSlot = -1;        // 正在食用的快捷栏槽
    private int _eatBagAnchor = -1;   // 正在食用的背包锚点（Bag模式）
    private ItemData _eatBagItem;     // 锚点物品引用（背包变化时校验物品是否被移动）
    private Transform _eatBagIcon;    // 环挂载的背包覆盖层图标（重建后失效则由Update重挂）
    private float _eatElapsed;        // 已积累食用时间

    /// <summary>是否处于食用硬直中（PlayerController读此减速移动）</summary>
    public bool IsEating => _eating;

    /// <summary>移动速度倍率（食用中=eatMoveSlow，其余=1）</summary>
    public float MoveSpeedFactor => _eating ? eatMoveSlow : 1f;

    /// <summary>整栏尺寸（HUDController堆叠定位用）</summary>
    public Vector2 BarSize => new Vector2(SlotCount * SlotSize + (SlotCount - 1) * SlotGap, SlotSize);

    private void Start()
    {
        _inventory = GetComponent<Inventory>();
        _health = GetComponent<Health>();
        _invUI = GetComponent<InventoryUI>();
        if (_inventory == null)
        {
            Debug.LogError("[Hotbar] 缺少同物体上的Inventory组件");
            enabled = false;
            return;
        }

        var canvasRoot = GameManager.Instance != null && GameManager.Instance.GameCanvas != null
            ? GameManager.Instance.GameCanvas.transform
            : null;
        if (canvasRoot == null)
        {
            Debug.LogError("[Hotbar] 找不到GameCanvas，跳过构建");
            enabled = false;
            return;
        }
        var old = canvasRoot.Find("HotbarBar_Spec");
        if (old != null) Destroy(old.gameObject); // 跨场景防重

        BuildBar(canvasRoot);
        RefreshSlots();

        _inventory.Changed += OnInvChanged;         // 背包消耗/拾取→数量刷新+背包通道校验
        _inventory.HotbarChanged += RefreshSlots;   // 分配/取下→图标刷新
        if (_health != null) _health.onDamaged.AddListener(OnDamaged);
    }

    private void OnDestroy()
    {
        if (_inventory != null)
        {
            _inventory.Changed -= OnInvChanged;
            _inventory.HotbarChanged -= RefreshSlots;
        }
        if (_health != null) _health.onDamaged.RemoveListener(OnDamaged);
    }

    private void Update()
    {
        // 食用通道推进（优先于按键守卫：进行中的通道只受timeScale/受伤影响）
        if (_eating)
        {
            _eatElapsed += Time.deltaTime;
            // 背包食用：图标覆盖层随背包变化（拾取/数量变化都会重建）被连带销毁时，
            // 重挂进度圈到新图标（_ring被销毁后为Unity-null，重挂后恢复进度显示）
            if (_eatMode == EatMode.Bag && _eatBagAnchor >= 0
                && (_ring == null || _eatBagIcon == null)
                && _invUI != null && _invUI.TryGetOverlayIcon(_eatBagAnchor, out var icon))
            {
                _eatBagIcon = icon;
                ShowRing(icon, 64f);
            }
            if (_ring != null) _ring.fillAmount = Mathf.Clamp01(_eatElapsed / eatDuration);
            if (_eatElapsed >= _eatDuration)
            {
                CompleteEat();
            }
        }

        // 数字键1~4使用（暂停/面板打开时不响应；死亡在TryUseSlot内拦截）
        if (PauseUI.IsPaused || GameUIState.IsAnyPanelOpen) return;
        for (int i = 0; i < SlotCount; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
            {
                TryUseSlot(i);
            }
        }
    }

    /// <summary>
    /// 使用快捷栏槽位（数字键1~4入口，测试脚本亦可直调）：
    /// 空槽/无货/非消耗品/满血/死亡直接忽略；否则进入1.5s食用通道（受伤打断需重新）
    /// </summary>
    public void TryUseSlot(int slot)
    {
        if (_eating || slot < 0 || slot >= SlotCount) return;
        var item = _inventory.GetHotbarItem(slot);
        if (item == null || item.type != ItemType.Consumable) return; // 空槽/非食物
        if (_inventory.GetHotbarCount(slot) <= 0)
        {
            Debug.Log("[Hotbar] 快捷栏" + (slot + 1) + "（" + item.itemName + "）已无存货");
            return;
        }
        if (_health == null || _health.IsDead) return;
        if (_health.Current >= _health.Max && item.healStamina <= 0f)
        {
            Debug.Log("[Hotbar] 生命已满，无需使用 " + item.itemName);
            return;
        }

        _eating = true;
        _eatMode = EatMode.Hotbar;
        _eatSlot = slot;
        _eatElapsed = 0f;
        ShowRing(_slotBgs[slot].transform, 56f);
    }

    /// <summary>
    /// 背包"使用"入口（InventoryUI右键菜单调用）：与快捷栏同一条1.5s食用通道——
    /// 进度圈绕背包图标、移动减速50%、受伤/物品被移动打断不消耗。
    /// 返回null=通道已开始；否则为拒绝原因（满血/正在食用/死亡/非消耗品），由UI提示行显示
    /// </summary>
    public string TryUseBagAnchor(int anchor, Transform ringParent)
    {
        if (_eating) return "正在食用中，请稍候";
        var stack = _inventory.GetSlot(anchor);
        if (stack == null || stack.item == null || stack.item.type != ItemType.Consumable)
        {
            return "该物品无法食用";
        }
        if (_health == null || _health.IsDead) return "无法在死亡状态食用";
        if (_health.Current >= _health.Max && stack.item.healStamina <= 0f)
        {
            return "生命已满，无需使用 " + stack.item.itemName;
        }

        _eating = true;
        _eatMode = EatMode.Bag;
        _eatSlot = -1;
        _eatBagAnchor = anchor;
        _eatBagItem = stack.item;
        _eatBagIcon = ringParent;
        _eatElapsed = 0f;
        _eatDuration = stack.item.eatDuration; // 配置表v0.1
        if (ringParent != null) ShowRing(ringParent, 64f); // 背包格74px，圈稍大
        return null;
    }

    /// <summary>食用完成：真正使用物品（回血+扣1）+图标弹跳+数量滚动</summary>
    private void CompleteEat()
    {
        int slot = _eatSlot;
        bool bagMode = _eatMode == EatMode.Bag;
        int bagAnchor = _eatBagAnchor;
        ResetChannelState();
        HideRing();
        if (bagMode)
        {
            _inventory.UseItem(bagAnchor); // 背包路径：回血+扣1（Changed触发UI重建，环随旧图标销毁）
        }
        else
        {
            _inventory.UseHotbar(slot); // 内部UseItem：回血+RemoveAt→Changed→RefreshSlots
            if (_bounceRoutine != null) StopCoroutine(_bounceRoutine);
            _bounceRoutine = StartCoroutine(BounceRoutine(slot));
        }
    }

    /// <summary>受伤打断：取消通道（不消耗物品），进度圈隐藏，需重新使用</summary>
    private void OnDamaged(float amount)
    {
        if (!_eating) return;
        CancelEat("食用被打断，需重新使用");
    }

    /// <summary>取消食用通道（受伤/物品被移动）：不消耗物品，清空通道状态</summary>
    private void CancelEat(string reason)
    {
        ResetChannelState();
        HideRing();
        Debug.Log("[Hotbar] " + reason);
    }

    /// <summary>清空通道状态（_eating=false与全部来源字段复位）</summary>
    private void ResetChannelState()
    {
        _eating = false;
        _eatMode = EatMode.Hotbar;
        _eatSlot = -1;
        _eatBagAnchor = -1;
        _eatBagItem = null;
        _eatBagIcon = null;
        _eatElapsed = 0f;
    }

    /// <summary>背包变化：数量刷新 + 背包食用中校验锚点物品是否被移动/移除（是则打断）</summary>
    private void OnInvChanged()
    {
        RefreshSlots();
        if (_eating && _eatMode == EatMode.Bag)
        {
            var s = _inventory.GetSlot(_eatBagAnchor);
            if (s == null || s.item != _eatBagItem)
            {
                CancelEat("食用被打断（物品被移动）");
            }
        }
    }

    /// <summary>图标弹跳0.2s（1→1.18→1正弦缩放）</summary>
    private IEnumerator BounceRoutine(int slot)
    {
        var icon = _icons[slot];
        if (icon == null) yield break;
        var rt = (RectTransform)icon.transform;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f)
        {
            float s = 1f + 0.18f * Mathf.Sin(Mathf.PI * t);
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        rt.localScale = Vector3.one;
        _bounceRoutine = null;
    }

    // ===== UI构建 =====

    /// <summary>构建4格栏（PixelKit）：槽位 sprite + 像素字体数量角标</summary>
    private void BuildBar(Transform canvasRoot)
    {
        const float s = UIKit.LegacyScale;
        canvasRoot = UIKit.PixelCanvas().transform;

        _root = new GameObject("HotbarBar_Spec", typeof(RectTransform)).GetComponent<RectTransform>();
        _root.SetParent(canvasRoot, false);
        _root.anchorMin = new Vector2(0f, 0f);
        _root.anchorMax = new Vector2(0f, 0f);
        _root.pivot = new Vector2(0f, 0f);
        _root.anchoredPosition = barMarginConst * s;
        _root.sizeDelta = BarSize * s;

        float slotPx = SlotSize * s;
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = UIKit.Img(_root, "Slot_" + (i + 1), UIKit.Slot,
                new Vector2(slotPx, slotPx), new Vector2(i * (SlotSize + SlotGap) * s, 0f));
            var rt = slot.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            _slotBgs[i] = slot;

            // ×N 数量角标（右下）
            var count = UIKit.Txt(slot.transform, "Count", "", 14, Color.white, TextAnchor.LowerRight);
            var crt = count.rectTransform;
            crt.anchorMin = new Vector2(1f, 0f);
            crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(1f, 0f);
            crt.sizeDelta = new Vector2(44f, 18f);
            crt.anchoredPosition = new Vector2(-3f, 3f);
            _countTexts[i] = count;
        }
    }

    /// <summary>刷新4槽图标与数量（分配变化/背包变化时全量重建图标组）</summary>
    private void RefreshSlots()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_icons[i] != null)
            {
                Destroy(_icons[i]);
                _icons[i] = null;
            }
            var item = _inventory.GetHotbarItem(i);
            int count = _inventory.GetHotbarCount(i);
            if (item == null || count <= 0)
            {
                _countTexts[i].text = ""; // 空槽/无货：只显示暗色槽位
                continue;
            }
            // 虚拟堆count=1：角标由独立countText绘制（总数跨堆叠统计，且支持滚动动画）
            var virt = new Inventory.ItemStack { item = item, count = 1, anchorRow = 0, anchorCol = 0, orient = ItemOrient.Horizontal };
            _icons[i] = InventoryUI.BuildIconGroup(virt, new Vector2(40f, 40f), ItemOrient.Horizontal, false, false);
            _icons[i].name = "HotIcon_" + (i + 1);
            _icons[i].transform.SetParent(_slotBgs[i].transform, false);
            var rt = (RectTransform)_icons[i].transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            _countTexts[i].text = count > 1 ? "×" + count : "×1";
        }
    }

    // ===== 食用进度圈 =====

    private static Sprite _ringSprite;

    /// <summary>白色圆环贴图（96px，环半径40厚8，边缘1px抗锯齿），Radial360填充做进度圈</summary>
    private static Sprite RingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        const int n = 96;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        float c = (n - 1) * 0.5f;
        float outer = 44f, inner = 36f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Clamp01(Mathf.Min(d - (inner - 0.5f), (outer + 0.5f) - d));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 48f);
        return _ringSprite;
    }

    /// <summary>在正在食用的图标上显示进度圈（Radial360顶点起顺时针填充；快捷栏槽56px/背包格64px）</summary>
    private void ShowRing(Transform parent, float size)
    {
        if (_ring == null)
        {
            var go = new GameObject("EatRing", typeof(RectTransform), typeof(Image));
            var img = go.GetComponent<Image>();
            img.sprite = RingSprite();
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.raycastTarget = false;
            _ring = img;
        }
        _ring.transform.SetParent(parent, false);
        var rt2 = _ring.rectTransform;
        rt2.anchorMin = rt2.anchorMax = new Vector2(0.5f, 0.5f);
        rt2.pivot = new Vector2(0.5f, 0.5f);
        rt2.anchoredPosition = Vector2.zero;
        rt2.sizeDelta = new Vector2(size, size);
        _ring.fillAmount = 0f;
        _ring.gameObject.SetActive(true);
    }

    /// <summary>隐藏进度圈</summary>
    private void HideRing()
    {
        if (_ring != null) _ring.gameObject.SetActive(false);
    }

    /// <summary>堆叠底边边距（与HUDController.barMargin一致；常量避免互相依赖）</summary>
    private static Vector2 barMarginConst => new Vector2(32f, 32f);
}
