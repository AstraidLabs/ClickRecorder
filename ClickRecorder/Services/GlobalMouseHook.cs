using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClickRecorder.Services
{
    public class MouseHookEventArgs : EventArgs
    {
        public int      X         { get; init; }
        public int      Y         { get; init; }
        public HookButton Button  { get; init; }
        public DateTime Timestamp { get; init; }
    }

    public enum HookButton { Left, Right, Middle }

    public sealed class GlobalMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL   = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT  pt;
            public uint   mouseData;
            public uint   flags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc fn,
                                                       IntPtr hMod, uint threadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
                                                     IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        public event EventHandler<MouseHookEventArgs>? MouseClicked;

        private IntPtr             _hookId = IntPtr.Zero;
        private LowLevelMouseProc? _proc;
        private bool               _active;

        public void Start()
        {
            if (_active) return;
            _proc = HookCallback;
            using var proc   = Process.GetCurrentProcess();
            using var module = proc.MainModule!;
            _hookId  = SetWindowsHookEx(WH_MOUSE_LL, _proc,
                                         GetModuleHandle(module.ModuleName), 0);
            _active  = true;
        }

        public void Stop()
        {
            if (!_active) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _active = false;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
                {
                    var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    MouseClicked?.Invoke(this, new MouseHookEventArgs
                    {
                        X         = s.pt.x,
                        Y         = s.pt.y,
                        Button    = msg == WM_LBUTTONDOWN ? HookButton.Left
                                  : msg == WM_RBUTTONDOWN ? HookButton.Right
                                  : HookButton.Middle,
                        Timestamp = DateTime.Now
                    });
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose() => Stop();
    }
}
