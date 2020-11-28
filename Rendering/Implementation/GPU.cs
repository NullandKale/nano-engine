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

        public Action<Index1, dByteFrameBuffer, dFrameData, dRenderData> clearFrame;
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
            clearFrame = device.LoadAutoGroupedStreamKernel<Index1, dByteFrameBuffer, dFrameData, dRenderData>(GPUKernels.ClearFrame);
        }

        public void Dispose()
        {
            device.Dispose();
            context.Dispose();
        }

        public void Render(ByteFrameBuffer output, RenderDataManager renderDataManager, FrameData frameData)
        {
            clearFrame(output.memoryBuffer.Length / 4, output.frameBuffer, frameData.deviceFrameData, renderDataManager.getDeviceRenderData());
            device.Synchronize();
        }
    }

    public static class GPUKernels
    {
        public static void ClearFrame(Index1 pixel, dByteFrameBuffer output, dFrameData frameData, dRenderData renderData)
        {
            output.writeFrameBuffer((int)(pixel * 4), 1f, 0f, 1f, 1f);
        }

        public static void GenerateRays(Index1 pixel, Camera camera, dFrameData frameData)
        {

        }

    }
}
