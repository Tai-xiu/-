using UnityEngine;

/// <summary>
/// 掉落物拾取（白盒原型阶段）
/// 挂在掉落物上：玩家靠近（轮询距离）或接触（触发器）时自动拾取进背包；
/// 背包放不下时提示且物品保留在地上。
/// 说明：transform直移的CharacterController与静态触发器的进入事件偶发不派发，
/// 因此以Update距离轮询为主、OnTriggerEnter为辅，双通道保证拾取可靠。
/// 拾取成功时向GameManager.RecordLoot记录本次探索战利品（返回营地结算面板展示）。
/// 物品内容通过 Init() 注入（EnemyAI掉金币/Chest掉战利品/编辑器摆放测试拾取物均使用）
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickupItem : MonoBehaviour
{
    [Tooltip("拾取获得的物品")]
    [SerializeField] private ItemData item;

    [Tooltip("拾取数量")]
    [SerializeField] private int count = 1;

    [Tooltip("轮询拾取半径（米）：玩家距离小于该值时自动拾取")]
    [SerializeField] private float pickupRadius = 1f;

    private Transform _playerTf;
    private bool _warnedFull;

    private void Reset()
    {
        // 编辑器添加组件时自动设为触发器
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        // 保证为触发器（运行时生成的掉落物也生效）
        GetComponent<Collider>().isTrigger = true;
        TintByType();
    }

    private void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) _playerTf = p.transform;
    }

    private void Update()
    {
        // 主通道：距离轮询（不依赖物理事件，可靠）
        if (_playerTf == null) return;
        if ((_playerTf.position - transform.position).sqrMagnitude <= pickupRadius * pickupRadius)
        {
            DoPickup(_playerTf.gameObject);
        }
    }

    /// <summary>注入物品内容：同时按物品名挂接对应美术模型与拾取闪光特效</summary>
    public void Init(ItemData itemData, int itemCount)
    {
        item = itemData;
        count = itemCount;
        ApplyArtModel();
    }

    /// <summary>
    /// 按物品名映射挂美术模型（金币/血瓶/铁剑/皮甲，Resources/Models下），隐藏白盒Cube
    /// 外观但保留碰撞体（拾取逻辑不变）；同时挂拾取闪光特效。未匹配物品保持白盒外观。
    /// </summary>
    private void ApplyArtModel()
    {
        if (item == null) return;
        string modelName = null;
        string n = item.itemName;
        if (n.Contains("金币")) modelName = "GoldCoins";
        else if (n.Contains("血瓶")) modelName = "HealthPotion";
        else if (n.Contains("铁剑")) modelName = "IronSword";
        else if (n.Contains("皮甲")) modelName = "LeatherArmor";
        if (modelName == null) return;

        var model = Resources.Load<GameObject>("Models/" + modelName);
        if (model != null)
        {
            var visual = Instantiate(model, transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * 0.5f; // 掉落物缩小一档（模型为1m基准）
            var cubeR = GetComponent<MeshRenderer>();
            if (cubeR != null) cubeR.enabled = false; // 隐藏白盒外观，Collider保留
        }

        var sparkle = Resources.Load<GameObject>("Effects/ItemSparkleYellow");
        if (sparkle != null)
        {
            var fx = Instantiate(sparkle, transform);
            fx.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            fx.transform.localScale = Vector3.one * 0.5f;
        }
    }

    /// <summary>按物品类型染色，便于白盒阶段区分（运行时材质实例，不生成资产）</summary>
    private void TintByType()
    {
        var r = GetComponentInChildren<MeshRenderer>();
        if (r == null || item == null) return;
        switch (item.type)
        {
            case ItemType.Weapon: r.material.color = new Color(0.55f, 0.7f, 1f, 1f); break;   // 蓝：武器
            case ItemType.Armor: r.material.color = new Color(0.8f, 0.6f, 0.3f, 1f); break;   // 棕：防具
            case ItemType.Consumable: r.material.color = new Color(1f, 0.3f, 0.3f, 1f); break; // 红：消耗品
            case ItemType.Material: r.material.color = new Color(1f, 0.85f, 0.2f, 1f); break;  // 金：材料
        }
    }

    /// <summary>
    /// 尝试拾取：背包放得下则进包并销毁自身；放不下提示一次并保留
    /// </summary>
    private void DoPickup(GameObject playerGO)
    {
        if (item == null) return;

        Inventory inventory = playerGO.GetComponent<Inventory>();
        if (inventory == null) return;

        if (!inventory.HasSpaceFor(item, count))
        {
            if (!_warnedFull)
            {
                Debug.LogWarning("[Pickup] 背包已满，无法拾取 " + item.itemName + " x" + count);
                _warnedFull = true;
            }
            return;
        }

        inventory.AddItem(item, count);
        Debug.Log("[Pickup] 拾取 " + item.itemName + " x" + count);
        AudioManager.PlaySfx("SFX_CoinPickup", transform.position); // 拾取音效（3D定位在拾取点）

        // 记录本次探索战利品（返回营地的结算面板展示）
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RecordLoot(item, count);
        }

        Destroy(gameObject);
    }

    /// <summary>辅通道：物理触发器接触（玩家CC接触时派发）</summary>
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            DoPickup(other.gameObject);
        }
    }
}
