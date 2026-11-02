using UnityEngine;

/// <summary>怪物世界空间血条的可编辑视觉节点和颜色。</summary>
public class EnemyHealthBarVisual : MonoBehaviour
{
    public Renderer background, fill;
    public Color lowHealthColor = new Color(.9f,.2f,.2f,1);
    [Range(0,1)] public float backgroundOpacity = .8f;
}
