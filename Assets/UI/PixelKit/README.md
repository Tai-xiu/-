# PixelKit —— 简洁像素风 UI 素材套件 v1

暗棕底 + 铜金边，纯色填充 + 硬边，**全部按九宫格设计**，可任意拉伸不变形。
生成脚本：`E:\DSunity\tools\gen_pixelkit.py`（逐像素绘制，无 AI、无外部依赖）

## 素材清单（23 张，1:1 像素）

| 文件 | 尺寸 | 九宫格 border | 用途 |
|---|---|---|---|
| `panel_frame.png` | 72×72 | 24 | 主窗框（四角括饰 + 金钉） |
| `panel_inner.png` | 40×40 | 12 | 内嵌小节 / 内凹区块 |
| `titlebar.png` | 32×24 | 左右 10 / 上下 4 | 窗口标题条 |
| `tooltip.png` | 48×48 | 14 | 提示框 / 对话框 |
| `slot.png` | 48×48 | 10 | 物品格（内凹） |
| `slot_hover.png` | 48×48 | 10 | 物品格悬停（亮铜圈） |
| `slot_selected.png` | 48×48 | 10 | 物品格选中（金圈加粗） |
| `btn_normal.png` | 40×24 | 10 | 按钮常态 |
| `btn_hover.png` | 40×24 | 10 | 按钮悬停 |
| `btn_pressed.png` | 40×24 | 10 | 按钮按下（内凹） |
| `btn_disabled.png` | 40×24 | 10 | 按钮禁用（灰化） |
| `bar_bg.png` | 16×16 | 4 | 进度条底槽 |
| `bar_fill_hp/mp/sp/exp.png` | 12×12 | 3 | 四条属性条填充 |
| `divider.png` | 64×9 | — | 分隔线（中心菱形） |
| `circle_btn.png` | 72×72 | — | 圆形动作按钮（固定尺寸，别拉伸） |
| `icon_close/minus/plus/lock/caret.png` | 16×16 | — | 窗口控件字形（1:1 或整数倍放大） |

## 调色板

```
墨    #080605    面板  #2E251D    面板亮 #44372A
铜暗  #38291C    铜中  #926E46    铜亮   #C09A60    金 #F2D07C
HP #B0302C   MP #2C68B0   SP #C49E2C   EXP #6CB03C
```

## 导入设置（已自动配好）

- Texture Type = **Sprite**，Filter = **Point**，Compression = **None**，Mipmap = **Off**
- Mesh Type = FullRect，Sprite Border 按上表写入
- Image 组件用 **Sliced** 类型即可，`Pixels Per Unit Multiplier` 保持 1（保持像素 1:1）

## 使用要点

1. **尺寸下限**：九宫格区域不能被压扁。最小尺寸 ≈ 2×border + 4px。例如 `panel_frame` 最小约 52×52，`slot` 约 24×24。
2. **像素对齐**：Canvas 勾选 `Pixel Perfect`；所有 RectTransform 的 position/size 用整数，否则边缘会糊。
3. **想要更"块"的像素感**：把 CanvasScaler 参考分辨率设成 960×540（或整体放大 2 倍整数），像素块会变成 2×2，仍然是清晰的。
4. **不要用 Unity 的 Image 颜色做渐变**——这版是纯色填充，靠边框层次出体积；需要更亮的面板就调 sprite 的 tint，不要拉伸中间区域。

## 已知缺口（v2 待做）

- **物品 / 技能图标**：本套只有窗口控件字形，没有剑、药水、护甲、法术这类内容图标。
- 标题条两端可以再加卷涡纹；目前是简洁直线 + 金点。
- 没有像素中文字体（当前示例文字借用 `Assets/Fonts/字魂醒狮体.ttf`，是抗锯齿的，和像素边不完全统一）。
