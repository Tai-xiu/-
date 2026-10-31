using UnityEngine;

/// <summary>
/// 怪物弹体（直线弹道，无追踪）：骷髅弓箭占位——
/// - 直线飞行：沿发射方向匀速前进（配置表弹速12m/s），可被掩体/立柱挡住
///   （命中非触发实体碰撞体即销毁，走位与掩体都有意义）
/// - 命中玩家：球形判定（覆盖玩家胶囊），伤害走玩家Health（护甲修正自动生效）
/// - 射程上限：超过maxDistance销毁；忽略射击者自身碰撞体（弹体从弓位生成不挡自己）
/// 挂载：运行时AddComponent在弹体视觉物上（无自身碰撞体，纯逻辑+视觉）
/// </summary>
public class MonsterProjectile : MonoBehaviour
{
    private Vector3 _dir;
    private float _speed;
    private float _damage;
    private float _maxDist;
    private Transform _shooter;
    private float _traveled;

    /// <summary>注入弹体参数（由射击怪物调用）</summary>
    public void Init(Vector3 dir, float speed, float damage, float maxDist, Transform shooter)
    {
        _dir = dir.normalized;
        _speed = speed;
        _damage = damage;
        _maxDist = maxDist;
        _shooter = shooter;
    }

    private void Update()
    {
        float step = _speed * Time.deltaTime;
        transform.position += _dir * step;
        _traveled += step;

        // 命中检测：球形覆盖（玩家胶囊/实体掩体），忽略触发器与射击者自身
        var hits = Physics.OverlapSphere(transform.position, 0.25f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            if (_shooter != null && c.transform.IsChildOf(_shooter)) continue;

            if (c.CompareTag("Player"))
            {
                var targetHealth = c.GetComponent<Health>();
                if (targetHealth != null && !targetHealth.IsDead)
                {
                    targetHealth.TakeDamage(_damage);
                }
                Destroy(gameObject);
                return;
            }
            // 实体掩体：弹体被挡住销毁（配置表：直线弹道可被掩体挡住，走位才有意义）
            Destroy(gameObject);
            return;
        }

        if (_traveled >= _maxDist)
        {
            Destroy(gameObject);
        }
    }
}
