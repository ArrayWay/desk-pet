# 浮光橙仔 Desk Pet

桌面宠物 + VS Code 联动。支持独立运行，也可在 VS Code 中自动启动与联动。

## 功能

- **桌面宠物**：透明置顶窗口，闲逛 / 待机 / 互动动画
- **训练狗互动**：喂食、嗅闻等访客互动；可玩接飞盘
- **VS Code 联动**：根据编辑、保存、任务、调试等事件切换动作
- **专注模式**：开始 / 停止专注，稍后提醒
- **桌面控制**：显示、隐藏、暂停/继续、退出

## 两种用法

### 1. 联动版（推荐）

安装 VSIX：`fuguang-orange-pet-v1.0.0-vscode.vsix`

1. VS Code → 扩展 → `...` → **从 VSIX 安装…**
2. 选择上述 VSIX，重新加载窗口
3. 默认会自动启动桌面宠；也可命令面板搜索「浮光橙仔」

常用命令：打开桌宠、启动/显示/隐藏/暂停/退出桌面宠物、开始/停止专注。

可选设置：

- `fuguangPet.autoStartDesktop`：是否启动后自动开桌宠
- `fuguangPet.desktopExecutable`：桌宠 exe 路径（留空用扩展内置）

要求：Windows x64，VS Code `^1.90.0`

### 2. 独立桌面版

解压 `Fuguang.DesktopPet-v1.0.0-windows-x64.zip`，运行 `Fuguang.DesktopPet.exe`。  
无需安装 .NET，也不依赖 VS Code。

## 下载

[Releases / v1.0.0](https://github.com/ArrayWay/desk-pet/releases/tag/v1.0.0)
