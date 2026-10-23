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
            ConnectLarge = DrawPlug(32, joined: true);
            ConnectSmall = DrawPlug(16, joined: true);
            DisconnectLarge = DrawPlug(32, joined: false);
            DisconnectSmall = DrawPlug(16, joined: false);
        }

        public ImageSource ConnectLarge { get; }
        public ImageSource ConnectSmall { get; }
        public ImageSource DisconnectLarge { get; }
        public ImageSource DisconnectSmall { get; }

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

        /// <summary>
        /// Side view of a plug and a wall socket. Joined pins sit in the holes;
        /// the disconnect icon leaves a gap and turns the socket rim red.
        /// </summary>
        private static ImageSource DrawPlug(int size, bool joined)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var plugBrush = FrozenBrush(Color.FromRgb(21, 101, 192));
                var rimBrush = FrozenBrush(joined
                    ? Color.FromRgb(46, 125, 50)
                    : Color.FromRgb(198, 40, 40));
                var plateBrush = FrozenBrush(Color.FromRgb(236, 239, 241));
                var holeBrush = FrozenBrush(Color.FromRgb(55, 71, 79));
                var prongBrush = FrozenBrush(Color.FromRgb(120, 144, 156));

                var socket = new Rect(
                    joined ? size * 0.48 : size * 0.58,
                    size * 0.12,
                    joined ? size * 0.44 : size * 0.36,
                    size * 0.76);
                var rim = new Pen(rimBrush, Math.Max(1.6, size * 0.08));
                rim.Freeze();
                dc.DrawRoundedRectangle(plateBrush, rim, socket, size * 0.10, size * 0.10);

                var holeRadius = Math.Max(1.2, size * 0.065);
                var holeX = socket.X + socket.Width * 0.58;
                var topHole = new Point(holeX, socket.Y + socket.Height * 0.34);
                var bottomHole = new Point(holeX, socket.Y + socket.Height * 0.66);
                dc.DrawEllipse(holeBrush, null, topHole, holeRadius, holeRadius);
                dc.DrawEllipse(holeBrush, null, bottomHole, holeRadius, holeRadius);

                var plug = new Rect(
                    joined ? size * 0.08 : size * 0.02,
                    size * 0.26,
                    joined ? size * 0.28 : size * 0.26,
                    size * 0.48);
                dc.DrawRoundedRectangle(plugBrush, null, plug, size * 0.08, size * 0.08);

                var cord = new Pen(plugBrush, Math.Max(1.6, size * 0.09))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                cord.Freeze();
                var midY = size / 2.0;
                dc.DrawLine(
                    cord,
                    new Point(Math.Max(0.5, plug.X - size * 0.04), midY),
                    new Point(plug.X + size * 0.10, midY));

                var prong = new Pen(prongBrush, Math.Max(1.5, size * 0.07))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                prong.Freeze();
                var prongStart = plug.Right - size * 0.02;
                var prongEnd = joined ? holeX : socket.X - size * 0.08;
                if (prongEnd < prongStart + size * 0.06)
                    prongEnd = prongStart + size * 0.06;

                dc.DrawLine(prong, new Point(prongStart, topHole.Y), new Point(prongEnd, topHole.Y));
                dc.DrawLine(prong, new Point(prongStart, bottomHole.Y), new Point(prongEnd, bottomHole.Y));
            }

            return Rasterize(visual, size);
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
