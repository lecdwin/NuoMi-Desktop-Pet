using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace NuoMiDesktopPet
{
    /// <summary>
    /// Creates the warm, flat tray menu used by NuoMi.  The menu deliberately
    /// remains a regular ContextMenuStrip so keyboard navigation, screen reader
    /// support and Windows menu behaviour are preserved.
    /// </summary>
    internal static class TrayMenuTheme
    {
        public static Forms.ContextMenuStrip CreateContextMenu()
        {
            return new NuoMiContextMenuStrip();
        }
    }

    internal sealed class NuoMiContextMenuStrip :
        Forms.ContextMenuStrip
    {
        private readonly NuoMiTrayMenuRenderer _themeRenderer;
        private readonly HashSet<Forms.ToolStripDropDown> _dropDowns;
        private readonly HashSet<Forms.ToolStripMenuItem> _dropDownItems;
        private readonly Font _menuFont;
        private bool _menuFontDisposed;

        public NuoMiContextMenuStrip()
        {
            _themeRenderer = new NuoMiTrayMenuRenderer();
            _dropDowns =
                new HashSet<Forms.ToolStripDropDown>();
            _dropDownItems =
                new HashSet<Forms.ToolStripMenuItem>();
            _menuFont =
                new Font(
                    "Microsoft YaHei UI",
                    9.5F,
                    FontStyle.Regular,
                    GraphicsUnit.Point);

            AutoSize = true;
            BackColor = NuoMiTrayPalette.Surface;
            ForeColor = NuoMiTrayPalette.Text;
            Font = _menuFont;
            DropShadowEnabled = true;
            ShowImageMargin = false;
            ShowCheckMargin = true;
            Renderer = _themeRenderer;
        }

        protected override void OnOpening(CancelEventArgs e)
        {
            // Let consumers refresh dynamic labels/check states first.  Sizing
            // afterwards prevents a changed label from being clipped.
            base.OnOpening(e);
            if (e.Cancel)
            {
                return;
            }

            ApplyThemeTree();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            // The final drop-down size is only guaranteed after layout.
            ApplyRoundedRegion(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Forms.ToolStripDropDown dropDown in _dropDowns)
                {
                    dropDown.Opening -= DropDownOpening;
                    dropDown.Opened -= DropDownOpened;
                }

                foreach (Forms.ToolStripMenuItem item in _dropDownItems)
                {
                    item.DropDownOpening -= ItemDropDownOpening;
                }

                _dropDowns.Clear();
                _dropDownItems.Clear();
            }

            base.Dispose(disposing);

            if (disposing && !_menuFontDisposed)
            {
                _menuFontDisposed = true;
                _menuFont.Dispose();
            }
        }

        private void ApplyThemeTree()
        {
            float scale = GetDpiScale(this);
            ApplyDropDownStyle(this, scale, true);
            ApplyItemCollection(Items, scale);
        }

        private void ApplyItemCollection(
            Forms.ToolStripItemCollection items,
            float scale)
        {
            for (int index = 0; index < items.Count; index++)
            {
                Forms.ToolStripSeparator separator =
                    items[index] as Forms.ToolStripSeparator;
                if (separator != null)
                {
                    separator.AutoSize = false;
                    separator.Margin = Forms.Padding.Empty;
                    separator.Padding = Forms.Padding.Empty;
                    separator.Size =
                        new Size(
                            1,
                            Scale(11, scale));
                    continue;
                }

                Forms.ToolStripMenuItem menuItem =
                    items[index] as Forms.ToolStripMenuItem;
                if (menuItem == null)
                {
                    continue;
                }

                menuItem.AutoSize = true;
                menuItem.ForeColor =
                    !menuItem.Enabled
                        ? NuoMiTrayPalette.DisabledText
                        : HasRole(
                            menuItem,
                            "NuoMi.Danger")
                            ? NuoMiTrayPalette.Danger
                            : HasRole(
                                menuItem,
                                "NuoMi.Accent")
                                ? NuoMiTrayPalette.AccentDark
                                : NuoMiTrayPalette.Text;
                menuItem.Margin =
                    new Forms.Padding(
                        Scale(3, scale),
                        Scale(1, scale),
                        Scale(3, scale),
                        Scale(1, scale));
                menuItem.Padding =
                    new Forms.Padding(
                        Scale(8, scale),
                        Scale(4, scale),
                        Scale(8, scale),
                        Scale(4, scale));

                if (!menuItem.HasDropDownItems)
                {
                    continue;
                }

                Forms.ToolStripDropDown dropDown =
                    menuItem.DropDown;
                ApplyDropDownStyle(
                    dropDown,
                    GetDpiScale(dropDown),
                    false);
                ApplyItemCollection(
                    menuItem.DropDownItems,
                    GetDpiScale(dropDown));

                if (_dropDownItems.Add(menuItem))
                {
                    menuItem.DropDownOpening +=
                        ItemDropDownOpening;
                }
                if (_dropDowns.Add(dropDown))
                {
                    dropDown.Opening += DropDownOpening;
                    dropDown.Opened += DropDownOpened;
                }
            }
        }

        private void ItemDropDownOpening(
            object sender,
            EventArgs e)
        {
            Forms.ToolStripMenuItem menuItem =
                sender as Forms.ToolStripMenuItem;
            if (menuItem == null)
            {
                return;
            }

            float scale =
                GetDpiScale(menuItem.DropDown);
            ApplyDropDownStyle(
                menuItem.DropDown,
                scale,
                false);
            ApplyItemCollection(
                menuItem.DropDownItems,
                scale);
        }

        private void DropDownOpening(
            object sender,
            CancelEventArgs e)
        {
            Forms.ToolStripDropDown dropDown =
                sender as Forms.ToolStripDropDown;
            if (dropDown == null || e.Cancel)
            {
                return;
            }

            float scale = GetDpiScale(dropDown);
            ApplyDropDownStyle(
                dropDown,
                scale,
                false);
            ApplyItemCollection(
                dropDown.Items,
                scale);
        }

        private void DropDownOpened(
            object sender,
            EventArgs e)
        {
            Forms.ToolStripDropDown dropDown =
                sender as Forms.ToolStripDropDown;
            if (dropDown != null)
            {
                ApplyRoundedRegion(dropDown);
            }
        }

        private void ApplyDropDownStyle(
            Forms.ToolStripDropDown dropDown,
            float scale,
            bool isRoot)
        {
            dropDown.AutoSize = true;
            dropDown.BackColor =
                NuoMiTrayPalette.Surface;
            dropDown.ForeColor =
                NuoMiTrayPalette.Text;
            dropDown.Font = Font;
            dropDown.Renderer =
                _themeRenderer;
            dropDown.Padding =
                new Forms.Padding(
                    Scale(6, scale),
                    Scale(7, scale),
                    Scale(6, scale),
                    Scale(7, scale));

            int minimumWidth =
                Scale(isRoot ? 286 : 206, scale);
            dropDown.MinimumSize =
                new Size(minimumWidth, 0);

            Forms.ToolStripDropDownMenu menu =
                dropDown as Forms.ToolStripDropDownMenu;
            if (menu != null)
            {
                menu.ShowImageMargin = false;
                menu.ShowCheckMargin = true;
            }
        }

        private static void ApplyRoundedRegion(
            Forms.ToolStripDropDown dropDown)
        {
            if (dropDown.Width <= 0 ||
                dropDown.Height <= 0 ||
                dropDown.IsDisposed)
            {
                return;
            }

            float scale = GetDpiScale(dropDown);
            int radius = Scale(10, scale);
            Rectangle bounds =
                new Rectangle(
                    0,
                    0,
                    dropDown.Width,
                    dropDown.Height);

            using (GraphicsPath path =
                NuoMiTrayMenuRenderer.CreateRoundedPath(
                    bounds,
                    radius))
            {
                dropDown.Region = new Region(path);
            }
        }

        private static float GetDpiScale(
            Forms.ToolStrip toolStrip)
        {
            if (toolStrip == null || toolStrip.IsDisposed)
            {
                return 1.0F;
            }

            try
            {
                using (Graphics graphics =
                    toolStrip.CreateGraphics())
                {
                    return Math.Max(
                        0.75F,
                        Math.Min(
                            4.0F,
                            graphics.DpiX / 96.0F));
                }
            }
            catch
            {
                return 1.0F;
            }
        }

        private static int Scale(
            int logicalPixels,
            float scale)
        {
            return Math.Max(
                1,
                (int)Math.Round(
                    logicalPixels * scale));
        }

        private static bool HasRole(
            Forms.ToolStripItem item,
            string role)
        {
            return
                item != null &&
                String.Equals(
                    item.Tag as string,
                    role,
                    StringComparison.Ordinal);
        }
    }

    internal sealed class NuoMiTrayMenuRenderer :
        Forms.ToolStripProfessionalRenderer
    {
        public NuoMiTrayMenuRenderer()
            : base(new NuoMiTrayColorTable())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderToolStripBackground(
            Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(
                NuoMiTrayPalette.Surface);
        }

        protected override void OnRenderImageMargin(
            Forms.ToolStripRenderEventArgs e)
        {
            // Keep the check column and the menu body visually continuous.
            using (SolidBrush brush =
                new SolidBrush(
                    NuoMiTrayPalette.Surface))
            {
                e.Graphics.FillRectangle(
                    brush,
                    e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(
            Forms.ToolStripRenderEventArgs e)
        {
            Rectangle bounds =
                new Rectangle(
                    0,
                    0,
                    Math.Max(1, e.ToolStrip.Width - 1),
                    Math.Max(1, e.ToolStrip.Height - 1));
            float scale = GetScale(e.Graphics);
            int radius = Scale(10, scale);

            SmoothingMode oldSmoothing =
                e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;
            using (GraphicsPath path =
                CreateRoundedPath(bounds, radius))
            using (Pen pen =
                new Pen(
                    NuoMiTrayPalette.Border,
                    Math.Max(1.0F, scale)))
            {
                e.Graphics.DrawPath(pen, path);
            }
            e.Graphics.SmoothingMode =
                oldSmoothing;
        }

        protected override void OnRenderMenuItemBackground(
            Forms.ToolStripItemRenderEventArgs e)
        {
            Forms.ToolStripMenuItem menuItem =
                e.Item as Forms.ToolStripMenuItem;
            bool hasVisibleDropDown =
                menuItem != null &&
                menuItem.HasDropDownItems &&
                menuItem.DropDown.Visible;
            if (menuItem == null ||
                (!menuItem.Selected &&
                 !menuItem.Pressed &&
                 !hasVisibleDropDown))
            {
                return;
            }

            float scale = GetScale(e.Graphics);
            int horizontalInset = Scale(3, scale);
            int verticalInset = Scale(1, scale);
            Rectangle bounds =
                new Rectangle(
                    horizontalInset,
                    verticalInset,
                    Math.Max(
                        1,
                        e.Item.Width -
                        horizontalInset * 2),
                    Math.Max(
                        1,
                        e.Item.Height -
                        verticalInset * 2));
            Color background =
                !e.Item.Enabled
                ? NuoMiTrayPalette.DisabledHover
                : HasRole(
                    menuItem,
                    "NuoMi.Danger")
                    ? NuoMiTrayPalette.DangerHover
                    : (menuItem.Pressed ||
                       hasVisibleDropDown
                        ? NuoMiTrayPalette.Pressed
                        : NuoMiTrayPalette.Hover);

            SmoothingMode oldSmoothing =
                e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;
            using (GraphicsPath path =
                CreateRoundedPath(
                    bounds,
                    Scale(7, scale)))
            using (SolidBrush brush =
                new SolidBrush(background))
            {
                e.Graphics.FillPath(brush, path);
            }
            e.Graphics.SmoothingMode =
                oldSmoothing;
        }

        protected override void OnRenderItemText(
            Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor =
                !e.Item.Enabled
                    ? NuoMiTrayPalette.DisabledText
                    : HasRole(
                        e.Item,
                        "NuoMi.Danger")
                        ? NuoMiTrayPalette.Danger
                        : HasRole(
                            e.Item,
                            "NuoMi.Accent")
                            ? NuoMiTrayPalette.AccentDark
                            : NuoMiTrayPalette.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(
            Forms.ToolStripItemImageRenderEventArgs e)
        {
            Forms.ToolStripMenuItem menuItem =
                e.Item as Forms.ToolStripMenuItem;
            if (menuItem == null)
            {
                return;
            }

            float scale = GetScale(e.Graphics);
            int boxSize = Scale(17, scale);
            Rectangle itemBounds =
                new Rectangle(
                    0,
                    0,
                    e.Item.Width,
                    e.Item.Height);
            Rectangle box =
                new Rectangle(
                    Scale(12, scale),
                    Math.Max(
                        0,
                        (itemBounds.Height - boxSize) / 2),
                    boxSize,
                    boxSize);
            Color fill =
                e.Item.Enabled
                ? NuoMiTrayPalette.Accent
                : NuoMiTrayPalette.DisabledCheck;

            SmoothingMode oldSmoothing =
                e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;
            using (GraphicsPath boxPath =
                CreateRoundedPath(
                    box,
                    Scale(5, scale)))
            using (SolidBrush brush =
                new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, boxPath);
            }

            if (menuItem.CheckState ==
                Forms.CheckState.Indeterminate)
            {
                using (Pen pen =
                    CreateGlyphPen(scale))
                {
                    float y =
                        box.Top + box.Height / 2.0F;
                    e.Graphics.DrawLine(
                        pen,
                        box.Left + box.Width * 0.28F,
                        y,
                        box.Right - box.Width * 0.28F,
                        y);
                }
            }
            else
            {
                using (Pen pen =
                    CreateGlyphPen(scale))
                {
                    PointF first =
                        new PointF(
                            box.Left +
                                box.Width * 0.25F,
                            box.Top +
                                box.Height * 0.53F);
                    PointF middle =
                        new PointF(
                            box.Left +
                                box.Width * 0.44F,
                            box.Top +
                                box.Height * 0.72F);
                    PointF last =
                        new PointF(
                            box.Left +
                                box.Width * 0.76F,
                            box.Top +
                                box.Height * 0.32F);
                    e.Graphics.DrawLines(
                        pen,
                        new PointF[]
                        {
                            first,
                            middle,
                            last
                        });
                }
            }

            e.Graphics.SmoothingMode =
                oldSmoothing;
        }

        protected override void OnRenderArrow(
            Forms.ToolStripArrowRenderEventArgs e)
        {
            float scale = GetScale(e.Graphics);
            Rectangle bounds = e.ArrowRectangle;
            float centerX =
                bounds.Left + bounds.Width / 2.0F;
            float centerY =
                bounds.Top + bounds.Height / 2.0F;
            float halfWidth = Scale(2, scale);
            float halfHeight = Scale(4, scale);
            Color arrowColor =
                e.Item.Enabled
                ? (e.Item.Selected
                    ? NuoMiTrayPalette.AccentDark
                    : NuoMiTrayPalette.SecondaryText)
                : NuoMiTrayPalette.DisabledText;

            SmoothingMode oldSmoothing =
                e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;
            using (Pen pen =
                new Pen(
                    arrowColor,
                    Math.Max(
                        1.4F,
                        1.55F * scale)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(
                    pen,
                    new PointF[]
                    {
                        new PointF(
                            centerX - halfWidth,
                            centerY - halfHeight),
                        new PointF(
                            centerX + halfWidth,
                            centerY),
                        new PointF(
                            centerX - halfWidth,
                            centerY + halfHeight)
                    });
            }
            e.Graphics.SmoothingMode =
                oldSmoothing;
        }

        protected override void OnRenderSeparator(
            Forms.ToolStripSeparatorRenderEventArgs e)
        {
            float scale = GetScale(e.Graphics);
            int start = Scale(42, scale);
            int end = e.Item.Width - Scale(10, scale);
            int y = e.Item.Height / 2;
            if (end <= start)
            {
                return;
            }

            using (Pen pen =
                new Pen(
                    NuoMiTrayPalette.Separator,
                    Math.Max(1.0F, scale)))
            {
                e.Graphics.DrawLine(
                    pen,
                    start,
                    y,
                    end,
                    y);
            }
        }

        private static Pen CreateGlyphPen(float scale)
        {
            Pen pen =
                new Pen(
                    Color.White,
                    Math.Max(1.6F, 1.8F * scale));
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        private static bool HasRole(
            Forms.ToolStripItem item,
            string role)
        {
            return
                item != null &&
                String.Equals(
                    item.Tag as string,
                    role,
                    StringComparison.Ordinal);
        }

        private static float GetScale(Graphics graphics)
        {
            return Math.Max(
                0.75F,
                Math.Min(
                    4.0F,
                    graphics.DpiX / 96.0F));
        }

        private static int Scale(
            int logicalPixels,
            float scale)
        {
            return Math.Max(
                1,
                (int)Math.Round(
                    logicalPixels * scale));
        }

        internal static GraphicsPath CreateRoundedPath(
            Rectangle bounds,
            int radius)
        {
            GraphicsPath path =
                new GraphicsPath();
            int diameter =
                Math.Max(
                    2,
                    Math.Min(
                        radius * 2,
                        Math.Min(
                            bounds.Width,
                            bounds.Height)));
            Rectangle arc =
                new Rectangle(
                    bounds.Left,
                    bounds.Top,
                    diameter,
                    diameter);

            path.AddArc(arc, 180, 90);
            arc.X =
                bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y =
                bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class NuoMiTrayColorTable :
        Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground
        {
            get { return NuoMiTrayPalette.Surface; }
        }

        public override Color ImageMarginGradientBegin
        {
            get { return NuoMiTrayPalette.Surface; }
        }

        public override Color ImageMarginGradientMiddle
        {
            get { return NuoMiTrayPalette.Surface; }
        }

        public override Color ImageMarginGradientEnd
        {
            get { return NuoMiTrayPalette.Surface; }
        }

        public override Color MenuBorder
        {
            get { return NuoMiTrayPalette.Border; }
        }

        public override Color MenuItemBorder
        {
            get { return NuoMiTrayPalette.Hover; }
        }

        public override Color MenuItemSelected
        {
            get { return NuoMiTrayPalette.Hover; }
        }

        public override Color MenuItemSelectedGradientBegin
        {
            get { return NuoMiTrayPalette.Hover; }
        }

        public override Color MenuItemSelectedGradientEnd
        {
            get { return NuoMiTrayPalette.Hover; }
        }

        public override Color MenuItemPressedGradientBegin
        {
            get { return NuoMiTrayPalette.Pressed; }
        }

        public override Color MenuItemPressedGradientMiddle
        {
            get { return NuoMiTrayPalette.Pressed; }
        }

        public override Color MenuItemPressedGradientEnd
        {
            get { return NuoMiTrayPalette.Pressed; }
        }

        public override Color CheckBackground
        {
            get { return NuoMiTrayPalette.Accent; }
        }

        public override Color CheckSelectedBackground
        {
            get { return NuoMiTrayPalette.Accent; }
        }

        public override Color CheckPressedBackground
        {
            get { return NuoMiTrayPalette.AccentDark; }
        }

        public override Color SeparatorDark
        {
            get { return NuoMiTrayPalette.Separator; }
        }

        public override Color SeparatorLight
        {
            get { return NuoMiTrayPalette.Surface; }
        }
    }

    internal static class NuoMiTrayPalette
    {
        public static readonly Color Surface =
            Color.FromArgb(253, 251, 248);
        public static readonly Color Text =
            Color.FromArgb(53, 46, 43);
        public static readonly Color SecondaryText =
            Color.FromArgb(154, 137, 128);
        public static readonly Color DisabledText =
            Color.FromArgb(184, 166, 157);
        public static readonly Color Hover =
            Color.FromArgb(255, 243, 232);
        public static readonly Color Pressed =
            Color.FromArgb(255, 229, 214);
        public static readonly Color DisabledHover =
            Color.FromArgb(248, 241, 236);
        public static readonly Color Border =
            Color.FromArgb(232, 224, 216);
        public static readonly Color Separator =
            Color.FromArgb(237, 230, 224);
        public static readonly Color Accent =
            Color.FromArgb(185, 85, 43);
        public static readonly Color AccentDark =
            Color.FromArgb(159, 71, 38);
        public static readonly Color DisabledCheck =
            Color.FromArgb(218, 196, 183);
        public static readonly Color Danger =
            Color.FromArgb(182, 61, 61);
        public static readonly Color DangerHover =
            Color.FromArgb(255, 236, 235);
    }
}
