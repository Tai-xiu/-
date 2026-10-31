using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 开箱宝箱（白模测试版）：2~6件逐件"光柱→延迟→物品"串行（每件独立品质，可混开）+ 无碰撞抛物线toss + F拾取
/// - 外观：方块白模（编辑器摆放，Awake统一染棕色）
/// - 交互：准星瞄准制（射线命中宝箱碰撞体任意部位即指向，遮挡天然生效）——
///   在交互半径内长按F 2秒开启（进度环绕准星径向填充；松键/移开视线/离开范围/受伤打断
///   需重新长按，打断时环变红淡出=打断反馈——均由InteractPromptUI绘制）；
///   InRange/Aimed状态与注册表供InteractPromptUI屏幕正中提示/方框（世界空间重复提示已移除）
/// - 节奏：2~6件物品逐件独立roll品质（可能出现不同品质混开）→ 逐件串行：
///   每件先出该品质光柱 → 延迟①(光柱→物品，品质越高越久：白蓝0.5s/紫蓝1.4s/紫2.4s) →
///   物品弹出 → 延迟②(本件→下一件光柱，品质越高越久：白蓝0.3s/紫蓝0.8s/紫1.5s) → 下一件……
///   一道光柱只对应一件物品
/// - 弹出：掉落小方块无碰撞体（用户要求），程序化抛物线toss+射线找落点，
///   落地即停——彻底规避刚体隧穿/去重叠爆炸（实测踩坑）
/// - 拾取：弹出的方块由LootItemCube处理（靠近按F确认拾取，避免开箱瞬间误吸）
/// - 测试入口：TryOpen()公开（直接开箱）、TickHold(bool)公开（直驱蓄力——旧Input
///   Manager无法注入按键，测试脚本禁用本组件后逐帧调用即可模拟长按）
/// </summary>
public class LootChest : MonoBehaviour
{
    [Header("—— 战利品配置 ——")]
    [Tooltip("配置表v0.2掉落表（挂表后按表独立概率roll，优先于品质池；表六宝箱列：小袋80/大袋40/蓝晶石15/红榴石8/蓝装10/紫装5）")]
    [SerializeField] private MonsterLootTable lootTable;
    [Tooltip("白蓝品质物品池")]
    [SerializeField] private ItemData[] lootBlueWhite;
    [Tooltip("紫蓝品质物品池")]
    [SerializeField] private ItemData[] lootVioletBlue;
    [Tooltip("紫品质物品池")]
    [SerializeField] private ItemData[] lootPurple;
    [Tooltip("掉落件数下限")]
    [SerializeField] private int countMin = 2;
    [Tooltip("掉落件数上限")]
    [SerializeField] private int countMax = 6;
    [Tooltip("品质权重 x=白蓝 y=紫蓝 z=紫（每件物品独立roll，可混出不同品质）")]
    [SerializeField] private Vector3 rarityWeights = new Vector3(55f, 30f, 15f);

    [Header("—— 开箱动画（正式模型用，白模可留空） ——")]
    [Tooltip("开箱动画的Animator（留空=白模压暗表现）。Start时自动禁用防止默认状态提前播放")]
    [SerializeField] private Animator openAnimator;
    [Tooltip("开箱动画状态名（Animator默认状态名）")]
    [SerializeField] private string openStateName = "Open";
    [Tooltip("是否套用白模棕色外观（正式模型关闭）")]
    [SerializeField] private bool whiteboxTint = false;

    [Header("—— 交互 ——")]
    [Tooltip("交互半径（米）")]
    [SerializeField] private float interactRadius = 2.4f;
    [Tooltip("交互键（开启宝箱）")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [Tooltip("长按开启时长（秒）：按住交互键蓄力，中途松键/移开视线/离开范围/受伤打断需重新")]
    [SerializeField] private float holdDuration = 2f;

    /// <summary>是否已开启（公开只读，测试断言用）</summary>
    public bool IsOpen { get; private set; }

    // —— 可交互注册表：InteractPromptUI汇集屏幕正中提示/方框目标（注册制避免每帧全场景扫描） ——
    private static readonly System.Collections.Generic.List<LootChest> _all
        = new System.Collections.Generic.List<LootChest>();

    /// <summary>玩家是否处于交互半径内（Update每帧刷新；已开启/暂停时复位false）</summary>
    public bool InRange { get; private set; }

    /// <summary>准星是否指向本宝箱（Update每帧刷新；夹角≤aimAngle且在交互半径内，
    /// 与LootItemCube瞄准判定同口径）</summary>
    public bool Aimed { get; private set; }

    /// <summary>交互半径（米；InteractPromptUI射线距离与距离排序用）</summary>
    public float InteractRadius { get { return interactRadius; } }

    /// <summary>全场景宝箱只读视图</summary>
    public static System.Collections.Generic.IReadOnlyList<LootChest> All { get { return _all; } }

    /// <summary>是否正在长按蓄力（TickHold每帧刷新）</summary>
    public bool IsHolding { get; private set; }

    /// <summary>长按进度0~1（InteractPromptUI进度环用；未蓄力=0）</summary>
    public float HoldProgress { get; private set; }

    /// <summary>长按被打断（松键/离开范围/受伤；参数=打断时刻进度，InteractPromptUI据此红闪反馈）</summary>
    public static event System.Action<float> HoldInterrupted;

    /// <summary>当前蓄力中的宝箱（InteractPromptUI进度环数据源；注册表不覆盖组件禁用态，
    /// 测试脚本禁用组件直驱TickHold时进度环仍工作）</summary>
    public static LootChest CurrentHold { get { return _currentHold; } }
    private static LootChest _currentHold;

    private float _holdElapsed;
    private BoxCollider _boxCollider;
    private Health _playerHealth;
    private bool _damageHooked;

    private void OnEnable()
    {
        _all.Add(this);
    }

    private void OnDisable()
    {
        _all.Remove(this);
        if (_currentHold == this) _currentHold = null;
    }

    private void OnDestroy()
    {
        if (_damageHooked && _playerHealth != null)
        {
            _playerHealth.onDamaged.RemoveListener(OnPlayerDamaged);
        }
    }

    private Transform _player;

    private void Start()
    {
        // 白模棕色外观（仅白模宝箱启用；正式模型由美术资产自带外观）
        if (whiteboxTint)
        {
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                r.material.color = new Color(0.55f, 0.4f, 0.25f, 1f);
            }
        }
        // 开箱动画由Animator驱动：Start先禁用，防止控制器默认状态在开箱前播放（盖子提前打开）
        if (openAnimator != null) openAnimator.enabled = false;
        _boxCollider = GetComponent<BoxCollider>();
        RefreshPlayerRef();
    }

    private void Update()
    {
        // 暂停菜单打开期间不响应交互键（守卫：暂停优先）
        if (PauseUI.IsPaused)
        {
            InRange = false;
            Aimed = false;
            return;
        }
        if (IsOpen)
        {
            InRange = false; // 已开启不再作为可交互目标（屏幕正中提示汇集用）
            Aimed = false;
            return;
        }
        if (_player == null && !RefreshPlayerRef()) return;

        bool inRange = (_player.position - transform.position).sqrMagnitude
            <= interactRadius * interactRadius;
        InRange = inRange;    // 距离门槛（InteractPromptUI提示/方框汇集用）
        Aimed = inRange && EvaluateAim(); // 准星指向判定（InteractPromptUI与F键同口径）

        // 长按开启：瞄准+按住交互键蓄力，进度由InteractPromptUI进度环（绕准星）展示；
        // 松键/移开视线/离开范围/受伤（OnPlayerDamaged）打断——进度清零需重新长按
        TickHold(Aimed && Input.GetKey(interactKey));
    }

    /// <summary>
    /// 准星瞄准判定（射线命中制，适配大体积宝箱）：从相机沿视线发射线，
    /// 命中本宝箱自身碰撞体任意部位即视为指向——此前用中心锥角(5°)判定时，
    /// 大宝箱只有中心一小块可交互、箱体边缘全部失效（实测用户反馈）。
    /// 射线先命中他物=被遮挡不指向；距离门槛由InRange（玩家位置）承担
    /// </summary>
    private bool EvaluateAim()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        RaycastHit hit;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out hit,
                interactRadius + 2f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)) // 忽略触发器：隐形触发区不挡瞄准
        {
            return false;
        }
        return hit.collider.GetComponentInParent<LootChest>() == this;
    }

    /// <summary>
    /// 长按蓄力推进（Update每帧调用；公开供测试脚本直驱——旧Input Manager无法注入按键，
    /// 测试时禁用本组件后逐帧调用即可模拟长按）：held=true累计进度满holdDuration开箱；
    /// false且蓄力中=打断（进度>0时发出HoldInterrupted）
    /// </summary>
    public void TickHold(bool held)
    {
        if (IsOpen) return;
        if (held)
        {
            if (!IsHolding)
            {
                IsHolding = true;
                _holdElapsed = 0f;
            }
            _holdElapsed += Time.deltaTime;
            HoldProgress = Mathf.Clamp01(_holdElapsed / holdDuration);
            _currentHold = this;
            if (_holdElapsed >= holdDuration)
            {
                EndHold();
                TryOpen();
            }
        }
        else if (IsHolding)
        {
            CancelHold();
        }
    }

    /// <summary>结束蓄力（完成路径）：进度清零并退出当前蓄力登记</summary>
    private void EndHold()
    {
        IsHolding = false;
        HoldProgress = 0f;
        if (_currentHold == this) _currentHold = null;
    }

    /// <summary>打断蓄力（松键/离开范围/受伤）：进度清零并发出打断事件（进度>0才有反馈意义）</summary>
    private void CancelHold()
    {
        float progress = HoldProgress;
        EndHold();
        if (progress > 0f && HoldInterrupted != null) HoldInterrupted(progress);
    }

    private bool RefreshPlayerRef()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            _player = p.transform;
            HookPlayerHealth(p);
        }
        return _player != null;
    }

    /// <summary>订阅玩家受伤事件：长按蓄力中被击=打断（一次性挂钩防重复订阅）</summary>
    private void HookPlayerHealth(GameObject p)
    {
        if (_damageHooked) return;
        var h = p.GetComponent<Health>();
        if (h != null)
        {
            _playerHealth = h;
            _playerHealth.onDamaged.AddListener(OnPlayerDamaged);
            _damageHooked = true;
        }
    }

    private void OnPlayerDamaged(float amount)
    {
        if (IsHolding) CancelHold(); // 长按中被击打断
    }

    /// <summary>
    /// 开箱：件数2~6 → 逐件"光柱→延迟①→物品→延迟②"串行，每件独立roll品质
    /// （可能出现不同品质混开）。公开入口供F键与测试脚本调用
    /// </summary>
    public void TryOpen()
    {
        if (IsOpen) return;
        IsOpen = true;
        AudioManager.PlaySfx("SFX_ChestOpen", transform.position); // 开箱音效（3D定位宝箱处）

        if (lootTable != null)
        {
            // 配置表v0.2：挂了掉落表→按表独立概率roll（表六宝箱列），优先于品质池
            StartCoroutine(TableSequence(lootTable.Roll()));
        }
        else
        {
            // 旧品质池：件数2~6（每件物品独立roll品质，可能出现不同品质混开）
            int count = Random.Range(countMin, countMax + 1);
            StartCoroutine(OpenSequence(count));
        }

        // 3. 开箱动画：启用Animator并从头播放开盖动画；播完禁用定格在开盖姿态
        if (openAnimator != null)
        {
            openAnimator.enabled = true;
            openAnimator.Play(openStateName, 0, 0f);
            StartCoroutine(DisableAnimatorAfterClip());
        }
        else if (whiteboxTint)
        {
            // 白模回退表现：箱体压暗（区别于未开启）
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                r.material.color = new Color(0.35f, 0.28f, 0.2f, 1f);
            }
        }
        FloatingText.Show("宝箱开启！按F拾取掉落物", transform.position + Vector3.up * 1.4f, 2f);
        Debug.Log("[LootChest] " + name + " 开启，掉落=" + (lootTable != null ? "按掉落表roll" : "品质池2~6件") + "（每件独立品质）");
    }

    /// <summary>按权重随机品质（x=白蓝 y=紫蓝 z=紫）</summary>
    private ItemRarity RollRarity()
    {
        float total = rarityWeights.x + rarityWeights.y + rarityWeights.z;
        if (total <= 0f) return ItemRarity.BlueWhite;
        float v = Random.Range(0f, total);
        if ((v -= rarityWeights.x) < 0f) return ItemRarity.BlueWhite;
        if ((v -= rarityWeights.y) < 0f) return ItemRarity.VioletBlue;
        return ItemRarity.Purple;
    }

    /// <summary>从对应品质池随机取一件（池空返回null由调用方跳过）</summary>
    private ItemData PickItem(ItemRarity rarity)
    {
        ItemData[] pool = rarity == ItemRarity.BlueWhite ? lootBlueWhite
            : rarity == ItemRarity.VioletBlue ? lootVioletBlue
            : lootPurple;
        if (pool == null || pool.Length == 0) return null;
        return pool[Random.Range(0, pool.Length)];
    }

    /// <summary>延迟①：光柱→物品（品质越高越久）</summary>
    private float BeamToItemDelay(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.VioletBlue: return 1.4f;
            case ItemRarity.Purple: return 2.4f;
            default: return 0.5f;
        }
    }

    /// <summary>
    /// 开箱序列协程：逐件串行，每件独立roll品质（可能出现不同品质混开）——
    /// 该品质光柱 → 延迟① → 弹出物品 → 延迟② → 下一件光柱……
    /// 一道光柱只对应一件物品
    /// </summary>
    private IEnumerator OpenSequence(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var rarity = RollRarity();
            var item = PickItem(rarity);
            if (item == null) continue;
            float d1 = BeamToItemDelay(rarity);
            // 光柱存活=延迟①+间隔②-0.1s：恰在下一道光柱出现前0.1秒消失，杜绝重叠
            SpawnBeam(rarity, d1 + ItemGapDelay(rarity) - 0.1f);
            yield return new WaitForSeconds(d1);
            SpawnLootCube(item, Random.Range(1, 4));
            if (i < count - 1)
            {
                yield return new WaitForSeconds(ItemGapDelay(rarity));
            }
        }
    }

    /// <summary>
    /// 掉落表驱动开箱序列（配置表v0.2：挂了lootTable的宝箱走此管线）——
    /// 按MonsterLootTable.Roll()结果逐件"光柱→物品"，节奏与品质池版一致
    /// </summary>
    private IEnumerator TableSequence(List<(ItemData item, int count)> rolls)
    {
        for (int i = 0; i < rolls.Count; i++)
        {
            var rarity = rolls[i].item.rarity;
            float d1 = BeamToItemDelay(rarity);
            SpawnBeam(rarity, d1 + ItemGapDelay(rarity) - 0.1f);
            yield return new WaitForSeconds(d1);
            SpawnLootCube(rolls[i].item, rolls[i].count);
            if (i < rolls.Count - 1)
            {
                yield return new WaitForSeconds(ItemGapDelay(rarity));
            }
        }
    }

    /// <summary>延迟②：本件物品→下一件光柱（品质越高越久）</summary>
    private float ItemGapDelay(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.VioletBlue: return 0.8f;
            case ItemRarity.Purple: return 1.5f;
            default: return 0.3f;
        }
    }

    /// <summary>动画播完后禁用Animator定格在开盖姿态（防clip循环配置导致反复开盖）</summary>
    private IEnumerator DisableAnimatorAfterClip()
    {
        float len = 0.4f;
        if (openAnimator != null && openAnimator.runtimeAnimatorController != null)
        {
            foreach (var c in openAnimator.runtimeAnimatorController.animationClips)
            {
                if (c != null && c.name.Contains("Treasure")) { len = c.length; break; }
            }
        }
        yield return new WaitForSeconds(len + 0.05f);
        if (openAnimator != null) openAnimator.enabled = false;
    }

    /// <summary>品质光柱：无碰撞圆柱，Sprites/Default半透明染色，lifetime秒后自动销毁（调用方保证在下一道光柱前销毁）</summary>
    private void SpawnBeam(ItemRarity rarity, float lifetime)
    {
        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "RarityBeam_" + ItemRarityStyle.GetName(rarity);
        Destroy(beam.GetComponent<Collider>()); // 光柱不可触碰
        beam.transform.position = transform.position + Vector3.up * 3.5f;
        beam.transform.localScale = new Vector3(0.5f, 3.5f, 0.5f); // 直径0.5m 高7m
        var c = ItemRarityStyle.GetColor(rarity);
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(c.r, c.g, c.b, 0.4f);
        beam.GetComponent<MeshRenderer>().material = mat;
        Destroy(beam, lifetime);
    }

    /// <summary>
    /// 弹出一件掉落小方块：品质染色（Unlit保证黑暗地牢可读）。
    /// 掉落物无碰撞体（用户要求）——运动为程序化抛物线toss（LootItemCube.BeginToss）：
    /// 向下射线找落点高度+手动重力积分，落地即停。
    /// 彻底规避刚体方案的两个实测坑：薄面片地砖隧穿、去重叠爆炸弹飞
    /// </summary>
    private void SpawnLootCube(ItemData item, int count)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "LootCube_" + item.itemName;
        Destroy(go.GetComponent<Collider>()); // 掉落物无碰撞体
        go.transform.position = transform.position + Vector3.up * 1.2f;
        go.transform.localScale = Vector3.one * 0.3f;

        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Unlit/Color"));
        mr.material.color = ItemRarityStyle.GetColor(item.rarity);

        var loot = go.AddComponent<LootItemCube>();
        loot.Init(item, count);
        loot.BeginToss();
    }
}
