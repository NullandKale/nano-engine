using ILGPU.Algorithms.Random;
using NullEngine.Rendering.DataStructures;
using NullEngine.Rendering.Implementation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Utils
{
    public static class PerformanceTests
    {
        public static void TestRandomUnitVectors()
        {
            int total = 0;
            int passed = 0;

            Random rng = new Random();
            byte[] bytes = new byte[16];
            rng.NextBytes(bytes);
            ulong a = BitConverter.ToUInt64(bytes, 0);
            ulong b = BitConverter.ToUInt64(bytes, 8);
            XorShift128Plus xorShift128Plus = new XorShift128Plus(a, b);

            Stopwatch timer = new Stopwatch();
            timer.Start();


            for (int i = 0; i < 10000000; i++)
            {
                if (RandomUnitVectorInaccurate(ref xorShift128Plus))
                {
                    passed++;
                }
                total++;
            }

            timer.Stop();

            Trace.WriteLine((int)(((float)passed / (float)total) * 100.0) + " % " + timer.ElapsedMilliseconds + " MS");

            timer.Restart();

            total = 0;
            passed = 0;

            for (int i = 0; i < 10000000; i++)
            {
                GPUKernels.RandomUnitVectorSlow(ref xorShift128Plus);
                passed++;
                total++;
            }

            timer.Stop();

            Trace.WriteLine((int)(((float)passed / (float)total) * 100.0) + " % " + timer.ElapsedMilliseconds + " MS");
        }

        public static bool RandomUnitVectorInaccurate(ref XorShift128Plus rng)
        {
            Vec3 random = default;

            for (int i = 0; i < 4; i++)
            {
                random = new Vec3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat());
                if (random.lengthSquared() <= 1)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
