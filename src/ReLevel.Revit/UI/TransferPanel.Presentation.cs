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
    private readonly SearchableComboBox target = new() { DisplayMemberPath = "Name" };
    private Button? transferButton, recreateButton;
    public FrameworkElement View => this;
    public IReadOnlyCollection<long> AdditionalElementIds => additionalElementIds;
    private string LevelCaption(Level level) => index is null ? level.Name
        : $"{level.Name} ({index.Count(level.Id.Value)}{(index.Errors.Count > 0 ? "*" : "")})";

    public void RefreshAfterDistribution()
    {
        statuses.Clear();
        Refresh();
    }

    public void SetLanguage(bool english)
    {
        language.SelectedIndex = english ? 0 : 1;
    }

    private void Close() => owner.Close();

    private FrameworkElement CreateSelectors() => TransferLayout.Selectors(source, target,
        L.Get("Исходный уровень"), L.Get("Целевой уровень"));

    private void UpdateTargets()
    {
        var previous = target.SelectedItem as Level;
        var sourceId = (source.SelectedItem as Level)?.Id;
        var available = levels.Where(level => level.Id != sourceId).ToList();
        target.SetItems(available, level => level.Name);
        target.ClearSelection();
        if (previous is not null && available.Any(level => level.Id == previous.Id)) target.SelectedItem = previous;
    }

    private bool HasTarget => source.SelectedItem is Level from && target.SelectedItem is Level to
        && from.Id != to.Id;

    private void ApplyModernStyle()
    {
        foreach (var table in new[] { elementTable, viewTable })
        {
            TransferLayout.StyleTable(table);
        }
    }
}
