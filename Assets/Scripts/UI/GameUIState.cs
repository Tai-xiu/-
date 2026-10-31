using System;
using System.Collections.Generic;

/// <summary>
/// 全局UI状态门（静态类，不继承MonoBehaviour、无需挂物体）：
/// 管理游戏面板（背包/商店等）的打开状态，供PlayerController/PlayerCombat/CameraFollow
/// 读取以冻结玩家移动/攻击/相机鼠标偏移，并作为Esc分层路由的"最上层面板"数据源。
/// 采用有序面板栈（后开在上）而非bool/散计数：
/// - 背包与商店可同时打开，只关其一时仍保持冻结，全部关闭才解除；
/// - Esc只需关闭"最上层"（最后打开的）面板，由TryCloseTopPanel提供该能力；
/// - 面板本身挂在常驻UI（DDOL画布）上，静态数据天然跨场景存活，场景切换不重置。
/// 同步纪律：面板必须在自己"是否打开"状态变更处调用Register/Unregister
/// （打开入栈、关闭/销毁出栈），保证栈与面板实际激活状态严格一一对应、防重复注册。
/// </summary>
public static class GameUIState
{
    /// <summary>已打开面板注册表：列表末位=最上层（最后打开）的面板</summary>
    private static readonly List<(string id, Action closeAction)> _panels = new List<(string id, Action closeAction)>();

    /// <summary>面板打开时调用：入栈（同id先移除旧条目再入栈，防重复注册）</summary>
    public static void RegisterPanel(string id, Action closeAction)
    {
        UnregisterPanel(id);
        _panels.Add((id, closeAction));
    }

    /// <summary>面板关闭/销毁时调用：按id出栈（未注册时不做任何事）</summary>
    public static void UnregisterPanel(string id)
    {
        for (int i = _panels.Count - 1; i >= 0; i--)
        {
            if (_panels[i].id == id)
            {
                _panels.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>
    /// Esc路由用：栈非空则弹出栈顶（最后打开的）面板并调用其closeAction关闭，返回true；
    /// 栈空返回false（调用方据此决定是否触发暂停）。
    /// 先出栈再回调：closeAction内部的UnregisterPanel不会重复移除，重入安全。
    /// </summary>
    public static bool TryCloseTopPanel()
    {
        if (_panels.Count == 0) return false;
        var top = _panels[_panels.Count - 1];
        _panels.RemoveAt(_panels.Count - 1);
        top.closeAction?.Invoke();
        return true;
    }

    /// <summary>是否有任一面板处于打开状态（true=应冻结玩家移动/攻击/相机鼠标偏移）</summary>
    public static bool IsAnyPanelOpen => _panels.Count > 0;
}
