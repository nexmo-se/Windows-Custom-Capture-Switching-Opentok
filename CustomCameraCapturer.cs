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
        IVideoFrameConsumer fauxConsumer;
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
            Debug.WriteLine(">>INIT WE CAM");
            //if null is passed, use the defaule camera
            
            fade_out = 0;
            fade_in =255;
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
                
                mediaFrameReader_buff = mediaFrameReader;
                
                
                mediaFrameReader_buff.FrameArrived += ColorFrameReader_FrameArrived_overflow;
                mediaFrameReader_buff.FrameArrived -= ColorFrameReader_FrameArrived_normal;
                //await mediaFrameReader.StopAsync();
            }

            try
            {
                mediaCapture_buff = mediaCapture;
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
            if (mediaFrameReader != null)
                await mediaFrameReader.StopAsync();
            mediaFrameReader.FrameArrived += ColorFrameReader_FrameArrived_normal;
            await mediaFrameReader.StartAsync();
  
        }

        private async void ColorFrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args, Boolean overflow)
        {
            
            var mediaFrameReference = sender.TryAcquireLatestFrame();
            var videoMediaFrame = mediaFrameReference?.VideoMediaFrame;
            var softwareBitmap = videoMediaFrame?.SoftwareBitmap;


            if (softwareBitmap != null)
            {
                if (!overflow)
                {
                    Debug.WriteLine(">>FRAME ");
                    if (mediaFrameReader_buff != null)
                    {
                        mediaCapture_buff = null;
                        
                        mediaFrameReader_buff.FrameArrived -= ColorFrameReader_FrameArrived_overflow;
                        await mediaFrameReader_buff.StopAsync();
                        mediaFrameReader_buff = null;
 
                        return;
                    }
                }
                else
                {
                    Debug.WriteLine(">>FRAME OVERFLOW");
                    if (sender == null)
                    {
                        return;
                    }
                }

                
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
                        
                        if (fade_in >= 0 && !overflow)
                        {

                            
                            Debug.WriteLine(fade_in);
                            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                using (Brush cloud_brush = new SolidBrush(Color.FromArgb(fade_in > 255?255:fade_in, Color.Black)))
                                {
                                    g.FillRectangle(cloud_brush, r);
                                }
                            }
                            fade_in -= 50;
                        }
                        else if(overflow )
                        {
                            if (fade_out >= 255)
                            {
                                return;

                            }
                            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                using (Brush cloud_brush = new SolidBrush(Color.FromArgb(fade_out, Color.Black)))
                                {
                                    g.FillRectangle(cloud_brush, r);
                                }
                            }
                            fade_out += 50;
                           

                        }
                        if (AllOneColor(bmp)) return;
                        using (var frame = VideoFrame.CreateYuv420pFrameFromBitmap(bmp))
                        {
                            Debug.WriteLine("Wrote Something");
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

        private void ColorFrameReader_FrameArrived_normal(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            ColorFrameReader_FrameArrived(sender, args, false);
        }
        
        private  void ColorFrameReader_FrameArrived_overflow(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            ColorFrameReader_FrameArrived( sender,  args, true);
        }

        private bool AllOneColor(Bitmap bmp)
        {
            // Lock the bitmap's bits.  
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, bmp.PixelFormat);

            // Get the address of the first line.
            IntPtr ptr = bmpData.Scan0;

            // Declare an array to hold the bytes of the bitmap.
            int bytes = bmpData.Stride * bmp.Height;
            byte[] rgbValues = new byte[bytes];

            // Copy the RGB values into the array.

            System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);

            bool AllOneColor = true;
            for (int index = 0; index < rgbValues.Length; index++)
            {
                //compare the current A or R or G or B with the A or R or G or B at position 0,0.
                if (rgbValues[index] != rgbValues[index % 4])
                {
                    AllOneColor = false;
                    break;
                }
            }
            // Unlock the bits.
            bmp.UnlockBits(bmpData);
            return AllOneColor;
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
