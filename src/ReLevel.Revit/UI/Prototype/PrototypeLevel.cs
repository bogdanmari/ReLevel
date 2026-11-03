using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Prototype;

// A detached snapshot and window-local choices; deliberately no Revit API references.
internal sealed class PrototypeLevel(long id, string name, double elevation) : INotifyPropertyChanged
{
    private bool inComparison;
    public bool InComparison { get => inComparison; set { inComparison = value; Changed(); } }
    public long Id { get; } = id;
    public string Name { get; } = name;
    public double Elevation { get; } = elevation;
    // Elevation is always Revit internal feet; formatting is supplied by the workspace.
    private Func<double, string> formatElevation = value => value.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
    public string ElevationText => formatElevation(Elevation);
    public void SetElevationFormatter(Func<double, string> formatter) { formatElevation = formatter; RefreshUnits(); }
    public void RefreshUnits() => Changed(nameof(ElevationText));
    private bool keep, protect, selected, hidden;
    private int elementCount;
    public int ElementCount { get => elementCount; set { elementCount = value; Changed(); } }
    private int modelElementCount;
    private int viewCount;
    public int ViewCount { get => viewCount; set { viewCount = value; Changed(); } }
    public string ModelElementCountText => $"{ModelElementCount}{(AnalysisIncomplete ? "*" : "")}";
    private bool analysisIncomplete;
    public int ModelElementCount { get => modelElementCount; set { modelElementCount = value; Changed(); Changed(nameof(NameWithCount)); Changed(nameof(ModelElementCountText)); } }
    public bool AnalysisIncomplete { get => analysisIncomplete; set { analysisIncomplete = value; Changed(); Changed(nameof(NameWithCount)); Changed(nameof(ModelElementCountText)); } }
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
