using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 宝箱（白盒原型阶段，状态：关闭→开启，不可重复开启）
/// - 结构：根物体挂交互触发器（玩家靠近）+ Base底盒（实体阻挡）+ Lid盖子（绕后沿枢轴翻开的空物体）
/// - 交互：玩家在范围内按交互键（默认F）→ TryOpen
/// - 开启表现：盖子翻开动画（openDuration）→ 按LootTable权重随机抽取战利品，
///   在宝箱周围生成掉落物（复用PickupItem拾取逻辑）→ 宝箱上方弹出漂浮文字提示获得物
/// - 不可重复开启：IsOpen后TryOpen直接忽略
/// </summary>
public class Chest : MonoBehaviour
{
    [Tooltip("战利品表（ScriptableObject资产）")]
    [SerializeField] private LootTable lootTable;

    [Tooltip("盖子枢轴（开启时绕其X轴向后翻开）")]
    [SerializeField] private Transform lid;

    [Tooltip("盖子翻开角度（度，负值向后翻）")]
    [SerializeField] private float openAngle = -75f;

    [Tooltip("翻盖动画时长（秒）")]
    [SerializeField] private float openDuration = 0.4f;

    [Tooltip("战利品掉落散落半径（米）")]
    [SerializeField] private float dropRadius = 0.9f;

    [Tooltip("交互键（开启宝箱，默认F）")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    /// <summary>是否已开启（公开只读，便于调试与测试）</summary>
    public bool IsOpen { get; private set; }

    private bool _playerInRange;
    private Transform _player;

    private void Awake()
    {
        // 白盒棕色外形（运行时材质实例，不生成资产）
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            r.material.color = new Color(0.55f, 0.4f, 0.25f, 1f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || IsOpen) return;
        _playerInRange = true;
        _player = other.transform;
        // 靠近提示（一次性短提示）
        FloatingText.Show("按F开启宝箱", transform.position + Vector3.up * 1.1f, 1.2f);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) _playerInRange = false;
    }

    private void Update()
    {
        // 暂停菜单打开期间不响应交互键（守卫：暂停优先）
        if (PauseUI.IsPaused) return;

        if (_playerInRange && !IsOpen && Input.GetKeyDown(interactKey))
        {
            TryOpen();
        }
    }

    /// <summary>
    /// 尝试开启：已开启则忽略；首次开启翻盖并掉落战利品
    /// </summary>
    public void TryOpen()
    {
        if (IsOpen) return;
        if (lootTable == null)
        {
            Debug.LogWarning("[Chest] " + name + " 未配置战利品表");
            IsOpen = true; // 防止无表时反复触发
            return;
        }
        IsOpen = true;
        AudioManager.PlaySfx("SFX_ChestOpen", transform.position); // 开箱音效（3D定位宝箱处）
        StartCoroutine(OpenRoutine());
    }

    private IEnumerator OpenRoutine()
    {
        // 1. 盖子翻开动画（绕枢轴X轴旋转到openAngle）
        Quaternion from = lid.localRotation;
        Quaternion to = Quaternion.Euler(openAngle, 0f, 0f);
        float t = 0f;
        while (t < openDuration)
        {
            t += Time.deltaTime;
            lid.localRotation = Quaternion.Slerp(from, to, Mathf.Clamp01(t / openDuration));
            yield return null;
        }
        lid.localRotation = to;

        // 2. 按战利品表抽取并在宝箱周围生成掉落物
        var rolls = lootTable.Roll();
        var parts = new List<string>();
        foreach (var roll in rolls)
        {
            if (roll.item == null || roll.count <= 0) continue;
            Vector3 offset = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                * new Vector3(0f, 0f, Random.Range(0.5f, dropRadius));
            Vector3 pos = transform.position + offset + Vector3.up * 0.2f;
            SpawnPickup(roll.item, roll.count, pos);
            parts.Add(roll.item.itemName + " x" + roll.count);
        }

        // 3. 宝箱上方漂浮提示获得了什么（2秒消失）
        string text = "获得：" + (parts.Count > 0 ? string.Join("、", parts) : "（空空如也）");
        FloatingText.Show(text, transform.position + Vector3.up * 1.2f, 2f);
        Debug.Log("[Chest] " + name + " 开启：" + text);
    }

    /// <summary>生成一个掉落物（触发器+拾取组件+按类型染色）</summary>
    private void SpawnPickup(ItemData item, int count, Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Drop_" + item.itemName;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.35f;
        go.GetComponent<Collider>().isTrigger = true;
        go.AddComponent<PickupItem>().Init(item, count);
    }
}
