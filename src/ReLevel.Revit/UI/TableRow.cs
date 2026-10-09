using System.ComponentModel;

namespace ReLevel.Revit.UI;

internal sealed class TableRow(Action selectionChanged) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool isChecked;
    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (isChecked == value || (value && !CanCheck)) return;
            isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            selectionChanged();
        }
    }
    public bool CanCheck { get; init; } = true;
    public long Id { get; init; }
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string Level { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
}
