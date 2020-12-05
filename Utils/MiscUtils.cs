using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Utils
{
    public static class MiscUtils
    {
        public static string CameraModeToString(int mode)
        {
            switch(mode)
            {
                case 0:
                    return "Combined GI + Direct Lighting";
                case 1:
                    return "Global Illumination";
                case 2:
                    return "Direct Lighting";
                default:
                    return "Unknown";
            }
        }
    }
}
