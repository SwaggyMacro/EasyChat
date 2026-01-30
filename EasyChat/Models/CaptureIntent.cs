namespace EasyChat.Models;

public enum CaptureIntent
{
    Translation, // Default (Confirm button)
    CopyOriginal,
    CopyTranslated,
    CopyBilingual,
    CopyImageTranslated,
    RectSelection
}
