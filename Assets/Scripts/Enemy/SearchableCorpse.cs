using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 怪物尸体可搜索组件（MonsterAI.OnDeath动态挂载，配置表v0.1：怪物战利品需搜索获得）：
/// 准星瞄准制（射线命中自身碰撞体，与宝箱同口径）+ 长按F 2秒搜索
/// 首搜 → 按MonsterLootTable独立roll → 结果写入SearchResult并广播Searched
/// 战利品数据持久在尸体上（三角洲行动式）：拾取即从SearchResult转移，重搜只显示剩余——防反复搜索刷资源
/// 可重复搜索：再次长按重新打开剩余战利品；完成搜索后需松开F才可再次蓄力（_needRelease防按住连搜）
/// 注册表_All/当前长按_CurrentHold/打断事件与LootChest同款手法（InteractPromptUI复用进度环）
/// </summary>
public class SearchableCorpse : MonoBehaviour
{
    public static event Action<float> HoldInterrupted;          // 蓄力打断（松键/移开/受伤）
    public static event Action<SearchableCorpse> Searched;      // 搜索完成（搜索结果面板打开）

    private static readonly List<SearchableCorpse> _all = new List<SearchableCorpse>();
    public static IReadOnlyList<SearchableCorpse> All => _all;
    private static SearchableCorpse _currentHold;
    public static SearchableCorpse CurrentHold => _currentHold;

    [SerializeField] private float interactRadius = 2.4f;
    [SerializeField] private float holdDuration = 1f;
    [SerializeField] private float aimAngle = 12f; // 瞄准锥角：尸体躺平面积大，比掉落物(5°)放宽，瞄到身体任意部位即可

    public MonsterLootTable LootTable { get; set; }
    public bool IsSearched { get; private set; }
    public bool Aimed { get; private set; }
    public float HoldProgress { get; private set; }

    private float _holdElapsed;
    private bool _holding;
    private bool _needRelease;   // 完成搜索后需松开F才可再次蓄力（防按住连搜）

    private void OnEnable()
    {
        _all.Add(this);

        // 尸体不阻挡（防堵路）：关闭本体树上所有实体碰撞体——
        // MonsterAI.OnDeath只关了根碰撞体，子节点实体碰撞体（魔狼咬击盒/大型哥布林狼牙棒等）会残留挡路；
        // 瞄准判定是纯数学角度制不依赖碰撞体，因此统一改补一个触发器瞄准碰撞体：
        // 不阻挡玩家CharacterController与寻路代理，保留可被射线/触发查询的扩展能力
        foreach (var col in GetComponentsInChildren<Collider>(true))
        {
            col.enabled = false;
        }
        var aimCol = gameObject.AddComponent<BoxCollider>();
        aimCol.isTrigger = true; // 触发器：不阻挡移动
        aimCol.size = new Vector3(0.9f, 0.5f, 1.6f);
        aimCol.center = new Vector3(0f, 0.25f, 0f);
    }

    private void OnDisable()
    {
        if (_currentHold == this) EndHold();
        _all.Remove(this);
    }

    private void Update()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;
        bool inRange = (player.transform.position - transform.position).sqrMagnitude <= interactRadius * interactRadius;
        Aimed = inRange && EvaluateAim();
        TickHold(Aimed && Input.GetKey(KeyCode.F) && !PauseUI.IsPaused);
    }

    /// <summary>
    /// 准星瞄准判定（纯数学角度制，同掉落物LootItemCube口径）：
    /// 尸体目标点（本体+0.25m）与相机视线的夹角≤aimAngle即指向。
    /// 不用射线命中制：玩家站尸体旁俯视时，射线会先命中玩家自己的CharacterController胶囊，
    /// 导致瞄准永远失败（实测踩坑）；角度制无此问题且不依赖碰撞体。
    /// 锥角12°比掉落物(5°)宽：尸体躺平后目标点偏离身体中心，窄锥角会导致瞄身体中部不显示指示器
    /// </summary>
    private bool EvaluateAim()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 targetPos = transform.position + Vector3.up * 0.25f;
        Vector3 toTarget = targetPos - cam.transform.position;
        return Vector3.Angle(cam.transform.forward, toTarget) <= aimAngle;
    }

    /// <summary>长按驱动（测试禁用组件后逐帧调用可模拟长按）：进度满→完成搜索；可重复蓄力（完成搜索后需先松键）</summary>
    public void TickHold(bool held)
    {
        if (held)
        {
            if (_needRelease) return; // 完成搜索后需松开F才可再次蓄力
            if (!_holding) { _holding = true; _holdElapsed = 0f; }
            _holdElapsed += Time.deltaTime;
            HoldProgress = Mathf.Clamp01(_holdElapsed / holdDuration);
            _currentHold = this;
            if (_holdElapsed >= holdDuration)
            {
                EndHold();
                CompleteSearch();
            }
        }
        else
        {
            _needRelease = false;
            if (_holding)
            {
                float progress = HoldProgress;
                EndHold();
                HoldInterrupted?.Invoke(progress);
            }
        }
    }

    private void EndHold()
    {
        _holding = false;
        HoldProgress = 0f;
        if (_currentHold == this) _currentHold = null;
    }

    private void CompleteSearch()
    {
        IsSearched = true;          // 至少搜索过一次的标记
        _needRelease = true;        // 需松开F再蓄力，防按住连搜
        // 首搜roll定持久战利品：拾取即从SearchResult转移（搜索面板直接引用本列表），
        // 重复搜索只显示剩余——防止反复搜索同一尸体刷资源
        if (SearchResult == null && LootTable != null) SearchResult = LootTable.Roll();
        Searched?.Invoke(this);
    }

    /// <summary>搜索产出（首搜后持久存在；搜索面板直接引用本列表，拾取/换位实时写回）</summary>
    public List<(ItemData item, int count)> SearchResult { get; private set; }
}