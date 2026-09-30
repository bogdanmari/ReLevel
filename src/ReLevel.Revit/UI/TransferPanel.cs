using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using UIApplication = Autodesk.Revit.UI.UIApplication;
using ReLevel.Revit.Logic;
using ReLevel.Revit.Transfer;
using Binding = System.Windows.Data.Binding;

namespace ReLevel.Revit.UI;

internal sealed class TransferPanel : UserControl
{
    private readonly UIApplication application;
    private readonly Document document;
    private readonly ReLevelPaneController controller;
    private IReadOnlyList<Level> levels = [];
    private bool loading;
    private readonly TextBlock documentName = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) };
    public IntPtr MainWindowHandle { get; }
    private ElementId? SelectedSourceId => source.SelectedItem is LevelChoice choice ? new ElementId(choice.Id) : null;
    private readonly SearchableComboBox source = new() { DisplayMemberPath = "Name", MinWidth = 120 };
    private readonly ComboBox language = new() { IsEditable = false, IsTextSearchEnabled = false, Width = 100,
        ItemsSource = new[] { "English", "Русский" }, ToolTip = "Language / Язык", Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Top };
    private readonly TabControl tabs = new();
    private readonly TabItem viewsTab = new() { Header = L.Get("Виды") };
    private readonly DataGrid elementTable = NewTable();
    private readonly DataGrid viewTable = NewTable();
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly List<Button> elementButtons = [];
    private readonly List<Button> viewButtons = [];
    private readonly Dictionary<long, string> statuses = [];
    private readonly Dictionary<DataGrid, CheckBox> checkAllHeaders = [];
    private readonly Dictionary<DataGrid, TableSearch> searches = [];
    private bool updatingChecks;
    private List<TableRow> elementRows = [];
    private List<TableRow> viewRows = [];
    public TransferPanel(UIApplication application, ReLevelPaneController controller)
    {
        this.application = application; document = application.ActiveUIDocument.Document;
        this.controller = controller;
        MainWindowHandle = application.MainWindowHandle;
        MinWidth = 320;
        var shell = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(documentName, Dock.Top); shell.Children.Add(documentName);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(language, Dock.Right); top.Children.Add(language);
        language.SelectedIndex = L.UseEnglish ? 0 : 1;
        var header = new DockPanel();
        var levelLabel = new TextBlock { Text = L.Get("Уровень"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(levelLabel, Dock.Left); header.Children.Add(levelLabel);
        header.Children.Add(source);
        top.Children.Add(header);
        DockPanel.SetDock(top, Dock.Top); shell.Children.Add(top);
        var debugTools = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8) };
        AddAction(debugTools, null, L.Get("Выделить все элементы по…"), SelectAllByCase);
        AddAction(debugTools, null, L.Get("ReLevel инспектор"), () => ElementInspectorAction.Run(application));
        AddAction(debugTools, null, L.Get("Обновить"), Reload);
        DockPanel.SetDock(debugTools, Dock.Top); shell.Children.Add(debugTools);
        DockPanel.SetDock(summary, Dock.Bottom); shell.Children.Add(summary);

        AddCheckColumn(elementTable);
        AddTextColumn(elementTable, L.Get("Имя элемента"), nameof(TableRow.Name));
        AddTextColumn(elementTable, L.Get("Категория"), nameof(TableRow.Category), 130);
        AddTextColumn(elementTable, "ID", nameof(TableRow.Id), 80);
        AddTextColumn(elementTable, L.Get("Привязка"), nameof(TableRow.Level), 90);
        AddTextColumn(elementTable, L.Get("Статус / причина"), nameof(TableRow.Status));
        var elementsPanel = MakePanel(elementTable, L.Get("Показаны доступные элементы реализованных кейсов. Меняются только привязки к выбранному исходному уровню."), out var elementActions);
        AddAction(elementActions, elementButtons, L.Get("Выделить элементы"), SelectElements);
        AddAction(elementActions, elementButtons, L.Get("Удалить элементы"), () => DeleteRows(elementRows, DeleteScope.Elements));
        AddAction(elementActions, elementButtons, L.Get("Перенести элементы"), TransferElements);
        tabs.Items.Add(new TabItem { Header = L.Get("Элементы"), Content = elementsPanel });

        AddCheckColumn(viewTable);
        AddTextColumn(viewTable, L.Get("Имя вида"), nameof(TableRow.Name));
        AddTextColumn(viewTable, L.Get("Тип вида"), nameof(TableRow.Type), 130);
        AddTextColumn(viewTable, "ID", nameof(TableRow.Id), 80);
        AddTextColumn(viewTable, L.Get("Статус / причина"), nameof(TableRow.Status), 180);
        var viewsPanel = MakePanel(viewTable, L.Get("Пересоздание планов этажей, потолков и конструкций на другом уровне. Исходные виды и размещения на листах сохраняются."), out var viewActions);
        AddAction(viewActions, viewButtons, L.Get("Открыть виды"), OpenViews);
        AddAction(viewActions, viewButtons, L.Get("Удалить виды"), () => DeleteRows(viewRows, DeleteScope.Views));
        AddAction(viewActions, viewButtons, L.Get("Пересоздать виды"), RecreateViews);
        viewsTab.Content = viewsPanel; tabs.Items.Add(viewsTab);
        shell.Children.Add(tabs); Content = shell;
        source.SelectionChanged += (_, _) =>
        {
            if (loading) return;
            if (source.SelectedItem is LevelChoice) QueueAction(ChangeSource);
            else
            {
                // Typing a query clears the selection. Keep the editor responsive without an API call.
                elementRows = []; viewRows = []; statuses.Clear();
                searches[elementTable].SetRows(elementRows); searches[viewTable].SetRows(viewRows);
                summary.Text = L.Get("Выберите уровень."); summary.ToolTip = null;
                UpdateButtons();
            }
        };
        language.SelectionChanged += (_, _) => { if (!loading) QueueAction(ChangeLanguage); };
        Reload();
    }

    // Called only in a Revit API callback. The combo box receives plain values, not Revit elements.
    public void Reload()
    {
        var selected = (source.SelectedItem as LevelChoice)?.Id;
        loading = true;
        try
        {
            language.SelectedIndex = L.UseEnglish ? 0 : 1;
            levels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(level => level.ProjectElevation).ThenBy(level => level.Name).ToList();
            var choices = levels.Select(level => new LevelChoice(level.Id.Value, level.Name)).ToList();
            source.SetItems(choices, choice => choice.Name);
            source.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected) ?? choices.FirstOrDefault();
            documentName.Text = document.Title;
            documentName.ToolTip = document.Title;
        }
        finally { loading = false; }
        Refresh();
    }

    public void ShowMessage(string message) => summary.Text = message;
    private void Stop() => controller.Suspend();
    private void QueueAction(Action action) => controller.Request(this, action);
    private sealed record LevelChoice(long Id, string Name);

    private void ChangeLanguage()
    {
        if (language.SelectedIndex < 0 || L.UseEnglish == (language.SelectedIndex == 0)) return;
        var tables = new[] { elementTable, viewTable };
        var checkedIds = elementRows.Concat(viewRows).Where(row => row.IsChecked).Select(row => row.Id).ToHashSet();
        var selectedIds = tables.ToDictionary(table => table,
            table => table.SelectedItems.Cast<TableRow>().Select(row => row.Id).ToHashSet());
        var sorts = tables.ToDictionary(table => table, table => table.Items.SortDescriptions.ToList());
        L.UseEnglish = language.SelectedIndex == 0;
        LanguageLabels.Refresh(this);
        documentName.Text = document.Title;
        Refresh();
        SetChecks(elementRows.Concat(viewRows).Where(row => checkedIds.Contains(row.Id)), true);
        foreach (var table in tables)
        {
            using (table.Items.DeferRefresh())
            {
                table.Items.SortDescriptions.Clear();
                foreach (var sort in sorts[table]) table.Items.SortDescriptions.Add(sort);
            }
            foreach (var row in table.Items.OfType<TableRow>())
                if (selectedIds[table].Contains(row.Id)) table.SelectedItems.Add(row);
            searches[table].RefreshLanguage();
        }
    }

    private void ChangeSource()
    {
        statuses.Clear();
        Refresh();
    }

    private void Refresh()
    {
        elementRows = []; viewRows = [];
        summary.ToolTip = null;
        var elementErrors = new List<string>();
        try
        {
            var sourceLevelId = SelectedSourceId;
            IEnumerable<Element> candidates = sourceLevelId is not null ? TransferCases.Candidates(document) : [];
            elementRows = candidates.OrderBy(e => e.Id.Value)
                .Select(e => ElementRow(e, sourceLevelId, elementErrors)).OfType<TableRow>().ToList();
            if (sourceLevelId is not null)
            {
                viewRows = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && v.GenLevel?.Id == sourceLevelId).OrderBy(v => v.Name)
                    .Select(v => new TableRow(UpdateButtons) { Id = v.Id.Value, Name = v.Name,
                        Type = ViewTypeName(v.ViewType), Status = ViewStatus(v) }).ToList();
            }
            summary.Text = L.Format($"Элементы: {elementRows.Count}. Виды: {viewRows.Count}.")
                + (source.SelectedItem is null ? L.Get(" Выберите уровень.") : "");
            if (elementErrors.Count > 0)
            {
                summary.Text += L.Format($" Не удалось проверить элементов: {elementErrors.Count}. Подробности — в подсказке.");
                summary.ToolTip = string.Join(Environment.NewLine, elementErrors);
            }
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
        {
            OperationDialogs.Show(this, L.Get("Операция отменена"), L.Get("Критическая ошибка Revit. Панель остановлена; проверьте состояние документа."));
            Stop();
            return;
        }
        catch (Exception ex) { summary.Text = L.Format($"Не удалось обновить таблицы: {ex.Message}"); }
        searches[elementTable].SetRows(elementRows); searches[viewTable].SetRows(viewRows);
        UpdateButtons();
    }

    private TableRow? ElementRow(Element element, ElementId? sourceLevelId, List<string> errors)
    {
        try
        {
            var transferCase = TransferCases.Find(element);
            if (sourceLevelId is null || transferCase is null || !transferCase.IsOnLevel(element, sourceLevelId)
                || transferCase.WriteRestriction(element, sourceLevelId) is not null)
                return null;
            var status = transferCase.Name + ": " + L.Get("Доступен для переноса.");
            if (statuses.TryGetValue(element.Id.Value, out var result)) status += Environment.NewLine + result;
            return new TableRow(UpdateButtons) { Id = element.Id.Value, Name = element.Name,
                Category = element.Category?.Name ?? "—", Level = transferCase.Relation(element, sourceLevelId), Status = status };
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex) { errors.Add($"ID {element.Id.Value}: {ex.Message}"); return null; }
    }

    private void SelectElements()
    {
        var rows = Checked(elementRows);
        application.ActiveUIDocument.Selection.SetElementIds(rows.Select(row => new ElementId(row.Id)).ToList());
        var selected = application.ActiveUIDocument.Selection.GetElementIds().Select(id => id.Value).ToHashSet();
        Complete(L.Get("Выделение элементов"), rows.ToDictionary(row => row.Id, row => selected.Contains(row.Id)
            ? L.Get("Выделен в модели.") : L.Get("Не выделен: Revit не включил объект в выделение.")));
    }

    private void SelectAllByCase()
    {
        var dialog = new SelectByCaseWindow(this);
        if (dialog.ShowDialog() != true) return;
        var result = CaseElementFinder.Find(document, dialog.SelectedCase);
        var details = result.Errors.OrderBy(pair => pair.Key).Select(pair => $"ID {pair.Key}: {pair.Value}").ToList();
        if (result.Ids.Count == 0)
        {
            OperationDialogs.Show(this, dialog.SelectedCase.Name,
                L.Get("Подходящие элементы не найдены. Текущее выделение сохранено.")
                + (details.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, details)));
            return;
        }
        application.ActiveUIDocument.Selection.SetElementIds(result.Ids.ToList());
        var selectedIds = application.ActiveUIDocument.Selection.GetElementIds().Select(id => id.Value).ToHashSet();
        var notSelected = result.Ids.Where(id => !selectedIds.Contains(id.Value)).Select(id => id.Value).Order().ToList();
        var selectedCount = result.Ids.Count - notSelected.Count;
        if (notSelected.Count > 0)
            details.Add(L.Get("Revit не включил в выделение ID: ") + string.Join(", ", notSelected));
        OperationDialogs.Show(this, dialog.SelectedCase.Name,
            L.Format($"Найдено: {result.Ids.Count}. Выделено: {selectedCount}. Ошибок чтения: {result.Errors.Count}.")
            + (details.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, details)));
    }

    private void TransferElements()
    {
        var rows = Checked(elementRows);
        if (rows.Count == 0) return;
        var sourceLevelId = SelectedSourceId;
        if (sourceLevelId is null) return;
        var choices = levels.Where(level => level.Id != sourceLevelId).ToList();
        if (choices.Count == 0) { OperationDialogs.Show(this, L.Get("Перенос недоступен"), L.Get("Нет другого уровня.")); return; }
        var dialog = new TargetLevelWindow(this, choices);
        if (dialog.ShowDialog() != true) return;
        try
        {
            var report = new ElementTransferService(document).Execute(
                rows.Select(row => new ElementId(row.Id)).ToList(), dialog.Target.Id, sourceLevelId);
            var results = report.Items.ToDictionary(item => item.Id, item => (item.Status switch
            {
                ElementTransferStatus.Transferred => L.Get("Перенесён"),
                ElementTransferStatus.Skipped => L.Get("Пропущен"),
                _ => L.Get("Ошибка")
            }) + ": " + item.Reason);
            var title = L.Format($"Перенесено: {report.Transferred}. Пропущено: {report.Skipped}. Ошибки: {report.Failed}.");
            if (report.Stopped)
            {
                OperationDialogs.Show(this, title, L.Get("Обработка остановлена. Ранее завершённые переносы сохранены; панель будет остановлена. Проверьте документ и при необходимости используйте Undo.")
                    + Environment.NewLine + Format(results));
                Stop();
                return;
            }
            Complete(title, results);
        }
        catch (Exception ex)
        {
            // Never refresh after an escaped transaction or regeneration error.
            OperationDialogs.Show(this, L.Get("Операция отменена"), ex.Message);
            Stop();
        }
    }

    private string ViewStatus(View view)
    {
        string status;
        try
        {
            var reason = ViewRecreationSupport.UnsupportedReason(view);
            status = reason is null ? L.Get("Поддерживается: пересоздание вида.")
                : L.Get("Не поддерживается") + ": " + reason;
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex) { status = L.Get("Не поддерживается") + ": " + L.Format($"Ошибка анализа: {ex.Message}"); }
        return statuses.TryGetValue(view.Id.Value, out var result) ? status + Environment.NewLine + result : status;
    }

    private void RecreateViews()
    {
        var rows = Checked(viewRows);
        if (rows.Count == 0 || SelectedSourceId is not { } sourceLevelId) return;
        var choices = levels.Where(l => l.Id != sourceLevelId).ToList();
        if (choices.Count == 0) { OperationDialogs.Show(this, L.Get("Пересоздание недоступно"), L.Get("Нет другого уровня.")); return; }
        var dialog = new RecreateViewsWindow(this, choices, rows.Count);
        if (dialog.ShowDialog() != true) return;
        var report = new ViewRecreationService(document).Execute(rows.Select(r => new ElementId(r.Id)).ToList(), dialog.Target.Id, dialog.Prefix);
        foreach (var item in report.Results.Items) statuses[item.ElementId] = item.Reason;
        if (!report.CriticalFailure) Refresh();
        new ViewLogWindow(this, report, id =>
        {
            var created = document.GetElement(new ElementId(id)) as View
                ?? throw new InvalidOperationException(L.Get("Созданный вид больше не существует."));
            application.ActiveUIDocument.ActiveView = created;
        }).ShowDialog();
        if (report.CriticalFailure) Stop();
    }

    private void OpenViews()
    {
        var results = new Dictionary<long, string>();
        foreach (var row in Checked(viewRows))
        {
            try
            {
                var view = document.GetElement(new ElementId(row.Id)) as View ?? throw new InvalidOperationException(L.Get("Вид больше не существует."));
                // Executed in the ExternalEvent callback, with no open transaction.
                application.ActiveUIDocument.ActiveView = view;
                if (!application.ActiveUIDocument.GetOpenUIViews().Any(v => v.ViewId == view.Id))
                    throw new InvalidOperationException(L.Get("Revit не подтвердил открытие вида."));
                results[row.Id] = L.Get("Вид открыт.");
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (Exception ex) { results[row.Id] = L.Format($"Ошибка: {ex.Message}"); }
        }
        Complete(L.Get("Открытие видов — результаты"), results);
    }

    private void DeleteRows(List<TableRow> tableRows, DeleteScope scope)
    {
        var rows = Checked(tableRows);
        var service = new DeleteService(document, scope);
        var previews = new List<DeletePreview>();
        var results = new Dictionary<long, string>();
        foreach (var row in rows)
        {
            try
            {
                previews.Add(service.Preview(new ElementId(row.Id)));
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (DeleteStoppedException) { throw; }
            catch (Exception ex) { results[row.Id] = L.Format($"Не удалён: {ex.Message}"); }
        }
        if (previews.Count == 0) { Complete(L.Get("Удаление не выполнено"), results); return; }
        var total = previews.SelectMany(p => p.DeletedIds).Distinct().Count();
        var description = L.Format($"Отмечено объектов: {rows.Count}. Доступно для удаления: {previews.Count}.\n")
            + L.Format($"Всего объектов с зависимостями: {total}. Удаление затронет следующие объекты:\n\n")
            + string.Join("\n\n", previews.Select(p => p.Description))
            + (results.Count == 0 ? "" : L.Get("\n\nНе будут удалены:\n") + Format(results));
        if (!OperationDialogs.Show(this, L.Get("Подтвердить удаление"), description, L.Format($"Удалить ({total})"))) { Refresh(); return; }
        foreach (var preview in previews)
        {
            try
            {
                if (document.GetElement(preview.Id) is null) { results[preview.Id.Value] = L.Get("Удалён как подтверждённая зависимость другого объекта."); continue; }
                service.Delete(preview); results[preview.Id.Value] = L.Get("Удалён.");
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
            catch (DeleteStoppedException) { throw; }
            catch (Exception ex) { results[preview.Id.Value] = L.Format($"Не удалён: {ex.Message}"); }
        }
        Complete(L.Get("Удаление — результаты по объектам"), results);
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
        foreach (var button in viewButtons) button.IsEnabled = Checked(viewRows).Count > 0;
        foreach (var (table, header) in checkAllHeaders)
        {
            var rows = table.Items.OfType<TableRow>().ToList();
            var count = rows.Count(r => r.IsChecked);
            header.IsEnabled = rows.Count > 0;
            header.IsChecked = count == 0 ? false : count == rows.Count ? true : null;
        }
    }

    private void AddAction(System.Windows.Controls.Panel panel, List<Button>? buttons, string label, Action? action)
    {
        var button = new Button { Content = label, IsEnabled = buttons is null && action is not null,
            Margin = new Thickness(0, 8, 8, 0), Padding = new Thickness(10, 5, 10, 5) };
        if (action is not null) button.Click += (_, _) => QueueAction(() =>
        {
            try { action(); }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException ex)
            {
                OperationDialogs.Show(this, L.Get("Критическая ошибка Revit"), L.Format($"Панель остановлена из-за критической ошибки Revit.\n{ex.Message}"));
                Stop();
            }
            catch (DeleteStoppedException ex)
            {
                OperationDialogs.Show(this, L.Get("Операция отменена"), ex.Message);
                Stop();
            }
            catch (Exception ex)
            {
                if (buttons is not null) Refresh();
                OperationDialogs.Show(this, L.Get("Операция не выполнена"), ex.Message);
            }
        });
        if (action is not null) buttons?.Add(button);
        panel.Children.Add(button);
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
        var header = new CheckBox { ToolTip = L.Get("Отметить все / снять все отметки"), IsEnabled = false,
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
            MinWidth = property == nameof(TableRow.Status) ? 180 : width ?? 100,
            Width = width is { } value ? new DataGridLength(value) : new DataGridLength(1, DataGridLengthUnitType.Star) };
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));
        if (property == nameof(TableRow.Status)) style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        column.ElementStyle = style;
        table.Columns.Add(column); return column;
    }

    private DockPanel MakePanel(DataGrid table, string hint, out WrapPanel actions)
    {
        var panel = new DockPanel { Margin = new Thickness(2) };
        var text = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        var help = new Expander { Header = L.Get("Описание"), Content = text, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(help, Dock.Top); panel.Children.Add(help);
        var controller = new TableSearch(table, UpdateButtons);
        searches.Add(table, controller);
        var search = controller.Bar;
        DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        panel.Children.Add(table); return panel;
    }

    private static string ViewTypeName(ViewType type) => type switch
    {
        ViewType.FloorPlan => L.Get("План этажа"), ViewType.CeilingPlan => L.Get("План потолка"), ViewType.EngineeringPlan => L.Get("План конструкций"),
        ViewType.AreaPlan => L.Get("План площадей"), _ => type.ToString()
    };
}
