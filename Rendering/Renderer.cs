using NullEngine.Utils;
using NullEngine.Rendering.DataStructures;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ILGPU;
using ILGPU.Runtime;
using NullEngine.Rendering.Implementation;
using System.Windows;
using Avalonia.Controls;
using NullEngine.Views;
using Avalonia.Threading;
using System.Diagnostics;

namespace NullEngine.Rendering
{
    public class Renderer
    {
        public int width;
        public int height;
        
        private bool run = true;
        private int targetFramerate;
        private double frameTime;

        private ByteFrameBuffer deviceFrameBuffer;
        private byte[] frameBuffer = Array.Empty<byte>();
        private GPU gpu;
        private Camera camera;
        private RenderDataManager renderDataManager;
        private FrameData frameData;
        private MainWindow window;
        private Thread renderThread;
        private FrameTimer frameTimer;

        public Renderer(MainWindow window, int targetFramerate, bool forceCPU)
        {
            this.window = window;
            this.targetFramerate = targetFramerate;

            gpu = new GPU(forceCPU);
            renderDataManager = new RenderDataManager(gpu);
            frameTimer = new FrameTimer();

            renderThread = new Thread(RenderThread);
            renderThread.IsBackground = true;
        }

        public void Start()
        {
            renderThread.Start();
        }

        public void Stop()
        {
            run = false;
            renderThread.Join();
        }

        public void OnResChanged(int width, int height)
        {
            this.width = width;
            this.height = height;
            
            camera = new Camera(new Vec3(0, -1, -5), new Vec3(0, -1, -4), Vec3.unitVector(new Vec3(0, 1, 0)), width, height, 3, 40f);
        }

        //eveything below this happens in the render thread
        private void RenderThread()
        {
            while (run)
            {
                frameTimer.startUpdate();

                if(ReadyFrameBuffer())
                {
                    RenderToFrameBuffer();
                    Dispatcher.UIThread.Post(Draw);
                }

                frameTime = frameTimer.endUpdateForTargetUpdateTime(1000.0 / targetFramerate, true);
                window.frameTime = frameTime;
            }

            if (deviceFrameBuffer != null)
            {
                deviceFrameBuffer.Dispose();
                frameData.Dispose();
            }
            gpu.Dispose();
        }

        private bool ReadyFrameBuffer()
        {
            if((width != 0 && height != 0))
            {
                if(deviceFrameBuffer == null || deviceFrameBuffer.frameBuffer.width != width || deviceFrameBuffer.frameBuffer.height != height)
                {
                    if (deviceFrameBuffer != null)
                    {
                        deviceFrameBuffer.Dispose();
                        frameData.Dispose();
                    }

                    frameBuffer = new byte[width * height * 4];
                    deviceFrameBuffer = new ByteFrameBuffer(gpu, height, width);
                    frameData = new FrameData(gpu.device, width, height);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        private void RenderToFrameBuffer()
        {
            if (deviceFrameBuffer != null && !deviceFrameBuffer.isDisposed)
            {
                gpu.Render(deviceFrameBuffer, renderDataManager, frameData);
                deviceFrameBuffer.memoryBuffer.CopyTo(frameBuffer, 0, 0, frameBuffer.Length);
            }
        }

        private void Draw()
        {
            window.update(ref frameBuffer);
            window.frameRate = frameTimer.lastFrameTimeMS;
        }
    }
}
