using UnityEngine;

/// <summary>
/// 测试用碰撞掉血胶囊（HUD血条验证用，验证完可删）
/// 玩家进入触发范围后每interval秒扣damage点血，用于观察：
/// 掉血瞬切 / 白色残影延迟追赶 / 受击闪白闪红 / LowHP≤30%深红呼吸+数值变黄
/// </summary>
public class DamageTestCapsule : MonoBehaviour
{
    [Tooltip("每次扣血量")]
    [SerializeField] private float damage = 10f;
    [Tooltip("扣血间隔（秒）")]
    [SerializeField] private float interval = 0.5f;

    private bool _inside;
    private float _timer;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) _inside = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) _inside = false;
    }

    private void Update()
    {
        if (!_inside || PauseUI.IsPaused) return;
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            _timer = interval;
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                var h = player.GetComponent<Health>();
                if (h != null && !h.IsDead) h.TakeDamage(damage);
            }
        }
    }
}
