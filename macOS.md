# macOS 适配详细实施计划（审核版）

## 1. 范围基线

本计划严格限定为：

- 目标系统：**macOS 26 Tahoe 最新正式版**。
- 部署下限：`macOS 26.0`。
- 开发 SDK：最新正式版 Xcode 26/macOS 26 SDK。
- 不兼容 macOS 25 及更早版本。
- 不以 macOS 27 Beta 作为发布门槛；目前 Apple 将 macOS 27 标记为 Beta，而 macOS 26 是正式版本。
- 不新增业务功能，只让 README 中现有功能在 macOS 上达到功能一致性。
- 不在 Application 或 Presentation 中加入 macOS/Windows 分支。
- Windows 现有实现和行为不能回归。

### 架构范围

按照 `DDD_ARCHITECTURE.md`，新增且仅新增两个生产程序集：

```text
src/
  Infrastructure/
    EasyChat.Infrastructure/
    EasyChat.Infrastructure.Windows/
    EasyChat.Infrastructure.MacOS/
  Host/
    EasyChat.Desktop/
    EasyChat.Desktop.Windows/
    EasyChat.Desktop.MacOS/
```

测试项目新增：

```text
tests/
  EasyChat.Infrastructure.MacOS.Tests/
```

依赖必须保持：

```text
EasyChat.Contracts
        ▲
        │
EasyChat.Infrastructure.MacOS

EasyChat.Desktop
EasyChat.Infrastructure.MacOS
EasyChat.Presentation
        ▲
        │
EasyChat.Desktop.MacOS
```

macOS Infrastructure 禁止引用：

- Avalonia。
- Presentation。
- Application。
- Desktop。
- Windows Infrastructure。

macOS Host 只负责：

- macOS 入口。
- 平台依赖注册。
- Avalonia 到 NSWindow 的桥接。
- worker 入口。
- 打包和部署初始化。

### CPU 架构假设

本计划默认首发 **Apple Silicon、`osx-arm64`**。

原因是当前 OCR 技术栈已经存在 macOS ARM64 OpenVINO runtime，而加入 Intel 会额外引入原生库、worker、签名和双架构一致性问题。

如果审核要求“所有能运行 macOS 26 的 Intel Mac 也必须支持”，需要把以下内容加入计划：

- `osx-x64` 独立制品。
- OpenVINO x64 runtime 手工打包或替代包。
- OpenCV x64 runtime。
- Intel 真机 CI/验收。
- 所有 native dylib 的双架构验证。

不建议第一版合并为 universal `.app`；独立 ARM64/x64 制品更容易隔离原生依赖。

## 2. 不可违反的架构规则

整个实施期间，每个阶段都必须检查以下约束：

1. Domain、Application 不引用 Avalonia 或 macOS API。
2. Presentation 不引用 AppKit、CoreGraphics、CoreAudio、ScreenCaptureKit。
3. macOS Infrastructure 不引用 Avalonia。
4. 原生类型不得穿过 Contracts：不暴露 `NSWindow*`、`AXUIElementRef`、`SCDisplay`、`AudioDeviceID` 或 PID。
5. `ExternalTargetToken` 和 `AudioCaptureSourceToken` 继续保持不透明、会话级，不持久化。
6. 不创建 `MacPlatformService` 一类 God Service。
7. 每个适配器只实现一个能力边界。
8. 权限不足必须返回 `PermissionRequired` 或失败，不允许假装成功。
9. 授权后必须重新查询 capability，符合 `DDD_ARCHITECTURE.md` 的权限语义。
10. 平台物理坐标统一转换为契约规定的左上角物理像素空间。
11. 不为了代码复用创建抽象基类。
12. 只在存在真实边界或独立可变策略时新增接口。
13. macOS 代码按功能目录组织，不建立 `Services`、`Models`、`Helpers` 等机械分类目录。
14. 所有平台条件只能存在于平台项目或 Composition Root，不能渗入业务流程。
15. 不修改现有翻译、OCR、字幕或输入的业务策略，除非是在移除 Windows 语义泄漏。

## 3. 分阶段实施计划

### 阶段 0：建立不可变功能基线

预计：1～2 人日。

**状态：已完成（2026-09-04）**

#### 任务

- [x] 根据 README 和现有快捷键动作建立功能清单：
   - 截图翻译。
   - 截图 OCR。
   - 长截图。
   - 图片翻译。
   - 划词翻译。
   - 快捷翻译。
   - 输入翻译并写回。
   - 润色、总结、纠错。
   - 系统音频实时字幕。
   - 应用音频实时字幕。
   - 麦克风同声传译。
   - TTS 预览和输出。
   - 托盘、开机启动、更新、单实例。
- [x] 为每项功能记录 Windows 当前行为：输入和输出、权限前提、失败语义、剪贴板保存、焦点、多显示器行为和 worker 生命周期。
- [x] 固定 macOS 首发范围：macOS 26、ARM64、Developer ID 站外发行、DMG/ZIP、不进入 Mac App Store、不启用 App Sandbox。
- [x] 记录适配前测试基线，并明确区分 macOS 主机实测结果与仍需 Windows runner 验证的结果。

#### 功能一致性矩阵

下表是 macOS 实现必须保持的功能语义。平台权限和原生实现可以不同，但 Application 可观察到的输入、输出、取消及失败行为不能降级。

| 功能 | 输入与输出基线 | Windows 当前行为 | 权限/失败语义 | 剪贴板、焦点、多屏与 worker 基线 | macOS 对应阶段 |
| --- | --- | --- | --- | --- | --- |
| 截图翻译 | 用户框选物理屏幕区域，输出 OCR 区域和翻译结果窗口 | `WindowsScreenshotCaptureSession` 驱动覆盖层，`WindowsScreenCapture` 采集 BGRA32，再进入共享 OCR/翻译流程 | Windows 报告 `ScreenCapture=Available`；取消框选不产生伪结果，采集/OCR/翻译错误按 `Result` 返回 | 使用统一桌面物理像素和每屏 DPI；截图 worker 为一次请求生命周期 | 4、9、10 |
| 截图 OCR | 框选或打开图片，输出可选择、复制、翻译、解释和替换的 OCR 区域 | 与截图翻译共享采集和 OCR 端口，结果由 `ScreenshotOcrWindowView` 呈现 | OCR 模型缺失、识别失败和取消必须可区分；不能静默返回成功 | 复制/替换仅在用户触发时访问剪贴板或文本目标；OCR worker 支持一次性和持久模式 | 4、9、10 |
| 长截图 | 用户选择区域并滚动，输出拼接后的 `ImageFrame` | 连续屏幕采集，由 `OpenCvLongScreenshotStitcher` 计算重叠并拼接 | 无可靠重叠、滚动结束、取消和原生错误均显式结束 | 保持 BGRA32、stride、DPI 和物理坐标；依赖截图 worker，拼接状态不得泄漏到下一会话 | 9 |
| 图片翻译 | 输入文件或截图 `ImageFrame`，输出原图、OCR 区域、背景清除结果和译文渲染 | Windows 使用 OCR、`WindowsImageBackgroundCleaner`/OpenCV worker，Presentation 负责 Avalonia 渲染 | 模型缺失、清除失败、取消和翻译失败显式返回；不得用空图伪装成功 | 不自动覆盖系统剪贴板；清除 worker 隔离 native 崩溃并按请求释放 | 10、11 |
| 划词翻译 | 监听选择动作，读取前台应用选中文本，输出悬浮工具栏及翻译/润色/纠错结果 | 全局鼠标监听记录目标窗口，`WindowsSelectedTextCapture` 配合原生选择与剪贴板读取 | 目标退出、无选区、黑白名单拒绝、权限不足和取消均不弹出错误结果 | 捕获前保存剪贴板，合成复制后读取，并在未被用户改写时恢复；工具栏不抢走原目标语义 | 4、6、7、8 |
| 快捷翻译 | 快捷键触发，读取选中文本或接受输入，输出快速翻译窗口 | `ShortcutCoordinator` 分发 `QuickTranslate`，共享选择捕获和翻译用例 | 快捷键冲突、无选中文本、提供商错误和取消显式处理 | 读取选区时遵循剪贴板快照/恢复和前台目标规则 | 6、7、14 |
| 输入翻译并写回 | 当前输入框文本、前后按键及替换选项，输出翻译文本并写回原输入框 | Application 编排焦点、延迟和投递；Windows 使用目标 token、Unicode 输入/剪贴板粘贴及焦点重试 | 目标无效、焦点恢复失败、剪贴板改变或投递失败返回 `Result`，不向错误窗口输入 | 投递前保存剪贴板和焦点；仅在 change token 未变化时恢复剪贴板；支持替换或插入 | 4、6、7 |
| 润色、总结、纠错 | 选中文本或输入文本，输出流式改写、摘要或结构化纠错项 | `TextAssistUseCases` 和 Presentation 功能流平台无关，Windows 只提供选择捕获/写回 | 提供商、协议、取消和无文本失败保持现有语义 | 若从外部应用取词或写回，沿用选择/投递的剪贴板与焦点保护 | 6、7、14 |
| 系统音频实时字幕 | 系统输出源产生 PCM，输出原文及双语悬浮字幕 | `WindowsPcmAudioCapture` 采集系统音频并转换为 16 kHz/单声道/16-bit PCM，MicroASR 识别，共享字幕会话负责翻译 | 无设备、模型缺失、采集失败、静音、取消均必须终止或报告，不能假运行 | 音频会话独占自身 native 资源；字幕窗口不改变被监听应用焦点 | 4、12、14 |
| 应用音频实时字幕 | 当前会话中的应用音频 token，输出该应用音频的双语字幕 | `WindowsAudioCaptureSourceCatalog` 枚举进程源，token 只在平台适配器解释 | token 过期、进程退出、不支持或采集失败显式返回 | token 不持久化；切换/停止时释放进程音频捕获会话 | 4、12、14 |
| 麦克风同声传译 | 麦克风 PCM，输出识别文本、译文及合成语音 | Windows 麦克风捕获进入共享识别/翻译/TTS 流程，可路由到虚拟音频设备 | 麦克风/设备/模型/TTS/输出失败及取消保持可观察状态 | 反馈音不得进入虚拟线路；采集和播放会话停止后释放设备 | 4、12、13、14 |
| TTS 预览和输出 | 文本、语音和输出目标，输出可停止的音频队列 | 共享 TTS provider 合成，Windows `WindowsSoundFlowAudioPlaybackQueue` 播放或路由虚拟设备 | provider、格式、设备、播放和取消失败显式返回或停止 | 默认输出和虚拟线路是不同目标；Stop 必须清空/终止当前播放 | 13、14 |
| 托盘与开机启动 | 用户关闭行为和自启动设置，输出托盘驻留、激活窗口或登录启动 | 共享 `DesktopApplication` 决定启动到托盘；Windows Host 提供窗口桥，计划任务实现自启动 | 创建/删除自启动项失败返回 `Result`；托盘激活不得创建第二套业务状态 | 主窗口关闭行为由设置决定；自启动参数为 `--autostart` | 5、14 |
| 更新、单实例与重启 | 启动参数、更新操作和重启请求，输出激活既有实例、更新或重启后的唯一实例 | 共享 Host 先执行单实例仲裁，再初始化 Velopack 和 Shell；重启携带 `--restart` | 第二实例只通知既有实例；初始化/更新失败不能留下重复后台实例 | 日志当前写在应用基目录 `Logs`；进程重启保留原参数并追加 `--restart` | 2、5、15 |

#### Windows 行为的共同约束

- Windows 平台能力当前统一报告为 `Available`，权限请求统一返回 `Granted`；macOS 不得照搬此语义，必须反映 TCC 的实时状态。
- `ExternalTargetToken`、`AudioCaptureSourceToken` 和播放设备 token 都是平台拥有、会话范围的 opaque identity，不得持久化或跨平台解析。
- `ImageFrame` 的跨层格式固定为 BGRA32、显式 stride、物理像素尺寸和相对 96 DPI；任何 AppKit/CoreGraphics/Avalonia 图像类型都不得越过 Contracts。
- 选择捕获和文本写回必须以“保存 → 临时操作 → 检查 change token → 条件恢复”为剪贴板基线，不能覆盖用户并发写入的新内容。
- Host/worker 必须具有确定的取消、断连、退出和资源释放路径；native 崩溃不能被翻译成空成功结果。

#### macOS 26 ARM64 首发范围声明

- 唯一发布目标：macOS 26 最新正式小版本，`DeploymentTarget=26.0`，Apple Silicon `osx-arm64`。
- 发行方式：Developer ID 签名并公证的 `.app`，提供 DMG 和 ZIP。
- 不进入 Mac App Store，不启用 App Sandbox。
- 所有 README 已声明的现有功能都在首发范围；只有经过实现和真机验收后才能标记为支持。

#### 明确的非目标

- macOS 25 及更早版本、Intel/x64 和 macOS 27 Beta。
- Mac App Store、App Sandbox、Universal Binary 和企业 MDM 部署。
- 新业务功能、界面重设计、翻译策略调整或新增 provider。
- 为追求 API 形式一致而复制 Windows 原生实现细节；验收对象是契约和用户可观察行为一致。
- 在 Application、Domain 或平台无关 Presentation 中增加操作系统分支。
- 以降级、静默成功或文档删减替代已有功能适配。

#### 适配前测试基线（2026-09-04）

基线提交：`b4e2fdc`（`v1.0.31`）。实测主机：macOS 26.5.1 ARM64、.NET SDK 10.0.400。完整 Xcode 尚未安装，当前 `xcode-select` 指向 Command Line Tools。

| 测试项目 | 通过 | 失败 | 跳过 | 结论 |
| --- | ---: | ---: | ---: | --- |
| Domain | 1 | 0 | 0 | 通过 |
| Application | 210 | 0 | 0 | 通过 |
| Infrastructure | 92 | 0 | 0 | 通过 |
| Infrastructure.Windows | 49 | 14 | 2 | 失败项依赖 Win32/OpenCV Windows native runtime，另有 Unix domain socket 路径过长；此结果不能代替 Windows 真机基线 |
| Presentation | 217 | 0 | 0 | 通过 |
| Architecture | 10 | 1 | 0 | `ProjectReference` 使用 Windows 反斜杠时，测试在 macOS 上取项目名失败；阶段 1 必须修复 |
| Acceptance | 11 | 1 | 4 | Composition 测试在 macOS 注册 Windows Infrastructure，被平台守卫拒绝；Live 测试按设计跳过 |
| 合计 | 590 | 16 | 6 | 解决方案在 macOS 上可还原和编译，但全量测试当前不通过 |

附加基线事实：

- `global.json` 当前声明无效 SDK 版本 `10.0.0`；本次从仓库外工作目录调用 SDK 10.0.400 才完成还原和测试。后续在修改构建入口的相应阶段修复。
- 当前 GitHub `build.yml` 只运行 `windows-latest`，且测试步骤设置了 `continue-on-error: true`，因此现有 CI 不能充当强制通过的 Windows 回归门禁。
- Windows 真机的权威数字必须在阶段 16 的 Windows runner 上重新建立；在此之前，上表只冻结“适配前仓库状态”，不宣称 Windows 原生能力已在本机验证。

#### 交付物

- 功能一致性矩阵。
- Windows 基线测试结果。
- macOS 26 ARM64 范围声明。
- 明确的非目标清单。

#### 阶段门槛

未经确认的功能不得被标成“macOS 暂不支持”。现有功能必须有对应适配步骤。

**门槛结果：通过。** 上述 14 项能力均已进入后续阶段，没有任何现有功能被移出 macOS 首发范围；测试环境缺口已作为显式风险记录，而非功能豁免。

### 阶段 1：同步架构文档和架构测试

预计：1～2 人日。

**状态：已完成（2026-09-05）**

#### 任务

- [x] 更新架构文档的 Physical project layout，添加两个 macOS 生产项目和 macOS 测试项目。当前文档在平台扩展章节已经要求添加这两个项目，但顶部物理项目清单尚未列出，需要消除内部不一致。
- [x] 更新架构测试的项目白名单。
- [x] 扩展生产项目依赖图：

```text
EasyChat.Infrastructure.MacOS
  -> EasyChat.Contracts
  -> EasyChat.Shared

EasyChat.Desktop.MacOS
  -> EasyChat.Desktop
  -> EasyChat.Infrastructure.MacOS
  -> EasyChat.Presentation
```

- [x] 新增架构约束：
   - Mac Infrastructure 不能引用 Avalonia/Presentation。
   - Windows 和 Mac Infrastructure 不能相互引用。
   - Mac 原生包不得出现在共享项目。
   - Application 和 Presentation 中禁止 `OperatingSystem.IsMacOS()`。
   - Contracts 禁止出现 `AXUIElement`、`NSWindow`、`SCDisplay`、`AudioDeviceID`。
   - Mac Host 必须调用 `DesktopApplication.Run`。
   - Mac Program 不得重复注册 Application、Infrastructure 和 Presentation。

#### 涉及位置

- `DDD_ARCHITECTURE.md`
- `tests/EasyChat.ArchitectureTests/ArchitectureRulesTests.cs`
- `EasyChat.sln`

#### 阶段门槛

新增空项目后，架构测试通过；空项目本身不单独合并，必须与下一阶段的有效适配器一起提交。

**门槛结果：通过。** 架构测试 11/11 通过；`EasyChat.Desktop.MacOS` 以 `osx-arm64` 成功构建且零警告；Mac Infrastructure smoke test 1/1 通过；`Info.plist` 和 entitlements 均通过 `plutil -lint`。当前未创建 Git commit，两个骨架项目必须与后续有效适配器一起提交，不能形成单独的空项目提交。

### 阶段 2：清除共享层中的 Windows 语义泄漏

预计：2～3 人日。

**状态：进行中（2026-09-05）**

这一阶段不是 macOS 功能实现，而是让共享层真正满足架构文档。

#### 2.1 单实例与二次激活

当前共享 Desktop 使用 Windows 风格的命名 Mutex 和 `EventWaitHandle`。

实施方案：

- [x] 在 `EasyChat.Desktop` 定义窄生命周期边界 `IDesktopInstanceLease` 和 `IDesktopInstanceCoordinator`。
- [x] Windows Host 包装当前 Mutex/EventWaitHandle 实现。
- [x] macOS Host 使用用户级文件锁保证单实例，并使用短路径 Unix domain socket 传递激活信号。
- [x] 已运行实例收到信号后调用现有主窗口激活回调。
- [x] `DesktopApplication.Run` 只消费实例协调器，不知道实现平台。

接口成立的理由：已有两个真实平台实现，符合“只有真实边界才定义接口”的规则。

#### 2.2 重启行为

- [x] `IApplicationRestartService` 保持现有契约。
- [x] Windows 实现归 Windows Host。
- [x] macOS 实现归 Mac Host。
- [x] Mac 通过 `/usr/bin/open -n <bundle> --args ... --restart` 重新打开 `.app`，不直接执行 bundle 内二进制。
- [x] `DesktopApplication` 不再自行决定平台重启方式。

#### 2.3 日志目录

日志已从应用程序目录调整到用户可写目录：

```text
~/Library/Logs/EasyChat/
```

或统一落到：

```text
~/Library/Application Support/EasyChat/Logs/
```

- [x] 日志配置仍由 Desktop 拥有，不移入 Presentation 或平台 Infrastructure。
- [x] 统一使用 `Environment.SpecialFolder.LocalApplicationData/EasyChat/Logs`；在 macOS 映射到 `~/Library/Application Support/EasyChat/Logs/`。

#### 2.4 语义快捷键

当前 Application 硬编码了 `Ctrl + A`。计划在现有 `ITextDelivery` 中增加窄的语义命令，而不是创建平台快捷键工具类：

```csharp
enum StandardTextCommand
{
    SelectAll,
    Delete,
    Copy,
    Paste
}
```

- [x] Application 发送 `StandardTextCommand.SelectAll` 和 `StandardTextCommand.Delete`。
- [x] Windows 适配为 `Ctrl+A` 和 Windows 对应命令。
- [ ] macOS 适配为 `Command+A` 和 macOS 对应命令（阶段 6 的文本投递适配器）。
- [x] 用户自定义前置键/后置键仍使用 `ShortcutGesture` 语义。
- [x] 旧设置中的 `Win` 和 `Windows` 作为 `Meta` 的持久化兼容别名读取，并有测试覆盖。
- [x] 新录制设置统一保存 `Meta`。
- [ ] Presentation 在 macOS 显示 `⌘`、Windows 显示 `Win`（阶段 14 的平台交互呈现，不在共享 UI 中加入 OS 分支）。

#### 测试

- Windows 行为保持不变。
- Application 测试不再断言 `Ctrl + A` 字符串，而断言语义命令。
- 设置兼容测试覆盖旧 `Win` 和 `Ctrl` 数据。

#### 阶段门槛

Application 中不再包含操作系统专用快捷键名称；Windows 功能测试全部通过。

**门槛状态：部分通过。** Application 已不再包含 `Ctrl + A` 等平台专用命令，Windows 与 macOS Host 均成功编译；本次改动直接覆盖的 Application 测试 7/7 通过，架构测试 11/11 通过。完整 Application 套件曾通过 213/213，但复验时既有字幕时序用例 `FailedStructuredRetryRestoresAnExactSourceTranslationSnapshot` 连续两次超时，因此不能宣称全量绿。Windows 原生功能测试仍需 Windows runner，macOS 命令映射和平台显示分别按阶段 6、14 完成，所以本阶段暂不标记完成。

### 阶段 3：创建 macOS 平台项目与原生桥接边界

预计：2～4 人日。

#### 3.1 新建项目

```text
src/Infrastructure/EasyChat.Infrastructure.MacOS/
  DependencyInjection/
  ApplicationStartup/
  Audio/
  Capture/
  Hotkeys/
  ImageTranslation/
  Input/
  Ocr/
  Native/
  Workers/

src/Host/EasyChat.Desktop.MacOS/
  Capture/
  DependencyInjection/
  Program.cs
  Info.plist
  EasyChat.entitlements
  EasyChat.Desktop.MacOS.csproj
```

#### 3.2 原生调用策略

在 Mac Infrastructure 内放置一个极薄的原生桥：

```text
EasyChat.Infrastructure.MacOS/Native/EasyChatMacNative/
```

边界原则：

- 原生桥只做 Apple Framework 调用和 C ABI 转换。
- 不包含 EasyChat 业务规则。
- 不引用 Avalonia。
- C# 通过 `LibraryImport` 调用稳定 C ABI。
- C# adapter 负责 Contracts 映射、Result/Error、CancellationToken、生命周期、缓冲和异步流。
- 原生桥产生的 handle 不得离开 Mac Infrastructure。

建议按子能力导出接口：

- Accessibility。
- CGEventTap。
- ScreenCaptureKit。
- CoreAudio/AVFoundation。
- AppKit window helper。

#### 3.3 资源释放规则

- 使用 `SafeHandle` 包装 native handle。
- delegate 必须保持 GC 引用。
- native callback 停止后才能释放上下文。
- worker 退出时停止 stream/event tap。
- 所有异步 callback 支持取消。
- 原生错误转换为结构化错误码和消息。

#### 阶段门槛

- Mac 项目可编译。
- 空 Composition Root 可通过 `--verify-composition`。
- Native bridge 有独立 smoke test。
- 没有原生类型越过 platform assembly。

### 阶段 4：平台能力和权限系统

预计：2～3 人日。

#### 实现类

- `MacPlatformCapabilities`
- `MacPlatformPermissionRequester`

#### 权限映射

| EasyChat 权限 | macOS 实现 |
|---|---|
| `Accessibility` | `AXIsProcessTrustedWithOptions` |
| `ScreenRecording` | ScreenCaptureKit/屏幕捕获预检与请求 |
| `InputMonitoring` | 事件监听预检和请求 |
| `Microphone` | AVFoundation 麦克风授权 |
| `SystemAudioCapture` | 映射到 ScreenCaptureKit 捕获授权 |

#### 行为要求

1. `GetStatusAsync` 只检查，不弹框。
2. `RequestAsync` 才允许触发系统提示。
3. 用户拒绝后返回 `Denied`。
4. 系统没有可用权限入口时返回 `Unsupported`。
5. 授权请求后重新检查 capability。
6. 如果系统要求重启，返回明确原因，不将“已点击允许”当作 `Granted`。
7. 不读取 TCC 数据库，只使用公开 API。
8. 权限失败不能导致主进程崩溃。
9. 不增加新的权限设置页面，沿用现有 capability/use-case/toast 流程。

#### Info.plist

至少声明：

- 屏幕捕获用途。
- 麦克风用途。
- Bundle identifier。
- 应用名称和版本。
- 最低系统版本 26.0。
- 高分辨率支持。
- 后台/菜单栏行为所需键值。

#### 测试

覆盖未决定、已授权、已拒绝、授权后需要重启、运行中撤销权限、原生检查异常和 CancellationToken。

#### 阶段门槛

所有 capability 都能正确返回三态，不存在无条件 `Available`。

### 阶段 5：macOS Host、应用生命周期和窗口桥

预计：3～5 人日。

#### 5.1 Mac Program

`Program.cs` 仅负责：

- 识别 worker 参数。
- 注册 Mac Infrastructure。
- 注册 Mac Desktop adapters。
- 初始化部署/更新。
- 调用 `DesktopApplication.Run`。
- 应用统一 Skia 配置。

不得在 Program 中重新实现设置、翻译、UI 或 shell 生命周期。

#### 5.2 AppKit 窗口桥

实现：

- `AvaloniaMacWindowBehavior`
- 对应纯原生 `MacOwnedWindowBehavior`

职责：

- 非激活显示。
- 保持前台应用文本输入上下文。
- 提升悬浮窗但不抢焦点。
- 鼠标穿透。
- 屏幕捕获排除。
- Topmost/NSWindow level 映射。
- Mission Control/Space 行为。
- 全屏应用上方的字幕和工具栏行为。

Avalonia 只能存在 Host 桥中；原生窗口操作继续放在 Mac Infrastructure。

#### 5.3 主窗口行为

验证和适配：

- 无边框主窗口拖动。
- 关闭、最小化和退出。
- Dock 点击恢复窗口。
- 菜单栏图标点击。
- 全屏切换。
- 交通灯区域冲突。
- `MinWidth=1180` 在当前系统缩放模式下的可用性。
- 应用隐藏后快捷键和字幕仍然工作。

#### 5.4 开机启动

实现 `MacApplicationAutoStartService`：

- 使用 `SMAppService.mainApp`。
- 读取真实注册状态。
- 用户在系统设置中禁用后正确反映。
- 不手工写 `LaunchAgents`。
- 不增加后台守护进程。

#### 阶段门槛

主程序能以正确 `.app` 身份启动、关闭、隐藏、恢复、二次激活和开机启动。

### 阶段 6：剪贴板、应用枚举、焦点和文本写回

预计：5～7 人日。

#### 6.1 NSPasteboard

实现：

- `MacClipboardSnapshots`
- `MacClipboardText`
- `MacClipboardImage`

要求：

- 使用 `changeCount` 作为 change token。
- 备份剪贴板全部可读取 UTI 类型。
- 恢复时保留类型和二进制内容。
- `RestoreIfUnchangedAsync` 只在 change token 匹配时恢复。
- 图片写入必须匹配 `ImageFrame` BGRA 数据。
- 延迟提供者或无法序列化类型要返回可诊断错误，不能静默丢失。
- 剪贴板读写串行化，防止并发覆盖。

#### 6.2 目标 Token

`ExternalTargetToken` 内部编码建议包含进程 ID、AX element/window identity、生成代次或会话校验值。

要求：

- Token 不持久化。
- 过期目标明确失败。
- 非 Mac token 明确失败。
- 不把编码格式暴露给 Application 或 Presentation。

#### 6.3 应用枚举

实现 `MacRunningProcessCatalog`：

- 枚举可交互、拥有可见窗口的应用。
- 稳定身份使用 bundle identifier。
- 名称使用 localized application name。
- 描述来自 bundle metadata。
- 图标转成 PNG。
- 过滤 EasyChat 自身和无 UI 后台进程。
- 黑白名单仍只比较 Contracts 中的字符串身份。

#### 6.4 窗口和焦点

实现：

- `MacWindowFocus`
- `MacWindowInputTransparency`

能力：

- 当前前台应用。
- 当前 AX 聚焦元素。
- 激活目标应用。
- 恢复原目标。
- 校验目标是否已退出。
- 不激活浮窗。
- 点击穿透。

#### 6.5 文本选择和写回

实现：

- `MacTextSelection`
- `MacTextDelivery`
- `MacSelectedTextCapture`

选择读取顺序：

1. Accessibility 直接读取 selected text。
2. AX selected text range。
3. `Command+C` + NSPasteboard。
4. 按 change token 安全恢复剪贴板。

写回模式：

- `Type`：CGEvent Unicode 输入。
- `Paste`：临时剪贴板 + `Command+V` + 恢复。
- `Message`：优先通过 AX 设置选区/值；目标不支持时返回明确失败，不假装成功。
- 标准命令通过 `StandardTextCommand` 映射。

#### 重点兼容应用

- Safari。
- Chrome。
- TextEdit。
- Notes。
- Microsoft Word。
- VS Code。
- Electron 输入框。
- JetBrains IDE。
- Terminal。
- 密码输入框和受保护文本框。

密码框必须拒绝抓取，不能绕过系统保护。

#### 阶段门槛

划词以外的选中文字、全选、复制、翻译、写回和剪贴板恢复通过应用矩阵。

### 阶段 7：全局快捷键、键盘状态和鼠标监听

预计：4～6 人日。

#### 实现

- `MacGlobalHotkeys`
- `MacGlobalPointerMonitor`
- `MacPointerPosition`
- `MacKeyboardState`

#### 快捷键

要求支持：

- 普通注册。
- 冲突探测。
- 注销。
- Command/Option/Control/Shift。
- 功能键。
- OEM/符号键。
- 多个快捷键并存。
- 同声传译的按下和释放回调。
- 应用隐藏时触发。
- 用户注销/睡眠/唤醒后恢复。
- 输入法切换后仍按物理按键匹配。

#### 鼠标监听

需要产生现有契约事件：

- `PrimaryPressed`。
- `PrimaryReleased`。
- `PrimaryDoubleClick`。
- `WindowMoveStarted`。

事件必须携带统一桌面物理像素坐标、前台目标、鼠标下目标、EasyChat Overlay 判断、时间戳和剪贴板序列。

#### 线程要求

- CGEventTap 使用独立 RunLoop。
- 回调中不运行翻译或 UI 工作。
- 回调快速转发到 C# 队列。
- event tap 被系统禁用时尝试恢复，并记录失败。
- Dispose 必须退出 RunLoop，不遗留后台线程。

#### 阶段门槛

所有现有快捷键动作可触发，按住式同声传译释放事件可靠，鼠标监听不造成系统输入延迟。

### 阶段 8：划词工具栏完整链路

预计：3～5 人日。

这一阶段不新增 macOS 专用划词逻辑，只连接现有 Application 协调器。

#### 任务

1. 接入 `SelectionInteractionCoordinator`。
2. 验证拖动选择和双击选择。
3. 等待修饰键释放。
4. 保存前台目标。
5. 检测目标在异步处理期间是否变化。
6. 获取选中文字。
7. 在选区附近显示工具栏。
8. 工具栏不得抢走目标应用输入焦点。
9. 工具栏关闭后恢复目标输入上下文。
10. 屏幕边缘自动调整位置。
11. 多显示器、Retina、负坐标验证。
12. 应用黑名单/白名单使用 bundle identifier。
13. EasyChat 自己的窗口不能触发外部划词工具栏。

#### 阶段门槛

Safari、Chrome、TextEdit、Word、VS Code 至少五类应用通过单击、拖选、双击和跨屏测试。

### 阶段 9：截图和显示器坐标系统

预计：4～6 人日。

#### 9.1 ScreenCatalog

实现 `MacScreenCatalog`：

- 获取所有显示器。
- 使用稳定 display identifier。
- 返回物理像素 Bounds。
- 正确处理主屏之外的负坐标。
- 使用 backing scale 计算有效 DPI：`Dpi = 96 × backingScaleFactor`。
- Quartz 左下角坐标必须转换成 Contracts 左上角统一坐标。
- 不允许 Presentation 处理 macOS 坐标特例。

#### 9.2 ScreenCapture

实现 `MacScreenCapture`：

- `PrimaryScreen`。
- 指定 `Screen`。
- 指定 `Region`。
- 使用 ScreenCaptureKit/SCScreenshotManager。
- 输出 BGRA32。
- 修正行方向、stride 和 alpha。
- 保持实际物理像素尺寸。
- 排除 EasyChat overlay。
- 屏幕锁定、权限撤销、显示器断开时返回 Result 失败。
- 不使用废弃 CGWindowList 截图作为主路径。

#### 9.3 截图 Session 与 worker

在 Mac Host 实现：

- `MacScreenshotCaptureSession`
- `MacScreenshotWorker`

保留当前 worker 隔离目的：截图大缓冲、Avalonia overlay 资源、OCR 前图像，并在 worker 完成后回收内存。

IPC 推荐 Unix domain socket，或已验证可在 macOS 使用的匿名/命名管道。worker 直接运行同一 bundle executable 的 worker mode，不创建第二个 Dock 应用实例。

#### 9.4 长截图

- 使用共享 `ManagedLongScreenshotStitcher`。
- 使用 CGEvent 或 AX 执行滚动。
- 固定区域每帧重新截取。
- 验证惯性滚动和滚动结束判定。
- 保持原有水平/垂直拼接行为。

#### 阶段门槛

单屏、Retina、多屏、显示器位于主屏左侧/上方、不同 scale 的截图区域均像素对齐。

### 阶段 10：macOS OCR

预计：4～6 人日。

#### 实现

- `MacOpenVinoOcr`
- `MacOpenVinoOcrBackend`
- `MacOcrWorker`
- `MacOcrModelCatalog`

#### 原则

- `IOcrRecognizer` 和 `IOcrModelStore` 不修改业务语义。
- 模型 ID、语言 ID、下载校验和保持兼容。
- 不改 OCR 默认语言策略。
- 不用 Apple Vision 替换现有 OCR，因为这会改变语言范围、模型管理和结果语义。
- OpenVINO/PaddleOCR native package 只进入 Mac Infrastructure。
- 不引用 Windows OCR 类型。
- 不使用 `System.Drawing.Common`。

#### Runtime

ARM64 方案：

- `Sdcb.OpenVINO.runtime.osx.12.6-arm64`。
- 对应 OpenVINO/PaddleOCR managed binding。
- 确认 dylib 安装名和相对路径。
- 所有 dylib 放入 `.app/Contents/Frameworks`。
- 修改 rpath 后再签名。
- worker 和主进程都能解析同一 runtime。

#### 测试

- 模型未下载。
- 下载、SHA256 校验、安装。
- 中断下载。
- 删除模型。
- 中英日韩。
- 自动语言。
- 旋转文字。
- 超大截图。
- worker 空闲释放。
- worker 崩溃恢复。
- 连续 OCR 内存稳定性。
- Unicode 路径。

#### 阶段门槛

当前 OCR 语言目录和功能入口在 macOS 上保持一致，连续识别无持续内存增长。

### 阶段 11：图片文字清除和图片翻译

预计：3～5 人日。

#### 实现

- `MacImageTranslationModelStore`
- `MacImageBackgroundCleaner`
- `MacImageBackgroundCleanerWorker`

#### Fast 模式

- 移植当前 OpenCV mask、边缘扩展和 inpaint 算法。
- 使用 macOS ARM64 OpenCV runtime。
- 输出仍为 BGRA32。
- 保持只修改 OCR polygon 内像素的现有约束。

#### Precise 模式

- 复用现有 AOT-GAN ONNX 模型。
- 使用 ONNX Runtime macOS ARM64。
- 模型 ID、下载地址、哈希和目录保持兼容。
- 继续通过 worker 隔离大内存和 native crash。

#### 架构要求

- 模型下载编排仍由 Application 管理。
- macOS Infrastructure 只实现 model store 和 native compute。
- 渲染、文本拟合、旋转、颜色分析继续保留在 Presentation。
- 不把 OpenCV Mat、OrtValue 暴露出平台层。

#### 阶段门槛

Fast/Precise 两种模式的输出、取消、模型缺失和 worker 恢复行为与 Windows 一致。

### 阶段 12：音频源、PCM 采集和语音识别

预计：7～10 人日，是最高风险阶段。

#### 12.1 音频源目录

实现 `MacAudioCaptureSourceCatalog`：

- System Output。
- 正在运行的可捕获应用。
- 麦克风设备。
- 默认麦克风。
- 虚拟音频设备。
- 应用图标。
- 会话级 token。

建议 token 内部区分：

```text
macos:system-output
macos:application:<native-id>
macos:microphone:<device-id>
```

编码仅在 Mac Infrastructure 内解释。

#### 12.2 系统和应用音频

使用 ScreenCaptureKit：

- 系统输出：显示器 filter + audio。
- 应用输出：只 include 指定 `SCRunningApplication`。
- 排除 EasyChat 自己产生的声音，避免回声。
- 无视频需求时仍配置最小化视频开销。
- 输出转换为标准 PCM。

#### 12.3 麦克风

使用 AVFoundation/CoreAudio：

- 枚举 capture device。
- 捕获当前设备格式。
- 转成 16 kHz、mono、PCM16。
- 处理 AirPods 等运行时采样率变化。
- 设备断开时返回明确失败。

#### 12.4 多源混音

实现 `MacPcmAudioCapture`：

- 保持 `IPcmAudioCapture` 接口。
- 每个源独立缓冲。
- 20ms 帧。
- 16 kHz mono PCM16。
- 多源求和和限幅。
- 有界 Channel。
- 丢弃最旧帧，而不是无限增长。
- 支持 `IPreparablePcmAudioCapture`。
- Stop/Dispose 不死锁。
- 捕获失败及时结束异步枚举。

#### 12.5 MicroASR

继续复用 `MicroASR`、`MicroAsrSpeechRecognitionEngine`、模型安装器、字幕分段和实时翻译，禁止复制到 Mac Infrastructure。

#### 阶段门槛

系统、单应用、麦克风和多源混合都能稳定产生 ASR 输入；30 分钟连续运行无明显延迟累积或内存增长。

### 阶段 13：播放设备、TTS 和同声传译

预计：3～5 人日。

#### 实现

- `MacAudioPlaybackDeviceCatalog`
- `MacAudioPlaybackQueue`
- `MacAudioFeedbackCuePlayer`

#### 播放要求

- 默认播放设备。
- 虚拟音频设备。
- 顺序播放队列。
- Stop 立即停止当前音频并清空队列。
- 支持现有 TTS 返回的媒体格式。
- 设备切换后重新初始化。
- 设备失效时给出可诊断错误。
- UI 提示音不能路由到虚拟设备。

#### 虚拟音频设备识别

当前 Windows 只识别 VB-Audio 名称。macOS 需要识别已有等价设备，例如 BlackHole、Loopback 或其他具有输入/输出配对能力的虚拟设备。

这不是增加新功能，而是实现现有 `IsVirtualCable` 契约。不得在 Presentation 中硬编码具体驱动名称；设备判定属于 Mac Infrastructure。

#### 同声传译链路

```text
全局按键按下
  → 麦克风预热/采集
  → MicroASR
  → 翻译
  → Edge TTS
  → 虚拟播放设备
  → 按键释放停止
```

#### 阶段门槛

同声传译、TTS 预览、默认设备播放和虚拟设备播放全部通过。

### 阶段 14：Presentation 的 macOS 交互适配

预计：2～4 人日。

这一阶段只允许修改表现，不允许搬入平台业务逻辑。

#### 任务

1. 快捷键显示：`Meta` → `⌘`、`Alt` → `⌥`、`Control` → `⌃`、`Shift` → `⇧`。
2. 快捷键录制不再保存 `Win +`，兼容读取旧设置，并使用稳定物理键名。
3. 验证 PingFang SC、Hiragino Sans、Inter 和 CJK 字体回退。
4. 适配主窗口标题栏拖动、全屏、关闭/隐藏、菜单栏/Dock 和小屏幕缩放。
5. 验证悬浮窗不抢焦点、跨 Space、全屏播放器、多显示器、点击穿透和屏幕捕获排除。
6. 验证 OCR/ASR 模型导入、TTS 输出、应用数据目录迁移和 macOS 文件选择器行为。
7. 沿用现有 Toast 和错误资源，补充必要的中英文 macOS 权限说明，不新增独立功能页面。

#### 阶段门槛

Presentation 不包含 Apple Framework 类型或 `OperatingSystem.IsMacOS()`；交互符合 macOS 习惯。

### 阶段 15：打包、签名、公证与更新

预计：4～6 人日。

#### 15.1 `.app` 结构

```text
EasyChat.app/
  Contents/
    Info.plist
    MacOS/
      EasyChat
    Frameworks/
      *.dylib
    Resources/
      EasyChat.icns
      Assets/
      Models/
```

#### 15.2 发布配置

- `RuntimeIdentifier=osx-arm64`。
- self-contained。
- 是否启用 trimming 要通过 worker、反射、序列化测试后决定。
- 不默认启用 Native AOT。
- 修正当前 `global.json` 的无效 SDK feature-band。
- 固定 SDK 和依赖版本。
- 生成符号文件，发布包排除调试符号。

#### 15.3 签名顺序

从内到外：

1. 原生 dylib。
2. worker/native helper。
3. 主 executable。
4. `.app` bundle。
5. DMG/ZIP。

要求：

- Developer ID Application。
- Hardened Runtime。
- secure timestamp。
- entitlements。
- `codesign --verify --deep --strict`。
- `spctl --assess`。
- `notarytool submit`。
- `stapler staple`。

#### 15.4 更新

现有更新服务继续使用 `IApplicationUpdateService`，但需要：

- 独立 macOS release channel。
- 只匹配 `osx-arm64` 包。
- 更新时退出全部 worker。
- 替换整个 `.app`。
- 更新后签名保持有效。
- 重新启动 bundle，而不是内部 executable。
- 权限身份保持同一 bundle identifier 和签名主体，避免 TCC 权限丢失。
- 不让 Windows 客户端看到 macOS 包，反之亦然。

#### 阶段门槛

从 GitHub 下载的干净制品可以直接打开，通过 Gatekeeper、签名、公证和应用内更新验证。

### 阶段 16：CI、测试矩阵与发布门禁

预计：5～8 人日。

#### CI 拆分

```text
shared-tests
windows-tests
macos-arm64-build
macos-arm64-tests
macos-package
macos-sign-notarize
```

#### 必须调整

1. 删除测试步骤的 `continue-on-error: true`。
2. Windows 原生测试只在 Windows 运行。
3. Mac 原生测试只在 macOS ARM64 runner 运行。
4. 共享测试必须同时在 Windows 和 macOS 运行。
5. Composition test 分平台。
6. Architecture test 对两套 Host/Infrastructure 使用相同规则。

#### 自动化测试

纯单元测试包括：

- Token 编解码。
- 快捷键映射。
- 坐标转换。
- 音频混音。
- BGRA 帧转换。
- 权限状态映射。
- 剪贴板 change token。
- worker 协议。
- 原生错误映射。
- Dispose 和取消。

macOS 集成测试包括：

- AppKit/AX 可用性。
- 屏幕枚举。
- 截图尺寸。
- 麦克风枚举。
- 默认播放设备。
- `.app` 资源解析。
- dylib 加载。
- Composition Root。

#### 手工真机测试

权限组合：

- 全部权限未授予。
- 只授予屏幕录制。
- 只授予 Accessibility。
- 全部授予。
- 授权后未重启。
- 运行中撤销权限。

硬件组合：

- 单 Retina 屏。
- Retina + 外接非 Retina。
- 外接屏在主屏左侧。
- 外接屏在主屏上方。
- AirPods。
- USB 麦克风。
- 虚拟音频设备。

应用组合：

- Safari。
- Chrome。
- TextEdit。
- Notes。
- Word。
- VS Code/Electron。
- JetBrains。
- Terminal。
- 全屏视频播放器。

#### 阶段门槛

任何核心功能失败、测试失败、签名失败或权限状态错误都阻止发布。

## 4. 推荐提交拆分

为便于审核，建议按以下顺序提交，避免一个巨型 PR：

1. `arch: approve macOS project boundaries`
2. `refactor: remove Windows lifecycle assumptions from shared desktop`
3. `refactor: replace platform key strings with semantic text commands`
4. `feat-platform: add macOS host and composition`
5. `feat-platform: add macOS permissions and capabilities`
6. `feat-platform: add macOS window lifecycle and autostart`
7. `feat-platform: add macOS clipboard focus and text delivery`
8. `feat-platform: add macOS hotkeys and pointer monitoring`
9. `feat-platform: enable selection workflows on macOS`
10. `feat-platform: add ScreenCaptureKit capture`
11. `feat-platform: add macOS OCR runtime`
12. `feat-platform: add macOS image background cleaning`
13. `feat-platform: add macOS audio capture`
14. `feat-platform: add macOS audio playback`
15. `ui: adapt shortcut and window presentation for macOS`
16. `build: package sign and notarize macOS app`
17. `ci: enforce Windows and macOS release gates`
18. `docs: document macOS installation and permissions`

这里的 `feat-platform` 表示实现已有端口，不代表增加产品功能。

## 5. 工作量与关键路径

按 macOS 26 ARM64 单架构估算：

| 工作项 | 预计人日 |
|---|---:|
| 基线、架构测试和共享边界整理 | 4～7 |
| Host、生命周期、权限、窗口 | 6～9 |
| 剪贴板、输入、快捷键、划词 | 10～15 |
| 截图、长截图和坐标 | 5～7 |
| OCR 和图片文字清除 | 7～10 |
| 音频采集、播放、同声传译 | 10～15 |
| UI 适配 | 2～4 |
| 打包、更新、CI、公证 | 6～9 |
| 合计 | **50～76 人日** |

其中可以交叉进行，实际日历时间约：

- 单人：10～15 周。
- 两名熟悉 .NET/Avalonia/macOS 原生 API 的开发者：6～9 周。
- 另预留 1～2 周真机稳定性测试。

关键路径：

```text
原生桥
  → 权限
  → 输入/划词
  → ScreenCaptureKit
  → 音频
  → 签名公证
```

## 6. 最终完成定义

只有同时满足以下条件，才能认定 macOS 适配完成：

- 两个 macOS 生产项目符合架构依赖图。
- Domain/Application 没有 macOS 分支。
- Mac Infrastructure 没有 Avalonia 引用。
- 所有现有功能在 macOS 26 ARM64 上可用。
- 权限拒绝、撤销和重启场景不会崩溃。
- 划词和输入回写通过目标应用矩阵。
- 截图在 Retina、多屏和负坐标环境像素准确。
- OCR、图片清除和 MicroASR native runtime 可以在签名 `.app` 中加载。
- 系统、应用、麦克风音频均可识别。
- 同声传译能使用虚拟音频设备。
- 托盘、Dock、开机启动、单实例、重启和更新正常。
- Windows 全量测试无回归。
- macOS CI 全绿。
- `.app` 已签名、公证并通过 Gatekeeper。
- README 和安装文档只描述实际已经通过验收的 macOS 能力。
