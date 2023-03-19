using OpenTok;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CameraCapture
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public const string API_KEY = "";
        public const string SESSION_ID = "";
        public const string TOKEN = "";

        CameraCapturer Capturer;
        Session Session;
        Publisher Publisher;
        bool Disconnect = false;
        Dictionary<Stream, Subscriber> SubscriberByStream = new Dictionary<Stream, Subscriber>();
        private IntPtr windowHandle;

        public MainWindow()
        {
            InitializeComponent();

            Capturer = new CameraCapturer();
            Capturer.getVideoDevices((devices) => {
                populateCameraList(devices);
            }); // this is where you do your magic

            // We create the publisher here to show the preview when application starts
            // Please note that the PublisherVideo component is added in the xaml file
            Publisher = new Publisher.Builder(Context.Instance)
            {
                Renderer = PublisherVideo,
                Capturer = Capturer,
                HasAudioTrack = false
            }.Build();
            
            // We set the video source type to screen to disable the downscaling of the video
            // in low bandwidth situations, instead the frames per second will drop.
            Publisher.VideoSourceType = VideoSourceType.Screen;
            
            if (API_KEY == "" || SESSION_ID == "" || TOKEN == "")
            {
                MessageBox.Show("Please fill out the API_KEY, SESSION_ID and TOKEN variables in the source code " +
                    "in order to connect to the session", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectDisconnectButton.IsEnabled = false;
            }
            else
            {
                Session = new Session.Builder(Context.Instance, API_KEY, SESSION_ID).Build();
                Session.Connected += Session_Connected;
                Session.Disconnected += Session_Disconnected;
                Session.Error += Session_Error;
                Session.StreamReceived += Session_StreamReceived;
                Session.StreamDropped += Session_StreamDropped;
            }

            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            foreach (var subscriber in SubscriberByStream.Values)
            {
                subscriber.Dispose();
            }
            Publisher?.Dispose();
            Session?.Dispose();
        }

        private void Session_Connected(object sender, EventArgs e)
        {
            try
            {
                Session.Publish(Publisher);
            }
            catch (OpenTokException ex)
            {
                Trace.WriteLine("OpenTokException " + ex.ToString());
            }
        }

        private void Session_Disconnected(object sender, EventArgs e)
        {
            Trace.WriteLine("Session disconnected");
            SubscriberByStream.Clear();
            SubscriberGrid.Children.Clear();
        }

        private void Session_Error(object sender, Session.ErrorEventArgs e)
        {
            MessageBox.Show("Session error:" + e.ErrorCode, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void UpdateGridSize(int numberOfSubscribers)
        {
            int rows = Convert.ToInt32(Math.Round(Math.Sqrt(numberOfSubscribers)));
            int cols = rows == 0 ? 0 : Convert.ToInt32(Math.Ceiling(((double)numberOfSubscribers) / rows));
            SubscriberGrid.Columns = cols;
            SubscriberGrid.Rows = rows;
        }

        private void Session_StreamReceived(object sender, Session.StreamEventArgs e)
        {
            Trace.WriteLine("Session stream received");
            VideoRenderer renderer = new VideoRenderer();
            SubscriberGrid.Children.Add(renderer);
            UpdateGridSize(SubscriberGrid.Children.Count);
            Subscriber subscriber = new Subscriber.Builder(Context.Instance, e.Stream)
            {
                Renderer = renderer
            }.Build();
            SubscriberByStream.Add(e.Stream, subscriber);

            try
            {
                Session.Subscribe(subscriber);
            }
            catch (OpenTokException ex)
            {
                Trace.WriteLine("OpenTokException " + ex.ToString());
            }
        }

        private void Session_StreamDropped(object sender, Session.StreamEventArgs e)
        {
            Trace.WriteLine("Session stream dropped");
            var subscriber = SubscriberByStream[e.Stream];
            if (subscriber != null)
            {
                SubscriberByStream.Remove(e.Stream);
                try
                {
                    Session.Unsubscribe(subscriber);
                }
                catch (OpenTokException ex)
                {
                    Trace.WriteLine("OpenTokException " + ex.ToString());
                }

                SubscriberGrid.Children.Remove((UIElement)subscriber.VideoRenderer);
                UpdateGridSize(SubscriberGrid.Children.Count);
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (Disconnect)
            {
                Trace.WriteLine("Disconnecting session");
                try
                {
                    Session.Unpublish(Publisher);
                    Session.Disconnect();
                }
                catch (OpenTokException ex)
                {
                    Trace.WriteLine("OpenTokException " + ex.ToString());
                }
            }
            else
            {
                Trace.WriteLine("Connecting session");
                try
                {
                    Session.Connect(TOKEN);
                }
                catch (OpenTokException ex)
                {
                    Trace.WriteLine("OpenTokException " + ex.ToString());
                }
            }
            Disconnect = !Disconnect;
            ConnectDisconnectButton.Content = Disconnect ? "Disconnect" : "Connect";
        }

        private void CameraListDropDownClosed(object sender, EventArgs e)
        {
            ComboBoxPairs cameraSelected = CameraList.SelectedItem as ComboBoxPairs;
            if (cameraSelected != null)  Capturer.InitializeWebCam(cameraSelected._Value);
        }

        private void populateCameraList(Windows.Devices.Enumeration.DeviceInformationCollection devices)
        {
            List<ComboBoxPairs> device_list = new List<ComboBoxPairs>();
            foreach (var device in devices)
            {
                device_list.Add(new ComboBoxPairs(device.Name, device.Id));
            }
            CameraList.DisplayMemberPath = "_Key";
            CameraList.SelectedValuePath = "_Value";
            CameraList.ItemsSource = device_list;
        }

        //When this window is launched, Add a Handler (HwndHandler) to our USB event watcher
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Adds the windows message processing hook and registers USB device add/removal notification.
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null)
            {
                windowHandle = source.Handle;
                source.AddHook(HwndHandler);
                UsbNotification.RegisterUsbDeviceNotification(windowHandle);
            }
        }

        /// <summary>
        /// Method that receives window messages.
        /// </summary>
        private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (msg == UsbNotification.WmDevicechange)
            {
                switch ((int)wparam)
                {
                    case UsbNotification.DbtDeviceremovecomplete:
                        Capturer.getVideoDevices((devices) => {
                            Debug.WriteLine("Device Removed");
                            int i = 0;
                            foreach (var device in devices)
                            {
                                Debug.WriteLine("* Device [{0}]", i++);
                                /*Debug.WriteLine("EnclosureLocation.InDock: " + device.EnclosureLocation.InDock);
                                Debug.WriteLine("EnclosureLocation.InLid: " + device.EnclosureLocation.InLid);
                                Debug.WriteLine("EnclosureLocation.Panel: " + device.EnclosureLocation.Panel);*/
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
                            populateCameraList(devices);
                        }); 
                        break;
                    case UsbNotification.DbtDevicearrival:
                        Capturer.getVideoDevices((devices) => {
                            Debug.WriteLine("Device Attached");
                            int i = 0;
                            foreach (var device in devices)
                            {
                                Debug.WriteLine("* Device [{0}]", i++);
                                /*Debug.WriteLine("EnclosureLocation.InDock: " + device.EnclosureLocation.InDock);
                                Debug.WriteLine("EnclosureLocation.InLid: " + device.EnclosureLocation.InLid);
                                Debug.WriteLine("EnclosureLocation.Panel: " + device.EnclosureLocation.Panel);*/
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
                            populateCameraList(devices);
                        });
                        break;
                }
            }

            handled = false;
            return IntPtr.Zero;
        }
    }

    //This Class detects new hardware (USB) changes by using external DLLS
    internal static class UsbNotification
    {
        public const int DbtDevicearrival = 0x8000; // system detected a new device        
        public const int DbtDeviceremovecomplete = 0x8004; // device is gone      
        public const int WmDevicechange = 0x0219; // device change event      
        private const int DbtDevtypDeviceinterface = 5;
        private static readonly Guid GuidDevinterfaceUSBDevice = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED"); // USB devices
        private static IntPtr notificationHandle;

        /// <summary>
        /// Registers a window to receive notifications when USB devices are plugged or unplugged.
        /// </summary>
        /// <param name="windowHandle">Handle to the window receiving notifications.</param>
        public static void RegisterUsbDeviceNotification(IntPtr windowHandle)
        {
            DevBroadcastDeviceinterface dbi = new DevBroadcastDeviceinterface
            {
                DeviceType = DbtDevtypDeviceinterface,
                Reserved = 0,
                ClassGuid = GuidDevinterfaceUSBDevice,
                Name = 0
            };

            dbi.Size = Marshal.SizeOf(dbi);
            IntPtr buffer = Marshal.AllocHGlobal(dbi.Size);
            Marshal.StructureToPtr(dbi, buffer, true);

            notificationHandle = RegisterDeviceNotification(windowHandle, buffer, 0);
        }

        /// <summary>
        /// Unregisters the window for USB device notifications
        /// </summary>
        public static void UnregisterUsbDeviceNotification()
        {
            UnregisterDeviceNotification(notificationHandle);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr notificationFilter, int flags);

        [DllImport("user32.dll")]
        private static extern bool UnregisterDeviceNotification(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct DevBroadcastDeviceinterface
        {
            internal int Size;
            internal int DeviceType;
            internal int Reserved;
            internal Guid ClassGuid;
            internal short Name;
        }
    }

    public class ComboBoxPairs
    {
        public string _Key { get; set; }
        public string _Value { get; set; }

        public ComboBoxPairs(string _key, string _value)
        {
            _Key = _key;
            _Value = _value;
        }
    }

   


}