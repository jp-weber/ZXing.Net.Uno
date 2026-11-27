namespace CommunityToolkit.Uno.Core;

/// <summary>
/// Calculates a camera frame analysis divider that adapts to resolution and fps,
/// keeping analysis throughput roughly between 15-20 FPS across different cameras.
/// </summary>
public static class AdaptiveFrameDivider
{
    /// <summary>
    /// Computes an appropriate frame divider for barcode-analysis throttling.
    /// Lower values mean more frames are analyzed; higher values skip more frames.
    /// </summary>
    /// <param name="cameraFps">The camera's estimated frames-per-second (use -1 to ignore). </param>
    /// <param name="width">Capture width in pixels.</param>
    /// <param name="height">Capture height in pixels</param>
    /// <returns>A positive int divider; 1 = every frame, 20+ = skip most frames.</returns>
    public static int Calculate(int cameraFps, int width, int height)
    {
        const int LowMegapixelThreshold  = 500_000;   // ~0.5 MP (e.g. VGA / 800×600)
        const int HighMegapixelThreshold = 2_000_000;  // ~2  MP (e.g. FHD / 1920×1080)

        if (width <= 0 || height <= 0)
            return 20;  // safe default for unknown resolutions

        int adaptiveDivider = CameraFpsBasedDivider(cameraFps);
        bool isUltraHd     = IsUltraHighDefinition(width, height, HighMegapixelThreshold);
        bool isLowRes      = IsLowResolution(width,height, LowMegapixelThreshold);

            return ScaleByPixelCount(adaptiveDivider, width, height, isLowRes, isUltraHd);
    }

    private static int CameraFpsBasedDivider(int cameraFps)
    {
        // The closer the actual fps to 15 FPS, the lower the multiplier.
        if (cameraFps < 0)
        {
            // Fallback: high fps assumed for unknown cameras
            return 2;
        }
        else if (cameraFps <= 10)             // Front cam, dim light
            return 1;                          // analyze almost everything
        return cameraFps switch
            {
                < 15 => 1,                       // ≤ 14 FPS → every frame
                <= 20 => 2,                      // 15-20 FPS → ~10 FPS analysis
                <= 30 => 3,                      // 21-30 FPS → ~8-10 FPS analysis
                _ => cameraFps / 15              // ≥ 30 FPS → scale up
            };
    }

    private static int ScaleByPixelCount(int baseDivider, int w, int h, bool isLowRes, bool isUltraHd)
    {
        // High-resolution cameras need a larger divider.
        if (isUltraHd)
        {
            const int UltraHDWidth  = 3840;
            const int UltraHDHeight = 2160;
            double factor = ((double)w * h) / (UltraHDWidth * UltraHDHeight);
            return Math.Max(5, (int)(baseDivider * factor)); // min divider 5
        }

        // Low resolution cameras benefit from a smaller divider (more analysis).
        if (isLowRes)
        {
            return Math.Max(1, baseDivider / 4);
        }

        // Fallback divider for medium-res cameras.
        const double MediumMultiplier = 0.625;
        return (int)(baseDivider * MediumMultiplier);
    }

    private static bool IsUltraHighDefinition(int w, int h, int threshold)
        => w > threshold / MathF.Sqrt(2) && h > MathF.Sqrt(threshold / w);

    private static bool IsLowResolution(int w, int h, int threshold)
        => w * h < threshold;
}
