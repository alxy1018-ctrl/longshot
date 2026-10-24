// 自动化验证用的可滚动目标窗口:
// 最大化窗口, 客户区内是 7000px 高的彩色色带 + BLOCK 编号。
// 手动维护滚动偏移(不依赖 WinForms AutoScroll), 标题栏显示实时滚动位置。

using System;
using System.Drawing;
using System.Windows.Forms;

namespace LongShotTest
{
    internal static class TestTargetProgram
    {
        [STAThread]
        internal static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TestForm());
        }
    }

    internal sealed class TestForm : Form
    {
        private ScrollPanel _panel;

        public TestForm()
        {
            Text = "LongShot Test Target (do not touch)";
            // 激活工具可能把最大化窗口还原；正常尺寸也要足够显示编号。
            ClientSize = new Size(1100, 800);
            WindowState = FormWindowState.Maximized;
            BackColor = Color.White;
            _panel = new ScrollPanel();
            _panel.Dock = DockStyle.Fill;
            Controls.Add(_panel);
            Shown += delegate
            {
                _panel.Focus();
            };
            Activated += delegate
            {
                _panel.Focus();
            };
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_MOUSEWHEEL && _panel != null)
            {
                _panel.HandleWheel(m.WParam);
                return;
            }
            base.WndProc(ref m);
        }
    }

    internal sealed class ScrollPanel : Panel
    {
        private const int ContentHeight = 7000;
        private const int BlockHeight = 100;
        private const int BlockCount = ContentHeight / BlockHeight;
        private int _scrollY;

        public ScrollPanel()
        {
            TabStop = true;
            DoubleBuffered = true;
            BackColor = Color.White;
            ResizeRedraw = true;
        }

        public void HandleWheel(IntPtr wParam)
        {
            int zDelta = (short)((long)wParam >> 16);
            int lines = zDelta / 120;
            int maxScroll = Math.Max(0, ContentHeight - ClientSize.Height);
            _scrollY -= lines * 100;
            if (_scrollY < 0) _scrollY = 0;
            if (_scrollY > maxScroll) _scrollY = maxScroll;
            Invalidate();
            Form form = FindForm();
            if (form != null)
            {
                form.Text = "LongShot Test Target scroll=" + _scrollY;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                HandleWheel(m.WParam);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.TranslateTransform(0, -_scrollY);
            int w = Math.Max(ClientSize.Width, 400);
            for (int i = 0; i < BlockCount; i++)
            {
                int y = i * BlockHeight;
                double hue = (i * 360.0 / BlockCount) % 360.0;
                Color c = HsvToRgb(hue, 0.55, 0.88);
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.FillRectangle(b, 0, y, w, BlockHeight);
                }
                Color textColor = HsvToRgb(hue, 0.80, 0.35);
                using (SolidBrush tb = new SolidBrush(textColor))
                using (Font f = new Font("Arial", 34, FontStyle.Bold))
                {
                    g.DrawString("BLOCK " + (i + 1) + " / " + BlockCount, f, tb, 40, y + 18);
                }
            }
        }

        private static Color HsvToRgb(double h, double s, double v)
        {
            int hi = (int)(h / 60.0) % 6;
            double f = h / 60.0 - Math.Floor(h / 60.0);
            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);
            double r = 0, g = 0, b = 0;
            switch (hi)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }
    }
}
