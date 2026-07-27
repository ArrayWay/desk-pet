# 访客 Profile 接入指南

## 接入边界

- 每个访客使用稳定、大小写不敏感的英文 ID；发布后不要修改。
- 身份基准图和至少一套完整动作素材通过 QA 后再注册 Profile，不提前生成或注册占位访客。
- 同时只允许一个活动访客；主宠不进入访客注册表。
- Profile 只声明真实具备的能力。声明 `Fetch` 时必须提供球素材，声明 `Feeding` 时必须提供食盆素材。

## 资源布局

资源放入 `shared/companions/`，项目会自动将其打包到 Windows Release 和 VSIX：

```text
shared/companions/
  <visitor-id>.png
  <visitor-id>/
    idle/*.png
    <state-name>/*.png
    ball.png                 # 仅 Fetch 访客需要
    food-bowl.png            # 仅 Feeding 访客需要
```

每个 Profile 必须声明 `Idle`，每个声明状态目录至少包含一帧 PNG。目录名必须与 `VisitorProfile.States` 中的值完全一致。

## 代码接入

1. 在 `VisitorProfile.cs` 定义新的静态 Profile，填写 ID、显示名称、资源目录、基础图、能力和状态映射。
2. 将 Profile 加入 `VisitorProfile.Registered` 的单一清单；查找索引和访客选择菜单会自动更新。
3. 不在 `MainWindow` 增加物种分支。菜单、反馈、资源加载和玩法入口必须继续由 Profile 与能力驱动。
4. 若新访客需要现有能力之外的玩法，先扩展通用能力与生命周期，再注册该访客。

## 自动验证

```powershell
$env:DOTNET_CLI_HOME = "$PWD\tmp\dotnet-home"
$env:NUGET_PACKAGES = "$PWD\tmp\nuget-packages"
dotnet build .\desktop-wpf\Fuguang.DesktopPet\Fuguang.DesktopPet.csproj -c Release
npm run check --prefix .\vscode-extension
.\tools\publish.ps1 -Configuration Release
.\tools\smoke-test.ps1 -PublishDirectory .\publish\windows\release
```

发布前确认：

- Profile 校验通过，错误路径中没有缺失的基础图、状态帧或能力素材。
- Windows Release 与 VSIX 都包含新访客资源。
- 设置保存稳定的 `Visitor.ActiveVisitorId`，无旧 `DogCompanion*` 字段。
- 单实例、IPC、显示、隐藏和退出 smoke test 通过。

## 多访客人工验收

- 注册数为 1 时不显示“选择访客”；注册数大于 1 时才显示。
- 切换后菜单标题、召唤文案、玩球和找 Bug 项与新 Profile 的名称和能力一致。
- 活动访客切换时，旧窗口、追逐、取球和球窗口全部关闭，只保留一个新访客窗口。
- 声明 `Handshake` 的访客应提供邀请和成功状态；握手由窗口内的限时邀请和局部命中区域完成。
- 当前训练犬的左键单击用于启动握手邀请；右键单击用于摸头回应，摸头使用 `petting-response` 状态并保留原位轻微脸部特写。
- 声明 `Feeding` 的访客应提供 `food-bowl.png` 和嗅闻、进食、感谢状态；食盆必须随隐藏、退出、切换和高优先级中断清理。
- 资源无效的目标 Profile 不应替换当前访客，也不应影响主宠运行。
- 重启后恢复 `ActiveVisitorId`；多显示器和高 DPI 下切换后访客仍位于可见工作区。