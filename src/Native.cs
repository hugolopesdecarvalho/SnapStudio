using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SnapStudio
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public const int WM_HOTKEY = 0x0312;
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, int rop);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

        /// <summary>Limites visíveis da janela (sem a borda invisível do Windows 10/11).</summary>
        public static Rectangle GetWindowBounds(IntPtr h)
        {
            RECT r;
            try
            {
                if (DwmGetWindowAttribute(h, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out r, Marshal.SizeOf(typeof(RECT))) == 0)
                    return r.ToRectangle();
            }
            catch { }
            if (GetWindowRect(h, out r)) return r.ToRectangle();
            return Rectangle.Empty;
        }

        static bool IsCloaked(IntPtr h)
        {
            int c;
            try { return DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out c, 4) == 0 && c != 0; }
            catch { return false; }
        }

        /// <summary>Janelas visíveis de topo, por ordem Z (a primeira é a da frente).</summary>
        public static List<Rectangle> GetVisibleWindows()
        {
            var list = new List<Rectangle>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) return true;
                int ex = GetWindowLong(h, -20 /*GWL_EXSTYLE*/);
                if ((ex & 0x20 /*WS_EX_TRANSPARENT*/) != 0) return true;
                if (GetWindowTextLength(h) == 0) return true;
                Rectangle r = GetWindowBounds(h);
                if (r.Width < 20 || r.Height < 20) return true;
                list.Add(r);
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}
