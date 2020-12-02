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

        private MainWindow window;
        private Thread renderThread;
        private FrameTimer frameTimer;

        private ByteFrameBuffer deviceFrameBuffer;
        private byte[] frameBuffer = Array.Empty<byte>();

        private GPU gpu;
        public Camera camera;
        private RenderDataManager renderDataManager;
        private FrameData frameData;

        private int lastCameraMovementTick = 0;

        public Renderer(MainWindow window, string sceneFileName, int targetFramerate, bool forceCPU, bool isLinux)
        {
            this.window = window;
            this.targetFramerate = targetFramerate;

            gpu = new GPU(forceCPU, isLinux);
            renderDataManager = new RenderDataManager(gpu);

            int mat0 = renderDataManager.addMaterialForID(MaterialData.makeDiffuse(new Vec3(0.9999, 0, 0)));
            int mat1 = renderDataManager.addMaterialForID(MaterialData.makeDiffuse(new Vec3(0, 0.9999, 0)));
            int mat2 = renderDataManager.addMaterialForID(MaterialData.makeDiffuse(new Vec3(0, 0, 0.9999)));
            int mat3 = renderDataManager.addMaterialForID(MaterialData.makeLight(new Vec3(0.9999, 0.9999, 0.9999)));
            int mat4 = renderDataManager.addMaterialForID(MaterialData.makeDiffuse(new Vec3(0.9999, 0.9999, 0.9999)));
            int mat5 = renderDataManager.addMaterialForID(MaterialData.makeMirror(new Vec3(0.9999, 0.9999, 0.9999), 0.2f));
            int mat6 = renderDataManager.addMaterialForID(MaterialData.makeMirror(new Vec3(0.9999, 0.9999, 0.9999)));
            int mat7 = renderDataManager.addMaterialForID(MaterialData.makeLight(new Vec3(0.9999, 0.1, 0.1)));
            int mat8 = renderDataManager.addMaterialForID(MaterialData.makeLight(new Vec3(0.1, 0.1, 0.9999)));

            renderDataManager.addSphereForID(new Sphere(new Vec3(0, 1, 0), 0.5f, mat0));
            renderDataManager.addSphereForID(new Sphere(new Vec3(1, 1, 0), 0.5f, mat1));
            renderDataManager.addSphereForID(new Sphere(new Vec3(-1, 1, 0), 0.5f, mat2));
            renderDataManager.addSphereForID(new Sphere(new Vec3(0, 10, -5), 0.5f, mat3));
            renderDataManager.addSphereForID(new Sphere(new Vec3(-10, 10, -5), 0.5f, mat7));
            renderDataManager.addSphereForID(new Sphere(new Vec3(10, 10, -5), 0.5f, mat8));
            renderDataManager.addSphereForID(new Sphere(new Vec3(-1, -1000, 0), 1000f, mat4));
            renderDataManager.addSphereForID(new Sphere(new Vec3(4, 2, 0), 2f, mat5));
            renderDataManager.addSphereForID(new Sphere(new Vec3(-4, 2, 0), 2f, mat6));

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
            
            camera = new Camera(new Vec3(0, 1, -5), new Vec3(0, 1, -4), Vec3.unitVector(new Vec3(0, 1, 0)), width, height, 5, 40f);
            lastCameraMovementTick = gpu.tick;
        }

        public void CameraModeUpdate(int mode)
        {
            camera.mode = mode;
            lastCameraMovementTick = gpu.tick;
        }

        public void CameraUpdate(Vec3 movement, Vec3 turn)
        {
            camera = new Camera(camera, movement, turn);
            lastCameraMovementTick = gpu.tick;
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
                    gpu.InitRNG(frameData);
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
                gpu.Render(deviceFrameBuffer, camera, renderDataManager, frameData, gpu.tick - lastCameraMovementTick);
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
