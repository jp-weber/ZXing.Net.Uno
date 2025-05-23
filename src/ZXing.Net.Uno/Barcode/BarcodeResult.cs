﻿using Windows.Foundation;

namespace ZXing.Net.Uno
{
	public record BarcodeResult
	{
		public byte[] Raw { get; init; }

		public string Value { get; init; }

		public BarcodeFormat Format { get; init; }

		public IReadOnlyDictionary<MetadataType, object> Metadata { get; init; }

		public Point[] PointsOfInterest { get; init; }
	}
}
