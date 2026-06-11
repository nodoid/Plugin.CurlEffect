# API reference

Namespace: `CurlEffect.Controls` (assembly `CurlEffect`, package `Plugin.CurlEffect`).

- [`CurlView`](#curlview)
  - [Bindable properties](#bindable-properties)
  - [Properties](#properties)
  - [Navigation methods](#navigation-methods)
  - [Async methods](#async-methods)
  - [Commands](#commands)
  - [Events](#events)
- [`ICurlView`](#icurlview)
- [`CurlViewExtensions`](#curlviewextensions)
- [`CurlEffectHostBuilderExtensions`](#curleffecthostbuilderextensions)
- [`CurlPageDrawContext`](#curlpagedrawcontext)
- [Enums](#enums)

---

## `CurlView`

`public class CurlView : ContentView, ICurlView`

A book-style page-turn control. Pages come from `ItemsSource`; the visible page is `CurrentIndex`.
Drag from the right edge to turn forward or the left edge to turn back.

### Bindable properties

| Property | Type | Default | Notes |
|---|---|---|---|
| `ItemsSource` | `IList?` | `null` | The pages. Changing it clears the render cache and clamps `CurrentIndex`. |
| `CurrentIndex` | `int` | `0` | The visible view. `BindingMode.TwoWay` by default. |
| `PageColor` | `Color` | `#FFFDF6E3` | Paper colour — background, spine and the back of the turning page. |
| `TurnSpeed` | `CurlTurnSpeed` | `Normal` | Duration of animated turns. |
| `DrawPageCommand` | `ICommand?` | `null` | Command form of `DrawPage`; executed with a `CurlPageDrawContext`. |
| `PageChangedCommand` | `ICommand?` | `null` | Command form of `PageChanged`; executed with the new index. |

### Properties

| Member | Type | Notes |
|---|---|---|
| `PageDrawer` | `Action<CurlPageDrawContext>?` | Per-page painter. If null, `DrawPage`/`DrawPageCommand` are used, else a default text drawer. |
| `PageCount` | `int` (get) | `ItemsSource?.Count ?? 0`. |

### Navigation methods

| Method | Description |
|---|---|
| `void Next()` | Animate one page forward. |
| `void Previous()` | Animate one page back. |
| `void Animate()` | Animate forward to the next view. |
| `void Animate(int value)` | Animate to a view. `value >= 0` = absolute index; `value < 0` = pages back from the current view. Clamped to the collection (overshoot → first/last view). |
| `void AnimateFrom(CurlOrigin origin, int value)` | As `Animate(int)`, but the curl grips from `origin`. |
| `void AnimateToStart()` | Single-turn jump to the first view; intermediate views are not shown. |
| `void AnimateToEnd()` | Single-turn jump to the last view; intermediate views are not shown. |
| `void StopAnimation()` | Stop any in-progress turn and settle on the current view. |

### Async methods

Each returns a `Task` that completes when the turn settles **or** the animation is stopped.

| Method | Description |
|---|---|
| `Task AnimateAsync()` | Forward to the next view. |
| `Task AnimateAsync(int value)` | Same semantics as `Animate(int)`. |
| `Task AnimateFromAsync(CurlOrigin origin, int value)` | Same semantics as `AnimateFrom`. |
| `Task AnimateToStartAsync()` | Single-turn jump to the first view. |
| `Task AnimateToEndAsync()` | Single-turn jump to the last view. |

### Commands

| Command | Parameter | Notes |
|---|---|---|
| `NextCommand` | — | `CanExecute` false on the last view. |
| `PreviousCommand` | — | `CanExecute` false on the first view. |
| `AnimateCommand` | `int`, numeric `string`, or none | None = next; otherwise the `Animate(int)` semantics. |
| `AnimateFromCommand` | `string` `"Origin,value"` | e.g. `"BottomRight,3"`, `"TopLeft,-2"` (separator `,` `;` `:` or space). |
| `AnimateToStartCommand` | — | `CanExecute` false on the first view. |
| `AnimateToEndCommand` | — | `CanExecute` false on the last view. |
| `StopAnimationCommand` | — | |

### Events

| Event | Type | Fires |
|---|---|---|
| `PageChanged` | `EventHandler<int>` | After a turn completes and `CurrentIndex` changes (arg = new index). |
| `DrawPage` | `EventHandler<CurlPageDrawContext>` | When a page needs painting (alternative to `PageDrawer`). |

---

## `ICurlView`

`public interface ICurlView : IView`

The curl navigation contract, implemented by `CurlView`. Because it derives from `IView`, any
IView-based control can expose curl behaviour by implementing it.

```csharp
IList? ItemsSource { get; set; }
int CurrentIndex { get; set; }
int PageCount { get; }
CurlTurnSpeed TurnSpeed { get; set; }

Task AnimateAsync();
Task AnimateAsync(int value);
Task AnimateFromAsync(CurlOrigin origin, int value);
Task AnimateToStartAsync();
Task AnimateToEndAsync();
void StopAnimation();
```

---

## `CurlViewExtensions`

`public static class CurlViewExtensions` — extension methods on `IView`. Each dispatches to the
view's `ICurlView` implementation and is a safe no-op (completed task) when the view isn't
curl-capable.

| Extension | Returns |
|---|---|
| `IsCurlView(this IView)` | `bool` |
| `AsCurlView(this IView)` | `ICurlView?` |
| `NextAsync(this IView)` | `Task` |
| `PreviousAsync(this IView)` | `Task` |
| `AnimateAsync(this IView, int value)` | `Task` |
| `AnimateFromAsync(this IView, CurlOrigin, int)` | `Task` |
| `AnimateToStartAsync(this IView)` | `Task` |
| `AnimateToEndAsync(this IView)` | `Task` |
| `Next` / `Previous` / `Animate(int)` / `AnimateFrom(CurlOrigin,int)` / `AnimateToStart` / `AnimateToEnd` / `StopAnimation` | `void` (fire-and-forget) |

> On a concrete `CurlView`, its instance methods are used in preference to these extensions; the
> extensions matter when you hold an `IView` reference.

---

## `CurlEffectHostBuilderExtensions`

`public static class CurlEffectHostBuilderExtensions`

| Extension | Description |
|---|---|
| `MauiAppBuilder UseCurlEffect(this MauiAppBuilder)` | Registers everything `CurlView` needs (SkiaSharp). Call in `MauiProgram` instead of registering SkiaSharp yourself. |

---

## `CurlPageDrawContext`

Passed to `PageDrawer` / `DrawPage` / `DrawPageCommand` so you can paint a page. All values are in
device pixels.

| Member | Type | Notes |
|---|---|---|
| `Canvas` | `SKCanvas` | Draw the page here. |
| `Bounds` | `SKRect` | The full page rectangle. |
| `Item` | `object?` | The `ItemsSource` item for this page. |
| `Index` | `int` | The page index. |

---

## Enums

### `CurlTurnSpeed`

| Value | Duration |
|---|---|
| `Fast` | 160 ms |
| `Normal` | 320 ms |
| `Slow` | 600 ms |

### `CurlOrigin`

Where a programmatic curl grips: `TopLeft`, `TopRight`, `Middle`, `BottomRight`, `BottomLeft`.
Corner values produce a diagonal corner curl; `Middle` is a straight edge-to-edge sweep. The
left/right side of the grip follows the turn direction (forward grips the right edge, back grips the
left edge), so the vertical position is what each value controls.
