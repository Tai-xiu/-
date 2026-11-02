# 可编辑 UI Prefab 使用说明

2026-11-02：现役界面使用 Prefab 保存外观与层级；运行时代码绑定节点、刷新数据及处理交互。

## 编辑入口

| 界面 | Prefab | 独立编辑预览场景 |
|---|---|---|
| 商店 | Assets/Resources/UI/Panels/ShopPanel.prefab | Assets/Scenes/UIEditing/ShopPanel/UI.scene |
| 任务侧栏 | Assets/Resources/UI/Panels/QuestPanel.prefab | Assets/Scenes/UIEditing/QuestPanel/UI.scene |
| 协会悬赏板 | Assets/Resources/UI/Panels/QuestBoardPanel.prefab | Assets/Scenes/UIEditing/QuestBoardPanel/UI.scene |
| 搜索结果栏 | Assets/Resources/UI/Panels/SearchBarPanel.prefab | Assets/Scenes/UIEditing/SearchBarPanel/UI.scene |
| 主菜单 | Assets/Prefabs/UI/MainMenuPanel.prefab | Assets/Scenes/MainMenu.scene（原固定机位） |
| 背包 | Assets/Prefabs/UI/InventoryPanel.prefab | 原 Assets/Scenes/UI.scene |

双击 Prefab 可以编辑，也可打开对应预览场景直接看到完整界面。预览场景只用于制作，不加入构建列表、不作为游戏附加场景加载。预览场景的侧栏居中位置是实例覆盖，游戏中的左右位置仍由 Prefab 保存。

在预览场景修改实例后，要通过 Overrides 有选择地 Apply 回 Prefab，游戏中才会使用这些修改。建议直接在 Prefab 模式编辑。

## 主体与动态条目

- 商店 Row 节点：商品行模板，包含图标位、名称、属性、价格、动作按钮；购买和出售共用。BuyPage/SellPage 决定编辑时查看哪一页。
- 任务侧栏 Row 节点：契约条目模板，Header 是展开按钮，Info 是详细信息。预览默认展开；游戏按已保存的展开状态显示。
- 悬赏板 Row 节点：契约行模板，包含名称描述、状态、奖励和动作按钮。已领取且无动作时保留按钮列的布局空间。
- Empty 节点：空列表模板，默认可以隐藏；勾选 Active 可编辑空状态。
- 搜索栏：12个真实格子，固定3列4行。修改格子尺寸后同步调整网格高度与面板宽度；图标读取实际格子尺寸。
- 编辑状态显示的示例商品/契约仅用于预览；运行时隐藏模板，按真实数据实例化，不生成占位数据。

LayoutGroup 驱动的节点应修改 LayoutElement 的 Preferred Width/Height、Padding、Spacing；直接拖动其 RectTransform 位置会被布局系统恢复。修改背包宽度时，请同时调整任务侧栏和搜索栏的横向位置。

## 节点绑定

新增 UINodeReferences 组件通过 Key → Target 显式保存节点引用。

- 允许物体改名或移动层级，绑定仍然有效。
- Key 是代码接口，保持不变；删除或替换组件后，在 Target 中重新绑定对应组件。
- Row/Empty 引用的是条目模板自己的 UINodeReferences 组件。
- Selected Text、Inactive Text、Selected Tint、Inactive Tint 及页签 Sprite 等状态样式可在 Inspector 编辑。
- Rarity Tint 是搜索格子的品质染色强度，0保留模板底色。
- 代码只设置数据文字、交互回调、动态状态与必要尺寸；字体、字号和静态排版保存在 Prefab。

## 辅助显示模板

以下路径位于 Assets/Resources/UI/Panels/：

- FloatingTextLabel.prefab：世界坐标投影的漂浮提示。
- EatRing.prefab：食用进度环；外观来自模板，进度和所在图标由代码控制。
- SkillFallback.prefab：没有技能图片时的文字占位。
- SceneFader.prefab：跨场景遮罩，含独立 Canvas 和 GraphicRaycaster；遮罩透明度由传送控制。
- AlertMark.prefab：怪物头顶警戒感叹号，保留字体与初始位置供编辑。
- EnemyHealthBar.prefab：世界空间怪物血条，EnemyHealthBarVisual 保存背景/填充引用、低血颜色及背景透明度；原逐怪物宽高配置仍按比例生效。

原 HUDRoot、Crosshair、InteractPrompt、PausePanel、DeathPanel、VictoryPanel 已经是 Prefab，继续直接编辑原资产。准星、HUD等废弃的运行时构建分支已移除。GameCanvas 和 EventSystem 仍由全局系统维护，属于运行基础设施。

主菜单的 MainMenuRoot 控制器已纳入 MainMenuPanel，开始/退出的原有序列化事件保存为内部引用。原来无动作的按钮保持原行为；镜头和大厅场景不包含在界面 Prefab 中。

## 首次迁移工具

Tools/UI/迁移其他界面为可编辑 Prefab（仅首次）
Tools/UI/生成辅助 UI Prefab（仅首次）

这些工具只在目标资产不存在时生成，不覆盖已有手工调整。*.Authoring.cs 仅在编辑器编译，游戏启动不运行生成器。

## 修改文件与验证

主要控制器：ShopUI、QuestPanelUI、QuestBoardUI、SearchPanelUI。
新增节点组件：UINodeReferences、EnemyHealthBarVisual。
新增编辑器生成器：EditableUIPrefabBuilder、SupportingUIPrefabBuilder，以及四个控制器的 Authoring 部分。
辅助显示接入：FloatingText、HotbarController、SkillHUD、Portal、MonsterAI、EnemyHealthBar。
清理已无调用的构建代码：HUDController、InteractPromptUI、PauseUI、DeathHandler、CursorManager。
主菜单场景接入完整界面 Prefab；未主动保存或覆盖原 UI.scene。

已进行 Play 模式程序化验证：商店四分类/购买/出售/空列表；悬赏接取/领奖/放弃/日契刷新；任务侧栏展开与刷新后保留；搜索12格/拖拽图标/关闭保留战利品/拾取写回尸体/重复空搜索；辅助进度环与遮罩实例化；Camp→Dungeon→Camp 唯一面板和清理；Dungeon五个血条与五个警戒标记。

测试仅使用Play临时数据；契约测试使用独立临时ID并清理其测试存档键，没有改动原有契约进度。运行截图检查过商店、悬赏板与背包左右附属栏。真实鼠标手感、极端字体和更多分辨率仍可在美术调整时进一步验收。
