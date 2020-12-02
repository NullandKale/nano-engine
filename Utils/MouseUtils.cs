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

        [DllImport("User32.dll")]
        public static extern bool SetCursorPos(int X, int Y);
    }
}
