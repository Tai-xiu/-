using UnityEngine;

/// <summary>
/// 第一人称鼠标视角（挂在玩家物体上）：
/// - 鼠标水平移动 → 旋转玩家物体Y轴（身体转向，WASD移动方向随之改变）；
/// - 鼠标垂直移动 → 只旋转相机本地X轴俯仰，钳制在±80°（不带动身体）；
/// - 仅在光标锁定（CursorManager.IsLocked，游戏进行中）且未暂停（timeScale≠0）时响应；
/// 相机Transform由场景接线赋值（玩家子物体，眼睛高度）。
/// </summary>
public class FirstPersonLook : MonoBehaviour
{
    [Header("—— 视角设置 ——")]
    [Tooltip("鼠标灵敏度（越大转得越快）")]
    [SerializeField] private float mouseSensitivity = 2f;

    [Tooltip("俯仰角下限（度）")]
    [SerializeField] private float minPitch = -80f;

    [Tooltip("俯仰角上限（度）")]
    [SerializeField] private float maxPitch = 80f;

    [Tooltip("头部相机Transform（本地X轴俯仰用，场景接线赋值）")]
    [SerializeField] private Transform cameraTransform;

    private float _pitch; // 当前俯仰角（度）

    private void Update()
    {
        // 暂停（timeScale=0）时视角输入不响应
        if (Time.timeScale == 0f) return;

        // 仅光标锁定（游戏进行中，无面板/未暂停/未死亡）时响应鼠标移动
        if (!CursorManager.IsLocked) return;

        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // 水平：旋转身体Y轴
        transform.Rotate(0f, mx, 0f);

        // 垂直：只转相机本地俯仰，钳制防翻转。
        // 注意方向：Unity中Euler X正角度=低头，而鼠标上移时Mouse Y为正值，
        // 故用减法使"鼠标上移=抬头"（标准FPS手感）
        _pitch = Mathf.Clamp(_pitch - my, minPitch, maxPitch);
        if (cameraTransform != null)
        {
            cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
