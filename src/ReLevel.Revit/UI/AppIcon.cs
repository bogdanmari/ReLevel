using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReLevel.Revit.UI;

// One vector master for the window, navigation and exported manifest icons.
internal static class AppIcon
{
    public static ImageSource Create(int size = 64)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 64d, size / 64d));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(21, 125, 136)), null, new Rect(0, 0, 64, 64), 13, 13);
            var line = new Pen(Brushes.White, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(line, new Point(13, 17), new Point(34, 17));
            dc.DrawLine(line, new Point(13, 32), new Point(29, 32));
            dc.DrawLine(line, new Point(13, 47), new Point(34, 47));
            var arrow = new Pen(new SolidColorBrush(Color.FromRgb(153, 235, 221)), 4)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, arrow, Geometry.Parse("M45,47 V17 M38,24 L45,17 L52,24"));
            dc.Pop();
        }
        var image = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); image.Freeze(); return image;
    }
}
