using CommunityToolkit.Uno.Core.Primitives;
using Windows.Foundation;
using ZXing.Net.Uno;

namespace CommunityToolkit.Uno.Core;

/// <summary>
/// A class that manages the camera functionality.
/// </summary>
/// <remarks>
/// Creates a new instance of the <see cref="CameraManager"/> class.
/// </remarks>
/// <param name="_cameraView">The <see cref="ICameraControl"/> implementation.</param>
/// <param name="_cameraProvider">The <see cref="CameraProvider"/> implementation.</param>
/// <param name="onLoaded">The <see cref="Action"/> to execute when the camera is loaded.</param>
/// <param name="analyseImages">The <see cref="bool"/> determines whether the images are analysed for barcode recognition.</param>
/// <exception cref="NullReferenceException">Thrown when no <see cref="CameraProvider"/> can be resolved.</exception>
/// <exception cref="InvalidOperationException">Thrown when there are no cameras available.</exception>
sealed partial class CameraManager(
    ICameraControl _cameraView,
    ICameraProvider _cameraProvider,
    Action onLoaded,
    bool analyseImages = false) : IDisposable
{
    internal Action OnLoaded { get; } = onLoaded;

    internal bool IsInitialized { get; private set; }

    internal bool AnalyseImages { get; } = analyseImages;

    private long _videoFrameCounter = 0;

    /// <summary>
    /// Resolution dimensions used to calculate the adaptive frame divider.
    /// Set by platform-specific code when the camera capture is configured.
    /// </summary>
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }

    /// <summary>
    /// An optional estimated FPS of the current camera (use -1 if unknown).
    /// Set by platform-specific code after the camera is connected.
    /// </summary>
    public int EstimatedFps { get; set; } = -1;

    /// <summary>
    /// Legacy frame skip value used when adaptive throttling is disabled.
    /// </summary>
    public int VidioFrameDivider { get; set; } = 20;

    /// <summary>
    /// Enable adaptive frame throttling based on resolution and estimated FPS.
    /// When false (default is true), falls back to the classic <see cref="VidioFrameDivider"/>
    /// value set by the caller.
    /// </summary>
    public bool EnableAdaptiveThrottling { get; set; } = true;

    /// <summary>
    /// Gets a calculated divider that adapts to resolution and camera FPS.
    /// Lower values mean more frames are analyzed, higher values skip more frames.
    /// </summary>
    private int AdaptiveDivider => CalculateAdaptiveDivider(EstimatedFps, CaptureWidth, CaptureHeight);

    public event EventHandler<CameraFrameBufferEventArgs> FrameReady;

    // ------------------------------------------------------------------
    // Adaptive Frame Divider algorithm
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns a frame divider that adapts to camera FPS and resolution.
    /// </summary>
    private static int CalculateAdaptiveDivider(int estimatedFps, int width, int height)
    {
        if (width <= 0 || height <= 0) return 20;

        long pixelCount = (long)width * height;

        // High-res cameras (≥ 2 MP — e.g. 1920×1080) are throttled more aggressively.
        if (pixelCount > 2_000_000)
            return estimatedFps > 0 ? Math.Max(3, estimatedFps / 15) : 3;

        if (pixelCount > 500_000) // ≥ 0.5 MP — e.g. 800×600
            return estimatedFps > 0 ? Math.Max(1, estimatedFps / 20) : 1;

        return 1; // Low-res: analyse every frame
    }


    /// <summary>
    /// Connects to the camera.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that can be awaited.</returns>
    public async Task ConnectCamera(CancellationToken token)
    {
        if (_cameraProvider.AvailableCameras is null)
        {
            await _cameraProvider.RefreshAvailableCameras(token);
        }
        _cameraView.SelectedCamera ??= _cameraProvider.AvailableCameras?.FirstOrDefault() ?? throw new CameraException("No camera available on device");
        await PlatformConnectCamera(token);
    }

    /// <summary>
    /// Disconnects from the camera.
    /// </summary>
    public void Disconnect() => PlatformDisconnect();

    /// <summary>
    /// Takes a picture using the camera.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that can be awaited.</returns>
    public ValueTask TakePicture(CancellationToken token) => PlatformTakePicture(token);

    /// <summary>
    /// Starts the camera preview.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that can be awaited.</returns>
    public Task StartCameraPreview(CancellationToken token) => PlatformStartCameraPreview(token);

    /// <summary>
    /// Starts the video recording.
    /// </summary>
    /// <param name="stream">The output stream for video recording</param>
    /// <param name="token"><see cref="CancellationToken"/></param>
    /// <returns>A <see cref="Task"/> that can be awaited.</returns>
    /// <exception cref="CameraException"></exception>
    public Task StartVideoRecording(Stream stream, CancellationToken token)
    {
        return PlatformStartVideoRecording(stream, token);
    }

    /// <summary>
    /// Stops the video recording.
    /// </summary>
    /// <param name="token"><see cref="CancellationToken"/></param>
    /// <returns>A <see cref="Task"/> that can be awaited.</returns>
    public Task<Stream> StopVideoRecording(CancellationToken token) => PlatformStopVideoRecording(token);

    /// <summary>
    /// Stops the camera preview.
    /// </summary>
    public void StopCameraPreview() => PlatformStopCameraPreview();

    /// <summary>
    /// Updates the current camera.
    /// </summary>
    /// <param name="cameraInfo">The new <see cref="CameraInfo"/> to set as current.</param>
    /// <param name="token">A <see cref="CancellationToken"/> that can be used to cancel the work.</param>
    /// <remarks>
    /// If the <see cref="CameraManager"/> is initialized the old camera preview will be stopped
    /// and the new camera preview will be started.
    /// </remarks>
    /// <returns>A <see cref="ValueTask"/> that can be awaited.</returns>
    public async ValueTask UpdateCurrentCamera(CameraInfo? cameraInfo, CancellationToken token)
    {
        if (cameraInfo is null)
        {
            return;
        }

        if (IsInitialized)
        {
            PlatformStopCameraPreview();
            await PlatformStartCameraPreview(token);
        }
    }

    /// <summary>
    /// Updates the flash mode of the camera.
    /// </summary>
    /// <param name="flashMode">The new <see cref="CameraFlashMode"/> to set.</param>
    public partial void UpdateFlashMode(CameraFlashMode flashMode);

    /// <summary>
    /// Updates the zoom level of the camera.
    /// </summary>
    /// <param name="zoomLevel">The new zoom level to set.</param>
    public partial void UpdateZoom(float zoomLevel);

    /// <summary>
    /// Updates the capture resolution of the camera.
    /// </summary>
    /// <param name="resolution">The <see cref="Size" /> resolution to use when capturing an image from the current camera.</param>
    /// <param name="token">A <see cref="CancellationToken"/> that can be used to cancel the work.</param>
    /// <returns>A <see cref="ValueTask"/> that can be awaited.</returns>
    public partial ValueTask UpdateCaptureResolution(Size resolution, CancellationToken token);

    /// <summary>
    /// Performs the capturing of a picture at the platform-specific level. 
    /// </summary>
    /// <param name="token">A <see cref="CancellationToken"/> that can be used to cancel the work.</param>
    /// <returns>A <see cref="ValueTask"/> that can be awaited.</returns>
    private partial ValueTask PlatformTakePicture(CancellationToken token);

    /// <summary>
    /// Starts the preview from the camera, at the platform-specific level.
    /// </summary>
    /// <param name="token">A <see cref="CancellationToken"/> that can be used to cancel the work.</param>
    /// <returns>A <see cref="Task"/> that can be awaited.</returns>
    private partial Task PlatformStartCameraPreview(CancellationToken token);

    /// <summary>
    /// Connects to the camera, at the platform-specific level.
    /// </summary>
    /// <param name="token">A <see cref="CancellationToken"/> that can be used to cancel the work.</param>
    /// <returns>A <see cref="Task"/> that can be awaited.</returns>
    private partial Task PlatformConnectCamera(CancellationToken token);

    /// <summary>
    /// Disconnects from the camera, at the platform-specific level.
    /// </summary>
    private partial void PlatformDisconnect();

    /// <summary>
    /// Stops the preview from the camera, at the platform-specific level.
    /// </summary>
    private partial void PlatformStopCameraPreview();

    /// <summary>
    /// Starts video recording and writes the recorded data to the specified stream.
    /// </summary>
    /// <remarks>This method is platform-specific and should be overridden in a derived class to provide the actual
    /// implementation. Ensure that the stream remains open and writable for the duration of the recording.</remarks>
    /// <param name="stream">The stream to which the video data will be written. Must be writable and not null.</param>
    /// <param name="token">A cancellation token that can be used to cancel the video recording operation.</param>
    /// <returns>A task that represents the asynchronous video recording operation.</returns>
    private partial Task PlatformStartVideoRecording(Stream stream, CancellationToken token);

    /// <summary>
    /// Stops the video recording process asynchronously.
    /// </summary>
    /// <remarks>This method is platform-specific and should be overridden in a derived class to implement the stop
    /// functionality.</remarks>
    /// <param name="token">A cancellation token that can be used to cancel the stop operation.</param>
    /// <returns>A task that represents the asynchronous stop operation.</returns>
    private partial Task<Stream> PlatformStopVideoRecording(CancellationToken token);
}