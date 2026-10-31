using UnityEngine;

/// <summary>
/// 魔狼咬击触发器转发器：挂在BiteTrigger子物体（Trigger碰撞体+运动学刚体）上，
/// 把OnTriggerEnter事件转发给所属WolfAI结算咬中（一次扑咬只结算一次，由WolfAI._biteHit防重）
/// </summary>
public class WolfBiteTrigger : MonoBehaviour
{
    [HideInInspector] public WolfAI Owner;

    private void OnTriggerEnter(Collider other)
    {
        if (Owner != null) Owner.NotifyBiteHit(other);
    }
}
