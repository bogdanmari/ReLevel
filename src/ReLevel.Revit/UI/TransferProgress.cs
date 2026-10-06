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

    public TransferProgress()
    {
        Margin = new Thickness(0, 8, 0, 0);
        Visibility = Visibility.Collapsed;
        Children.Add(label);
        Children.Add(bar);
    }

    public void Begin(int count)
    {
        completed = 0;
        total = count;
        bar.Maximum = Math.Max(1, count);
        bar.Value = 0;
        Visibility = Visibility.Visible;
        clock.Restart();
        Report(0, count, null);
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
        Report(completed, total, null);
        label.Text = (message ?? (stopped ? L.Get("Перенос остановлен.") : L.Get("Перенос завершён."))) + " " + label.Text;
        Paint();
    }

    private void Paint()
    {
        UpdateLayout();
        // Flush WPF rendering without pumping lower-priority input or moving Revit API off-thread.
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
}
