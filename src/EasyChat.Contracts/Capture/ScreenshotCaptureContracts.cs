using EasyChat.Contracts.Platform;

namespace EasyChat.Contracts.Capture;

public enum CaptureOverlayAction
{
    Translation = 0,
    CopyOriginal = 1,
    CopyTranslated = 2,
    CopyBilingual = 3,
    CopyImageTranslated = 4,
    OcrWorkbench = 5
}

public enum CaptureToolbarMode
{
    Full = 0,
    ImageSelection = 1
}

public sealed record ScreenshotSelection(
    ImageFrame Image,
    CaptureOverlayAction Action,
    PhysicalScreenPoint CompletionPoint);
