using System.Collections;
using Microsoft.Maui;

namespace CurlEffect.Controls;

/// <summary>
/// The page-turn / curl navigation contract. Implemented by <see cref="CurlView"/> and usable from
/// any <see cref="IView"/> reference via <see cref="CurlViewExtensions"/>. Because it derives from
/// <see cref="IView"/>, any IView-based control can expose curl behaviour by implementing this.
/// </summary>
public interface ICurlView : IView
{
    /// <summary>The pages the curl navigates.</summary>
    IList? ItemsSource { get; set; }

    /// <summary>Index of the currently shown view.</summary>
    int CurrentIndex { get; set; }

    /// <summary>Number of views available.</summary>
    int PageCount { get; }

    /// <summary>How fast a turn settles.</summary>
    CurlTurnSpeed TurnSpeed { get; set; }

    /// <summary>Left/right inset (device-independent units) that holds the touch-sensitive curl edge
    /// away from the physical screen edge, so a platform edge gesture (e.g. Android's back swipe)
    /// can't hijack an edge page-turn at any height. 0 = no inset.</summary>
    double EdgeInset { get; set; }

    /// <summary>Animate forward to the next view.</summary>
    Task AnimateAsync();

    /// <summary>Animate to a view: non-negative = absolute index, negative = pages back from the
    /// current view; clamped so overshoot stops at the first/last view.</summary>
    Task AnimateAsync(int value);

    /// <summary>Animate to a view with the curl gripping from <paramref name="origin"/>.</summary>
    Task AnimateFromAsync(CurlOrigin origin, int value);

    /// <summary>Single-turn jump straight to the first view (intermediate views are not shown).</summary>
    Task AnimateToStartAsync();

    /// <summary>Single-turn jump straight to the last view (intermediate views are not shown).</summary>
    Task AnimateToEndAsync();

    /// <summary>Stop any in-progress turn animation and settle on the current view.</summary>
    void StopAnimation();
}

/// <summary>
/// Curl navigation exposed as extension methods on <see cref="IView"/>, so the API can be called on
/// any view reference. Each method dispatches to the view's <see cref="ICurlView"/> implementation
/// and is a safe no-op (returning a completed task) when the view is not curl-capable.
/// </summary>
public static class CurlViewExtensions
{
    /// <summary>True when <paramref name="view"/> supports curl navigation.</summary>
    public static bool IsCurlView(this IView view) => view is ICurlView;

    /// <summary>Returns the view as an <see cref="ICurlView"/>, or null if it isn't curl-capable.</summary>
    public static ICurlView? AsCurlView(this IView view) => view as ICurlView;

    // ---- async ----

    public static Task NextAsync(this IView view) =>
        view is ICurlView c ? c.AnimateAsync() : Task.CompletedTask;

    public static Task PreviousAsync(this IView view) =>
        view is ICurlView c ? c.AnimateAsync(c.CurrentIndex - 1) : Task.CompletedTask;

    public static Task AnimateAsync(this IView view, int value) =>
        view is ICurlView c ? c.AnimateAsync(value) : Task.CompletedTask;

    public static Task AnimateFromAsync(this IView view, CurlOrigin origin, int value) =>
        view is ICurlView c ? c.AnimateFromAsync(origin, value) : Task.CompletedTask;

    public static Task AnimateToStartAsync(this IView view) =>
        view is ICurlView c ? c.AnimateToStartAsync() : Task.CompletedTask;

    public static Task AnimateToEndAsync(this IView view) =>
        view is ICurlView c ? c.AnimateToEndAsync() : Task.CompletedTask;

    // ---- fire-and-forget ----

    public static void Next(this IView view) => _ = view.NextAsync();
    public static void Previous(this IView view) => _ = view.PreviousAsync();
    public static void Animate(this IView view, int value) => _ = view.AnimateAsync(value);
    public static void AnimateFrom(this IView view, CurlOrigin origin, int value) => _ = view.AnimateFromAsync(origin, value);
    public static void AnimateToStart(this IView view) => _ = view.AnimateToStartAsync();
    public static void AnimateToEnd(this IView view) => _ = view.AnimateToEndAsync();
    public static void StopAnimation(this IView view) => (view as ICurlView)?.StopAnimation();
}
