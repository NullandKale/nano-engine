using ILGPU;
using ILGPU.Algorithms;
using NullEngine.Rendering.DataStructures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Rendering.Implementation
{
    // The following is an implementation of https://github.com/maZZZu/bmfr
    public static class BMFR_Kernels
    {
        public const float NOISE_AMOUNT = 1e-2f;
        public const float BLEND_ALPHA = 0.2f;
        public const float SECOND_BLEND_ALPHA = 0.1f;
        public const float TAA_BLEND_ALPHA = 0.1f;

        public const int COMPRESSED_R = 1;
        public const int CACHE_TMP_DATA = 1;
        public const int USE_HALF_PRECISION_IN_TMP_DATA = 1;
        public const int ADD_REQD_WG_SIZE = 1;
        public const int LOCAL_WIDTH = 8;
        public const int LOCAL_HEIGHT = 8;

        public const int BLOCK_EDGE_LENGTH = 32;
        public const int BLOCK_PIXELS = BLOCK_EDGE_LENGTH * BLOCK_EDGE_LENGTH;

        public const int LOCAL_SIZE = 256;

        private static float clamp(float val, float min, float max)
        {
            return XMath.Max(XMath.Min(val, max), min);
        }

        private static void ReduceForSum(ArrayView<float> sum_vec, ref float val, int startIndex) // Dont know what startIndex is for
        {
            int globalIndex = Grid.GlobalIndex.X;
            float possibleVal = WarpExtensions.Reduce<float, ILGPU.Algorithms.ScanReduceOperations.AddFloat>(1);

            if(globalIndex == 0)
            {
                val = possibleVal;
            }
        }

        private static void ReduceForMin(ArrayView<float> min_vec, ref float val, int startIndex) // Dont know what startIndex is for
        {
            int globalIndex = Grid.GlobalIndex.X;
            float possibleVal = WarpExtensions.Reduce<float, ILGPU.Algorithms.ScanReduceOperations.MinFloat>(1);

            if (globalIndex == 0)
            {
                val = possibleVal;
            }
        }

        private static void ReduceForMax(ArrayView<float> max_vec, ref float val, int startIndex) // Dont know what startIndex is for
        {
            int globalIndex = Grid.GlobalIndex.X;
            float possibleVal = WarpExtensions.Reduce<float, ILGPU.Algorithms.ScanReduceOperations.MaxFloat>(1);

            if (globalIndex == 0)
            {
                val = possibleVal;
            }
        }

        private static int R_ACCESS(int x, int y, int R_EDGE)
        {
            if(COMPRESSED_R == 1)
            {
                int R_SIZE = (R_EDGE * (R_EDGE + 1) / 2);
                int R_ROW_START = (R_SIZE - (R_EDGE - y) * (R_EDGE - y + 1) / 2);
                return R_ROW_START + x - y;
            }
            else
            {
                return x * R_EDGE + y;
            }
        }

        private static Vec3 LoadRMat(ArrayView<Vec3> r_mat, int x, int y, int R_EDGE)
        {
            return r_mat[R_ACCESS(x, y, R_EDGE)];
        }

        private static void SaveRMat(ArrayView<Vec3> r_mat, int x, int y, int R_EDGE, Vec3 value)
        {
            r_mat[R_ACCESS(x, y, R_EDGE)] = value;
        }

        private static void SaveRMat(ArrayView<Vec3> r_mat, int x, int y, int R_EDGE, float value)
        {
            r_mat[R_ACCESS(x, y, R_EDGE)] = new Vec3(value, value, value);
        }
        private static void SaveRMatChannel(ArrayView<Vec3> r_mat, int x, int y, int R_EDGE, int channel, float value)
        {
            r_mat[R_ACCESS(x, y, R_EDGE)] = new Vec3(value, value, value);
        }
    }
}
