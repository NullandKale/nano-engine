using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Utils
{
    public static class Log
    {
        public static void d(string toLog)
        {
            if(Environment.OSVersion.Platform == PlatformID.Unix)
            {
                Console.WriteLine(toLog);
            }
            else
            {
                Trace.WriteLine(toLog);
            }
        }
    }
}
