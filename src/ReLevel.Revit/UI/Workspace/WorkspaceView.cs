using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Workspace;

internal sealed partial class WorkspaceView : UserControl
{
    private readonly WorkspaceText text;
    private readonly ReLevel.Revit.Logic.LengthUnitSettings units;
    private IWorkspaceTransferPage? transferPage;
    private readonly IWorkspace? workspace;
    public bool English => text.English;
    private readonly IReadOnlyList<WorkspaceLevel> levels;
    private readonly ListBox bottomNavigation = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly Window dialogOwner;
    private readonly Action<Action> runApi;
    private readonly ContentControl body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ListBox navigation = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private static readonly Brush Ink = ReLevelTheme.Text;
    private static readonly Brush Muted = Brush("#616161");
    private static readonly Brush Accent = Brush("#157D88");

    public WorkspaceView(IReadOnlyList<WorkspaceLevel> levels, bool english,
        Func<Window, bool, IWorkspaceTransferPage>? transferFactory = null, IWorkspace? workspace = null, Window? dialogOwner = null, Action<Action>? runApi = null)
    {
        this.dialogOwner = dialogOwner ?? throw new ArgumentNullException(nameof(dialogOwner));
        this.runApi = runApi ?? (action => action());
        this.levels = levels;
        this.workspace = workspace;
        text = new(english);
        units = workspace?.Units ?? ReLevel.Revit.Logic.LengthUnitSettings.FeetOnly();
        foreach (var level in levels) level.SetElevationFormatter(units.Format);
        units.Changed += () => { foreach (var level in levels) level.RefreshUnits(); };
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13; Foreground = Ink;
        Background = Brush("#F6F7F8");
        Resources.Add(typeof(Button), WorkspaceButtonStyle.Create());
        Resources.Add(typeof(ToggleButton), ReLevelTheme.Toggle());
        var comboStyle = new Style(typeof(ComboBox));
        comboStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        Resources.Add(typeof(ComboBox), comboStyle);
        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        Resources.Add(typeof(ComboBoxItem), itemStyle);
        var shell = new Grid { UseLayoutRounding = true };
        shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        toolbar.Children.Add(new Image { Source = AppIcon.Create(), Width = 24, Height = 24, Margin = new Thickness(4, 0, 8, 0) });
        foreach (var menu in new[] { navigation, bottomNavigation })
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            menu.ItemsPanel = new ItemsPanelTemplate(panel);
            toolbar.Children.Add(menu);
        }
        shell.Children.Add(new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.White });
        body.Margin = new Thickness(12); Grid.SetRow(body, 1); shell.Children.Add(body);
        Content = shell;

        // Register new sections here. Each page is built once, retaining table and control state.
        if (transferFactory is not null) transferPage = transferFactory(this.dialogOwner, text.English);
        AddSection("Levels", Information());
        AddSection("Colors", ColorsPage());
        AddSection("Compare", ComparisonPage());
        AddSection("Debug", Debug(), bottomNavigation);
        AddSection("Settings", Settings(), bottomNavigation);
        foreach (var menu in new[] { navigation, bottomNavigation })
        {
            for (int i = 0; i < menu.Items.Count; i++)
                ((ListBoxItem)menu.Items[i]).BorderThickness = new Thickness(1);
            ScrollViewer.SetHorizontalScrollBarVisibility(menu, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(menu, ScrollBarVisibility.Disabled);
        }
        text.PropertyChanged += (_, _) => this.runApi(() => transferPage?.SetLanguage(text.English));
        void Select(ListBox active, ListBox other)
        {
            if (active.SelectedItem is not ListBoxItem item) return;
            other.SelectedIndex = -1;
            body.Content = item.Tag;
            if (ReferenceEquals(item.Tag, colorsPage) && workspace is not null) this.runApi(PrepareColors);
        }
        navigation.SelectionChanged += (_, _) => Select(navigation, bottomNavigation);
        bottomNavigation.SelectionChanged += (_, _) => Select(bottomNavigation, navigation);
        navigation.SelectedIndex = 0;
    }

    private void AddSection(string key, FrameworkElement page, ListBox? target = null)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(24) });
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(WorkspaceIcons.Create(key, Accent));
        var caption = Label(key, 13); caption.Margin = new Thickness(8, 0, 0, 0);
        caption.Visibility = Visibility.Collapsed; caption.Tag = key; caption.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(caption, 1); layout.Children.Add(caption);
        var item = new ListBoxItem { Content = layout, Tag = page, Height = 32, Width = 36, Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(BorderBrushProperty, Brushes.Transparent));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.PaddingProperty })
            frame.SetBinding(property, new Binding(property.Name) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content);
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = frame }));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, Brush("#FAFBFC"))); style.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, Brush("#E7F3F4")));
        selected.Setters.Add(new Setter(BorderBrushProperty, Brush("#157D88")));
        selected.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
        style.Triggers.Add(selected); item.Style = style;
        Bind(item, System.Windows.Automation.AutomationProperties.NameProperty, key);
        Bind(item, ToolTipProperty, key); (target ?? navigation).Items.Add(item);
        void ResizeNavigation()
        {
            bool wide = ActualWidth >= 760;
            caption.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            item.Width = wide ? double.NaN : 36;
            item.Padding = new Thickness(wide ? 10 : 6, 0, wide ? 10 : 6, 0);
        }
        SizeChanged += (_, _) => ResizeNavigation(); ResizeNavigation();
    }

    private DockPanel Page(string key, out DockPanel content)
    {
        var page = new DockPanel();
        var heading = new Grid { Height = 40, Margin = new Thickness(0, 0, 0, 12) };
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(32) });
        var help = new Button { Width = 32, Height = 32, Margin = new Thickness(0),
            Content = WorkspaceIcons.Create("Help", Accent, 20), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        Bind(help, ToolTipProperty, "Help");
        Bind(help, System.Windows.Automation.AutomationProperties.NameProperty, "Help");
        help.Click += (_, _) => new ModeHelpWindow(dialogOwner, text, key).ShowDialog();
        Grid.SetColumn(help, 2); heading.Children.Add(help);
        var title = Label(key, 20, Ink);
        title.MaxWidth = 240; title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 16, 0);
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis; heading.Children.Add(title);
        var description = Label(key + "Description", 13, Muted);
        description.VerticalAlignment = VerticalAlignment.Center;
        description.TextWrapping = TextWrapping.NoWrap;
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        Bind(description, ToolTipProperty, key + "Description");
        Grid.SetColumn(description, 1); heading.Children.Add(description);
        void ResizeHeading() => description.Visibility = ActualWidth < 520 ? Visibility.Collapsed : Visibility.Visible;
        SizeChanged += (_, _) => ResizeHeading(); ResizeHeading();
        DockPanel.SetDock(heading, Dock.Top); page.Children.Add(heading);
        content = new DockPanel(); page.Children.Add(content);
        return page;
    }

    private FrameworkElement ColorsPage()
    {
        var page = Page("Colors", out var content);
        colorsPage = page;
        ColorFooter(content);
        var table = Table();
        CheckColumn(table, "Selected", nameof(WorkspaceLevel.Selected), 80);
        LevelColumns(table);
        if (workspace is not null)
        {
            var count = new FrameworkElementFactory(typeof(TextBlock));
            count.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceLevel.ElementCount)));
            count.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            count.SetValue(MarginProperty, new Thickness(10, 0, 10, 0));
            TemplateColumn(table, "Count", count, 80).SortMemberPath = nameof(WorkspaceLevel.ElementCount);
        }
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(FrameworkElement.HeightProperty, 28d);
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 3, 8, 3));
        button.SetBinding(IsEnabledProperty, new Binding(nameof(WorkspaceLevel.Selected)));
        var colorStyle = new Style(typeof(Button), WorkspaceButtonStyle.Create());
        colorStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(WorkspaceLevel.ColorHex))));
        var hiddenColor = new DataTrigger { Binding = new Binding(nameof(WorkspaceLevel.Hidden)), Value = true };
        hiddenColor.Setters.Add(new Setter(ToolTipProperty, Translation("Hidden")));
        colorStyle.Triggers.Add(hiddenColor); button.SetValue(StyleProperty, colorStyle);
        button.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(PickColor));
        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.SetValue(Border.BorderBrushProperty, ReLevelTheme.Border);
        swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        swatch.SetValue(FrameworkElement.WidthProperty, 48d);
        swatch.SetValue(FrameworkElement.HeightProperty, 18d);
        swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        swatch.SetBinding(Border.BackgroundProperty, new Binding(nameof(WorkspaceLevel.ColorBrush)));
        var swatchStyle = new Style(typeof(Border));
        var unselected = new DataTrigger { Binding = new Binding(nameof(WorkspaceLevel.Selected)), Value = false };
        unselected.Setters.Add(new Setter(VisibilityProperty, Visibility.Hidden)); swatchStyle.Triggers.Add(unselected);
        var hidden = new DataTrigger { Binding = new Binding(nameof(WorkspaceLevel.Hidden)), Value = true };
        hidden.Setters.Add(new Setter(OpacityProperty, 0.25)); swatchStyle.Triggers.Add(hidden);
        swatch.SetValue(StyleProperty, swatchStyle); button.AppendChild(swatch);
        TemplateColumn(table, "Color", button, 88).SortMemberPath = nameof(WorkspaceLevel.ColorHex);
        CheckColumn(table, "Hide", nameof(WorkspaceLevel.Hidden), 80, nameof(WorkspaceLevel.Selected));
        content.Children.Add(TableContainer(table));
        return page;
    }

    private FrameworkElement Debug()
    {
        var page = Page("Debug", out var content);
        var buttons = new StackPanel { MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (key, tool) in new[] { ("SelectByCase", WorkspaceDebugTool.SelectByCase),
                     ("AddById", WorkspaceDebugTool.AddById), ("Inspector", WorkspaceDebugTool.Inspector) })
        {
            var button = new Button { IsEnabled = workspace is not null, HorizontalAlignment = HorizontalAlignment.Left,
                MinHeight = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 12) };
            Bind(button, ContentProperty, key);
            Bind(button, ToolTipProperty, workspace is null ? "Unavailable" : key + "Help");
            ToolTipService.SetShowOnDisabled(button, true);
            button.Click += (_, _) => runApi(() => workspace?.RunDebug(tool));
            buttons.Children.Add(button);
            var hint = Label(key + "Help", 12, Muted);
            hint.Margin = new Thickness(0, 0, 0, 12); buttons.Children.Add(hint);
        }
        if (workspace is null) buttons.Children.Add(Label("Unavailable", 13, Muted));
        content.Children.Add(buttons); return page;
    }

    private FrameworkElement Settings()
    {
        var page = Page("Settings", out var content);
        var panel = new StackPanel { MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(Label("Language"));
        var language = new ComboBox { ItemsSource = new[] { "Русский", "English" }, SelectedIndex = text.English ? 1 : 0,
            Width = 260, MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        language.SelectionChanged += (_, _) => text.English = language.SelectedIndex == 1;
        panel.Children.Add(language);
        var unitTitle = Label("Units"); unitTitle.Margin = new Thickness(0, 24, 0, 0); panel.Children.Add(unitTitle);
        var unitChoice = new ComboBox { Width = 320, MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0) };
        foreach (var key in new[] { "ProjectUnits", "InternalUnits" })
        {
            var item = new ComboBoxItem(); Bind(item, ContentControl.ContentProperty, key); unitChoice.Items.Add(item);
        }
        unitChoice.SelectedIndex = units.InternalFeet ? 1 : 0;
        unitChoice.SelectionChanged += (_, _) => units.InternalFeet = unitChoice.SelectedIndex == 1;
        panel.Children.Add(unitChoice);
        var unitNote = Label("UnitsHelp", 12, Muted); unitNote.Margin = new Thickness(0, 8, 0, 0);
        void UpdateNote() => unitNote.Text = text["UnitsHelp"] + " " + text["CurrentUnits"] + ": " + units.Caption(text.English);
        units.Changed += UpdateNote; text.PropertyChanged += (_, _) => UpdateNote(); UpdateNote();
        panel.Children.Add(unitNote);
        content.Children.Add(panel); return page;
    }

    private DataGrid Table()
    {
        var table = new CopyableDataGrid
        {
            ItemsSource = new ListCollectionView(levels.ToList()), AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserSortColumns = true, CanUserReorderColumns = false, IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.All,
            VerticalGridLinesBrush = Brush("#D5E3E7"),
            HorizontalGridLinesBrush = Brush("#E9EEF2"), BorderBrush = Brush("#DFE6ED"), BorderThickness = new Thickness(1),
            Background = Brushes.White, RowBackground = Brushes.White, AlternatingRowBackground = Brush("#FAFBFC"),
            ColumnHeaderHeight = 40, RowHeight = 34, MinRowHeight = 34, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
            EnableRowVirtualization = true, EnableColumnVirtualization = true
        };
        ReLevelTheme.Table(table);
        table.ColumnHeaderStyle = WorkspaceTableHeader.CreateStyle();
        return table;
    }

    private FrameworkElement TableContainer(DataGrid table)
    {
        var panel = new DockPanel();
        var note = new WorkspaceTableSearch(table, text).Bar;
        DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        var grid = new Grid(); grid.Children.Add(table);
        var empty = Label(levels.Count == 0 ? "Empty" : "NoMatches", 13, Muted);
        empty.IsHitTestVisible = false; empty.VerticalAlignment = VerticalAlignment.Center;
        empty.HorizontalAlignment = HorizontalAlignment.Center; grid.Children.Add(empty);
        void UpdateEmpty() => empty.Visibility = table.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ((System.Collections.Specialized.INotifyCollectionChanged)table.Items).CollectionChanged += (_, _) => UpdateEmpty();
        UpdateEmpty();
        panel.Children.Add(grid); return panel;
    }

    private void LevelColumns(DataGrid table, bool showCount = true)
    {
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(showCount ? nameof(WorkspaceLevel.NameWithCount) : nameof(WorkspaceLevel.Name)));
        var nameTip = new MultiBinding { StringFormat = "{0}\nID: {1}" };
        nameTip.Bindings.Add(new Binding(nameof(WorkspaceLevel.Name)));
        nameTip.Bindings.Add(new Binding(nameof(WorkspaceLevel.Id)));
        name.SetBinding(ToolTipProperty, nameTip);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0));
        var column = TemplateColumn(table, "Name", name, 180);
        if (!showCount) Bind((TextBlock)column.Header, ToolTipProperty, "InformationHelp");
        column.Width = new DataGridLength(1, DataGridLengthUnitType.Star); column.MinWidth = 220; column.SortMemberPath = nameof(WorkspaceLevel.Name);
        var elevation = new FrameworkElementFactory(typeof(TextBlock));
        elevation.SetBinding(TextBlock.TextProperty, new Binding(nameof(WorkspaceLevel.ElevationText)));
        elevation.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        elevation.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
        elevation.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0));
        var elevationColumn = TemplateColumn(table, "Elevation", elevation, 128);
        BindUnitLabel((TextBlock)elevationColumn.Header, "Elevation");
        elevationColumn.SortMemberPath = nameof(WorkspaceLevel.Elevation);
    }

    private void CheckColumn(DataGrid table, string key, string property, double width, string? enabledProperty = null)
    {
        WorkspaceTableChecks.Add(table, levels, text, key, property, width, enabledProperty);
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
        if (sender is not Button { DataContext: WorkspaceLevel level } || !level.Selected) return;
        var picker = new WorkspaceColorWindow(text, level.Color) { Owner = dialogOwner };
        if (picker.ShowDialog() == true) level.Color = picker.SelectedColor;
    }

    private TextBlock Label(string key, double size = 13, Brush? foreground = null)
    {
        var label = new TextBlock { FontSize = size, Foreground = foreground ?? Ink, TextWrapping = TextWrapping.Wrap };
        Bind(label, TextBlock.TextProperty, key); return label;
    }
    private Binding Translation(string key) => new($"[{key}]") { Source = text, Mode = BindingMode.OneWay };
    private void Bind(DependencyObject target, DependencyProperty property, string key) => BindingOperations.SetBinding(target, property, Translation(key));
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
