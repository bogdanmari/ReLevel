using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Level = Autodesk.Revit.DB.Level;
using ReLevel.Revit.UI.Prototype;
using Binding = System.Windows.Data.Binding;

namespace ReLevel.Revit.UI;

internal sealed partial class TransferPanel
{
    private Button? transferButton, recreateButton;
    private readonly CheckBox includeUpper = new() { Content = L.Get("Включить верхние привязки"),
        Margin = new Thickness(0) };
    private readonly ContentControl levelHeading = new() { VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ContentControl backNavigation = new() { HorizontalContentAlignment = HorizontalAlignment.Left };
    public void SetNavigation(FrameworkElement title, FrameworkElement back)
    {
        levelHeading.Content = title; backNavigation.Content = back;
    }
    public FrameworkElement View => this;
    public IReadOnlyCollection<long> AdditionalElementIds => additionalElementIds;
    private string LevelCaption(Level level) => index is null ? level.Name
        : $"{level.Name} ({index.Count(level.Id.Value)}{(index.Errors.Count > 0 ? "*" : "")})";

    public void RefreshAfterDistribution()
    {
        statuses.Clear();
        Refresh();
    }

    public void ProcessInformation(IReadOnlyList<long> ids, bool delete, Transfer.DeleteScope scope = Transfer.DeleteScope.Elements)
    {
        if (transferring || ids.Count == 0) return;
        try
        {
            if (delete)
                DeleteCheckedRows(ids.Select(id => new TableRow(UpdateButtons) { Id = id }).ToList(), scope);
            else
            {
                application.ActiveUIDocument.Selection.SetElementIds(ids.Select(id => new Autodesk.Revit.DB.ElementId(id)).ToList());
                var selected = application.ActiveUIDocument.Selection.GetElementIds().Select(id => id.Value).ToHashSet();
                OperationDialogs.Show(owner, L.Get("Выделение элементов"), Format(ids.ToDictionary(id => id, id => selected.Contains(id)
                    ? L.Get("Выделен в модели.") : L.Get("Не выделен: Revit не включил объект в выделение."))));
            }
        }
        catch (Exception ex)
        {
            OperationDialogs.Show(owner, L.Get("Операция отменена"), ex.Message);
            Close();
        }
    }

    public void SetLanguage(bool english)
    {
        language.SelectedIndex = english ? 0 : 1;
    }

    public void OpenLevel(long levelId, bool views)
    {
        var level = levels.First(item => item.Id.Value == levelId);
        if (source.SelectedItem is not Level previous || previous.Id != level.Id)
        {
            includeUpper.IsChecked = false;
            source.SelectedItem = level;
        }
        else Refresh();
        tabs.SelectedIndex = views ? 1 : 0;
    }

    private void Close() => owner.Close();

    private FrameworkElement CreateSelectors()
    {
        var panel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        includeUpper.Checked += (_, _) => Refresh();
        includeUpper.Unchecked += (_, _) => Refresh();
        panel.Children.Add(includeUpper);
        return panel;
    }

    private void ApplyModernStyle()
    {
        foreach (var table in new[] { elementTable, viewTable })
        {
            TransferLayout.StyleTable(table);
        }
    }
}
