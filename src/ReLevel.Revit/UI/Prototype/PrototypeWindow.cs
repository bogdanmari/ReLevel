using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Prototype;

internal sealed partial class PrototypeWindow : Window
{
    private readonly PrototypeText text;
    private readonly Func<Window, bool, IPrototypeTransferPage>? transferFactory;
    private IPrototypeTransferPage? transferPage;
    private readonly IPrototypeWorkspace? workspace;
    public bool English => text.English;
    private readonly IReadOnlyList<PrototypeLevel> levels;
    private readonly ListBox bottomNavigation = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly List<TextBlock> navigationLabels = [];
    private bool collapsed;
    private readonly ContentControl body = new();
    private readonly ListBox navigation = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private static readonly Brush Ink = Brush("#183247");
    private static readonly Brush Muted = Brush("#657788");
    private static readonly Brush Accent = Brush("#157D88");

    public PrototypeWindow(IReadOnlyList<PrototypeLevel> levels, bool english,
        Func<Window, bool, IPrototypeTransferPage>? transferFactory = null, IPrototypeWorkspace? workspace = null)
    {
        this.levels = levels;
        this.transferFactory = transferFactory;
        this.workspace = workspace;
        text = new(english);
        Width = 968; Height = 624; MinWidth = 800; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14; Foreground = Ink;
        Background = Brush("#F4F7FA");
        Bind(this, TitleProperty, transferFactory is null ? "Title" : "LiveTitle");
        var comboStyle = new Style(typeof(ComboBox));
        comboStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        Resources.Add(typeof(ComboBox), comboStyle);
        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        Resources.Add(typeof(ComboBoxItem), itemStyle);
        var shell = new Grid { LayoutTransform = new ScaleTransform(0.9, 0.9), UseLayoutRounding = true };
        shell.ColumnDefinitions.Add(new() { Width = new GridLength(206) });
        shell.ColumnDefinitions.Add(new() { Width = new GridLength(18) });
        shell.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = new DockPanel { Margin = new Thickness(8, 18, 8, 16) };
        var brand = new StackPanel { Height = 44, ClipToBounds = true, Margin = new Thickness(10, 0, 0, 30) };
        brand.Children.Add(new TextBlock { Text = "ReLevel", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Accent });
        brand.Children.Add(Label(transferFactory is null ? "Prototype" : "Workspace", 10, Muted));
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var arrow = new System.Windows.Shapes.Path { Data = Geometry.Parse("M6,0 L1,5 L6,10"),
            Stroke = Muted, StrokeThickness = 1.4, Width = 7, Height = 10, RenderTransformOrigin = new Point(0.5, 0.5) };
        var collapse = new Button { Content = arrow, Width = 18, Height = 32,
            VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        var collapseStyle = new Style(typeof(Button));
        collapseStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        chrome.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); chrome.AppendChild(presenter);
        collapseStyle.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = chrome }));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, Brush("#DDF1F1"))); collapseStyle.Triggers.Add(hover);
        collapse.Style = collapseStyle;
        // Use the style value so the subtle hover background can take effect.
        collapse.ClearValue(BackgroundProperty);
        Bind(collapse, ToolTipProperty, "ToggleMenu");
        Bind(collapse, System.Windows.Automation.AutomationProperties.NameProperty, "ToggleMenu");
        Grid.SetColumn(collapse, 1); shell.Children.Add(collapse);
        var bottom = new StackPanel();
        bottom.Children.Add(new Border { Height = 1, Background = Brush("#D5E3E7"), Margin = new Thickness(4, 10, 4, 10) });
        bottom.Children.Add(bottomNavigation);
        DockPanel.SetDock(bottom, Dock.Bottom); sidebar.Children.Add(bottom);
        void FitSidebar()
        {
            double width = 206;
            foreach (var label in navigationLabels)
            {
                var measured = new FormattedText(text[(string)label.Tag], System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    label.FontSize, Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                width = Math.Max(width, Math.Ceiling(measured.WidthIncludingTrailingWhitespace) + 80);
            }
            shell.ColumnDefinitions[0].Width = new GridLength(collapsed ? 64 : width);
        }
        collapse.Click += (_, _) =>
        {
            collapsed = !collapsed;
            FitSidebar();
            arrow.RenderTransform = new RotateTransform(collapsed ? 180 : 0);
            brand.Visibility = collapsed ? Visibility.Hidden : Visibility.Visible;
            foreach (var label in navigationLabels) label.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        };
        sidebar.Children.Add(navigation);
        shell.Children.Add(new Border { Background = Brushes.White, BorderBrush = Brush("#E0E7ED"), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar });
        body.Margin = new Thickness(8, 18, 20, 16); Grid.SetColumn(body, 2); shell.Children.Add(body);
        Content = shell;

        // Register new sections here. Each page is built once, retaining table and control state.
        AddSection("Distribute", Distribution());
        AddSection("Colors", ColorsPage());
        AddSection("Transfer", Transfer());
        AddSection("Debug", Debug(), bottomNavigation);
        AddSection("Settings", Settings(), bottomNavigation);
        foreach (var menu in new[] { navigation, bottomNavigation })
        {
            for (int i = 0; i < menu.Items.Count; i++)
                ((ListBoxItem)menu.Items[i]).BorderThickness = new Thickness(1, i == 0 ? 1 : 0, 1, i == menu.Items.Count - 1 ? 1 : 0);
            ScrollViewer.SetHorizontalScrollBarVisibility(menu, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(menu, ScrollBarVisibility.Disabled);
        }
        text.PropertyChanged += (_, _) => FitSidebar();
        text.PropertyChanged += (_, _) => transferPage?.SetLanguage(text.English);
        FitSidebar();
        void Select(ListBox active, ListBox other)
        {
            if (active.SelectedItem is not ListBoxItem item) return;
            other.SelectedIndex = -1;
            body.Content = item.Tag;
            if (ReferenceEquals(item.Tag, colorsPage) && workspace is not null) PrepareColors();
        }
        navigation.SelectionChanged += (_, _) => Select(navigation, bottomNavigation);
        bottomNavigation.SelectionChanged += (_, _) => Select(bottomNavigation, navigation);
        navigation.SelectedIndex = transferFactory is null ? 0 : 2;
    }

    private void AddSection(string key, FrameworkElement page, ListBox? target = null)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(24) });
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(PrototypeIcons.Create(key, Accent));
        var caption = Label(key, 13); caption.Margin = new Thickness(8, 0, 0, 0);
        caption.Tag = key; caption.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(caption, 1); layout.Children.Add(caption); navigationLabels.Add(caption);
        var item = new ListBoxItem { Content = layout, Tag = page, Height = 50, Padding = new Thickness(10, 12, 6, 12),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(BorderBrushProperty, Brushes.Transparent));
        var frame = new FrameworkElementFactory(typeof(Border));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.PaddingProperty })
            frame.SetBinding(property, new Binding(property.Name) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content);
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = frame }));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, Brush("#EFF7F8"))); style.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, Brush("#DDF1F1")));
        selected.Setters.Add(new Setter(BorderBrushProperty, Brush("#C7DCDF")));
        selected.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
        style.Triggers.Add(selected); item.Style = style;
        Bind(item, ToolTipProperty, key); (target ?? navigation).Items.Add(item);
    }

    private DockPanel Page(string key, out DockPanel content)
    {
        var page = new DockPanel();
        Panel heading;
        if (key == "Transfer")
        {
            heading = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var title = Label(key, 22);
            title.Margin = new Thickness(0, 0, 16, 0);
            DockPanel.SetDock(title, Dock.Left); heading.Children.Add(title);
            var description = Label(key + "Description", 13, Muted);
            description.VerticalAlignment = VerticalAlignment.Center;
            description.TextWrapping = TextWrapping.NoWrap;
            description.TextTrimming = TextTrimming.CharacterEllipsis;
            Bind(description, ToolTipProperty, key + "Description");
            heading.Children.Add(description);
        }
        else
        {
            heading = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            heading.Children.Add(Label(key, 22));
            var description = Label(key + "Description", 13); description.Margin = new Thickness(0, 12, 0, 8);
            heading.Children.Add(description);
        }

        DockPanel.SetDock(heading, Dock.Top); page.Children.Add(heading);
        content = new DockPanel(); page.Children.Add(content);
        return page;
    }

    private FrameworkElement Distribution()
    {
        var page = Page("Distribute", out var content);
        if (workspace is null) Footer(content, "RunDistribution");
        else DistributionFooter(content);
        var table = Table();
        CheckColumn(table, "Keep", nameof(PrototypeLevel.Keep), 48);
        LevelColumns(table);
        CheckColumn(table, "Protect", nameof(PrototypeLevel.Protect), 170, nameof(PrototypeLevel.Keep));
        content.Children.Add(TableContainer(table));
        return page;
    }

    private FrameworkElement ColorsPage()
    {
        var page = Page("Colors", out var content);
        colorsPage = page;
        if (workspace is null) Footer(content, "Apply");
        else ColorFooter(content);
        var table = Table();
        CheckColumn(table, "Selected", nameof(PrototypeLevel.Selected), 48);
        LevelColumns(table);
        if (workspace is not null)
        {
            var count = new FrameworkElementFactory(typeof(TextBlock));
            count.SetBinding(TextBlock.TextProperty, new Binding(nameof(PrototypeLevel.ElementCount)));
            count.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            count.SetValue(MarginProperty, new Thickness(10, 0, 10, 0));
            TemplateColumn(table, "Count", count, 72).SortMemberPath = nameof(PrototypeLevel.ElementCount);
        }
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(FrameworkElement.HeightProperty, 28d);
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 3, 8, 3));
        button.SetBinding(IsEnabledProperty, new Binding(nameof(PrototypeLevel.Selected)));
        button.SetBinding(ToolTipProperty, new Binding(nameof(PrototypeLevel.ColorHex)));
        button.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(PickColor));
        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.SetValue(FrameworkElement.WidthProperty, 64d);
        swatch.SetValue(FrameworkElement.HeightProperty, 18d);
        swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        swatch.SetBinding(Border.BackgroundProperty, new Binding(nameof(PrototypeLevel.ColorBrush)));
        var swatchStyle = new Style(typeof(Border));
        var unselected = new DataTrigger { Binding = new Binding(nameof(PrototypeLevel.Selected)), Value = false };
        unselected.Setters.Add(new Setter(VisibilityProperty, Visibility.Hidden)); swatchStyle.Triggers.Add(unselected);
        var hidden = new DataTrigger { Binding = new Binding(nameof(PrototypeLevel.Hidden)), Value = true };
        hidden.Setters.Add(new Setter(OpacityProperty, 0.25)); swatchStyle.Triggers.Add(hidden);
        swatch.SetValue(StyleProperty, swatchStyle); button.AppendChild(swatch);
        TemplateColumn(table, "Color", button, 90).SortMemberPath = nameof(PrototypeLevel.ColorHex);
        CheckColumn(table, "Hide", nameof(PrototypeLevel.Hidden), 110, nameof(PrototypeLevel.Selected));
        var mode = new FrameworkElementFactory(typeof(TextBlock));
        mode.SetBinding(TextBlock.TextProperty, Translation("Hidden"));
        mode.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        mode.SetValue(TextBlock.ForegroundProperty, Muted);
        var modeStyle = new Style(typeof(TextBlock));
        modeStyle.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        var show = new MultiDataTrigger();
        show.Conditions.Add(new Condition(new Binding(nameof(PrototypeLevel.Selected)), true));
        show.Conditions.Add(new Condition(new Binding(nameof(PrototypeLevel.Hidden)), true));
        show.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible)); modeStyle.Triggers.Add(show);
        mode.SetValue(StyleProperty, modeStyle);
        TemplateColumn(table, "", mode, 75).SortMemberPath = nameof(PrototypeLevel.Hidden);
        content.Children.Add(TableContainer(table));
        return page;
    }

    private FrameworkElement Transfer()
    {
        var page = Page("Transfer", out var content);
        if (transferFactory is not null)
        {
            transferPage = transferFactory(this, text.English);
            content.Children.Add(transferPage.View);
            return page;
        }
        Footer(content, "Move", "Recreate");
        var selectors = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        selectors.ColumnDefinitions.Add(new()); selectors.ColumnDefinitions.Add(new());
        string[] keys = ["Source", "Target"];
        for (int i = 0; i < keys.Length; i++)
        {
            var panel = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 12, 0, i == 0 ? 12 : 0, 0) };
            panel.Children.Add(Label(keys[i], 13, Muted));
            panel.Children.Add(new ComboBox { ItemsSource = new ListCollectionView(levels.ToList()), DisplayMemberPath = nameof(PrototypeLevel.Name),
                SelectedValuePath = nameof(PrototypeLevel.Id), MinHeight = 34, Margin = new Thickness(0, 7, 0, 0) });
            Grid.SetColumn(panel, i); selectors.Children.Add(panel);
        }
        DockPanel.SetDock(selectors, Dock.Top); content.Children.Add(selectors);
        var tabs = new TabControl();
        foreach (var key in new[] { "Elements", "Views" })
        {
            var empty = Label("NotLoaded", 15, Muted); empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center; empty.Margin = new Thickness(20);
            var tab = new TabItem { Header = Label(key, 13), Content = empty, Padding = new Thickness(12, 4, 12, 4) };
            tabs.Items.Add(tab);
        }
        content.Children.Add(tabs);
        return page;
    }

    private FrameworkElement Debug()
    {
        var page = Page("Debug", out var content);
        var buttons = new StackPanel();
        foreach (var (key, tool) in new[] { ("SelectByCase", WorkspaceDebugTool.SelectByCase),
                     ("AddById", WorkspaceDebugTool.AddById), ("Inspector", WorkspaceDebugTool.Inspector) })
        {
            var button = new Button { IsEnabled = workspace is not null, HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 250, Padding = new Thickness(20, 12, 20, 12), Margin = new Thickness(0, 0, 0, 12) };
            Bind(button, ContentProperty, key);
            Bind(button, ToolTipProperty, workspace is null ? "Unavailable" : key + "Help");
            ToolTipService.SetShowOnDisabled(button, true);
            button.Click += (_, _) => workspace?.RunDebug(tool);
            buttons.Children.Add(button);
        }
        if (workspace is null) buttons.Children.Add(Label("Unavailable", 13, Muted));
        content.Children.Add(buttons); return page;
    }

    private FrameworkElement Settings()
    {
        var page = Page("Settings", out var content);
        var panel = new StackPanel(); panel.Children.Add(Label("Language"));
        var language = new ComboBox { ItemsSource = new[] { "Русский", "English" }, SelectedIndex = text.English ? 1 : 0,
            Width = 260, MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        language.SelectionChanged += (_, _) => text.English = language.SelectedIndex == 1;
        panel.Children.Add(language); content.Children.Add(panel); return page;
    }

    private DataGrid Table()
    {
        var table = new DataGrid
        {
            ItemsSource = new ListCollectionView(levels.ToList()), AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserSortColumns = true, CanUserReorderColumns = false, IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.All,
            VerticalGridLinesBrush = Brush("#D5E3E7"),
            HorizontalGridLinesBrush = Brush("#E9EEF2"), BorderBrush = Brush("#DFE6ED"), BorderThickness = new Thickness(1),
            Background = Brushes.White, RowBackground = Brushes.White, AlternatingRowBackground = Brush("#EFF7F8"),
            RowHeight = 40, MinRowHeight = 40, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
            EnableRowVirtualization = true, EnableColumnVirtualization = true
        };
        table.ColumnHeaderStyle = PrototypeTableHeader.CreateStyle();
        return table;
    }

    private FrameworkElement TableContainer(DataGrid table)
    {
        var panel = new DockPanel();
        var note = new PrototypeTableSearch(table, text).Bar;
        DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        var grid = new Grid(); grid.Children.Add(table);
        if (levels.Count == 0)
        {
            var empty = Label("Empty", 15, Muted); empty.VerticalAlignment = VerticalAlignment.Center;
            empty.HorizontalAlignment = HorizontalAlignment.Center; grid.Children.Add(empty);
        }
        panel.Children.Add(grid); return panel;
    }

    private void LevelColumns(DataGrid table)
    {
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(PrototypeLevel.NameWithCount)));
        name.SetBinding(ToolTipProperty, new Binding(nameof(PrototypeLevel.Id)) { StringFormat = "ID: {0}" });
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0));
        var column = TemplateColumn(table, "Name", name, 180);
        column.Width = new DataGridLength(1, DataGridLengthUnitType.Star); column.MinWidth = 120; column.SortMemberPath = nameof(PrototypeLevel.Name);
        var elevation = new FrameworkElementFactory(typeof(TextBlock));
        elevation.SetBinding(TextBlock.TextProperty, new Binding(nameof(PrototypeLevel.ElevationText)));
        elevation.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        elevation.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0));
        TemplateColumn(table, "Elevation", elevation, 100).SortMemberPath = nameof(PrototypeLevel.Elevation);
    }

    private void CheckColumn(DataGrid table, string key, string property, double width, string? enabledProperty = null)
    {
        PrototypeTableChecks.Add(table, levels, text, key, property, width, enabledProperty);
    }
    private DataGridTemplateColumn TemplateColumn(DataGrid table, string key, FrameworkElementFactory visual, double width)
    {
        var column = new DataGridTemplateColumn { Width = width, CanUserSort = true, CellTemplate = new DataTemplate { VisualTree = visual } };
        if (key.Length > 0)
        {
            var header = Label(key, 12);
            if (key == "Name") Bind(header, ToolTipProperty, "ModelCountsHelp");
            column.Header = header;
        }
        table.Columns.Add(column); return column;
    }

    private void Footer(DockPanel page, params string[] actions)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var key in actions)
        {
            var button = new Button { IsEnabled = false, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(8, 0, 0, 6) };
            Bind(button, ContentProperty, key); Bind(button, ToolTipProperty, "Unavailable");
            ToolTipService.SetShowOnDisabled(button, true); buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);
        var note = Label("Unavailable", 12, Muted); note.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(note); DockPanel.SetDock(panel, Dock.Bottom); page.Children.Add(panel);
    }

    private void PickColor(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PrototypeLevel level } || !level.Selected) return;
        var picker = new PrototypeColorWindow(text, level.Color) { Owner = this };
        if (picker.ShowDialog() == true) level.Color = picker.SelectedColor;
    }

    private TextBlock Label(string key, double size = 14, Brush? foreground = null)
    {
        var label = new TextBlock { FontSize = size, Foreground = foreground ?? Ink, TextWrapping = TextWrapping.Wrap };
        Bind(label, TextBlock.TextProperty, key); return label;
    }
    private Binding Translation(string key) => new($"[{key}]") { Source = text, Mode = BindingMode.OneWay };
    private void Bind(DependencyObject target, DependencyProperty property, string key) => BindingOperations.SetBinding(target, property, Translation(key));
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}

