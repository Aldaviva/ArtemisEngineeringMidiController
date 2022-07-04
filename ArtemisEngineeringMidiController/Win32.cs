using System;
using System.Runtime.InteropServices;

namespace ArtemisEngineeringMidiController;

internal static class Win32 {

    public const uint WM_KEYDOWN     = 0x100; // https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-keydown
    public const uint WM_KEYUP       = 0x101; // https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-keyup
    public const uint WM_MOUSEMOVE   = 0x200;
    public const uint WM_LBUTTONDOWN = 0x201;
    public const uint WM_LBUTTONUP   = 0x202;
    public const uint MK_LBUTTON     = 0x1;

    // https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
    public const uint VK_1     = 0x31;
    public const uint VK_SHIFT = 0x10;

    /// <returns><c>true</c> on success, or <c>false</c> on failure</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint message, uint wParam, uint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendMessage(IntPtr hWnd, uint message, uint wParam, uint lParam);

}