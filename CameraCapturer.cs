using OpenTok;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using BitmapEncoder = Windows.Graphics.Imaging.BitmapEncoder;

namespace CameraCapture
{
    public class CameraCapturer : IVideoCapturer
    {
        const int FPS = 15;
        int width;
        int height;
        Timer timer;
        IVideoFrameConsumer frameConsumer;
        MediaCapture mediaCapture;
        DeviceInformationCollection devices;
        int currentDevice = 0;
        bool switchingMedia = false;
        System.Windows.Controls.Image imageCap;
        Texture2D screenTexture;
        OutputDuplication duplicatedOutput;
        private object _mediaCapture;
        MediaFrameReader mediaFrameReader;
        private SoftwareBitmap backBuffer;
        private bool taskRunning = false;

        public CameraCapturer(System.Windows.Controls.Image imageCapx)
        {
            imageCap = imageCapx;
        }

        public void Init(IVideoFrameConsumer _frameConsumer)
        {
            frameConsumer = _frameConsumer;
        }
/*        private async void LayoutRoot_Tapped(object sender, Windows.UI.Xaml.Input.TappedEventArgs e)
        {
            if (devices != null)
            {
                InitializeWebCam();
            }
        }*/

        private static BitmapImage ToBitmapImage(Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        public async void InitializeWebCam()
        {
            Debug.WriteLine("Init");
            if (switchingMedia)
                return;
            switchingMedia = true;

            if (devices == null)
            {
                devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                ListDeviceDetails();
            }
            else
            {
                currentDevice = (currentDevice + 1) % devices.Count;
            }

            if (mediaCapture != null)
            {
                await mediaCapture.StopPreviewAsync();
                imageCap.Source = null;
            }

            var allGroups = await MediaFrameSourceGroup.FindAllAsync();
            var eligibleGroups = allGroups.Select(g => new
            {
                Group = g,

                // For each source kind, find the source which offers that kind of media frame,
                // or null if there is no such source.
                SourceInfos = new MediaFrameSourceInfo[]
                {
                    g.SourceInfos.FirstOrDefault(info => info.SourceKind == MediaFrameSourceKind.Color),
                    g.SourceInfos.FirstOrDefault(info => info.SourceKind == MediaFrameSourceKind.Depth),
                    g.SourceInfos.FirstOrDefault(info => info.SourceKind == MediaFrameSourceKind.Infrared),
                }
            }).Where(g => g.SourceInfos.Any(info => info != null)).ToList();

            if (eligibleGroups.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No source group with color, depth or infrared found.");
                return;
            }
            else
            {
                Debug.WriteLine("Found some sources.", eligibleGroups[0].ToString());
            }

            var selectedGroupIndex = 0; // Select the first eligible group
            MediaFrameSourceGroup selectedGroup = eligibleGroups[selectedGroupIndex].Group;
            MediaFrameSourceInfo colorSourceInfo = eligibleGroups[selectedGroupIndex].SourceInfos[0];
            MediaFrameSourceInfo infraredSourceInfo = eligibleGroups[selectedGroupIndex].SourceInfos[1];
            MediaFrameSourceInfo depthSourceInfo = eligibleGroups[selectedGroupIndex].SourceInfos[2];

            mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync(
                new MediaCaptureInitializationSettings
                {
                    SourceGroup = selectedGroup,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video
                });
            var colorFrameSource = mediaCapture.FrameSources.FirstOrDefault().Value;
            Debug.WriteLine("Frame Sources", mediaCapture.FrameSources);
            var preferredFormat = colorFrameSource.SupportedFormats.Where(format =>
            {
                return format.VideoFormat.Width >= 1080
                && format.Subtype == Windows.Media.MediaProperties.MediaEncodingSubtypes.Argb32;

            }).FirstOrDefault();

          /*  if (preferredFormat == null)
            {
                // Our desired format is not supported
                Debug.WriteLine("No pref Format");
                return;
            }*/

            //await colorFrameSource.SetFormatAsync(preferredFormat);
            //imageCap.Source = new SoftwareBitmapSource();
            mediaFrameReader = await mediaCapture.CreateFrameReaderAsync(colorFrameSource, MediaEncodingSubtypes.Argb32);
            mediaFrameReader.FrameArrived += ColorFrameReader_FrameArrived;
            await mediaFrameReader.StartAsync();
  
        }

        private async void ColorFrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            var mediaFrameReference = sender.TryAcquireLatestFrame();
            var videoMediaFrame = mediaFrameReference?.VideoMediaFrame;
            var softwareBitmap = videoMediaFrame?.SoftwareBitmap;


            if (softwareBitmap != null)
            {
                if (softwareBitmap.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8 ||
                    softwareBitmap.BitmapAlphaMode != Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied)
                {
                    softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }

                // Swap the processed frame to _backBuffer and dispose of the unused image.
                softwareBitmap = Interlocked.Exchange(ref backBuffer, softwareBitmap);
                softwareBitmap?.Dispose();
                SoftwareBitmap latestBitmap;
                while ((latestBitmap = Interlocked.Exchange(ref backBuffer, null)) != null)
                {
                    using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                        encoder.SetSoftwareBitmap(latestBitmap);
                        await encoder.FlushAsync();
                        Bitmap bmp = new Bitmap(stream.AsStream());
                        using (var frame = VideoFrame.CreateYuv420pFrameFromBitmap(bmp))
                        {
                            this.frameConsumer.Consume(frame);
                        }
                        Debug.WriteLine("Writing bitmap");
                    }
                    latestBitmap.Dispose();

                }
               
            }

            mediaFrameReference.Dispose();
        }

        private void ListDeviceDetails()
        {
            int i = 0;

            foreach (var device in devices)
            {
                Debug.WriteLine("* Device [{0}]", i++);
                Debug.WriteLine("EnclosureLocation.InDock: " + device.EnclosureLocation.InDock);
                Debug.WriteLine("EnclosureLocation.InLid: " + device.EnclosureLocation.InLid);
                Debug.WriteLine("EnclosureLocation.Panel: " + device.EnclosureLocation.Panel);
                Debug.WriteLine("Id: " + device.Id);
                Debug.WriteLine("IsDefault: " + device.IsDefault);
                Debug.WriteLine("IsEnabled: " + device.IsEnabled);
                Debug.WriteLine("Name: " + device.Name);
                Debug.WriteLine("IsDefault: " + device.IsDefault);

                foreach (var property in device.Properties)
                {
                    Debug.WriteLine(property.Key + ": " + property.Value);
                }
            }
        }

        public void Start()
        {
            InitializeWebCam();
            /* const int numAdapter = 0;

             // Change the output number to select a different desktop
             const int numOutput = 0;

             var factory = new Factory1();
             var adapter = factory.GetAdapter1(numAdapter);
             var device = new SharpDX.Direct3D11.Device(adapter);

             var output = adapter.GetOutput(numOutput);
             var output1 = output.QueryInterface<Output1>();

             // When you have a multimonitor setup, the coordinates might be a little bit strange
             // depending on how you've setup the environment.
             // In any case Right - Left should give the width, and Bottom - Top the height.
             var desktopBounds = output.Description.DesktopBounds;
             width = desktopBounds.Right - desktopBounds.Left;
             height = desktopBounds.Bottom - desktopBounds.Top;

             var textureDesc = new Texture2DDescription
             {
                 CpuAccessFlags = CpuAccessFlags.Read,
                 BindFlags = BindFlags.None,
                 Format = Format.B8G8R8A8_UNorm,
                 Width = width,
                 Height = height,
                 OptionFlags = ResourceOptionFlags.None,
                 MipLevels = 1,
                 ArraySize = 1,
                 SampleDescription = { Count = 1, Quality = 0 },
                 Usage = ResourceUsage.Staging
             };
             screenTexture = new Texture2D(device, textureDesc);
             duplicatedOutput = output1.DuplicateOutput(device);

             timer = new Timer((Object stateInfo) =>
             {
                 try
                 {
                     SharpDX.DXGI.Resource screenResource;
                     OutputDuplicateFrameInformation duplicateFrameInformation;

                     duplicatedOutput.AcquireNextFrame(1000 / FPS, out duplicateFrameInformation, out screenResource);

                     using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
                         device.ImmediateContext.CopyResource(screenTexture2D, screenTexture);

                     screenResource.Dispose();
                     duplicatedOutput.ReleaseFrame();

                     var mapSource = device.ImmediateContext.MapSubresource(screenTexture, 0, MapMode.Read,
                                                                            SharpDX.Direct3D11.MapFlags.None);

                     IntPtr[] planes = { mapSource.DataPointer };
                     int[] strides = { mapSource.RowPitch };
                     using (var frame = VideoFrame.CreateYuv420pFrameFromBuffer(PixelFormat.FormatArgb32, width, height,
                                                                                planes, strides))
                     {
                         frameConsumer.Consume(frame);
                     }

                     device.ImmediateContext.UnmapSubresource(screenTexture, 0);

                 }
                 catch (SharpDXException) { }
             }, null, 0, 1000 / FPS);

             output1.Dispose();
             output.Dispose();
             adapter.Dispose();
             factory.Dispose();*/
        }

        public void Stop()
        {
            if (timer != null)
            {
                using (var timerDisposed = new ManualResetEvent(false))
                {
                    timer.Dispose(timerDisposed);
                    timerDisposed.WaitOne();
                }
            }
            timer = null;
        }

        public void Destroy()
        {
          //duplicatedOutput.Dispose();
          //screenTexture.Dispose();
        }

        public void SetVideoContentHint(VideoContentHint contentHint)
        {
            if (frameConsumer == null)
                throw new InvalidOperationException("Content hint can only be set after constructing the " +
                    "Publisher and Capturer.");
            frameConsumer.SetVideoContentHint(contentHint);
        }

        public VideoContentHint GetVideoContentHint()
        {
            if (frameConsumer != null)
                return frameConsumer.GetVideoContentHint();
            return VideoContentHint.NONE;
        }

    public VideoCaptureSettings GetCaptureSettings()
        {
            VideoCaptureSettings settings = new VideoCaptureSettings();
            settings.Width = width;
            settings.Height = height;
            settings.Fps = FPS;
            settings.MirrorOnLocalRender = false;
            settings.PixelFormat = OpenTok.PixelFormat.FormatYuv420p;
            return settings;
        }
    }
}
