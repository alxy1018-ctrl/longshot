// 离线验证 Matcher.FindShift: 用合成滚动帧检查位移检测精度
// 编译: csc /target:exe /main:MatchTestProgram MatchTest.cs ..\LongShot.cs

using System;
using System.Drawing;
using System.Drawing.Imaging;
using LongShot;

internal static class MatchTestProgram
{
    private static int _failed = 0;

    internal static void Main()
    {
        int w = 1200;
        int contentH = 5000;
        int viewH = 900;
        using (Bitmap source = MakeSource(w, contentH))
        {
            int[] cases = new int[] { 60, 120, 300, 350, 500, 700 };
            foreach (int d in cases)
            {
                using (Bitmap prev = Crop(source, 0, viewH))
                using (Bitmap next = Crop(source, d, viewH))
                {
                    ShiftInfo info = Matcher.FindShift(prev, next);
                    bool ok = info.Valid && Math.Abs(info.Shift - d) <= 1;
                    Console.WriteLine(string.Format("expect {0,4}  got {1,4}  valid={2}  score={3:F2}  {4}",
                        d, info.Shift, info.Valid, info.Score, ok ? "OK" : "FAIL"));
                    if (!ok) _failed++;
                }
            }

            // 相同帧 -> 必须报 shift=0
            using (Bitmap a = Crop(source, 0, viewH))
            using (Bitmap b = Crop(source, 0, viewH))
            {
                ShiftInfo same = Matcher.FindShift(a, b);
                bool ok = same.Valid && same.Shift == 0;
                Console.WriteLine(string.Format("same frame       got {0,4}  valid={1}  {2}",
                    same.Shift, same.Valid, ok ? "OK" : "FAIL"));
                if (!ok) _failed++;
            }

            // 到底后重复帧(内容不再变化, 只差滚动条): 必须报 shift=0
            using (Bitmap a = Crop(source, 4100, viewH))
            using (Bitmap b = Crop(source, 4100, viewH))
            {
                ShiftInfo same = Matcher.FindShift(a, b);
                bool ok = same.Valid && same.Shift == 0;
                Console.WriteLine(string.Format("bottom same      got {0,4}  valid={1}  {2}",
                    same.Shift, same.Valid, ok ? "OK" : "FAIL"));
                if (!ok) _failed++;
            }

            // 无重叠的随机两帧 -> 应当无效或有大误差, 不能误报有效小位移
            using (Bitmap a = Crop(source, 0, viewH))
            using (Bitmap b = Crop(source, 2500, viewH))
            {
                ShiftInfo no = Matcher.FindShift(a, b);
                bool ok = !no.Valid;
                Console.WriteLine(string.Format("unrelated        got shift={0} valid={1} score={2:F2}  {3}",
                    no.Shift, no.Valid, no.Score, ok ? "OK" : "FAIL"));
                if (!ok) _failed++;
            }
        }

        Console.WriteLine(_failed == 0 ? "ALL PASS" : ("FAILED: " + _failed));
        Environment.Exit(_failed == 0 ? 0 : 1);
    }

    private static Bitmap Crop(Bitmap src, int y, int h)
    {
        Bitmap bmp = new Bitmap(src.Width, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.DrawImage(src, new Rectangle(0, 0, src.Width, h),
                new Rectangle(0, y, src.Width, h), GraphicsUnit.Pixel);
        }
        return bmp;
    }

    private static Bitmap MakeSource(int w, int h)
    {
        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            Random rnd = new Random(12345);
            for (int y = 0; y < h; y += 30)
            {
                Color c = Color.FromArgb(80 + rnd.Next(170), 80 + rnd.Next(170), 80 + rnd.Next(170));
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.FillRectangle(b, 0, y, w, 30);
                }
            }
            using (Font f = new Font("Arial", 20, FontStyle.Bold))
            {
                for (int y = 20; y < h; y += 90)
                {
                    g.DrawString("Line " + y, f, Brushes.Black, 20, y);
                }
            }
        }
        return bmp;
    }
}
