using System.Web;
using Microsoft.UI.Dispatching;
using ZXing.Net.Uno.Sample.ViewModels;

namespace ZXing.Net.Uno.Sample;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new MainPageViewModel();
    private DispatcherQueue dispatcherQueue => DispatcherQueue.GetForCurrentThread();
    public MainPage()
    {
        this.InitializeComponent();
        BarcodeReaderControl.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.QR_CODE,
            TryHarder = false,
            AutoRotate = false,
            Multiple = false,
            EnableRoI = false,
            RoIRatio = 0.35f,
            ShowRoIVisualIndicator = false,
            DeduplicationPeriod = TimeSpan.FromSeconds(2)
        };
    }

    private void CameraBarcodeReaderControl_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {

#if DEBUG
        System.Diagnostics.Debug.WriteLine(HttpUtility.UrlDecode(e.Results.FirstOrDefault().Value));
#endif
        // Update the UI with the progress on the main thread
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            ViewModel.QRCodeResult = HttpUtility.UrlDecode(e.Results.FirstOrDefault().Value);
        });
    }
}
