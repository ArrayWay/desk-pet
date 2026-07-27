---
name: handoff
description: "在把未完成工作交接给其他代理或后续会话时使用；当用户说 handoff/交接/drop baton/grab baton/pick up later、上下文不足，或工作必须在别处继续时使用。覆盖编写已验证的交接文档以及从最新交接文档恢复。"
---

# 交接（desk-pet）

以下规则综合了多个 GitHub 上较受欢迎的 handoff 方案：

- blader/baton —— 经过 git 校验的会话 baton、固定章节、drop/grab 流程
- ToolMonsters/handoff-skill —— 可移植的对话交接、WIP 逐字保留、零编造
- REMvisual/claude-handoff —— 深度挖掘、链路连续性、具体下一步

## 核心原则

记录下一位执行者无法仅靠代码或 git 重新推导出来的信息：

1. 目标意图与验收标准
2. 已经排除的死胡同，以及为什么排除
3. 精确的下一步动作
4. 尚未完全落盘时，正在进行的草稿/代码/计划要逐字保留

所有内容都要基于已验证的仓库状态，而不是聊天叙事。

## 何时使用

**在以下情况写交接（Drop）：**

- 会话结束但工作未完成
- 用户说：handoff、交接、drop baton、pick up later、pass to another agent
- 上下文不足 / 即将压缩
- 剩余工作要转给其他会话

**在以下情况接回（Grab）：**

- 开始工作时，`.baton/` 里可能已有前序交接
- 用户说 grab baton / continue from handoff / 接上一次

**不要用于：** 已完成的任务，或应该放在长期文档中的稳定架构说明（如 `DEVELOPMENT_PLAN.md`、增长计划、仓库记忆）。

## 位置

- 目录：仓库根目录下的 `.baton/`（不存在就创建）
- 文件名：`YYYY-MM-DD-short-slug.md`
- 仅作本地临时草稿，必须保持 gitignore
- 最新交接：按修改时间/文件名取 `.baton/` 下最新文件

如果用户要在其他模型里继续，可以额外在对话中给出可移植的 `handoff-<topic>.md` 内容。

## 铁律：写之前先验证

写之前先做：

1. `git status`
2. `git branch --show-current`
3. 记录已提交、未提交、推测内容的区别

如果聊天叙述和 git / 文件系统冲突，要写现实状态，并明确标注差异。

## Drop 流程

1. Verify git/branch/working tree
2. Create `.baton/` if needed; ensure `.gitignore` has `.baton/`
3. Write full fixed-section handoff (empty sections = `none`, never delete sections)
4. Reply with exact path + one-line summary of next action

## Grab 流程

1. List `.baton/` and read the newest file
2. Re-verify State of Play against current `git status` (repo may have moved)
3. Continue from Immediate next step
4. When work lands: delete the baton, or fold durable learnings into canonical docs then delete

## 交接模板（所有章节都必须保留）

```markdown
# 交接：<一句话标题>

**TL;DR：** <1-3 句：这是什么、目前到哪一步、唯一下一步是什么>

## 目标与意图
为什么要做这个，最终希望达到什么状态，验收标准是什么（如果已知，写出精确的命令/测试）。

## 关键决策
已经敲定、后续代理不要再反复争论的选择。用要点列出，并简短写明原因。

## 当前状态（已验证）
- 分支：<来自 git>
- 已提交：<已经落地的内容>
- 未提交 / 进行中：<来自 git status>
- 已验证可用：<实际跑过并确认的内容>
- 假设 / 未验证：<尚未证明的判断>
- 与聊天叙述的差异：<无 | 具体描述>

## 进行中的内容（逐字保留）
仍在进行、且无法仅靠磁盘安全重建的草稿 / 代码 / 计划，要完整逐字贴出。
如果所有内容都已经在磁盘上，写 `none`，并指出路径。

## 收获与坑点 / 已排除路径
这是最有价值的章节：非显而易见的发现、死胡同、禁止触碰的区域，以及原因。

## 参考
- 带定位的路径：path:line —— 那里有什么
- 权威文档 / 之前的 baton / PR
- 用来熟悉环境或复现的命令

## 语气与偏好（如果已提到）
把用户偏好写成执行指令。若没有，写 `none`。

## 立即下一步
一项具体的首个动作，不要写“继续工作”这种空话。

## 未决问题
尚未解决的决定，以及当前倾向。若没有，写 `none`。
```

## 严格规则

1. 零编造——只写对话与已验证工具能支持的事实；不清楚就写 `unclear`
2. 对于尚未完全落盘的中间文本，逐字保留优先于总结
3. 路径、数字、日期、URL 必须完全保持原样
4. 面向陌生人写作，不要写“如上所述”这类省略语
5. 优先用高密度的代理间交接，而不是长篇散文
6. 不要提交秘密信息；handoff 只做本地临时草稿
7. 优先使用工作区本地输出（本仓库 / D:\ai_tools\ai_sandbox）；未经批准不要把项目产物写到 C:\Users

## desk-pet 目录提示（用于接回）

常见结构：

- `desktop-wpf/` —— WPF 桌宠
- `vscode-extension/` —— VS Code 扩展
- `shared/` —— 共享动画 / companion 资源
- `tools/` —— 工具脚本
- `tmp/` —— 本地临时研究与验证输出（不可长期依赖）
- `.venv-1/` —— 存在时的本地 Python 环境
- 计划文档：`DEVELOPMENT_PLAN.md`、`DOG_COMPANION_*`、`GROWTH_SYSTEM_EXECUTION_PLAN.md`

长期项目记忆可以放在仓库 memory notes 里；短期会话状态放在 `.baton/`。

## 常见错误

| 错误 | 修正 |
|---------|-----|
| 只是重复整个 diff | 写意图、死胡同、下一步 |
| 只根据聊天内容写 | 先用 git 验证 |
| 下一步太空泛 | 写出字面上的第一个动作 |
| 忽略死胡同 | 这是最高价值内容 |
| 放错位置 | 永远放在仓库根目录的 `.baton/` |
| baton 越积越多 | 任务落地后及时退役 |

## 回复要求

完成 Drop 后：写明具体路径 + 一行下一步。
完成 Grab 后：写明加载了哪个 baton + 你会先做什么。