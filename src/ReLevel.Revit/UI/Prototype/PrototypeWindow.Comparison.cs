using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.UI.Prototype;

internal sealed partial class PrototypeWindow
{
    private void HighlightComparedLevels(DataGrid table)
    {
        var style = new Style(typeof(DataGridCell));
        var match = new DataTrigger { Binding = new Binding(nameof(PrototypeLevel.InComparison)), Value = true };
        match.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#C4EDE3")));
        style.Triggers.Add(match);
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, SystemColors.HighlightBrush));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, SystemColors.HighlightTextBrush));
        style.Triggers.Add(selected); table.CellStyle = style;
    }

    private FrameworkElement ComparisonPage()
    {
        var page = Page("Compare", out var content);
        var fileName = new TextBlock { Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        var table = Table();
        var rows = new System.Collections.ObjectModel.ObservableCollection<LevelComparisonResult>();
        table.ItemsSource = new ListCollectionView(rows);
        foreach (var (key, property) in new[] { ("Name", "Name"), ("FileElevation", "FileElevation"),
            ("ModelElevation", "ModelElevation"), ("CompareStatus", "StatusKey") })
        {
            var cell = new FrameworkElementFactory(typeof(TextBlock));
            cell.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0));
            cell.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cell.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            Binding binding = property == "StatusKey" ? new Binding(property) { Converter = new ComparisonStatusConverter(text) }
                : property == "Name" ? new Binding(property)
                : new Binding(property) { Converter = new LengthDisplayConverter(units) };
            cell.SetBinding(TextBlock.TextProperty, binding);
            cell.SetBinding(ToolTipProperty, binding);
            var column = TemplateColumn(table, key, cell, 160);
            if (property is "FileElevation" or "ModelElevation") BindUnitLabel((TextBlock)column.Header, key);
            column.SortMemberPath = property;
            column.Width = new DataGridLength(property == "Name" || property == "StatusKey" ? 1.5 : 1, DataGridLengthUnitType.Star);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var load = new Button { Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(8, 0, 0, 6) };
        var clear = new Button { Padding = load.Padding, Margin = load.Margin, IsEnabled = false };
        Bind(load, ContentProperty, "LoadTsv"); Bind(clear, ContentProperty, "ClearComparison");
        var status = new TextBlock { FontSize = 12, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        IReadOnlyList<ComparisonLevel>? imported = null;
        string? loadedPath = null;
        string? importedUnits = null;
        void Update()
        {
            var instruction = text["CompareFormat"] + " " + text["CurrentUnits"] + ": " + units.Caption(text.English);
            load.ToolTip = instruction;
            if (imported is null) { status.Text = ""; status.ToolTip = null; status.Visibility = Visibility.Collapsed; return; }
            status.Visibility = Visibility.Visible;
            fileName.ToolTip = loadedPath + "\n" + text["ImportedUnits"] + ": " + importedUnits;
            var selectedRows = table.SelectedItems.Cast<LevelComparisonResult>().ToHashSet();
            var results = LevelComparison.Compare(imported, levels.Select(level => new ComparisonLevel(level.Name, level.Elevation)).ToList());
            rows.Clear(); foreach (var row in results) rows.Add(row);
            foreach (var row in results.Where(row => selectedRows.Contains(row) && table.Items.Contains(row))) table.SelectedItems.Add(row);
            var names = results.Where(row => row.StatusKey == "CompareMatch")
                .Select(row => LevelComparison.NormalizeName(row.Name)).ToHashSet(LevelComparison.NameComparer);
            foreach (var level in levels) level.InComparison = names.Contains(LevelComparison.NormalizeName(level.Name));
            status.Text = Path.GetFileName(loadedPath) + " · " + $"{text["CompareRows"]}: {imported.Count}. {text["CompareMatch"]}: {results.Count(row => row.StatusKey == "CompareMatch")}. ";
            status.ToolTip = fileName.ToolTip;
        }
        load.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "TSV (*.tsv)|*.tsv|Text (*.txt)|*.txt", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                using var reader = new StreamReader(dialog.FileName, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
                var parsed = LevelComparison.Read(reader, units.Parse, units.AcceptsHeaderUnit);
                imported = parsed; loadedPath = dialog.FileName; importedUnits = units.Caption(text.English);
                fileName.Text = Path.GetFileName(loadedPath); fileName.ToolTip = loadedPath;
                clear.IsEnabled = true; Update();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or DecoderFallbackException or Microsoft.VisualBasic.FileIO.MalformedLineException)
            {
                OperationDialogs.Show(this, text["CompareLoadError"], text["CompareFormat"] + "\n\n" + ex.Message);
            }
        };
        clear.Click += (_, _) =>
        {
            imported = null; loadedPath = null; rows.Clear(); clear.IsEnabled = false;
            foreach (var level in levels) level.InComparison = false;
            fileName.ToolTip = null; importedUnits = null; Update();
        };
        text.PropertyChanged += (_, _) => Update();
        units.Changed += Update; Update();
        buttons.Children.Add(clear); buttons.Children.Add(load);
        var tablePanel = new DockPanel();
        var search = new PrototypeTableSearch(table, text, item =>
        {
            var row = (LevelComparisonResult)item;
            return $"{row.Name} {units.Format(row.FileElevation)} {units.Format(row.ModelElevation)} {text[row.StatusKey]}";
        }).Bar;
        DockPanel.SetDock(search, Dock.Top); tablePanel.Children.Add(search); tablePanel.Children.Add(table);
        content.Children.Add(StablePageLayout.Create(fileName, tablePanel, buttons, status));
        return page;
    }

    private sealed class ComparisonStatusConverter(PrototypeText text) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => text[(string)value];
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
