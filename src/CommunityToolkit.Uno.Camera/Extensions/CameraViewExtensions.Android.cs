﻿#if ANDROID
using Android.Content;
using Android.Content.PM;
using AndroidX.Camera.Core;
using CommunityToolkit.Uno.Core;
using CommunityToolkit.Uno.Core.Primitives;

namespace CommunityToolkit.Uno.Extensions;

/// <summary>
/// Extension methods for the CameraView on Android.
/// </summary>
static class CameraViewExtensions
{
	/// <summary>
	/// Converts a <see cref="CameraFlashMode"/> to the platform-specific flash mode.
	/// </summary>
	/// <param name="flashMode">The <see cref="CameraFlashMode"/> to convert.</param>
	/// <returns>The platform-specific flash mode.</returns>
	/// <exception cref="NotSupportedException">When the supplied <paramref name="flashMode"/> is not supported.</exception>
	public static int ToPlatform(this CameraFlashMode flashMode) => flashMode switch
	{
		CameraFlashMode.Off => ImageCapture.FlashModeOff,
		CameraFlashMode.On => ImageCapture.FlashModeOn,
		CameraFlashMode.Auto => ImageCapture.FlashModeAuto,
		_ => throw new NotSupportedException($"Flash mode {flashMode} not supported."),
	};

	/// <summary>
	/// Updates whether the camera feature is available on Android.
	/// </summary>
	/// <param name="cameraView">An <see cref="ICameraControl"/> implementation.</param>
	/// <param name="context">The <see cref="Context"/> used to determine whether the environment supports camera access.</param>
	public static void UpdateAvailability(this Core.ICameraControl cameraView, Context context)
	{
		cameraView.IsAvailable = context.PackageManager?.HasSystemFeature(PackageManager.FeatureCamera) ?? false;
	}
}
#endif