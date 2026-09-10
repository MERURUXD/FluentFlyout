# 整改阶段 1：规则与测试入口

请在 `MERURUXD/FluentFlyout` 执行本阶段，只处理规则、上游 CLA 边界和最小测试入口，不提前实现运行时、媒体选择或发布流程修复。

## 先读与核验

- `AGENTS.md` 和 `docs/development/remediation-prompts/README.md`，遵守共用执行边界和交付格式。
- `docs/development/baseline.md`、`docs/development/downstream-policy.md`。
- `FluentFlyoutWPF/FluentFlyout.csproj`、`FluentFlyout.sln`、`FluentFlyoutWPF/.editorconfig`。
- `.github/workflows/downstream-build.yml`、`.github/workflows/dotnet-format.yml`、`.github/workflows/cla-assistant.yml`、`.github/workflows/notify-website.yml`。
- 当前已有测试项目、CI 和 AGENTS 变更；本提示词所在的文档 PR 可能已经完成规则更新，不要为了完成清单重写一遍。

参考审查发现：根规则曾残留“Stage 1 不优化、隔离以后做”的过期指令；CLA 工作流在 fork 上引用上游维护者的协议；当时没有测试项目。先检查这些情况现在是否仍成立。

## 要做的事

1. 校核 AGENTS 是否已明确长期目标、下游约束、文档时态、build/format 命令、验证边界和提交授权。只补仍缺的内容，不把历史问题清单变成永久规则。
2. 为上游 CLA 工作流增加与网站通知工作流相同目的的仓库限制，保留上游原有事件过滤行为。注意多条件表达式的括号和运算优先级，不能让 issue_comment 分支绕过仓库限制。
3. 不修改 GPL、不代替用户制定新 CLA、不请求 OWNER_TOKEN，也不扩大 pull_request_target 权限或运行来自不可信 PR 的代码。
4. 建立最小有效测试项目和显式命令，兼容现有 .NET 10 / Windows x64 构建。优先用一个不访问桌面、音频或网络的生产行为建立测试，例如 Automatic/未知模式的偏好判断；验证测试确实被发现、执行，而不是空项目或恒真断言。
5. 将该测试接入合适的只读 CI 验证入口，并记录实际路径和命令。不要仅为测试入口重构主程序、提前修改下一阶段行为或引入大型 UI 测试平台。
6. 新项目需要时接入 solution，遵守其所在目录的格式约定；不要把 WPF 目录的 EditorConfig 无条件扩展到全仓。

## 验收

- 应用行为、默认值、发布和安装方式不变。
- Windows x64 WPF 构建与 solution format 通过；最终差异检查通过。
- 新增或既有测试命令执行了至少一个有实际断言的生产行为测试。
- CLA 在 fork 的 PR/评论事件上不再执行，上游原有允许事件仍符合原条件。用配置/表达式检查证明，不要通过实际签署或发表评论来测试。
- 若缺乏运行环境或 CI 授权，明确哪些检查未执行，不宣称本阶段已完成运行验证。

按共用格式交付本阶段，停止，不进入阶段 2。
