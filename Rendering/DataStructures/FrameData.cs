﻿using ILGPU;
using ILGPU.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace NullEngine.Rendering.DataStructures
{
    public class FrameData
    {
        public int width;
        public int height;

        public MemoryBuffer<float> colorBuffer;
        public MemoryBuffer<float> lightBuffer;
        public MemoryBuffer<float> depthBuffer;
        public MemoryBuffer<Ray> rayBuffer;
        public MemoryBuffer<float> outputBuffer;


        public dFrameData deviceFrameData;

        public FrameData(Accelerator device, int width, int height)
        {
            this.width = width;
            this.height = height;

            colorBuffer = device.Allocate<float>(width * height * 3);
            lightBuffer = device.Allocate<float>(width * height * 3);
            depthBuffer = device.Allocate<float>(width * height * 3);
            rayBuffer = device.Allocate<Ray>(width * height);
            outputBuffer = device.Allocate<float>(width * height * 4);

            deviceFrameData = new dFrameData(this);
        }

        public void Dispose()
        {
            colorBuffer.Dispose();
            lightBuffer.Dispose();
            depthBuffer.Dispose();
            outputBuffer.Dispose();
        }
    }

    public struct dFrameData
    {
        public int width;
        public int height;
        public ArrayView<float> colorBuffer;
        public ArrayView<float> lightBuffer;
        public ArrayView<float> depthBuffer;
        public ArrayView<Ray> rayBuffer;
        public ArrayView<float> outputBuffer;

        public dFrameData(FrameData frameData)
        {
            width = frameData.width;
            height = frameData.height;
            colorBuffer = frameData.colorBuffer;
            lightBuffer = frameData.lightBuffer;
            depthBuffer = frameData.depthBuffer;
            rayBuffer = frameData.rayBuffer;
            outputBuffer = frameData.outputBuffer;
        }
    }
}
