# CurlEffect

A bindable, book-style **page-curl control for .NET MAUI**, rendered with SkiaSharp. Drag from any
edge to turn pages, or drive it programmatically with sync / async / `ICommand` APIs.

## Install

```sh
dotnet add package CurlEffect
```

Register SkiaSharp in your `MauiProgram`:

```csharp
using SkiaSharp.Views.Maui.Controls.Hosting;

builder.UseMauiApp<App>().UseSkiaSharp();
```

## Usage

```xml
xmlns:controls="clr-namespace:CurlEffect.Controls;assembly=CurlEffect"

<controls:CurlView x:Name="Curl"
                   ItemsSource="{Binding Pages}"
                   CurrentIndex="{Binding Index, Mode=TwoWay}"
                   TurnSpeed="Normal" />
```

Each page is painted by a `PageDrawer` / `DrawPage` callback (a default text drawer is used
otherwise), so a page can be anything you can draw on an `SKCanvas`.

## Navigation API

All of the following exist as a **method**, an **`...Async`** variant, and an **`ICommand`**:

| API | Behaviour |
| --- | --- |
| `Next()` / `Previous()` | Turn one page |
| `Animate(value)` | Animate to a view — non-negative = absolute index, negative = pages back from current; clamps to first/last |
| `AnimateFrom(origin, value)` | As above, but the curl grips from a `CurlOrigin` (`TopLeft`, `TopRight`, `Middle`, `BottomRight`, `BottomLeft`) |
| `AnimateToStart()` / `AnimateToEnd()` | Single-turn jump to the first/last view (intermediate views are not shown) |
| `StopAnimation()` | Halt an in-progress turn and settle |

`TurnSpeed` (`Fast` / `Normal` / `Slow`) is bindable and applies to every animated turn.

## Use from any `IView`

The contract is exposed as `ICurlView : IView`, with extension methods on `IView` so the API can be
called on any view reference (a safe no-op when the view isn't curl-capable):

```csharp
using CurlEffect.Controls;

IView view = GetSomeView();
await view.AnimateToEndAsync();
view.AnimateFrom(CurlOrigin.TopRight, 0);
if (view.IsCurlView()) view.Next();
```

## License

MIT
