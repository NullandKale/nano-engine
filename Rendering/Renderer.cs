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
using NullEngine.Scenes;

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
        private Scene scene;

        private int tick = 0;
        private int lastCameraMovementTick = 0;

        public Renderer(MainWindow window, string sceneFileName, int targetFramerate, bool forceCPU, bool isLinux)
        {
            this.window = window;
            this.targetFramerate = targetFramerate;

            gpu = new GPU(forceCPU, isLinux);
            renderDataManager = new RenderDataManager(gpu);

            //scene = DebugScene.Load();
            //scene.Save();
            scene = Scene.Load("debug_scene.json");

            LoadSceneData();

            frameTimer = new FrameTimer();

            renderThread = new Thread(RenderThread);
            renderThread.IsBackground = true;
        }

        public void LoadSceneData()
        {
            camera = scene.sceneData.mainCamera;
            for(int i = 0; i < scene.sceneData.spheres.Count; i++)
            {
                Sphere toAdd = scene.sceneData.spheres[i];
                toAdd.materialIndex = renderDataManager.addMaterialForID(scene.sceneData.materials[toAdd.materialIndex]);
                renderDataManager.addSphereForID(toAdd);
            }
        }

        public void Start()
        {
            renderThread.Start();
        }

        public void Stop()
        {
            run = false;
            renderThread.Join();

            scene.sceneData.mainCamera = camera;
            scene.Save();
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
            scene.sceneData.mainCamera = camera;
        }

        public void CameraUpdate(Vec3 movement, Vec3 turn)
        {
            camera = new Camera(camera, movement, turn);
            lastCameraMovementTick = tick;
            scene.sceneData.mainCamera = camera;
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
