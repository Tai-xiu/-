# 主菜单 MainMenu（地牢像素风原型 v1）

## 打开方式
场景：`Assets/Scenes/MainMenu.unity`（已作为当前场景打开）

## 目录
```
Assets/Scenes/MainMenu.unity              主菜单场景（Camera + EventSystem + MainMenuCanvas）
Assets/UI/MainMenu/Textures/*.png         全部素材（23 张，纯像素手绘，无 AI）
Assets/UI/MainMenu/Scripts/               MainMenuEntry（按钮含义+点击）、MainMenuController（选中/ESC）
Assets/Editor/MainMenuBuilder.cs          重建场景的工具（菜单：Tools/主菜单/重建 MainMenu 场景）
Assets/Editor/MenuCapture.cs              编辑期把 UGUI 渲染成 PNG 的预览工具
```

## 画布设置
- Render Mode：ScreenSpaceOverlay，pixelPerfect = true
- CanvasScaler：ScaleWithScreenSize，参考分辨率 1920×1080，match = 0.5
- 图标/面板全部 Point 过滤 + 无压缩，9-slice 边框：面板 16px、按钮 8px
- 砖墙贴图 32px，spritePixelsPerUnit = 50 → 每块砖 64×64 参考像素

## 层级结构
```
MainMenuCanvas
├ BG_Wall            砖墙 (Image Tiled)
├ BG_Shade           压暗层
├ Glow_Center        中央暖光
├ Glow_Torch_L/R     火把光晕
├ Torch_L/R + 托架
├ Plaque             石质铭牌 (Sliced 16)
│  ├ Title           DUNGEON（手绘 5×7 像素字，放大 12 倍）
│  ├ Divider
│  └ Subtitle        地牢回响
├ Btn_Start/Continue/Settings/Quit   460×84，SpriteSwap 三态
│  ├ Icon            40×40
│  └ Label           烘焙像素中文
├ Hint / Version     角落小字
└ Vignette           暗角（最上层）
```

## 素材再生成
```
python E:\DSunity\tools\gen_pixel_ui.py
```
脚本会重写 `Textures/*.png`，并额外输出 `E:\DSunity\out\mainmenu_mockup.png` 排版稿。
Unity 里刷新后素材即更新；若改了尺寸/切边，需要跑一次导入设置（见下）。

## 已知限制（v2 待做）
1. **文字是烘焙位图**：按钮标签、标题、角落小字都是 PNG，改文案要改脚本重新生成。
   若要可编辑文本，改成 TMP + 像素字体（工程里已有 `Assets/Fonts/字魂醒狮体.ttf`）。
2. 交互只有点击日志 + 退出，未接场景加载/存档判断。
3. 火把是静态的，没有火焰帧动画。
4. 尚未加入 Build Settings 场景列表。
