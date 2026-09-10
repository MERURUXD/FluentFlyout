# 整改阶段 2：资源生命周期

请在 `MERURUXD/FluentFlyout` 执行本阶段。目标是关闭功能后释放对应工作，以及在重建/重启时保持资源所有权正确；不增加功能，不改变音频处理、绘制质量或媒体选择策略。

## 先读与核验

- `AGENTS.md`、`docs/development/remediation-prompts/README.md`，以及阶段 1 实际建立的测试入口。
- `docs/development/performance.md`、`docs/development/architecture.md`。
- `FluentFlyoutWPF/Classes/Visualizer.cs`、`FluentFlyoutWPF/Controls/TaskbarVisualizerControl.xaml.cs`。
- `FluentFlyoutWPF/MainWindow.xaml.cs` 中可选窗口创建/关闭、媒体回调、显示环境恢复、seekbar timer 与退出路径。
- `FluentFlyoutWPF/Windows/VolumeMixerWindow.xaml.cs`、`FluentFlyoutWPF/Windows/TaskbarWindow.xaml.cs`、`FluentFlyoutWPF/Controls/TaskbarWidgetControl.xaml.cs`。
- `FluentFlyoutWPF/ViewModels/VolumeMixerViewModel.cs`、`FluentFlyoutWPF/ViewModels/UserSettings.cs`、`FluentFlyoutWPF/Classes/AudioDeviceMonitor.cs`。

## 逐项核验并修复

1. **优先修 Visualizer 的重启/释放竞争。** 审查发现 `RequestRestart` 在后台执行 Start，UI 可同时 Dispose；Start 通过最后一次状态检查后才发布 `_capture`，可能让关闭路径漏掉刚创建的 capture，而控制层已经丢弃旧实例。
   - 为状态判断、资源发布、停止和处置建立一致的生命周期协调，处理已排队的重启和绘制回调。
   - 不能只加 `_isDisposed`、volatile 或另一个分散的检查。选择锁或异步串行化时，要核对 StopRecording、事件回调和 Dispatcher 的等待关系，不能引入死锁。
2. **关闭未显示过的 Mixer。** `CloseVolumeMixerWindow` 曾在 `IsLoaded == false` 时只 Dispose ViewModel，跳过 Window.Close/OnClosed。核验 WPF 的所有权和重复关闭行为，统一完整清理，使隐藏窗口不因显示环境重建而积累。
3. **校正动态任务栏的回调所有权。** 媒体回调提前保存 `updateTaskbar`，做完封面工作才解引用可能已经置空的字段。最终应用更新应在 Dispatcher 内重新确认当前窗口、启用和清理状态；不能仅保留一个可能已关闭的旧窗口引用。避免异常跳过后续主浮窗/Next Up 更新或提前提交去重状态。
4. **释放最后一个音量消费者的后台工作。** 这是审查中发现的上游遗留缺口。梳理 Volume Control、Mixer 和任务栏音量消费者，明确隐藏与禁用的不同策略；最后一个消费者退出时停止相应 polling 与订阅，不误停仍被其他消费者使用的资源。
5. **修正 seekbar timer 启停。** 审查发现启动只看 Playing，关闭 seekbar 后仍可能周期唤醒。根据启用、可见、播放和支持状态统一判断，保持原有 seekbar 交互与播放时的更新节奏。

只为上述问题调整必要调用和相应测试/文档。不要清理所有音频历史问题、改 FFT/帧率/图像质量、重写窗口系统或添加常驻轮询来兜底。

## 验收与回归测试

- 通过最小可测试接口和同步屏障控制 capture 构造/发布与 Dispose 的交错，验证关闭后活跃 capture 为零、没有可复活旧实例；多次 enable/disable 和 Dispose 安全。
- 仅启用任务栏而不显示 Mixer，重复显示环境重建，检查 `Application.Windows` 不持续增长；确认窗口级取消与退订路径执行。
- 高频媒体属性更新中关闭/重建任务栏，不因失效引用中断其余有效 UI 更新。
- 分别覆盖从未启用、开启后关闭、仍有共享消费者三种情况；不以“回调马上 return”代替真正停止无用 timer。
- 覆盖设备切换、锁屏/恢复和退出与重启交错；区分自动化模拟与真实 Windows 音频测试。
- 执行 AGENTS 的构建、格式、最终差异检查及相关测试；性能文档只写本阶段已有证据，不宣称测得百分比收益。

每个独立问题保持独立、可解释的变更。按共用格式交付并停止，不进入阶段 3。
