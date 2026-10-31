using UnityEngine;

/// <summary>
/// 地板变体散布工具（编辑器/运行时两用）。
/// 在指定地面Renderer的世界空间XZ范围内随机散布少量"裂缝/破损"变体贴片（Quad），
/// 用于打破平铺地面的单调感。设计要点：
/// 1. 贴片统一抬高0.01米，避免与地面共面导致z-fighting闪烁；
/// 2. 随机旋转限定为90°倍数，保证像素纹理走向与地面平铺方向对齐；
/// 3. 随机种子固定可复现（同一参数每次生成完全一致，便于验收与回滚）；
/// 4. 数量按"每10平方米N块"计算并设上限，点缀而非铺满。
/// 用法：挂到场景任意物体（建议场景根节点），Inspector拖入地面Renderer与变体材质，
/// 编辑器中右键组件标题选"生成变体"/"清除变体"；运行时调用 Generate()/Clear() 等效。
/// </summary>
public class FloorVariantScatter : MonoBehaviour
{
    [Header("目标地面（取其世界包围盒顶面为散布区域）")]
    public Renderer groundRenderer;

    [Header("变体材质列表（裂缝/破损等，随机取用）")]
    public Material[] variantMaterials;

    [Header("每10平方米散布数量（0~1为点缀级密度）")]
    public float countPer10SquareMeters = 0.5f;

    [Header("数量上限（防止大面积地面撒过多）")]
    public int maxCount = 64;

    [Header("随机种子（固定可复现）")]
    public int seed = 20261001;

    [Header("贴片抬高量（米，防z-fighting）")]
    public float yOffset = 0.01f;

    [Header("单片变体尺寸（米；平铺主贴图4米一格含3块石板，单片约1.33）")]
    public float variantSize = 1.3333f;

    [Header("变体容器名（层级中统一管理/清除）")]
    public string containerName = "FloorVariants";

    /// <summary>按当前参数生成变体贴片（编辑器右键菜单或运行时调用；可重复执行，先清后撒）</summary>
    [ContextMenu("生成变体")]
    public void Generate()
    {
        // 参数守卫：缺地面或缺材质时直接报错返回，不生成任何东西
        if (groundRenderer == null)
        {
            Debug.LogError("[FloorVariantScatter] 未指定地面Renderer，无法散布");
            return;
        }
        if (variantMaterials == null || variantMaterials.Length == 0)
        {
            Debug.LogError("[FloorVariantScatter] 未配置变体材质列表");
            return;
        }

        Clear(); // 先清掉旧变体再生成，保证重复执行不叠加

        Bounds b = groundRenderer.bounds;
        float area = b.size.x * b.size.z;
        // 数量 = 面积/10㎡ × 密度，夹到[0,上限]
        int count = Mathf.Clamp(Mathf.RoundToInt(area / 10f * countPer10SquareMeters), 0, maxCount);

        System.Random rng = new System.Random(seed); // 固定种子→可复现
        GameObject container = new GameObject(containerName);
        // 编辑器下挂到地面同父（如Environment），保持层级整洁
        if (groundRenderer.transform.parent != null)
            container.transform.SetParent(groundRenderer.transform.parent, false);

        Debug.Log($"[FloorVariantScatter] 区域{b.size.x:F0}x{b.size.z:F0}m（{area:F0}㎡）生成{count}块变体，种子{seed}");

        for (int i = 0; i < count; i++)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "FloorVariant_" + i;

            // 纯视觉贴片：去掉碰撞体（编辑器与运行时统一用DestroyImmediate，均合法）
            Collider col = quad.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            // 薄贴片投射阴影易在墙面产生条纹瑕疵，关闭投射但保留受光
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sharedMaterial = variantMaterials[rng.Next(variantMaterials.Length)];

            // 在地面顶面范围内随机取位（四边内缩半片宽，避免悬出边缘）
            float half = variantSize * 0.5f;
            float x = (float)(b.min.x + half + rng.NextDouble() * (b.size.x - variantSize));
            float z = (float)(b.min.z + half + rng.NextDouble() * (b.size.z - variantSize));
            // 旋转 = 世界Y轴90°倍数 × 面朝上的90°翻折（Quad默认面朝-Z）
            int yaw = rng.Next(4) * 90;
            quad.transform.SetPositionAndRotation(
                new Vector3(x, b.max.y + yOffset, z),
                Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f));

            quad.transform.SetParent(container.transform, true);
        }
    }

    /// <summary>清除本工具生成的全部变体贴片</summary>
    [ContextMenu("清除变体")]
    public void Clear()
    {
        Transform parentT = (groundRenderer != null && groundRenderer.transform.parent != null)
            ? groundRenderer.transform.parent
            : transform;
        Transform old = parentT.Find(containerName);
        if (old != null) DestroyImmediate(old.gameObject);
    }
}
