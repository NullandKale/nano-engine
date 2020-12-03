﻿using NullEngine.Utils;
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
        public FrameTimer frameTimer;

        private ByteFrameBuffer deviceFrameBuffer;
        private byte[] frameBuffer = Array.Empty<byte>();

        private GPU gpu;
        public Camera camera;
        private RenderDataManager renderDataManager;
        private FrameData frameData;

        private int tick = 0;
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
            int mat3 = renderDataManager.addMaterialForID(MaterialData.makeLight(new Vec3(1, 1, 1)));
            int mat4 = renderDataManager.addMaterialForID(MaterialData.makeDiffuse(new Vec3(1, 1, 1)));
            int mat5 = renderDataManager.addMaterialForID(MaterialData.makeMirror(new Vec3(0.9999, 0.9999, 0.9999), 0.2f));
            int mat6 = renderDataManager.addMaterialForID(MaterialData.makeMirror(new Vec3(0.9999, 0.9999, 0.9999)));
            int mat7 = renderDataManager.addMaterialForID(MaterialData.makeLight(new Vec3(0.9999, 0.1, 0.1)));
            int mat8 = renderDataManager.addMaterialForID(MaterialData.makeLight(new Vec3(0.1, 0.1, 0.9999)));

            renderDataManager.addSphereForID(new Sphere(new Vec3(0, 1, 0), 0.5f, mat0));
            renderDataManager.addSphereForID(new Sphere(new Vec3(1, 1, 0), 0.5f, mat1));
            renderDataManager.addSphereForID(new Sphere(new Vec3(-1, 1, 0), 0.5f, mat2));
            renderDataManager.addSphereForID(new Sphere(new Vec3(0, 25, 0), 1f, mat3));
            //renderDataManager.addSphereForID(new Sphere(new Vec3(-25, 25, 0), 1f, mat7));
            //renderDataManager.addSphereForID(new Sphere(new Vec3(25, 25, 0), 1f, mat8));
            renderDataManager.addSphereForID(new Sphere(new Vec3(-1, -100000, 0), 100000f, mat4));
            //renderDataManager.addSphereForID(new Sphere(new Vec3(7, 5, 0), 5f, mat6));
            //renderDataManager.addSphereForID(new Sphere(new Vec3(-7, 5, 0), 5f, mat6));
            //renderDataManager.addSphereForID(new Sphere(new Vec3(0, 5, -7), 5f, mat5));

            Random rng = new Random();

            for(int i = 0; i < 50; i++)
            {
                int mat = mat0;

                if(rng.NextDouble() < 0.90)
                {
                    mat = renderDataManager.addMaterialForID(MaterialData.makeDiffuse(new Vec3(rng.NextDouble() > 0.5 ? 0.1 : 0.9, rng.NextDouble() > 0.5 ? 0.1 : 0.9, rng.NextDouble() > 0.5 ? 0.1 : 0.9)));
                }
                else
                {
                    mat = renderDataManager.addMaterialForID(MaterialData.makeMirror(new Vec3(rng.NextDouble() > 0.5 ? 0.1 : 0.9, rng.NextDouble() > 0.5 ? 0.1 : 0.9, rng.NextDouble() > 0.5 ? 0.1 : 0.9)));
                }

                float size = (float)(rng.NextDouble() * 2);
                renderDataManager.addSphereForID(new Sphere(new Vec3(rng.Next(-25, 25), size, rng.Next(5, 25)), size, mat));
            }
            camera = new Camera(new Vec3(-0.25, 1, 7.5), new Vec3(-0.25, 1.12, 6.5), Vec3.unitVector(new Vec3(0, 1, 0)), 300, 300, 5, 3, 3, 40f);

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

            camera = new Camera(camera, width, height);            
            lastCameraMovementTick = tick;
        }

        public void CameraModeUpdate(int mode)
        {
            camera.mode = mode;
            lastCameraMovementTick = tick;
        }

        public void CameraUpdate(Vec3 movement, Vec3 turn)
        {
            camera = new Camera(camera, movement, turn);
            lastCameraMovementTick = tick;
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
                gpu.Render(deviceFrameBuffer, camera, renderDataManager, frameData, tick, tick - lastCameraMovementTick);
                deviceFrameBuffer.memoryBuffer.CopyTo(frameBuffer, 0, 0, frameBuffer.Length);
                tick++;
            }
        }

        private void Draw()
        {
            window.update(ref frameBuffer);
            window.frameRate = frameTimer.lastFrameTimeMS;
        }
    }
}
