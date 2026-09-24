using System.Windows.Controls;
using System.Windows.Data;

namespace ReLevel.Revit.UI;

internal sealed class SearchableComboBox : ComboBox
{
    private ListCollectionView? choices;
    private Func<object, string> name = item => item.ToString() ?? "";
    private string query = "";
    private bool updating;

    public SearchableComboBox()
    {
        IsEditable = true;
        IsTextSearchEnabled = false;
        StaysOpenOnEdit = true;
        IsSynchronizedWithCurrentItem = false;
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(TextEdited));
    }

    public void SetItems<T>(IEnumerable<T> items, Func<T, string> getName)
    {
        name = item => getName((T)item);
        query = "";
        choices = new ListCollectionView(items.OrderBy(getName, StringComparer.CurrentCultureIgnoreCase).ToList());
        choices.Filter = item => name(item).Contains(query, StringComparison.CurrentCultureIgnoreCase);
        ItemsSource = choices;
    }

    public void ClearSelection()
    {
        updating = true;
        try
        {
            SelectedItem = null;
            Text = "";
            query = "";
            choices?.Refresh();
            IsDropDownOpen = false;
        }
        finally { updating = false; }
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        var wasUpdating = updating;
        updating = true;
        try { base.OnSelectionChanged(e); }
        finally { updating = wasUpdating; }
    }

    protected override void OnDropDownOpened(EventArgs e)
    {
        // Reopening a committed selection should offer every level again.
        if (SelectedItem is not null && choices is not null)
        {
            query = "";
            choices.Refresh();
        }
        base.OnDropDownOpened(e);
    }

    private void TextEdited(object sender, TextChangedEventArgs e)
    {
        if (updating || choices is null || e.OriginalSource is not TextBox editor
            || editor.Name != "PART_EditableTextBox" || !editor.IsKeyboardFocused) return;
        var text = editor.Text;
        if (SelectedItem is not null && text == name(SelectedItem)) return;
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        updating = true;
        try
        {
            // A partial or unknown name is a search, never an operation target.
            SelectedItem = null;
            query = text;
            choices.Refresh();
            Text = text;
            IsDropDownOpen = true;
            editor.Text = text;
            editor.Select(start, length);
        }
        finally { updating = false; }
    }
}
