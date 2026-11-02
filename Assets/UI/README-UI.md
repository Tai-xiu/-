> 2026-11-02：现役面板已全部支持 Prefab 编辑。下方旧批次记录仅供历史参考，当前入口与约束请看 Editable-UI.md。

# UI 重构说明（PixelKit + Prefab 化）

> 最后一次更新：批次 3 完成（背包 / 商店 / 任务侧栏 / 告示板 / 搜索面板换肤）
> 本文档面向后续维护：结构、约定、坑、以及"怎么再加一个面板"。

---

## 1. 目标结构

```
GameManager（常驻，跨场景）
└─ GameCanvas（唯一、常驻、ScreenSpaceOverlay）
   ├─ HUDRoot            ← Assets/Prefabs/UI/HUDRoot.prefab
   ├─ Crosshair          ← Assets/Prefabs/UI/Crosshair.prefab
   ├─ InteractPrompt     ← Assets/Prefabs/UI/InteractPrompt.prefab
   ├─ PausePanel         ← Assets/Prefabs/UI/PausePanel.prefab（默认隐藏）
   ├─ DeathPanel         ← Assets/Prefabs/UI/DeathPanel.prefab（默认隐藏）
   └─ （VictoryPanel 特殊：挂在传送黑屏 faderRoot 下，不挂 GameCanvas）

GameManager 上的 GameUIRefs 组件 = 所有 Prefab 引用的唯一入口
EventSystem = 全局唯一（GameManager.EnsureSingleEventSystem 负责）
```

**原则**

1. 控制器脚本**不再创建界面**，只负责"绑定节点 + 更新数值 + 处理交互"。
2. 一个面板 = 一个 Prefab；Prefab 内不含 Canvas / EventSystem / Camera。
3. Camp 与 Dungeon 共用同一套 HUD（`GameUIRefs.EnsureHud` 幂等）。
4. MainMenu 不实例化游戏 HUD（`GameManager.ApplySceneUiState` 里 `GameCanvas.SetActive(false)`）。

---

## 2. Prefab 清单

| Prefab | 路径 | 顶层结构 | 默认状态 |
|---|---|---|---|
| HUDRoot | `Assets/Prefabs/UI/HUDRoot.prefab` | HPBar_Spec / StaminaBar_Spec / HotbarBar_Spec / SkillBar_Spec | 显示 |
| Crosshair | `Assets/Prefabs/UI/Crosshair.prefab` | Line_H / Line_V | 隐藏（锁定才显示） |
| InteractPrompt | `Assets/Prefabs/UI/InteractPrompt.prefab` | Bracket / HoldRing / Hint | 三个子节点各自隐藏 |
| PausePanel | `Assets/Prefabs/UI/PausePanel.prefab` | Title / Btn_Continue / Btn_MainMenu | 隐藏 |
| DeathPanel | `Assets/Prefabs/UI/DeathPanel.prefab` | Title ×2 / Btn_ReturnCamp | 隐藏 |
| VictoryPanel | `Assets/Prefabs/UI/VictoryPanel.prefab` | Title ×2 / LootList / Hint / Btn_Continue | 隐藏（由 Portal 实例化到黑屏父级） |
| InventorySlot | `Assets/Prefabs/UI/Widgets/InventorySlot.prefab` | 单节点（kit slot + Outline） | 模板 |

**HUD 设计尺寸：1280×720**（与 `GameCanvas` 的 CanvasScaler 基准一致）。
`GameUIRefs.ApplyScale()` 会按 `画布基准宽 / 1280` 自动缩放；将来若把画布切到 1920，Prefab 不用改。

### 关键节点名（改名会断绑定）

```
HPBar_Spec/Inner/Fill | Ghost | Tick_1..3
HPBar_Spec/Value                     ← TMP 文本
StaminaBar_Spec/Inner/Fill
HotbarBar_Spec/Slot_1..4             ← 每个 Slot 下有 Count（TMP）
SkillBar_Spec/SkillSlot_Q|E          ← 每个槽下有 Icon / KeyLabel / CdOverlay / CdText
InteractPrompt: Bracket / HoldRing / Hint
PausePanel: Btn_Continue / Btn_MainMenu
DeathPanel: Btn_ReturnCamp
VictoryPanel: Btn_Continue / LootList
```

---

## 3. 代码分层

| 文件 | 角色 |
|---|---|
| `Assets/Scripts/UI/GameUIRefs.cs` | **UI 引用入口**：持有全部 Prefab，提供幂等的 `EnsureHud / EnsureCrosshair / EnsureInteractPrompt / EnsurePausePanel / EnsureDeathPanel / EnsurePanel / CreateVictoryPanel` |
| `Assets/Scripts/UI/UIKit.cs` | **皮肤层**：素材/字体加载、像素画布、工厂方法（`Img / Stretch / Txt / BuildBar`） |
| `HUDController` / `HotbarController` / `SkillHUD` | `BindHud() / BindBar()` 只找节点，找不到就 `Debug.LogError` 并停用（fail-fast） |
| `CursorManager` | `BuildCrosshair()` → `EnsureCrosshair()` |
| `InteractPromptUI` | `BuildUI()` → `EnsureInteractPrompt()` + 按名取 Bracket/HoldRing/Hint |
| `PauseUI` / `DeathHandler` / `Portal` | 面板改由 Prefab 提供，按钮在绑定时挂 `onClick` |
| `InventoryUI` | 已迁移为完整 InventoryPanel Prefab + InventoryPanelView 节点引用，详见 Inventory-Prefab.md |
| `ShopUI` / `QuestPanelUI` / `QuestBoardUI` / `SearchPanelUI` | 已迁移为完整 Prefab + UINodeReferences 节点绑定，详见 Editable-UI.md |

### GameUIRefs 字段

```
hudPrefab             HUDRoot
crosshairPrefab       准星
interactPromptPrefab  四角框 + 蓄力环 + 提示行
pausePanelPrefab      暂停
deathPanelPrefab      阵亡
victoryPanelPrefab    探索成功
inventorySlotPrefab   背包格子模板
```

Camp 与 Dungeon 两个场景的 GameManager 都需要绑定（菜单项会自动写当前场景）。

---

## 4. 字体

| 资产 | 用途 |
|---|---|
| `Assets/Resources/UI/PixelKit/Fonts/ArkPixel12.ttf` | 方舟像素字体 12px（zh_hans），**唯一正文字体** |
| `Assets/Resources/UI/PixelKit/Fonts/OFL.txt` | 字体许可证（OFL，必须随附） |
| `Assets/Resources/UI/PixelKit/Fonts/ArkPixel12Asset.asset` | TMP 字体资产：**RASTER + 采样 12 + Point + 2048 图集 + 多图集** |
| `UIKit.FontCN` | 面板/正文用的 TMP 字体 |
| `UIKit.LegacyFont` | 旧 uGUI `Text` 用的 Font 资产（同一个 TTF），全项目 16 处已统一替换 |

**约定**

- 字号取 12 的整数倍最清晰：正文 12、标题 24。
- 新增文本一律用 TMP（`TextMeshProUGUI` + `UIKit.FontCN`）。
- 旧的 `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` 已全部替换为 `UIKit.LegacyFont`。

---

## 5. 素材（PixelKit）

位置：`Assets/Resources/UI/PixelKit/`（放 Resources 下是为了运行时 `Resources.Load`）

| 文件 | 尺寸 | 九宫格 border | 用途 |
|---|---|---|---|
| panel_frame | 72×72 | 24 | 主面板框 |
| panel_inner | 40×40 | 12 | 内嵌分区/行框 |
| titlebar | 32×24 | 左右10/上下4 | 标题条 |
| tooltip | 48×48 | 14 | 提示框 |
| slot / slot_hover / slot_selected | 48×48 | 10 | 物品格三态 |
| btn_normal / hover / pressed / disabled | 40×24 | 10 | 按钮四态 |
| bar_bg / bar_frame | 16×16 / 24×24 | 4 / 7 | 进度条底槽 / HUD 条框 |
| bar_fill_hp/mp/sp/exp | 12×12 | 3 | 彩色填充 |
| bar_fill_white | 12×12 | 3 | **可染色**填充（受击闪白/低血变红靠它） |
| circle_btn | 72×72 | — | 圆形按钮 |
| icon_close/minus/plus/lock/caret | 16×16 | — | 控件字形 |
| ring_hold | 96×96 | — | 交互蓄力环 |

**导入设置**：Sprite / Point / 无压缩 / 无 mipmap / FullRect / 上表 border。

**使用要点**

- 尺寸下限 ≈ 2×border + 4，小于会被压扁。
- Canvas 需勾 `Pixel Perfect`，位置尺寸用整数。

---

## 6. 生成器脚本（工程外）

位置：`E:\DSunity\tools\`

| 脚本 | 产物 |
|---|---|
| `gen_pixelkit.py` | PixelKit 全部 PNG + 样式总览图 |
| `gen_menu_kit_text.py` | 主菜单像素文字 PNG |
| `gen_pixelfont16.py` | 中英混排像素字体（已被 Ark Pixel 取代，保留作参考） |
| `gen_pixel_font.py` | 5×7 ASCII 像素 TTF（HUD 早期字体） |
| `gen_pixel_ui.py` | 最早的像素菜单素材（含 5×7 字模，被其他脚本 import） |
| `gen_menu_scrim.py` | 主菜单左侧压暗渐变 |

**Prefab 构建器（Unity 菜单）**

| 菜单项 | 作用 |
|---|---|
| `Tools/UI/构建 HUDRoot Prefab` | 生成 HUDRoot 并接线当前场景 GameUIRefs |
| `Tools/UI/构建 准星与交互提示 Prefab` | Crosshair + InteractPrompt |
| `Tools/UI/构建 暂停与阵亡面板 Prefab` | Pause / Death / Victory |
| `Tools/UI/构建 背包格子 Prefab` | InventorySlot 模板 |
| `Tools/主菜单/重建 MainMenu UI（像素风）` | 主菜单（独立场景） |

构建器都是**幂等**的：重复执行只会覆盖同名 Prefab 并重新绑定引用。

---

## 7. 未 Prefab 化面板的现状

这些文件仍是"代码构建 UI"，但**绘制已换成 kit 素材**（改的是 `CreateBorderedBox` / `CreateBox` / 格子创建 / 滚动条 / 关闭按钮 / LineColor）：

- `InventoryUI`：面板 panel_frame、24 格 kit slot、标题/装备槽 panel_inner、关闭 circle_btn、文字暖金
- `ShopUI`：面板、行框、页签（选中金框金字 / 未选中暗框暗字）、商品行按钮、滚动条、图标框
- `QuestPanelUI`：面板、条目/提示框、文字暖金
- `QuestBoardUI`：面板、行框、按钮、关闭
- `SearchPanelUI`：面板、格子、提示框、文字（未截图验证）

**LineColor 是各文件独立常量**（不要跨文件共用）：
背包用暖金（深色面板），搜索/任务曾是黑字（浅底），现已各自独立，避免互相影响。

---

## 8. 坑与约定（重要）

1. **改完脚本必须 Stop → Play**
   Play 模式下重新编译不会重建已生成的 UI，会看到旧画面（表现为 `sprite=NULL`）。验证前先 Stop。

2. **`UIKit.EnsureLoaded()` 要先于取用其静态字段**
   `HUDController.BindHud()` 曾因为没有先调用它，导致 `UIKit.FontCN == null` → 文本回退到 LiberationSans。

3. **不要把 `UIKit.XXX` 作为参数传给尚未调用 EnsureLoaded 的函数**
   `UIKit.BuildBar(..., UIKit.BarWhite, ...)` 首次调用时实参可能为 null；`BuildBar` 内部已加兜底。

4. **1280 基准 + 1920 屏幕 = 1.5 倍缩放**
   像素素材会略软。要绝对锐利需切 1920 基准（`GameUIRefs` 会自动补偿、Prefab 不用改）。

5. **不要在面板里再建 Canvas / EventSystem**
   Canvas 由 GameManager 统一创建，EventSystem 由 `EnsureSingleEventSystem` 去重。

6. **`GameUIRefs.Instance` 可能被重复 GameManager 清空**
   `EnsureHud()` 里已加 `Instance = this` 兜底；新增 Ensure 方法时同样要加。

7. **胜利面板挂在黑屏父级**
   不要改成挂 GameCanvas，否则层级会盖住传送淡出效果。

---

## 9. 新增一个面板的标准步骤

1. 写 Prefab 构建器（参考 `UIPanelPrefabBuilder`），在 1280×720 坐标系里排版。
2. 加字段到 `GameUIRefs`（`xxxPanelPrefab`），需要幂等的加 `EnsureXxxPanel()`。
3. 构建器里调用 `SetupScene()` 写入当前场景的 GameUIRefs（Camp / Dungeon 各执行一次）。
4. 控制器把 `BuildXxx()` 改成 `BindXxx()`：只 `Find` 节点 + 挂 `onClick`，**不 new UI**。
5. Stop → Play 验证；改 Prefab 外观应无需改脚本。

---

## 10. 遗留事项

| 项 | 说明 |
|---|---|
| 背包属性区 HP 条 | 仍是代码画的浅色横条 |
| 背包装备槽虚线框 | `DashedArea` 仍是代码画的虚线 |
| 任务侧栏滚动条轨道 | 未换成 `bar_frame`（结构与其他面板不同，需单独看） |
| 搜索面板 | 代码已改，需实机搜刮验证 |
| HUD 数字字体 | 已切 Ark Pixel（24px），与面板一致 |
| 主菜单 | 仍在 MainMenu 场景，用烘焙像素文字，与本套 Prefab 体系独立 |

---

## 11. 当前整体状态

| 模块 | Prefab | kit 外观 | Ark Pixel |
|---|---|---|---|
| HUD（血/体力/快捷栏/技能栏） | ✅ | ✅ | ✅ |
| 准星 / 交互提示 | ✅ | ✅ | ✅ |
| 暂停 / 阵亡 / 胜利 | ✅ | ✅ | ✅ |
| 背包 | ✅（完整面板与动态内容模板） | ✅ | ✅ |
| 商店 | ✖ | ✅ | ✅ |
| 任务侧栏 / 告示板 | ✖ | ✅ | ✅ |
| 搜索面板 | ✖ | ✅（未验证） | ✅ |
