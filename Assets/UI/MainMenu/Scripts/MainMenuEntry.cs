using UnityEngine;
using UnityEngine.EventSystems;

namespace DungeonEcho
{
    /// <summary>主菜单按钮的自描述组件：保存按钮含义，并处理点击。</summary>
    public class MainMenuEntry : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("按钮含义，例如：开始游戏 / 退出游戏")]
        public string label = "";

        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log("[MainMenu] 点击：" + label);

            if (label == "退出游戏")
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
