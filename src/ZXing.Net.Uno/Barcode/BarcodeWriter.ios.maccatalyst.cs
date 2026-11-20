#if IOS || MACCATALYST
using UIKit;
using Windows.UI;

namespace ZXing.Net.Uno.Barcode
{
    public class BarcodeWriter : BarcodeWriter<UIImage>, IBarcodeWriter
    {
        BarcodeBitmapRenderer bitmapRenderer;

        public BarcodeWriter()
            => Renderer = (bitmapRenderer = new BarcodeBitmapRenderer());

        public Color ForegroundColor
        {
            get => ToWindowsColor(bitmapRenderer.ForegroundColor);
            set => bitmapRenderer.ForegroundColor = ToCGColor(value);
        }

        public Color BackgroundColor
        {
            get => ToWindowsColor(bitmapRenderer.BackgroundColor);
            set => bitmapRenderer.BackgroundColor = ToCGColor(value);
        }

        private static CoreGraphics.CGColor ToCGColor(Color color)
            => UIColor.FromRGBA(
                color.R / (nfloat)byte.MaxValue,
                color.G / (nfloat)byte.MaxValue,
                color.B / (nfloat)byte.MaxValue,
                color.A / (nfloat)byte.MaxValue).CGColor;

        private static Color ToWindowsColor(CoreGraphics.CGColor color)
        {
            new UIColor(color).GetRGBA(out var red, out var green, out var blue, out var alpha);
            return Color.FromArgb(
                (byte)Math.Round(alpha * byte.MaxValue),
                (byte)Math.Round(red * byte.MaxValue),
                (byte)Math.Round(green * byte.MaxValue),
                (byte)Math.Round(blue * byte.MaxValue));
        }
    }
}
#endif