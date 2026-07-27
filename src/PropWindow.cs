using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Drawing.Rectangle;

namespace NuoMiDesktopPet
{
    internal enum PropKind
    {
        Cup,
        FoodBowl,
        ToyBall
    }

    internal sealed class PropWindow : Window
    {
        private const int MonitorDefaultToNearest = 2;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        private static readonly Brush ShadowOuterBrush = MakeSolidBrush(28, 87, 62, 47);
        private static readonly Brush ShadowMiddleBrush = MakeSolidBrush(45, 87, 62, 47);
        private static readonly Brush ShadowInnerBrush = MakeSolidBrush(58, 87, 62, 47);
        private static readonly Brush OutlineBrush = MakeSolidBrush(255, 105, 68, 50);
        private static readonly Brush WhiteBrush = MakeSolidBrush(255, 255, 252, 244);
        private static readonly Brush PinkBrush = MakeSolidBrush(255, 238, 126, 137);
        private static readonly Brush DarkFaceBrush = MakeSolidBrush(255, 76, 54, 45);
        private static readonly Brush WaterBrush = MakeLinearBrush(
            Color.FromArgb(235, 180, 235, 255),
            Color.FromArgb(245, 74, 183, 239),
            new Point(0.0, 0.0),
            new Point(0.2, 1.0));
        private static readonly Brush CupBrush = MakeLinearBrush(
            Color.FromRgb(255, 250, 238),
            Color.FromRgb(239, 178, 133),
            new Point(0.1, 0.0),
            new Point(0.9, 1.0));
        private static readonly Brush CupRimBrush = MakeLinearBrush(
            Color.FromRgb(255, 255, 250),
            Color.FromRgb(239, 198, 168),
            new Point(0.0, 0.0),
            new Point(0.0, 1.0));
        private static readonly Brush BowlBrush = MakeLinearBrush(
            Color.FromRgb(255, 185, 120),
            Color.FromRgb(218, 82, 63),
            new Point(0.0, 0.0),
            new Point(0.7, 1.0));
        private static readonly Brush BowlRimBrush = MakeLinearBrush(
            Color.FromRgb(255, 232, 191),
            Color.FromRgb(230, 128, 84),
            new Point(0.0, 0.0),
            new Point(0.0, 1.0));
        private static readonly Brush FoodBrush = MakeLinearBrush(
            Color.FromRgb(209, 143, 69),
            Color.FromRgb(128, 73, 39),
            new Point(0.0, 0.0),
            new Point(0.0, 1.0));
        private static readonly Brush FishBrush = MakeLinearBrush(
            Color.FromRgb(255, 222, 124),
            Color.FromRgb(222, 127, 52),
            new Point(0.0, 0.0),
            new Point(1.0, 1.0));
        private static readonly Brush FishLightBrush = MakeSolidBrush(255, 255, 240, 176);
        private static readonly Brush BallBrush = MakeBallBrush();
        private static readonly Brush BallHighlightBrush = MakeSolidBrush(125, 255, 244, 247);
        private static readonly Brush YarnDarkBrush = MakeSolidBrush(185, 164, 56, 93);
        private static readonly Brush YarnLightBrush = MakeSolidBrush(210, 255, 184, 206);

        private static readonly Pen OutlinePen = MakePen(OutlineBrush, 2.0);
        private static readonly Pen ThinOutlinePen = MakePen(OutlineBrush, 1.25);
        private static readonly Pen FacePen = MakePen(DarkFaceBrush, 1.6);
        private static readonly Pen WaterPen = MakePen(MakeSolidBrush(220, 53, 155, 215), 1.0);
        private static readonly Pen YarnDarkPen = MakePen(YarnDarkBrush, 2.4);
        private static readonly Pen YarnLightPen = MakePen(YarnLightBrush, 1.8);

        private double _visualRotation;
        private double _actionProgress;

        public PropWindow(PropKind kind)
        {
            Kind = kind;
            Title = GetTitle(kind);
            Width = 128.0;
            Height = 128.0;
            MinWidth = 128.0;
            MinHeight = 128.0;
            MaxWidth = 128.0;
            MaxHeight = 128.0;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
        }

        public PropKind Kind { get; private set; }

        public double VisualRotation
        {
            get { return _visualRotation; }
            set
            {
                double next = SanitizeRotation(value);
                if (Math.Abs(_visualRotation - next) < 0.001)
                {
                    return;
                }

                _visualRotation = next;
                InvalidateVisual();
            }
        }

        public double ActionProgress
        {
            get { return _actionProgress; }
            set
            {
                double next = Clamp01(value);
                if (Math.Abs(_actionProgress - next) < 0.0001)
                {
                    return;
                }

                _actionProgress = next;
                InvalidateVisual();
            }
        }

        public new event Action<PropWindow> Activated;

        public void MoveToPixels(int x, int y)
        {
            IntPtr handle = EnsureWindowHandle();
            if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                x,
                y,
                0,
                0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public Rectangle GetPixelBounds()
        {
            IntPtr handle = EnsureWindowHandle();
            NativeRect bounds;
            if (!GetWindowRect(handle, out bounds))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        }

        public void ClampToWorkArea()
        {
            IntPtr handle = EnsureWindowHandle();
            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            MonitorInfo monitorInfo = new MonitorInfo();
            monitorInfo.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            NativeRect bounds;
            if (!GetWindowRect(handle, out bounds))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            int width = Math.Max(1, bounds.Right - bounds.Left);
            int height = Math.Max(1, bounds.Bottom - bounds.Top);
            int maximumX = Math.Max(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Right - width);
            int maximumY = Math.Max(monitorInfo.WorkArea.Top, monitorInfo.WorkArea.Bottom - height);
            int targetX = Clamp(bounds.Left, monitorInfo.WorkArea.Left, maximumX);
            int targetY = Clamp(bounds.Top, monitorInfo.WorkArea.Top, maximumY);

            if (targetX != bounds.Left || targetY != bounds.Top)
            {
                MoveToPixels(targetX, targetY);
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Action<PropWindow> handler = Activated;
            if (handler != null)
            {
                handler(this);
            }

            e.Handled = true;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            switch (Kind)
            {
                case PropKind.Cup:
                    DrawCup(drawingContext);
                    break;
                case PropKind.FoodBowl:
                    DrawFoodBowl(drawingContext);
                    break;
                case PropKind.ToyBall:
                    DrawToyBall(drawingContext);
                    break;
            }
        }

        private void DrawCup(DrawingContext drawingContext)
        {
            DrawSoftShadow(drawingContext, new Rect(27.0, 105.0, 76.0, 12.0), 1.0);

            double spill = Clamp01((Math.Abs(_visualRotation) - 7.0) / 48.0 + _actionProgress * 0.65);
            if (spill > 0.02)
            {
                DrawWaterDrops(drawingContext, spill);
            }

            drawingContext.PushTransform(new RotateTransform(_visualRotation, 64.0, 65.0));

            drawingContext.DrawRoundedRectangle(
                null,
                MakePen(CupBrush, 8.0),
                new Rect(78.0, 51.0, 28.0, 34.0),
                14.0,
                14.0);
            drawingContext.DrawRoundedRectangle(
                null,
                MakePen(WhiteBrush, 2.4),
                new Rect(81.0, 54.0, 21.0, 27.0),
                11.0,
                11.0);

            StreamGeometry cupBody = new StreamGeometry();
            using (StreamGeometryContext context = cupBody.Open())
            {
                context.BeginFigure(new Point(33.0, 43.0), true, true);
                context.LineTo(new Point(88.0, 43.0), true, false);
                context.QuadraticBezierTo(new Point(85.0, 78.0), new Point(78.0, 96.0), true, false);
                context.QuadraticBezierTo(new Point(62.0, 103.0), new Point(45.0, 96.0), true, false);
                context.QuadraticBezierTo(new Point(36.0, 76.0), new Point(33.0, 43.0), true, false);
            }

            cupBody.Freeze();
            drawingContext.DrawGeometry(CupBrush, OutlinePen, cupBody);
            drawingContext.DrawEllipse(CupRimBrush, OutlinePen, new Point(60.5, 43.0), 28.0, 8.3);
            drawingContext.DrawEllipse(WaterBrush, WaterPen, new Point(60.5, 43.4), 22.5, 4.8);
            drawingContext.DrawEllipse(
                MakeSolidBrush(145, 255, 255, 255),
                null,
                new Point(54.0, 41.8),
                11.0,
                1.6);

            drawingContext.DrawEllipse(DarkFaceBrush, null, new Point(50.0, 68.0), 2.6, 3.5);
            drawingContext.DrawEllipse(DarkFaceBrush, null, new Point(71.0, 68.0), 2.6, 3.5);
            drawingContext.DrawEllipse(WhiteBrush, null, new Point(49.2, 66.8), 0.8, 1.0);
            drawingContext.DrawEllipse(WhiteBrush, null, new Point(70.2, 66.8), 0.8, 1.0);
            drawingContext.DrawEllipse(PinkBrush, null, new Point(42.8, 75.5), 4.0, 2.0);
            drawingContext.DrawEllipse(PinkBrush, null, new Point(78.2, 75.5), 4.0, 2.0);

            StreamGeometry smile = new StreamGeometry();
            using (StreamGeometryContext context = smile.Open())
            {
                context.BeginFigure(new Point(55.0, 76.5), false, false);
                context.QuadraticBezierTo(new Point(60.5, 82.0), new Point(66.0, 76.5), true, false);
            }

            smile.Freeze();
            drawingContext.DrawGeometry(null, FacePen, smile);

            StreamGeometry highlight = new StreamGeometry();
            using (StreamGeometryContext context = highlight.Open())
            {
                context.BeginFigure(new Point(42.0, 53.0), false, false);
                context.QuadraticBezierTo(new Point(40.5, 69.0), new Point(46.0, 84.0), true, false);
            }

            highlight.Freeze();
            drawingContext.DrawGeometry(null, MakePen(MakeSolidBrush(125, 255, 255, 255), 3.0), highlight);
            drawingContext.Pop();
        }

        private void DrawWaterDrops(DrawingContext drawingContext, double spill)
        {
            int direction = _visualRotation >= 0.0 ? 1 : -1;
            Point localLip = direction > 0 ? new Point(88.0, 43.0) : new Point(33.0, 43.0);
            Point lip = RotatePoint(localLip, new Point(64.0, 65.0), _visualRotation);
            int count = spill > 0.72 ? 3 : (spill > 0.35 ? 2 : 1);

            for (int i = 0; i < count; i++)
            {
                double fall = (_actionProgress + i * 0.31 + spill * 0.12) % 1.0;
                Point top = new Point(
                    lip.X + direction * (5.0 + i * 2.5),
                    lip.Y + 5.0 + fall * 47.0);
                double size = Math.Max(2.8, 5.2 - i * 0.7);
                drawingContext.DrawGeometry(WaterBrush, WaterPen, CreateDroplet(top, size));
            }
        }

        private void DrawFoodBowl(DrawingContext drawingContext)
        {
            DrawSoftShadow(drawingContext, new Rect(18.0, 105.0, 92.0, 13.0), 1.0);
            double fishLift = -5.5 * Math.Sin(_actionProgress * Math.PI);

            drawingContext.DrawEllipse(
                BowlRimBrush,
                OutlinePen,
                new Point(64.0, 69.0),
                45.0,
                15.0);
            drawingContext.DrawEllipse(
                FoodBrush,
                ThinOutlinePen,
                new Point(64.0, 67.5),
                36.5,
                9.8);

            DrawDriedFish(drawingContext, new Point(46.0, 58.0 + fishLift), -20.0, 0.92);
            DrawDriedFish(drawingContext, new Point(66.0, 55.0 + fishLift * 0.7), 8.0, 1.08);
            DrawDriedFish(drawingContext, new Point(84.0, 60.0 + fishLift * 0.45), 25.0, 0.82);

            StreamGeometry bowl = new StreamGeometry();
            using (StreamGeometryContext context = bowl.Open())
            {
                context.BeginFigure(new Point(20.0, 70.0), true, true);
                context.QuadraticBezierTo(new Point(26.0, 99.0), new Point(43.0, 106.0), true, false);
                context.QuadraticBezierTo(new Point(64.0, 113.0), new Point(85.0, 106.0), true, false);
                context.QuadraticBezierTo(new Point(102.0, 99.0), new Point(108.0, 70.0), true, false);
                context.QuadraticBezierTo(new Point(64.0, 84.0), new Point(20.0, 70.0), true, false);
            }

            bowl.Freeze();
            drawingContext.DrawGeometry(BowlBrush, OutlinePen, bowl);
            drawingContext.DrawEllipse(
                MakeSolidBrush(80, 255, 245, 219),
                null,
                new Point(52.0, 91.0),
                18.0,
                4.5);

            DrawFishBadge(drawingContext, new Point(65.0, 94.0));
        }

        private static void DrawDriedFish(
            DrawingContext drawingContext,
            Point center,
            double rotation,
            double scale)
        {
            drawingContext.PushTransform(new RotateTransform(rotation, center.X, center.Y));
            drawingContext.PushTransform(new ScaleTransform(scale, scale, center.X, center.Y));

            StreamGeometry tail = new StreamGeometry();
            using (StreamGeometryContext context = tail.Open())
            {
                context.BeginFigure(new Point(center.X - 11.0, center.Y), true, true);
                context.LineTo(new Point(center.X - 21.0, center.Y - 7.0), true, false);
                context.LineTo(new Point(center.X - 20.0, center.Y + 7.0), true, false);
            }

            tail.Freeze();
            drawingContext.DrawGeometry(FishBrush, ThinOutlinePen, tail);

            StreamGeometry body = new StreamGeometry();
            using (StreamGeometryContext context = body.Open())
            {
                context.BeginFigure(new Point(center.X - 12.0, center.Y), true, true);
                context.BezierTo(
                    new Point(center.X - 4.0, center.Y - 10.0),
                    new Point(center.X + 13.0, center.Y - 8.0),
                    new Point(center.X + 18.0, center.Y),
                    true,
                    false);
                context.BezierTo(
                    new Point(center.X + 12.0, center.Y + 8.0),
                    new Point(center.X - 4.0, center.Y + 10.0),
                    new Point(center.X - 12.0, center.Y),
                    true,
                    false);
            }

            body.Freeze();
            drawingContext.DrawGeometry(FishBrush, ThinOutlinePen, body);
            drawingContext.DrawEllipse(DarkFaceBrush, null, new Point(center.X + 11.0, center.Y - 1.2), 1.5, 1.5);
            drawingContext.DrawEllipse(FishLightBrush, null, new Point(center.X + 2.0, center.Y - 3.0), 6.0, 1.3);
            drawingContext.Pop();
            drawingContext.Pop();
        }

        private static void DrawFishBadge(DrawingContext drawingContext, Point center)
        {
            StreamGeometry badge = new StreamGeometry();
            using (StreamGeometryContext context = badge.Open())
            {
                context.BeginFigure(new Point(center.X - 14.0, center.Y), true, true);
                context.LineTo(new Point(center.X - 21.0, center.Y - 6.0), true, false);
                context.LineTo(new Point(center.X - 21.0, center.Y + 6.0), true, false);
                context.LineTo(new Point(center.X - 14.0, center.Y), true, false);
                context.QuadraticBezierTo(new Point(center.X, center.Y - 10.0), new Point(center.X + 14.0, center.Y), true, false);
                context.QuadraticBezierTo(new Point(center.X, center.Y + 10.0), new Point(center.X - 14.0, center.Y), true, false);
            }

            badge.Freeze();
            drawingContext.DrawGeometry(
                MakeSolidBrush(205, 255, 229, 167),
                MakePen(MakeSolidBrush(210, 178, 77, 58), 1.1),
                badge);
            drawingContext.DrawEllipse(
                MakeSolidBrush(220, 145, 78, 62),
                null,
                new Point(center.X + 8.0, center.Y - 1.0),
                1.4,
                1.4);
        }

        private void DrawToyBall(DrawingContext drawingContext)
        {
            double bounce = 18.0 * Math.Sin(_actionProgress * Math.PI);
            double shadowScale = 1.0 - bounce / 55.0;
            double shadowWidth = 78.0 * shadowScale;
            DrawSoftShadow(
                drawingContext,
                new Rect(64.0 - shadowWidth / 2.0, 106.0, shadowWidth, 12.0),
                shadowScale);

            drawingContext.PushTransform(new TranslateTransform(0.0, -bounce));

            StreamGeometry looseThread = new StreamGeometry();
            using (StreamGeometryContext context = looseThread.Open())
            {
                context.BeginFigure(new Point(91.0, 84.0), false, false);
                context.BezierTo(
                    new Point(108.0, 82.0),
                    new Point(105.0, 103.0),
                    new Point(119.0, 99.0),
                    true,
                    false);
                context.BezierTo(
                    new Point(124.0, 97.0),
                    new Point(121.0, 91.0),
                    new Point(117.0, 94.0),
                    true,
                    false);
            }

            looseThread.Freeze();
            drawingContext.DrawGeometry(null, YarnDarkPen, looseThread);

            double roll = _visualRotation + _actionProgress * 270.0;
            drawingContext.PushTransform(new RotateTransform(roll, 62.0, 67.0));
            drawingContext.DrawEllipse(BallBrush, OutlinePen, new Point(62.0, 67.0), 39.0, 39.0);

            StreamGeometry strandOne = new StreamGeometry();
            using (StreamGeometryContext context = strandOne.Open())
            {
                context.BeginFigure(new Point(28.0, 53.0), false, false);
                context.BezierTo(
                    new Point(45.0, 38.0),
                    new Point(75.0, 38.0),
                    new Point(96.0, 55.0),
                    true,
                    false);
                context.BeginFigure(new Point(27.0, 76.0), false, false);
                context.BezierTo(
                    new Point(43.0, 92.0),
                    new Point(76.0, 96.0),
                    new Point(96.0, 77.0),
                    true,
                    false);
                context.BeginFigure(new Point(52.0, 29.0), false, false);
                context.BezierTo(
                    new Point(42.0, 50.0),
                    new Point(43.0, 84.0),
                    new Point(57.0, 104.0),
                    true,
                    false);
                context.BeginFigure(new Point(76.0, 31.0), false, false);
                context.BezierTo(
                    new Point(85.0, 51.0),
                    new Point(83.0, 85.0),
                    new Point(70.0, 104.0),
                    true,
                    false);
            }

            strandOne.Freeze();
            drawingContext.DrawGeometry(null, YarnDarkPen, strandOne);

            StreamGeometry strandTwo = new StreamGeometry();
            using (StreamGeometryContext context = strandTwo.Open())
            {
                context.BeginFigure(new Point(31.0, 64.0), false, false);
                context.BezierTo(
                    new Point(48.0, 54.0),
                    new Point(75.0, 56.0),
                    new Point(93.0, 66.0),
                    true,
                    false);
                context.BeginFigure(new Point(41.0, 39.0), false, false);
                context.BezierTo(
                    new Point(56.0, 53.0),
                    new Point(59.0, 86.0),
                    new Point(83.0, 94.0),
                    true,
                    false);
            }

            strandTwo.Freeze();
            drawingContext.DrawGeometry(null, YarnLightPen, strandTwo);
            drawingContext.DrawEllipse(BallHighlightBrush, null, new Point(48.0, 44.0), 10.0, 6.0);
            drawingContext.Pop();
            drawingContext.Pop();
        }

        private static void DrawSoftShadow(
            DrawingContext drawingContext,
            Rect bounds,
            double strength)
        {
            double centerX = bounds.X + bounds.Width / 2.0;
            double centerY = bounds.Y + bounds.Height / 2.0;
            drawingContext.PushOpacity(Clamp01(strength));
            drawingContext.DrawEllipse(
                ShadowOuterBrush,
                null,
                new Point(centerX, centerY),
                bounds.Width / 2.0,
                bounds.Height / 2.0);
            drawingContext.DrawEllipse(
                ShadowMiddleBrush,
                null,
                new Point(centerX, centerY),
                bounds.Width * 0.39,
                bounds.Height * 0.35);
            drawingContext.DrawEllipse(
                ShadowInnerBrush,
                null,
                new Point(centerX, centerY),
                bounds.Width * 0.27,
                bounds.Height * 0.22);
            drawingContext.Pop();
        }

        private static Geometry CreateDroplet(Point top, double size)
        {
            StreamGeometry droplet = new StreamGeometry();
            using (StreamGeometryContext context = droplet.Open())
            {
                context.BeginFigure(top, true, true);
                context.BezierTo(
                    new Point(top.X - size * 0.35, top.Y + size * 0.8),
                    new Point(top.X - size, top.Y + size * 1.1),
                    new Point(top.X - size * 0.72, top.Y + size * 1.75),
                    true,
                    false);
                context.BezierTo(
                    new Point(top.X - size * 0.35, top.Y + size * 2.45),
                    new Point(top.X + size * 0.35, top.Y + size * 2.45),
                    new Point(top.X + size * 0.72, top.Y + size * 1.75),
                    true,
                    false);
                context.BezierTo(
                    new Point(top.X + size, top.Y + size * 1.1),
                    new Point(top.X + size * 0.35, top.Y + size * 0.8),
                    top,
                    true,
                    false);
            }

            droplet.Freeze();
            return droplet;
        }

        private static Point RotatePoint(Point point, Point pivot, double angle)
        {
            double radians = angle * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            double deltaX = point.X - pivot.X;
            double deltaY = point.Y - pivot.Y;
            return new Point(
                pivot.X + deltaX * cosine - deltaY * sine,
                pivot.Y + deltaX * sine + deltaY * cosine);
        }

        private static double SanitizeRotation(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }

            value %= 360.0;
            if (value > 180.0)
            {
                value -= 360.0;
            }
            else if (value < -180.0)
            {
                value += 360.0;
            }

            return value;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || value <= 0.0)
            {
                return 0.0;
            }

            if (double.IsInfinity(value) || value >= 1.0)
            {
                return 1.0;
            }

            return value;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        private static string GetTitle(PropKind kind)
        {
            switch (kind)
            {
                case PropKind.Cup:
                    return "糯米的小杯子";
                case PropKind.FoodBowl:
                    return "糯米的饭碗";
                case PropKind.ToyBall:
                    return "糯米的毛线球";
                default:
                    return "糯米的玩具";
            }
        }

        private IntPtr EnsureWindowHandle()
        {
            return new WindowInteropHelper(this).EnsureHandle();
        }

        private static Brush MakeSolidBrush(byte alpha, byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Brush MakeLinearBrush(
            Color start,
            Color end,
            Point startPoint,
            Point endPoint)
        {
            LinearGradientBrush brush = new LinearGradientBrush(start, end, startPoint, endPoint);
            brush.Freeze();
            return brush;
        }

        private static Brush MakeBallBrush()
        {
            RadialGradientBrush brush = new RadialGradientBrush();
            brush.Center = new Point(0.48, 0.48);
            brush.GradientOrigin = new Point(0.31, 0.24);
            brush.RadiusX = 0.74;
            brush.RadiusY = 0.74;
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 183, 211), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(244, 105, 158), 0.48));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(184, 54, 104), 1.0));
            brush.Freeze();
            return brush;
        }

        private static Pen MakePen(Brush brush, double thickness)
        {
            Pen pen = new Pen(brush, thickness);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
            pen.Freeze();
            return pen;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect MonitorArea;
            public NativeRect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect bounds);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);
    }
}
