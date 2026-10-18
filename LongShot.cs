// LongShot - 滚动长截图工具
// 编译: build.bat (csc /codepage:65001)
// 用法: 双击 LongShot.exe -> 点"开始" -> 自动滚动当前前台窗口并拼接长图
//       保存到 C:\Users\LXY\Pictures\Screenshots，完成后自动退出。
// 参数: --auto  启动后自动开始（自动化测试用，成功后直接退出、不开文件夹）

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LongShot
{
    // ---------------- Win32 ----------------
    internal static class Native
    {
        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        internal const uint INPUT_MOUSE = 0;

        internal static uint ScrollWheel(int delta)
        {
            INPUT input = new INPUT();
            input.type = INPUT_MOUSE;
            input.U.mi.mouseData = (uint)delta;
            input.U.mi.dwFlags = MOUSEEVENTF_WHEEL;
            INPUT[] inputs = new INPUT[1];
            inputs[0] = input;
            return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        internal const uint MOUSEEVENTF_WHEEL = 0x0800;
        internal const int VK_ESCAPE = 0x1B;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }
    }

    internal sealed class CaptureTarget
    {
        public IntPtr Hwnd;
        public Rectangle ClientRect;
        public bool IsWindow;
    }

    internal static class Paths
    {
        // 用户指定的保存目录
        internal static readonly string OutputDir = @"C:\Users\LXY\Pictures\Screenshots";

        internal static string UniqueFile(string dir, string prefix)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = System.IO.Path.Combine(dir, prefix + "_" + stamp + ".png");
            int i = 1;
            while (File.Exists(path))
            {
                path = System.IO.Path.Combine(dir, prefix + "_" + stamp + "_" + i + ".png");
                i++;
            }
            return path;
        }
    }

    // 选择截取目标: 前台窗口客户区优先, 失败回退屏幕工作区(排除任务栏)
    internal static class TargetPicker
    {
        public static CaptureTarget Pick()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && Native.IsWindowVisible(fg))
            {
                StringBuilder cls = new StringBuilder(256);
                Native.GetClassName(fg, cls, cls.Capacity);
                string name = cls.ToString();
                // 桌面/任务栏不是可滚动目标, 直接回退
                if (name != "Progman" && name != "WorkerW" &&
                    name != "Shell_TrayWnd" && name != "Button")
                {
                    Native.RECT rc;
                    if (Native.GetClientRect(fg, out rc))
                    {
                        int w = rc.Right - rc.Left;
                        int h = rc.Bottom - rc.Top;
                        if (w >= 320 && h >= 240)
                        {
                            Native.POINT pt;
                            pt.X = 0;
                            pt.Y = 0;
                            if (Native.ClientToScreen(fg, ref pt))
                            {
                                Rectangle r = new Rectangle(pt.X, pt.Y, w, h);
                                Rectangle vs = SystemInformation.VirtualScreen;
                                r.Intersect(vs);
                                if (r.Width >= 320 && r.Height >= 240)
                                {
                                    CaptureTarget t = new CaptureTarget();
                                    t.Hwnd = fg;
                                    t.ClientRect = r;
                                    t.IsWindow = true;
                                    return t;
                                }
                            }
                        }
                    }
                }
            }

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (wa.Width >= 320 && wa.Height >= 240)
            {
                CaptureTarget t = new CaptureTarget();
                t.Hwnd = IntPtr.Zero;
                t.ClientRect = wa;
                t.IsWindow = false;
                return t;
            }
            return null;
        }
    }

    internal static class ScreenCapture
    {
        public static Bitmap Grab(Rectangle rect)
        {
            Bitmap bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }
    }

    internal struct ShiftInfo
    {
        public bool Valid;
        public int Shift;
        public double Score;
    }

    // 计算下一帧相对上一帧的垂直滚动像素数
    internal static class Matcher
    {
        private const int Downsample = 4;

        public static ShiftInfo FindShift(Bitmap prev, Bitmap next)
        {
            ShiftInfo none = new ShiftInfo();
            int w = prev.Width;
            int h = prev.Height;
            if (next.Width != w || next.Height != h) return none;

            int sw = w / Downsample;
            int sh = h / Downsample;
            if (sw < 16 || sh < 16) return none;

            byte[] pg = Gray(prev, Downsample, sw, sh);
            byte[] ng = Gray(next, Downsample, sw, sh);

            // 右侧约 32px 留给滚动条, 不参与匹配
            int useW = sw - 8;
            if (useW < sw / 2) useW = sw;

            double same = Mae(ng, pg, sw, 0, useW, sh);
            if (same < 1.8)
            {
                ShiftInfo s = new ShiftInfo();
                s.Valid = true;
                s.Shift = 0;
                s.Score = same;
                return s;
            }

            int minD = 2;
            int maxD = (int)(sh * 0.85);
            int limit = sh - sh / 5;
            if (maxD > limit) maxD = limit;

            int bestD = 0;
            double bestScore = double.MaxValue;
            for (int d = minD; d <= maxD; d++)
            {
                int rows = sh - d;
                if (rows < sh / 5) break;
                double s = Mae(ng, pg, sw, d, useW, rows);
                if (s < bestScore)
                {
                    bestScore = s;
                    bestD = d;
                }
            }
            if (bestD == 0 || bestScore > 12.0) return none;

            // 粗搜是 4px 精度, 用全分辨率精修到 1px
            byte[] pgFull = Gray(prev, 1, w, h);
            byte[] ngFull = Gray(next, 1, w, h);
            int useWFull = w - 32;
            if (useWFull < w / 2) useWFull = w;

            int coarse = bestD * Downsample;
            int lo = coarse - 8;
            int hi = coarse + 8;
            if (lo < 2) lo = 2;
            if (hi > h - 16) hi = h - 16;

            int fineBest = coarse;
            double fineScore = double.MaxValue;
            for (int d = lo; d <= hi; d++)
            {
                double s = MaeFull(ngFull, pgFull, w, h, d, useWFull);
                if (s < fineScore)
                {
                    fineScore = s;
                    fineBest = d;
                }
            }

            ShiftInfo result = new ShiftInfo();
            result.Valid = true;
            result.Shift = fineBest;
            result.Score = fineScore;
            return result;
        }

        private static double Mae(byte[] nextGray, byte[] prevGray, int stride, int d, int useW, int rows)
        {
            long sum = 0;
            for (int y = 0; y < rows; y++)
            {
                int ni = y * stride;
                int pi = (y + d) * stride;
                for (int x = 0; x < useW; x++)
                {
                    int diff = nextGray[ni + x] - prevGray[pi + x];
                    if (diff < 0) diff = -diff;
                    sum += diff;
                }
            }
            return (double)sum / ((double)rows * useW);
        }

        private static double MaeFull(byte[] nextGray, byte[] prevGray, int w, int h, int d, int useW)
        {
            long sum = 0;
            long count = 0;
            int rows = h - d;
            for (int y = 0; y < rows; y += 2)
            {
                int ni = y * w;
                int pi = (y + d) * w;
                for (int x = 0; x < useW; x += 2)
                {
                    int diff = nextGray[ni + x] - prevGray[pi + x];
                    if (diff < 0) diff = -diff;
                    sum += diff;
                    count++;
                }
            }
            if (count == 0) return double.MaxValue;
            return (double)sum / count;
        }

        private static byte[] Gray(Bitmap bmp, int step, int sw, int sh)
        {
            byte[] result = new byte[sw * sh];
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* basePtr = (byte*)data.Scan0;
                    int stride = data.Stride;
                    for (int y = 0; y < sh; y++)
                    {
                        byte* row = basePtr + (long)y * step * stride;
                        int outRow = y * sw;
                        for (int x = 0; x < sw; x++)
                        {
                            byte* px = row + (long)x * step * 4;
                            result[outRow + x] = (byte)((px[0] * 114 + px[1] * 587 + px[2] * 299) >> 10);
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return result;
        }
    }

    internal static class Stitcher
    {
        // 最终图片最大高度(物理像素), 防止超长页面耗尽内存
        private const int MaxTotalHeight = 18000;

        public static string RunAuto(CaptureTarget target, Action<string> progress)
        {
            Rectangle rect = target.ClientRect;
            // 把目标窗口置前, 保证滚轮事件落在它身上
            if (target.IsWindow)
            {
                Native.SetForegroundWindow(target.Hwnd);
                Thread.Sleep(250);
            }
            Bitmap first = ScreenCapture.Grab(rect);
            int w = first.Width;
            int h = first.Height;
            if (w < 320 || h < 240)
            {
                first.Dispose();
                throw new Exception("截取区域太小, 无法拼接。");
            }

            Bitmap total = new Bitmap(w, MaxTotalHeight, PixelFormat.Format32bppArgb);
            Bitmap prev = first;
            try
            {
                using (Graphics g = Graphics.FromImage(total))
                {
                    g.DrawImageUnscaled(first, 0, 0);
                }

                int totalH = h;
                int fails = 0;
                int wheelSteps = 3;
                int step = 0;

                while (fails < 5 && totalH < MaxTotalHeight - 100 && step < 300)
                {
                    step++;
                    if ((Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0) break;
                    if (!StillValid(target)) break;

                    if (target.IsWindow)
                    {
                        Native.SetForegroundWindow(target.Hwnd);
                        Thread.Sleep(120);
                    }
                    Cursor.Position = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                    Thread.Sleep(60);

                    for (int i = 0; i < wheelSteps; i++)
                    {
                        Native.ScrollWheel(-120);
                        Thread.Sleep(120);
                    }
                    Thread.Sleep(600);

                    if (!StillValid(target)) break;

                    Bitmap next;
                    try
                    {
                        next = ScreenCapture.Grab(rect);
                    }
                    catch
                    {
                        break;
                    }

                    ShiftInfo info = Matcher.FindShift(prev, next);
                    if (!info.Valid)
                    {
                        // 平滑滚动可能还没结束, 再等一会儿重拍一次
                        next.Dispose();
                        Thread.Sleep(350);
                        try
                        {
                            next = ScreenCapture.Grab(rect);
                        }
                        catch
                        {
                            break;
                        }
                        info = Matcher.FindShift(prev, next);
                    }
                    if (!info.Valid || info.Shift < 8)
                    {
                        fails++;
                        next.Dispose();
                        continue;
                    }

                    fails = 0;
                    int d = info.Shift;
                    int maxAppend = h - 40;
                    if (d > maxAppend) d = maxAppend;
                    if (totalH + d > MaxTotalHeight)
                    {
                        next.Dispose();
                        break;
                    }

                    // 新内容在下一帧底部 d 行
                    using (Graphics g = Graphics.FromImage(total))
                    {
                        g.DrawImage(next,
                            new Rectangle(0, totalH - d, w, d),
                            new Rectangle(0, h - d, w, d),
                            GraphicsUnit.Pixel);
                    }
                    totalH += d;
                    prev.Dispose();
                    prev = next;

                    progress("正在截图: 已拼接约 " + ((int)(totalH / h)) + " 屏...");

                    // 自适应滚动步长, 让每次位移约为视口的 1/3
                    double ratio = (double)d / h;
                    if (ratio < 0.10 && wheelSteps < 8) wheelSteps++;
                    else if (ratio > 0.60 && wheelSteps > 1) wheelSteps--;
                }

                if (totalH < h + 20)
                {
                    progress("未检测到滚动, 保存当前单屏...");
                }

                return SaveResult(total, w, totalH, progress);
            }
            finally
            {
                prev.Dispose();
                if (total != null) total.Dispose();
            }
        }

        // 手动模式: 不主动滚动, 用户自己滚; 按 Esc 结束并保存
        public static string RunManual(CaptureTarget target, Action<string> progress)
        {
            Rectangle rect = target.ClientRect;
            if (target.IsWindow)
            {
                Native.SetForegroundWindow(target.Hwnd);
                Thread.Sleep(250);
            }
            Bitmap first = ScreenCapture.Grab(rect);
            int w = first.Width;
            int h = first.Height;
            if (w < 320 || h < 240)
            {
                first.Dispose();
                throw new Exception("截取区域太小, 无法拼接。");
            }

            Bitmap total = new Bitmap(w, MaxTotalHeight, PixelFormat.Format32bppArgb);
            Bitmap prev = first;
            try
            {
                using (Graphics g = Graphics.FromImage(total))
                {
                    g.DrawImageUnscaled(first, 0, 0);
                }

                int totalH = h;
                int frames = 0;
                while (totalH < MaxTotalHeight - 100 && frames < 6000)
                {
                    frames++;
                    if (!StillValid(target)) break;
                    Thread.Sleep(220);
                    if ((Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0) break;

                    Bitmap next;
                    try
                    {
                        next = ScreenCapture.Grab(rect);
                    }
                    catch
                    {
                        break;
                    }

                    ShiftInfo info = Matcher.FindShift(prev, next);
                    if (!info.Valid || info.Shift < 4)
                    {
                        // 还没滚动 / 往回滚 / 滚太快匹配不上, 继续等
                        next.Dispose();
                        continue;
                    }

                    int d = info.Shift;
                    int maxAppend = h - 40;
                    if (d > maxAppend) d = maxAppend;
                    if (totalH + d > MaxTotalHeight)
                    {
                        next.Dispose();
                        break;
                    }

                    using (Graphics g = Graphics.FromImage(total))
                    {
                        g.DrawImage(next,
                            new Rectangle(0, totalH - d, w, d),
                            new Rectangle(0, h - d, w, d),
                            GraphicsUnit.Pixel);
                    }
                    totalH += d;
                    prev.Dispose();
                    prev = next;
                    progress("手动记录中: 已记录约 " + ((int)(totalH / h)) + " 屏...");
                }

                return SaveResult(total, w, totalH, progress);
            }
            finally
            {
                prev.Dispose();
                total.Dispose();
            }
        }

        private static string SaveResult(Bitmap total, int w, int totalH, Action<string> progress)
        {
            Bitmap result = new Bitmap(w, totalH, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.DrawImage(total,
                        new Rectangle(0, 0, w, totalH),
                        new Rectangle(0, 0, w, totalH),
                        GraphicsUnit.Pixel);
                }
                string dir = Paths.OutputDir;
                Directory.CreateDirectory(dir);
                string path = Paths.UniqueFile(dir, "LongShot");
                progress("正在保存图片...");
                byte[] pngData;
                using (MemoryStream ms = new MemoryStream())
                {
                    result.Save(ms, ImageFormat.Png);
                    pngData = ms.ToArray();
                }
                File.WriteAllBytes(path, pngData);
                return path;
            }
            finally
            {
                result.Dispose();
            }
        }

        private static bool StillValid(CaptureTarget target)
        {
            if (!target.IsWindow) return true;
            if (!Native.IsWindowVisible(target.Hwnd)) return false;
            Native.RECT rc;
            if (!Native.GetClientRect(target.Hwnd, out rc)) return false;
            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            if (Math.Abs(w - target.ClientRect.Width) > 40) return false;
            if (Math.Abs(h - target.ClientRect.Height) > 40) return false;
            return true;
        }
    }

    public sealed class MainForm : Form
    {
        private Button _autoButton;
        private Button _manualButton;
        private Label _status;
        private Label _title;
        private Label _desc;
        private NotifyIcon _tray;
        private bool _auto;
        private bool _manualArg;
        private bool _busy;

        public MainForm(bool auto, bool manual)
        {
            _auto = auto;
            _manualArg = manual;
            SuspendLayout();
            Text = "长截图工具";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(400, 232);
            Font = new Font("Microsoft YaHei UI", 9F);

            _title = new Label();
            _title.Text = "滚动长截图";
            _title.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            _title.AutoSize = true;
            _title.Location = new Point(20, 16);
            Controls.Add(_title);

            _desc = new Label();
            _desc.Text = "自动: 自动滚动并拼接\r\n手动: 自己滚动, 按 Esc 结束并拼接";
            _desc.AutoSize = false;
            _desc.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(_desc);

            _autoButton = new Button();
            _autoButton.Text = "自动";
            _autoButton.Size = new Size(120, 42);
            _autoButton.Location = new Point(70, 108);
            _autoButton.Click += delegate { OnStart(false); };
            Controls.Add(_autoButton);

            _manualButton = new Button();
            _manualButton.Text = "手动";
            _manualButton.Size = new Size(120, 42);
            _manualButton.Location = new Point(210, 108);
            _manualButton.Click += delegate { OnStart(true); };
            Controls.Add(_manualButton);

            _status = new Label();
            _status.AutoSize = false;
            _status.Size = new Size(360, 20);
            _status.Location = new Point(20, 168);
            _status.TextAlign = ContentAlignment.MiddleCenter;
            _status.ForeColor = Color.DimGray;
            _status.Text = "保存到: " + Paths.OutputDir;
            Controls.Add(_status);

            ResumeLayout(false);
            PerformLayout();

            Resize += delegate { LayoutCentered(); };
            Load += delegate { LayoutCentered(); };
            LayoutCentered();

            if (_auto || _manualArg)
            {
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 900;
                timer.Tick += delegate
                {
                    timer.Stop();
                    OnStart(_manualArg);
                };
                timer.Start();
            }
        }

        private void LayoutCentered()
        {
            if (_title == null || _desc == null || _autoButton == null ||
                _manualButton == null || _status == null) return;

            int clientW = ClientSize.Width;
            int clientH = ClientSize.Height;

            Size titleSize = _title.Size;
            Size descSize = TextRenderer.MeasureText(_desc.Text, _desc.Font);
            Size statusSize = TextRenderer.MeasureText(_status.Text, _status.Font);
            int buttonsW = _autoButton.Width + 20 + _manualButton.Width;

            int blockW = Math.Max(Math.Max(titleSize.Width, descSize.Width),
                Math.Max(buttonsW, statusSize.Width));
            int maxW = clientW - 40;
            if (blockW > maxW) blockW = maxW;
            if (blockW < 200) blockW = 200;

            int gap1 = 10;
            int gap2 = 16;
            int gap3 = 16;
            int blockH = titleSize.Height + gap1 + descSize.Height + gap2 +
                _autoButton.Height + gap3 + statusSize.Height;

            int left = (clientW - blockW) / 2;
            int top = (clientH - blockH) / 2;
            if (left < 0) left = 0;
            if (top < 0) top = 0;

            _title.Left = left + (blockW - titleSize.Width) / 2;
            _title.Top = top;

            _desc.AutoSize = false;
            _desc.Size = new Size(blockW, descSize.Height);
            _desc.Location = new Point(left, top + titleSize.Height + gap1);
            _desc.TextAlign = ContentAlignment.MiddleCenter;

            int btnY = _desc.Bottom + gap2;
            int btnLeft = left + (blockW - buttonsW) / 2;
            _autoButton.Location = new Point(btnLeft, btnY);
            _manualButton.Location = new Point(btnLeft + _autoButton.Width + 20, btnY);

            _status.AutoSize = false;
            _status.Size = new Size(blockW, statusSize.Height);
            _status.Location = new Point(left, btnY + _autoButton.Height + gap3);
            _status.TextAlign = ContentAlignment.MiddleCenter;
        }

        private void OnStart(bool manual)
        {
            if (_busy) return;
            _busy = true;
            _autoButton.Enabled = false;
            _manualButton.Enabled = false;
            if (manual)
            {
                _status.Text = "手动记录中, 按 Esc 结束...";
                ShowTray();
            }
            else
            {
                _status.Text = "准备自动截图...";
            }
            Hide();
            bool m = manual;
            Thread worker = new Thread((ThreadStart)delegate { Work(m); });
            worker.IsBackground = true;
            worker.Name = "longshot-worker";
            worker.Start();
        }

        private void Work(bool manual)
        {
            string path = null;
            string error = null;
            try
            {
                Thread.Sleep(700);
                CaptureTarget target = TargetPicker.Pick();
                if (target == null) throw new Exception("没有找到可截取的前台窗口。");
                path = manual
                    ? Stitcher.RunManual(target, SetStatus)
                    : Stitcher.RunAuto(target, SetStatus);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            try { BeginInvoke(new Action(HideTray)); } catch { }

            if (_auto || _manualArg)
            {
                LogAuto(path != null ? ("OK " + path) : ("FAIL " + error));
                Environment.Exit(path != null ? 0 : 2);
                return;
            }

            if (error != null)
            {
                ShowErrorAndExit("截图失败: " + error);
                return;
            }

            // 完成后打开文件夹并选中图片, 然后退出进程
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch
            {
            }
            Application.Exit();
        }

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (!IsDisposed) _status.Text = text;
                }));
            }
            catch
            {
            }
        }

        private void ShowErrorAndExit(string text)
        {
            try
            {
                Invoke(new Action(delegate
                {
                    MessageBox.Show(text, "长截图工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                }));
            }
            catch
            {
                Environment.Exit(2);
            }
        }

        private void ShowTray()
        {
            if (_tray == null)
            {
                _tray = new NotifyIcon();
                _tray.Icon = SystemIcons.Application;
                _tray.Text = "LongShot 手动记录中 - 按 Esc 结束";
                _tray.Visible = true;
            }
        }

        private void HideTray()
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }

        private void LogAuto(string text)
        {
            if (!_auto && !_manualArg) return;
            try
            {
                File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "longshot_auto.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + text + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        internal static void Main(string[] args)
        {
            bool auto = false;
            bool manual = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--auto", StringComparison.OrdinalIgnoreCase)) auto = true;
                else if (string.Equals(args[i], "--manual", StringComparison.OrdinalIgnoreCase)) manual = true;
            }

            // 高 DPI: 屏幕 2560x1440 @125%, 不声明会拿到虚拟化的 2048x1152 模糊截图
            try { Native.SetProcessDPIAware(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(auto, manual));
        }
    }
}
