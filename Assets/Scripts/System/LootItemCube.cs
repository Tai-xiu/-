using UnityEngine;

/// <summary>
/// 开箱掉落小方块（白模）：品质染色 + 缓慢自转 + 十字准星瞄准精确拾取
/// 拾取判定（用户指定"光标指向那个就拾取那个，单个"）：
/// - 从第一人称相机沿视线方向判定：距离≤pickupRange 且 视线夹角≤aimAngle 的掉落物为"被瞄准"
/// - 同一瞬间只有一个被瞄准（多件重叠时取夹角最小者），按F只拾取被瞄准的这一件
/// - 无碰撞体（用户要求）——瞄准为纯数学角度/距离判定，与物理系统完全解耦
/// 拾取：Inventory.AddItem（空间不足提示且保留在地上）+ GameManager.RecordLoot（结算面板）
/// 测试入口：TryPickup()公开
/// </summary>
public class LootItemCube : MonoBehaviour
{
    [Tooltip("拾取射程（米）：视线方向上超过该距离的掉落物不可拾取")]
    [SerializeField] private float pickupRange = 3f;
    [Tooltip("瞄准锥角（度）：视线与掉落物方向夹角小于该值视为【指向】")]
    [SerializeField] private float aimAngle = 5f;
    [Tooltip("拾取键")]
    [SerializeField] private KeyCode pickupKey = KeyCode.F;
    [Tooltip("自转速度（度/秒，便于辨识掉落物）")]
    [SerializeField] private float spinSpeed = 90f;

    private ItemData _item;
    private int _count = 1;
    private Transform _player;

    /// <summary>当前被瞄准的掉落物（InteractPromptUI读取显示方框与拾取提示；null=无）</summary>
    public static LootItemCube CurrentAimed { get { return _aimed; } }

    /// <summary>拾取提示标签（品质·名字 xN；InteractPromptUI屏幕正中提示用；无品质不带前缀）</summary>
    public string PromptLabel
    {
        get
        {
            if (_item == null) return "";
            string rarityPart = _item.rarity == ItemRarity.None
                ? "" : ItemRarityStyle.GetName(_item.rarity) + "·";
            return rarityPart + _item.itemName + " x" + _count;
        }
    }

    /// <summary>拾取时是否计入探索战利品结算（背包丢弃重拾的掉落物=false，避免结算面板重复计数）</summary>
    public bool countTowardsLootLog = true;

    // —— 抛物线toss（无碰撞体：手动重力积分，落地即停） ——
    private bool _tossing;
    private Vector3 _vel;
    private float _floorY;

    // —— 瞄准注册表：全场景掉落物统一评估，同一瞬间仅一件被瞄准 ——
    private static readonly System.Collections.Generic.List<LootItemCube> _all
        = new System.Collections.Generic.List<LootItemCube>();
    private static LootItemCube _aimed;   // 当前被瞄准的掉落物（null=无）
    private static int _aimFrame = -1;    // 帧号去重：每帧只评估一次

    /// <summary>注入物品内容（由LootChest弹出时调用）</summary>
    public void Init(ItemData item, int count)
    {
        _item = item;
        _count = count;
    }

    /// <summary>
    /// 程序化抛物线toss：随机方向1~2m外的落点，向下射线探测地面高度，
    /// 以向上初速+水平初速做手动重力积分飞行，落地即停（水平速度同时归零）。
    /// 无刚体无碰撞体——与物理系统完全解耦，不存在隧穿/弹飞。
    /// 地面探测从抛出点上方0.1m开始（不能从固定高空打：Dungeon室内天花板会先挡住射线，
    /// _floorY变成天花板高度→掉落物悬空不可见）；忽略触发器（尸体瞄准体等不是地面）；
    /// 落点搜索从最远落点向近处逐级收敛，每级8方向环扫（正对空洞/墙的方向跳过换向），
    /// 取第一个"下方有地面"的方向与距离，飞行速度按该距离缩放——保证落在有地面的一侧不悬空
    /// </summary>
    public void BeginToss()
    {
        var baseDir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        float dist = Random.Range(1f, 2f);
        var origin = transform.position;

        // 落点搜索：从最远落点向近处逐级收敛，每级8方向环扫，
        // 取第一个"下方有地面"的方向与距离（正对空洞/墙的方向自动换向，不悬空、不穿墙）
        _floorY = 0.2f; // 兜底（8方向全程无地面时落到该高度）
        var chosenDir = baseDir;
        float foundD = 0f;
        bool found = false;
        float step = Mathf.Max(0.25f, dist * 0.25f);

        for (float d = dist; d >= 0.2f && !found; d -= step)
        {
            for (int a = 0; a < 8 && !found; a++)
            {
                var tryDir = Quaternion.Euler(0f, 45f * a, 0f) * baseDir;
                var probe = origin + tryDir * d + Vector3.up * 0.1f;
                RaycastHit hit;
                if (Physics.Raycast(probe, Vector3.down, out hit, 100f, ~0, QueryTriggerInteraction.Ignore))
                {
                    _floorY = hit.point.y;
                    chosenDir = tryDir;
                    foundD = d;
                    found = true;
                }
            }
        }

        // 飞行速度按验证过的落点距离缩放：落点越近水平速度越小，保证落在有地面的范围内
        float hScale = Mathf.Clamp01(foundD / 1f);
        _vel = chosenDir * Random.Range(0.8f, 1.6f) * hScale + Vector3.up * Random.Range(2.2f, 3.2f);
        _tossing = true;
    }

    private void OnEnable()
    {
        _all.Add(this);
    }

    private void OnDisable()
    {
        _all.Remove(this);
        if (_aimed == this) _aimed = null;
    }

    private void Update()
    {
        // 暂停菜单打开期间不响应（守卫：暂停优先）
        if (PauseUI.IsPaused || _item == null) return;

        // 抛物线toss：手动重力积分（半高0.15=0.3尺寸的一半），落到地面高度即停
        if (_tossing)
        {
            _vel += Physics.gravity * Time.deltaTime;
            var pos = transform.position + _vel * Time.deltaTime;
            if (pos.y <= _floorY + 0.15f && _vel.y < 0f)
            {
                pos.y = _floorY + 0.15f;
                _tossing = false;
                _vel = Vector3.zero;
            }
            transform.position = pos;
        }

        // 缓慢自转便于辨识（白模阶段的掉落物辨识度）
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        // 瞄准评估：每帧全场景只算一次（帧号去重，多件掉落不重复计算）
        if (_aimFrame != Time.frameCount)
        {
            _aimFrame = Time.frameCount;
            EvaluateAim();
        }

        // 只有被十字准星瞄准的这一件响应F键（精确拾取，单个）；拾取提示由InteractPromptUI屏幕正中显示
        if (_aimed == this && Input.GetKeyDown(pickupKey))
        {
            TryPickup();
        }
    }

    /// <summary>
    /// 瞄准评估（每帧一次）：从第一人称相机沿视线判定——
    /// 距离≤pickupRange 且 视线夹角≤aimAngle 中夹角最小者为被瞄准；无命中则清空
    /// </summary>
    private static void EvaluateAim()
    {
        _aimed = null;
        var cam = Camera.main;
        if (cam == null) return;

        float bestAngle = float.MaxValue;
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            var c = _all[i];
            if (c == null || c._item == null)
            {
                _all.RemoveAt(i);
                continue;
            }
            var v = c.transform.position - cam.transform.position;
            float dist = v.magnitude;
            if (dist > c.pickupRange || dist < 0.05f) continue;
            float ang = Vector3.Angle(cam.transform.forward, v);
            if (ang <= c.aimAngle && ang < bestAngle)
            {
                bestAngle = ang;
                _aimed = c;
            }
        }
    }

    /// <summary>拾取入包（被瞄准+按F触发；测试脚本可直接调用）：空间不足提示且不消耗</summary>
    public void TryPickup()
    {
        if (_item == null) return;
        var playerGO = GameObject.FindWithTag("Player");
        if (playerGO == null) return;
        var inventory = playerGO.GetComponent<Inventory>();
        if (inventory == null) return;

        if (!inventory.HasSpaceFor(_item, _count))
        {
            FloatingText.Show("背包已满", transform.position + Vector3.up * 0.7f, 1f);
            return;
        }

        inventory.AddItem(_item, _count);
        if (countTowardsLootLog && GameManager.Instance != null)
        {
            GameManager.Instance.RecordLoot(_item, _count); // 探索结算面板展示
        }
        // 悬赏契约：写入本局收集清单（只记不结算，撤离成功才入账；背包丢弃重拾不计）
        if (countTowardsLootLog)
        {
            QuestManager.NotifyPickup(_item, _count);
        }
        AudioManager.PlaySfx("SFX_CoinPickup", transform.position);
        Debug.Log("[LootItemCube] 拾取 " + _item.itemName + " x" + _count
            + "（" + ItemRarityStyle.GetName(_item.rarity) + "）");
        Destroy(gameObject);
    }
}
