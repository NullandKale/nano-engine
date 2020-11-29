using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.CPU;
using System;
using System.Collections.Generic;
using System.Text;
using NullEngine.Rendering.DataStructures;

namespace NullEngine.Rendering.Implementation
{
    public class GPU
    {
        public Context context;
        public Accelerator device;

        private Action<Index1, dByteFrameBuffer, dFrameData, dRenderData> ClearFrame;
        private Action<Index1, dFrameData, ArrayView<ulong>> InitPerPixelRngData;
        public GPU(bool forceCPU)
        {
            context = new Context(ContextFlags.FastMath);
            context.EnableAlgorithms();

            if(forceCPU || CudaAccelerator.CudaAccelerators.Length < 1)
            {
                device = new CPUAccelerator(context);
            }
            else
            {
                device = new CudaAccelerator(context);
            }

            initRenderKernels();
        }

        private void initRenderKernels()
        {
            ClearFrame = device.LoadAutoGroupedStreamKernel<Index1, dByteFrameBuffer, dFrameData, dRenderData>(GPUKernels.ClearFrame);
            InitPerPixelRngData = device.LoadAutoGroupedStreamKernel<Index1, dFrameData, ArrayView<ulong>>(GPUKernels.InitPerPixelRngData);
        }

        public void Dispose()
        {
            device.Dispose();
            context.Dispose();
        }

        public void InitRNG(FrameData frameData)
        {
            long seedLength = frameData.rngBuffer.Length * 2;
            ulong[] seeds = new ulong[seedLength];

            Random rng = new Random();
            byte[] bytes = new byte[16];

            //probably always aligns to N % 4 == 0 so may be able to do sets of 4 ulongs but 2 will for sure work

            for (long i = 0; i < seedLength; i += 2)
            {
                rng.NextBytes(bytes);
                seeds[i] = BitConverter.ToUInt64(bytes, 0);
                seeds[i + 1] = BitConverter.ToUInt64(bytes, 8);
            }

            MemoryBuffer<ulong> deviceSeedBuffer = device.Allocate(seeds);
            InitPerPixelRngData(frameData.rngBuffer.Length, frameData.deviceFrameData, deviceSeedBuffer);
            device.Synchronize();
            deviceSeedBuffer.Dispose();
        }

        public void Render(ByteFrameBuffer output, RenderDataManager renderDataManager, FrameData frameData)
        {
            ClearFrame(output.memoryBuffer.Length / 4, output.frameBuffer, frameData.deviceFrameData, renderDataManager.getDeviceRenderData());
            device.Synchronize();
        }
    }

    public static class GPUKernels
    {
        public static void InitPerPixelRngData(Index1 pixel, dFrameData frameData, ArrayView<ulong> rngSeeds)
        {
            frameData.rngBuffer[pixel] = new ILGPU.Algorithms.Random.XorShift128Plus(rngSeeds[(pixel * 2)], rngSeeds[(pixel * 2) + 1]);
        }

        public static void ClearFrame(Index1 pixel, dByteFrameBuffer output, dFrameData frameData, dRenderData renderData)
        {
            output.writeFrameBuffer((int)(pixel * 4), 1f, 0f, 1f, 1f);
        }

        public static void GenerateRays(Index1 pixel, Camera camera, dFrameData frameData)
        {

        }

    }
}
