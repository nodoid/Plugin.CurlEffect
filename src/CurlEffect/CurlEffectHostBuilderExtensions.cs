using Microsoft.Maui.Hosting;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace CurlEffect.Controls;

/// <summary>
/// Host-builder registration for CurlEffect.
/// </summary>
public static class CurlEffectHostBuilderExtensions
{
    /// <summary>
    /// Registers everything <see cref="CurlView"/> needs (SkiaSharp's views/handlers). Call this in
    /// your <c>MauiProgram</c> instead of registering SkiaSharp yourself:
    /// <code>builder.UseMauiApp&lt;App&gt;().UseCurlEffect();</code>
    /// </summary>
    public static MauiAppBuilder UseCurlEffect(this MauiAppBuilder builder)
    {
        builder.UseSkiaSharp();
        return builder;
    }
}
