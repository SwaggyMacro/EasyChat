using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyChat.Services.Abstractions;
using SkiaSharp;

namespace EasyChat.Services.Platform;

public class WindowsScreenCaptureService : IScreenCaptureService
{
    private const int SrcCopy = 0x00CC0020;

    public Bitmap CaptureFullScreen(Screen screen)
    {
        // P/Invoke Logic similar to old version
        // Using pixel scaling factor from screen if available, though Win32 usually takes raw pixels

        var screenWidth = screen.Bounds.Width;
        var screenHeight = screen.Bounds.Height;

        // Handling DPI scaling might be needed here depending on how Avalonia reports Bounds vs WorkingArea
        // But assuming Bounds are physical pixels for BitBlt:

        var desktopWindow = Win32.GetDesktopWindow();
        var desktopDc = Win32.GetWindowDC(desktopWindow);
        var compatibleDc = Win32.CreateCompatibleDC(desktopDc);
        var hBitmap = Win32.CreateCompatibleBitmap(desktopDc, screenWidth, screenHeight);
        var oldBitmap = Win32.SelectObject(compatibleDc, hBitmap);

        // Calculate offsets based on screen position
        var x = screen.Bounds.X;
        var y = screen.Bounds.Y;

        Win32.BitBlt(compatibleDc, 0, 0, screenWidth, screenHeight, desktopDc, x, y, SrcCopy);

        // Convert HBITMAP to Avalonia Bitmap via SkiaSharp
        // Note: In strict refactor, we should probably keep this logic encapsulated or move HBitmap conversion to a util
        // For now, inlining the "Bitmap -> Skia -> Avalonia" pipeline

        var skBitmap = HBitmapToSkBitmap(hBitmap);

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);

        var avaloniaBitmap = new Bitmap(stream);

        // Cleanup
        Win32.SelectObject(compatibleDc, oldBitmap);
        Win32.DeleteObject(hBitmap);
        Win32.DeleteDC(compatibleDc);
        Win32.ReleaseDC(desktopWindow, desktopDc);

        return avaloniaBitmap;
    }

    public Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        var desktopWindow = Win32.GetDesktopWindow();
        var desktopDc = Win32.GetWindowDC(desktopWindow);
        var compatibleDc = Win32.CreateCompatibleDC(desktopDc);
        var hBitmap = Win32.CreateCompatibleBitmap(desktopDc, width, height);
        var oldBitmap = Win32.SelectObject(compatibleDc, hBitmap);

        Win32.BitBlt(compatibleDc, 0, 0, width, height, desktopDc, x, y, SrcCopy);

        var skBitmap = HBitmapToSkBitmap(hBitmap);

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);

        var avaloniaBitmap = new Bitmap(stream);

        Win32.SelectObject(compatibleDc, oldBitmap);
        Win32.DeleteObject(hBitmap);
        Win32.DeleteDC(compatibleDc);
        Win32.ReleaseDC(desktopWindow, desktopDc);

        return avaloniaBitmap;
    }

    private SKBitmap HBitmapToSkBitmap(IntPtr hBitmap)
    {
        // Minimal implementation of HBitmap to SkBitmap conversion
        Win32.GetObject(hBitmap, Marshal.SizeOf<Win32.BitmapStruct>(), out var bitmap);
        var bytesPerPixel = bitmap.bmBitsPixel / 8;
        // ... (Similar logic to old version)

        var bmi = new Win32.BitmapInfo
        {
            biSize = Marshal.SizeOf<Win32.BitmapInfo>(),
            biWidth = bitmap.bmWidth,
            biHeight = -bitmap.bmHeight,
            biPlanes = 1,
            biBitCount = bitmap.bmBitsPixel,
            biCompression = 0
        };

        var imageSize = bitmap.bmWidth * Math.Abs(bitmap.bmHeight) * bytesPerPixel;
        var pixelData = new byte[imageSize];
        var hdc = Win32.GetDC(IntPtr.Zero);
        Win32.GetDIBits(hdc, hBitmap, 0, (uint)Math.Abs(bitmap.bmHeight), pixelData, ref bmi, 0);
        Win32.ReleaseDC(IntPtr.Zero, hdc);

        var imageInfo = new SKImageInfo(bitmap.bmWidth, Math.Abs(bitmap.bmHeight), SKColorType.Bgra8888,
            SKAlphaType.Premul);
        var skBitmap = new SKBitmap(imageInfo);
        var skBitmapPtr = skBitmap.GetPixels();
        Marshal.Copy(pixelData, 0, skBitmapPtr, pixelData.Length);

        return skBitmap;
    }

    internal static class Win32
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc,
            int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BitmapStruct lpvObject);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint uStartScan, uint cScanLines, byte[] lpvBits,
            ref BitmapInfo lpbi, uint uUsage);

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapStruct
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfo
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }
    }
}