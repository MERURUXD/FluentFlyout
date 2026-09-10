# 整改阶段 3：媒体会话边界

请在 `MERURUXD/FluentFlyout` 执行本阶段，只修媒体会话事件与显示所有权边界，保留统一选择入口和现有状态机，不新增轮询、Spotify Exclusive 模式或默认行为变化。

## 先读与核验

- `AGENTS.md`、`docs/development/remediation-prompts/README.md`，以及前序阶段的测试和回调所有权处理。
- `docs/development/architecture.md` 的选择规则与场景表。
- `FluentFlyoutWPF/Classes/Downstream/MediaSessionSelectionPolicy.cs`。
- `FluentFlyoutWPF/MainWindow.xaml.cs` 的 `GetActiveMediaSession`、`RefreshFilteredMedia`、事件订阅/退订、媒体属性去重、Next Up 和全部单会话消费者。
- `FluentFlyoutWPF/ViewModels/UserSettings.cs`、`FluentFlyoutWPF/Pages/MediaFlyoutPage.xaml`。
- 当前锁定的 WindowsMediaController 依赖实现，核对首次会话事件、Start 时序、回调线程和会话对象生命周期。参考审查使用的是 2.5.6，不假定版本永远不变。

## 要修的边界

1. **新增未选中会话仍可能改变选择结果。**
   - 最小场景：Spotify Preferred；当前没有 Spotify，暂停的 A 有 Windows 媒体焦点，B 正在播放，选择回退到 A。新增暂停的 Spotify 后，策略改选 B；Spotify 的首次 metadata 因“非选中会话”提前返回，显示可能仍是 A，控制却操作 B。
   - 为会话打开/集合变化提供已有刷新入口的调用，正确使用 Dispatcher 并对称退订。核对启动期间的回调顺序，避免在 manager 未就绪时访问它。
   - 保留未选中 metadata 不直接覆盖显示的原则，不依赖假定一定到来的首次 playback 事件。
2. **去重记录不能跨已失效的会话所有权。**
   - 最小场景：可见主浮窗处理过 S 的歌曲，S 关闭后 UI 清空；同 ID 新会话以相同标题、歌手、状态、封面重启，旧去重键可能让首次 metadata 在恢复 UI 前返回。测试应关闭 seekbar 并控制后续事件，避免其他刷新掩盖问题。
   - 在会话关闭/显示所有者失效时清理去重，或附加会话实例身份。允许用于短期 UI 所有权/去重的实例标记，不建立长期缓存的选中会话。
   - 核验延迟回调跨会话切换、窗口重建后的所有权，保持前一阶段任务栏回调修复有效。
3. **保留所有单会话消费者的一致性。** 主浮窗、任务栏、播放控制、seek、媒体应用音量、打开播放器、Next Up 都应从同一选择规则取得目标。

## 必须保持的选择行为

- 先应用黑白名单，再应用偏好；被过滤的 Spotify 不能胜出。
- Automatic 与未知模式保持 focused-then-first 回退；设置仍保持原有持久化数值与默认值。
- 仅在 Spotify Preferred 模式下，Spotify 播放时优先；Spotify 存在但暂停时可选正在播放的非 Spotify 会话；没有适用偏好时使用原自动选择。
- 没有 Spotify 时不擅自把 Automatic 回退改成“优先任何播放中的会话”。
- Spotify Web Player 仍属于浏览器会话，不凭标题/歌手推断应用身份。保留独立的 Pause Other Sessions 行为。

## 验收

- 将上述两个问题的事件序列做成回归测试；断言显示所有者和实际控制目标，而不仅测试筛选结果。
- 覆盖 Spotify/浏览器播放与暂停组合、多个候选、过滤、未知模式、关闭重启、相同 metadata、迟到回调及 Next Up 所有权。
- 若纯策略测试需要接口，只提取最小行为边界；不能复制生产策略或以测试为由重构整个协调器。
- 依赖本身按 ID 保留底层旧对象的“原地替换”风险要单独标明。不要把每次重读集合宣称成依赖重新订阅的保证，不在本阶段升级或 fork 依赖。
- 执行 AGENTS 的构建、格式、最终差异检查及相关测试；实际 Windows 播放器场景未测则明确保留。

按共用格式交付并停止，不进入阶段 4。
