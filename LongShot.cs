// LongShot - 滚动长截图工具
// 编译: build.bat (csc /codepage:65001)
// 用法: 双击 LongShot.exe -> 选自动/手动 -> 记录当前前台窗口并拼接长图
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

        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
                }
                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }
    }

    internal struct ShiftInfo
    {
        public bool Valid;
        public int Shift;
        public double Score;
        public int BottomInset;
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

            // 固定工具栏不随内容移动，排除连续不变的顶部/底部行。
            int top = StationaryEdge(pg, ng, sw, sh, useW, false);
            int bottom = StationaryEdge(pg, ng, sw, sh, useW, true);

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
                double s = MaeRegion(ng, pg, sw, d, useW, top, rows - bottom);
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
            int topFull = StationaryEdge(pgFull, ngFull, w, h, useWFull, false);
            int bottomFull = StationaryEdge(pgFull, ngFull, w, h, useWFull, true);

            int coarse = bestD * Downsample;
            int lo = coarse - 8;
            int hi = coarse + 8;
            if (lo < 2) lo = 2;
            if (hi > h - 16) hi = h - 16;

            int fineBest = coarse;
            double fineScore = double.MaxValue;
            for (int d = lo; d <= hi; d++)
            {
                double s = MaeFull(ngFull, pgFull, w, h, d, useWFull,
                    topFull, bottomFull);
                if (s < fineScore)
                {
                    fineScore = s;
                    fineBest = d;
                }
            }

            if (fineScore > 12.0 || fineScore >= same * 0.8) return none;

            ShiftInfo result = new ShiftInfo();
            result.Valid = true;
            result.Shift = fineBest;
            result.Score = fineScore;
            result.BottomInset = bottomFull;
            return result;
        }

        private static int StationaryEdge(byte[] prev, byte[] next, int w, int h, int useW, bool fromBottom)
        {
            int count = 0;
            for (int i = 0; i < h / 3; i++)
            {
                int y = fromBottom ? h - 1 - i : i;
                long sum = 0;
                for (int x = 0; x < useW; x++)
                    sum += Math.Abs(prev[y * w + x] - next[y * w + x]);
                if ((double)sum / useW > 0.8) break;
                count++;
            }
            return count;
        }

        private static double MaeRegion(byte[] next, byte[] prev, int w, int d, int useW, int start, int end)
        {
            if (end - start < 4) return double.MaxValue;
            long sum = 0;
            for (int y = start; y < end; y++)
                for (int x = 0; x < useW; x++)
                    sum += Math.Abs(next[y * w + x] - prev[(y + d) * w + x]);
            return (double)sum / ((end - start) * useW);
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

        private static double MaeFull(byte[] nextGray, byte[] prevGray, int w, int h, int d, int useW,
            int top, int bottom)
        {
            long sum = 0;
            long count = 0;
            int rows = h - d - bottom;
            for (int y = top; y < rows; y += 2)
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

    // 每帧由调用者释放；仅保留最后一次成功匹配的参考帧。
    internal sealed class FrameStitcher : IDisposable
    {
        private Bitmap _canvas;
        private Bitmap _previous;
        public int Height { get; private set; }
        public int ViewHeight { get { return _previous.Height; } }
        public int Capacity { get; private set; }

        public FrameStitcher(Bitmap first, int capacity)
        {
            if (first.Height > capacity) throw new ArgumentException("首帧超过图片高度上限。");
            Capacity = capacity;
            _previous = (Bitmap)first.Clone();
            try
            {
                _canvas = new Bitmap(first.Width, capacity, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(_canvas)) g.DrawImageUnscaled(first, 0, 0);
                Height = first.Height;
            }
            catch { Dispose(); throw; }
        }

        public FrameState Add(Bitmap frame)
        {
            ShiftInfo info = Matcher.FindShift(_previous, frame);
            if (!info.Valid) return FrameState.Unmatched;
            if (info.Shift == 0) return FrameState.Unchanged;
            if (Height == Capacity) return FrameState.Full;
            int append = Math.Min(info.Shift, Capacity - Height);
            int footer = info.BottomInset;
            // 覆盖旧固定页脚，在已有内容末尾追加新内容，最后放回新页脚。
            using (Graphics g = Graphics.FromImage(_canvas))
            {
                g.DrawImage(frame, new Rectangle(0, Height - footer, frame.Width, append),
                    new Rectangle(0, frame.Height - footer - info.Shift, frame.Width, append), GraphicsUnit.Pixel);
                if (footer > 0)
                    g.DrawImage(frame, new Rectangle(0, Height + append - footer, frame.Width, footer),
                        new Rectangle(0, frame.Height - footer, frame.Width, footer), GraphicsUnit.Pixel);
            }
            Height += append;
            Bitmap reference = (Bitmap)frame.Clone();
            _previous.Dispose();
            _previous = reference;
            return Height == Capacity ? FrameState.Full : FrameState.Appended;
        }

        public Bitmap Finish()
        {
            return _canvas.Clone(new Rectangle(0, 0, _canvas.Width, Height), PixelFormat.Format32bppArgb);
        }

        public void Dispose()
        {
            if (_previous != null) _previous.Dispose();
            if (_canvas != null) _canvas.Dispose();
        }
    }

    internal enum FrameState { Unchanged, Unmatched, Appended, Full }

    // 将桌面操作与记录循环分开，离线测试直接使用确定的帧序列。
    internal interface ICaptureFeed
    {
        bool IsValid { get; }
        bool EscapePressed { get; }
        Bitmap Grab();
        void Scroll(int notches);
        void Wait(int milliseconds);
    }

    internal sealed class DesktopCaptureFeed : ICaptureFeed
    {
        private readonly CaptureTarget _target;
        private readonly Func<bool> _cancelled;
        public DesktopCaptureFeed(CaptureTarget target, Func<bool> cancelled)
        {
            _target = target;
            _cancelled = cancelled;
            if (target.IsWindow)
            {
                Native.SetForegroundWindow(target.Hwnd);
                Thread.Sleep(250);
            }
        }

        public bool EscapePressed
        {
            get { return _cancelled() || (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0; }
        }
        public bool IsValid
        {
            get
            {
                if (!_target.IsWindow) return true;
                if (!Native.IsWindowVisible(_target.Hwnd) || Native.IsIconic(_target.Hwnd) ||
                    Native.GetForegroundWindow() != _target.Hwnd) return false;
                Native.RECT rc;
                Native.POINT pt = new Native.POINT();
                if (!Native.GetClientRect(_target.Hwnd, out rc) || !Native.ClientToScreen(_target.Hwnd, ref pt)) return false;
                Rectangle current = new Rectangle(pt.X, pt.Y, rc.Right - rc.Left, rc.Bottom - rc.Top);
                // 不追随移动或部分移出屏幕的窗口，避免混入其他屏幕内容。
                return current == _target.ClientRect;
            }
        }

        public Bitmap Grab()
        {
            if (!IsValid) throw new InvalidOperationException("目标窗口已移动、缩放、最小化或失去焦点。");
            return ScreenCapture.Grab(_target.ClientRect);
        }

        public void Scroll(int notches)
        {
            if (!IsValid) throw new InvalidOperationException("目标窗口已改变，已停止滚动。");
            Rectangle r = _target.ClientRect;
            Cursor.Position = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            Wait(60);
            int direction = notches < 0 ? -120 : 120;
            for (int i = 0; i < Math.Abs(notches); i++)
            {
                if (EscapePressed) break;
                if (!IsValid) throw new InvalidOperationException("目标窗口已改变，已停止滚动。");
                if (Native.ScrollWheel(direction) != 1)
                    throw new InvalidOperationException("无法发送滚轮输入，请检查目标窗口权限。");
                Wait(120);
            }
        }

        public void Wait(int milliseconds) { Thread.Sleep(milliseconds); }
    }

    internal sealed class RecordingResult : IDisposable
    {
        public Bitmap Image;
        public string Notice;
        public void Dispose() { if (Image != null) Image.Dispose(); }
    }

    internal static class Stitcher
    {
        internal const int MaxTotalHeight = 18000;

        public static string RunAuto(CaptureTarget target, Action<string> progress, Action<string> warning, Func<bool> cancelled)
        {
            using (RecordingResult result = RecordAuto(new DesktopCaptureFeed(target, cancelled), progress))
            {
                if (result.Notice != null) warning(result.Notice);
                return SaveResult(result.Image, progress);
            }
        }

        public static string RunManual(CaptureTarget target, Action<string> progress, Action<string> warning, Func<bool> cancelled)
        {
            using (RecordingResult result = RecordManual(new DesktopCaptureFeed(target, cancelled), progress))
            {
                if (result.Notice != null) warning(result.Notice);
                return SaveResult(result.Image, progress);
            }
        }

        internal static RecordingResult RecordAuto(ICaptureFeed feed, Action<string> progress)
        {
            using (Bitmap first = feed.Grab())
            using (FrameStitcher frames = new FrameStitcher(first, MaxTotalHeight))
            {
                int unchanged = 0;
                int wheel = 3;
                int recovery = 0;
                bool recovering = false;
                string notice = null;
                int step = 0;
                try
                {
                    for (step = 0; step < 300; step++)
                    {
                        if (feed.EscapePressed) { notice = "已中止自动截图，保存已截部分。"; break; }
                        if (!feed.IsValid) { notice = "目标窗口已改变，保存已截部分。"; break; }
                        // 匹配失败后仅回滚一格找回重叠，不继续向下滚而扩大缺口。
                        feed.Scroll(recovering ? 1 : -wheel);
                        feed.Wait(600);
                        if (!feed.IsValid) { notice = "目标窗口已改变，保存已截部分。"; break; }
                        int before = frames.Height;
                        FrameState state;
                        using (Bitmap next = feed.Grab()) state = frames.Add(next);
                        if (state == FrameState.Unmatched || state == FrameState.Unchanged)
                        {
                            feed.Wait(350);
                            if (!feed.IsValid) { notice = "目标窗口已改变，保存已截部分。"; break; }
                            using (Bitmap next = feed.Grab()) state = frames.Add(next);
                        }
                        if (state == FrameState.Full) { notice = "已达到 18000 像素高度上限，保存已截部分。"; break; }
                        if (state == FrameState.Unmatched || (recovering && state == FrameState.Unchanged))
                        {
                            recovering = true;
                            unchanged = 0;
                            wheel = 1;
                            if (++recovery >= 8)
                            {
                                notice = "无法恢复画面重叠，保存已截部分。可使用手动模式慢慢滚动重试。";
                                break;
                            }
                            progress("匹配失败，正在回滚寻找重叠...");
                            continue;
                        }
                        if (state == FrameState.Unchanged)
                        {
                            if (++unchanged >= 5) break;
                            continue;
                        }
                        recovering = false;
                        recovery = 0;
                        unchanged = 0;
                        double ratio = (double)(frames.Height - before) / frames.ViewHeight;
                        if (ratio < 0.10 && wheel < 8) wheel++;
                        else if (ratio > 0.60 && wheel > 1) wheel--;
                        progress("正在截图: 已拼接 " + frames.Height + " 像素...");
                    }
                }
                catch (InvalidOperationException ex) { notice = "截图中断，保存已截部分。" + ex.Message; }
                catch (System.ComponentModel.Win32Exception ex) { notice = "屏幕读取失败，保存已截部分。" + ex.Message; }
                catch (ExternalException ex) { notice = "屏幕读取失败，保存已截部分。" + ex.Message; }
                if (step == 300) notice = "已达到自动滚动步数上限，保存已截部分。";
                return new RecordingResult { Image = frames.Finish(), Notice = notice };
            }
        }

        internal static RecordingResult RecordManual(ICaptureFeed feed, Action<string> progress)
        {
            using (Bitmap first = feed.Grab())
            using (FrameStitcher frames = new FrameStitcher(first, MaxTotalHeight))
            {
                string notice = null;
                bool unmatched = false;
                int frame = 0;
                try
                {
                    for (frame = 0; frame < 6000; frame++)
                    {
                        if (feed.EscapePressed) break;
                        if (!feed.IsValid) { notice = "目标窗口已改变，保存已截部分。"; break; }
                        feed.Wait(220);
                        if (feed.EscapePressed) break;
                        if (!feed.IsValid) { notice = "目标窗口已改变，保存已截部分。"; break; }
                        FrameState state;
                        using (Bitmap next = feed.Grab()) state = frames.Add(next);
                        if (state == FrameState.Full) { notice = "已达到 18000 像素高度上限，保存已截部分。"; break; }
                        if (state == FrameState.Unmatched)
                        {
                            unmatched = true;
                            progress("画面无法衔接，请缓慢往回滚到已记录位置，再继续向下。");
                            continue;
                        }
                        unmatched = false;
                        progress("手动记录中: 已记录 " + frames.Height + " 像素，按 Esc 完成。");
                    }
                }
                catch (InvalidOperationException ex) { notice = "截图中断，保存已截部分。" + ex.Message; }
                catch (System.ComponentModel.Win32Exception ex) { notice = "屏幕读取失败，保存已截部分。" + ex.Message; }
                catch (ExternalException ex) { notice = "屏幕读取失败，保存已截部分。" + ex.Message; }
                if (frame == 6000) notice = "已达到手动记录帧数上限，保存已截部分。";
                else if (unmatched && notice == null) notice = "结束时仍有未衔接画面，仅保存连续记录的部分。";
                return new RecordingResult { Image = frames.Finish(), Notice = notice };
            }
        }

        private static string SaveResult(Bitmap result, Action<string> progress)
        {
            string dir = Paths.OutputDir;
            Directory.CreateDirectory(dir);
            string path = Paths.UniqueFile(dir, "LongShot");
            progress("正在保存图片...");
            using (MemoryStream encoded = new MemoryStream())
            {
                result.Save(encoded, ImageFormat.Png);
                FileStream output;
                while (true)
                {
                    try
                    {
                        output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                        break;
                    }
                    catch (IOException)
                    {
                        if (!File.Exists(path)) throw;
                        path = Paths.UniqueFile(dir, "LongShot");
                    }
                }
                using (output) { encoded.Position = 0; encoded.CopyTo(output); }
                return path;
            }
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
        private bool _matchWarningShown;
        private volatile bool _escapeRequested;
        private bool _hotKeyRegistered;
        private const int EscapeHotKeyId = 0x4C53;

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
            _escapeRequested = false;
            // WM_HOTKEY 会保留短按事件，避免每 220ms 轮询漏掉 Esc。
            _hotKeyRegistered = Native.RegisterHotKey(Handle, EscapeHotKeyId, 0x4000, Native.VK_ESCAPE);
            if (!_hotKeyRegistered)
            {
                _busy = false;
                MessageBox.Show(this, "Esc 快捷键被其他程序占用，无法开始记录。请关闭占用程序后重试。",
                    "长截图工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
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
            string notice = null;
            try
            {
                Thread.Sleep(700);
                CaptureTarget target = TargetPicker.Pick();
                if (target == null) throw new Exception("没有找到可截取的前台窗口。");
                path = manual
                    ? Stitcher.RunManual(target, SetStatus, delegate(string text) { notice = text; }, delegate { return _escapeRequested; })
                    : Stitcher.RunAuto(target, SetStatus, delegate(string text) { notice = text; }, delegate { return _escapeRequested; });
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            try { Invoke(new Action(delegate { ReleaseEscapeHotKey(); HideTray(); })); } catch { }

            if (_auto || _manualArg)
            {
                if (notice != null) LogAuto("NOTICE " + notice);
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
            if (notice != null)
            {
                try
                {
                    Invoke(new Action(delegate
                    {
                        MessageBox.Show(notice + "\r\n保存位置: " + path, "长截图工具",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            }
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
                    if (_tray != null)
                    {
                        _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
                        bool unmatched = text.StartsWith("画面无法衔接", StringComparison.Ordinal);
                        if (unmatched && !_matchWarningShown)
                            _tray.ShowBalloonTip(5000, "请慢慢往回滚", text, ToolTipIcon.Warning);
                        _matchWarningShown = unmatched;
                    }
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

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == EscapeHotKeyId)
                _escapeRequested = true;
            base.WndProc(ref m);
        }

        private void ReleaseEscapeHotKey()
        {
            if (_hotKeyRegistered)
            {
                Native.UnregisterHotKey(Handle, EscapeHotKeyId);
                _hotKeyRegistered = false;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ReleaseEscapeHotKey();
            HideTray();
            base.OnFormClosed(e);
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
