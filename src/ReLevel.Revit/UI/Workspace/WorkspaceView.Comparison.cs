using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.UI.Workspace;

internal sealed partial class WorkspaceView
{
    private void HighlightComparedLevels(DataGrid table)
    {
        var style = new Style(typeof(DataGridCell), ReLevelTheme.Cells());
        var match = new DataTrigger { Binding = new Binding(nameof(WorkspaceLevel.InComparison)), Value = true };
        match.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#C4EDE3")));
        style.Triggers.Add(match);
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, ReLevelTheme.Selected));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, ReLevelTheme.Text));
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
            if (property is "FileElevation" or "ModelElevation") cell.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            var column = TemplateColumn(table, key, cell, 160);
            if (property is "FileElevation" or "ModelElevation") BindUnitLabel((TextBlock)column.Header, key);
            column.MinWidth = property == "Name" ? 220 : property == "StatusKey" ? 160 : 128; column.SortMemberPath = property;
            column.Width = new DataGridLength(property == "Name" || property == "StatusKey" ? 1.5 : 1, DataGridLengthUnitType.Star);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var load = new Button { Style = WorkspaceButtonStyle.Create(true), Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 6) };
        var clear = new Button { Height = 34, Padding = load.Padding, Margin = load.Margin, IsEnabled = false };
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
            if (dialog.ShowDialog(dialogOwner) != true) return;
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
                OperationDialogs.Show(dialogOwner, text["CompareLoadError"], text["CompareFormat"] + "\n\n" + ex.Message);
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
        var search = new WorkspaceTableSearch(table, text, item =>
        {
            var row = (LevelComparisonResult)item;
            return $"{row.Name} {units.Format(row.FileElevation)} {units.Format(row.ModelElevation)} {text[row.StatusKey]}";
        }).Bar;
        DockPanel.SetDock(search, Dock.Top); tablePanel.Children.Add(search);
        var tableHost = new Grid(); tableHost.Children.Add(table);
        var empty = Label("CompareEmpty", 13, Muted);
        empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
        empty.IsHitTestVisible = false; tableHost.Children.Add(empty);
        void EmptyState()
        {
            Bind(empty, TextBlock.TextProperty, imported is null ? "CompareEmpty" : "NoMatches");
            empty.Visibility = table.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        ((System.Collections.Specialized.INotifyCollectionChanged)table.Items).CollectionChanged += (_, _) => EmptyState();
        EmptyState(); tablePanel.Children.Add(tableHost);
        content.Children.Add(StablePageLayout.Create(fileName, tablePanel, buttons, status));
        return page;
    }

    private sealed class ComparisonStatusConverter(WorkspaceText text) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => text[(string)value];
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
