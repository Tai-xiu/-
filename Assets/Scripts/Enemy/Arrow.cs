using UnityEngine;

/// <summary>
/// 箭矢（白盒原型阶段，白色细长Cube占位）
/// 行为：
/// - 直线匀速飞行（Transform驱动 + 运动学刚体保证Trigger事件可靠触发，无重力下坠）
/// - 命中玩家（Trigger检测，Tag=Player）→ 造成伤害并销毁
/// - 命中墙体等实心环境 → 销毁（避免穿墙）
/// - 穿过其他敌人Trigger时不生效继续飞行
/// - 飞行超过lifeTime（默认5秒）自毁
/// 通过静态方法Arrow.Spawn生成，弓手不直接关心箭矢细节
/// </summary>
public class Arrow : MonoBehaviour
{
    [Header("—— 箭矢参数 ——")]
    [Tooltip("飞行速度（米/秒）")]
    [SerializeField] private float speed = 10f;

    [Tooltip("命中伤害")]
    [SerializeField] private float damage = 8f;

    [Tooltip("最长飞行时间（秒），超时自毁")]
    [SerializeField] private float lifeTime = 5f;

    /// <summary>飞行方向（单位向量）</summary>
    private Vector3 _direction;
    /// <summary>是否已进入飞行状态</summary>
    private bool _fired;

    /// <summary>
    /// 静态工厂：在指定位置生成一支朝指定方向飞行的箭矢
    /// </summary>
    public static Arrow Spawn(Vector3 position, Vector3 direction, float speed, float damage)
    {
        // 白盒外形：白色细长Cube（Z轴为飞行方向）
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Arrow";
        go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        go.transform.localScale = new Vector3(0.08f, 0.08f, 0.6f);

        // 命中判定用触发器：视觉细长，判定盒略放宽（XY放大），避免高速擦边不判定
        BoxCollider col = go.GetComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(10f, 10f, 1f);

        // 运动学刚体：保证Trigger事件在本体移动时可靠触发（无刚体的静态Trigger互检不发消息）
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        Arrow arrow = go.AddComponent<Arrow>();
        arrow.speed = speed;
        arrow.damage = damage;
        arrow._direction = direction.normalized;
        arrow._fired = true;

        // 淡灰白色外观（运行时材质实例，不生成资产）
        go.GetComponent<MeshRenderer>().material.color = new Color(0.95f, 0.95f, 0.95f, 1f);

        // 超时自毁（无论是否命中）
        Destroy(go, arrow.lifeTime);
        return arrow;
    }

    private void Update()
    {
        if (!_fired) return;

        // 直线匀速飞行
        transform.position += _direction * (speed * Time.deltaTime);
    }

    /// <summary>
    /// 触发器命中检测：玩家→伤害+销毁；敌人Trigger→穿过；实心环境→销毁
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // 命中玩家：扣血并销毁
        if (other.CompareTag("Player"))
        {
            Health hp = other.GetComponent<Health>();
            if (hp != null && !hp.IsDead)
            {
                hp.TakeDamage(damage);
                Debug.Log("[Arrow] 命中玩家，造成 " + damage + " 伤害，玩家剩余 " + hp.Current + "/" + hp.Max);
            }
            Destroy(gameObject);
            return;
        }

        // 穿过其他敌人的Trigger（如史莱姆/弓手本体），不产生效果
        if (other.isTrigger) return;

        // 命中墙体/地面等实心环境：销毁，避免穿墙
        Destroy(gameObject);
    }
}
