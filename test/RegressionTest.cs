using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LongShot;

internal static class RegressionTestProgram
{
    private static int _failed;
    internal static void Main()
    {
        Run("variable shifts preserve every pixel", TestContinuity);
        Run("fixed header and footer appear once", TestFixedBars);
        Run("non-aligned fixed bars preserve every pixel", delegate { FixedBars(63, 31); });
        Run("height limit keeps continuous prefix", TestHeightLimit);
        Run("manual fast scroll can recover without gaps", TestManualRecovery);
        Run("manual reverse scroll does not duplicate content", TestReverse);
        Run("manual unmatched finish reports partial output", TestUnmatchedFinish);
        Run("automatic overscroll rolls back and reaches bottom", TestAutoRecovery);
        Run("automatic no-scroll saves one screen", TestNoScroll);
        Run("automatic unrecoverable match reports partial output", TestAutoUnmatched);
        Run("target change stops before scrolling", TestTargetChange);
        Run("Escape saves existing content", TestEscape);
        Run("capture failure saves existing content", TestCaptureFailure);
        Run("PNG encoding preserves pixels and alpha", TestPng);
        Console.WriteLine(_failed == 0 ? "ALL REGRESSIONS PASS" : "FAILED: " + _failed);
        Environment.ExitCode = _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static Bitmap Source(int height)
    {
        Bitmap image = new Bitmap(384, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(image))
        {
            Random random = new Random(1729);
            for (int y = 0; y < height; y += 23)
            {
                using (Brush b = new SolidBrush(Color.FromArgb(40 + random.Next(200), 40 + random.Next(200), 40 + random.Next(200))))
                    g.FillRectangle(b, 0, y, image.Width, 23);
                g.FillRectangle(Brushes.Black, (y * 7) % 300, y, 31, 6);
            }
        }
        return image;
    }

    private static Bitmap View(Bitmap source, int offset, int height, int header, int footer)
    {
        Bitmap image = new Bitmap(source.Width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(image))
        {
            g.DrawImage(source, new Rectangle(0, header, source.Width, height - header - footer),
                new Rectangle(0, offset, source.Width, height - header - footer), GraphicsUnit.Pixel);
            if (header > 0) g.FillRectangle(Brushes.DarkBlue, 0, 0, source.Width, header);
            if (footer > 0) g.FillRectangle(Brushes.OrangeRed, 0, height - footer, source.Width, footer);
        }
        return image;
    }

    private static byte[] Pixels(Bitmap image)
    {
        BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = new byte[image.Width * image.Height * 4];
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * image.Width * 4, image.Width * 4);
            return pixels;
        }
        finally { image.UnlockBits(data); }
    }

    private static void Equal(Bitmap expected, Bitmap actual)
    {
        Check(expected.Size == actual.Size, "size expected " + expected.Size + " got " + actual.Size);
        byte[] a = Pixels(expected), b = Pixels(actual);
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) throw new Exception("pixel differs at x=" + (i / 4 % expected.Width) +
                " y=" + (i / 4 / expected.Width) + " channel=" + (i % 4));
    }

    private static void TestContinuity()
    {
        using (Bitmap source = Source(2200))
        using (Bitmap first = View(source, 0, 640, 0, 0))
        using (FrameStitcher frames = new FrameStitcher(first, 3000))
        {
            foreach (int offset in new int[] { 61, 184, 371, 571, 774, 1000, 1207 })
                using (Bitmap next = View(source, offset, 640, 0, 0))
                    Check(frames.Add(next) == FrameState.Appended, "offset not appended: " + offset);
            using (Bitmap expected = View(source, 0, 1847, 0, 0))
            using (Bitmap actual = frames.Finish()) Equal(expected, actual);
        }
    }

    private static void TestFixedBars() { FixedBars(64, 32); }

    private static void FixedBars(int header, int footer)
    {
        using (Bitmap source = Source(1800))
        using (Bitmap first = View(source, 0, 640, header, footer))
        using (FrameStitcher frames = new FrameStitcher(first, 2500))
        {
            foreach (int offset in new int[] { 120, 280, 480, 720 })
                using (Bitmap next = View(source, offset, 640, header, footer))
                    Check(frames.Add(next) == FrameState.Appended, "fixed bars offset: " + offset);
            using (Bitmap expected = View(source, 0, 1360, header, footer))
            using (Bitmap actual = frames.Finish()) Equal(expected, actual);
        }
    }

    private static void TestHeightLimit()
    {
        using (Bitmap source = Source(1800))
        using (Bitmap first = View(source, 0, 640, 0, 0))
        using (FrameStitcher frames = new FrameStitcher(first, 850))
        using (Bitmap next = View(source, 300, 640, 0, 0))
        {
            Check(frames.Add(next) == FrameState.Full, "limit not reached");
            using (Bitmap expected = View(source, 0, 850, 0, 0))
            using (Bitmap actual = frames.Finish()) Equal(expected, actual);
        }
    }

    private sealed class Feed : ICaptureFeed, IDisposable
    {
        public Bitmap SourceImage;
        public int[] Offsets;
        public int Captures;
        public int Position;
        public int ScrollPixels = 250;
        public bool InvalidAfterFirst;
        public bool StopImmediately;
        public bool UnrelatedAfterFirst;
        public bool FailAfterFirst;
        public List<int> Scrolls = new List<int>();
        public bool IsValid { get { return !InvalidAfterFirst || Captures == 0; } }
        public bool EscapePressed { get { return StopImmediately || (Offsets != null && Captures >= Offsets.Length); } }
        public Bitmap Grab()
        {
            if (FailAfterFirst && Captures > 0) throw new InvalidOperationException("test capture failure");
            int offset = Offsets == null ? Position : Offsets[Math.Min(Captures, Offsets.Length - 1)];
            Captures++;
            if (UnrelatedAfterFirst && Captures > 1)
            {
                Bitmap unrelated = new Bitmap(384, 640, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(unrelated)) g.Clear(Color.Magenta);
                return unrelated;
            }
            return View(SourceImage, offset, 640, 0, 0);
        }
        public void Scroll(int notches)
        {
            Scrolls.Add(notches);
            Position = Math.Max(0, Math.Min(SourceImage.Height - 640, Position - notches * ScrollPixels));
        }
        public void Wait(int milliseconds) { }
        public void Dispose() { SourceImage.Dispose(); }
    }

    private static void Manual(int[] offsets, bool partial)
    {
        using (Feed feed = new Feed { SourceImage = Source(2200), Offsets = offsets })
        using (RecordingResult result = Stitcher.RecordManual(feed, delegate { }))
        using (Bitmap expected = View(feed.SourceImage, 0, 640 + (partial ? 120 : offsets[offsets.Length - 1]), 0, 0))
        {
            Equal(expected, result.Image);
            Check((result.Notice != null) == partial, "unexpected manual notice: " + result.Notice);
            Check(feed.Scrolls.Count == 0, "manual mode injected scroll");
        }
    }

    private static void TestManualRecovery() { Manual(new int[] { 0, 120, 950, 360, 600 }, false); }
    private static void TestReverse() { Manual(new int[] { 0, 160, 80, 200 }, false); }
    private static void TestUnmatchedFinish() { Manual(new int[] { 0, 120, 950 }, true); }

    private static void TestAutoRecovery()
    {
        using (Feed feed = new Feed { SourceImage = Source(2200) })
        using (RecordingResult result = Stitcher.RecordAuto(feed, delegate { }))
        {
            Equal(feed.SourceImage, result.Image);
            Check(feed.Scrolls.Count > 1 && feed.Scrolls[0] == -3 && feed.Scrolls[1] == 1, "did not rewind overscroll");
            Check(result.Notice == null, "unexpected automatic notice: " + result.Notice);
        }
    }

    private static void TestNoScroll()
    {
        using (Feed feed = new Feed { SourceImage = Source(640) })
        using (RecordingResult result = Stitcher.RecordAuto(feed, delegate { }))
        {
            Equal(feed.SourceImage, result.Image);
            Check(feed.Scrolls.Count == 5 && result.Notice == null, "bottom detection failed");
        }
    }

    private static void TestAutoUnmatched()
    {
        using (Feed feed = new Feed { SourceImage = Source(2200), UnrelatedAfterFirst = true })
        using (RecordingResult result = Stitcher.RecordAuto(feed, delegate { }))
        {
            Check(result.Image.Height == 640 && result.Notice != null, "unrecoverable output not marked partial");
            for (int i = 1; i < feed.Scrolls.Count; i++) Check(feed.Scrolls[i] > 0, "continued downward after mismatch");
        }
    }

    private static void TestTargetChange()
    {
        using (Feed feed = new Feed { SourceImage = Source(640), InvalidAfterFirst = true })
        using (RecordingResult result = Stitcher.RecordAuto(feed, delegate { }))
        {
            Equal(feed.SourceImage, result.Image);
            Check(feed.Scrolls.Count == 0 && result.Notice != null, "target change ignored");
        }
    }

    private static void TestEscape()
    {
        using (Feed feed = new Feed { SourceImage = Source(640), StopImmediately = true })
        using (RecordingResult result = Stitcher.RecordAuto(feed, delegate { }))
        {
            Equal(feed.SourceImage, result.Image);
            Check(feed.Scrolls.Count == 0 && result.Notice != null, "automatic Escape ignored");
        }
        Manual(new int[] { 0 }, false);
    }

    private static void TestCaptureFailure()
    {
        using (Feed feed = new Feed { SourceImage = Source(640), FailAfterFirst = true })
        using (RecordingResult result = Stitcher.RecordAuto(feed, delegate { }))
        {
            Equal(feed.SourceImage, result.Image);
            Check(result.Notice != null, "automatic failure not reported");
        }
        using (Feed feed = new Feed { SourceImage = Source(640), FailAfterFirst = true })
        using (RecordingResult result = Stitcher.RecordManual(feed, delegate { }))
        {
            Equal(feed.SourceImage, result.Image);
            Check(result.Notice != null, "manual failure not reported");
        }
    }

    private static void TestPng()
    {
        using (Feed feed = new Feed { SourceImage = Source(2200), Offsets = new int[] { 0, 120, 360, 600 } })
        using (RecordingResult result = Stitcher.RecordManual(feed, delegate { }))
        using (System.IO.MemoryStream encoded = new System.IO.MemoryStream())
        {
            result.Image.Save(encoded, ImageFormat.Png);
            encoded.Position = 0;
            using (Bitmap decoded = new Bitmap(encoded))
            using (Bitmap expected = View(feed.SourceImage, 0, 1240, 0, 0)) Equal(expected, decoded);
        }
    }
}
