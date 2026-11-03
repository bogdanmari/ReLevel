using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace ReLevel.Revit.UI.Prototype;

internal sealed partial class PrototypeWindow
{
    private FrameworkElement Information()
    {
        var page = Page("Levels", out var content);
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        content.Children.Add(host);
        var overview = new DockPanel();
        host.Content = overview;
        var details = new DockPanel();
        var back = new Button { Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0),
            Content = PrototypeIcons.Create("Back", Accent, 18) };
        Bind(back, ToolTipProperty, "BackToLevels");
        Bind(back, System.Windows.Automation.AutomationProperties.NameProperty, "BackToLevels");
        var levelTitle = new TextBlock { FontSize = 16, Foreground = Accent, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis };
        transferPage?.SetNavigation(levelTitle, back);
        if (transferPage is not null) details.Children.Add(transferPage.View);
        else details.Children.Add(Label("NotLoaded"));
        PrototypeLevel? openedLevel = null;
        bool openedViews = false;
        void UpdateTitle()
        {
            if (openedLevel is null) return;
            levelTitle.Text = $"{openedLevel.Name} · {text[openedViews ? "Views" : "Elements"]}";
            levelTitle.ToolTip = levelTitle.Text;
        }
        text.PropertyChanged += (_, _) => UpdateTitle();
        void Open(PrototypeLevel level, bool views)
        {
            if (transferPage is null) return;
            transferPage.OpenLevel(level.Id, views);
            openedLevel = level; openedViews = views;
            UpdateTitle();
            host.Content = details;
        }
        back.Click += (_, _) => host.Content = overview;
        var table = Table();
        HighlightComparedLevels(table);
        CheckColumn(table, "Check", nameof(PrototypeLevel.Keep), 90);
        LevelColumns(table, showCount: false);
        foreach (var (key, property, sort) in new[] {
            ("Elements", nameof(PrototypeLevel.ModelElementCountText), nameof(PrototypeLevel.ModelElementCount)),
            ("Views", nameof(PrototypeLevel.ViewCount), nameof(PrototypeLevel.ViewCount)) })
        {
            var value = new FrameworkElementFactory(typeof(Button));
            value.SetBinding(ContentControl.ContentProperty, new Binding(property));
            value.SetBinding(ToolTipProperty, Translation(key == "Views" ? "OpenViews" : "OpenElements"));
            value.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,
                Translation(key == "Views" ? "OpenViews" : "OpenElements"));
            value.SetValue(Control.ForegroundProperty, Accent);
            value.SetValue(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            value.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            value.SetValue(Control.PaddingProperty, new Thickness(6, 3, 6, 3));
            value.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
            value.SetValue(IsEnabledProperty, transferPage is not null);
            value.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
            {
                if (((Button)sender).DataContext is PrototypeLevel level) Open(level, key == "Views");
                e.Handled = true;
            }));
            value.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            value.SetValue(MarginProperty, new Thickness(10, 0, 10, 0));
            TemplateColumn(table, key, value, 100).SortMemberPath = sort;
        }
        CheckColumn(table, "Protect", nameof(PrototypeLevel.Protect), 170, nameof(PrototypeLevel.Keep));
        Bind(table, ToolTipProperty, "InformationHelp");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var progress = new TransferProgress();
        var distribute = new Button { Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(8, 0, 0, 6) };
        Bind(distribute, ContentProperty, "RunDistribution");
        Bind(distribute, ToolTipProperty, "DistributeHelp");
        void UpdateDistribution() => distribute.IsEnabled = workspace is not null && levels.Any(row => row.Keep);
        foreach (var row in levels) row.PropertyChanged += (_, _) => UpdateDistribution();
        distribute.Click += (_, _) =>
        {
            using var controls = new OperationControls(this, progress);
            progress.Begin(0);
            workspace!.Distribute(levels, progress.Report,
                stopped => { progress.Finish(stopped, text[stopped ? "DistributionStopped" : "DistributionComplete"]); controls.Dispose(); },
                () => progress.CancellationRequested);
        };
        UpdateDistribution(); buttons.Children.Add(distribute);
        foreach (var key in new[] { "SelectElements", "DeleteElements", "DeleteViews" })
        {
            var button = new Button { Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(8, 0, 0, 6) };
            Bind(button, ContentProperty, key);
            void Update() => button.IsEnabled = workspace is not null &&
                table.Items.OfType<PrototypeLevel>().Any(row => row.Keep && (key == "DeleteViews" ? row.ViewCount : row.ModelElementCount) > 0);
            foreach (var row in levels) row.PropertyChanged += (_, _) => Update();
            ((System.Collections.Specialized.INotifyCollectionChanged)table.ItemsSource).CollectionChanged += (_, _) => Update();
            button.Click += (_, _) => workspace?.ProcessInformation(table.Items.OfType<PrototypeLevel>()
                .Where(row => row.Keep).Select(row => row.Id).ToList(), key != "SelectElements", key == "DeleteViews");
            Update(); buttons.Children.Add(button);
        }
        var status = new StackPanel();
        var counts = new TextBlock { FontSize = 12, Foreground = Muted };
        void UpdateCounts() => counts.Text = $"{text["Levels"]}: {levels.Count}. {text["Elements"]}: {levels.Sum(row => row.ModelElementCount)}. {text["Views"]}: {levels.Sum(row => row.ViewCount)}.";
        foreach (var row in levels) row.PropertyChanged += (_, e) =>
        { if (e.PropertyName is nameof(PrototypeLevel.ModelElementCount) or nameof(PrototypeLevel.ViewCount)) UpdateCounts(); };
        text.PropertyChanged += (_, _) => UpdateCounts(); UpdateCounts();
        status.Children.Add(progress); status.Children.Add(counts);
        var footer = StablePageLayout.Footer(buttons, status);
        DockPanel.SetDock(footer, Dock.Bottom); overview.Children.Add(footer);
        overview.Children.Add(TableContainer(table));
        return page;
    }
}
