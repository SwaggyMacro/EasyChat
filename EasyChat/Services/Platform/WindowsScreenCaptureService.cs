using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Platform;

public class WindowsScreenCaptureService : IScreenCaptureService
{
    private const int SrcCopy = 0x00CC0020;

    public Bitmap CaptureFullScreen(Screen screen)
    {
        var screenWidth = screen.Bounds.Width;
        var screenHeight = screen.Bounds.Height;
        var x = screen.Bounds.X;
        var y = screen.Bounds.Y;

        return CaptureRegion(x, y, screenWidth, screenHeight);
    }

    public Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        var desktopWindow = Win32.GetDesktopWindow();
        var desktopDc = Win32.GetWindowDC(desktopWindow);
        var compatibleDc = Win32.CreateCompatibleDC(desktopDc);
        var hBitmap = Win32.CreateCompatibleBitmap(desktopDc, width, height);
        var oldBitmap = Win32.SelectObject(compatibleDc, hBitmap);

        try
        {
            // Perform the bit-block transfer of the color data
            if (!Win32.BitBlt(compatibleDc, 0, 0, width, height, desktopDc, x, y, SrcCopy))
            {
                throw new InvalidOperationException("BitBlt failed");
            }

            // Create a WriteableBitmap to hold the pixel data
            // We use Bgra8888 as it matches the standard Windows 32-bit DIB format
            var writeableBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (var buffer = writeableBitmap.Lock())
            {
                var bmi = new Win32.BitmapInfo
                {
                    biSize = Marshal.SizeOf<Win32.BitmapInfo>(),
                    biWidth = width,
                    // Negative height requests a top-down bitmap, which matches Avalonia's expectation
                    biHeight = -height, 
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0 // BI_RGB
                };

                // Copy pixels directly from the GDI bitmap to the WriteableBitmap buffer
                var result = Win32.GetDIBits(
                    compatibleDc,
                    hBitmap,
                    0,
                    (uint)height,
                    buffer.Address,
                    ref bmi,
                    Win32.DIB_RGB_COLORS);

                if (result == 0)
                {
                    throw new InvalidOperationException("GetDIBits failed");
                }
            }

            return writeableBitmap;
        }
        finally
        {
            // Cleanup GDI resources
            Win32.SelectObject(compatibleDc, oldBitmap);
            Win32.DeleteObject(hBitmap);
            Win32.DeleteDC(compatibleDc);
            Win32.ReleaseDC(desktopWindow, desktopDc);
        }
    }

    internal static class Win32
    {
        public const int DIB_RGB_COLORS = 0;

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
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint uStartScan, uint cScanLines, IntPtr lpvBits,
            ref BitmapInfo lpbi, uint uUsage);

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
