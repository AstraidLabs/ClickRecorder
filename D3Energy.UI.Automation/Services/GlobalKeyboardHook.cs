using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace D3Energy.UI.Automation.Services
{
    public sealed class KeyboardHookEventArgs : EventArgs
    {
        public string? Text { get; init; }
        public bool IsBackspace { get; init; }
        public bool IsEnter { get; init; }
        public DateTime Timestamp { get; init; }
        public uint ProcessId { get; init; }
    }

    public sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const uint VK_BACK = 0x08;
        private const uint VK_RETURN = 0x0D;
        private const uint VK_TAB = 0x09;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
                                              [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
                                              int cchBuff, uint wFlags, IntPtr dwhkl);

        public event EventHandler<KeyboardHookEventArgs>? KeyPressed;

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;
        private bool _active;

        public void Start()
        {
            if (_active) return;
            _proc = HookCallback;
            using var proc = Process.GetCurrentProcess();
            using var module = proc.MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
            _active = true;
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
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    var foreground = GetForegroundWindow();
                    _ = GetWindowThreadProcessId(foreground, out uint processId);
                    var args = new KeyboardHookEventArgs
                    {
                        Timestamp = DateTime.Now,
                        ProcessId = processId,
                        IsBackspace = data.vkCode == VK_BACK,
                        IsEnter = data.vkCode == VK_RETURN,
                        Text = ResolveKeyText(data)
                    };

                    if (args.Text is not null || args.IsBackspace || args.IsEnter)
                    {
                        KeyPressed?.Invoke(this, args);
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static string? ResolveKeyText(KBDLLHOOKSTRUCT keyData)
        {
            if (keyData.vkCode == VK_RETURN || keyData.vkCode == VK_BACK)
            {
                return null;
            }

            if (keyData.vkCode == VK_TAB)
            {
                return "\t";
            }

            var keyState = new byte[256];
            if (!GetKeyboardState(keyState))
            {
                return null;
            }

            var buffer = new StringBuilder(4);
            var layout = GetKeyboardLayout(0);
            int count = ToUnicodeEx(keyData.vkCode, keyData.scanCode, keyState, buffer, buffer.Capacity, 0, layout);
            if (count <= 0)
            {
                return null;
            }

            string text = buffer.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        public void Dispose() => Stop();
    }
}
