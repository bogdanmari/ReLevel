using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Prototype;

// A detached snapshot and window-local choices; deliberately no Revit API references.
internal sealed class PrototypeLevel(long id, string name, double elevation) : INotifyPropertyChanged
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    public double Elevation { get; } = elevation;
    public string ElevationText => Elevation.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    private bool keep, protect, selected, hidden;
    private int elementCount;
    public int ElementCount { get => elementCount; set { elementCount = value; Changed(); } }
    private int modelElementCount;
    private bool analysisIncomplete;
    public int ModelElementCount { get => modelElementCount; set { modelElementCount = value; Changed(); Changed(nameof(NameWithCount)); } }
    public bool AnalysisIncomplete { get => analysisIncomplete; set { analysisIncomplete = value; Changed(); Changed(nameof(NameWithCount)); } }
    public string NameWithCount => $"{Name} ({ModelElementCount}{(AnalysisIncomplete ? "*" : "")})";
    private Color color = Colors.SteelBlue;
    public bool Keep { get => keep; set { keep = value; Changed(); } }
    public bool Protect { get => protect; set { protect = value; Changed(); } }
    public bool Selected { get => selected; set { selected = value; Changed(); } }
    public bool Hidden { get => hidden; set { hidden = value; Changed(); } }
    public Color Color { get => color; set { color = value; Changed(); Changed(nameof(ColorBrush)); Changed(nameof(ColorHex)); } }
    public SolidColorBrush ColorBrush => new(Color);
    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
