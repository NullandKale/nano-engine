using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NullEngine.Rendering;
using NullEngine.Rendering.DataStructures;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NullEngine.Views
{
    public class MainWindow : Window
    {
        public double scale = -2;

        public bool isMouseActive = false;
        public bool hasInitialMousePos = false;
        public float lastMouseX;
        public float lastMouseY;

        public Action<int, int> onResolutionChanged;
        public int width;
        public int height;
        public WriteableBitmap wBitmap;

        public double frameTime;
        public double frameRate;

        private Image Frame;
        public double FrameWidth;
        public double FrameHeight;

        private TextBlock Info;

        public Renderer renderer;

        public MainWindow()
        {
            InitializeComponent();
            InitRenderer();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            Frame = this.FindControl<Image>("Frame");
            Info = this.FindControl<TextBlock>("Info");
            ClientSizeProperty.Changed.Subscribe(HandleResized);
            Closing += MainWindow_Closing;
            Frame.PointerEnter += MainWindow_PointerEnter;
            Frame.PointerMoved += MainWindow_PointerMoved;
            KeyDown += Frame_KeyDown;
            resize(ClientSize);
        }

        private void Frame_KeyDown(object sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.E)
            {
                isMouseActive = !isMouseActive;
                if(!isMouseActive)
                {
                    hasInitialMousePos = false;
                }
            }

            Vec3 movement = new Vec3();
            float speed = 0.1f;
            bool moved = false;

            if (e.Key == Avalonia.Input.Key.W)
            {
                movement += (renderer.camera.lookAt - renderer.camera.origin) * speed;
                movement.y = 0;
                moved = true;
            }

            if (e.Key == Avalonia.Input.Key.S)
            {
                movement -= (renderer.camera.lookAt - renderer.camera.origin) * speed;
                movement.y = 0;
                moved = true;
            }

            if (e.Key == Avalonia.Input.Key.D)
            {
                movement -= Vec3.cross(renderer.camera.up, renderer.camera.lookAt - renderer.camera.origin) * speed;
                movement.y = 0;
                moved = true;
            }

            if (e.Key == Avalonia.Input.Key.A)
            {
                movement += Vec3.cross(renderer.camera.up, renderer.camera.lookAt - renderer.camera.origin) * speed;
                movement.y = 0;
                moved = true;
            }

            if(e.Key == Avalonia.Input.Key.D1)
            {
                renderer.camera.mode = 0;
            }

            if (e.Key == Avalonia.Input.Key.D2)
            {
                renderer.camera.mode = 1;
            }

            if (e.Key == Avalonia.Input.Key.D3)
            {
                renderer.camera.mode = 2;
            }

            if (moved)
            {
                renderer.CameraUpdate(movement, new Vec3());
            }
        }

        private void MainWindow_PointerMoved(object sender, Avalonia.Input.PointerEventArgs e)
        {
            if(isMouseActive)
            {
                Point p = e.GetPosition(Frame);

                float x = (float)(p.X - (FrameWidth / 2));
                float y = (float)(p.Y - (FrameHeight / 2));

                if (hasInitialMousePos)
                {
                    float xChange = lastMouseX - x;
                    float yChange = lastMouseY - y;

                    Vec3 strafe = new Vec3(-yChange, -xChange, 0) * 0.008f;
                    renderer.CameraUpdate(new Vec3(), strafe);
                }

                lastMouseX = x;
                lastMouseY = y;
                hasInitialMousePos = true;
            }
        }

        private void MainWindow_PointerEnter(object sender, Avalonia.Input.PointerEventArgs e)
        {
            e.Pointer.Capture(Frame);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            renderer.Stop();
        }

        private void HandleResized(AvaloniaPropertyChangedEventArgs obj)
        {
            resize(ClientSize);
        }

        private void InitRenderer()
        {
            renderer = new Renderer(this, "", 60, false, Environment.OSVersion.Platform == PlatformID.Unix);
            renderer.Start();
        }

        public void resize(Size size)
        {
            FrameWidth = size.Width;
            FrameHeight = size.Height;

            width = (int)size.Width;
            height = (int)size.Height;

            if (scale > 0)
            {
                height = (int)(height * scale);
                width = (int)(width * scale);
            }
            else
            {
                height = (int)(height / -scale);
                width = (int)(width / -scale);
            }

            if (wBitmap != null)
            {
                wBitmap.Dispose();
            }

            wBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888);

            Frame.Source = wBitmap;

            if(renderer != null)
            {
                renderer.OnResChanged(width, height);
            }
        }

        public void update(ref byte[] data)
        {
            if (data.Length == wBitmap.PixelSize.Width * wBitmap.PixelSize.Height * 4)
            {
                using (ILockedFramebuffer framebuffer = wBitmap.Lock())
                {
                    Marshal.Copy(data, 0, framebuffer.Address, data.Length);
                }

                //HACK must manually call invalidate on the Image control that displays the writeable bitmap
                Frame.InvalidateVisual();

                Info.Text = (int)frameRate + " MS";
            }
            else
            {
                Trace.WriteLine("invalid frame data size " + data.Length + " expected " + wBitmap.PixelSize.Width * wBitmap.PixelSize.Height * 4);
            }
        }
    }
}