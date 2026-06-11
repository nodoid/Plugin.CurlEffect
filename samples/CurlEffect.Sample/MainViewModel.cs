using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CurlEffect.Controls;

namespace CurlEffect;

public class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<string> Pages { get; } = new()
    {
        "Chapter One", "The Curl", "Page Three", "Halfway", "Almost There", "The End",
    };

    public CurlTurnSpeed[] Speeds { get; } =
        { CurlTurnSpeed.Fast, CurlTurnSpeed.Normal, CurlTurnSpeed.Slow };

    CurlTurnSpeed _speed = CurlTurnSpeed.Normal;
    public CurlTurnSpeed Speed
    {
        get => _speed;
        set { if (_speed != value) { _speed = value; OnPropertyChanged(); } }
    }

    int _index;
    public int Index
    {
        get => _index;
        set { if (_index != value) { _index = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageLabel)); } }
    }

    public string PageLabel => $"{Index + 1} of {Pages.Count}";

    // Demonstrates the ICommand equivalent of the PageChanged event.
    public ICommand PageChangedCommand => new Command<int>(i => Index = i);

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
