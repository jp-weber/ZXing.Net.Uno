namespace ZXing.Net.Uno;

public record BarcodeReaderOptions
{
	public bool AutoRotate { get; init; }

	public bool TryHarder { get; init; }

	public bool TryInverted { get; init; }

	public BarcodeFormat Formats { get; init; }

	public bool Multiple { get; init; }

	public bool UseCode39ExtendedMode { get; init; }

	public string CharacterSet { get; init; } = "UTF-8";

	public bool AssumeGS1 { get; init; }

	/// <summary>
	/// Minimum time between reporting the same barcode value again.
	/// Default: 2 seconds. This prevents repeated callback storms for a static QR code.
	/// </summary>
	public TimeSpan DeduplicationPeriod { get; init; } = TimeSpan.FromSeconds(2);

	// -------------------------------------------------------------------------
	// ROI (Region of Interest) — only scan the central area of the frame.
	// This improves speed considerably and gives the user visual feedback.
	// -------------------------------------------------------------------------

	/// <summary>
	/// When true only the central crop defined by <see cref="RoIRatio"/> is scanned.
	/// Default: <c>false</c> (full frame scan).
	/// </summary>
	public bool EnableRoI { get; init; }

	/// <summary>
	/// Size of the ROI as fraction of total width/height.  Recommended range 0.25 – 0.45.
	/// Default: <c>0.35</c>.  A value of 0.35 means a central area that
	/// spans 35 % of the width and 35 % of the height.
	/// </summary>
	public float RoIRatio { get; init; } = 0.35f;

	/// <summary>
	/// Requests visual indication on the camera preview (dark mask around ROI).
	/// Default: <c>false</c>.  Bind this to a UI toggle if you want the user
	/// to control it at runtime.
	/// </summary>
	public bool ShowRoIVisualIndicator { get; init; }

	/// <summary>
	/// Internal binding property for the visual overlay visibility.
	/// Set to true when <see cref="ShowRoIVisualIndicator"/> is true and the
	/// camera has been initialised.
	/// </summary>
	public bool RoIIsVisible { get; set; }
}
