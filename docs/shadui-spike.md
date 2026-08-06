# L4 — ShadUI Spike

**Decision: stay on SukiUI + Ec design system for now.**  
Do **not** add the `ShadUI` package to production projects. Half-mixing skins is forbidden.

## Goal

Evaluate whether EasyChat can migrate window chrome / dialogs / toasts from SukiUI to [ShadUI](https://www.nuget.org/packages/ShadUI) without rewriting every feature call site.

## Compatibility snapshot (2026-08)

| Item | EasyChat | ShadUI 0.2.4 |
|------|----------|--------------|
| Avalonia | 12.1.0 | 12.1.0 |
| TFM | net10.0 | net10.0 + net9.0 |
| Theme host | `SukiTheme` + `SukiWindow` | `ShadTheme` + `ShadUI.Controls.Window` |
| Dialogs | `ISukiDialogManager` fluent | `DialogManager` fluent (`CreateDialog`, buttons, custom context) |
| Toasts | `ISukiToastManager` fluent | `ToastManager` / `ToastBuilder` (`ShowInfo/Success/Warning/Error`) |
| Sidebar | Suki menu + Ec nav badges | Composable sidebar (different model) |
| Maturity | Suki 7.x, already shipping | 0.2.x, still early |

API shapes are **similar enough** that a thin host abstraction can map both. The hard part is **shell chrome** (`SukiWindow`, glass, color themes, tray close flow), not simple toasts.

## What landed in this spike

Vendor-neutral hosts under `Presentation/Foundation/UiHost`:

| Type | Role |
|------|------|
| `IUiDialogHost` | Message confirmations + content dialogs |
| `IUiDialogSession` | Dismiss handle for content VMs |
| `IUiToastHost` | Info/error toasts, sticky progress, action buttons |
| `SukiUiDialogHost` / `SukiUiToastHost` | Production adapters |

Feature code (settings, shortcuts, speech, capture, shell close, update toast) now calls `IUi*` — not Suki builders.

**Still Suki-only by design:**

- `MainWindow` is a `SukiWindow`
- `MainWindow.axaml` binds `SukiDialogHost` / `SukiToastHost` to `ISukiDialogManager` / `ISukiToastManager`
- Color themes via `SukiTheme` / `SukiColorTheme`
- Ec tokens still target Suki primary / glass surfaces

## Migration cost if we ever switch

1. Implement `ShadUiDialogHost` + `ShadUiToastHost` against ShadUI managers (API map is 1:1 for L4 surface).
2. Replace `SukiWindow` shell with `ShadUI.Controls.Window` (or host Shad dialog/toast inside a hybrid — **rejected**; that is half-mixing).
3. Re-home theme cycle / custom color themes onto Shad theme APIs; Ec tokens need a Shad primary binding.
4. Re-skin every float window that currently uses `EcFloatShell` against Shad surfaces (or keep Ec floats and only swap main shell — still a product decision).
5. Manual smoke: close-to-tray dialog, nested TTS dialogs, update progress toast, settings search, all confirms.

**Estimate:** multi-day full chrome cutover, not a package drop-in. Spike value is the seam, not the swap.

## Rules going forward

1. New feature code uses `IUiDialogHost` / `IUiToastHost` only.
2. Do not reference `ISukiDialog` / `ISukiToastManager` outside:
   - `Foundation/UiHost/Suki*`
   - `MainWindow` host chrome bindings
   - Theme services that are inherently Suki (`SukiTheme`)
3. Do not install `ShadUI` next to Suki in the same running app.
4. Revisit only if Suki blocks Avalonia upgrades or Ec cannot deliver the target look.

## Status

| Item | State |
|------|-------|
| L4 Dialog/Toast abstraction | Done |
| Call-site migration to `IUi*` | Done |
| ShadUI package in product | **No** |
| Full ShadUI migration | Deferred |
