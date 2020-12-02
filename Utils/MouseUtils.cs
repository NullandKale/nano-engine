using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Utils
{
    public class MouseUtils
    {
        public static void SetMousePos(int x, int y)
        {
            if(Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                SetCursorPos(x, y);
            }
            else
            {
                ("xdotool mousemove" + x + " " + y).Bash();
            }
        }

        [DllImport("User32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
    }
}
