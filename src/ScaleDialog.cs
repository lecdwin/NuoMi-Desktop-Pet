using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace NuoMiDesktopPet
{
    internal sealed class ScaleDialog : Forms.Form
    {
        private readonly Forms.NumericUpDown _percentage;
        private readonly Font _baseFont;
        private readonly Font _titleFont;
        private readonly Font _inputFont;
        private readonly Font _percentFont;
        private bool _fontsDisposed;

        public ScaleDialog(
            int currentPercentage,
            int minimumPercentage,
            int maximumPercentage)
        {
            Text = "设置糯米大小";
            _baseFont =
                new Font(
                    "Microsoft YaHei UI",
                    10.0F);
            _titleFont =
                new Font(
                    _baseFont.FontFamily,
                    12.0F,
                    FontStyle.Bold);
            _inputFont =
                new Font(
                    _baseFont.FontFamily,
                    14.0F,
                    FontStyle.Bold);
            _percentFont =
                new Font(
                    _baseFont.FontFamily,
                    13.0F,
                    FontStyle.Bold);
            Font = _baseFont;
            AutoScaleMode = Forms.AutoScaleMode.Dpi;
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = Forms.FormStartPosition.CenterScreen;
            ClientSize = new Size(382, 214);
            TopMost = true;

            Forms.Label title = new Forms.Label();
            title.AutoSize = false;
            title.Text = "整体大小";
            title.Font = _titleFont;
            title.Location = new Point(22, 18);
            title.Size = new Size(160, 28);
            Controls.Add(title);

            Forms.Label hint = new Forms.Label();
            hint.AutoSize = false;
            hint.Text =
                String.Format(
                    "可输入 {0}%–{1}%，建议 60%–125%。\r\n",
                    minimumPercentage,
                    maximumPercentage) +
                "设置后小猫、键盘、鼠标和互动道具会一起缩放。";
            hint.ForeColor = Color.FromArgb(92, 82, 78);
            hint.Location = new Point(22, 50);
            hint.Size = new Size(338, 46);
            Controls.Add(hint);

            _percentage = new Forms.NumericUpDown();
            _percentage.Minimum = minimumPercentage;
            _percentage.Maximum = maximumPercentage;
            _percentage.Increment = 5;
            _percentage.DecimalPlaces = 0;
            _percentage.TextAlign = Forms.HorizontalAlignment.Right;
            _percentage.Font = _inputFont;
            _percentage.AccessibleName =
                "糯米整体大小百分比";
            _percentage.AccessibleDescription =
                "输入 60 到 150 之间的整数，糯米和互动道具会一起缩放。";
            _percentage.Location = new Point(22, 106);
            _percentage.Size = new Size(132, 34);
            _percentage.TabIndex = 0;
            _percentage.Value = Math.Max(
                minimumPercentage,
                Math.Min(maximumPercentage, currentPercentage));
            Controls.Add(_percentage);

            Forms.Label percent = new Forms.Label();
            percent.AutoSize = false;
            percent.Text = "%";
            percent.Font = _percentFont;
            percent.Location = new Point(160, 109);
            percent.Size = new Size(36, 30);
            Controls.Add(percent);

            Forms.Button reset = new Forms.Button();
            reset.Text = "恢复 100%(&R)";
            reset.Location = new Point(210, 106);
            reset.Size = new Size(150, 34);
            reset.TabIndex = 1;
            reset.Click += delegate
            {
                _percentage.Value = Math.Max(
                    _percentage.Minimum,
                    Math.Min(_percentage.Maximum, 100));
            };
            Controls.Add(reset);

            Forms.Button cancel = new Forms.Button();
            cancel.Text = "取消(&C)";
            cancel.DialogResult = Forms.DialogResult.Cancel;
            cancel.Location = new Point(184, 163);
            cancel.Size = new Size(82, 34);
            cancel.TabIndex = 2;
            Controls.Add(cancel);

            Forms.Button confirm = new Forms.Button();
            confirm.Text = "确定(&O)";
            confirm.DialogResult = Forms.DialogResult.OK;
            confirm.Location = new Point(278, 163);
            confirm.Size = new Size(82, 34);
            confirm.TabIndex = 3;
            Controls.Add(confirm);

            AcceptButton = confirm;
            CancelButton = cancel;
        }

        public int SelectedPercentage
        {
            get { return Decimal.ToInt32(_percentage.Value); }
        }

        public void CenterOnCursorScreen()
        {
            StartPosition = Forms.FormStartPosition.Manual;
            Rectangle initialArea =
                Forms.Screen.FromPoint(
                    Forms.Cursor.Position).WorkingArea;
            Location = new Point(
                initialArea.Left + 16,
                initialArea.Top + 16);

            Action centerAndClamp = delegate
            {
                if (IsDisposed)
                {
                    return;
                }

                Rectangle area =
                    Forms.Screen.FromPoint(
                        Forms.Cursor.Position).WorkingArea;
                int targetX =
                    area.Left + (area.Width - Width) / 2;
                int targetY =
                    area.Top + (area.Height - Height) / 2;
                Location = new Point(
                    Math.Max(
                        area.Left,
                        Math.Min(
                            area.Right - Width,
                            targetX)),
                    Math.Max(
                        area.Top,
                        Math.Min(
                            area.Bottom - Height,
                            targetY)));
            };

            Load += delegate
            {
                centerAndClamp();
                BeginInvoke(centerAndClamp);
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing || _fontsDisposed)
            {
                return;
            }

            _fontsDisposed = true;
            _percentFont.Dispose();
            _inputFont.Dispose();
            _titleFont.Dispose();
            _baseFont.Dispose();
        }
    }

    internal sealed class WindowHandleOwner : Forms.IWin32Window
    {
        public WindowHandleOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; private set; }
    }
}
