namespace ZXing.Net.Uno.Readers;

public class BarcodeReader : IBarcodeReader
{
	private readonly BarcodeReaderGeneric _zxingReader = new();

	public BarcodeReaderOptions Options { get; set; } = new();

	/// <summary>Returns (left, top, sizeWidth, sizeHeight) for the ROI crop.</summary>
	private static (int left, int top, int sw, int sh) ComputeROICrop(int width, int height, float ratio)
	{
		var r = Math.Clamp(ratio, 0.1f, 0.95f);
		var sw = (int)Math.Max(1, width * r);
		var sh = (int)Math.Max(1, height * r);
		var left = (width - sw) / 2;
		var top  = (height - sh) / 2;
		return (left, top, sw, sh);
	}

	public BarcodeResult[] Decode(PixelBufferHolder image)
	{
		var w = (int)image.Size.Width;
		var h = (int)image.Size.Height;

		LuminanceSource? ls = default;

#if ANDROID
		if (Options.EnableRoI)
		{
			var (left, top, sw, sh) = ComputeROICrop(w, h, Options.RoIRatio);
			// ByteBufferYUVLuminanceSource already supports cropping via left/top/width/height params
			ls = new ByteBufferYUVLuminanceSource(image.Data, w, h, left, top, sw, sh);
		}
		else
		{
			ls = new ByteBufferYUVLuminanceSource(image.Data, w, h, 0, 0, w, h);
		}
#elif MACCATALYST || IOS
		if (Options.EnableRoI)
		{
			var (left, top, sw, sh) = ComputeROICrop(w, h, Options.RoIRatio);
			ls = new CVPixelBufferBGRA32LuminanceSource(image.Data, w, h, left, top, sw, sh);
		}
		else
		{
			ls = new CVPixelBufferBGRA32LuminanceSource(image.Data, w, h);
		}
#elif WINDOWS
		if (Options.EnableRoI)
		{
			var (left, top, sw, sh) = ComputeROICrop(w, h, Options.RoIRatio);
			ls = new SoftwareBitmapLuminanceSource((Windows.Graphics.Imaging.SoftwareBitmap)image.Data, left, top, sw, sh);
		}
		else
		{
			ls = new SoftwareBitmapLuminanceSource((Windows.Graphics.Imaging.SoftwareBitmap)image.Data);
		}
#endif

		if (ls is null)
			return [];

		if (Options.Multiple)
			return _zxingReader.DecodeMultiple(ls)?.ToBarcodeResults() ?? [];

		var result = _zxingReader.Decode(ls);
		return result != null ? new[] { result.ToBarcodeResult() } : [];
	}
}