#if __ANDROID__
using Java.Nio;
using System;

namespace ZXing.Net.Uno
{
	public sealed class ByteBufferYUVLuminanceSource : BaseLuminanceSource
	{
		protected readonly ByteBuffer Yuv;
		protected readonly int DataWidth;
		protected readonly int DataHeight;
		protected readonly int Left;
		protected readonly int Top;

		/// <summary>
		/// Initializes a new instance of the <see cref="PlanarYUVLuminanceSource"/> class.
		/// </summary>
		/// <param name="yuv">The yuv data.</param>
		/// <param name="dataWidth">Width of the data.</param>
		/// <param name="dataHeight">Height of the data.</param>
		/// <param name="left">The left.</param>
		/// <param name="top">The top.</param>
		/// <param name="width">The width.</param>
		/// <param name="height">The height.</param>
		public ByteBufferYUVLuminanceSource(ByteBuffer yuv,
			int dataWidth,
			int dataHeight,
			int left,
			int top,
			int width,
			int height)
			: base(width, height)
		{
			if (left < 0)
				throw new ArgumentException("Negative value", nameof(left));

			if (top < 0)
				throw new ArgumentException("Negative value", nameof(top));

			if (width < 0)
				throw new ArgumentException("Negative value", nameof(width));

			if (height < 0)
				throw new ArgumentException("Negative value", nameof(height));

			if (left + width > dataWidth || top + height > dataHeight)
			{
				throw new ArgumentException("Crop rectangle does not fit within image data.");
			}

			Yuv = yuv;
			DataWidth = dataWidth;
			DataHeight = dataHeight;
			Left = left;
			Top = top;
		}

		/// <summary>
		/// Fetches one row of luminance data from the underlying platform's bitmap. Values range from
		/// 0 (black) to 255 (white). Because Java does not have an unsigned byte type, callers will have
		/// to bitwise and with 0xff for each value. It is preferable for implementations of this method
		/// to only fetch this row rather than the whole image, since no 2D Readers may be installed and
		/// getMatrix() may never be called.
		/// </summary>
		/// <param name="y">The row to fetch, 0 &lt;= y &lt; Height.</param>
		/// <param name="row">An optional preallocated array. If null or too small, it will be ignored.
		/// Always use the returned object, and ignore the .length of the array.</param>
		/// <returns>
		/// An array containing the luminance data of the requested row.
		/// </returns>
		public override byte[] getRow(int y, byte[] row)
		{
			if (y < 0 || y >= Height)
				throw new ArgumentException("Requested row is outside the image: " + y, nameof(y));

			var width = Width;
			if (row == null || row.Length < width)
				row = new byte[width]; // ensure we have room for the row

			var offset = (y + Top) * DataWidth + Left;

			Yuv.Position(offset);
			_ = Yuv.Get(row, 0, width);
			
			return row;
		}

		public override byte[] Matrix
		{
			get
			{
				int width  = Width;
				int height = Height;

				// Lazily allocate and cache the luminance matrix when first accessed.
				if (_cachedMatrix == null || _cachedMatrix.Length != width * height)
				{
					_cachedMatrix = new byte[width * height];
					int inputOffset = Top * DataWidth + Left;

					// If the width matches the full width of the underlying data, perform a single copy.
					if (width == DataWidth)
					{
						Yuv.Position(inputOffset);
						_ = Yuv.Get(_cachedMatrix, 0, height * width);
						return _cachedMatrix;
					}

					// Otherwise copy one cropped row at a time.
					int posOffset = inputOffset;
					for (int y = 0; y < height; y++)
					{
						Yuv.Position(posOffset);
						_ = Yuv.Get(_cachedMatrix, y * width, width);
						posOffset += DataWidth;
					}
					return _cachedMatrix;
				}

				// Cache exists — re-extract ROI from the original ByteBuffer.
				int pos = Top * DataWidth + Left;

				if (width == DataWidth)
				{
					Yuv.Position(pos);
					_ = Yuv.Get(_cachedMatrix, 0, height * width);
					return _cachedMatrix;
				}

				for (int y = 0; y < height; y++)
				{
					Yuv.Position(pos);
					_ = Yuv.Get(_cachedMatrix, y * width, width);
					pos += DataWidth;
				}

				return _cachedMatrix;
			}
		}

		/// <returns> Whether this subclass supports cropping.</returns>
		override public bool CropSupported
			=> true;

		/// <summary>Cached byte array for Matrix to avoid re-allocating on every access.
		/// ZXing may call Matrix multiple times during multi-pass decoding — caching is free since
		/// the underlying ByteBuffer has not changed between calls.</summary>
		private byte[] _cachedMatrix;


		/// <summary>
		/// Returns a new object with cropped image data. Implementations may keep a reference to the
		/// original data rather than a copy. Only callable if CropSupported is true.
		/// </summary>
		/// <param name="left">The left coordinate, 0 &lt;= left &lt; Width.</param>
		/// <param name="top">The top coordinate, 0 &lt;= top &lt;= Height.</param>
		/// <param name="width">The width of the rectangle to crop.</param>
		/// <param name="height">The height of the rectangle to crop.</param>
		/// <returns>
		/// A cropped version of this object.
		/// </returns>
		override public LuminanceSource crop(int left, int top, int width, int height)
			=> new ByteBufferYUVLuminanceSource(
				Yuv,
				DataWidth,
				DataHeight,
				Left + left,
				Top + top,
				width,
				height);

		// Called when rotating. 
		// todo: This partially defeats the purpose as we traffic in byte[] luminances
		protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height)
			=> new PlanarYUVLuminanceSource(newLuminances, width, height, 0, 0, width, height, false);
	}
}
#endif