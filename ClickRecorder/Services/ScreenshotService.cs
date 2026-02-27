using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace ClickRecorder.Services
{
    public static class ScreenshotService
    {
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int cx, int cy);
        [DllImport("gdi32.dll")]  private static extern IntPtr SelectObject(IntPtr hDC, IntPtr h);
        [DllImport("gdi32.dll")]  private static extern bool   BitBlt(IntPtr hDestDC, int xDest, int yDest, int w, int h, IntPtr hSrcDC, int xSrc, int ySrc, uint rop);
        [DllImport("gdi32.dll")]  private static extern bool   DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")]  private static extern bool   DeleteObject(IntPtr ho);

        private const uint SRCCOPY = 0x00CC0020;

        public static string? Capture(int stepId, int repeatIndex)
        {
            try
            {
                int w = (int)SystemParameters.PrimaryScreenWidth;
                int h = (int)SystemParameters.PrimaryScreenHeight;

                IntPtr desktop = GetDesktopWindow();
                IntPtr hdcSrc  = GetWindowDC(desktop);
                IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
                IntPtr hBmp    = CreateCompatibleBitmap(hdcSrc, w, h);
                IntPtr hOld    = SelectObject(hdcDest, hBmp);

                BitBlt(hdcDest, 0, 0, w, h, hdcSrc, 0, 0, SRCCOPY);

                SelectObject(hdcDest, hOld);
                DeleteDC(hdcDest);
                ReleaseDC(desktop, hdcSrc);

                var bmp = Image.FromHbitmap(hBmp);
                DeleteObject(hBmp);

                string dir  = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "ClickRecorder_Screenshots");
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir,
                    $"step{stepId:D3}_r{repeatIndex}_{DateTime.Now:HHmmss_fff}.png");
                bmp.Save(path, ImageFormat.Png);
                bmp.Dispose();
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
