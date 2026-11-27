#if IOS || MACCATALYST
using AVFoundation;
using CommunityToolkit.Uno.Core.Primitives;
using CommunityToolkit.Uno.Extensions;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using ObjCRuntime;
using System.Diagnostics;
using UIKit;
using Windows.Foundation;
using ZXing.Net.Uno;

namespace CommunityToolkit.Uno.Core;


partial class CameraManager
{
    // TODO: Check if we really need this
    readonly NSDictionary<NSString, NSObject> codecSettings = new([AVVideo.CodecKey], [new NSString("jpeg")]);
    AVCaptureDeviceInput? _audioInput;
    AVCaptureSession? _captureSession;
    AVCapturePhotoOutput? _photoOutput;
    AVCaptureInput? _captureInput;
    AVCaptureDevice? _captureDevice;
    AVCaptureVideoDataOutput? _videoDataOutput;
    CaptureDelegate _captureDelegate;
    DispatchQueue _dispatchQueue;

    AVCaptureFlashMode _flashMode;

    IDisposable? _orientationDidChangeObserver;
    PreviewView? _previewView;

    AVCaptureDeviceInput? _videoInput;
    AVCaptureVideoOrientation _videoOrientation;
    AVCaptureMovieFileOutput? _videoOutput;
    string? _videoRecordingFileName;
    TaskCompletionSource? _videoRecordingFinalizeTcs;
    Stream? _videoRecordingStream;

    /// <inheritdoc />
    public void Dispose()
    {
        CleanupVideoRecordingResources();

        _captureSession?.StopRunning();
        _captureSession?.Dispose();
        _captureSession = null;

        _captureInput?.Dispose();
        _captureInput = null;

        _captureDevice = null;

        _orientationDidChangeObserver?.Dispose();
        _orientationDidChangeObserver = null;

        _photoOutput?.Dispose();
        _photoOutput = null;

        _previewView?.Dispose();
        _previewView = null;

        _videoRecordingStream?.Dispose();
        _videoRecordingStream = null;
    }

    public NativePlatformCameraPreviewView CreatePlatformView()
    {
        _captureSession = new AVCaptureSession
        {
            SessionPreset = AVCaptureSession.PresetPhoto
        };

        _previewView = new PreviewView
        {
            Session = _captureSession
        };

        _orientationDidChangeObserver = UIDevice.Notifications.ObserveOrientationDidChange((_, _) => UpdateVideoOrientation());
        UpdateVideoOrientation();

        return _previewView;
    }

    public partial void UpdateFlashMode(CameraFlashMode flashMode)
    {
        this._flashMode = flashMode.ToPlatform();
    }

    public partial void UpdateZoom(float zoomLevel)
    {
        if (!IsInitialized || _captureDevice is null)
        {
            return;
        }

        if (zoomLevel < (float)_captureDevice.MinAvailableVideoZoomFactor || zoomLevel > (float)_captureDevice.MaxAvailableVideoZoomFactor)
        {
            return;
        }

        _captureDevice.LockForConfiguration(out NSError? error);
        if (error is not null)
        {
            Trace.WriteLine(error);
            return;
        }

        _captureDevice.VideoZoomFactor = zoomLevel;
        _captureDevice.UnlockForConfiguration();
    }

    public partial ValueTask UpdateCaptureResolution(Size resolution, CancellationToken token)
    {
        if (_cameraView.SelectedCamera is null)
        {
            throw new CameraException($"Unable to update Capture Resolution because {nameof(ICameraControl)}.{nameof(ICameraControl.SelectedCamera)} is null.");
        }

        if (_captureDevice is null)
        {
            return ValueTask.CompletedTask;
        }

        _captureDevice.LockForConfiguration(out NSError? error);
        if (error is not null)
        {
            Trace.WriteLine(error);
            return ValueTask.CompletedTask;
        }

        var screenScale = (double)(_previewView?.Window?.Screen?.Scale ?? UIScreen.MainScreen.Scale);
        var targetResolution = new Size(resolution.Width * screenScale, resolution.Height * screenScale);
        var isPortrait = GetVideoOrientation() is AVCaptureVideoOrientation.Portrait or AVCaptureVideoOrientation.PortraitUpsideDown;

        var formatsMatchingResolution = _cameraView.SelectedCamera.SupportedFormats
            .Where(format => MatchesResolution(format, targetResolution, isPortrait))
            .ToList();

        var availableFormats = formatsMatchingResolution.Count is not 0
            ? formatsMatchingResolution
            : GetPhotoCompatibleFormats(_cameraView.SelectedCamera.SupportedFormats);

        var selectedFormat = availableFormats
            .OrderByDescending(f => f.ResolutionArea)
            .FirstOrDefault();

        if (selectedFormat is not null)
        {
            _captureDevice.ActiveFormat = selectedFormat;
        }

        _captureDevice.UnlockForConfiguration();
        return ValueTask.CompletedTask;
    }

    private async partial Task PlatformConnectCamera(CancellationToken token)
    {
        await PlatformStartCameraPreview(token);
    }

    private async partial Task PlatformStartCameraPreview(CancellationToken token)
    {
        if (_captureSession is null)
        {
            return;
        }

        _captureSession.BeginConfiguration();

        foreach (var input in _captureSession.Inputs)
        {
            _captureSession.RemoveInput(input);
            input.Dispose();
        }

        // Prioritize cameras suitable for barcode scanning
        AVCaptureDevice selectedDevice = null;
        AVCaptureDevice fallbackDevice = null;

        foreach (var device in _cameraProvider.AvailableCameras)
        {
            // Skip depth-only cameras (TrueDepth, LiDAR) as they're not suitable for barcode scanning
            if (device.CaptureDevice.DeviceType == AVCaptureDeviceType.BuiltInTrueDepthCamera ||
                device.CaptureDevice.DeviceType == AVCaptureDeviceType.BuiltInLiDarDepthCamera)
                continue;

            var isCorrectPosition = device.CaptureDevice.Position == AVCaptureDevicePosition.Front ||
                                    device.CaptureDevice.Position == AVCaptureDevicePosition.Back;

            if (isCorrectPosition)
            {
                // Prefer multi-camera systems (Dual, Triple, DualWide) - these are the main cameras on modern iPhones
                if (device.CaptureDevice.DeviceType == AVCaptureDeviceType.BuiltInDualCamera ||
                    device.CaptureDevice.DeviceType == AVCaptureDeviceType.BuiltInTripleCamera ||
                    device.CaptureDevice.DeviceType == AVCaptureDeviceType.BuiltInDualWideCamera)
                {
                    selectedDevice = device.CaptureDevice;
                    break; // Multi-camera systems are ideal for barcode scanning
                }
                // Wide-angle is a good standard camera
                else if (device.CaptureDevice.DeviceType == AVCaptureDeviceType.BuiltInWideAngleCamera && selectedDevice == null)
                {
                    selectedDevice = device.CaptureDevice;
                }
                // Avoid ultra-wide and telephoto, but keep as last resort fallback
                else if (fallbackDevice == null)
                {
                    fallbackDevice = device.CaptureDevice;
                }
            }
        }

        // Use selected device, or fallback if nothing better was found
        _captureDevice = selectedDevice ?? fallbackDevice;

        if (_captureDevice == null)
        {
            _cameraView.SelectedCamera ??= _cameraProvider.AvailableCameras?.FirstOrDefault() ?? throw new CameraException("No camera available on device");

            _captureDevice = _cameraView.SelectedCamera.CaptureDevice ?? throw new CameraException($"No Camera found");
        }

        _captureInput = new AVCaptureDeviceInput(_captureDevice, out _);
        _captureSession.AddInput(_captureInput);

        if (_photoOutput is null)
        {
            _photoOutput = new AVCapturePhotoOutput();
            _captureSession.AddOutput(_photoOutput);
        }

        if (AnalyseImages)
        {
            if (_videoDataOutput == null)
            {
                _videoDataOutput = new AVCaptureVideoDataOutput();

                var videoSettings = NSDictionary.FromObjectAndKey(
                    new NSNumber((int)CVPixelFormatType.CV32BGRA),
                    CVPixelBuffer.PixelFormatTypeKey);

                _videoDataOutput.WeakVideoSettings = videoSettings;

                if (_captureDelegate == null)
                {
                    _captureDelegate = new CaptureDelegate
                    {
                        SampleProcessor = cvPixelBuffer =>
                        {
                            // analyse only every _vidioFrameDivider value
                            if (_videoFrameCounter % VidioFrameDivider == 0)
                            {
                                FrameReady?.Invoke(this, new CameraFrameBufferEventArgs(new ZXing.Net.Uno.Readers.PixelBufferHolder
                                {
                                    Data = cvPixelBuffer,
                                    Size = new Size(cvPixelBuffer.Width, cvPixelBuffer.Height)
                                }));
                            }
                            _videoFrameCounter++;
                        }
                    };
                }

                if (_dispatchQueue == null)
                    _dispatchQueue = new DispatchQueue("CameraBufferQueue");

                _videoDataOutput.AlwaysDiscardsLateVideoFrames = true;
                _videoDataOutput.SetSampleBufferDelegate(_captureDelegate, _dispatchQueue);
            }

            _captureSession.AddOutput(_videoDataOutput);
        }


        await UpdateCaptureResolution(_cameraView.ImageCaptureResolution, token);

        _captureSession.CommitConfiguration();
        _captureSession.StartRunning();
        IsInitialized = true;
        OnLoaded.Invoke();
    }

    private partial void PlatformStopCameraPreview()
    {
        if (_captureSession is null)
        {
            return;
        }

        if (_captureSession.Running)
        {
            _captureSession.StopRunning();
        }

        IsInitialized = false;
    }

    private partial void PlatformDisconnect()
    {
    }

    private async partial Task PlatformStartVideoRecording(Stream stream, CancellationToken token)
    {
        var isPermissionGranted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video).WaitAsync(token);
        if (!isPermissionGranted)
        {
            throw new CameraException("Camera permission is not granted. Please enable it in the app settings.");
        }

        if (_captureSession is null)
        {
            throw new CameraException("Capture session is not initialized. Call ConnectCamera first.");
        }

        CleanupVideoRecordingResources();

        var videoDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) ?? throw new CameraException("Unable to get video device");

        _videoInput = new AVCaptureDeviceInput(videoDevice, out NSError? error);
        if (error is not null)
        {
            throw new CameraException($"Error creating video input: {error.LocalizedDescription}");
        }

        if (!_captureSession.CanAddInput(_videoInput))
        {
            _videoInput?.Dispose();
            throw new CameraException("Unable to add video input to capture session.");
        }

        _captureSession.BeginConfiguration();
        _captureSession.AddInput(_videoInput);

        try
        {
            var audioDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Audio);
            if (audioDevice is not null)
            {
                _audioInput = new AVCaptureDeviceInput(audioDevice, out NSError? audioError);
                if (audioError is null && _captureSession.CanAddInput(_audioInput))
                {
                    _captureSession.AddInput(_audioInput);
                }
                else
                {
                    _audioInput?.Dispose();
                    _audioInput = null;
                }
            }
        }
        catch
        {
            // Ignore audio configuration issues; proceed with video-only recording
        }

        _videoOutput = new AVCaptureMovieFileOutput();

        if (!_captureSession.CanAddOutput(_videoOutput))
        {
            _captureSession.RemoveInput(_videoInput);
            if (_audioInput is not null)
            {
                _captureSession.RemoveInput(_audioInput);
                _audioInput.Dispose();
                _audioInput = null;
            }

            _videoInput?.Dispose();
            _videoOutput?.Dispose();
            _captureSession.CommitConfiguration();
            throw new CameraException("Unable to add video output to capture session.");
        }

        _captureSession.AddOutput(_videoOutput);
        _captureSession.CommitConfiguration();

        _videoRecordingStream = stream;
        _videoRecordingFinalizeTcs = new TaskCompletionSource();
        _videoRecordingFileName = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mov");

        var outputUrl = NSUrl.FromFilename(_videoRecordingFileName);
        _videoOutput.StartRecordingToOutputFile(outputUrl, new AVCaptureMovieFileOutputRecordingDelegate(_videoRecordingFinalizeTcs));
    }

    private async partial Task<Stream> PlatformStopVideoRecording(CancellationToken token)
    {
        if (_captureSession is null
            || _videoRecordingFileName is null
            || _videoInput is null
            || _videoOutput is null
            || _videoRecordingStream is null
            || _videoRecordingFinalizeTcs is null)
        {
            return Stream.Null;
        }

        _videoOutput.StopRecording();
        await _videoRecordingFinalizeTcs.Task.WaitAsync(token);

        if (File.Exists(_videoRecordingFileName))
        {
            await using var inputStream = new FileStream(_videoRecordingFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            await inputStream.CopyToAsync(_videoRecordingStream, token);
            await _videoRecordingStream.FlushAsync(token);
            if (_videoRecordingStream.CanSeek)
            {
                _videoRecordingStream.Position = 0;
            }
        }

        CleanupVideoRecordingResources();

        return _videoRecordingStream;
    }

    void CleanupVideoRecordingResources()
    {
        if (_captureSession is not null)
        {
            _captureSession.BeginConfiguration();

            foreach (var input in _captureSession.Inputs)
            {
                _captureSession.RemoveInput(input);
                input.Dispose();
            }

            foreach (var output in _captureSession.Outputs)
            {
                _captureSession.RemoveOutput(output);
                output.Dispose();
            }

            // Restore to photo preset for preview after video recording
            _captureSession.SessionPreset = AVCaptureSession.PresetPhoto;
            _captureSession.CommitConfiguration();
        }

        _videoOutput = null;
        _videoInput = null;
        _audioInput = null;

        // Clean up temporary file
        if (_videoRecordingFileName is not null)
        {
            if (File.Exists(_videoRecordingFileName))
            {
                File.Delete(_videoRecordingFileName);
            }

            _videoRecordingFileName = null;
        }

        _videoRecordingFinalizeTcs = null;
    }

    private async partial ValueTask PlatformTakePicture(CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(_photoOutput);

        var capturePhotoSettings = AVCapturePhotoSettings.FromFormat(codecSettings);
        capturePhotoSettings.FlashMode = _photoOutput.SupportedFlashModes.Contains(_flashMode) ? _flashMode : _photoOutput.SupportedFlashModes.First();

        if (AVMediaTypes.Video.GetConstant() is NSString avMediaTypeVideo)
        {
            var photoOutputConnection = _photoOutput.ConnectionFromMediaType(avMediaTypeVideo);
            if (photoOutputConnection is not null)
            {
                photoOutputConnection.VideoOrientation = _videoOrientation;
            }
        }

        var wrapper = new AVCapturePhotoCaptureDelegateWrapper();

        _photoOutput.CapturePhoto(capturePhotoSettings, wrapper);

        var result = await wrapper.Task.WaitAsync(token);
        if (result.Error is not null)
        {
            var failureReason = result.Error.LocalizedDescription;
            if (!string.IsNullOrEmpty(result.Error.LocalizedFailureReason))
            {
                failureReason = $"{failureReason} - {result.Error.LocalizedFailureReason}";
            }

            _cameraView.OnMediaCapturedFailed(failureReason);
            return;
        }

        Stream? imageData;
        try
        {
            imageData = result.Photo.FileDataRepresentation?.AsStream();
        }
        catch (Exception e)
        {
            // possible exception: ObjCException NSInvalidArgumentException NSAllocateMemoryPages(...) failed in AVCapturePhoto.get_FileDataRepresentation()
            _cameraView.OnMediaCapturedFailed($"Unable to retrieve the file data representation from the captured result: {e.Message}");
            return;
        }

        if (imageData is null)
        {
            _cameraView.OnMediaCapturedFailed("Unable to retrieve the file data representation from the captured result.");
        }
        else
        {
            _cameraView.OnMediaCaptured(imageData);
        }
    }

    static AVCaptureVideoOrientation GetVideoOrientation()
    {
        IEnumerable<UIScene> scenes = UIApplication.SharedApplication.ConnectedScenes;

        UIInterfaceOrientation interfaceOrientation;
        if (!(OperatingSystem.IsMacCatalystVersionAtLeast(26) || OperatingSystem.IsIOSVersionAtLeast(26)))
        {
            interfaceOrientation = scenes.FirstOrDefault() is UIWindowScene windowScene
                ? windowScene.InterfaceOrientation
                : UIApplication.SharedApplication.StatusBarOrientation;
        }
        else
        {
            interfaceOrientation = scenes.FirstOrDefault() is UIWindowScene windowScene
                ? windowScene.EffectiveGeometry.InterfaceOrientation
                : UIApplication.SharedApplication.StatusBarOrientation;
        }

        return interfaceOrientation switch
        {
            UIInterfaceOrientation.Portrait => AVCaptureVideoOrientation.Portrait,
            UIInterfaceOrientation.PortraitUpsideDown => AVCaptureVideoOrientation.PortraitUpsideDown,
            UIInterfaceOrientation.LandscapeRight => AVCaptureVideoOrientation.LandscapeRight,
            UIInterfaceOrientation.LandscapeLeft => AVCaptureVideoOrientation.LandscapeLeft,
            _ => AVCaptureVideoOrientation.Portrait
        };
    }

    void UpdateVideoOrientation()
    {
        _videoOrientation = GetVideoOrientation();
        _previewView?.UpdatePreviewVideoOrientation(_videoOrientation);
    }

    IEnumerable<AVCaptureDeviceFormat> GetPhotoCompatibleFormats(IEnumerable<AVCaptureDeviceFormat> formats)
    {
        if (_photoOutput is not null)
        {
            var photoPixelFormats = _photoOutput.GetSupportedPhotoPixelFormatTypesForFileType(nameof(AVFileTypes.Jpeg));
            return formats.Where(format => photoPixelFormats.Contains((NSNumber)format.FormatDescription.MediaSubType));
        }

        return formats;
    }

    static bool MatchesResolution(AVCaptureDeviceFormat format, Size resolution, bool isPortrait)
    {
        var dimensions = ((CMVideoFormatDescription)format.FormatDescription).Dimensions;
        var width = isPortrait ? dimensions.Height : dimensions.Width;
        var height = isPortrait ? dimensions.Width : dimensions.Height;
        return width <= resolution.Width
               && height <= resolution.Height;
    }

    sealed class AVCapturePhotoCaptureDelegateWrapper : AVCapturePhotoCaptureDelegate
    {
        readonly TaskCompletionSource<CapturePhotoResult> taskCompletionSource = new();

        public Task<CapturePhotoResult> Task =>
            taskCompletionSource.Task;

        public override void DidFinishProcessingPhoto(AVCapturePhotoOutput output, AVCapturePhoto photo, NSError? error)
        {
            taskCompletionSource.TrySetResult(new()
            {
                Output = output,
                Photo = photo,
                Error = error
            });
        }
    }

    sealed record CapturePhotoResult
    {
        public required AVCapturePhotoOutput Output { get; init; }

        public required AVCapturePhoto Photo { get; init; }

        public NSError? Error { get; init; }
    }

    sealed partial class PreviewView : NativePlatformCameraPreviewView
    {
        public PreviewView()
        {
            PreviewLayer.VideoGravity = AVLayerVideoGravity.ResizeAspectFill;
        }

        public AVCaptureSession? Session
        {
            get => PreviewLayer.Session;
            set => PreviewLayer.Session = value;
        }

        AVCaptureVideoPreviewLayer PreviewLayer => (AVCaptureVideoPreviewLayer)Layer;

        [Export("layerClass")]
        public static Class GetLayerClass()
        {
            return new Class(typeof(AVCaptureVideoPreviewLayer));
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            UpdatePreviewVideoOrientation(GetVideoOrientation());
        }

        public void UpdatePreviewVideoOrientation(AVCaptureVideoOrientation videoOrientation)
        {
            if (PreviewLayer.Connection is not null)
            {
#if IOS || MACCATALYST
#if (IOS && !MACCATALYST)
                // iOS 17+ uses VideoRotationAngle, older uses VideoOrientation
                if (OperatingSystem.IsIOSVersionAtLeast(17, 0))
                {
                    PreviewLayer.Connection.VideoRotationAngle = videoOrientation switch
                    {
                        AVCaptureVideoOrientation.Portrait => 0f,
                        AVCaptureVideoOrientation.LandscapeRight => 90f,
                        AVCaptureVideoOrientation.PortraitUpsideDown => 180f,
                        AVCaptureVideoOrientation.LandscapeLeft => 270f,
                        _ => 0f
                    };
                }
                else
                {
#pragma warning disable CA1422 // Suppress obsolete warning for older iOS
                    PreviewLayer.Connection.VideoOrientation = videoOrientation;
#pragma warning restore CA1422
                }
#else
				// MacCatalyst 17+ uses VideoRotationAngle, older uses VideoOrientation
				if (OperatingSystem.IsMacCatalystVersionAtLeast(17, 0))
				{
					PreviewLayer.Connection.VideoRotationAngle = videoOrientation switch
					{
						AVCaptureVideoOrientation.Portrait => 0f,
						AVCaptureVideoOrientation.LandscapeRight => 90f,
						AVCaptureVideoOrientation.PortraitUpsideDown => 180f,
						AVCaptureVideoOrientation.LandscapeLeft => 270f,
						_ => 0f
					};
				}
				else
				{
#pragma warning disable CA1422 // Suppress obsolete warning for older MacCatalyst
					PreviewLayer.Connection.VideoOrientation = videoOrientation;
#pragma warning restore CA1422
				}
#endif
#endif
            }
        }
    }
}

class AVCaptureMovieFileOutputRecordingDelegate(TaskCompletionSource taskCompletionSource) : AVCaptureFileOutputRecordingDelegate
{
    public override void FinishedRecording(AVCaptureFileOutput captureOutput, NSUrl outputFileUrl, NSObject[] connections, NSError? error)
    {
        taskCompletionSource.SetResult();
    }
}
#endif