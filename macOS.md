# macOS 适配详细实施计划（审核版）
- dotnet run --project src/Host/EasyChat.Desktop.MacOS

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

**状态：已完成（2026-09-12）**

两个遗留项已在后续阶段闭合：`Command+A` 映射见阶段 6.5 的 `MacTextDelivery`，`⌘` 显示见阶段 14 的 `KeyGlyphs`。

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
- [x] macOS 适配为 `Command+A` 和 macOS 对应命令（`MacTextDelivery` / `MacKeyCombination.ForCommand`，2026-09-12 完成）。
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

**状态：已完成（2026-09-12）**

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

#### 与原计划的偏差：原生桥的形态

3.2 原设想编译一个独立的 `EasyChatMacNative` C 动态库。实际落地时改为 **C# 直接 `LibraryImport` 系统框架的稳定 C ABI**，理由是本阶段和阶段 4 需要的全部入口（`AXIsProcessTrustedWithOptions`、`CGPreflightScreenCaptureAccess`、`CGRequestScreenCaptureAccess`、`IOHIDCheckAccess`、`IOHIDRequestAccess`）本身就是 C 函数，包一层自有 dylib 只会增加构建、签名和双向 ABI 维护成本，不会带来任何隔离收益。

唯一没有 C 入口的是 `AVCaptureDevice` 的麦克风授权，因此 `Native/` 内提供了一个**最小 Objective-C 运行时层**：`objc_getClass` / `sel_registerName` / 按签名逐个声明的 `objc_msgSend`，外加一个符合 libclosure 布局的 global block 用于承接 `requestAccessForMediaType:completionHandler:` 回调。global block 由 libclosure 保证不会被 copy/free，因此静态分配一次、进程生命周期内不释放，管理侧不需要引用计数。

独立编译的原生桥推迟到**首次出现只有 Objective-C/Swift 接口且无法用单条消息发送表达的能力**时再引入，即阶段 9（ScreenCaptureKit 采集）和阶段 12（ScreenCaptureKit 音频）。届时需要重新评估是否值得引入 Xcode 构建步骤。

实际目录（与 3.1 的差异）：`Audio/`、`Capture/`、`Hotkeys/`、`ImageTranslation/`、`Input/`、`Ocr/`、`Workers/` 仍未创建，因为还没有对应适配器；按“不建立空目录”的约定留到各自阶段。能力与权限适配器放在程序集根目录，与 `EasyChat.Infrastructure.Windows` 中 `WindowsPlatformCapabilities` 的位置一致。

**门槛结果：部分通过。**

| 门槛 | 结果 |
| --- | --- |
| Mac 项目可编译 | 通过。`dotnet build EasyChat.sln -c Debug` 在 macOS 主机上 0 警告 0 错误 |
| Native bridge 有独立 smoke test | 通过。`tests/EasyChat.Infrastructure.MacOS.Tests/Native/MacSystemPrivacyGatewayTests.cs` 在真机上逐个解析五项权限的原生入口，并单独断言 `AVCaptureDevice` 的类与 selector 真实存在（`objc_msgSend` 对 nil 接收者返回 0，与 `NotDetermined` 同值，必须分开断言），以及 global block 的 isa/flags/descriptor 布局 |
| 没有原生类型越过 platform assembly | 通过。`Native/` 的返回值只有 `bool`、`nint` 和内部枚举；`IMacPrivacyGateway` 只输出 `MacPrivacyState`；跨 Contracts 的只有 `CapabilityStatus` / `PermissionStatus` |
| 空 Composition Root 可通过 `--verify-composition` | **未通过，且本阶段无法通过。** 共享 Desktop 的 DI 图要求全部平台端口存在，只注册能力与权限不足以让 `ValidateOnBuild` 成立 |

`--verify-composition` 当前缺失的端口如下，正好构成阶段 5～13 的适配器清单，可作为后续阶段的验收对照：

```text
EasyChat.Contracts.Platform.IScreenCatalog                     阶段 9
EasyChat.Contracts.Platform.IGlobalHotkeys                     阶段 7
EasyChat.Contracts.Platform.IGlobalPointerMonitor              阶段 7
EasyChat.Contracts.Platform.IPointerPosition                   阶段 7
EasyChat.Contracts.Platform.ISelectedTextCapture               阶段 6
EasyChat.Contracts.Platform.IWindowFocus                       阶段 6
EasyChat.Contracts.Platform.IPcmAudioCapture                   阶段 12
EasyChat.Contracts.Platform.IAudioPlaybackQueue                阶段 13
EasyChat.Contracts.Platform.IAudioFeedbackCuePlayer            阶段 13
EasyChat.Contracts.Ocr.IOcrRecognizer                          阶段 10
EasyChat.Contracts.Ocr.IOcrModelStore                          阶段 10
EasyChat.Contracts.ImageTranslation.IImageBackgroundCleaner    阶段 11
EasyChat.Contracts.ImageTranslation.IImageTranslationModelStore 阶段 11
EasyChat.Presentation.Features.Capture.IScreenshotCaptureSession 阶段 9
EasyChat.Presentation.Foundation.Platform.IPlatformWindowBehavior 阶段 5
```

因此该门槛顺延为**阶段 13 的出口条件**：在最后一个平台端口落地前，`--verify-composition` 不可能成立，把它挂在阶段 3 是原计划的排序错误。

### 阶段 4：平台能力和权限系统

预计：2～3 人日。

**状态：已完成（2026-09-12）**

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

#### 实际落地的结构

原计划只列了两个实现类。实际在两者之间加了一个**内部接缝** `IMacPrivacyGateway`，理由是：如果把「检查 → 弹窗 → 重新检查 → 判定」的策略写进直接调 TCC 的类里，上面测试清单中的七种情形在非授权主机上一种都测不了。

- `Native/*`：只做 C ABI 与 Objective-C 消息发送，返回 `bool` / `nint` / 内部枚举。
- `IMacPrivacyGateway`：唯一的原生接缝，输出 `MacPrivacyState`（`Granted` / `NotDetermined` / `Denied` / `Restricted` / `Unavailable`）。`Check` 绝不弹窗，`PromptAsync` 是唯一允许弹窗的入口。
- `MacPlatformCapabilities` / `MacPlatformPermissionRequester`：把原始状态映射成 Contracts 三态，并持有「弹窗后必须重新读」这条策略。
- `MacPrivacyReasons`：把状态翻成用户可执行的说明，包含对应的「系统设置 > 隐私与安全性 > …」面板名与重启提示。

只有一层接口，不构成 façade 链；它是真实边界（真机 TCC 对可脚本化的假实现），符合「只有真实边界才定义接口」的规则。

#### 能力到权限的映射

| 能力 | 所需权限 | 说明 |
| --- | --- | --- |
| `ScreenCapture` | `ScreenRecording` | |
| `SelectedTextCapture` | `Accessibility` | |
| `TextDelivery` | `Accessibility` | |
| `WindowActivation` | `Accessibility` | |
| `GlobalPointerMonitoring` | `InputMonitoring` | |
| `AudioCaptureSources` | `SystemAudioCapture` | 实为 ScreenCaptureKit 的屏幕录制授权 |
| `GlobalHotkeys` | 无 | Carbon `RegisterEventHotKey` 不受 TCC 管辖 |
| `Clipboard` | 无 | `NSPasteboard` 不受 TCC 管辖 |
| `SpeechRecognition` | 无 | 本地 MicroASR 引擎，麦克风/系统音频授权由 Application 另行按音源请求 |
| `AudioPlayback` | 无 | 输出设备不受 TCC 管辖 |

四项「无权限」能力返回 `Available` 是事实描述而非兜底：macOS 对它们确实不设隐私门。判断依据是能力是否落在上表的受控集合里，新增枚举值若两边都没登记会落到 `Unsupported`，并由 `EveryCapabilityIsClassifiedByTheMacOsModule` 直接失败。

#### 关于「已点击允许」

`MacPlatformPermissionRequester` **完全忽略** `PromptAsync` 的返回值，弹窗后一律重新 `Check`。因此 `CGRequestScreenCaptureAccess` 返回 `true` 但 TCC 尚未生效时，结果是带重启说明的 `Denied`，而不是 `Granted`。`NotDetermined` 与 `Denied` 都映射到 Contracts 的 `Denied`（契约没有第四态），差异通过 `Reason` 表达；`Restricted`（系统策略禁止，用户无法自行开启）映射到 `Unsupported`，且不弹窗。

#### 阶段门槛

所有 capability 都能正确返回三态，不存在无条件 `Available`。

**门槛结果：通过。** `EasyChat.Infrastructure.MacOS.Tests` 24 项，真机 23 通过 / 1 跳过（「不支持的主机应拒绝回答」按设计只在非 macOS 主机上运行）。覆盖：未决定、已授权、已拒绝、系统策略限制、无隐私入口、弹窗后仍未生效（重启语义）、运行中撤销、原生检查抛异常降级为 `Unsupported` 而不崩溃、查询期取消与弹窗期取消。真机 smoke 测试确认五项权限的原生入口全部解析成功。

Windows 侧未改动，`WindowsPlatformCapabilities` 的无条件 `Available` 保持原样——那是 Windows 的事实，不是 macOS 要照搬的语义。

#### Info.plist 与 entitlements 的实际改动

- `NSMicrophoneUsageDescription`：硬化运行时下缺失会在请求麦克风时直接终止进程。
- `CFBundleDisplayName`、`LSApplicationCategoryType`。
- entitlements 增加 `com.apple.security.device.audio-input`，这是硬化运行时允许 `AVCaptureDevice` 请求麦克风的前提。

屏幕录制**没有**对应的 Info.plist 用途字符串键：Apple 未定义这样的键，屏幕录制完全由 TCC 面板和 `CGRequestScreenCaptureAccess` 控制，所以不新增自造键。其余签名、公证相关的 entitlements 留在阶段 15。

### 阶段 5：macOS Host、应用生命周期和窗口桥

预计：3～5 人日。

**状态：代码完成；5.3 待真机验收（2026-09-12）**

5.1（Skia 配置）、5.2（窗口桥）、5.4（开机启动）均已落地；5.3 是主窗口的真机行为清单，`.app` 已能产出（阶段 15），可以开始验收。

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

**已实现（2026-09-12）**：`EasyChat.Infrastructure.MacOS/Input/MacOwnedWindowBehavior.cs` 与 `EasyChat.Desktop.MacOS/AvaloniaMacWindowBehavior.cs`。后者是唯一接触 UI 框架的一侧，只把平台句柄交给前者；`NSView → NSWindow` 的解析和全部 AppKit 消息都在 Infrastructure 内。注意架构测试禁止 macOS Infrastructure 源码中出现 "Avalonia" 字样，**注释也算**，所以该程序集里一律称「toolkit 平台句柄」。

| 职责 | AppKit 落地方式 |
| --- | --- |
| Topmost/NSWindow level | `setLevel:` = `NSFloatingWindowLevel` |
| Mission Control / Space | `setCollectionBehavior:` = `canJoinAllSpaces｜stationary｜ignoresCycle` |
| 全屏应用上方 | 同上再加 `fullScreenAuxiliary` |
| 提升但不抢焦点 | `orderFrontRegardless` |
| 鼠标穿透 | `setIgnoresMouseEvents:` |
| 屏幕捕获排除 | `setSharingType:` = `NSWindowSharingNone`，调用前先 `respondsToSelector:` 预检，不支持时返回 `false` 让调用方走视觉降级 |
| 应用失活后不隐藏 | `setHidesOnDeactivate:` = `NO`，否则用户切回被监听的应用时字幕会消失 |

**「非激活显示」的真实限制**：AppKit 没有办法阻止一个普通 `NSWindow` 成为 key window——只有以非激活样式创建的 `NSPanel` 才能做到，而 Avalonia 创建的不是 `NSPanel`。因此该保证不是来自某个属性，而是来自**显示方式**：用 `orderFrontRegardless` 抬升而绝不调用 `makeKeyAndOrderFront:` 或 `activateIgnoringOtherApps:`，前台应用就保住 key 状态和文本输入上下文。这条限制已写进 `MacOwnedWindowBehavior` 的文档注释，调用方不能把它和一个会激活的 show 配对使用。

因此 `RestoreForegroundTextInputContext` 在 macOS 上**不实现**（沿用契约的默认空实现）：Windows 需要它是因为要恢复 IME 上下文，而 macOS 的浮窗从未成为 key window，输入上下文根本没有离开过前台应用。这是「适配器不伪造行为」的一部分，不是遗漏。

真机验证：`MacOwnedWindowBehaviorTests` 确认 AppKit 加载后 `NSWindow` 及上表全部 selector（含 `setSharingType:`）在 macOS 26 上真实存在，并覆盖空句柄的拒绝路径与捕获排除的失败降级。窗口的可见行为必须等到 5.3 打出 `.app` 后真机验收。

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

- [x] 使用 `SMAppService.mainApp`。
- [x] 读取真实注册状态。
- [x] 用户在系统设置中禁用后正确反映。
- [x] 不手工写 `LaunchAgents`。
- [x] 不增加后台守护进程。

实现位置：`EasyChat.Infrastructure.MacOS/ApplicationStartup/MacApplicationAutoStartService.cs`，接缝 `IMacLoginItemGateway`（与阶段 4 的 `IMacPrivacyGateway` 同一模式：原生侧只报状态，映射策略留在适配器里以便测试）。

`SMAppServiceStatus` 到契约的映射：

| `SMAppServiceStatus` | `GetEnabled()` | `SetEnabled(true)` |
| --- | --- | --- |
| `enabled` | `true` | 成功 |
| `notRegistered` | `false` | 失败 `autostart.register-not-effective` |
| `requiresApproval` | `false` | 失败 `autostart.requires-approval`，提示前往「系统设置 > 通用 > 登录项与扩展」 |
| `notFound` | 失败 `autostart.bundle-not-found` | 失败 `autostart.bundle-not-found` |

`requiresApproval` 不当作已启用，理由是它确实不会在登录时启动。`SettingViewModel` 在 `SetEnabled` 失败时会弹出错误 toast 且**不翻转开关**，因此用户看到的是「需要去系统设置批准」，而不是一个假装打开的开关——符合规则 8「不允许假装成功」。

每次写入后都重新读取状态再判定，`GetEnabled` 也不缓存，所以用户在系统设置里关掉登录项后下次读取就是 `false`。

真机验证：`MacSystemLoginItemGatewayTests` 确认 `SMAppService` 类与 `mainAppService` 在加载 ServiceManagement 框架后真实解析（框架必须先 `NativeLibrary.Load` 才会向 Objective-C 运行时注册类，这一点决定了测试里的断言顺序）。测试只走读路径，不会在开发机上真的注册登录项。

#### 阶段门槛

主程序能以正确 `.app` 身份启动、关闭、隐藏、恢复、二次激活和开机启动。

**门槛状态：未达成。** 5.1 的 Skia 配置已与 Windows 对齐（`MaxGpuResourceSizeBytes = 16 MiB`）；worker 参数分发暂不需要，因为 macOS 还没有 worker（阶段 9～11）。5.2 的代码已落地并通过 selector 级真机验证，但**窗口的可见行为无法在没有 `.app` 的情况下验收**；5.3 的清单同理。两者都顺延到阶段 15 打出签名 `.app` 之后复核。

`--verify-composition` 缺失端口从 15 项降到 14 项（`IPlatformWindowBehavior` 已解决）。

### 阶段 6：剪贴板、应用枚举、焦点和文本写回

预计：5～7 人日。

**状态：已完成（2026-09-12）**

6.1～6.5 全部落地。`MacSelectedTextCapture` 已随阶段 7 的指针与键盘状态端口一并完成。应用矩阵（Safari / Chrome / Word / VS Code 等）的真机验收属于阶段 8。

#### 6.1 NSPasteboard

**状态：已完成（2026-09-12）**

实现：

- [x] `MacClipboardSnapshots`
- [x] `MacClipboardText`
- [x] `MacClipboardImage`

要求：

- [x] 使用 `changeCount` 作为 change token。
- [x] 备份剪贴板全部可读取 UTI 类型。
- [x] 恢复时保留类型和二进制内容。
- [x] `RestoreIfUnchangedAsync` 只在 change token 匹配时恢复。
- [x] 图片写入必须匹配 `ImageFrame` BGRA 数据。
- [x] 延迟提供者或无法序列化类型要返回可诊断错误，不能静默丢失。
- [x] 剪贴板读写串行化，防止并发覆盖。

落地要点：

- **串行化**：三个端口共享同一个 `MacPasteboard` 单例，内部一把 `SemaphoreSlim`。这不是防御式加锁——6.5 的选区抓取会「写入临时值 → 读回 → 恢复」，若并发读插进中间，用户会拿到 EasyChat 的临时内容。
- **全类型备份**：通过 `pasteboardItems` 遍历每个 item 的每个 UTI，逐个 `dataForType:` 取二进制。延迟提供者拒绝序列化时该类型返回 nil，`CaptureAsync` **直接失败**并列出这些 UTI（`clipboard.capture-incomplete`），而不是丢掉它们再假装恢复成功。
- **条件恢复**：`RestoreIfUnchangedAsync` 在写入前重读 `changeCount`，不一致就跳过并返回成功——用户在此期间复制的新内容优先，这是「不覆盖用户并发写入」的基线要求。快照只能兑付一次，重复恢复是 no-op 而不是重复写。
- **图片编码**：BGRA32 →`CGImageCreate`→ ImageIO 编码 PNG →`CFData`（与 `NSData` toll-free bridged）→`setData:forType:"public.png"`。全程 C 函数，没有 Objective-C 消息。位图标志用 `kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little`，即 32 位小端 ARGB，在内存中正是 B,G,R,A——与契约的 BGRA32 完全对应。非 BGRA32 的帧直接失败，不做静默转换。DPI 元数据暂未写入 PNG（契约未要求，粘贴目标一般也不读）。
- **autorelease pool**：AppKit 返回的都是 autoreleased 对象，而 .NET 线程自带没有 pool。每个剪贴板操作用 `objc_autoreleasePoolPush/Pop` 包一层，否则临时对象会泄漏到进程结束。`generalPasteboard` 这类跨操作持有的单例则显式 `retain`。

真机验证：`MacClipboardTests` 直接驱动真实的 general pasteboard（假实现验证不了任何 AppKit 管道），覆盖文本往返、change token 失效、快照恢复、条件恢复的两个分支（无人写入时恢复 / 用户已复制新内容时让路）、PNG 真实编码（校验 PNG magic），以及外来快照和外来 token 的拒绝。测试前用 `Capture()` 备份开发者自己的剪贴板、结束后 `Restore()` 放回，实测确认运行后剪贴板内容原样保留。

#### 6.2 目标 Token

**状态：已完成（2026-09-12）**

`ExternalTargetToken` 内部编码建议包含进程 ID、AX element/window identity、生成代次或会话校验值。

要求：

- [x] Token 不持久化。
- [x] 过期目标明确失败。
- [x] 非 Mac token 明确失败。
- [x] 不把编码格式暴露给 Application 或 Presentation。

实际编码：`mac:<session>:<pid>:<window>`，实现在 `Input/MacTargetTokens.cs`，`internal`，Application/Presentation 只做字符串传递与比较（与 Windows 的 `win32:<HWND hex>` 同一模式）。

`session` 是**进程启动时生成一次的随机值**，这是「不持久化」的执行手段而非声明：跨进程留存的 token、或其他平台铸造的 token，解码时直接失败，不会落到「碰巧占用同一个 pid 的进程」上。`window` 目前恒为 0（表示「该应用的前台窗口」），字段保留给后续需要精确到窗口的场景。

#### 6.3 应用枚举

**状态：已完成（2026-09-12）**

实现 `MacRunningProcessCatalog`：

- [x] 枚举可交互、拥有可见窗口的应用。
- [x] 稳定身份使用 bundle identifier。
- [x] 名称使用 localized application name。
- [x] 描述来自 bundle metadata（`CFBundleGetInfoString`，回退 `CFBundleShortVersionString`）。
- [x] 图标转成 PNG（`NSRunningApplication.icon` → `TIFFRepresentation` → `NSBitmapImageRep` → PNG）。
- [x] 过滤 EasyChat 自身和无 UI 后台进程。
- [x] 黑白名单仍只比较 Contracts 中的字符串身份。

两处需要明确的取舍：

- 「拥有可见窗口」用 `NSApplicationActivationPolicyRegular` 近似。真正的「有可见窗口」要走 `CGWindowList`，而那需要屏幕录制授权——**为了填一个应用选择列表就索要屏幕录制是不能接受的**。Regular policy 即「有 Dock 图标、有界面」的应用集合，是不触发任何授权的最接近答案。
- 同理，`WindowTitle` 恒为 `null`：读取窗口标题同样需要屏幕录制。契约允许为空，所以留空，而不是伪造一个。

无 bundle identifier 的极少数应用回退到 localized name 作为身份，保证用户保存的黑白名单条目重启后仍能匹配。

#### 6.4 窗口和焦点

**状态：已完成（2026-09-12）**

实现：

- [x] `MacWindowFocus`
- [ ] `MacWindowInputTransparency` —— **不实现**，见下。

能力：

- [x] 当前前台应用（`NSWorkspace.frontmostApplication`）。
- [x] 当前 AX 聚焦元素（`AXUIElementCreateSystemWide` → `AXFocusedApplication` → `AXUIElementGetPid`）。
- [x] 激活目标应用（`NSRunningApplication.activateWithOptions:`，轮询确认已到前台）。
- [x] 恢复原目标（同上，调用方持 token 回切）。
- [x] 校验目标是否已退出（`isTerminated` / 查无此应用 → `window.target-exited`）。
- [x] 不激活浮窗 / 点击穿透 —— 由阶段 5.2 的 `IPlatformWindowBehavior` 承担。

关键差异：**macOS 没有 Win32 那种「按窗口」的前台概念，激活是按应用进行的**，所以 token 标识的是应用。

`GetFocusedTargetAsync` 在缺少辅助功能授权时**返回失败而不是退回到 frontmost application**。这不是保守：前台应用和键盘焦点应用恰恰在有意义的时候才会不同（例如浮窗、输入法面板），用 frontmost 冒充 focused 会把文字写进错误的应用。

`IWindowFocus.ConfigureNoActivateAsync` 在 macOS 上**明确返回失败**（`window.no-activate-unsupported`）：这个端口标识的是**别的应用**的目标，EasyChat 不去改别人窗口的样式；自己的浮窗走 5.2 的窗口行为端口。核查过 Application 与 Presentation 都不调用该方法，也不调用 `IWindowInputTransparency`（Presentation 统一走 `IPlatformWindowBehavior`），因此后者不注册也不实现，避免造一个没有调用方的适配器。

真机验证：前台应用 token 能解码并反查到真实 bundle identifier；Finder 必然出现在枚举结果中；图标确实是 PNG；已退出目标返回 `window.target-exited` 而不是激活别的应用；外来平台 token 在任何原生调用之前就被拒绝。

#### 6.5 文本选择和写回

**状态：部分完成（2026-09-12）**

实现：

- [x] `MacTextSelection`
- [x] `MacTextDelivery`
- [x] `MacSelectedTextCapture`（2026-09-12 完成，随阶段 7 的指针与键盘状态端口一并落地）

选择读取顺序：

1. [x] Accessibility 直接读取 selected text（`AXSelectedText`）——**优先这条是因为它什么都不动**：不注入按键、不写剪贴板、用户察觉不到。
2. [x] AX selected text range —— 由 `MacTextSelection` 在 `CaptureAll` 时负责。
3. [x] `Command+C` + NSPasteboard，仅在 AX 失败时走。
4. [x] 按 change token 安全恢复剪贴板。

判定「复制完成」用的是 **`changeCount` 变化**而不是「剪贴板非空」：用户很可能本来剪贴板里就有同一段文字，靠内容判断会误判。

`selection.keyboard-busy`：用户还按着自己的修饰键时注入 Command+C 会组合出谁也没要的按键，所以直接让路。

**密码框**：`MacAccessibilityText.ReadSelectedText` 对 `AXSecureTextField` 返回 null，读取侧的守卫至此落地（阶段 6.5 里说明过，`Type`/`Paste` 注入路径做不到这个检查，因为 CGEvent 不携带目的地）。

写回模式：

- [x] `Type`：`CGEventKeyboardSetUnicodeString` 逐字符输入，按 rune 而非 UTF-16 code unit 迭代，避免把代理对拆开；换行走 `kVK_Return`。
- [x] `Paste`：快照剪贴板 → 写入译文 → `Command+V` → 恢复。粘贴后有 200 ms 落地延迟，否则剪贴板先被恢复，目标读到的是旧内容而不是译文。
- [x] `Message`：通过 AX 设置 `AXSelectedText`。元素不支持该属性时返回 `text-delivery.message-unsupported`，**不假装成功**。
- [x] 标准命令通过 `StandardTextCommand` 映射（见下）。

`StandardTextCommand` 的 macOS 映射：

| 命令 | macOS | 说明 |
| --- | --- | --- |
| `SelectAll` | `Command + A` | 完成阶段 2 遗留项 |
| `Copy` | `Command + C` | |
| `Paste` | `Command + V` | |
| `Delete` | `Backspace`（`kVK_Delete` = 51） | **不用 forward delete**：Backspace 在所有 macOS 文本控件里都会删除当前选区，而且每块 Mac 键盘上都有；forward delete 两条都不成立 |

`MacKeyCombination` 复用 Windows 的快捷键词汇（`Ctrl` / `Alt` / `Shift` / `Win` / `Windows` / `Meta`），其中 `Meta` 及其旧设置别名 `Win`、`Windows` 一律映射到 Command。键码用的是 HIToolbox 的 ANSI **位置**而非字符，因此录制为「A」的快捷键在 AZERTY 布局下仍然工作。

`MacTextSelection` 直接设置 AX 选区而不是发 `Command+A`，因为它能**读回实际生效的选区**——调用方正是靠这个区分「真的全选了」和「控件忽略了请求」。无焦点文本元素（包括未授予辅助功能）时返回 `HasFocusedControl=false`，调用方据此回退到按键命令，与 Windows 上遇到非编辑控件的行为一致。

**密码框**：`Message` 模式在写入前检查 AX role，`AXSecureTextField` 直接返回 `text-delivery.secure-field`。必须说明的是，`Type` 和 `Paste` 模式**做不到这个检查**——CGEvent 投递出去时不携带目的地，macOS 不告诉你谁会收到。这正是「读取侧必须拒绝密码框」的原因，该守卫随 `MacSelectedTextCapture` 一起落地。

**授权前提**：合成事件需要辅助功能授权，未授权时 macOS **静默丢弃**事件且不报错。因此调用方必须先查 capability（阶段 4 已经做到），不能把「post 成功」当作「按键送达」。

测试策略：**不注入任何真实按键，也不在任何应用里改动选区**——那样会打到开发者当时正在用的窗口里。可测的部分（命令映射、快捷键词汇、非法组合、未知模式、无焦点元素）全部覆盖；当测试宿主碰巧持有辅助功能授权时，涉及 AX 的用例标记 Inconclusive 而不是去动前台应用。真实按键注入的验收放到阶段 8 的划词工具栏链路和「重点兼容应用」矩阵。

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

**状态：已完成（2026-09-12），`WindowMoveStarted` 归入阶段 8**

四个适配器全部落地。`MacPointerPosition` 随阶段 9.1 的 `MacDisplayGeometry` 一并完成。

#### 实现

- [x] `MacGlobalHotkeys`
- [x] `MacGlobalPointerMonitor`
- [x] `MacPointerPosition`（见阶段 9.1）
- [x] `MacKeyboardState`

**为什么两个指针端口要等阶段 9**：契约里的 `PhysicalScreenPoint` 是「统一桌面**物理像素**」，而 CoreGraphics 的全局坐标（`CGEventGetLocation`、`CGDisplayBounds`）是**点**（逻辑单位）。两者之间的换算需要每块显示器的 backing scale factor，也就是阶段 9 的 `IScreenCatalog` / `ScreenDescriptor` 要建立的那张表。现在自己搭一套换算等于把阶段 9 的活先做一遍再扔掉，所以指针端口顺延，与 `MacDisplayGeometry` 一起落地。

#### 快捷键

要求支持：

- [x] 普通注册。
- [x] 冲突探测（`ProbeAsync` 注册后立即释放——这是 macOS 唯一会告诉你组合是否被占用的方式）。
- [x] 注销。
- [x] Command/Option/Control/Shift。
- [x] 功能键。
- [x] OEM/符号键（`Equal`、`Minus`、`LeftBracket`、`Semicolon`、`Comma`、`Grave` 等已在键表内）。
- [x] 多个快捷键并存。
- [x] 同声传译的按下和释放回调（`kEventHotKeyPressed` / `kEventHotKeyReleased`）。
- [x] 应用隐藏时触发。
- [x] 用户注销/睡眠/唤醒后恢复。
- [x] 输入法切换后仍按物理按键匹配。

**选 Carbon 而不是 CGEventTap 的理由**：`RegisterEventHotKey` **完全不需要任何隐私授权**，而 event tap 在拿到输入监视授权前一个键都收不到。Carbon 还是按虚拟键码（物理键位）注册的，所以「切换输入法后仍按物理按键匹配」是这条路径的自然属性而不是额外实现；注册登记在窗口服务器里而不是本进程持有的事件流上，所以隐藏、后台、睡眠唤醒后都继续有效。

Carbon 事件派发在**主线程 run loop** 上。因此回调里只做一件事：把托管工作丢给线程池后立即返回——否则一次慢翻译会卡住应用正在等的所有其他事件。回调抛出的异常被吞掉并隔离，一个失败的动作不能连累其余快捷键的处理器。

不是 EasyChat 注册的热键返回 `eventNotHandledErr`，让事件继续沿处理器链传递，而不是被我们吞掉。

真机验证：`MacGlobalHotkeysTests` 在真实窗口服务器上注册（用四个修饰键 + 功能键的组合，且用完立即注销，不会劫持开发者已绑定的快捷键），覆盖注册/注销/重复注销/三个快捷键并存/探测后不占用/按住式双边注册，以及重复注册同一组合确实返回 `eventHotKeyExistsErr` 并被映射成 `hotkey.conflict`。回调触发本身需要主线程 run loop，测试宿主没有，留到阶段 8 真机验收。

#### 键盘状态

`MacKeyboardState` 用 `CGEventSourceKeyState` 读取按键当前是否按下——读的是会话合并状态而不是事件流，因此不需要 event tap。契约里 side-agnostic 的 Control/Alt/Shift 两侧任一按下即为 true，Command 保留契约要求的左右区分。

#### 鼠标监听

**状态：已完成（2026-09-12），`WindowMoveStarted` 除外**

需要产生现有契约事件：

- [x] `PrimaryPressed`。
- [x] `PrimaryReleased`。
- [x] `PrimaryDoubleClick` —— 用 `kCGMouseEventClickState`，即 macOS 自己累计的连击数，不需要像 Windows 那样自己做时间窗和位移判定。
- [ ] `WindowMoveStarted` —— **未实现**，见下。

事件携带：

- [x] 统一桌面物理像素坐标（经 `MacDisplayGeometry` 换算）。
- [x] 前台目标（`NSWorkspace.frontmostApplication`）。
- [x] 鼠标下目标（`CGWindowListCopyWindowInfo` 取最前一个包含该点的窗口，用其 owner pid + window number 编码 token）。
- [x] EasyChat Overlay 判断（鼠标下窗口的 owner pid 是否等于本进程）。
- [x] 时间戳。
- [x] 剪贴板序列（`NSPasteboard.changeCount`）。
- [ ] `CapturedTarget` —— **macOS 没有鼠标捕获窗口这个概念**，因此恒为空。`SelectionInteractionCoordinator` 对空 token 的守卫会自行停用，而不是去比较一个编造出来的值。

窗口列表的 window number、owner pid、bounds、layer **不需要任何隐私授权**；被屏幕录制授权挡住的只有窗口标题和内容，本适配器两者都不读。

**`WindowMoveStarted` 为什么留空**：Windows 靠 `EVENT_SYSTEM_MOVESIZESTART` 或比较手势前后的窗口矩形来判定。macOS 的等价做法是在 mouse-down 记下鼠标下窗口的 bounds、mouse-up 再比一次——`CGWindowListCopyWindowInfo` 能拿到 bounds，所以技术上可行。没有本轮做的原因是它的**行为正确性无法在没有输入监视授权的测试宿主里验证**，而这个事件的作用是抑制「拖动窗口标题栏被误判为划词」，做错了比没做更糟。留到阶段 8 的真机链路，连同 tap 回调本身一起验收。

#### 线程要求

- [x] CGEventTap 使用独立 RunLoop。
- [x] 回调中不运行翻译或 UI 工作。
- [x] 回调快速转发到 C# 队列。
- [x] event tap 被系统禁用时尝试恢复，并记录失败。
- [x] Dispose 必须退出 RunLoop，不遗留后台线程。

tap 用 `kCGEventTapOptionListenOnly`：EasyChat 只观察点击，永不修改或吞掉事件，因此这里出任何故障都不可能让用户的鼠标失灵。

tap 回调里只做一件事：打时间戳后塞进 Channel。解析前台应用、鼠标下窗口、剪贴板状态全部在 pump 线程做——回调里任何慢操作都会表现为**全系统输入延迟**。

独立线程独立 run loop，而不是借用 UI 的 run loop：后者会把鼠标延迟和界面正在做的事绑在一起。

macOS 会在 tap 太慢或用户强制关闭时把它禁用（`kCGEventTapDisabledByTimeout` / `ByUserInput`，这两个事件无视 mask 照样投递）。重新 enable 是官方的恢复手段，不做的话监视器会**静默死掉**。

`TryStart` 对 run loop 线程的启动应答设了 2 秒上限：UI 路径上的调用方绝不能被一个没返回的原生调用无限期阻塞。Dispose 先 `CFRunLoopStop` 再 join 线程，之后才释放 CoreFoundation 句柄——句柄归创建它的那个线程所有，没有后台线程可以活过这次调用。

#### 阶段门槛

所有现有快捷键动作可触发，按住式同声传译释放事件可靠，鼠标监听不造成系统输入延迟。

**门槛状态：未达成。** 快捷键的注册侧已在真机验证，但「可触发」和「释放事件可靠」要在主线程 run loop 下才能验收，即阶段 8 的真机链路；鼠标监听尚未实现。

### 阶段 8：划词工具栏完整链路

预计：3～5 人日。

**状态：接线已完成；任务 2～13 全部是真机验证项（2026-09-12）**

这一阶段不新增 macOS 专用划词逻辑，只连接现有 Application 协调器。

**接线部分已经完成**：`SelectionInteractionCoordinator` 是共享 Application 代码，它依赖的四个端口——`IGlobalPointerMonitor`（阶段 7）、`ISelectedTextCapture`（阶段 6.5）、`IWindowFocus`（阶段 6.4）、`IPlatformWindowBehavior`（阶段 5.2）——都已在 macOS 上注册，`--verify-composition` 里没有任何与划词相关的缺失端口。

**其余任务全部需要真机操作才能判断**：拖动选择、双击选择、修饰键释放时机、工具栏位置与焦点、屏幕边缘调整、多显示器与负坐标，都要人在装好 `.app` 并授予辅助功能的机器上实际操作。自动化测试替代不了这一层，所以不标记完成。

**`WindowMoveStarted` 归属本阶段**（阶段 7 已说明理由）：它的作用是防止「拖动窗口标题栏被误判为划词」，正确性只能在授予输入监视后于真机判断，做错了比没做更糟。实现方式已确定——在 mouse-down 记录鼠标下窗口的 bounds，mouse-up 再比一次，`CGWindowListCopyWindowInfo` 已经能提供 bounds。

任务 12「黑白名单使用 bundle identifier」已由阶段 6.3 的 `MacRunningProcessCatalog` 满足；任务 13「EasyChat 自己的窗口不能触发外部划词」已由指针事件的 `PointerTargetIsOverlay`（比较 owner pid 与本进程）满足。

#### 任务

1. [x] 接入 `SelectionInteractionCoordinator`。
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

**门槛状态：未达成，需真机验收。** 前置条件（`.app`、辅助功能授权）已具备。

### 阶段 9：截图和显示器坐标系统

预计：4～6 人日。

#### 9.1 ScreenCatalog

**状态：已完成（2026-09-12）**

实现 `MacScreenCatalog`：

- [x] 获取所有显示器。
- [x] 使用稳定 display identifier。
- [x] 返回物理像素 Bounds。
- [x] 正确处理主屏之外的负坐标。
- [x] 使用 backing scale 计算有效 DPI：`Dpi = 96 × backingScaleFactor`。
- [x] Quartz 左下角坐标必须转换成 Contracts 左上角统一坐标 —— 见下，实际**不需要翻转**。
- [x] 不允许 Presentation 处理 macOS 坐标特例。

**坐标翻转是计划里的一个误判。** `CGDisplayBounds` 用的是 global display space，原点在主显示器**左上角**、y 向下增长，正是契约要的方向。左下角原点属于 AppKit 的 `NSScreen`，而本适配器不用它。所以这里没有任何翻转代码——`CGDisplayBounds` 真正需要换算的是**单位**：它给的是点，不是像素。

稳定身份用 `vendor-model-serial-unit` 四元组，因为 `CGDirectDisplayID` 会在重启和重新接线后被重新分配。

**混合 DPI 的诚实说明**：每块显示器的点矩形按**它自己**的 backing factor 缩放。所有显示器同 scale 时这是精确的；scale 不同时，两块屏交界处的像素矩形可能出现缝隙或重叠——因为 macOS 本来就是按点排布显示器的，不存在一张跨屏的统一像素网格。所有操作都先把点定位到所属显示器再在该显示器内换算，因此这个缝隙不会影响任何一次真实截图或指针读数。

#### 9.1.1 实测：Avalonia 12.1.1 在 macOS 上的 Screens 与契约不一致

这是**实测发现，计划此前不知道**，且它决定了 9.2 和阶段 14 怎么做。测量环境：MacBook Pro 内建 Retina 屏，缩放分辨率。

| 来源 | Bounds | Scale |
| --- | --- | --- |
| `CGDisplayBounds` + `CGDisplayMode` | `(0,0,1408,881)` 点 / `2816×1762` 像素 | 2.0 |
| `NSScreen.frame` + `backingScaleFactor` | `(0,0,1408,881)` 点 | **2.0** |
| **Avalonia `Screen.Bounds` / `Screen.Scaling`** | `(0,0,1408,881)` | **1** |
| Avalonia `Window.RenderScaling` | — | **2**（正确） |

即：**Avalonia 12.1.1 的 `Screen.Bounds` 是点而不是物理像素，且 `Screen.Scaling` 恒为 1**，尽管它自己渲染时用的 `RenderScaling` 是正确的 2。

这带来一个真实冲突。`CaptureOverlayGeometry.MatchesTopology` 会把 `ScreenDescriptor.Bounds` 当作 `PixelRect` 去和 Avalonia 的 `Screen.Bounds` **逐值比较**，`ScaleX/ScaleY` 和 `Screen.Scaling` 比较：

- 若 `MacScreenCatalog` 报告点（1408×881，scale 1）以迎合 Avalonia：拓扑校验通过，但截图只有 Retina 屏一半的分辨率，OCR 输入质量直接减半，阶段 9 门槛「Retina 截图区域像素对齐」不成立。
- 若报告真实物理像素（2816×1762，scale 2）：契约正确、截图原生分辨率，但拓扑校验失败，截图覆盖层拒绝启动。

**本次选择后者**，理由是不能为了绕过一个上游 bug 而永久性地把核心功能的输入质量砍半。

**由此产生一个待决问题，需要你拍板**（我没有擅自改共享代码）：`MatchesTopology` 需要改成在**逻辑空间**比较——两边各自 `Bounds / Scale` 之后再比。Windows 侧（描述符像素+scale s，Avalonia 像素+scale s）结果不变，macOS 侧（描述符 2816/2=1408，Avalonia 1408/1=1408）则能对上，且**不需要任何 OS 分支**。这是平台无关的重述，但它动的是共享 Presentation 代码且有 Windows 回归面，所以留给你决定后再做。在此之前 macOS 的截图覆盖层无法通过拓扑校验。

#### 9.2 ScreenCapture

**状态：代码完成，采集路径未经真机验证（2026-09-12）**

实现 `MacScreenCapture`：

- [x] `PrimaryScreen` / 指定 `Screen` / 指定 `Region`。
- [x] 使用 ScreenCaptureKit/SCScreenshotManager。
- [x] 输出 BGRA32。
- [x] 修正行方向、stride 和 alpha。
- [x] 保持实际物理像素尺寸（`MacDisplayGeometry` 提供换算，见 9.1）。
- [x] 排除 EasyChat overlay（`SCContentFilter` 排除 owner pid 为本进程的窗口）。
- [x] 屏幕锁定、权限撤销、显示器断开时返回 Result 失败。
- [x] 不使用废弃 CGWindowList 截图作为主路径。

**⚠️ 采集路径未经真机验证。** 见下方「验证状态」。

**已完成并验证的部分：像素管线。** `ImageEncodingNative.CreateImage` / `ReadBgra32` 负责 `CGImage ↔ BGRA32` 的双向转换，这是截图里**真正容易出错**的地方——通道顺序搞反、行方向翻转、stride 没去 padding，产出的图看起来仍然像一张截图，只有 OCR 质量会暴露问题。因此这三点是用已知字节逐位断言的，不靠肉眼：

- 逐字节往返一致（通道顺序）。
- 每行填入不同值，断言内存第一行就是图像顶部（行方向）。
- 输入带 16 字节行填充，断言读回是紧凑排列（stride 归一化）。

读回刻意走「画进自己指定格式的 bitmap context」而不是直接读源图的 data provider：采集到的图可能是任意色彩空间、任意 alpha 排布、任意行填充，画一次就全部归一化。

**块（block）机制已独立验证。** 所有异步 Apple API（麦克风授权、ScreenCaptureKit）都靠手工构造的 global block 回调。此前只验证了它的头部布局，本轮补了一个真正的调用验证：把 block 交给 `-[NSArray enumerateObjectsUsingBlock:]`，断言运行时确实按元素回调并传入正确下标——这条路不需要任何隐私授权。isa / flags / descriptor / invoke 四项至此全部经过真实调用。

**验证状态（2026-09-12，经用户确认「先写下来，验证留到以后」）**

`CGPreflightScreenCaptureAccess()` 在测试宿主上返回 `false`——终端未被授予屏幕录制。ScreenCaptureKit 的六个类（`SCShareableContent`、`SCContentFilter`、`SCStreamConfiguration`、`SCScreenshotManager`、`SCDisplay`、`SCWindow`）已确认在 macOS 26 上全部存在，代码已按其对象图写完，但**没有一次真实截图跑通过**。

| 部分 | 验证状态 |
| --- | --- |
| block 回调机制 | ✅ 真实调用验证（`NSArray enumerateObjectsUsingBlock:`） |
| `CGImage ↔ BGRA32` 像素管线 | ✅ 已知字节逐位验证（通道序、行方向、stride 归一化） |
| 显示器几何与区域换算 | ✅ 真机验证（9.1） |
| 无授权时返回明确失败 | ✅ 真机验证 |
| **ScreenCaptureKit 对象图与真实采集** | ❌ **未验证** |

授权后必须补验的点：

1. `SCScreenshotManager` 返回图的实际像素尺寸是否等于 `ScreenDescriptor.Bounds`——直接对应阶段门槛的「Retina 像素对齐」。
2. `setSourceRect:` 传 `CGRect`（arm64 上是 4 个 double 的 HFA，走浮点寄存器）是否被正确编组；这是整段代码里 ABI 风险最高的一处。
3. `SCContentFilter` 排除 EasyChat 窗口是否真的生效。
4. 权限运行中被撤销、显示器断开、屏幕锁定时是否按 `Result` 失败而非返回空图或半张图。

设计上的两个取舍：

- **不保留 `CGDisplayCreateImage` 兜底**。它在 macOS 26 上仍可用，但已被 Apple 取代；为「唯一一个像素保真度直接决定 OCR 质量」的功能维护两套采集路径、两套像素行为，不划算。
- **跨显示器的区域请求直接失败**而不是截一部分：这样的区域在 macOS 的点空间里没有单一表示，截半张图比明确报错更糟。

#### 9.3 截图 Session 与 worker

**状态：已完成（2026-09-12），握手链路已真机验证**

在 Mac Host 实现：

- [x] `MacScreenshotCaptureSession`
- [x] `MacScreenshotWorker`

- [x] 保留当前 worker 隔离目的：截图大缓冲、Avalonia overlay 资源、OCR 前图像，并在 worker 完成后回收内存。
- [x] IPC 用 Unix domain socket。
- [x] worker 直接运行同一 bundle executable 的 worker mode，不创建第二个 Dock 应用实例。

**隔离的理由是内存而不只是崩溃**：一张全桌面 Retina 帧约 20 MB，长截图是它的若干倍。让 helper 退出等于把这些内存直接还给系统，而不是留在主进程堆里熬完整个会话。因此 session 在拿到应答后**立即退役 worker** 而不是留着热身——留着就等于留住了这套设计本来要释放的那些缓冲。

**不创建第二个 Dock 图标**：activation policy 来自共享的 Info.plist，运行时只能收窄不能放宽，所以 worker 启动后立刻调用 `[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory]`（封装为 `MacApplicationPresentation.TryHideFromDock`）。该调用已在真机上验证返回成功。

**socket 路径刻意放在临时目录并保持短**：平台对 Unix socket 路径有一百余字节的硬上限，完整的 Application Support 路径可能超出。

协议按 Windows 的框架**复刻**而非共享：两个 Host 不允许互相引用，且各自只和自己可执行文件产出的 worker 对话，版本号从来不需要一致。代价是两份小体量的帧格式代码。

**真机验证（2026-09-12）**：用一个临时监听端启动 `EasyChat --screenshot-worker <socket>`，确认同一可执行文件以 worker 模式重新拉起、Unix socket 连通、Avalonia 在 worker 中完成启动、Dock 抑制未导致崩溃，并收到正确的 Ready 帧（`magic=0x50414353 version=4 message=0`）。这条链路里风险最高的「同一 bundle 起第二个 Avalonia 进程」至此得到确认。

**仍未验证**：真正的选区与采集（依赖屏幕录制授权，见 9.2 的验证状态表），以及 Dock 图标是否确实不出现（需要目视确认，`setActivationPolicy:` 返回成功只是必要条件）。

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

**状态：阻塞。计划中的 runtime 方案经实测不可用（2026-09-12）**

原计划：

- `Sdcb.OpenVINO.runtime.osx.12.6-arm64`。
- 对应 OpenVINO/PaddleOCR managed binding。
- 确认 dylib 安装名和相对路径。
- 所有 dylib 放入 `.app/Contents/Frameworks`。
- 修改 rpath 后再签名。
- worker 和主进程都能解析同一 runtime。

#### 实测结论：现有 OCR 技术栈在 macOS arm64 上缺两块原生件

包本身存在且版本与 Windows 侧一致（`Sdcb.OpenVINO.runtime.osx.12.6-arm64 2026.2.0`，Windows 用 `win-x64 2026.2.0`），还原正常，`OVCore` 也能初始化。但：

**1. 没有任何推理设备插件。** 该包只含核心 runtime（`libopenvino`、`libopenvino_c`）、五个模型前端（IR / ONNX / Paddle / TensorFlow / TF-Lite / PyTorch）以及 TBB、hwloc。**没有 `libopenvino_arm_cpu_plugin.dylib`，也没有 `plugins.xml`。**

实测：

```text
OpenVINO devices: []
```

即 OpenVINO 能**解析** PaddleOCR 模型，但无法 `CompileModel`、无法推理。这不是路径或 rpath 问题——文件根本不在包里。

**2. OpenCvSharp 原生库缺失。** `Sdcb.OpenVINO.PaddleOCR` 的预处理依赖 OpenCvSharp，而依赖链只带来托管的 `OpenCvSharp.dll`，没有 `libOpenCvSharpExtern.dylib`：

```text
OpenCvSharp native FAILED: TypeInitializationException
```

因此阶段 10 的 Runtime 小节建立在一个**错误前提**上：它假设「已经存在 macOS ARM64 OpenVINO runtime」（这句判断也出现在计划第 1 节的 CPU 架构假设里，是选择 Apple Silicon 首发的理由之一）。实际存在的只是一个**不含推理插件**的核心包。

**连带影响**：计划在「原则」里明确写了「不用 Apple Vision 替换现有 OCR」，理由是那会改变语言范围、模型管理和结果语义。这条禁令的前提是 OpenVINO 路线可行——现在这个前提不成立，因此该决策需要重新做，不能照旧执行。

三条可选路线（成本与代价差异很大，需要决策）：

| 路线 | 保留什么 | 代价 |
| --- | --- | --- |
| A. Apple Vision（`VNRecognizeTextRequest`） | 无需模型下载、无原生打包签名负担、Apple Silicon 上速度好 | 语言范围从约 90 种缩到 Vision 支持的约 20 种；模型下载/删除设置页在 macOS 上失去意义；结果语义不同（归一化包围盒，旋转角表达方式不一致） |
| B. 自行构建 OpenVINO ARM CPU 插件 + OpenCvSharp 原生库 | 模型 ID、语言 ID、下载校验和、结果语义**全部不变** | 需要从源码构建 OpenVINO 与 OpenCV 的 macOS arm64 版本，自行 vendoring、改 rpath、签名公证，并长期维护这套构建 |
| C. 换用 ONNX Runtime 推理 | 语言范围与结果语义可保留；仓库已依赖 `Microsoft.ML.OnnxRuntime`（MicroASR 在用），**实测 macOS arm64 可用且带 CoreML 执行提供程序**（见阶段 12） | PaddleOCR 模型需转换为 ONNX，**下载目录与校验和必须改**，与计划「下载校验和保持兼容」冲突 |

**决策（2026-09-12，用户）：暂缓 OCR，先推进其他阶段。** 阶段 10 挂起；阶段 11（图片文字清除）同样依赖 OpenCvSharp 原生库，因此一并挂起。上表三条路线保留待定，选定前不写实现代码。

被挂起的两个阶段对应的未注册端口：`IOcrRecognizer`、`IOcrModelStore`、`IImageBackgroundCleaner`、`IImageTranslationModelStore`。

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

**状态：挂起（2026-09-12）。** 与阶段 10 同一个原因：`Sdcb.OpenVINO.PaddleOCR` 与图片清除都依赖 OpenCvSharp 原生库，而 macOS arm64 上没有可用的 `libOpenCvSharpExtern.dylib`（实测见阶段 10）。OCR 路线选定后一并处理。

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

#### 前置实测：ONNX Runtime 在 macOS arm64 上可用（2026-09-12）

语音识别走的是平台无关的 MicroASR（`Microsoft.ML.OnnxRuntime 1.28.0`，位于共享 Infrastructure），因此它能否在 macOS 上跑是本阶段的决定性前提。实测结果：

```text
ONNX Runtime native OK. providers: [CoreMLExecutionProvider, WebGpuExecutionProvider, CPUExecutionProvider]
```

与阶段 10 的 OpenVINO 形成鲜明对比：ONNX Runtime 的 macOS arm64 原生库**完整**，且带 CoreML 执行提供程序。识别这一半不需要任何 macOS 专有适配，只需接上 PCM 采集。

因此本阶段的实际工作量集中在**采集侧**（系统音频、应用音频、麦克风），而不是识别侧。

#### 本阶段进度（2026-09-12）

- [x] 12.1 音频源目录与 token（`MacAudioCaptureSourceCatalog`、`MacAudioSourceTokens`）——真机验证。
- [x] 12.2 系统与应用音频（ScreenCaptureKit）——已实现，采集路径未经真机验证，见下。
- [x] 12.3 麦克风采集（`AudioQueue`）——已实现，见下。
- [x] 12.4 编排与格式转换（`MacPcmAudioCapture`、`PcmAudioConversion`）——纯托管、完整测试。
- [x] 12.5 复用 MicroASR——无需任何 macOS 适配（见上）。

#### 12.4 编排：为什么先做这一层

多源缓冲、成帧、混音、关闭时序是这条链路里**并发错误真正发生的地方**，而它们一个都不需要麦克风才能触发。因此 `MacPcmAudioCapture` 把采集抽到 `IMacAudioSourceFactory` / `IMacAudioSourceStream` 之后，编排本身用脚本化的假音源完整测试：

- 输出恒为整帧（16 kHz 单声道 16 位、20 ms = 640 字节）。
- **不能整除帧长的块结转到下一块**，而不是补零或丢弃——补零会插入爆音，丢弃会导致漂移。
- 尾部不足一帧的数据不作为短帧发出。
- 两个源求和成一路。
- 某个源提前结束不影响其余源继续产出。
- 某个源失败时**结束整条采集**而不是转为静音——工作流才能据此报错，而不是看起来在听一段没有声音的输入。
- 非本平台签发的 token 被忽略且**根本不会到达平台层**。
- 取消及时返回，且**每个已打开的源都被关闭**。
- 预热打开后立即关闭，不把设备留着；预热失败只记日志不阻止启动。

缓冲是**有界且丢弃最旧**的：识别跟不上时有用的是最新的语音，让缓冲无限增长等于把固定延迟换成无限延迟，最终换成进程内存。

#### 12.2 系统与应用音频

ScreenCaptureKit 的音频输出**只通过 delegate 协议投递，没有 block 版本**，必须存在一个真正的 Objective-C 类来接收回调。这是此前最大的未知。

该机制已构建并**独立验证**：`ObjectiveCClassBuilder` 在运行时用 `objc_allocateClassPair` / `class_addMethod` / `objc_registerClassPair` 定义类，方法实现是导出为函数指针的托管静态函数。测试创建一个带 `v@:@q` 编码方法的类、实例化、发消息，断言回调触发**且两个参数原样到达**——类型编码写错会破坏调用栈而不是干净失败，所以必须断言参数而不只是断言方法跑了。该测试不需要任何授权。

落地要点：

- **系统音频 = 覆盖整个显示器的 filter**。ScreenCaptureKit 里「音频」是 filter 所覆盖内容的音频，因此系统级音频用 `initWithDisplay:excludingWindows:`，单应用用 `initWithDisplay:includingApplications:exceptingWindows:` 并只放入目标应用。
- **`excludesCurrentProcessAudio = YES`**：不设这个，EasyChat 会听见自己合成的语音和字幕并送回识别。
- **没有纯音频模式**，流必然携带视频。配置成 2×2 帧、队列深度 3，把这部分开销压到接近零，而不是合成并投递整屏画面再立刻丢弃（计划里「无视频需求时仍配置最小化视频开销」即此）。
- **请求单声道**：投递的是非交错 32 位浮点，单个平面就是一段连续的 float，可直接复制；请求立体声会拿到两个独立平面，还得先交错。
- **采样率用 ScreenCaptureKit 自己的 48 kHz**，再交给共享的 `PcmAudioConversion` 重采样到识别器的 16 kHz——直接申请 16 kHz 不保证被接受。
- 采集回调跑在专用的**串行** dispatch queue 上：音频必须保序，并发队列会让两个缓冲同时被处理。
- 屏幕录制授权**预检**而不是从 ScreenCaptureKit 的模糊错误里反推，用户才能知道缺的是哪一项。

**API 形状已在 macOS 26 上逐一核实**（无需任何授权）：`SCStreamConfiguration` 的 `setCapturesAudio:` / `setExcludesCurrentProcessAudio:` / `setSampleRate:` / `setChannelCount:` / `setQueueDepth:`，`SCContentFilter` 的两个初始化器，`SCStream` 的 `initWithFilter:configuration:delegate:` / `addStreamOutput:type:sampleHandlerQueue:error:` / `startCaptureWithCompletionHandler:` / `stopCaptureWithCompletionHandler:`，`SCRunningApplication.processID` —— **全部存在**。

**仍未验证**：真实采集需要屏幕录制授权，与 9.2 同一限制。授权后需要确认的是运行时行为而非 API 形状：采样是否真按请求的单声道 48 kHz 到达、`excludesCurrentProcessAudio` 是否确实滤掉自身、以及停止时 delegate 的 `stream:didStopWithError:` 是否如期结束枚举。

#### 12.3 麦克风：选 AudioQueue 的理由

`AudioQueueNewInput` 是**纯 C API**，回调是普通函数指针，不需要运行时构造 delegate 对象——比 `AVCaptureSession` 简单得多。

更重要的一点：**向 AudioQueue 直接申请识别器要的格式（16 kHz 单声道 16 位），CoreAudio 自己完成采样率与声道转换**。这不只是少写代码——设备在会话中途改变采样率时它依然工作，而 AirPods 在听/录模式切换时正是会这样。因此麦克风路径不需要走 `PcmAudioConversion`。

传 null 作为回调 run loop，AudioQueue 会用自己的线程派发回调——线程池线程上本来就没有 run loop 可借。回调里只做三件事：拷走音频、丢进 channel、立刻把缓冲还给队列；占着缓冲会让录制无处可写。

`DisposeAsync` 先同步 `AudioQueueStop` 再 `AudioQueueDispose`，确保没有回调还在对一个即将释放的队列执行。

打开设备的任何一步失败都**如实报错**，而不是返回一个只产出静音的流——安静的房间和没启动的采集，从静音上分辨不出来。

**`IPcmAudioCapture` 与 `IPreparablePcmAudioCapture` 已注册。** `--verify-composition` 的缺失端口为 4 个，且**全部属于挂起的 OCR 与图片清除阶段**——所有不受 OCR 决策阻塞的平台端口至此全部就位。

**源目录不需要任何授权**：设备枚举、设备名、通道布局对任何进程开放，只有真正取样才受限。应用列表复用与划词选择器同一套 workspace 查询。因此源选择器在 EasyChat 向用户要任何权限之前就能填好。

**token 编码**：`macos:system-output` / `macos:application:<pid>` / `macos:microphone:<device-uid>`。与窗口目标 token 不同，这里**不加会话戳**——麦克风的 device UID 本身就是稳定的，用户在选择器里的选择应当在设备拔插后依然有效；应用源带 pid，只在该进程存活期间有意义，由采集侧在使用点校验。

**输入设备靠通道数而非名字识别**：扬声器报告 0 个输入通道，这样区分麦克风和输出设备不需要猜名字。

**虚拟声卡靠名字启发式识别**（BlackHole / Loopback / Soundflower 等），因为 CoreAudio 不标记设备是否虚拟——在系统看来 loopback 驱动就是一个普通输入设备。这一条已在文档里标明是启发式。

**转换内核为什么是纯托管代码**：采样率转换、声道下混、帧对齐是音频链路里**悄悄出错**的地方——半速播放、丢掉一个声道、帧逐渐错位，这些听起来仍然像音频，只会表现为识别质量下降。放在托管侧意味着不接麦克风也能逐样本断言：立体声取平均而非求和（否则每个立体声源都会响一倍）、48 kHz → 16 kHz 后常量信号仍是常量（朴素抽取会漂移）、超幅样本**限幅而非回绕**（回绕会把一段大声变成爆音）、混音同样限幅、较短的源在未覆盖的帧上贡献静音。

**真机测试抓到的一个真实 bug**：`AudioBufferList` 在 `mNumberBuffers` 之后有 4 字节填充（`AudioBuffer` 以指针结尾，需 8 字节对齐），我最初按紧邻排布解析，导致这台机器上**一个输入设备都枚举不到**。这正是「对着真实 CoreAudio 跑」才会暴露、纯 mock 永远发现不了的那类错误。


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

**状态：已完成（2026-09-12），播放链路已真机验证**

#### 实现

- [x] `MacAudioPlaybackDeviceCatalog`
- [x] `MacAudioPlaybackQueue`
- [x] `MacAudioFeedbackCuePlayer`

#### 播放要求

- [x] 默认播放设备。
- [x] 虚拟音频设备。
- [x] 顺序播放队列。
- [x] Stop 立即停止当前音频并清空队列。
- [x] 支持现有 TTS 返回的媒体格式（按 media type 选扩展名，交给 AVFoundation 解码）。
- [x] 设备切换后重新初始化（每个片段都重新解析目标设备并新建播放器）。
- [x] 设备失效时给出可诊断错误。
- [x] UI 提示音不能路由到虚拟设备。

**为什么用 `AVPlayer` 而不是 `AVAudioPlayer`**：`AVPlayer` 是 macOS 上唯一简单且暴露 `audioOutputDeviceUniqueID` 的播放器，而「把语音送到指定设备」正是同声传译这条链路的全部意义。播放本身不需要任何隐私授权。代价是 `AVPlayer` 从 URL 播放，所以每个片段先落到临时文件。

**播放结束判定用轮询 `rate` 而非 KVO**：用 KVO 意味着要在运行时构造一个 Objective-C 观察者类来接回调，而片段只有几秒。判定时必须**先看到 rate 升起来**才能把 rate 归零读作「播完」——因为从 `play()` 到真正开始播之间 rate 也是 0。

**提示音自己合成而不是借用系统音效**：Windows 侧用 `Console.Beep` 发三个不同音高，而 `Console.Beep` 在非 Windows 上不存在。与其换成某个系统提示音，不如合成同样的三个音高与时长，让提示音在两个平台上**听起来完全一致**。提示音一律走默认输出——路由进 loopback 设备会被对话另一端听见，这正是契约禁止的。

#### 虚拟音频设备识别

当前 Windows 只识别 VB-Audio 名称。macOS 需要识别已有等价设备，例如 BlackHole、Loopback 或其他具有输入/输出配对能力的虚拟设备。

这不是增加新功能，而是实现现有 `IsVirtualCable` 契约。不得在 Presentation 中硬编码具体驱动名称；设备判定属于 Mac Infrastructure。

**已实现为 `MacVirtualAudioDevices.IsLoopback`（Infrastructure 内），按名字匹配 BlackHole / Loopback / Soundflower / VB-Cable 等。**这是启发式且明确标注为启发式：CoreAudio **不标记**设备是否虚拟——在系统看来 loopback 驱动就是一个同时有输入和输出通道的普通设备。这与 Windows 侧匹配 VB-Audio 名称是同一做法，只是换成 macOS 的等价物。

请求走 loopback 但系统未安装任何 loopback 驱动时，**降级到默认输出并记警告**而不是失败：用户至少还能听到译文。

#### 真机验证（2026-09-12）

- 输出设备枚举：真实设备、身份不重复、至多一个标记为默认，全部无需任何授权。
- **整条播放链路用一段静音片段真机跑通**：临时文件 → `AVPlayer` 创建 → 解码 → 播放 → 结束判定 → 队列排空。用静音是为了不在开发者机器上发出声音，同时完整覆盖这条路径。
- `Stop` 排空队列：排入 5 段各一秒的片段后 `Stop`，整体耗时远低于 5 秒，证明它不是「让积压播完」。
- 提示音：三个 cue 音频互不相同，且全部走默认输出。

**测试暴露的一个真实缺陷**：`DisposeAsync` 原本不是幂等的，第二次调用会在 `CancellationTokenSource` 上抛 `ObjectDisposedException`。容器持有的队列同时也可能被使用它的工作流停止并释放，所以幂等是必须的，已修复。

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

**状态：任务 1、2 已完成；3～7 为真机验证项，需先打出 `.app`（2026-09-12）**

这一阶段只允许修改表现，不允许搬入平台业务逻辑。

#### 任务

1. [x] 快捷键显示：`Meta` → `⌘`、`Alt` → `⌥`、`Control` → `⌃`、`Shift` → `⇧`。
2. [x] 快捷键录制不再保存 `Win +`，兼容读取旧设置（阶段 2 已完成）；物理键名见下方说明。
3. [ ] 验证 PingFang SC、Hiragino Sans、Inter 和 CJK 字体回退。
4. [ ] 适配主窗口标题栏拖动、全屏、关闭/隐藏、菜单栏/Dock 和小屏幕缩放。
5. [ ] 验证悬浮窗不抢焦点、跨 Space、全屏播放器、多显示器、点击穿透和屏幕捕获排除。
6. [ ] 验证 OCR/ASR 模型导入、TTS 输出、应用数据目录迁移和 macOS 文件选择器行为。
7. [ ] 沿用现有 Toast 和错误资源，补充必要的中英文 macOS 权限说明，不新增独立功能页面。

**任务 3～7 全部是「验证」性质**，需要一个能跑起来的 `.app` 才能判断，因此与 5.3 一样顺延到阶段 15 打包之后。现在把它们勾上等于说谎。

#### 快捷键显示的落地方式

约束是 Presentation **不能按操作系统分支**（架构测试强制），所以约定由 Host 提供而不是在 Presentation 里判断。实现：

- `EasyChat.Presentation.Shared/Controls/KeyGlyphs.cs`：`KeyGlyphConvention` 记录四个修饰键怎么写，`KeyGlyphs.Format` 是唯一决定「存储的键名 → 屏幕上的样子」的地方。
- `KeyGlyphConvention.Names`（`Ctrl`/`Alt`/`Shift`/`Win`，Windows 保持原样）与 `KeyGlyphConvention.MacSymbols`（`⌃`/`⌥`/`⇧`/`⌘`）。
- macOS `Program.Main` 在启动时设置 `KeyGlyphs.Convention = KeyGlyphConvention.MacSymbols`。默认值是 `Names`，所以 Windows 行为零变化。

这是进程级的一次性设置，性质与当前区域性（`CurrentUICulture`）相同：Host 选一次，界面里所有快捷键跟着走。

顺带把 `KeySequenceDisplay` 里原本内联的键名映射提取成静态方法——控件本身要 Avalonia 运行时才能实例化，映射抽出来之后就能直接单元测试了。

旧设置里的 `Win`、`Windows` 都按 Meta 读取，所以在 Mac 上显示为 `⌘` 而不是字面的「Win」。

#### 一个已知的不一致：键名与物理键位

任务 2 提到「使用稳定物理键名」。目前录制取的是 Avalonia 的 `e.Key`，它经过键盘布局映射；而 `MacGlobalHotkeys` 按虚拟键**码**（物理键位）注册。在非 US 布局上这两者可能对不上。

**这一点 Windows 侧同样存在**（`WindowsKeyCombination` 用的 VK 码也受布局影响），因此不是 macOS 引入的回归，而是两个平台共有的既有行为。真要修需要改成记录 `PhysicalKey` 并在两个平台的键表上同步，属于跨平台改动，不在本阶段范围内——此处如实记录，留待需要时单独处理。

#### 阶段门槛

Presentation 不包含 Apple Framework 类型或 `OperatingSystem.IsMacOS()`；交互符合 macOS 习惯。

**门槛状态：前半已达成**——架构测试持续验证 Presentation 无 `OperatingSystem.IsMacOS`，本次改动也没有在任何共享层引入操作系统判断。后半「交互符合 macOS 习惯」属于任务 3～7 的真机验收。

### 阶段 15：打包、签名、公证与更新

预计：4～6 人日。

**状态：打包与本地签名已完成并真机验证；Developer ID 签名与公证待证书（2026-09-12）**

构建脚本：`build/macos/make-app.sh`。

#### 15.1 `.app` 结构

实际产出（`codesign --verify --deep --strict` 通过，可启动）：

```text
EasyChat.app/
  Contents/
    Info.plist
    _CodeSignature/
    MacOS/
      EasyChat              # 单文件，含运行时与全部托管程序集
      *.dylib               # 5 个原生库
    Resources/
      Assets/
      Models/
      EasyChat.icns         # 若 build/macos/EasyChat.icns 存在
```

与计划的差异：**没有 `Frameworks/` 目录**。原生库留在 `MacOS/` 里——它们本来就是代码，codesign 接受，移到 `Frameworks/` 反而要改 rpath 才能让 .NET 找到，换不来任何好处。

#### 15.1.1 为什么必须用单文件发布（实测结论）

这一条是被 codesign 逼出来的，不是偏好。

**codesign 把主可执行文件旁边的每个文件都当作嵌套代码。** 普通 `dotnet publish` 会在那里留下 `deps.json`、`runtimeconfig.json` 和二百多个托管程序集。实测顺序：

1. 只签 `.dylib` → 签名 bundle 时失败：`In subcomponent: System.Runtime.Intrinsics.dll`。
2. 连托管 `.dll` 一起签（codesign 确实能签 PE 文件）→ 失败点移到 `In subcomponent: EasyChat.deps.json`。
3. **JSON 文件无法携带签名**，到此走不通。

`PublishSingleFile=true` 把运行时、全部托管程序集和两个 JSON 配置折进可执行文件，旁边只剩 Mach-O 库，签名随即通过。

**刻意不开 `IncludeNativeLibrariesForSelfExtract`**：自解压出来的原生库落在 bundle 之外、不带签名，硬化运行时会拒绝加载它们，除非再去关掉库验证。保留为真实文件意味着它们和其他东西一起被签名。

#### 15.1.2 硬化运行时缺一个 entitlement 就起不来（实测结论）

bundle 签好、`codesign --verify --deep --strict` 全绿之后，进程仍然在启动时死掉：

```text
Failed to create CoreCLR, HRESULT: 0x80070008
```

原因是 .NET 运行时要即时编译托管代码，硬化运行时必须放行 JIT 和它需要的可写-可执行页。补上两个 entitlement 后启动正常：

- `com.apple.security.cs.allow-jit`
- `com.apple.security.cs.allow-unsigned-executable-memory`

**这类问题只有真正打包并运行才会暴露**——单元测试、`dotnet run`、甚至签名验证全都发现不了。

#### 15.2 发布配置

- [x] `RuntimeIdentifier=osx-arm64`。
- [x] self-contained。
- [ ] 是否启用 trimming 要通过 worker、反射、序列化测试后决定 —— **未启用**。本项目大量依赖反射（Avalonia 的 `ViewLocator` 按命名约定解析视图、设置的 JSON 序列化、DI），裁剪需要先跑完这三类验证才谈得上，现在开等于赌。
- [x] 不默认启用 Native AOT。
- [x] 修正当前 `global.json` 的无效 SDK feature-band（`10.0.0` → `10.0.100`）。
- [x] 生成符号文件，发布包排除调试符号（`-p:DebugType=none`）。
- [ ] 固定 SDK 和依赖版本 —— 保留 `rollForward: latestMajor`，见下。

**关于 `global.json`**：阶段 0 的基线记录说它「声明无效 SDK 版本 `10.0.0`，需从仓库外调用 SDK 才能还原和测试」。**这一点本次没有复现**：在仓库根目录 `dotnet --version` 正常解析到 10.0.400，整个会话的构建与测试都在仓库内进行。`10.0.0` 确实不是合法的 feature band，改成 `10.0.100` 是修正，但它不是此前描述的那个阻塞问题——如实记录，避免后来者按错误的现象排查。

`rollForward` 保持 `latestMajor` 而非锁死：锁到具体补丁号会让任何没装那一版 SDK 的贡献者无法构建，收益不抵成本。真正需要可复现构建的是 CI，应当在阶段 16 用 `actions/setup-dotnet` 固定版本，而不是在 `global.json` 里卡死所有人。

产物体积：约 174 MB（self-contained + 单文件 + ONNX Runtime）。

#### 15.3 签名顺序

从内到外：

1. 原生 dylib。
2. worker/native helper。
3. 主 executable。
4. `.app` bundle。
5. DMG/ZIP。

要求：

- [ ] Developer ID Application —— 脚本支持（`EASYCHAT_SIGN_IDENTITY`），**待证书**；未设置时退化为 ad-hoc 签名。
- [x] Hardened Runtime（`--options runtime`）。
- [ ] secure timestamp —— 仅在使用真实身份时请求；ad-hoc 签名无法取得时间戳，脚本据此切换。
- [x] entitlements。
- [x] `codesign --verify --deep --strict` —— **通过**（`valid on disk` / `satisfies its Designated Requirement`）。
- [ ] `spctl --assess` —— ad-hoc 签名必然失败，脚本对此明确提示而不是伪装成功；需 Developer ID 后验证。
- [ ] `notarytool submit` —— 需 Apple 开发者账号。
- [ ] `stapler staple` —— 同上。

签名顺序按「由内到外」实现：先逐个签 `.dylib`，再签 bundle。顺序反了的话，之后签任何内层文件都会让外层签名失效。

**未自动化公证的理由**：`notarytool` 需要 Apple 开发者账号凭据。把凭据流程写进脚本却无法运行验证，只会产生一段谁也没跑过的代码；`macOS.md` 记录步骤比伪装成已完成更有用。

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

#### 15.4 更新

**状态：客户端侧已就绪；发布 `osx` channel 待签名（2026-09-12）**

**这一节的前提此前是错的，实测后更正。** 原判断是「macOS 需要独立的更新路径」。实际上 `VelopackApplicationUpdateService` 位于**平台无关**的 Infrastructure 中，由 `AddEasyChatInfrastructure` 注册，macOS Host 经 `DesktopApplication.Run` 已经拿到它——`--verify-composition` 里从来没有 `IApplicationUpdateService` 缺失。

在 macOS 上实测 Velopack 0.0.1298：

```text
VelopackApp.Build().Run()  → 正常返回（非 Velopack 安装环境下是 no-op）
SystemOs: OSX
SystemRid: osx-arm64
resolved channel: osx
```

因此计划列出的要求里，**有三条不需要任何新代码**：

| 要求 | 状态 |
| --- | --- |
| 独立 macOS release channel | ✅ Velopack 自动解析为 `osx` |
| 只匹配 `osx-arm64` 包 | ✅ `SystemRid` 即 `osx-arm64` |
| 不让 Windows 客户端看到 macOS 包，反之亦然 | ✅ 由 channel 分离天然保证（`releases.osx.json` / `releases.win.json`） |
| 重启 bundle 而非内部可执行文件 | Velopack 在 macOS 上按 `.app` 替换与重启处理 |
| 整包替换 `.app` | 同上 |
| 更新时退出全部 worker | ⚠️ 见下 |
| 更新后签名保持有效 | 打包与签名问题，随 `osx` channel 一并验证 |
| 保持同一 bundle identifier 与签名主体 | 打包问题，见下 |

**本次唯一的代码改动**：macOS `Program.cs` 现在和 Windows 一样调用 `VelopackApp.Build().Run()`。这个调用必须在任何文件系统操作之前发生——它负责更新后的首次启动与随之而来的重启。已实测在非 Velopack 打包的 bundle 中它是 no-op，加进去不会影响现在的 `.app`。

**「更新时退出全部 worker」**：截图 worker 在每次应答后**立即退役**（阶段 9.3 的设计），所以正常情况下更新时没有 worker 存活。但 `ApplyUpdatesAndRestart` 仍可能与一次正在进行的截图相撞，这一点在真机验收时需要确认。

**「保持同一 bundle identifier 与签名主体」尤其关键**：macOS 的隐私授权绑定在签名身份上，换了签名主体等于让用户重新授权辅助功能、屏幕录制和麦克风。这是打包环节必须守住的，不是代码能保证的。

**仍然阻塞的**：CI 里发布 `osx` channel 需要先有 Developer ID 证书与公证（见 15.3），因为未签名的 `.app` 即使能下载也过不了 Gatekeeper。在此之前 `manager.IsInstalled` 为 false，更新检查会如实回答「没有可用更新」而不是报错。

#### 阶段门槛

从 GitHub 下载的干净制品可以直接打开，通过 Gatekeeper、签名、公证和应用内更新验证。

**门槛状态：未达成。** 本地已能产出可启动、通过 `codesign --verify --deep --strict` 的 `.app`，但 Gatekeeper、公证和应用内更新三项都还没有。需要 Developer ID 证书和 Apple 开发者账号才能继续。

#### 本阶段解锁了什么

`.app` 能跑起来之后，此前因「没有可运行的 `.app`」而顺延的验收项现在具备条件了：阶段 5.3（主窗口拖动、Dock 恢复、交通灯冲突、全屏切换）、阶段 9.2（真实截图，仍需屏幕录制授权）、阶段 14 任务 3～7（字体回退、悬浮窗跨 Space、文件选择器等）。这些都需要人在真机上观察，不是自动化测试能替代的。

### 阶段 16：CI、测试矩阵与发布门禁

预计：5～8 人日。

**状态：CI 已重构为平台矩阵并启用门禁（2026-09-12）；签名公证作业待证书**

#### CI 拆分

实际落地为三个作业（`.github/workflows/build.yml`）：

```text
tests (matrix: windows-latest, macos-latest)   共享测试 + 各自的平台原生测试
windows-package                                 依赖 tests
macos-package                                   依赖 tests，产出 ad-hoc 签名的 .app
```

比计划少一个 `macos-sign-notarize`：没有 Developer ID 证书和 Apple 账号，那个作业写出来也跑不了。

#### 必须调整

1. [x] 删除测试步骤的 `continue-on-error: true` —— **门禁现在是真的**。
2. [x] Windows 原生测试只在 Windows 运行。
3. [x] Mac 原生测试跟随矩阵（见下方关于 runner 版本的限制）。
4. [x] 共享测试同时在 Windows 和 macOS 运行。
5. [x] Composition test 分平台。
6. [x] Architecture test 对两套 Host/Infrastructure 使用相同规则 —— 并且**在两个平台上都跑**：它读项目文件比较路径，此前只在 Windows 跑，放过了一个反斜杠分隔的 `ProjectReference`，在 macOS 检出时才炸（阶段 0 基线里的那个失败）。

**Composition test 怎么分**：`CompositionRegistrationTests` 组装的是 Windows 模块，其适配器在别处会拒绝注册，因此在非 Windows 上报 Inconclusive。macOS 的组装改由**运行 `.app` 并传 `--verify-composition`** 来验证——那条路同时覆盖真实的 bundle 身份和原生库解析，测试宿主做不到这两点。

#### 两个必须说明的限制

**1. GitHub 的 `macos-latest` 目前是 macOS 15，不是 26。** 本项目所有 macOS 原生测试都以 `OperatingSystem.IsMacOSVersionAtLeast(26)` 为前提，在 CI 上会**全部报 Inconclusive**。也就是说 macOS 作业当前把守的是跨平台逻辑和纯托管部分（token 编解码、快捷键映射、坐标换算、音频混音、BGRA 帧转换、权限状态映射等），**原生桥的真机验证仍然只发生在开发者机器上**。runner 镜像升到 macOS 26 之后，这批测试会自动开始真正运行，无需改代码。

**2. 已知的偶发失败会让 macOS 作业间歇性变红。** `SubtitleSessionCoordinatorTests.FailedStructuredRetryRestoresAnExactSourceTranslationSnapshot` 在本次会话的全量运行中失败过两次，单独重跑和后续全量重跑均通过。它属于 Application 层的字幕时序用例，与 macOS 适配无关，是既有问题（CLAUDE.md 已记录）。**去掉 `continue-on-error` 意味着它现在会真的挡住构建**——这是启用门禁的代价，需要单独 triage，不应靠继续容忍失败来掩盖。

#### 为什么 release.yml 没有加 macOS 作业

没有 Developer ID 证书时只能产出 ad-hoc 签名的 `.app`。这样的包被用户下载后会被 Gatekeeper 直接拒绝，表现为「下载的东西是坏的」——比不提供下载更糟。`build.yml` 里的 `macos-package` 产物标注为**仅供在下载它的机器上测试**，不是分发件。拿到证书后，再在 release 流程中补 `macos-sign-notarize`。

#### 本地验证（2026-09-12）

按 macOS 作业的确切步骤在 Release 配置下跑通：

| 套件 | 结果 |
| --- | --- |
| Domain | 1 通过 |
| Application | 213 通过 |
| Infrastructure | 92 通过 |
| Presentation | 224 通过 |
| Architecture | 11 通过 |
| Acceptance | 11 通过 / 5 跳过 |
| Infrastructure.MacOS | 143 通过 / 1 跳过 |
| **合计** | **695 通过 / 6 跳过 / 0 失败** |

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

**门槛状态：测试门禁已达成，签名门禁未达成。** 测试失败现在会阻断构建；签名和公证作业需要证书才能建立。

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

## 5.1 当前进度总览（2026-09-12）

| 阶段 | 状态 |
| --- | --- |
| 0 基线 | 已完成 |
| 1 架构文档与测试 | 已完成 |
| 2 清除 Windows 语义泄漏 | 已完成 |
| 3 平台项目与原生桥 | 已完成 |
| 4 能力与权限 | 已完成 |
| 5 Host、生命周期、窗口桥 | 代码完成；5.3 待真机验收 |
| 6 剪贴板、枚举、焦点、写回 | 已完成 |
| 7 快捷键、键盘状态、鼠标监听 | 已完成 |
| 8 划词工具栏链路 | 接线完成；验收待真机 |
| 9 截图与坐标 | 9.1/9.3 已完成；9.2 采集未经真机验证 |
| 10 OCR | **阻塞**：runtime 实测不可用，路线待定 |
| 11 图片文字清除 | **挂起**：与阶段 10 同因 |
| 12 音频采集 | 12.1/12.4 内核完成；12.2/12.3 采集未实现 |
| 13 播放、TTS、同传 | 已完成 |
| 14 Presentation 适配 | 任务 1～2 完成；3～7 待真机 |
| 15 打包签名公证更新 | 打包与本地签名完成；公证待证书；15.4 客户端已就绪，发布 channel 待签名 |
| 16 CI 与门禁 | 测试门禁已启用；签名门禁待证书 |

### 阻塞项与所需决策

按「挡住了什么」排序：

1. **OCR 路线（阻塞阶段 10、11）** —— 计划假设的 `Sdcb.OpenVINO.runtime.osx.12.6-arm64` 不含推理插件，OpenCvSharp 原生库亦缺失（实测见阶段 10）。三条路线各有代价，需要决策后才能动工。
2. **屏幕录制授权（挡住 9.2 与 12.2 的验证）** —— 代码已写，但采集路径一次都没真机跑过。
3. **Developer ID 证书与 Apple 账号（挡住公证、Gatekeeper、发布门禁）**。
4. **真机验收（挡住 5.3、8、9.2、14.3～7）** —— 需要人在装好 `.app` 的机器上操作，自动化替代不了。
5. ~~12.2/12.3 音频采集与 15.4 更新路径~~ —— **已完成**（2026-09-12）。至此不依赖外部条件的适配器工作全部结束。

### 本次会话遗留的技术债

- **偶发失败的用例是 `SubtitleSessionCoordinatorTests.IdenticalFinalDoesNotRestartTtlAfterQuietTranslationCompleted`**（5 次连跑中失败 1 次，已用 trx 抓到具体名字）。

  **更正**：`CLAUDE.md` 记录的是同一测试类中的另一个用例 `FailedStructuredRetryRestoresAnExactSourceTranslationSnapshot`「在 macOS 上可复现超时」。本次会话中**那个用例每次都通过**，实际偶发失败的是上面这个。此前几轮我只说「与已记录的特征吻合」而未核实名字，是推断不是事实——现已核实并更正。

  性质：该测试用虚拟时钟推进协调器的定时器（`harness.Time.Advance`），同时用 `WaitForAsync` / `DrainAsync` 轮询真实的异步事件泵。虚拟时间与真实并发混用时，时钟推进可能先于事件处理完全落定，TTL 记账便会落在不同分支。属 Application 层字幕时序问题，与 macOS 适配无关，去掉 `continue-on-error` 后会真的挡住 CI，需要单独 triage。
- 快捷键录制取 `e.Key`（布局映射）而热键按物理键码注册，非 US 布局可能不一致；Windows 侧同样存在，属跨平台既有行为。
- `ScreenshotWorkerProtocol` 在两个 Host 各存一份，因为 Host 之间不允许互相引用。

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
