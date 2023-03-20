using OpenTok;
using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using BitmapEncoder = Windows.Graphics.Imaging.BitmapEncoder;
using System.Windows;
using System.Threading.Tasks;

namespace CameraCapture
{
    public class CustomCameraCapturer : IVideoCapturer
    {
        int width;
        int height;
        const int FPS = 15;
        Timer timer;
        IVideoFrameConsumer frameConsumer;
        MediaCapture mediaCapture;
        DeviceInformationCollection devices;
        MediaFrameReader mediaFrameReader;
        private SoftwareBitmap backBuffer;
        public void Init(IVideoFrameConsumer _frameConsumer)
        {
            frameConsumer = _frameConsumer;
        }

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

        public async void getVideoDevices( Action<DeviceInformationCollection> callback )
        {
            await Task.Delay(1500);//wait 1.5 seconds to account for slow devices
            Debug.WriteLine("getting Devices");
            devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
           
            if(devices.Count == 0)
            {
                Debug.WriteLine("No Device found");
            }
            callback(devices);
        }

        public async void InitializeWebCam(string device_id)
        {
            //if null is passed, use the defaule camera
            if (device_id is null)
            {
                devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                if(devices.Count == 0)
                {
                    //send a message if camera cannot be accessed
                    MessageBox.Show("No Available Capture Devices");
                    return;
                }
                device_id = devices[0].Id;
            }
            if (mediaFrameReader != null)
            {
                await mediaFrameReader.StopAsync();
            }

            try
            {
                mediaCapture = new MediaCapture();
                await mediaCapture.InitializeAsync(
                    new MediaCaptureInitializationSettings
                    {
                        VideoDeviceId = device_id,
                        SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                        MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                        StreamingCaptureMode = StreamingCaptureMode.Video
                    });
            }
            catch (UnauthorizedAccessException)
            {
                // This will be thrown if the user denied access to the camera in privacy settings
                MessageBox.Show("The app was denied access to the camera");
                return;
            }

            
            Debug.WriteLine(">>KEY",mediaCapture.FrameSources.FirstOrDefault().Key);
            var colorFrameSource = mediaCapture.FrameSources.FirstOrDefault().Value;
            Debug.WriteLine("Frame Sources", mediaCapture.FrameSources);
            var preferredFormat = colorFrameSource.SupportedFormats.Where(format =>
            {
                return format.VideoFormat.Width >= 1080
                && format.Subtype == MediaEncodingSubtypes.Argb32;

            }).FirstOrDefault();


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
                    }
                    latestBitmap.Dispose();

                }
               
            }

        }


        public void Start()
        {
            InitializeWebCam(null); //starts with default camera
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
