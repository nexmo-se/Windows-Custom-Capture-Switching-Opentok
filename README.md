Custom Camera Capture with Seamless Switching
=============

This project shows how to use OpenTok Windows SDK to publish a stream that uses
USB Cameras of the screen as the video source for an OpenTok publisher.

## Quick Start

1. Get values for your OpenTok **API key**, **session ID**, and **token**.

   You can obtain these values from your [TokBox account](#https://tokbox.com/account/#/).
   Make sure that the token isn't expired.

   For testing, you can use a session ID and token generated at your TokBox account page.
   However, the final application should obtain these values using the [OpenTok server
   SDKs](https://tokbox.com/developer/sdks/server/). For more information, see the OpenTok
   developer guides on [session creation](https://tokbox.com/developer/guides/create-session/)
   and [token creation](https://tokbox.com/developer/guides/create-token/).

2. In Visual Studio, open the .sln solution file for the sample app you are using
   (CustomVideoRenderer/CustomVideoRenderer.sln, ScreenSharing/ScreenSharing.sln,
   or SimpleMultiparty/SimpleMultiparty.sln).

3. Open the MainWindow.xaml.cs file for the app and edit the values for `API_KEY`, `SESSION_ID`,
   and `TOKEN` to match API key, session ID, and token data you obtained in step 1.

NuGet automatically installs the OpenTok SDK when you build the project.


CustomCameraCatpturer.cs
------------------------

This is the core class of the sample application. It captures the contents of the
USB Camera and uses the frames as the video source for an OpenTok Publisher object.

To be able to provide frames to the OpenTok SDK you need to implement the
`IVideoCapturer` interface. This is also known as building your own video Capturer.

The app returns the capture settings in the implementation of the
`IVideoCapturer.GetCaptureSettings()` method:

```csharp
public VideoCaptureSettings GetCaptureSettings()
{
    VideoCaptureSettings settings = new VideoCaptureSettings();
    settings.Width = width; // DesktopBounds.Right;
    settings.Height = height; // DesktopBounds.Bottom;
    settings.Fps = FPS; // 16
    settings.MirrorOnLocalRender = false;
    settings.PixelFormat = PixelFormat.FormatYuv420p;
    return settings;
}
```

The application implements the `Init(frameConsumer)`, `Start()`, `Stop()`, and `Destroy()` methods
defined by the `IVideoCapturer` interface of the OpenTok Windows SDK. The OpenTok SDK manages the
capturer lifecycle by calling these methods when the Publisher initializes the video capturer,
when it starts requesting frames, when it stops capturing frames, and when the capturer is
destroyed.

Note that the `Init` method contains a `frameConsumer` parameter. This object is defined by the
`IVideoFrameConsumer` interface of the OpenTok Windows SDK. The app saves that parameter value and
uses it to provide a frame to the custom video capturer for the Publisher.

Whenever a frame is ready, the app calls the `Consume(frame)` method of the `frameConsumer`
object (passing in the frame object):

```csharp
using (var frame = VideoFrame.CreateYuv420pFrameFromBuffer(PixelFormat.FormatArgb32,
    width, height,
    planes, strides)) // planes and strides the actual frame data
{
    frameConsumer.Consume(frame);
}
```

#### Capturing the Camera

This sample UWP Media Capture class to capture the screen contents. To create
the video, the app Initializes the camera using MediaCapture class. It then uses MediaFrameReader to
get the frames from Media capture and pass the bmp frame to `frameConsumer.Consume(frame)`.

### Detecting Hardware Changes

Inside `MainWindow.xaml.cs`, we use the `UsbNotification` function to include two external functions from `user32.dll`.
These functions are `RegisterDeviceNotification` and `UnregisterDeviceNotification`. 
These functions requires a callback parameter that handles the Hardware changes.

Once we detect USB hardware change, we call `CustomCameraCapturer().getVideoDevices()` to query for vide capture devices
and populate out list of cameras.

#### Switching between Cameras

To Switch cameras, simply get the camera's deviceID and pass it to `CustomCameraCapturer().InitializeWebCam(deviceID)`
We get the camera's deviceID from the data in our camera list.

MainWindow.xaml.cs
------------------

To use the capturer, pass it in as the `capturer` parameter of the `Publisher()` constructor:

```csharp
Capturer = new CustomCameraCatpturer();

Publisher = new Publisher(Context.Instance, 
  renderer: PublisherVideo,
  capturer: Capturer,
  hasAudioTrack: false)
```
