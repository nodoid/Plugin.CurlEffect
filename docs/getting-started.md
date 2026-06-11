# Getting started

A full MVVM walk-through building a small book reader with `CurlView`.

## 1. Install and register

```sh
dotnet add package Plugin.CurlEffect
```

```csharp
// MauiProgram.cs
using CurlEffect.Controls;

builder
    .UseMauiApp<App>()
    .UseCurlEffect();   // also registers SkiaSharp
```

## 2. A view model

`ItemsSource` is any `IList`. `CurrentIndex` is two-way bindable, so the view model always knows the
page on screen — whether it changed by a drag, a button or a command.

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class ReaderViewModel : INotifyPropertyChanged
{
    public ObservableCollection<string> Pages { get; } = new()
    {
        "Chapter One", "The Curl", "Page Three", "Halfway", "Almost There", "The End",
    };

    int _index;
    public int Index
    {
        get => _index;
        set { if (_index != value) { _index = value; OnPropertyChanged(); OnPropertyChanged(nameof(Caption)); } }
    }

    public string Caption => $"{Index + 1} of {Pages.Count}";

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

## 3. The page

The control fills its container, so give it room (here a `Border` for rounded corners). The buttons
drive the control through its commands.

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:controls="clr-namespace:CurlEffect.Controls;assembly=CurlEffect"
             x:Class="MyApp.ReaderPage">

    <Grid RowDefinitions="*,Auto" Padding="16">

        <Border Grid.Row="0" StrokeShape="RoundRectangle 6">
            <controls:CurlView x:Name="Curl"
                               ItemsSource="{Binding Pages}"
                               CurrentIndex="{Binding Index, Mode=TwoWay}"
                               TurnSpeed="Normal" />
        </Border>

        <HorizontalStackLayout Grid.Row="1" Spacing="12" HorizontalOptions="Center">
            <Button Text="◀ Back"
                    Command="{Binding Source={x:Reference Curl}, Path=PreviousCommand}" />
            <Label Text="{Binding Caption}" VerticalOptions="Center" />
            <Button Text="Next ▶"
                    Command="{Binding Source={x:Reference Curl}, Path=NextCommand}" />
            <Button Text="⏭ End"
                    Command="{Binding Source={x:Reference Curl}, Path=AnimateToEndCommand}" />
        </HorizontalStackLayout>
    </Grid>
</ContentPage>
```

```csharp
public partial class ReaderPage : ContentPage
{
    public ReaderPage()
    {
        InitializeComponent();
        BindingContext = new ReaderViewModel();
    }
}
```

That's a working reader: drag the pages, or use the buttons. `NextCommand`/`PreviousCommand` and the
`AnimateTo…` commands disable themselves automatically at the ends of the book.

## 4. Reacting to page changes

Bind `PageChangedCommand` (or handle the `PageChanged` event) to run code after each turn:

```xml
<controls:CurlView ...
    PageChangedCommand="{Binding PageTurnedCommand}" />
```

```csharp
public ICommand PageTurnedCommand => new Command<int>(index =>
{
    // e.g. persist reading position, fetch the next chapter, analytics…
});
```

## Next

- [API reference](api-reference.md)
- [Customizing pages](customizing-pages.md)
