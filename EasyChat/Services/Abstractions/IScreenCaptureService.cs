using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EasyChat.Services.Abstractions;

public interface IScreenCaptureService
{
    Bitmap CaptureFullScreen(Screen screen);
    Bitmap CaptureRegion(int x, int y, int width, int height);
}