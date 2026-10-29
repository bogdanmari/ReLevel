using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ReLevel.Revit.UI;

// Only this small WPF window lives on a separate UI thread. All Revit reads stay
// on the calling external-command thread; the loader receives strings only.
internal sealed class ModelAnalysisLoader : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly Stopwatch throttle = Stopwatch.StartNew();
    private Window? window;
    private TextBlock? progress;
    private Exception? startupError;
    private bool closing;

    public ModelAnalysisLoader()
    {
        var title = L.Get("Анализ элементов модели…");
        thread = new Thread(() =>
        {
            try
            {
                window = Create(title);
                // No cross-thread owner or activation: the Revit UI thread is synchronously
                // reading the model and must not receive blocking window-owner messages.
                window.Closing += (_, e) => e.Cancel = !closing;
                window.Show();
                ready.Set();
                Dispatcher.Run();
            }
            catch (Exception ex) { startupError = ex; ready.Set(); }
        }) { IsBackground = true, Name = "ReLevel analysis indicator" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (startupError is not null)
        {
            ready.Dispose();
            throw new InvalidOperationException(title, startupError);
        }
    }

    public void Report(int completed, int total)
    {
        if (completed != 0 && completed != total && throttle.ElapsedMilliseconds < 100) return;
        throttle.Restart();
        var text = L.Format($"Проанализировано: {completed} / {total}.");
        window!.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => progress!.Text = text));
    }

    private Window Create(string title)
    {
        var accent = new SolidColorBrush(Color.FromRgb(21, 125, 136));
        var content = new StackPanel { Margin = new Thickness(30), VerticalAlignment = VerticalAlignment.Center };
        var spinner = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 0, 16) };
        spinner.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = new SolidColorBrush(Color.FromRgb(213, 231, 233)), StrokeThickness = 4 });
        var arc = new System.Windows.Shapes.Path { Data = Geometry.Parse("M20,2 A18,18 0 0 1 38,20"), Stroke = accent, StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = new RotateTransform(0, 20, 20) };
        ((RotateTransform)arc.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.85)) { RepeatBehavior = RepeatBehavior.Forever });
        spinner.Children.Add(arc); content.Children.Add(spinner);
        content.Children.Add(new TextBlock { Text = title, FontSize = 16, TextAlignment = TextAlignment.Center });
        progress = new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        content.Children.Add(progress);
        return new Window { Title = "ReLevel", Width = 390, Height = 205, ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Segoe UI"), Foreground = new SolidColorBrush(Color.FromRgb(24, 50, 71)),
            Background = new SolidColorBrush(Color.FromRgb(244, 247, 250)),
            Content = new Border { BorderBrush = accent, BorderThickness = new Thickness(1), Child = content } };
    }

    public void Dispose()
    {
        if (window is not null && !window.Dispatcher.HasShutdownStarted)
            window.Dispatcher.Invoke(() =>
            {
                closing = true;
                window.Close();
                window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            });
        thread.Join();
        ready.Dispose();
    }
}
