using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PreySense.Overlay
{
    public static class OverlayChartRenderer
    {
        private static void FillArea(Graphics g, PointF[] polyPts, PointF[] topLine, PointF[] bottomLine, SolidBrush brush)
        {
            int n = topLine.Length;
            topLine.CopyTo(polyPts, 0);
            for (int i = 0; i < n; i++)
                polyPts[n + i] = bottomLine[n - 1 - i];
            g.FillPolygon(brush, polyPts);
        }

        public static void DrawStackedChart(
            Graphics g,
            int x, int y, int w, int h,
            float sc,
            float[] cpuHistory,
            float[] gpuHistory,
            int historyHead,
            int historyLength,
            PointF[] basePts,
            PointF[] gpuPts,
            PointF[] cpuPts,
            PointF[] polyPts,
            SolidBrush graphGpuBrush,
            Pen graphGpuPen,
            SolidBrush graphCpuBrush,
            Pen graphCpuPen,
            Pen totalPen,
            Pen axPen)
        {
            float peak = 10f;
            for (int i = 0; i < historyLength; i++)
            {
                if (cpuHistory[i] > peak) peak = cpuHistory[i];
                if (gpuHistory[i] > peak) peak = gpuHistory[i];
            }

            float stepX = (float)w / (historyLength - 1);
            int Idx(int i) => (historyHead + i) % historyLength;

            for (int i = 0; i < historyLength; i++)
            {
                int idx = Idx(i);
                float px = x + i * stepX;
                float cpuH = (cpuHistory[idx] / peak) * h;
                float gpuH = (gpuHistory[idx] / peak) * h;

                basePts[i] = new PointF(px, y + h);
                gpuPts[i]  = new PointF(px, y + h - gpuH);
                cpuPts[i]  = new PointF(px, y + h - cpuH);
            }

            var saved = g.Save();
            g.SetClip(new RectangleF(x, y, w, h));

            FillArea(g, polyPts, gpuPts, basePts, graphGpuBrush);
            g.DrawLines(graphGpuPen, gpuPts);
            FillArea(g, polyPts, cpuPts, basePts, graphCpuBrush);
            g.DrawLines(graphCpuPen, cpuPts);

            g.Restore(saved);
            g.DrawLine(axPen, x, y + h, x + w, y + h);
        }

        public static void DrawFrametimeChart(
            Graphics g,
            int x, int y, int w, int h,
            float sc,
            float[] localFrametimes,
            int historyLength,
            EtwFpsMonitor? fps,
            Pen axPen)
        {
            Array.Clear(localFrametimes, 0, localFrametimes.Length);

            int count = 0;
            if (fps != null)
            {
                count = fps.GetRecentFrameTimes(localFrametimes);
            }

            if (count < 2)
            {
                using var emptyPen = new Pen(Color.FromArgb(100, 100, 100), sc * 0.5f);
                g.DrawLine(emptyPen, x, y + h / 2, x + w, y + h / 2);
                return;
            }

            float maxFt = 16.6f;
            for (int i = 0; i < count; i++)
            {
                if (localFrametimes[i] > maxFt)
                    maxFt = localFrametimes[i];
            }

            if (maxFt > 100f) maxFt = 100f;

            float stepX = (float)w / (historyLength - 1);
            int pointsCount = Math.Min(count, historyLength);
            var pts = new PointF[pointsCount];
            var basePts = new PointF[pointsCount];

            for (int i = 0; i < pointsCount; i++)
            {
                float px = x + w - i * stepX;
                float ftVal = Math.Clamp(localFrametimes[i], 0f, maxFt);
                float py = y + h - (ftVal / maxFt) * h;
                pts[pointsCount - 1 - i] = new PointF(px, py);
                basePts[pointsCount - 1 - i] = new PointF(px, y + h);
            }

            Color ftColor = Color.FromArgb(255, 191, 0); // Amber
            using var ftPen = new Pen(ftColor, 1.5f);
            using var ftBrush = new SolidBrush(Color.FromArgb(40, ftColor.R, ftColor.G, ftColor.B));

            var saved = g.Save();
            g.SetClip(new RectangleF(x, y, w, h));

            var poly = new PointF[pointsCount * 2];
            pts.CopyTo(poly, 0);
            for (int i = 0; i < pointsCount; i++)
            {
                poly[pointsCount + i] = basePts[pointsCount - 1 - i];
            }
            g.FillPolygon(ftBrush, poly);
            g.DrawLines(ftPen, pts);

            g.Restore(saved);
            g.DrawLine(axPen, x, y + h, x + w, y + h);
        }
    }
}
