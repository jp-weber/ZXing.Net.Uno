
using CommunityToolkit.Uno.Core;
using CommunityToolkit.Uno.Core.Primitives;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using System.ComponentModel;
using Windows.Foundation;
using ZXing.Net.Uno;
using ZXing.Net.Uno.Readers;

namespace CommunityToolkit.Uno.Camera.Controls;

[TemplatePart(Name = MainGridName, Type = typeof(Grid))]
public sealed partial class CameraBarcodeReaderControl : Control, ICameraBarcodeReaderControl
{
    private const string MainGridName = "MainGrid";
    private bool _isDetecting = true;
    Grid MainGrid;

    // ROI overlay elements — populated in OnApplyTemplate
    Canvas?       _roiOverlay;
    Rectangle?    _roiBorder;
    Rectangle?    _roiTopMask;
    Rectangle?    _roiBottomMask;
    Rectangle?    _roiLeftMask;
    Rectangle?    _roiRightMask;

    CameraManager CameraManager { get; }
    ZXing.Net.Uno.Readers.IBarcodeReader barcodeReader;
    private BarcodeDeduplicator _barcodeDeduplicator = new();

    public event EventHandler<BarcodeDetectionEventArgs> BarcodesDetected;
    public event EventHandler<CameraFrameBufferEventArgs> FrameReady;
    public CameraBarcodeReaderControl()
	{
		this.DefaultStyleKey = typeof(CameraBarcodeReaderControl);
        CameraManager = new CameraManager(this, CameraProvider, () =>
        {
            //await CameraManager.UpdateCaptureResolution(MainGrid.DesiredSize, CancellationToken.None);
        }, true);
    }

    private void CameraManager_FrameReady(object? sender, CameraFrameBufferEventArgs e)
    {
        if (!_isDetecting)
            return;

        FrameReady?.Invoke(this, e);

        var barcodes = BarcodeReader.Decode(e.Data);
        var filteredBarcodes = _barcodeDeduplicator.FilterNewResults(barcodes);

        if (filteredBarcodes.Length > 0)
            BarcodesDetected?.Invoke(this, new BarcodeDetectionEventArgs(filteredBarcodes));
    }

    private void UpdateBarcodeDeduplicator()
    {
        var deduplicationPeriod = Options.DeduplicationPeriod > TimeSpan.Zero
            ? Options.DeduplicationPeriod
            : TimeSpan.FromSeconds(2);

        _barcodeDeduplicator = new BarcodeDeduplicator(deduplicationPeriod);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        MainGrid = (Grid)GetTemplateChild(MainGridName);

        // Wire up ROI overlay elements
        _roiOverlay         = GetTemplateChild("ROIOverlay")     as Canvas;
        _roiBorder          = GetTemplateChild("ROIBorder")     as Rectangle;
        _roiTopMask         = GetTemplateChild("ROITopMask")    as Rectangle;
        _roiBottomMask      = GetTemplateChild("ROIBottomMask") as Rectangle;
        _roiLeftMask        = GetTemplateChild("ROILeftMask")   as Rectangle;
        _roiRightMask       = GetTemplateChild("ROIRightMask")  as Rectangle;

        // Update ROI layout whenever the grid size changes
        MainGrid.SizeChanged -= OnMainGridSizeChanged;
        MainGrid.SizeChanged += OnMainGridSizeChanged;

        UpdateROIVisualState();

        StartCamara();
    }

    private void UpdateROIVisualState()
    {
        if (_roiOverlay is null)
            return;

        var shouldShow = Options.EnableRoI && Options.ShowRoIVisualIndicator;
        _roiOverlay.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        Options.RoIIsVisible = shouldShow;

        if (shouldShow && MainGrid is not null && MainGrid.ActualWidth > 0 && MainGrid.ActualHeight > 0)
        {
            UpdateROILayout(MainGrid.ActualWidth, MainGrid.ActualHeight);
        }
    }

    private async void OnMainGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            UpdateROILayout(e.NewSize.Width, e.NewSize.Height);

            if (CameraManager.IsInitialized)
            {
                await CameraManager.UpdateCaptureResolution(e.NewSize, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Positions the ROI border and dark masks centred on the grid.
    /// The ratio controls how much of the frame is scanned — a value of 0.35 means
    /// a central window spanning 35 % of width and height.
    /// </summary>
    private void UpdateROILayout(double gridWidth, double gridHeight)
    {
        if (_roiOverlay is null || _roiBorder is null) return;

        var ratio       = Math.Clamp((float)Options.RoIRatio, 0.1f, 0.95f);
        var roiW        = gridWidth * ratio;
        var roiH        = gridHeight * ratio;
        var roiX       = (gridWidth - roiW) / 2.0;    // centred left offset
        var roiY       = (gridHeight - roiH) / 2.0;   // centred top offset

        // Measure & arrange the rectangular border in the centre
        _roiBorder.Measure(new Size(roiW, roiH));
        _roiBorder.Arrange(new Rect(roiX, roiY, roiW, roiH));

        var halfTB = _roiBorder.StrokeThickness / 2.0;

        // Top mask — full-width strip above the ROI
        _roiTopMask?.SetValue(Canvas.LeftProperty, 0.0);
        _roiTopMask?.SetValue(Canvas.TopProperty, 0.0);
        _roiTopMask?.SetValue(Canvas.WidthProperty, gridWidth);
        _roiTopMask?.SetValue(Canvas.HeightProperty, roiY - halfTB);

        // Bottom mask — full-width strip below the ROI
        var bottomStart = roiY + roiH + halfTB;
        _roiBottomMask?.SetValue(Canvas.LeftProperty, 0.0);
        _roiBottomMask?.SetValue(Canvas.TopProperty, bottomStart);
        _roiBottomMask?.SetValue(Canvas.WidthProperty, gridWidth);
        _roiBottomMask?.SetValue(Canvas.HeightProperty, gridHeight - bottomStart);

        // Left mask — ROI-height strip to the left of the ROI
        var leftEnd = roiX - halfTB;
        _roiLeftMask?.SetValue(Canvas.LeftProperty, 0.0);
        _roiLeftMask?.SetValue(Canvas.TopProperty, roiY - halfTB);
        _roiLeftMask?.SetValue(Canvas.WidthProperty, leftEnd);
        _roiLeftMask?.SetValue(Canvas.HeightProperty, roiH + halfTB * 2);

        // Right mask — ROI-height strip to the right of the ROI
        var rightStart = roiX + roiW + halfTB;
        _roiRightMask?.SetValue(Canvas.LeftProperty, rightStart);
        _roiRightMask?.SetValue(Canvas.TopProperty, roiY - halfTB);
        _roiRightMask?.SetValue(Canvas.WidthProperty, gridWidth - rightStart);
        _roiRightMask?.SetValue(Canvas.HeightProperty, roiH + halfTB * 2);
    }

    private async Task StartCamara()
    {
        var previewView = CameraManager.CreatePlatformView();
        MainGrid.Children.Insert(0, new ContentControl
        {
            Content = previewView,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        });

        var initialSize = MainGrid.ActualWidth > 0 && MainGrid.ActualHeight > 0
            ? new Size(MainGrid.ActualWidth, MainGrid.ActualHeight)
            : MainGrid.DesiredSize;
        await CameraManager.UpdateCaptureResolution(initialSize, CancellationToken.None);
        await CameraManager.ConnectCamera(CancellationToken.None);

        if (MainGrid.ActualWidth > 0 && MainGrid.ActualHeight > 0)
        {
            await CameraManager.UpdateCaptureResolution(new Size(MainGrid.ActualWidth, MainGrid.ActualHeight), CancellationToken.None);
        }

        CameraManager.FrameReady -= CameraManager_FrameReady;
        CameraManager.FrameReady += CameraManager_FrameReady;
    }

    /// <summary>
    /// Gets a value indicating whether the camera feature is available on the current device.
    /// </summary>
    public bool IsAvailable
    {
        get => (bool)GetValue(IsAvailableProperty);
        set => SetValue(IsAvailableProperty, value);
    }

    public static readonly DependencyProperty IsAvailableProperty =
        DependencyProperty.Register(
            nameof(IsAvailable),
            typeof(bool),
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(CameraViewDefaults.IsAvailable));

    /// <summary>
    /// Backing <see cref="DependencyProperty"/> for the <see cref="CameraFlashMode"/> property.
    /// </summary>
    public static readonly DependencyProperty CameraFlashModeProperty =
        DependencyProperty.Register(
            nameof(CameraFlashMode),
            typeof(CameraFlashMode),
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(CameraViewDefaults.CameraFlashMode));

    /// <summary>
    /// Gets or sets the <see cref="CameraFlashMode"/>.
    /// </summary>
    public CameraFlashMode CameraFlashMode
    {
        get => (CameraFlashMode)GetValue(CameraFlashModeProperty);
        set => SetValue(CameraFlashModeProperty, value);
    }

    /// <summary>
    /// Backing <see cref="DependencyProperty"/> for the <see cref="IsTorchOn"/> property.
    /// </summary>
    public static readonly DependencyProperty IsTorchOnProperty =
        DependencyProperty.Register(
            nameof(IsTorchOn),
            typeof(bool),
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(CameraViewDefaults.IsTorchOn));

    /// <summary>
    /// Gets or sets a value indicating whether the torch (flash) is on.
    /// </summary>
    public bool IsTorchOn
    {
        get => (bool)GetValue(IsTorchOnProperty);
        set
        {
            SetValue(IsTorchOnProperty, value);
            //CameraManager?.UpdateTorch(value);
        }
    }

    public static readonly DependencyProperty IsCameraBusyProperty =
        DependencyProperty.Register(
            nameof(IsCameraBusy),
            typeof(bool),
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(CameraViewDefaults.IsCameraBusy));

    /// <summary>
    /// Gets a value indicating whether the camera is currently busy.
    /// </summary>
    public bool IsCameraBusy
    {
        get => (bool)GetValue(IsCameraBusyProperty);
        private set => SetValue(IsCameraBusyProperty, value);
    }

    /// <summary>
    /// Backing <see cref="DependencyProperty"/> for the <see cref="SelectedCamera"/> property.
    /// </summary>
    public static readonly DependencyProperty? SelectedCameraProperty =
        DependencyProperty.Register(
            nameof(SelectedCamera),
            typeof(CameraInfo),
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(null));

    /// <inheritdoc cref="ICameraControl.SelectedCamera"/>
    public CameraInfo? SelectedCamera
    {
        get => (CameraInfo?)GetValue(SelectedCameraProperty);
        set => SetValue(SelectedCameraProperty, value);
    }

    /// <summary>
    /// Backing <see cref="DependencyProperty"/> for the <see cref="ZoomFactor"/> property.
    /// </summary>
    public static readonly DependencyProperty ZoomFactorProperty =
        DependencyProperty.Register(
            nameof(ZoomFactor),
            typeof(float),
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(CameraViewDefaults.ZoomFactor));

    /// <inheritdoc cref="ICameraControl.ZoomFactor"/>
    public float ZoomFactor
    {
        get => (float)GetValue(ZoomFactorProperty);
        set
        {
            SetValue(ZoomFactorProperty, value);
            CameraManager?.UpdateZoom(value);
        }
    }

    /// <summary>
    /// Backing <see cref="DependencyProperty"/> for the <see cref="ImageCaptureResolution"/> property.
    /// </summary>
    public static readonly DependencyProperty ImageCaptureResolutionProperty =
        DependencyProperty.Register(
            nameof(ImageCaptureResolution),
            typeof(Size),
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(CameraViewDefaults.ImageCaptureResolution));

    /// <inheritdoc cref="ICameraControl.ImageCaptureResolution"/>
    public Size ImageCaptureResolution
    {
        get => (Size)GetValue(ImageCaptureResolutionProperty);
        set => SetValue(ImageCaptureResolutionProperty, value);
    }

    public static readonly DependencyProperty OptionsProperty =
        DependencyProperty.Register(
            nameof(Options), 
            typeof(BarcodeReaderOptions), 
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(new BarcodeReaderOptions(), OnOptionsChanged));

    private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CameraBarcodeReaderControl control)
        {
            control.BarcodeReader.Options = (BarcodeReaderOptions)e.NewValue;
            control.UpdateBarcodeDeduplicator();
            control.UpdateROIVisualState();
        }
    }

    public BarcodeReaderOptions Options
    {
        get => (BarcodeReaderOptions)GetValue(OptionsProperty);
        set
        {
            SetValue(OptionsProperty, value);
            BarcodeReader.Options = value;
            UpdateBarcodeDeduplicator();
            UpdateROIVisualState();
        }
    }

    public static readonly DependencyProperty IsDetectingProperty =
        DependencyProperty.Register(
            nameof(IsDetecting), 
            typeof(bool), 
            typeof(CameraBarcodeReaderControl),
            new PropertyMetadata(true, OnIsDetectingChanged));

    private static void OnIsDetectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CameraBarcodeReaderControl)d)._isDetecting = (bool)e.NewValue;
    }

    public bool IsDetecting
    {
        get => (bool)GetValue(IsDetectingProperty);
        set => SetValue(IsDetectingProperty, value);
    }

    public static readonly DependencyProperty VidioFrameDividerProperty =
    DependencyProperty.Register(
        nameof(VidioFrameDivider),
        typeof(int),
        typeof(CameraBarcodeReaderControl),
        new PropertyMetadata(20));

    public int VidioFrameDivider
    {
        get => (int)GetValue(VidioFrameDividerProperty);
        set
        {
            SetValue(VidioFrameDividerProperty, value);
            CameraManager.VidioFrameDivider = value;
        }   
    }

    //static ICameraProvider CameraProvider => Application.Current?.Services.GetRequiredService<ICameraProvider>() ?? throw new CameraException("Unable to retrieve CameraProvider");
    static ICameraProvider CameraProvider { get; } = new CameraProvider();

    protected ZXing.Net.Uno.Readers.IBarcodeReader BarcodeReader
    => barcodeReader ??= new BarcodeReader();

    TaskCompletionSource handlerCompletedTCS = new();

    [EditorBrowsable(EditorBrowsableState.Never)]
    TaskCompletionSource IAsynchronousHandler.HandlerCompleteTCS => handlerCompletedTCS;

    [EditorBrowsable(EditorBrowsableState.Never)]
    bool ICameraControl.IsBusy
    {
        get => IsCameraBusy;
        set => SetValue(IsCameraBusyProperty, value);
    }

    void ICameraBarcodeReaderControl.BarcodesDetected(BarcodeDetectionEventArgs e) => BarcodesDetected?.Invoke(this, e);
    void ICameraFrameAnalyzer.FrameReady(CameraFrameBufferEventArgs e) => FrameReady?.Invoke(this, e);

    public void OnMediaCaptured(Stream imageData)
    {
        throw new NotImplementedException();
    }

    public void OnMediaCapturedFailed(string failureReason)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc cref="ICameraControl.CaptureImage"/>
    public async ValueTask CaptureImage(CancellationToken token)
    {
        handlerCompletedTCS.TrySetCanceled(token);

        handlerCompletedTCS = new();
        //Handler?.Invoke(nameof(ICameraView.CaptureImage));

        await handlerCompletedTCS.Task.WaitAsync(token);
    }

    /// <inheritdoc cref="ICameraControl.StartCameraPreview"/>
    public async ValueTask StartCameraPreview(CancellationToken token)
    {
        handlerCompletedTCS.TrySetCanceled(token);

        handlerCompletedTCS = new();

        await CameraManager.StartCameraPreview(token);
        //Handler?.Invoke(nameof(ICameraView.StartCameraPreview));

        await handlerCompletedTCS.Task.WaitAsync(token);
    }

    /// <inheritdoc cref="ICameraControl.StopCameraPreview"/>
    public void StopCameraPreview()
    {
        CameraManager.StopCameraPreview();
        //Handler?.Invoke(nameof(ICameraView.StopCameraPreview));
    }

    /// <inheritdoc cref="ICameraControl.GetAvailableCameras"/>
    public async ValueTask<IReadOnlyList<CameraInfo>> GetAvailableCameras(CancellationToken token)
    {
        if (CameraProvider.AvailableCameras is null)
        {
            await CameraProvider.RefreshAvailableCameras(token);

            if (CameraProvider.AvailableCameras is null)
            {
                throw new CameraException("Unable to refresh available cameras");
            }
        }

        return CameraProvider.AvailableCameras;
    }

    Task<Stream> ICameraControl.CaptureImage(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    Task ICameraControl.StartCameraPreview(CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public Task StartVideoRecording(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public Task StartVideoRecording(Stream stream, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public Task<Stream> StopVideoRecording(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }
}
