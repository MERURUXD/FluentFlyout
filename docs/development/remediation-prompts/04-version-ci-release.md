# 整改阶段 4：版本、CI 与发布

请在 `MERURUXD/FluentFlyout` 执行本阶段，只完善版本、验证门禁、ZIP 发行材料及保留的 MSIX 配置。修改流程不等于执行发布：不要创建 release、移动 dev/stable tag、调用发布 workflow、签名或部署。

## 先读与核验

- `AGENTS.md`、`docs/development/remediation-prompts/README.md` 与前序阶段的实际测试命令。
- `docs/development/release.md`、`docs/development/downstream-policy.md`。
- `FluentFlyoutWPF/Classes/Downstream/ProductVersion.cs`、`FluentFlyoutWPF/Classes/Downstream/ProductIdentity.cs`、`FluentFlyoutWPF/Classes/Services/UpdateCheckerService.cs`。
- `FluentFlyoutWPF/FluentFlyout.csproj`、`FluentFlyoutMSIX/Package.appxmanifest`、`FluentFlyoutMSIX/FluentFlyoutMSIX.wapproj`。
- `.github/workflows/downstream-build.yml`、`.github/workflows/dotnet-format.yml`、`.github/workflows/downstream-dev-release.yml`、`.github/workflows/build-msix.yml`、`.github/workflows/notify-website.yml`。
- `LICENSE`、当前发布输出内容及依赖的实际许可要求。

## 要做的事

1. **明确构建版本来源。** 审查时普通未打包 GitHub Release 未传 Version，新增程序集回退将其识别成 SDK 默认 v1.0.0。
   - 明确开发构建、正式版本、滚动 dev 的身份；默认值可覆盖，不引入第二套容易漂移的版本来源。
   - 未注入发行身份的开发构建不能伪装成正式版本参与比较。dev 显示或诊断信息应包含 SHA，仍不把 prerelease 当作稳定更新。
   - 覆盖开发版本、合法稳定版本、无稳定 release/404、错误标签和网络失败的处理；用可控制的响应测试，不访问真实上游服务。
2. **让 dev 发布依赖同一 SHA 的必要检查。** 审查发现发布仅依赖自己的 publish 构建，已有 format 失败但 dev 成功的情况。
   - 优先使用同一 workflow 的 needs 或可复用 workflow，串联 format、测试、构建/产物检查和最终发布，避免脆弱地查询“最近一次成功”。
   - 保留 PR 不发布、仅下游仓库与 master 允许发布、仅发布 job 有 contents: write 的边界。不能接受来自不可信 PR 或其他 SHA 的产物。
   - 保持已有质量检查有效，不靠取消 format、忽略测试失败或宽泛 continue-on-error 达成“门禁”。
3. **补齐 ZIP 发行材料。** 审查快照的实际 ZIP 未包含 LICENSE/README/NOTICE。
   - 随包提供项目 LICENSE、根据实际依赖要求保留的 notices、下游使用说明、版本/SHA 和对应源码入口。
   - 使用固定 commit 定位源码，不能仅指向会移动的 dev tag。不要臆造第三方许可、把所有依赖都标成 GPL，或删除原署名。
   - 每次使用干净或唯一 publish 目录，检查预期 exe、版本、许可与来源文件；显式处理脚本外部命令失败，避免打包旧残留文件。
4. **校正稳定发布文档。** 要求先提交版本变更、确认工作树干净，再从同一 SHA 构建并打 tag；提供校验和失败停止条件。保持稳定标签不可移动，以及 updater 只读元数据、不会安装的性质。
5. **保留但隔离未支持的 MSIX 路径。**
   - 审查时成功生成的包内 exe 为 `FluentFlyout/FluentFlyoutDownstream.exe`，COM ExeServer 却指向 `FluentFlyoutDownstream/FluentFlyoutDownstream.exe`。按当前真实打包映射修复，不只按程序集名称猜目录。
   - 停止 fork 自动执行上游 MSIX 打包，保留文件和上游行为以便同步。当前不扩展签名、安装或 Store 支持，也不把路径修复宣传成 MSIX 已可交付。

## 验收

- 自动化测试覆盖版本/更新边界；包内文件检查可对本地或非发布 CI 构建执行。
- 从依赖图及可控失败测试确认：format、测试或构建失败时不会进入发布；未获授权时不要为证明这一点运行真实 publisher。
- 明确普通开发构建、正式 ZIP 和 dev 的版本输出，验证对应源码定位信息。
- 对 MSIX 检查构建清单和实际文件路径；未生成实际包时只记录静态验证，不能推断安装/通知激活通过。
- 执行 AGENTS 的构建、格式、最终差异检查及相关测试，更新本阶段直接改变的 release 说明。

按共用格式交付并停止，不进入阶段 5。
