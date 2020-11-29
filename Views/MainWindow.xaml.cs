using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NullEngine.Rendering;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NullEngine.Views
{
    public class MainWindow : Window
    {
        public double scale = -4;

        public Action<int, int> onResolutionChanged;
        public int width;
        public int height;
        public WriteableBitmap wBitmap;

        public double frameTime;
        public double frameRate;

        private Image Frame;
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
            //Info = this.FindControl<TextBlock>("Info");
            ClientSizeProperty.Changed.Subscribe(HandleResized);
            Closing += MainWindow_Closing;
            resize(ClientSize);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            renderer.Stop();
        }

        private void HandleResized(AvaloniaPropertyChangedEventArgs obj)
        {
            //WTF? fixes resizing issues with writing to writeablebitmap
            Dispatcher.UIThread.Post(() => { resize(ClientSize); });
        }

        private void InitRenderer()
        {
            renderer = new Renderer(this, 60, false);
            renderer.Start();
        }

        public void resize(Size size)
        {
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

            wBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888);
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
                using (Avalonia.Platform.ILockedFramebuffer framebuffer = wBitmap.Lock())
                {
                    Marshal.Copy(data, 0, framebuffer.Address, data.Length);
                }

                Frame.Source = wBitmap;

                //Info.Text = (int)frameRate + " MS";
            }
            else
            {
                Trace.WriteLine("invalid frame data size " + data.Length + " expected " + wBitmap.PixelSize.Width * wBitmap.PixelSize.Height * 4);
            }
        }
    }
}