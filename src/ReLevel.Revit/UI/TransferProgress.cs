using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ReLevel.Revit.UI;

internal sealed class TransferProgress : StackPanel
{
    private readonly TextBlock label = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar bar = new() { Height = 6, Margin = new Thickness(0, 5, 0, 0) };
    private readonly Stopwatch clock = new();
    private int completed;
    private int total;
    private bool running;
    public bool CancellationRequested { get; private set; }
    private readonly Button cancel = new() { Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(12, 5, 12, 5), Visibility = Visibility.Collapsed };

    public TransferProgress()
    {
        Margin = new Thickness(0, 8, 0, 0);
        Visibility = Visibility.Collapsed;
        var row = new DockPanel();
        DockPanel.SetDock(cancel, Dock.Right);
        row.Children.Add(cancel);
        var details = new StackPanel();
        details.Children.Add(label); details.Children.Add(bar);
        row.Children.Add(details); Children.Add(row);
        cancel.Click += (_, _) => { CancellationRequested = true; cancel.IsEnabled = false; };
    }

    public void Begin(int count)
    {
        completed = 0;
        CancellationRequested = false;
        running = false;
        cancel.Content = L.Get("Отмена");
        cancel.IsEnabled = true;
        cancel.Visibility = Visibility.Visible;
        total = count;
        bar.Maximum = Math.Max(1, count);
        bar.Value = 0;
        Visibility = Visibility.Visible;
        clock.Restart();
        Report(0, count, null);
        running = true;
        cancel.Focus();
    }

    public void Report(int done, int count, long? currentId)
    {
        completed = done;
        total = count;
        bar.Value = done;
        var elapsed = clock.Elapsed.ToString(@"hh\:mm\:ss");
        label.Text = currentId is { } id
            ? L.Format($"Обработано: {done} / {count}. Текущий ID: {id}. Прошло: {elapsed}.")
            : L.Format($"Обработано: {done} / {count}. Прошло: {elapsed}.");
        Paint();
    }

    public void Finish(bool stopped, string? message = null)
    {
        clock.Stop();
        running = false;
        cancel.Visibility = Visibility.Collapsed;
        Report(completed, total, null);
        label.Text = (CancellationRequested && !stopped ? L.Get("Запуск отменён. Все изменения запуска отменены.") : message ?? (stopped ? L.Get("Перенос остановлен.") : L.Get("Перенос завершён."))) + " " + label.Text;
        Paint();
    }

    private void Paint()
    {
        UpdateLayout();
        // Input is pumped only at operation boundaries, with all other controls disabled.
        Dispatcher.Invoke(() => { }, running ? DispatcherPriority.Background : DispatcherPriority.Render);
    }
}
