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
        bool blackout = false;
        const int FPS = 15;
        Timer timer;
        IVideoFrameConsumer frameConsumer;
        MediaCapture mediaCapture;
        MediaCapture mediaCapture_buff;
        DeviceInformationCollection devices;
        MediaFrameReader mediaFrameReader;
        MediaFrameReader mediaFrameReader_buff;
        private SoftwareBitmap backBuffer;
        int fade_in = 255;
        int fade_out = 0;
        public void Init(IVideoFrameConsumer _frameConsumer)
        {
            frameConsumer = _frameConsumer;
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
            Debug.WriteLine(">>INIT WE CAM");
            //if null is passed, use the defaule camera
            
            fade_out = 0;
            
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

            try
            {
                mediaCapture_buff = new MediaCapture(); //instantiate a mediaCapture_buffer
                await mediaCapture_buff.InitializeAsync(
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

            var colorFrameSource = mediaCapture_buff.FrameSources.FirstOrDefault().Value;
            Debug.WriteLine("Frame Sources", mediaCapture_buff.FrameSources);
            var preferredFormat = colorFrameSource.SupportedFormats.Where(format =>
            {
                return format.VideoFormat.Width >= 1080
                && format.Subtype == MediaEncodingSubtypes.Argb32;

            }).FirstOrDefault();
            
            //here we let the mediaCapture Buffer do the initialization. The main mediaCapture is untouched until we switch
            //Also we assign it to a mediaFrameReader buffer so the main one is also untouched while this one loads
            mediaFrameReader_buff = await mediaCapture_buff.CreateFrameReaderAsync(colorFrameSource, MediaEncodingSubtypes.Argb32);
            await mediaFrameReader_buff.StartAsync(); //start capture on new device using the mediaFrameReader Buffer
            mediaCapture = mediaCapture_buff; //we assign the buffer to the mediaCapture
            mediaCapture_buff = null; //we dispose the mediaCaptureBuffer
            
            fade_in = 255; //start the fade

            //if there is a current Frame reader, let's dispose it
            if (mediaFrameReader != null)
            {
                
                mediaFrameReader.Dispose();
                mediaFrameReader = null;
            }
            
            //we assign the mediFrameReader buffer to the main mediaFrame Reader
            mediaFrameReader = mediaFrameReader_buff;
            mediaFrameReader.FrameArrived += ColorFrameReader_FrameArrived; //assign a callback handler
            mediaFrameReader_buff = null; //dispose the buffer
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

                        if (fade_in >= 0)
                        {
                            Debug.WriteLine(fade_in);
                            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                using (Brush cloud_brush = new SolidBrush(Color.FromArgb(fade_in, Color.Black)))
                                {
                                    g.FillRectangle(cloud_brush, r);
                                }
                            }
                            fade_in -= 70;
                        }

      
                        using (var frame = VideoFrame.CreateYuv420pFrameFromBitmap(bmp))
                        {
                            this.frameConsumer.Consume(frame);
                        }
                    }
                    latestBitmap.Dispose();

                }

            }
            else
            {
                Debug.WriteLine(">>NOFRAME");
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
