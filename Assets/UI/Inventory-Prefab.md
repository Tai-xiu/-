# 背包 Prefab 编辑说明

主体：`Assets/Prefabs/UI/InventoryPanel.prefab`。UI.scene 中已有该 Prefab 实例，编辑状态直接可见。
物品图标：`Assets/Resources/UI/InventoryItemVisual.prefab`，背包、拖拽、搜索面板和 HUD 共用。
格子：`Assets/Prefabs/UI/Widgets/InventorySlot.prefab`。

## 手动编辑

- 在 Prefab 模式修改主体；也可调整 UI.scene 实例后通过 Overrides 有选择地 Apply 回 Prefab。
- LayoutGroup 管理的节点请调整 LayoutElement 的 Preferred Width/Height、布局间距与 Padding；直接拖动受布局驱动的 RectTransform 会被布局系统恢复。
- GridArea 的 Cell Size、Spacing、Padding 可调整。保持固定五列、从左上角按行排列，并同步调整左栏宽度以容纳五列。
- 根节点 InventoryPanelView 保存所有节点引用，支持节点改名与调整层级。删除或替换组件后要在 Inspector 重新绑定相应字段。
- 静态十格用于编辑预览，运行时按当前容量增减到10～40格；新增格子从 InventorySlot 模板实例化。现有十格是 InventorySlot 的嵌套实例；统一修改模板会同步到现有格子及扩容生成的格子，单格 Overrides 仍可独立编辑。
- SplitPopup、CellMenu、MenuButtonTemplate 默认隐藏，可临时勾选 Active 调整。编辑后恢复隐藏；右键菜单按可用动作实例化按钮模板。
- InventoryItemVisual 的 Fallback 是无物品图标时的文字。字体/字号/颜色与图标排版可编辑；Tint By Type 决定是否以物品类型着色。
- InventoryPanelView 的 Rarity Tint 控制品质染色强度，设为0保留格子底色。

## 代码职责

InventoryUI 绑定 Prefab、订阅 Inventory 事件、刷新数据和处理交互，不重建主体布局。
InventoryPanelView 只保存引用并检查关键配置。
InventoryItemVisual 根据物品更新图片、文字、旋转和显隐。
InventoryUI.Authoring.cs 与 InventoryPanelPrefabBuilder 仅用于编辑器首次迁移，已存在完整 Prefab 时拒绝覆盖。进入游戏不会执行生成器。
Camp/Dungeon 的 GameUIRefs 已绑定完整面板；UI.scene 不用于游戏运行时 Additive 加载。

## 已做验证

Play模式：开关面板、基础10格/扩容40格/缩容10格、右键动作、拆分按钮、快捷栏绑定、拖拽图标和旋转、修改格子尺寸/间距/边距后的物品对齐、Camp到Dungeon的数据恢复与唯一面板。
所有测试数据仅在Play模式生成，退出后还原。鼠标真实手感与更多分辨率仍可按实际美术调整继续验收。
