using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RevitConnector.Transport;

namespace RevitConnector.UI
{
    /// <summary>
    /// Frozen ribbon bitmaps. Build them on the Revit UI thread (OnStartup is STA).
    /// </summary>
    internal sealed class ConnectionIcons
    {
        private readonly ImageSource _connectedLarge;
        private readonly ImageSource _connectedSmall;
        private readonly ImageSource _connectingLarge;
        private readonly ImageSource _connectingSmall;
        private readonly ImageSource _reconnectingLarge;
        private readonly ImageSource _reconnectingSmall;
        private readonly ImageSource _offlineLarge;
        private readonly ImageSource _offlineSmall;

        private ConnectionIcons()
        {
            _connectedLarge = DrawStatus(Color.FromRgb(46, 125, 50), 32);
            _connectedSmall = DrawStatus(Color.FromRgb(46, 125, 50), 16);
            _connectingLarge = DrawStatus(Color.FromRgb(249, 168, 37), 32);
            _connectingSmall = DrawStatus(Color.FromRgb(249, 168, 37), 16);
            _reconnectingLarge = DrawStatus(Color.FromRgb(239, 108, 0), 32);
            _reconnectingSmall = DrawStatus(Color.FromRgb(239, 108, 0), 16);
            _offlineLarge = DrawStatus(Color.FromRgb(198, 40, 40), 32);
            _offlineSmall = DrawStatus(Color.FromRgb(198, 40, 40), 16);
            ReconnectLarge = DrawReconnect(32);
            ReconnectSmall = DrawReconnect(16);
        }

        public ImageSource ReconnectLarge { get; }
        public ImageSource ReconnectSmall { get; }

        public static ConnectionIcons Create() => new ConnectionIcons();

        public ImageSource Large(ConnectionState state) => Pick(state, large: true);

        public ImageSource Small(ConnectionState state) => Pick(state, large: false);

        private ImageSource Pick(ConnectionState state, bool large)
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    return large ? _connectedLarge : _connectedSmall;
                case ConnectionState.Connecting:
                    return large ? _connectingLarge : _connectingSmall;
                case ConnectionState.Reconnecting:
                    return large ? _reconnectingLarge : _reconnectingSmall;
                default:
                    return large ? _offlineLarge : _offlineSmall;
            }
        }

        private static ImageSource DrawStatus(Color color, int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var fill = FrozenBrush(color);
                var outline = FrozenBrush(Color.FromArgb(80, 0, 0, 0));
                var pen = new Pen(outline, Math.Max(1, size / 16.0));
                pen.Freeze();

                var center = new Point(size / 2.0, size / 2.0);
                var radius = size * 0.30;
                dc.DrawEllipse(fill, pen, center, radius, radius);
            }

            return Rasterize(visual, size);
        }

        private static ImageSource DrawReconnect(int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var brush = FrozenBrush(Color.FromRgb(21, 101, 192));
                var pen = new Pen(brush, Math.Max(1.6, size / 9.0))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                pen.Freeze();

                var cx = size / 2.0;
                var cy = size / 2.0;
                var radius = size * 0.28;
                var start = PointOnCircle(cx, cy, radius, 40);
                var end = PointOnCircle(cx, cy, radius, 300);

                var arc = new StreamGeometry();
                using (var ctx = arc.Open())
                {
                    ctx.BeginFigure(start, isFilled: false, isClosed: false);
                    ctx.ArcTo(
                        end,
                        new Size(radius, radius),
                        rotationAngle: 0,
                        isLargeArc: true,
                        SweepDirection.Clockwise,
                        isStroked: true,
                        isSmoothJoin: true);
                }
                arc.Freeze();
                dc.DrawGeometry(null, pen, arc);

                var tip = end;
                var tangent = (300.0 * Math.PI / 180.0) + (Math.PI / 2.0);
                var head = size * 0.18;
                var left = new Point(
                    tip.X - head * Math.Cos(tangent - 0.55),
                    tip.Y - head * Math.Sin(tangent - 0.55));
                var right = new Point(
                    tip.X - head * Math.Cos(tangent + 0.55),
                    tip.Y - head * Math.Sin(tangent + 0.55));

                var arrow = new StreamGeometry();
                using (var ctx = arrow.Open())
                {
                    ctx.BeginFigure(left, isFilled: true, isClosed: true);
                    ctx.LineTo(tip, isStroked: false, isSmoothJoin: false);
                    ctx.LineTo(right, isStroked: false, isSmoothJoin: false);
                }
                arrow.Freeze();
                dc.DrawGeometry(brush, null, arrow);
            }

            return Rasterize(visual, size);
        }

        private static Point PointOnCircle(double cx, double cy, double radius, double degrees)
        {
            var radians = degrees * Math.PI / 180.0;
            return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
        }

        private static SolidColorBrush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static BitmapImage Rasterize(DrawingVisual visual, int size)
        {
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
