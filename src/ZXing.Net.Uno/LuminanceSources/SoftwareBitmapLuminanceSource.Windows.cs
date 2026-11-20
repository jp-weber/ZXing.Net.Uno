#if WINDOWS
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace ZXing.Net.Uno;

public class SoftwareBitmapLuminanceSource : BaseLuminanceSource
{
    public int SourceWidth { get; }
    public int SourceHeight { get; }

    public static LuminanceSource FromSoftwareBitmap(SoftwareBitmap softwareBitmap, int left, int top, int width, int height)
        => new SoftwareBitmapLuminanceSource(softwareBitmap, left, top, width, height);

    public static LuminanceSource FromSoftwareBitmap(SoftwareBitmap softwareBitmap)
        => new SoftwareBitmapLuminanceSource(softwareBitmap);

    public SoftwareBitmapLuminanceSource(SoftwareBitmap softwareBitmap, int left, int top, int width, int height)
        : base(width, height)
    {
        if (left < 0 || top < 0 || width <= 0 || height <= 0)
            throw new ArgumentException("Invalid crop parameters.", nameof(softwareBitmap));

        if (left + width > softwareBitmap.PixelWidth || top + height > softwareBitmap.PixelHeight)
            throw new ArgumentException("Crop rectangle does not fit within image data.", nameof(softwareBitmap));

        SourceWidth = softwareBitmap.PixelWidth;
        SourceHeight = softwareBitmap.PixelHeight;

        var converted = softwareBitmap.BitmapPixelFormat == BitmapPixelFormat.Gray8
            ? softwareBitmap
            : SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Gray8);

        var bytes = new byte[converted.PixelWidth * converted.PixelHeight];
        converted.CopyToBuffer(bytes.AsBuffer());

        var stride = converted.PixelWidth;
        for (int row = 0; row < height; row++)
        {
            int srcBase = (top + row) * stride + left;
            int dstBase = row * width;
            Array.Copy(bytes, srcBase, luminances, dstBase, width);
        }
    }

    public SoftwareBitmapLuminanceSource(SoftwareBitmap softwareBitmap) : base(softwareBitmap.PixelWidth, softwareBitmap.PixelHeight)
    {
        SourceWidth = softwareBitmap.PixelWidth;
        SourceHeight = softwareBitmap.PixelHeight;

        var gray = softwareBitmap.BitmapPixelFormat == BitmapPixelFormat.Gray8
            ? softwareBitmap
            : SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Gray8);

        var bytes = new byte[gray.PixelWidth * gray.PixelHeight];
        gray.CopyToBuffer(bytes.AsBuffer());
        Array.Copy(bytes, 0, luminances, 0, luminances.Length);
    }

    public SoftwareBitmapLuminanceSource(byte[] luminances, int width, int height) : base(luminances, width, height)
    {
        SourceWidth = width;
        SourceHeight = height;
    }

    public override bool CropSupported => true;

    public override LuminanceSource crop(int left, int top, int width, int height)
    {
        if (left < 0 || top < 0 || width <= 0 || height <= 0 ||
            left + width > Width || top + height > Height)
            throw new ArgumentException("Crop rectangle does not fit within image data.", nameof(left));

        var cropped = new byte[width * height];
        for (int row = 0; row < height; row++)
        {
            var sourceIndex = (top + row) * Width + left;
            Array.Copy(luminances, sourceIndex, cropped, row * width, width);
        }

        return new SoftwareBitmapLuminanceSource(cropped, width, height);
    }

    protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height)
        => new SoftwareBitmapLuminanceSource(newLuminances, width, height);
}
#endif
