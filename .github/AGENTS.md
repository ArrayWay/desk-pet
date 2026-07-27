# desk-pet 代理运行规范

这个仓库包含一个 WPF 桌宠（`desktop-wpf/`）和一个 VS Code 扩展（`vscode-extension/`），共享资源位于 `shared/`。

## 强制交接入口

当用户要求 handoff / 交接 / drop baton / grab baton / pick up later，或工作必须留给后续会话继续时，必须先读取并执行 `.github/skills/handoff/SKILL.md`。该 skill 是交接流程、baton 位置、验证步骤和模板结构的唯一规范来源；不要在其他文件重复展开这些细则。

## 路径与输出规则

- 优先把输出和临时文件放在本仓库或 `D:\ai_tools\ai_sandbox` 下。
- 除非用户明确批准，不要把项目产物、临时文件或缓存写到 `C:\Users`。

## 项目定位

- `desktop-wpf/Fuguang.DesktopPet/` 是主应用。
- `vscode-extension/` 是配套 VS Code 扩展。
- `shared/` 存放共享动画和 companion 数据。
- `tmp/` 是可丢弃的本地研究和验证输出。

## 日常启动与 build 同步（强制）

本地桌宠**日常启动入口**固定为：

- `publish/windows/release/Fuguang.DesktopPet.exe`（与工作区 `fuguangPet.desktopExecutable` 一致）

不要引导用户长期从 `desktop-wpf/.../bin/Release/...` 启动做功能验收；`bin` 仅作编译中间输出。

### 规则

1. **`dotnet build` 之后必须同步更新 release 启动目录**，避免用户/扩展仍打开旧 DLL。
2. 工程已在 `Fuguang.DesktopPet.csproj` 挂 `SyncLaunchDirectories`：
   - 任意配置 `build` 成功后 → 同步到 `publish/windows/{debug|release}/`
   - `Release` 额外同步 → `vscode-extension/desktop/`
3. 代理在改完桌宠代码后，**默认执行**（并确认日志出现 `Synced build output`）：

```powershell
$env:DOTNET_CLI_HOME = "$PWD\tmp\dotnet-home"
$env:NUGET_PACKAGES = "$PWD\tmp\nuget-packages"
dotnet build .\desktop-wpf\Fuguang.DesktopPet\Fuguang.DesktopPet.csproj -c Release
```

4. 若因特殊原因关闭了同步（`-p:SyncLaunchDirectories=false`）或只改了资源却未触发重编，**必须手动**把最新输出同步到 `publish/windows/release`（及 Release 时的 `vscode-extension/desktop`），再让用户重启进程。
5. **完整发布/打包**（VSIX、`publish/vscode/*`）仍用 `tools\publish.ps1`；日常迭代不要求每次跑完整 publish，但 **Windows release 启动目录不得落后于当前源码构建**。
6. 用户反馈「没有新菜单/新功能」时，先核对 `publish/windows/release\Fuguang.DesktopPet.dll` 与 `bin\Release\...\Fuguang.DesktopPet.dll` 的时间戳与体积是否一致，并确认已退出旧进程后再启动 release 路径。
