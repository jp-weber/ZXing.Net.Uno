#if IOS || MACCATALYST
using CoreVideo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZXing.Net.Uno
{
	public class CVPixelBufferBGRA32LuminanceSource : BaseLuminanceSource
	{
		private readonly int _sourceWidth;
		private readonly int _sourceHeight;

		/// <summary>Full-frame constructor.</summary>
		public unsafe CVPixelBufferBGRA32LuminanceSource(CVPixelBuffer pixelBuffer, int width, int height)
			: base(width, height)
		{
			ExtractLuminanceFromPixelBuffer(pixelBuffer, 0, 0, width, height);
			_sourceWidth = width;
			_sourceHeight = height;
		}

		/// <summary>ROI-activated constructor — extract luminance from [left,top..width×height].</summary>
		public unsafe CVPixelBufferBGRA32LuminanceSource(CVPixelBuffer pixelBuffer, int sourceWidth, int sourceHeight, int left, int top, int width, int height)
			: base(width, height)
		{
			if (left < 0 || top < 0 || width <= 0 || height <= 0 ||
				left + width > sourceWidth || top + height > sourceHeight)
				throw new ArgumentException("Crop rectangle does not fit within image data.", nameof(pixelBuffer));

			ExtractLuminanceFromPixelBuffer(pixelBuffer, left, top, width, height);
			_sourceWidth = sourceWidth;
			_sourceHeight = sourceHeight;
		}

		public CVPixelBufferBGRA32LuminanceSource(byte[] luminances, int width, int height) : base(luminances, width, height)
		{
			_sourceWidth = width;
			_sourceHeight = height;
		}

		private unsafe void ExtractLuminanceFromPixelBuffer(CVPixelBuffer pixelBuffer, int left, int top, int width, int height)
		{
			var rowBytes = pixelBuffer.BytesPerRow;
			var basePtr = (byte*)pixelBuffer.BaseAddress.ToPointer();

			for (int srcRow = 0, dstRow = 0; srcRow < height && dstRow < Height; srcRow++, dstRow++)
			{
				var rowStart = basePtr + (srcRow + top) * rowBytes + left * 4; // BGRA=4 bytes

				for (int col = 0; col < width; col++)
				{
					var b = rowStart[col * 4 + 0];
					var g = rowStart[col * 4 + 1];
					var r = rowStart[col * 4 + 2];
					var alpha = rowStart[col * 4 + 3];

					var luminance = (byte)((RChannelWeight * r + GChannelWeight * g + BChannelWeight * b) >> ChannelWeight);
					luminances[dstRow * width + col] = (byte)(((luminance * alpha) >> 8) + ((255 * (255 - alpha)) >> 8));
				}
			}
		}

		unsafe void CalculateLuminance(byte* rgbRawBytes, int bytesLen)
		{
			for (int rgbIndex = 0, luminanceIndex = 0; rgbIndex < bytesLen && luminanceIndex < luminances.Length; luminanceIndex++)
			{
				// Calculate luminance cheaply, favoring green.
				var b = rgbRawBytes[rgbIndex++];
				var g = rgbRawBytes[rgbIndex++];
				var r = rgbRawBytes[rgbIndex++];
				var alpha = rgbRawBytes[rgbIndex++];
				var luminance = (byte)((RChannelWeight * r + GChannelWeight * g + BChannelWeight * b) >> ChannelWeight);
				luminances[luminanceIndex] = (byte)(((luminance * alpha) >> 8) + ((255 * (255 - alpha)) >> 8));
			}
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

			return new CVPixelBufferBGRA32LuminanceSource(cropped, width, height);
		}

		protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height)
			=> new CVPixelBufferBGRA32LuminanceSource(newLuminances, width, height);
	}
}
#endif