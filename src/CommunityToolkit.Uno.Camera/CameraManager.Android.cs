#if __ANDROID__
using Android.Content;
using Android.Runtime;
using Android.Views;
using AndroidX.Camera.Core;
using AndroidX.Camera.Core.ResolutionSelector;
using AndroidX.Camera.Extensions;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.Video;
using AndroidX.Core.Content;
using AndroidX.Core.Util;
using AndroidX.Lifecycle;
using CommunityToolkit.Uno.Core.Primitives;
using CommunityToolkit.Uno.Extensions;
using Java.Lang;
using Java.Util.Concurrent;
using System.Runtime.Versioning;
using Windows.Foundation;
using ZXing.Net.Uno;
using Image = Android.Media.Image;
using Math = System.Math;
using Object = Java.Lang.Object;

namespace CommunityToolkit.Uno.Core;

[SupportedOSPlatform("android21.0")]
partial class CameraManager
{
    readonly Context context = ContextHelper.Current ?? throw new CameraException($"Unable to retrieve {nameof(Context)}");

    NativePlatformCameraPreviewView? _previewView;
    IExecutorService? _cameraExecutor;
    ProcessCameraProvider? _processCameraProvider;
    ImageCapture? _imageCapture;
    ImageCallBack? _imageCallback;
    ImageAnalysis _imageAnalyzer;
    VideoCapture? _videoCapture;
    Recorder? _videoRecorder;
    Recording? _videoRecording;
    ICamera? _camera;
    AndroidX.Camera.Core.ICameraControl? _cameraControl;
    Preview? _cameraPreview;
    ResolutionSelector? _resolutionSelector;
    ResolutionFilter? _resolutionFilter;
    OrientationListener? _orientationListener;
    int _targetRotation = (int)SurfaceOrientation.Rotation0;
    Java.IO.File? _videoRecordingFile;
    TaskCompletionSource? videoRecordingFinalizeTcs;
    Stream? videoRecordingStream;
    int extensionMode = ExtensionMode.Auto;

    public async Task SetExtensionMode(int mode, CancellationToken token)
    {
        extensionMode = mode;
        if (_cameraView.SelectedCamera is null
            || _processCameraProvider is null
            || _cameraPreview is null
            || _imageCapture is null
            || _videoCapture is null)
        {
            return;
        }

        _camera = await RebindCamera(_processCameraProvider, _cameraView.SelectedCamera, token, _cameraPreview, _imageCapture, _videoCapture);

        _cameraControl = _camera.CameraControl;
    }

    public void Dispose()
    {
        CleanupVideoRecordingResources();

        _camera?.Dispose();
        _camera = null;

        _cameraControl?.Dispose();
        _cameraControl = null;

        _cameraPreview?.Dispose();
        _cameraPreview = null;

        _cameraExecutor?.Dispose();
        _cameraExecutor = null;

        _imageCapture?.Dispose();
        _imageCapture = null;

        _videoCapture?.Dispose();
        _videoCapture = null;

        _imageCallback?.Dispose();
        _imageCallback = null;

        _previewView?.Dispose();
        _previewView = null;

        _processCameraProvider?.UnbindAll();
        _processCameraProvider?.Dispose();
        _processCameraProvider = null;

        _resolutionSelector?.Dispose();
        _resolutionSelector = null;

        _resolutionFilter?.Dispose();
        _resolutionFilter = null;

        _orientationListener?.Disable();
        _orientationListener?.Dispose();
        _orientationListener = null;

        videoRecordingStream?.Dispose();
        videoRecordingStream = null;
    }

    // IN the future change the return type to be an alias
    public NativePlatformCameraPreviewView CreatePlatformView()
    {
        _imageCallback = new ImageCallBack(_cameraView);
        _previewView = new NativePlatformCameraPreviewView(context);
        if (NativePlatformCameraPreviewView.ScaleType.FillCenter is not null)
        {
            _previewView.SetScaleType(NativePlatformCameraPreviewView.ScaleType.FillCenter);
        }
        _previewView.SetImplementationMode(NativePlatformCameraPreviewView.ImplementationMode.Compatible);

        _cameraExecutor = Executors.NewSingleThreadExecutor() ?? throw new CameraException($"Unable to retrieve {nameof(IExecutorService)}");
        _orientationListener = new OrientationListener(SetImageCaptureTargetRotation, context);
        _orientationListener.Enable();

        return _previewView;
    }

    public partial void UpdateFlashMode(CameraFlashMode flashMode)
    {
        if (_imageCapture is null)
        {
            return;
        }

        _imageCapture.FlashMode = flashMode.ToPlatform();
    }

    public partial void UpdateZoom(float zoomLevel)
    {
        _cameraControl?.SetZoomRatio(zoomLevel);
    }

    public async partial ValueTask UpdateCaptureResolution(Size resolution, CancellationToken token)
    {
        var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        var targetSize = new Android.Util.Size(
            (int)Math.Round(resolution.Width * density),
            (int)Math.Round(resolution.Height * density));

        if (_resolutionFilter is not null &&
            _resolutionFilter.TargetSize.Width == targetSize.Width &&
            _resolutionFilter.TargetSize.Height == targetSize.Height)
        {
            return;
        }

        if (_resolutionFilter is null)
        {
            _resolutionFilter = new ResolutionFilter(targetSize);
        }
        else
        {
            _resolutionFilter.TargetSize = targetSize;
        }

        _resolutionSelector?.Dispose();

        _resolutionSelector = new ResolutionSelector.Builder()
            .SetAllowedResolutionMode(ResolutionSelector.PreferHigherResolutionOverCaptureRate)?
            .SetResolutionFilter(_resolutionFilter)
            ?.Build() ?? throw new InvalidOperationException("Unable to Set Resolution Filter");

        // `.SetResolutionFilter()` should never return null
        // According to the Android docs, `ResolutionSelector.Builder.setResolutionFilter(ResolutionFilter)` returns a `NonNull` object
        // `ResolutionSelector.Builder.SetResolutionFilter(ResolutionFilter)` returning a nullable object in .NET for Android is likely a C# Binding mistake

        if (IsInitialized)
        {
            await StartUseCase(token);
        }
    }

    private async partial Task PlatformConnectCamera(CancellationToken token)
    {
        var cameraProviderFuture = ProcessCameraProvider.GetInstance(context);
        if (_previewView is null)
        {
            return;
        }

        var cameraProviderTCS = new TaskCompletionSource();

        cameraProviderFuture.AddListener(new Runnable(async () =>
        {
            _processCameraProvider = (ProcessCameraProvider)(cameraProviderFuture.Get() ?? throw new CameraException($"Unable to retrieve {nameof(ProcessCameraProvider)}"));

            await StartUseCase(token);

            cameraProviderTCS.SetResult();
        }), ContextCompat.GetMainExecutor(context));

        await cameraProviderTCS.Task.WaitAsync(token);
    }

    async Task StartUseCase(CancellationToken token)
    {
        if (_resolutionSelector is null || _cameraExecutor is null)
        {
            return;
        }

        PlatformStopCameraPreview();

        _cameraPreview?.Dispose();
        _imageCapture?.Dispose();

        _videoCapture?.Dispose();
        _videoRecorder?.Dispose();

        _cameraPreview = new Preview.Builder().SetResolutionSelector(_resolutionSelector)?.Build();
        if (_cameraPreview is not null)
        {
            _cameraPreview.TargetRotation = _targetRotation;
        }
        _cameraPreview?.SetSurfaceProvider(_cameraExecutor, _previewView?.SurfaceProvider);

        _imageCapture = new ImageCapture.Builder()
            .SetCaptureMode(ImageCapture.CaptureModeMaximizeQuality)?
            .SetResolutionSelector(_resolutionSelector)
            ?.Build() ?? throw new InvalidOperationException("Unable to set resolution selector");
        _imageCapture.TargetRotation = _targetRotation;

        // `.SetResolutionFilter()` should never return null
        // According to the Android docs, `ResolutionSelector.Builder.SetResolutionFilter(ResolutionFilter)` returns a `NonNull` object
        // `ResolutionSelector.Builder.SetResolutionFilter(ResolutionFilter)` returning a nullable object in .NET for Android is likely a C# Binding mistake
        // https://developer.android.com/reference/androidx/camera/core/resolutionselector/ResolutionSelector.Builder#setResolutionFilter(androidx.camera.core.resolutionselector.ResolutionFilter)

        var videoRecorderBuilder = new Recorder.Builder()
            .SetExecutor(_cameraExecutor) ?? throw new InvalidOperationException("Unable to set video recorder executor");

        // `.SetExecutor()` should never return null
        // According to the Android docs, `ResolutionSelector.Builder.setExecutor(ResolutionFilter)` returns a `NonNull` object
        // `ResolutionSelector.Builder.SetExecutor(ResolutionFilter)` returning a nullable object in .NET for Android is likely a C# Binding mistake
        // https://developer.android.com/reference/androidx/camera/video/Recorder.Builder#setExecutor(java.util.concurrent.Executor)

        if (Quality.Highest is not null)
        {
            videoRecorderBuilder = videoRecorderBuilder.SetQualitySelector(QualitySelector.From(Quality.Highest));
        }

        _videoRecorder = videoRecorderBuilder.Build();
        _videoCapture = VideoCapture.WithOutput(_videoRecorder);

        // If we are analyzing images, we need to set up the image analyzer
        if (AnalyseImages)
        {
            // Frame by frame analyze
            _imageAnalyzer = new ImageAnalysis.Builder()
                .SetResolutionSelector(_resolutionSelector)
                .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)
                .Build();

            _imageAnalyzer.SetAnalyzer(_cameraExecutor, new FrameAnalyzer((buffer, size) =>
            {
                var divider = EnableAdaptiveThrottling
                    ? AdaptiveFrameDivider.Calculate(EstimatedFps, (int)size.Width, (int)size.Height)
                    : VidioFrameDivider;

                if (_videoFrameCounter % divider == 0)
                {
                    FrameReady?.Invoke(this, new CameraFrameBufferEventArgs(
                        new ZXing.Net.Uno.Readers.PixelBufferHolder { Data = buffer, Size = size }));
                }
                _videoFrameCounter++;
            }));

        }

        await StartCameraPreview(token);
    }

    private async partial Task PlatformStartCameraPreview(CancellationToken token)
    {
        if (_previewView is null || _processCameraProvider is null || _cameraPreview is null || _imageCapture is null || _videoCapture is null)
        {
            return;
        }

        _cameraView.SelectedCamera ??= _cameraProvider.AvailableCameras?.FirstOrDefault() ?? throw new CameraException("No camera available on device");

        if (AnalyseImages)
        {
            _camera = await RebindCamera(_processCameraProvider, _cameraView.SelectedCamera, token, _cameraPreview, _imageAnalyzer, _imageCapture, _videoCapture);
        }
        else
        {
            _camera = await RebindCamera(_processCameraProvider, _cameraView.SelectedCamera, token, _cameraPreview, _imageCapture, _videoCapture);
        }

        _cameraControl = _camera.CameraControl;

        var point = _previewView.MeteringPointFactory.CreatePoint(_previewView.Width / 2.0f, _previewView.Height / 2.0f, 0.1f);
        var action = new FocusMeteringAction.Builder(point).Build();
        _camera.CameraControl?.StartFocusAndMetering(action);

        IsInitialized = true;
        OnLoaded.Invoke();
    }

    private partial void PlatformStopCameraPreview()
    {
        if (_processCameraProvider is null)
        {
            return;
        }

        _processCameraProvider.UnbindAll();
        IsInitialized = false;
    }

    private partial void PlatformDisconnect()
    {
    }

    private partial ValueTask PlatformTakePicture(CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(_cameraExecutor);
        ArgumentNullException.ThrowIfNull(_imageCallback);

        _imageCapture?.TakePicture(_cameraExecutor, _imageCallback);
        return ValueTask.CompletedTask;
    }

    private async partial Task PlatformStartVideoRecording(Stream stream, CancellationToken token)
    {
        if (_previewView is null
            || _processCameraProvider is null
            || _cameraPreview is null
            || _imageCapture is null
            || _videoCapture is null
            || _videoRecorder is null
            || _videoRecordingFile is not null)
        {
            return;
        }

        videoRecordingStream = stream;

        _cameraView.SelectedCamera ??= _cameraProvider.AvailableCameras?.FirstOrDefault() ?? throw new CameraException("No camera available on device");

        if (_camera is null || !IsVideoCaptureAlreadyBound())
        {
            _camera = await RebindCamera(_processCameraProvider, _cameraView.SelectedCamera, token, _cameraPreview, _imageCapture, _videoCapture);
            _cameraControl = _camera.CameraControl;
        }

        _videoRecordingFile = new Java.IO.File(context.CacheDir, $"{DateTime.UtcNow.Ticks}.mp4");
        _videoRecordingFile.CreateNewFile();

        var outputOptions = new FileOutputOptions.Builder(_videoRecordingFile).Build();

        videoRecordingFinalizeTcs = new TaskCompletionSource();
        var captureListener = new CameraConsumer(videoRecordingFinalizeTcs);
        var executor = ContextCompat.GetMainExecutor(context) ?? throw new CameraException($"Unable to retrieve {nameof(IExecutorService)}");
        _videoRecording = _videoRecorder
            .PrepareRecording(context, outputOptions)
            ?.WithAudioEnabled()
            .Start(executor, captureListener) ?? throw new InvalidOperationException("Unable to prepare recording");

        // `.PrepareRecording()` should never return null
        // According to the Android docs, `Recorder.prepareRecording(Context, eMediaSoreOutputOptions)` returns a `NonNull` object
        // `Recorder.PrepareRecording(Context, eMediaSoreOutputOptions)` returning a nullable object in .NET for Android is likely a C# Binding mistake
        // https://developer.android.com/reference/androidx/camera/video/Recorder#prepareRecording(android.content.Context,androidx.camera.video.MediaStoreOutputOptions)
    }

    private async partial Task<Stream> PlatformStopVideoRecording(CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(_cameraExecutor);
        if (_videoRecording is null
            || _videoRecordingFile is null
            || videoRecordingFinalizeTcs is null
            || videoRecordingStream is null)
        {
            return Stream.Null;
        }

        _videoRecording.Stop();
        await videoRecordingFinalizeTcs.Task.WaitAsync(token);

        await using var inputStream = new FileStream(_videoRecordingFile.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await inputStream.CopyToAsync(videoRecordingStream, token);
        await videoRecordingStream.FlushAsync(token);
        CleanupVideoRecordingResources();

        return videoRecordingStream;
    }

    bool IsVideoCaptureAlreadyBound()
    {
        return _processCameraProvider is not null
               && _videoCapture is not null
               && _processCameraProvider.IsBound(_videoCapture);
    }

    void CleanupVideoRecordingResources()
    {
        _videoRecording?.Dispose();
        _videoRecording = null;

        if (_videoRecordingFile is not null)
        {
            if (_videoRecordingFile.Exists())
            {
                _videoRecordingFile.Delete();
            }

            _videoRecordingFile.Dispose();
            _videoRecordingFile = null;
        }

        _videoRecorder?.Dispose();
        _videoRecorder = null;

        _videoCapture?.Dispose();
        _videoCapture = null;

        videoRecordingFinalizeTcs = null;
    }

    async Task<CameraSelector> EnableModes(CameraInfo selectedCamera, CancellationToken token)
    {
        var cameraFutureCts = new TaskCompletionSource();
        var cameraSelector = selectedCamera.CameraSelector ?? throw new CameraException($"Unable to retrieve {nameof(CameraSelector)}");
        var cameraProviderFuture = ProcessCameraProvider.GetInstance(context) ?? throw new CameraException($"Unable to retrieve {nameof(ProcessCameraProvider)}");
        cameraProviderFuture.AddListener(new Runnable(() =>
        {
            var cameraProviderInstance = cameraProviderFuture.Get().JavaCast<AndroidX.Camera.Core.ICameraProvider>();
            if (cameraProviderInstance is null)
            {
                return;
            }

            var extensionsManagerFuture = ExtensionsManager.GetInstanceAsync(context, cameraProviderInstance)
                                          ?? throw new InvalidOperationException("Unable to get listenable future for camera provider"); ;

            extensionsManagerFuture.AddListener(new Runnable(() =>
            {
                var extensionsManager = (ExtensionsManager?)extensionsManagerFuture.Get();
                if (extensionsManager is not null && extensionsManager.IsExtensionAvailable(cameraSelector, extensionMode))
                {
                    cameraSelector = extensionsManager.GetExtensionEnabledCameraSelector(cameraSelector, extensionMode);
                }

                cameraFutureCts.SetResult();
            }), ContextCompat.GetMainExecutor(context));
        }), ContextCompat.GetMainExecutor(context));

        await cameraFutureCts.Task.WaitAsync(token);
        return cameraSelector;
    }

    async Task<ICamera> RebindCamera(ProcessCameraProvider provider, CameraInfo cameraInfo, CancellationToken token, params UseCase[] useCases)
    {
        var cameraSelector = await EnableModes(cameraInfo, token);
        provider.UnbindAll();
        return provider.BindToLifecycle((ILifecycleOwner)context, cameraSelector, useCases);
    }

    void SetImageCaptureTargetRotation(int rotation)
    {
        _targetRotation = rotation switch
        {
            >= 45 and < 135 => (int)SurfaceOrientation.Rotation270,
            >= 135 and < 225 => (int)SurfaceOrientation.Rotation180,
            >= 225 and < 315 => (int)SurfaceOrientation.Rotation90,
            _ => (int)SurfaceOrientation.Rotation0
        };

        if (_imageCapture is not null)
        {
            _imageCapture.TargetRotation = _targetRotation;
        }

        if (_cameraPreview is not null)
        {
            _cameraPreview.TargetRotation = _targetRotation;
        }
    }

    sealed class ImageCallBack(ICameraControl cameraView) : ImageCapture.OnImageCapturedCallback
    {
        public override void OnCaptureSuccess(IImageProxy image)
        {
            base.OnCaptureSuccess(image);
            var img = image?.Image;

            if (img is null)
            {
                cameraView.OnMediaCapturedFailed("Unable to obtain Image data.");
                return;
            }

            var buffer = GetFirstPlane(img.GetPlanes())?.Buffer;

            if (buffer is null)
            {
                cameraView.OnMediaCapturedFailed("Unable to obtain a buffer for the image plane.");
                image?.Close();
                return;
            }

            var imgData = new byte[buffer.Remaining()];
            try
            {
                buffer.Get(imgData);
                var memStream = new MemoryStream(imgData);
                cameraView.OnMediaCaptured(memStream);
            }
            catch (System.Exception ex)
            {
                cameraView.OnMediaCapturedFailed(ex.Message);
                throw;
            }
            finally
            {
                image?.Close();
            }

            static Image.Plane? GetFirstPlane(Image.Plane[]? planes)
            {
                if (planes is null || planes.Length is 0)
                {
                    return null;
                }

                return planes[0];
            }
        }

        public override void OnError(ImageCaptureException exception)
        {
            base.OnError(exception);
            cameraView.OnMediaCapturedFailed(exception?.Message ?? "An unknown error occurred.");
        }
    }

    sealed class ResolutionFilter(Android.Util.Size size) : Object, IResolutionFilter
    {
        public Android.Util.Size TargetSize { get; set; } = size;

        public IList<Android.Util.Size> Filter(IList<Android.Util.Size>? supportedSizes, int rotationDegrees)
        {
            var isRotated = Math.Abs(rotationDegrees % 180) == 90;
            var filteredList = supportedSizes?
                .Where(size => isRotated
                    ? size.Height <= TargetSize.Width && size.Width <= TargetSize.Height
                    : size.Width <= TargetSize.Width && size.Height <= TargetSize.Height)
                .OrderByDescending(size => (long)size.Width * size.Height)
                .ToList();

            return filteredList is null || filteredList.Count is 0
                ? supportedSizes?.OrderByDescending(size => (long)size.Width * size.Height).ToList() ?? []
                : filteredList;
        }
    }

    sealed class OrientationListener(Action<int> callback, Context context) : OrientationEventListener(context)
    {
        public override void OnOrientationChanged(int orientation)
        {
            if (orientation == OrientationUnknown)
            {
                return;
            }

            callback.Invoke(orientation);
        }
    }
}

public class CameraConsumer(TaskCompletionSource finalizeTcs) : Object, IConsumer
{
    readonly TaskCompletionSource? finalizeTcs = finalizeTcs;

    public void Accept(Object? videoRecordEvent)
    {
        if (videoRecordEvent is VideoRecordEvent.Finalize)
        {
            finalizeTcs?.SetResult();
        }
    }
}
#endif