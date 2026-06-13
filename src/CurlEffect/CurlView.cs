using System.Collections;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace CurlEffect.Controls;

/// <summary>How quickly a page settles when released or turned via command.</summary>
public enum CurlTurnSpeed
{
    Fast,
    Normal,
    Slow,
}

/// <summary>Where along the turning edge a programmatic curl grips.</summary>
public enum CurlOrigin
{
    TopLeft,
    TopRight,
    Middle,
    BottomRight,
    BottomLeft,
}

/// <summary>
/// Context handed to a <see cref="CurlView.PageDrawer"/> (or the <c>DrawPage</c> event) so the
/// consumer can paint a single page. Everything is in device pixels; <see cref="Bounds"/> is the
/// full page rectangle.
/// </summary>
public sealed class CurlPageDrawContext
{
    public required SKCanvas Canvas { get; init; }
    public required SKRect Bounds { get; init; }
    public required object? Item { get; init; }
    public required int Index { get; init; }
}

/// <summary>
/// A book-style page-turn control. Pages come from <see cref="ItemsSource"/>; the visible page is
/// <see cref="CurrentIndex"/> (two-way bindable). Drag from anywhere along the right edge to turn
/// forward, or the left edge to turn back. Each page is painted by <see cref="PageDrawer"/> / the
/// <see cref="DrawPage"/> event (a default text drawer is used otherwise).
/// </summary>
public class CurlView : ContentView, ICurlView
{
    readonly SKCanvasView _canvas;

    // --- live turn state ---
    bool _turning;          // a curl is currently being rendered (drag or animation)
    bool _animating;        // the release animation is running (ignore touch)
    int _dir;               // +1 forward (spine left), -1 backward (spine right)
    int _turnTarget;        // index this turn settles on (the page shown beneath the curl)
    float _grabY;           // y the user grabbed along the edge (pixels)
    SKPoint _grip;          // current position of the gripped edge point
    SKPoint _origin;        // where the gripped point started (on the edge)

    readonly Dictionary<int, SKBitmap> _pageCache = new();
    SKSizeI _cacheSize;

    public CurlView()
    {
        _canvas = new SKCanvasView();
        _canvas.PaintSurface += OnPaintSurface;

#if ANDROID || IOS
        // On touch platforms, PointerGestureRecognizer does NOT deliver finger press/move/release
        // (it tracks mouse & stylus pointers, not touch), so a drag would never start. SKCanvasView's
        // own touch events do fire for touch and report positions already in canvas pixels.
        _canvas.EnableTouchEvents = true;
        _canvas.Touch += OnCanvasTouch;
#else
        // Desktop (Windows / Mac Catalyst): PointerGestureRecognizer tracks mouse & pen and reports a
        // usable position (unlike SKCanvasView.Touch, whose coordinates are wrong on Mac Catalyst).
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += OnPointerPressed;
        pointer.PointerMoved += OnPointerMoved;
        pointer.PointerReleased += OnPointerReleased;
        _canvas.GestureRecognizers.Add(pointer);
#endif

        Content = _canvas;
    }

    // ----------------------------------------------------------------- Android system-gesture handling

    // On Android 10+ with gesture navigation, a swipe from the left/right screen edge is the system
    // "back" gesture. That overlaps a backward page-turn (grab the left edge, drag inward), so without
    // this the OS would navigate back / close the app instead of turning the page. We mark the control's
    // bounds as a system-gesture exclusion zone so the OS leaves edge drags to us.
    // (Android clamps each edge's exclusion to 200dp, which covers the corner/near-edge grabs that page
    // turns naturally use; consumers can still add padding if they grab at the exact vertical centre.)
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (_nativeView is not null)
            _nativeView.LayoutChange -= OnNativeLayoutChange;

        _nativeView = Handler?.PlatformView as Android.Views.View;

        if (_nativeView is not null)
        {
            _nativeView.LayoutChange += OnNativeLayoutChange;
            ApplyGestureExclusion();
        }
#endif
    }

#if ANDROID
    Android.Views.View? _nativeView;

    void OnNativeLayoutChange(object? sender, Android.Views.View.LayoutChangeEventArgs e) =>
        ApplyGestureExclusion();

    void ApplyGestureExclusion()
    {
        // setSystemGestureExclusionRects is API 29+; older devices use button nav and don't need it.
        if (_nativeView is null || !OperatingSystem.IsAndroidVersionAtLeast(29)) return;

        int w = _nativeView.Width, h = _nativeView.Height;
        if (w <= 0 || h <= 0) return;

        // Rect is in the view's own coordinate space, so the whole control is (0,0)-(w,h).
        _nativeView.SystemGestureExclusionRects = new List<Android.Graphics.Rect>
        {
            new(0, 0, w, h),
        };
    }
#endif

    // DIP -> canvas-pixel scale (SkiaSharp draws in pixels; pointer positions arrive in DIPs).
    float PixelScale => _canvas.Width > 0 ? (float)(_canvas.CanvasSize.Width / _canvas.Width) : 1f;

    SKPoint ToPixels(Point? p)
    {
        var v = p ?? new Point();
        float s = PixelScale;
        return new SKPoint((float)v.X * s, (float)v.Y * s);
    }

    // ----------------------------------------------------------------- Bindable API

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IList), typeof(CurlView), null,
        propertyChanged: (b, _, _) => ((CurlView)b).OnItemsSourceChanged());

    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly BindableProperty CurrentIndexProperty = BindableProperty.Create(
        nameof(CurrentIndex), typeof(int), typeof(CurlView), 0, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => { var v = (CurlView)b; v.Invalidate(); v.RaiseCanGoChanged(); });

    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    /// <summary>Color of the paper; also shows at the spine and at the ends of the book.</summary>
    public static readonly BindableProperty PageColorProperty = BindableProperty.Create(
        nameof(PageColor), typeof(Color), typeof(CurlView), Color.FromArgb("#FFFDF6E3"),
        propertyChanged: (b, _, _) => ((CurlView)b).ClearCacheAndRedraw());

    public Color PageColor
    {
        get => (Color)GetValue(PageColorProperty);
        set => SetValue(PageColorProperty, value);
    }

    /// <summary>How fast a released/commanded turn animates to rest.</summary>
    public static readonly BindableProperty TurnSpeedProperty = BindableProperty.Create(
        nameof(TurnSpeed), typeof(CurlTurnSpeed), typeof(CurlView), CurlTurnSpeed.Normal);

    public CurlTurnSpeed TurnSpeed
    {
        get => (CurlTurnSpeed)GetValue(TurnSpeedProperty);
        set => SetValue(TurnSpeedProperty, value);
    }

    // Animation duration (ms) for the current TurnSpeed.
    uint TurnDurationMs => TurnSpeed switch
    {
        CurlTurnSpeed.Fast => 160,
        CurlTurnSpeed.Slow => 600,
        _ => 320,
    };

    /// <summary>
    /// Left/right inset (in device-independent units) that keeps the touch-sensitive curl edge away
    /// from the physical screen edge. On Android this is the belt-and-braces companion to the system
    /// gesture-exclusion zone (which the OS caps at 200dp per edge): a non-zero inset guarantees an
    /// edge page-turn isn't hijacked by the back swipe even when grabbed at the vertical centre.
    /// Defaults to 0 (no inset); ~24 is a comfortable value on Android.
    /// </summary>
    public static readonly BindableProperty EdgeInsetProperty = BindableProperty.Create(
        nameof(EdgeInset), typeof(double), typeof(CurlView), 0d,
        propertyChanged: (b, _, _) => ((CurlView)b).ApplyEdgeInset());

    public double EdgeInset
    {
        get => (double)GetValue(EdgeInsetProperty);
        set => SetValue(EdgeInsetProperty, value);
    }

    // Inset the drawing/touch surface from the left and right edges. Because both rendering and
    // pointer hit-testing run against the (now narrower) canvas, the curl's edge grab zone moves
    // inward with it — no extra coordinate math needed.
    void ApplyEdgeInset()
    {
        double i = Math.Max(0, EdgeInset);
        _canvas.Margin = new Thickness(i, 0, i, 0);
    }

    /// <summary>Optional per-page painter. If null, a default text drawer is used.</summary>
    public Action<CurlPageDrawContext>? PageDrawer { get; set; }

    /// <summary>Raised when a page needs painting (alternative to <see cref="PageDrawer"/>).</summary>
    public event EventHandler<CurlPageDrawContext>? DrawPage;

    /// <summary>Command equivalent of <see cref="DrawPage"/>; invoked with the draw context.</summary>
    public static readonly BindableProperty DrawPageCommandProperty = BindableProperty.Create(
        nameof(DrawPageCommand), typeof(ICommand), typeof(CurlView));

    public ICommand? DrawPageCommand
    {
        get => (ICommand?)GetValue(DrawPageCommandProperty);
        set => SetValue(DrawPageCommandProperty, value);
    }

    /// <summary>Raised after a turn completes and <see cref="CurrentIndex"/> has changed.</summary>
    public event EventHandler<int>? PageChanged;

    /// <summary>Command equivalent of <see cref="PageChanged"/>; invoked with the new index.</summary>
    public static readonly BindableProperty PageChangedCommandProperty = BindableProperty.Create(
        nameof(PageChangedCommand), typeof(ICommand), typeof(CurlView));

    public ICommand? PageChangedCommand
    {
        get => (ICommand?)GetValue(PageChangedCommandProperty);
        set => SetValue(PageChangedCommandProperty, value);
    }

    Command? _nextCommand;
    Command? _previousCommand;

    /// <summary>Turns forward one page (no-op at the last page).</summary>
    public ICommand NextCommand => _nextCommand ??= new Command(Next, () => CanGo(1));

    /// <summary>Turns back one page (no-op at the first page).</summary>
    public ICommand PreviousCommand => _previousCommand ??= new Command(Previous, () => CanGo(-1));

    void RaiseCanGoChanged()
    {
        _nextCommand?.ChangeCanExecute();
        _previousCommand?.ChangeCanExecute();
        _animateToStartCommand?.ChangeCanExecute();
        _animateToEndCommand?.ChangeCanExecute();
    }

    void RaisePageChanged()
    {
        PageChanged?.Invoke(this, CurrentIndex);
        if (PageChangedCommand?.CanExecute(CurrentIndex) == true)
            PageChangedCommand.Execute(CurrentIndex);
        RaiseCanGoChanged();
    }

    // ----------------------------------------------------------------- Public navigation

    public int PageCount => ItemsSource?.Count ?? 0;

    bool CanGo(int dir)
    {
        int target = CurrentIndex + dir;
        return target >= 0 && target < PageCount;
    }

    /// <summary>Animate a forward page turn from the middle of the edge.</summary>
    public void Next() => _ = TurnOnceAsync(1, 0.5f);

    /// <summary>Animate a backward page turn from the middle of the edge.</summary>
    public void Previous() => _ = TurnOnceAsync(-1, 0.5f);

    /// <summary>Animate forward to the next page (fire-and-forget).</summary>
    public void Animate() => _ = AnimateAsync();

    /// <summary>Animate to a view (fire-and-forget). See <see cref="AnimateAsync(int)"/>.</summary>
    public void Animate(int value) => _ = AnimateAsync(value);

    /// <summary>Animate forward to the next page; completes when the turn settles.</summary>
    public Task AnimateAsync() => AnimateToAsync(CurrentIndex + 1, 0.5f);

    /// <summary>
    /// Animate to a view, turning one page at a time at the bound <see cref="TurnSpeed"/>.
    /// A non-negative <paramref name="value"/> is an absolute view index in the collection; a
    /// negative value turns back that many pages from the current view. The destination is clamped
    /// to the collection, so overshooting forward stops at the last view and overshooting back
    /// stops at the first view. Completes when the target is reached or the animation is stopped.
    /// </summary>
    public Task AnimateAsync(int value) => AnimateToAsync(ResolveTargetView(value), 0.5f);

    /// <summary>Animate to a view with the curl gripping from <paramref name="origin"/>
    /// (fire-and-forget). See <see cref="AnimateFromAsync(CurlOrigin, int)"/>.</summary>
    public void AnimateFrom(CurlOrigin origin, int value) => _ = AnimateFromAsync(origin, value);

    /// <summary>
    /// Like <see cref="AnimateAsync(int)"/>, but every turn curls from <paramref name="origin"/>
    /// (corner or middle of the edge). View-number handling is identical: non-negative is an
    /// absolute view index, negative turns back from the current view, and the destination is
    /// clamped so overshooting forward stops at the last view and overshooting back at the first.
    /// </summary>
    public Task AnimateFromAsync(CurlOrigin origin, int value) =>
        AnimateToAsync(ResolveTargetView(value), GrabFraction(origin));

    /// <summary>Animate straight to the first view with a single turn (fire-and-forget). The views
    /// in between are not shown — the first view appears directly beneath the curl.</summary>
    public void AnimateToStart() => _ = AnimateToStartAsync();

    /// <summary>Animate straight to the last view with a single turn (fire-and-forget). The views
    /// in between are not shown — the last view appears directly beneath the curl.</summary>
    public void AnimateToEnd() => _ = AnimateToEndAsync();

    /// <summary>Animate straight to the first view with one backward turn (intermediate views are
    /// not shown); completes when the turn settles or the animation is stopped.</summary>
    public Task AnimateToStartAsync() => JumpToAsync(0);

    /// <summary>Animate straight to the last view with one forward turn (intermediate views are not
    /// shown); completes when the turn settles or the animation is stopped.</summary>
    public Task AnimateToEndAsync() => JumpToAsync(PageCount - 1);

    Task JumpToAsync(int target)
    {
        if (PageCount == 0) return Task.CompletedTask;
        target = Math.Clamp(target, 0, PageCount - 1);
        if (target == CurrentIndex) return Task.CompletedTask;
        int dir = target > CurrentIndex ? 1 : -1;
        return SingleTurnAsync(dir, 0.5f, target);
    }

    async Task AnimateToAsync(int target, float grabYFraction)
    {
        if (PageCount == 0) return;
        target = Math.Clamp(target, 0, PageCount - 1);
        while (CurrentIndex != target)
        {
            int dir = target > CurrentIndex ? 1 : -1;
            if (!await TurnOnceAsync(dir, grabYFraction)) break; // stopped, or couldn't start
        }
    }

    /// <summary>Stop any in-progress turn animation and settle on the current page.</summary>
    public void StopAnimation()
    {
        this.AbortAnimation("curl"); // fires the finished handler with cancelled = true
        _animationTcs?.TrySetResult(false);
        _animating = false;
        _turning = false;
        Invalidate();
    }

    Command? _animateCommand;
    Command? _stopAnimationCommand;

    /// <summary>Command form of <see cref="Animate()"/> / <see cref="Animate(int)"/>. Pass an int
    /// (or numeric string): non-negative = absolute view index, negative = pages back from the
    /// current view; pass nothing to advance one page. Out-of-range values clamp to first/last.</summary>
    public ICommand AnimateCommand => _animateCommand ??= new Command<object?>(param =>
    {
        if (param is int i) Animate(i);
        else if (param is string s && int.TryParse(s, out var j)) Animate(j);
        else Animate();
    });

    /// <summary>Command form of <see cref="StopAnimation"/>.</summary>
    public ICommand StopAnimationCommand => _stopAnimationCommand ??= new Command(StopAnimation);

    Command? _animateToStartCommand;
    Command? _animateToEndCommand;

    /// <summary>Command form of <see cref="AnimateToStart"/>.</summary>
    public ICommand AnimateToStartCommand =>
        _animateToStartCommand ??= new Command(AnimateToStart, () => CanGo(-1));

    /// <summary>Command form of <see cref="AnimateToEnd"/>.</summary>
    public ICommand AnimateToEndCommand =>
        _animateToEndCommand ??= new Command(AnimateToEnd, () => CanGo(1));

    Command? _animateFromCommand;

    /// <summary>Command form of <see cref="AnimateFrom(CurlOrigin, int)"/>. Pass the origin and
    /// view number together, e.g. the string <c>"BottomRight,3"</c> or <c>"TopLeft,-2"</c>
    /// (origin name and signed view number, separated by a comma/space/colon).</summary>
    public ICommand AnimateFromCommand => _animateFromCommand ??= new Command<object?>(param =>
    {
        if (TryParseAnimateFrom(param, out var origin, out var value)) AnimateFrom(origin, value);
    });

    static bool TryParseAnimateFrom(object? param, out CurlOrigin origin, out int value)
    {
        origin = CurlOrigin.Middle;
        value = 0;
        if (param is not string s) return false;
        var parts = s.Split(new[] { ',', ';', ':', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2
            && Enum.TryParse(parts[0], ignoreCase: true, out origin)
            && int.TryParse(parts[1], out value);
    }

    // ----------------------------------------------------------------- Caching / drawing data

    void OnItemsSourceChanged()
    {
        if (CurrentIndex >= PageCount) CurrentIndex = Math.Max(0, PageCount - 1);
        ClearCacheAndRedraw();
        RaiseCanGoChanged();
    }

    void ClearCacheAndRedraw()
    {
        foreach (var bmp in _pageCache.Values) bmp.Dispose();
        _pageCache.Clear();
        Invalidate();
    }

    void Invalidate() => _canvas.InvalidateSurface();

    SKBitmap? GetPage(int index, SKSizeI size)
    {
        if (index < 0 || index >= PageCount) return null;

        if (size != _cacheSize)
        {
            foreach (var bmp in _pageCache.Values) bmp.Dispose();
            _pageCache.Clear();
            _cacheSize = size;
        }

        if (_pageCache.TryGetValue(index, out var cached)) return cached;

        var page = new SKBitmap(size.Width, size.Height);
        using (var c = new SKCanvas(page))
        {
            var bounds = new SKRect(0, 0, size.Width, size.Height);
            c.Clear(PageColor.ToSKColor());
            var ctx = new CurlPageDrawContext
            {
                Canvas = c, Bounds = bounds, Item = ItemsSource![index], Index = index,
            };
            if (PageDrawer is not null) PageDrawer(ctx);
            else if (DrawPage is not null || DrawPageCommand is not null)
            {
                DrawPage?.Invoke(this, ctx);
                if (DrawPageCommand?.CanExecute(ctx) == true) DrawPageCommand.Execute(ctx);
            }
            else DrawDefaultPage(ctx);
        }
        _pageCache[index] = page;
        return page;
    }

    void DrawDefaultPage(CurlPageDrawContext ctx)
    {
        var b = ctx.Bounds;
        float margin = b.Width * 0.10f;

        using var border = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f,
            Color = new SKColor(0, 0, 0, 18), IsAntialias = true,
        };
        ctx.Canvas.DrawRect(b.Left + margin, b.Top + margin,
            b.Width - 2 * margin, b.Height - 2 * margin, border);

        string text = ctx.Item?.ToString() ?? string.Empty;
        using var title = new SKPaint { Color = new SKColor(40, 40, 40), IsAntialias = true };
        using var titleFont = new SKFont { Size = b.Width * 0.16f, Embolden = true };
        ctx.Canvas.DrawText(text, b.MidX, b.MidY, SKTextAlign.Center, titleFont, title);

        using var folio = new SKPaint { Color = new SKColor(120, 120, 120), IsAntialias = true };
        using var folioFont = new SKFont { Size = b.Width * 0.05f };
        ctx.Canvas.DrawText($"{ctx.Index + 1} / {PageCount}", b.MidX, b.Bottom - margin * 0.6f,
            SKTextAlign.Center, folioFont, folio);
    }

    // ----------------------------------------------------------------- Touch

    // The press/move/release logic is shared; each platform feeds it from whichever input source
    // actually delivers touch (SKCanvasView.Touch on mobile, PointerGestureRecognizer on desktop).
    // All positions are in canvas pixels.

    void BeginTurn(SKPoint p)
    {
        if (_animating || _turning) return;
        float w = _canvas.CanvasSize.Width, h = _canvas.CanvasSize.Height;
        if (w <= 0 || h <= 0) return;

        float edgeZone = w * 0.22f; // how far in from an edge counts as "grabbing the edge"

        int dir;
        if (p.X >= w - edgeZone && CanGo(1)) dir = 1;        // right edge -> forward
        else if (p.X <= edgeZone && CanGo(-1)) dir = -1;     // left edge  -> back
        else return;

        _dir = dir;
        _turnTarget = CurrentIndex + dir;
        _grabY = Math.Clamp(p.Y, 0, h);
        _origin = new SKPoint(SpineOppositeX(w), _grabY);
        _grip = _origin;
        _turning = true;
        Invalidate();
    }

    void UpdateTurn(SKPoint p)
    {
        if (!_turning || _animating) return;
        _grip = p;
        Invalidate();
    }

    void EndTurn()
    {
        if (!_turning || _animating) return;
        ReleaseTurn(_canvas.CanvasSize.Width, _canvas.CanvasSize.Height);
    }

#if ANDROID || IOS
    // SKTouchEventArgs.Location is already in canvas pixels. Marking the event Handled is required so
    // the surface keeps sending Moved/Released after the initial Pressed.
    void OnCanvasTouch(object? sender, SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed: BeginTurn(e.Location); break;
            case SKTouchAction.Moved: UpdateTurn(e.Location); break;
            case SKTouchAction.Released:
            case SKTouchAction.Cancelled: EndTurn(); break;
        }
        e.Handled = true;
    }
#else
    void OnPointerPressed(object? sender, PointerEventArgs e) => BeginTurn(ToPixels(e.GetPosition(_canvas)));
    void OnPointerMoved(object? sender, PointerEventArgs e) => UpdateTurn(ToPixels(e.GetPosition(_canvas)));
    void OnPointerReleased(object? sender, PointerEventArgs e) => EndTurn();
#endif

    // X of the free (turning) edge, opposite the spine.
    float SpineOppositeX(float w) => _dir > 0 ? w : 0f;
    // X of the spine the page hinges on.
    float SpineX(float w) => _dir > 0 ? 0f : w;

    float Progress(float w)
    {
        // 0 = flat/at edge, 1 = fully turned over the spine.
        return _dir > 0
            ? Math.Clamp((w - _grip.X) / w, 0f, 1.5f)
            : Math.Clamp(_grip.X / w, 0f, 1.5f);
    }

    void ReleaseTurn(float w, float h)
    {
        bool complete = Progress(w) >= 0.5f;
        if (complete)
        {
            var target = new SKPoint(2 * SpineX(w) - _origin.X, _grabY); // reflection of origin over spine
            AnimateGrip(target, then: () =>
            {
                CurrentIndex = _turnTarget;
                _turning = false;
                Invalidate();
                RaisePageChanged();
            });
        }
        else
        {
            AnimateGrip(_origin, then: () => { _turning = false; Invalidate(); });
        }
    }

    // Animates a single forward/backward turn to the adjacent page, gripping at grabYFraction
    // (0 = top, 1 = bottom). Returns true if completed (false if it couldn't start or was stopped).
    Task<bool> TurnOnceAsync(int dir, float grabYFraction) =>
        SingleTurnAsync(dir, grabYFraction, CurrentIndex + dir);

    // Animates one turn in direction <paramref name="dir"/> that settles on <paramref name="settleOn"/>.
    // The page beneath the curl is <paramref name="settleOn"/>, so callers can jump straight to a
    // distant view without flipping through the pages in between.
    Task<bool> SingleTurnAsync(int dir, float grabYFraction, int settleOn)
    {
        if (_turning || _animating) return Task.FromResult(false);
        float w = _canvas.CanvasSize.Width, h = _canvas.CanvasSize.Height;
        if (w <= 0 || h <= 0 || !CanGo(dir)) return Task.FromResult(false);

        _dir = dir;
        _turnTarget = Math.Clamp(settleOn, 0, PageCount - 1);
        _grabY = Math.Clamp(h * grabYFraction, 0, h);
        float edgeX = SpineOppositeX(w);
        float targetX = 2 * SpineX(w) - edgeX; // mirror of the grip over the spine -> page lies flat
        _origin = new SKPoint(edgeX, _grabY);
        _grip = _origin;
        _turning = true;

        // For a corner grip, bow the path toward the vertical centre mid-turn so the fold reads as
        // a diagonal corner curl, then resolves back to the grip height (a flat page) at the end.
        // For the middle grip the bow is zero, giving a straight edge-to-edge sweep.
        float centerBias = (0.5f - grabYFraction) * h * 0.7f;
        return AnimateAlong(
            t => new SKPoint(
                edgeX + (targetX - edgeX) * t,
                _grabY + centerBias * MathF.Sin(MathF.PI * t)),
            then: () =>
            {
                CurrentIndex = _turnTarget;
                _turning = false;
                Invalidate();
                RaisePageChanged();
            });
    }

    // Resolves an animate "value": non-negative = absolute view index, negative = pages back from
    // the current view. (Clamped to the collection by AnimateToAsync.)
    int ResolveTargetView(int value) => value >= 0 ? value : CurrentIndex + value;

    // Grip height (0 = top, 1 = bottom) for a curl origin. The left/right side of the corner is
    // determined by the turn direction (forward grips the right edge, back grips the left edge).
    static float GrabFraction(CurlOrigin origin) => origin switch
    {
        CurlOrigin.TopLeft or CurlOrigin.TopRight => 0.05f,
        CurlOrigin.BottomLeft or CurlOrigin.BottomRight => 0.95f,
        _ => 0.5f, // Middle
    };

    // Linearly animates the grip from its current position to <paramref name="target"/>.
    Task<bool> AnimateGrip(SKPoint target, Action then)
    {
        var start = _grip;
        return AnimateAlong(
            t => new SKPoint(start.X + (target.X - start.X) * t, start.Y + (target.Y - start.Y) * t),
            then);
    }

    // Drives the grip along <paramref name="path"/> (t in 0..1) over the bound TurnSpeed. Runs
    // <paramref name="then"/> only if the animation finished normally (not stopped); the task
    // result is true when it completed.
    Task<bool> AnimateAlong(Func<float, SKPoint> path, Action then)
    {
        _animating = true;
        var tcs = new TaskCompletionSource<bool>();
        _animationTcs = tcs;
        this.Animate("curl",
            v => { _grip = path((float)v); Invalidate(); },
            16, TurnDurationMs, Easing.CubicOut,
            (_, cancelled) =>
            {
                _animating = false;
                if (!cancelled) then();
                tcs.TrySetResult(!cancelled);
            });
        return tcs.Task;
    }

    TaskCompletionSource<bool>? _animationTcs;

    // ----------------------------------------------------------------- Rendering

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var surface = e.Surface.Canvas;
        var info = e.Info;
        var size = new SKSizeI(info.Width, info.Height);
        surface.Clear(PageColor.ToSKColor());

        var current = GetPage(CurrentIndex, size);

        if (!_turning || current is null)
        {
            if (current is not null) surface.DrawBitmap(current, 0, 0);
        }
        else
        {
            var beneath = GetPage(_turnTarget, size);
            DrawCurl(surface, info.Width, info.Height, current, beneath);
        }
    }

    void DrawCurl(SKCanvas canvas, float w, float h, SKBitmap front, SKBitmap? background)
    {
        var pageRect = new SKRect(0, 0, w, h);

        // The page underneath (the page we're turning toward).
        if (background is not null) canvas.DrawBitmap(background, 0, 0);

        SKPoint g = _origin;   // gripped point started here, on the free edge
        SKPoint d = _grip;     // ...and is now here
        var gd = new SKPoint(d.X - g.X, d.Y - g.Y);
        float len = MathF.Sqrt(gd.X * gd.X + gd.Y * gd.Y);
        if (len < 0.5f)
        {
            // Barely moved: just show the flat current page.
            canvas.DrawBitmap(front, 0, 0);
            return;
        }

        SKPoint mid = new((g.X + d.X) / 2f, (g.Y + d.Y) / 2f);  // a point on the fold line
        SKPoint nf = new((g.X - d.X) / len, (g.Y - d.Y) / len); // unit normal: fold line -> free edge (flap side)
        SKPoint u = new(-nf.Y, nf.X);                           // unit direction along the fold line

        SKMatrix reflection = ReflectAcrossLine(mid, u);

        const float big = 8000f;
        // Half-plane on the spine side of the fold line (the part of the page that stays flat).
        using var flatPath = HalfPlane(mid, u, nf, -1, big);
        // Half-plane on the free-edge side (the part that lifts and folds back).
        using var flapPath = HalfPlane(mid, u, nf, +1, big);

        // 1) The still-flat part of the current page.
        canvas.Save();
        canvas.ClipPath(flatPath, antialias: true);
        canvas.DrawBitmap(front, 0, 0);
        canvas.Restore();

        // 2) Soft contact shadow the lifted flap casts into the crease, on the page beneath.
        //    Wide, low-opacity, multi-stop falloff + a blur so it reads as diffuse, not a hard band.
        float shadowW = Math.Min(w, h) * 0.20f;
        using (var creaseShadow = new SKPaint { IsAntialias = true })
        {
            var p0 = mid;
            var p1 = new SKPoint(mid.X + nf.X * shadowW, mid.Y + nf.Y * shadowW);
            creaseShadow.Shader = SKShader.CreateLinearGradient(
                p0, p1,
                new[] { new SKColor(0, 0, 0, 55), new SKColor(0, 0, 0, 22), new SKColor(0, 0, 0, 0) },
                new[] { 0f, 0.4f, 1f },
                SKShaderTileMode.Clamp);
            creaseShadow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, shadowW * 0.18f);
            canvas.Save();
            canvas.ClipPath(flapPath, antialias: true);
            canvas.DrawRect(pageRect, creaseShadow);
            canvas.Restore();
        }

        // 3) The folded-back flap: paper back + a faint ghost of the printed side + curl shading.
        canvas.Save();
        canvas.SetMatrix(reflection);
        canvas.ClipPath(flapPath, antialias: true);

        using (var paper = new SKPaint { Color = PageColor.ToSKColor(), IsAntialias = true })
            canvas.DrawRect(pageRect, paper);

        using (var ghost = new SKPaint { Color = new SKColor(255, 255, 255, 255), IsAntialias = true })
        {
            // Show the printed content very faintly, mirrored, as ink soaking through paper.
            ghost.ColorFilter = SKColorFilter.CreateBlendMode(
                new SKColor(0, 0, 0, 28), SKBlendMode.SrcATop);
            canvas.DrawBitmap(front, 0, 0, ghost);
        }

        // Curl shading: gently darker toward the leading (outer) edge of the flap, with a soft
        // ramp so the back of the page looks rounded rather than flatly tinted.
        using (var curlShade = new SKPaint { IsAntialias = true })
        {
            curlShade.Shader = SKShader.CreateLinearGradient(
                mid, g,
                new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 18), new SKColor(0, 0, 0, 48) },
                new[] { 0f, 0.55f, 1f },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(pageRect, curlShade);
        }
        canvas.Restore();

        // 4) A soft highlight along the fold so the crease reads as a gently rounded ridge.
        using (var crease = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = Math.Min(w, h) * 0.012f, IsAntialias = true,
            Color = new SKColor(255, 255, 255, 70),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Min(w, h) * 0.01f),
        })
        {
            var a = new SKPoint(mid.X + u.X * big, mid.Y + u.Y * big);
            var b = new SKPoint(mid.X - u.X * big, mid.Y - u.Y * big);
            canvas.Save();
            canvas.ClipRect(pageRect);
            canvas.DrawLine(a, b, crease);
            canvas.Restore();
        }
    }

    // Reflection across the line through <paramref name="p"/> with unit direction <paramref name="u"/>.
    static SKMatrix ReflectAcrossLine(SKPoint p, SKPoint u)
    {
        float a = 2 * u.X * u.X - 1; // cos 2θ
        float b = 2 * u.X * u.Y;     // sin 2θ
        return new SKMatrix
        {
            ScaleX = a, SkewX = b, TransX = p.X - a * p.X - b * p.Y,
            SkewY = b, ScaleY = -a, TransY = p.Y - b * p.X + a * p.Y,
            Persp2 = 1,
        };
    }

    // A large quad covering one side of the fold line. side=-1 spine side, +1 free-edge side.
    static SKPath HalfPlane(SKPoint mid, SKPoint u, SKPoint nf, int side, float big)
    {
        var n = new SKPoint(nf.X * side, nf.Y * side);
        var path = new SKPath();
        path.MoveTo(mid.X + u.X * big, mid.Y + u.Y * big);
        path.LineTo(mid.X - u.X * big, mid.Y - u.Y * big);
        path.LineTo(mid.X - u.X * big + n.X * big, mid.Y - u.Y * big + n.Y * big);
        path.LineTo(mid.X + u.X * big + n.X * big, mid.Y + u.Y * big + n.Y * big);
        path.Close();
        return path;
    }
}
