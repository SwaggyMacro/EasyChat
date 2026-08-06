# EasyChat UI Design System (`Ec*`)

桌面翻译工具视觉层约定。**保留 SukiUI** 作为窗口/侧栏/对话框主机；产品观感由本系统的 token 与共享控件统一。

## 原则

- 暗色优先（ink/slate 画布），亮色为纸感浅灰，**不是**默认 Suki 灰
- 单强调色：跟随 `SukiPrimaryColor`（用户自定义主题仍生效）
- 产品感：大标题 + 色条、圆角 12–22、卡片阴影、分段控件、Hero 条
- 页面禁止新增硬编码色值；用 `Ec*` token 或 Suki 语义资源
- **明暗切换**：只改 `RequestedThemeVariant`；禁止 `SukiTheme.ChangeBaseTheme` / 点击路径 `ChangeColorTheme` / Toast / 同步写盘（Suki 的 ChangeBaseTheme 会二次重绑色并闪屏）

## 文件

| 路径 | 作用 |
|------|------|
| `Presentation/Themes/EcTokens.axaml` | 间距、圆角、字号、表面色、状态色、阴影 |
| `Presentation/Themes/EcControls.axaml` | 全局 Style Class |
| `Presentation.Shared/Controls/Ec*` | 可复用控件 |
| `Host/.../App.axaml` | 合并 Tokens + Styles |

`Themes/*.axaml` 必须是 **AvaloniaResource**（csproj 已配置），供 `avares://EasyChat.Presentation/Themes/...` 引用。

## Tokens

| Token | 用途 |
|-------|------|
| `EcBgCanvas` / `Elevated` / `Muted` / `Hover` | 画布 / 卡片 / 次级 / 悬停 |
| `EcBorder` / `EcBorderStrong` | 描边 |
| `EcTextPrimary` / `Secondary` / `Muted` | 字色层级 |
| `EcAccent` | 主色（绑 Suki Primary） |
| `EcAccentMuted` / `Soft` / `EcNavSelected` | 柔和底（主题字典固定色，禁止 Opacity+Dynamic 实心蓝） |
| `EcHero` / `EcAccentBar` / `EcMetric` / `EcSearchBar` | 首页与设置结构类 |
| `EcSearchInline` / `EcSearchInput` / `EcSearchTrigger` / `EcSearchClear` | 设置标题行可折叠搜索 |
| `EcGhost` / `EcIconRound` / `EcDisplay` | 次按钮、圆形图标钮、大标题 |
| `EcAddRow` | 全宽「添加」行（自有模板，防 Suki 按钮皮） |
| `EcDangerSolid` | 破坏性主按钮（确认删除） |
| `EcSuccess` / `Warning` / `Danger` | 状态 |
| `EcHandleFill` / `EcOnAccent` | 截图手柄高对比白 / 强调色上的前景 |
| `EcFloatBg` / `EcFloatBorder` / `EcShadowFloat` | 浮窗 |
| `EcShadowTooltip` | 紧凑 Tooltip 阴影（覆盖 Suki 大卡片 tip） |
| `EcOverlayScrim` | 加载遮罩 |
| `EcSpace1..6` | 4/8/12/16/24/32 |
| `EcRadiusSm/Md/Lg/Pill` | 6/8/12/999 |
| `EcFontFamily` / `EcFontFamilyMono` | 跨平台 UI / 等宽回退栈 |
| `EcHitTargetMin` | 交互控件最小边长 36 |
| `EcFieldHeight` / `EcFieldPadding` / `EcFieldPaddingWithSpinner` | 表单高 36；左缩进 12（有/无步进列） |
| `EcFieldBg` | 表单实心底（跟 Elevated，不用 Glass） |
| `EcFieldSpinnerWidth` | Combo/Numeric 右侧箭头列宽 28 |
| `EcSettingControlWidth` | 设置行右侧控件列统一宽 280（对齐字幕浮窗表单） |

## Style Classes

| Class | 用在 |
|-------|------|
| `EcPage` / `EcPageBody` | 页面滚动与边距 |
| `EcCard` | `suki:GlassCard` 统一 padding/圆角 |
| `EcLabel` / `EcHelper` / `EcMuted` / `EcTitle` | 文字层级 |
| `EcPrimary` | 主 CTA 按钮 |
| `EcGhost` | 次级描边按钮 |
| `EcDanger` | 破坏性次操作（删除等） |
| `EcMenuItem` | 全宽列表/菜单行（自绘模板，悬停铺满整行） |
| `EcStackList` | 纵向 ItemsControl：子项 ContentPresenter 强制 Stretch |
| `EcToolbarBtn` | 浮层图标按钮（36） |
| `EcToolbarBtnSm` | 紧凑浮层图标钮（28，字幕条等） |
| `EcSegmented` / `EcSegment` | 分段选择 |
| `EcPill` | 状态胶囊（或用 `EcStatusPill`） |
| `EcFloatShell` | 浮窗外壳 Border |
| `EcScrim` | 半透明遮罩 |
| `EcSettingsList` | 设置行列表容器 |
| `EcNav` | 主从导航 ListBox（选中强调底） |
| `EcColorSwatch` | 颜色圆钮 |
| `EcDialogBody` / `EcFieldLabel` | 对话框字段节奏 |
| （全局）`ComboBox` / `TextBox` / `NumericUpDown` | 统一 `EcFieldHeight` + `EcRadiusSm`；ComboBox 自绘模板去掉 Suki 外层 Padding |

## 共享控件

| 控件 | 职责 |
|------|------|
| `EcPageHeader` | 标题 + 副标题 + Actions |
| `EcSectionCard` | 带标题图标的分区卡（`Body` 为内容） |
| `EcSettingRow` | 左 Label/Helper，右 Value（Content） |
| `EcEmptyState` | 空状态 + 可选 Action |
| `EcStatusPill` | 就绪/警告/危险状态点 |
| `EcFloatChrome` | 浮窗外壳（可选；多数窗直接用 `EcFloatShell`） |

### EcSettingRow 示例

```xml
<ec:EcSettingRow Label="{x:Static lang:Resources.FontSize}"
                 Helper="可选说明">
    <NumericUpDown Value="{Binding ResultConf.FontSize}" Width="150" />
</ec:EcSettingRow>
```

## 反例

- 页面内写死 `#CC000000`、`#80000000`（占位符文本除外）
- 各页自定一套 Margin/圆角，不复用 `EcCard` / `EcSettingRow`
- 主窗用 Suki、浮窗完全另一套色板
- 半套引入 ShadUI 与 Suki 混皮（若评估迁移，先做 Spike 与 Dialog 抽象）

## 已落地能力（Tonight + L1–L3）

- **T0–T4**：Tokens / 共享控件 / 壳与首页 / 设置与热键 / 浮窗 / 语音
- **L1**：`EcSettingRow`、设置页行节奏、对话框字段样式、本设计文档
- **L2**：首页能力清单 + 三步 onboarding、设置搜索过滤
- **L3**：输入窗语向交换、结果窗复制工具条、划词条自适应宽高
- **后续 polish**：字幕工具条 Ec 化、Prompt/关闭对话框节奏
- **TTS / 截图 / 固定区域 / 图译结果窗**：对话框与工具栏统一 `EcCard` / `EcDialogBody` / `EcPrimary`
- **侧栏角标**：已去掉（不再在设置/热键图标上显示黄点）

## 长期

- **L4** ShadUI Spike：**完成** — 见 `docs/shadui-spike.md`。落地 `IUiDialogHost` / `IUiToastHost` + Suki 适配；**不**引入 ShadUI 包；决策：继续 Suki + Ec
- **L5** 跨平台字体 / blur 降级 / 无障碍：**完成**
  - `EcFontFamily` / `EcFontFamilyMono` + `EcFontFamilies.Resolve`
  - `WindowTransparencyLevels.ForPreference`（Acrylic→Blur→Transparent→None）
  - 焦点环 / 最小点击区 / AutomationProperties；首页 onboarding 持久化 `HomeOnboardingDismissed`
- **字幕预设主题**：**完成** — `SubtitleAppearancePresets`（Classic Dark / High Contrast / Soft Light / Cinema / Neon）
- **设置字段级搜索**：**完成** — `SettingsSearch` + `SettingsFieldVisibleConverter`，分区 + 行级关键词；标题行默认可折叠搜索图标，展开 pill 输入框（`IsSearchOpen`）
- **消息/删除确认框**：**完成** — 覆盖 `SukiDialog` 为 Ec 实心浮层（无玻璃/无顶标/无 55px 顶距）；`EcMessageDialog` 头+正文卡+底栏按钮（`EcGhost` / `EcDangerSolid`）
- **内容弹框骨架**：**完成** — `EcDialogHeader` / `EcDialogScroll` / `EcDialogFooter` / `EcDialogTitle`；快捷键/模型/密钥/Prompt/固定区域/主题/TTS 等表单弹框统一头身底
- **系统视觉重构 v2**：**完成** — ink/paper token、壳/首页 Hero、明暗切换去卡顿、主路径 `GlassCard`→`EcSurface`/`EcTile`、页面 `EcBgCanvas`
- **布局 / 交互重构**：**完成**
  - 设置：左导航 + 右详情等高双卡；浏览只显示一节；标题行搜索图标展开后多节匹配
  - 首页：全宽状态条 + 中列等高（工作台 | 2×2 捷径）+ 底栏版本→About；修版本误绑结果设置
  - 热键：分类分段切换 + 顶栏新增 + 空状态 CTA + 行内点击编辑
  - 语音：`EcPageHeader` + 录音主操作；Live/Overlay 分段工作区
  - Prompt：顶栏新增 + 空状态 + 行点击编辑
  - TextAssist：侧栏单入口「翻译 / 纠正」+ 页内分段工作区；顶栏按模式 Run；浮窗同模式切换
  - 设置：`ListBox.EcNav` 选中强调 + 浏览态详情标题（ActivePane）
  - About：版本徽章 + GitHub/站点主操作；第三方库分区
  - 字幕浮层：`EcFloatShell` + `EcToolbarBtnSm`；截图选区 `EcAccent`；词典骨架 `EcBgMuted`
  - TextAssist 结果浮层：去掉本地 surface 刷，统一 `EcFloatShell` / `EcBgMuted`；关闭钮 `EcToolbarBtn`
  - 细节合理性：页面 Run 不与 footer 重复（footer Run 仅 Compact 浮层）；热键/Prompt 行体编辑、删除用 `EcDanger`；页边距统一 32/28 节奏
  - 结果浮层：顶栏已有 Retry/Copy，底栏只留错误；About/侧栏 GitHub ≠ Releases；字幕条 tip 本地化
  - 布局修复：`EcPageFrame` 随窗拉伸；`EcSegment` 无 radio 圆点等宽；`EcPageHeader` 标题行/副标题分行；首页快捷入口同宽卡片；全屏延后写盘防抽搐
  - 明暗切换：只改 `RequestedThemeVariant`；不订阅 OnBaseThemeChanged 重绑色；写盘/托盘图标延后到 ApplicationIdle；主窗关闭背景动画/过渡
  - 悬浮窗配置：分区卡片；标签 Auto 不裁切；窗口外观 label-above-control
  - 结果浮窗：Copy 主按钮 + Esc/Ctrl+C；划词条 Translate 主操作；Action/Result 窗底栏复制
  - 输入翻译窗：显式发送主按钮 + 关闭；词典浮窗 Copy/Close 有标签
  - 产品页强调色统一走 `EcAccent`（页面不再直接绑 `SukiPrimaryColor`；token 层仍映射 Primary）
  - 对话框次操作统一 `EcGhost`；热键编辑 Save 为主 CTA；截图手柄 `EcHandleFill`；复制菜单 `EcMenuItem`
  - 表单字段：`ComboBox`/`TextBox`/`NumericUpDown` 统一 `EcFieldHeight=36` + `EcRadiusSm`；ComboBox 去掉 Suki 外层 Padding 与 45 高默认
  - Numeric 步进：自绘 ChevronUp/Down，去掉 Suki 碎 Path 箭头；颜色行 TextBox 与上方控件同列宽
