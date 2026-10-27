using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ReLevel.Revit.Transfer;
using Color = Autodesk.Revit.DB.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;

namespace ReLevel.Revit.UI;

internal sealed class LevelColorsWindow : Window
{
    private sealed record Row(long LevelId, ComboBox Action, Border Swatch, Color Color);
    private readonly List<Row> rows = [];
    private readonly LevelViewGraphics graphics;
    private readonly UIDocument uiDocument;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };

    public LevelColorsWindow(UIDocument uiDocument, IReadOnlyList<Level> levels)
    {
        this.uiDocument = uiDocument;
        graphics = new LevelViewGraphics(uiDocument.ActiveView, levels);
        Title = L.Get("Цвета по уровням") + " — " + uiDocument.ActiveView.Name;
        Width = 660; Height = 540; MinWidth = 560; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(16) };
        var description = new TextBlock
        {
            Text = L.Get("Только элементы кейсов переноса на текущем виде. Двухуровневые — по нижней привязке. Число рядом с уровнем — количество элементов."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(description, Dock.Top); root.Children.Add(description);
        var footer = new StackPanel();
        footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        AddButton(buttons, "Применить", () => Apply(false));
        AddButton(buttons, "Сбросить", () => Apply(true));
        AddButton(buttons, "Закрыть", Close);
        footer.Children.Add(buttons);
        footer.Children.Add(new TextBlock { Text = L.Get("Сброс восстанавливает графику до первого применения в этом окне. После закрытия используйте Undo Revit."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var list = new StackPanel();
        foreach (var level in levels)
        {
            var count = graphics.Count(level.Id.Value);
            var panel = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var swatch = new Border { Width = 32, Height = 24, Margin = new Thickness(8, 0, 4, 0),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Visibility = System.Windows.Visibility.Hidden };
            DockPanel.SetDock(swatch, Dock.Right); panel.Children.Add(swatch);
            var action = new ComboBox { Width = 130, ItemsSource = new[] { L.Get("Не менять"), L.Get("Покрасить"), L.Get("Скрыть") },
                SelectedIndex = 0, IsEnabled = count > 0 };
            DockPanel.SetDock(action, Dock.Right); panel.Children.Add(action);
            panel.Children.Add(new TextBlock { Text = $"{level.Name} ({count})", TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var color = new Color((byte)Random.Shared.Next(40, 231), (byte)Random.Shared.Next(40, 231), (byte)Random.Shared.Next(40, 231));
            swatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
            action.SelectionChanged += (_, _) => swatch.Visibility = action.SelectedIndex == 1
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
            rows.Add(new(level.Id.Value, action, swatch, color));
            list.Children.Add(panel);
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        status.Text = graphics.Diagnostics.Count == 0 ? "" : L.Format($"Ошибок чтения элементов: {graphics.Diagnostics.Count}.");
        status.ToolTip = string.Join("\n", graphics.Diagnostics);
        Closed += (_, _) => graphics.Dispose();
    }

    private void Apply(bool reset)
    {
        try
        {
            var choices = rows.ToDictionary(row => row.LevelId,
                row => new LevelGraphicsChoice(reset ? LevelGraphicsAction.Unchanged : (LevelGraphicsAction)row.Action.SelectedIndex, row.Color));
            status.Text = graphics.Apply(choices);
            if (reset) foreach (var row in rows) row.Action.SelectedIndex = 0;
            uiDocument.RefreshActiveView();
        }
        catch (Exception ex)
        {
            // Stop using the document after any failed operation, including regeneration/rollback failures.
            TaskDialog.Show(L.Get("Цвета по уровням"), ex.Message);
            Close();
        }
    }

    private static void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = L.Get(text), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6, 0, 0, 0) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
}
