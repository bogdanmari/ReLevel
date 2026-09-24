using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using UIApplication = Autodesk.Revit.UI.UIApplication;
using ReLevel.Revit.Logic;
using ReLevel.Revit.Transfer;
using Binding = System.Windows.Data.Binding;

namespace ReLevel.Revit.UI;

internal sealed class TransferWindow : Window
{
    private readonly UIApplication application;
    private readonly Document document;
    private readonly IReadOnlyList<Level> levels;
    private readonly IReadOnlyList<ElementId> initialSelection;
    private readonly SearchableComboBox source = new() { DisplayMemberPath = "Name", MinWidth = 260 };
    private readonly CheckBox selectedMode = new() { Content = "Работать с выбранными элементами", Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TabControl tabs = new();
    private readonly TabItem viewsTab = new() { Header = "Виды" };
    private readonly DataGrid elementTable = NewTable();
    private readonly DataGrid viewTable = NewTable();
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly DataGridTextColumn levelColumn;
    private readonly List<Button> elementButtons = [];
    private readonly List<Button> viewButtons = [];
    private readonly Dictionary<long, string> statuses = [];
    private readonly Dictionary<DataGrid, CheckBox> checkAllHeaders = [];
    private readonly Dictionary<DataGrid, TableSearch> searches = [];
    private bool updatingChecks;
    private List<TableRow> elementRows = [];
    private List<TableRow> viewRows = [];
    private bool IsSelectionMode => selectedMode.IsChecked == true;

    public TransferWindow(UIApplication application, IReadOnlyList<Level> levels, IReadOnlyList<ElementId> selected)
    {
        this.application = application; document = application.ActiveUIDocument.Document;
        this.levels = levels; initialSelection = selected;
        Title = "ReLevel"; Width = 920; Height = 607.2; MinWidth = 850; MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var shell = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "Уровень", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        source.SetItems(levels, level => level.Name); header.Children.Add(source);
        selectedMode.IsEnabled = selected.Count > 0; header.Children.Add(selectedMode);
        DockPanel.SetDock(header, Dock.Top); shell.Children.Add(header);
        DockPanel.SetDock(summary, Dock.Bottom); shell.Children.Add(summary);

        AddCheckColumn(elementTable);
        AddTextColumn(elementTable, "ID", nameof(TableRow.Id), 90);
        AddTextColumn(elementTable, "Категория", nameof(TableRow.Category), 170);
        AddTextColumn(elementTable, "Имя элемента", nameof(TableRow.Name), 220);
        levelColumn = AddTextColumn(elementTable, "Уровень", nameof(TableRow.Level), 160);
        AddTextColumn(elementTable, "Статус / причина", nameof(TableRow.Status));
        var elementsPanel = MakePanel(elementTable, "Поиск ограничен Element.LevelId и известными параметрами базового/опорного уровня. Все связи с уровнем пока не учитываются.", out var elementActions);
        AddAction(elementActions, elementButtons, "Выделить элементы", SelectElements);
        AddAction(elementActions, elementButtons, "Удалить элементы", () => DeleteRows(elementRows));
        AddAction(elementActions, elementButtons, "Перенести элементы", TransferElements);
        tabs.Items.Add(new TabItem { Header = "Элементы", Content = elementsPanel });

        AddCheckColumn(viewTable);
        AddTextColumn(viewTable, "ID", nameof(TableRow.Id), 90);
        AddTextColumn(viewTable, "Имя вида", nameof(TableRow.Name));
        AddTextColumn(viewTable, "Тип вида", nameof(TableRow.Type), 230);
        var viewsPanel = MakePanel(viewTable, "Пересоздание планов этажей, потолков и конструкций на другом уровне. Исходные виды и размещения на листах сохраняются.", out var viewActions);
        AddAction(viewActions, viewButtons, "Открыть виды", OpenViews);
        AddAction(viewActions, viewButtons, "Удалить виды", () => DeleteRows(viewRows));
        AddAction(viewActions, viewButtons, "Пересоздать виды", RecreateViews);
        viewsTab.Content = viewsPanel; tabs.Items.Add(viewsTab);
        shell.Children.Add(tabs); Content = shell;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && !source.IsDropDownOpen) { e.Handled = true; Close(); }
        };
        source.SelectionChanged += (_, _) => ChangeSource();
        selectedMode.Checked += (_, _) => ChangeMode();
        selectedMode.Unchecked += (_, _) => ChangeMode();
        if (levels.Count > 0) source.SelectedIndex = 0;
        else Refresh();
    }

    private void ChangeMode()
    {
        source.IsEnabled = !IsSelectionMode;
        viewsTab.IsEnabled = !IsSelectionMode;
        if (IsSelectionMode) { tabs.SelectedIndex = 0; source.ClearSelection(); }
        ChangeSource();
    }

    private void ChangeSource()
    {
        statuses.Clear();
        Refresh();
    }

    private void Refresh()
    {
        elementRows = []; viewRows = [];
        levelColumn.Visibility = IsSelectionMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        try
        {
            var analyzer = new TransferAnalyzer();
            IEnumerable<Element> candidates = [];
            if (IsSelectionMode)
                candidates = initialSelection.Select(document.GetElement).OfType<Element>();
            else if (source.SelectedItem is Level level)
            {
                candidates = new FilteredElementCollector(document).WhereElementIsNotElementType()
                    .Where(e => !e.ViewSpecific && e.Category?.CategoryType == CategoryType.Model && analyzer.IsOnLevel(e, level.Id));
                viewRows = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && v.GenLevel?.Id == level.Id).OrderBy(v => v.Name)
                    .Select(v => new TableRow(UpdateButtons) { Id = v.Id.ToLong(), Name = v.Name, Type = ViewTypeName(v.ViewType) }).ToList();
            }
            foreach (var e in candidates.OrderBy(e => e.Id.ToLong()))
            {
                string status;
                string levelName;
                try
                {
                    var related = levels.Where(l => analyzer.IsOnLevel(e, l.Id)).ToList();
                    levelName = related.Count > 0 ? string.Join(", ", related.Select(l => l.Name)) : "Не определён";
                    var target = levels.FirstOrDefault(l => !related.Any(r => r.Id == l.Id)) ?? levels.FirstOrDefault();
                    status = e is Level ? "Удаление уровней запрещено; перенос не поддерживается."
                        : e is View ? "Операции с видами доступны на вкладке «Виды»."
                        : target is null ? "Нет уровня для анализа переноса."
                        : Analyze(e, target).Reason ?? "Предварительная проверка пройдена; цель будет проверена при переносе.";
                }
                catch (Exception ex) { levelName = "Не определён"; status = $"Ошибка анализа: {ex.Message}"; }
                elementRows.Add(new TableRow(UpdateButtons) { Id = e.Id.ToLong(), Category = e.Category?.Name ?? "—", Name = e.Name,
                    Level = levelName, Status = statuses.GetValueOrDefault(e.Id.ToLong(), status) });
            }
            summary.Text = $"Элементы: {elementRows.Count}. Виды: {viewRows.Count}."
                + (!IsSelectionMode && source.SelectedItem is null ? " Выберите уровень." : "");
        }
        catch (Exception ex) { summary.Text = $"Не удалось обновить таблицы: {ex.Message}"; }
        searches[elementTable].SetRows(elementRows); searches[viewTable].SetRows(viewRows);
        UpdateButtons();
    }

    private static TransferPlan Analyze(Element element, Level target)
    {
        try
        {
            var plan = new TransferAnalyzer().Analyze(element, target);
            if (plan.Ready) _ = GeometrySnapshot.Capture(element);
            return plan;
        }
        catch (Exception ex) { return new(element.Id, element.Name, null, $"Ошибка предварительной проверки: {ex.Message}"); }
    }

    private void SelectElements()
    {
        var rows = Checked(elementRows);
        application.ActiveUIDocument.Selection.SetElementIds(rows.Select(r => ElementIds.Create(r.Id)).ToList());
        var actual = application.ActiveUIDocument.Selection.GetElementIds().Select(id => id.ToLong()).ToHashSet();
        Complete("Выделение элементов", rows.ToDictionary(r => r.Id,
            r => actual.Contains(r.Id) ? "Выделен в модели." : "Не выделен: Revit не включил объект в выделение."));
    }

    private void TransferElements()
    {
        var rows = Checked(elementRows);
        var sourceId = (source.SelectedItem as Level)?.Id;
        var choices = levels.Where(l => IsSelectionMode || l.Id != sourceId).ToList();
        if (choices.Count == 0) { OperationDialogs.Show(this, "Перенос недоступен", "Нет другого уровня для переноса."); return; }
        var dialog = new TargetLevelWindow(this, choices);
        if (dialog.ShowDialog() != true) return;
        var plans = rows.Select(r => document.GetElement(ElementIds.Create(r.Id)) is { } e
            ? Analyze(e, dialog.Target) : new TransferPlan(ElementIds.Create(r.Id), r.Name, null, "Элемент больше не существует.")).ToList();
        var report = new TransferService(application).Execute(plans, dialog.Target);
        var results = report.Items.ToDictionary(r => r.ElementId, r => $"{r.Status switch {
            TransferStatus.Transferred => "Перенесён", TransferStatus.Skipped => "Пропущен", _ => "Ошибка" }}: {r.Reason}");
        Complete($"Перенесено: {report.Transferred}. Пропущено: {report.Skipped}. Ошибки: {report.Failed}.", results);
    }

    private void RecreateViews()
    {
        var rows = Checked(viewRows);
        if (rows.Count == 0 || source.SelectedItem is not Level sourceLevel) return;
        var choices = levels.Where(l => l.Id != sourceLevel.Id).ToList();
        if (choices.Count == 0) { OperationDialogs.Show(this, "Пересоздание недоступно", "Нет другого уровня."); return; }
        var dialog = new RecreateViewsWindow(this, choices, rows.Count);
        if (dialog.ShowDialog() != true) return;
        var report = new ViewRecreationService(document).Execute(rows.Select(r => ElementIds.Create(r.Id)).ToList(), dialog.Target.Id, dialog.Prefix);
        foreach (var item in report.Results.Items) statuses[item.ElementId] = item.Reason;
        if (!report.CriticalFailure) Refresh();
        new ViewLogWindow(this, report, id =>
        {
            var created = document.GetElement(ElementIds.Create(id)) as View
                ?? throw new InvalidOperationException("Созданный вид больше не существует.");
            application.ActiveUIDocument.ActiveView = created;
        }).ShowDialog();
        if (report.CriticalFailure) Close();
    }

    private void OpenViews()
    {
        var results = new Dictionary<long, string>();
        foreach (var row in Checked(viewRows))
        {
            try
            {
                var view = document.GetElement(ElementIds.Create(row.Id)) as View ?? throw new InvalidOperationException("Вид больше не существует.");
                // Synchronous API call in the external command context, with no open transaction.
                application.ActiveUIDocument.ActiveView = view;
                if (!application.ActiveUIDocument.GetOpenUIViews().Any(v => v.ViewId == view.Id))
                    throw new InvalidOperationException("Revit не подтвердил открытие вида.");
                results[row.Id] = "Вид открыт.";
            }
            catch (Exception ex) { results[row.Id] = $"Ошибка: {ex.Message}"; }
        }
        Complete("Открытие видов — результаты", results);
    }

    private void DeleteRows(List<TableRow> tableRows)
    {
        var rows = Checked(tableRows);
        var service = new DeleteService(document);
        var previews = new List<DeletePreview>();
        var results = new Dictionary<long, string>();
        foreach (var row in rows)
        {
            try
            {
                if (ReferenceEquals(tableRows, elementRows) && document.GetElement(ElementIds.Create(row.Id)) is View)
                    throw new InvalidOperationException("Используйте вкладку «Виды».");
                previews.Add(service.Preview(ElementIds.Create(row.Id)));
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { results[row.Id] = $"Не удалён: {ex.Message}"; }
        }
        if (previews.Count == 0) { Complete("Удаление не выполнено", results); return; }
        var total = previews.SelectMany(p => p.DeletedIds).Distinct().Count();
        var description = $"Отмечено объектов: {rows.Count}. Доступно для удаления: {previews.Count}.\n"
            + $"Всего объектов с зависимостями: {total}. Удаление затронет следующие объекты:\n\n"
            + string.Join("\n\n", previews.Select(p => p.Description))
            + (results.Count == 0 ? "" : "\n\nНе будут удалены:\n" + Format(results));
        if (!OperationDialogs.Show(this, "Подтвердить удаление", description, $"Удалить ({total})")) { Refresh(); return; }
        foreach (var preview in previews)
        {
            try
            {
                if (document.GetElement(preview.Id) is null) { results[preview.Id.ToLong()] = "Удалён как подтверждённая зависимость другого объекта."; continue; }
                service.Delete(preview); results[preview.Id.ToLong()] = "Удалён.";
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { results[preview.Id.ToLong()] = $"Не удалён: {ex.Message}"; }
        }
        Complete("Удаление — результаты по объектам", results);
    }

    private void Complete(string title, Dictionary<long, string> results)
    {
        foreach (var pair in results) statuses[pair.Key] = pair.Value;
        Refresh();
        OperationDialogs.Show(this, title, Format(results));
    }

    private static string Format(Dictionary<long, string> results) => string.Join(Environment.NewLine, results.Select(p => $"ID {p.Key}: {p.Value}"));
    private List<TableRow> Checked(List<TableRow> rows) =>
        (ReferenceEquals(rows, elementRows) ? elementTable : viewTable).Items.OfType<TableRow>().Where(r => r.IsChecked).ToList();
    private void UpdateButtons()
    {
        if (updatingChecks) return;
        foreach (var button in elementButtons) button.IsEnabled = Checked(elementRows).Count > 0;
        foreach (var button in viewButtons) button.IsEnabled = !IsSelectionMode && Checked(viewRows).Count > 0;
        foreach (var (table, header) in checkAllHeaders)
        {
            var rows = table.Items.OfType<TableRow>().ToList();
            var count = rows.Count(r => r.IsChecked);
            header.IsEnabled = rows.Count > 0;
            header.IsChecked = count == 0 ? false : count == rows.Count ? true : null;
        }
    }

    private void AddAction(StackPanel panel, List<Button> buttons, string label, Action action)
    {
        var button = new Button { Content = label, IsEnabled = false, Margin = new Thickness(0, 8, 8, 0), Padding = new Thickness(10, 5, 10, 5) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException ex)
            {
                OperationDialogs.Show(this, "Критическая ошибка Revit", $"Операция остановлена. Окно будет закрыто.\n{ex.Message}");
                Close();
            }
            catch (Exception ex) { Refresh(); OperationDialogs.Show(this, "Операция не выполнена", ex.Message); }
        };
        buttons.Add(button); panel.Children.Add(button);
    }

    private static DataGrid NewTable() => new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
        IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended,
        SelectionUnit = DataGridSelectionUnit.FullRow, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader };

    private void SetChecks(IEnumerable<TableRow> rows, bool value)
    {
        updatingChecks = true;
        try { foreach (var row in rows) row.IsChecked = value; }
        finally { updatingChecks = false; UpdateButtons(); }
    }

    private void ToggleRow(DataGrid table, TableRow row)
    {
        var rows = table.SelectedItems.Contains(row)
            ? table.SelectedItems.Cast<TableRow>().ToList() : new List<TableRow> { row };
        SetChecks(rows, !row.IsChecked);
    }

    private void AddCheckColumn(DataGrid table)
    {
        var header = new CheckBox { ToolTip = "Отметить все / снять все отметки", IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Center };
        checkAllHeaders.Add(table, header);
        header.Click += (_, e) =>
        {
            var rows = table.Items.OfType<TableRow>().ToList();
            SetChecks(rows, !rows.All(r => r.IsChecked));
            e.Handled = true;
        };
        var factory = new FrameworkElementFactory(typeof(CheckBox));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding(nameof(TableRow.IsChecked)) { Mode = BindingMode.OneWay });
        // Handle the press before DataGrid can collapse a multiple-row selection.
        factory.AddHandler(PreviewMouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler((sender, e) =>
        {
            if (((CheckBox)sender).DataContext is not TableRow row) return;
            e.Handled = true;
            ToggleRow(table, row);
        }));
        factory.AddHandler(PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((sender, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Space || ((CheckBox)sender).DataContext is not TableRow row) return;
            e.Handled = true;
            if (!e.IsRepeat) ToggleRow(table, row);
        }));
        factory.AddHandler(CheckBox.ClickEvent, new RoutedEventHandler((sender, e) =>
        {
            if (((CheckBox)sender).DataContext is TableRow row) ToggleRow(table, row);
            e.Handled = true;
        }));
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(0)));
        table.Columns.Add(new DataGridTemplateColumn { Header = header, HeaderStyle = headerStyle, Width = 40, CanUserSort = false,
            CellTemplate = new DataTemplate { VisualTree = factory } });
    }

    private static DataGridTextColumn AddTextColumn(DataGrid table, string title, string property, double? width = null)
    {
        var column = new DataGridTextColumn { Header = title, Binding = new Binding(property),
            Width = width is { } value ? new DataGridLength(value) : new DataGridLength(1, DataGridLengthUnitType.Star) };
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));
        if (property == nameof(TableRow.Status)) style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        column.ElementStyle = style;
        table.Columns.Add(column); return column;
    }

    private DockPanel MakePanel(DataGrid table, string hint, out StackPanel actions)
    {
        var panel = new DockPanel { Margin = new Thickness(8) };
        var text = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(text, Dock.Top); panel.Children.Add(text);
        var controller = new TableSearch(table, UpdateButtons);
        searches.Add(table, controller);
        var search = controller.Bar;
        DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        panel.Children.Add(table); return panel;
    }

    private static string ViewTypeName(ViewType type) => type switch
    {
        ViewType.FloorPlan => "План этажа", ViewType.CeilingPlan => "План потолка", ViewType.EngineeringPlan => "План конструкций",
        ViewType.AreaPlan => "План площадей", _ => type.ToString()
    };
}
