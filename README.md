# CurlEffect

[![NuGet](https://img.shields.io/nuget/v/Plugin.CurlEffect.svg)](https://www.nuget.org/packages/Plugin.CurlEffect)

A bindable, **book-style page-curl control for .NET MAUI**, rendered with
[SkiaSharp](https://github.com/mono/SkiaSharp). Drag from any edge to turn a page like a real book,
or drive it programmatically with sync / `async` / `ICommand` APIs.

- 📖 Realistic page fold with soft shadow, paper back-face and crease highlight
- 👆 Drag from **anywhere** along either vertical edge — right to go forward, left to go back
- 🔗 Fully bindable: `ItemsSource`, two-way `CurrentIndex`, `TurnSpeed`
- ⚙️ Programmatic turns: `Next`, `Animate`, `AnimateFrom`, `AnimateToStart/End`, `StopAnimation` — each as a method, an `…Async`, and an `ICommand`
- 🧩 Contract exposed as `ICurlView : IView` with extension methods usable on **any** view
- 🖼️ Pages are drawn by you on an `SKCanvas`, so a page can be text, images, or anything

| | |
|---|---|
| **Package** | [`Plugin.CurlEffect`](https://www.nuget.org/packages/Plugin.CurlEffect) |
| **Namespace** | `CurlEffect.Controls` |
| **Target frameworks** | .NET 10 — Android, iOS, Mac Catalyst, Windows |

---

## Contents

- [Install](#install)
- [Set up](#set-up)
- [Quick start](#quick-start)
- [Drawing page content](#drawing-page-content)
- [Turning pages](#turning-pages)
  - [By gesture](#by-gesture)
  - [Programmatically](#programmatically)
  - [Turn speed](#turn-speed)
- [Events](#events)
- [Use from any `IView`](#use-from-any-iview)
- [Further docs](#further-docs)
- [Requirements & license](#requirements--license)

---

## Install

```sh
dotnet add package Plugin.CurlEffect
```

## Set up

Register the plugin in `MauiProgram.cs` — `UseCurlEffect()` also wires up SkiaSharp for you:

```csharp
using CurlEffect.Controls;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .UseCurlEffect();          // <-- registers everything CurlView needs

    return builder.Build();
}
```

## Quick start

Add the namespace and drop a `CurlView` into your page:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:controls="clr-namespace:CurlEffect.Controls;assembly=CurlEffect"
             x:Class="MyApp.MainPage">

    <controls:CurlView x:Name="Curl"
                       ItemsSource="{Binding Pages}"
                       CurrentIndex="{Binding Index, Mode=TwoWay}"
                       TurnSpeed="Normal" />
</ContentPage>
```

```csharp
public class MainViewModel
{
    public ObservableCollection<string> Pages { get; } = new()
    {
        "Chapter One", "The Curl", "Page Three", "The End",
    };

    public int Index { get; set; }
}
```

Out of the box each item is rendered with a default text drawer (the item's `ToString()` plus a page
number), so the snippet above already turns pages. Replace it with your own drawing as shown next.

> **Note:** the XAML namespace must include `;assembly=CurlEffect` — the control ships in the
> `CurlEffect` assembly (the NuGet package id `Plugin.CurlEffect` is separate from the assembly name).

## Drawing page content

A page is painted onto an `SKCanvas`, so it can be anything you can draw. Provide a `PageDrawer`
delegate (or handle the `DrawPage` event / bind `DrawPageCommand`):

```csharp
Curl.PageDrawer = ctx =>
{
    // ctx.Canvas : SKCanvas   ctx.Bounds : SKRect   ctx.Item : object?   ctx.Index : int
    ctx.Canvas.Clear(SKColors.White);

    using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using var font = new SKFont { Size = ctx.Bounds.Width * 0.1f };
    ctx.Canvas.DrawText(ctx.Item?.ToString(), ctx.Bounds.MidX, ctx.Bounds.MidY,
        SKTextAlign.Center, font, paint);
};
```

`PageColor` (bindable) sets the paper colour used for the background, the spine and the back of the
turning page (default is a warm off-white, `#FFFDF6E3`).

See [docs/customizing-pages.md](docs/customizing-pages.md) for drawing images, caching and theming.

## Turning pages

### By gesture

Drag from **anywhere along a vertical edge**:

- **Right edge → forward** (next view)
- **Left edge → back** (previous view)

Release past the halfway point to complete the turn, or before it to spring back. The grab height is
honoured, so the curl originates from wherever you touch.

### Programmatically

Every navigation action exists three ways — a **method**, an **`…Async`** variant that completes
when the turn settles, and an **`ICommand`** for MVVM:

| Method | Async | Command | What it does |
|---|---|---|---|
| `Next()` / `Previous()` | — | `NextCommand` / `PreviousCommand` | Turn one page |
| `Animate()` | `AnimateAsync()` | `AnimateCommand` (no parameter) | Turn forward one page |
| `Animate(value)` | `AnimateAsync(value)` | `AnimateCommand` (int / string) | Animate to a view — see semantics below |
| `AnimateFrom(origin, value)` | `AnimateFromAsync(origin, value)` | `AnimateFromCommand` (`"Origin,value"`) | As above, gripping from a corner/edge |
| `AnimateToStart()` | `AnimateToStartAsync()` | `AnimateToStartCommand` | Jump to first view in one turn |
| `AnimateToEnd()` | `AnimateToEndAsync()` | `AnimateToEndCommand` | Jump to last view in one turn |
| `StopAnimation()` | — | `StopAnimationCommand` | Halt an in-progress turn and settle |

**`Animate(value)` view-number semantics**

- `value >= 0` → **absolute** view index in the collection
- `value < 0` → **relative**, that many pages **back** from the current view
- The destination is **clamped**: overshooting forward stops at the **last** view, overshooting back stops at the **first** view

```csharp
Curl.Animate(3);     // go to index 3
Curl.Animate(-2);    // go back two pages
Curl.Animate(99);    // clamped -> last view
await Curl.AnimateAsync(0);   // go to first view, await completion
```

**`AnimateToStart` / `AnimateToEnd`** turn a single page straight to the first/last view — the views
in between are **not** shown; the destination is rendered directly beneath the curl.

**`AnimateFrom(origin, value)`** uses the same view-number semantics but the curl grips from a
`CurlOrigin`: `TopLeft`, `TopRight`, `Middle`, `BottomRight`, `BottomLeft`. Corner origins produce a
diagonal corner curl; `Middle` is a straight edge-to-edge sweep.

```csharp
await Curl.AnimateFromAsync(CurlOrigin.TopRight, 5);
```

In XAML, pass the origin and view number together as a string:

```xml
<Button Text="To End from corner"
        Command="{Binding Source={x:Reference Curl}, Path=AnimateFromCommand}"
        CommandParameter="BottomRight,99" />
```

### Turn speed

`TurnSpeed` is bindable and applies to every animated turn (including each step of a multi-page
animation):

| `CurlTurnSpeed` | Duration |
|---|---|
| `Fast` | 160 ms |
| `Normal` (default) | 320 ms |
| `Slow` | 600 ms |

## Events

| Event | Command equivalent | Fires |
|---|---|---|
| `PageChanged` (`EventHandler<int>`) | `PageChangedCommand` | After a turn completes and `CurrentIndex` changes (arg = new index) |
| `DrawPage` (`EventHandler<CurlPageDrawContext>`) | `DrawPageCommand` | When a page needs painting (alternative to `PageDrawer`) |

## Use from any `IView`

The contract is `ICurlView : IView`, and the API is also available as **extension methods on
`IView`**, so you can drive a curl from any view reference. Each is a safe no-op when the view isn't
curl-capable:

```csharp
using CurlEffect.Controls;

IView view = GetSomeView();

await view.AnimateToEndAsync();
view.AnimateFrom(CurlOrigin.TopRight, 0);
if (view.IsCurlView()) view.Next();

ICurlView? curl = view.AsCurlView();   // typed access, or null
```

## Further docs

- [Getting started](docs/getting-started.md) — full MVVM walk-through
- [API reference](docs/api-reference.md) — every public member
- [Customizing pages](docs/customizing-pages.md) — images, caching, theming

## Requirements & license

- .NET 10 MAUI (Android, iOS, Mac Catalyst, Windows)
- Depends on `Microsoft.Maui.Controls` and `SkiaSharp.Views.Maui.Controls`

Licensed under the [MIT License](LICENSE).
