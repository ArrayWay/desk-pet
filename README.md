# 浮光橙仔 Desk Pet

私有仓库。**v1.0.0** 提供两套产物：

1. **联动版（推荐）**：VS Code 扩展 VSIX（内嵌桌面运行时）
2. **独立桌面版**：Windows x64 自包含 zip

## 下载

见 [Releases / v1.0.0](https://github.com/ArrayWay/desk-pet/releases/tag/v1.0.0)

| 产物 | 文件名 |
|------|--------|
| 联动版 VSIX | `fuguang-orange-pet-v1.0.0-vscode.vsix` |
| 独立桌面版 | `Fuguang.DesktopPet-v1.0.0-windows-x64.zip` |

## 安装：联动版

1. VS Code → 扩展 → `...` → **从 VSIX 安装…**
2. 选择 `fuguang-orange-pet-v1.0.0-vscode.vsix`
3. 重新加载窗口

完整联动需要扩展 + 桌面进程（Named Pipe）。

## 安装：独立桌面版

1. 解压 zip
2. 运行 `Fuguang.DesktopPet.exe`（自包含，无需安装 .NET）

## 系统要求

- Windows x64
- 联动版：VS Code `^1.90.0`

## 本地整理目录

工作区 `publish/github/` 存放最小发布包与本说明的本地副本。
