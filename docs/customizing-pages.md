# Customizing pages

Every page is painted onto an `SKCanvas`, so a page can be text, shapes, images — anything SkiaSharp
can draw. You supply the drawing in one of three ways:

1. **`PageDrawer`** — an `Action<CurlPageDrawContext>` (simplest, set in code).
2. **`DrawPage`** event — `EventHandler<CurlPageDrawContext>`.
3. **`DrawPageCommand`** — bind a command that receives the `CurlPageDrawContext`.

If none is supplied, a default drawer renders the item's `ToString()` and a page number.

The `CurlPageDrawContext` gives you `Canvas`, `Bounds` (the page rectangle, in pixels), `Item` (the
`ItemsSource` item) and `Index`.

## Paper colour

`PageColor` (bindable) is used for the page background, the spine, and the back of the turning page.
Set it to match your design — e.g. pure white for a document, or a cream for a book:

```xml
<controls:CurlView PageColor="#FFFFFFFF" ... />
```

When you clear the canvas in your drawer, clearing to `PageColor` keeps the page and its folded back
face consistent.

## Drawing text

```csharp
Curl.PageDrawer = ctx =>
{
    ctx.Canvas.Clear(SKColors.White);

    using var title = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using var titleFont = new SKFont { Size = ctx.Bounds.Width * 0.12f, Embolden = true };
    ctx.Canvas.DrawText(ctx.Item?.ToString(), ctx.Bounds.MidX, ctx.Bounds.MidY,
        SKTextAlign.Center, titleFont, title);
};
```

## Drawing an image

Pages are commonly images (a scanned book, a PDF render, a photo). Decode once and cache the
`SKBitmap`, then blit it to fit the page:

```csharp
readonly Dictionary<string, SKBitmap> _cache = new();

Curl.PageDrawer = ctx =>
{
    ctx.Canvas.Clear(SKColors.Black);

    if (ctx.Item is string path)
    {
        if (!_cache.TryGetValue(path, out var bmp))
            _cache[path] = bmp = SKBitmap.Decode(path);

        if (bmp is not null)
            ctx.Canvas.DrawBitmap(bmp, ctx.Bounds, new SKSamplingOptions(SKFilterMode.Linear));
    }
};
```

> **Performance:** the drawer can be called every frame during a turn, so do the expensive work
> (decoding, layout) once and cache it — draw only cheap blits/paints inside the delegate.

## Per-item types

`ItemsSource` is an `IList` of anything. Branch on the item type to render different page kinds:

```csharp
Curl.PageDrawer = ctx =>
{
    switch (ctx.Item)
    {
        case TextPage t:  DrawText(ctx, t);  break;
        case ImagePage i: DrawImage(ctx, i); break;
        case CoverPage c: DrawCover(ctx, c); break;
    }
};
```

## MVVM-friendly drawing

To keep drawing out of code-behind, bind `DrawPageCommand`:

```xml
<controls:CurlView ItemsSource="{Binding Pages}"
                   DrawPageCommand="{Binding DrawPageCommand}" />
```

```csharp
public ICommand DrawPageCommand => new Command<CurlPageDrawContext>(ctx =>
{
    // paint ctx.Item onto ctx.Canvas within ctx.Bounds
});
```

## Back to

- [Getting started](getting-started.md)
- [API reference](api-reference.md)
