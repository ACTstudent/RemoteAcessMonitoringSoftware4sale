using System;
using System.Runtime.InteropServices;

namespace Client
{
    public static class InputSimulator
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        public static void ProcessRemoteInput(string eventType, int x, int y, int keyCode, bool isShift)
        {
            switch (eventType)
            {
                case "mousemove":
                    SetCursorPos(x, y);
                    break;

                case "mousedown":
                    SetCursorPos(x, y);
                    if (keyCode == 0) mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    else if (keyCode == 2) mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "mouseup":
                    SetCursorPos(x, y);
                    if (keyCode == 0) mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    else if (keyCode == 2) mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "scroll":
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)(keyCode * 120), UIntPtr.Zero);
                    break;

                case "keydown":
                    if (isShift) keybd_event(0x10, 0, 0, UIntPtr.Zero); // Shift down
                    keybd_event((byte)keyCode, 0, 0, UIntPtr.Zero);
                    keybd_event((byte)keyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    if (isShift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Shift up
                    break;
            }
        }
    }
}
