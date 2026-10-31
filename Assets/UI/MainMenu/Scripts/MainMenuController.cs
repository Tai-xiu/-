using UnityEngine;
using UnityEngine.EventSystems;

namespace DungeonEcho
{
    /// <summary>主菜单根逻辑：启动时保证有按钮被选中（键盘/手柄可直接操作），Esc 记录返回。</summary>
    public class MainMenuController : MonoBehaviour
    {
        public GameObject firstButton;

        void Start()
        {
            if (EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == null
                && firstButton != null)
            {
                EventSystem.current.SetSelectedGameObject(firstButton);
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                Debug.Log("[MainMenu] Esc：返回上一层（原型阶段仅记录）");
        }
    }
}
