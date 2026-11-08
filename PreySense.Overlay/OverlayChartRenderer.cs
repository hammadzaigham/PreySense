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
            Pen axPen,
            bool drawCpu = true,
            bool drawGpu = true)
        {
            float peak = 10f;
            for (int i = 0; i < historyLength; i++)
            {
                if (drawCpu && float.IsFinite(cpuHistory[i]) && cpuHistory[i] > peak) peak = cpuHistory[i];
                if (drawGpu && float.IsFinite(gpuHistory[i]) && gpuHistory[i] > peak) peak = gpuHistory[i];
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

            void DrawSeries(float[] history, PointF[] points, SolidBrush brush, Pen pen)
            {
                using var outline = new GraphicsPath();
                using var area = new GraphicsPath();
                int start = -1;
                for (int i = 0; i <= historyLength; i++)
                {
                    bool valid = i < historyLength && float.IsFinite(history[Idx(i)]) && history[Idx(i)] >= 0;
                    if (valid)
                    {
                        if (start < 0) start = i;
                        continue;
                    }
                    if (start >= 0 && i - start >= 2)
                    {
                        outline.Reset(); area.Reset();
                        area.AddLine(basePts[start], points[start]);
                        for (int j = start + 1; j < i; j++)
                        {
                            outline.AddLine(points[j - 1], points[j]);
                            area.AddLine(points[j - 1], points[j]);
                        }
                        area.AddLine(points[i - 1], basePts[i - 1]);
                        area.CloseFigure();
                        g.FillPath(brush, area);
                        g.DrawPath(pen, outline);
                    }
                    start = -1;
                }
            }
            if (drawGpu) DrawSeries(gpuHistory, gpuPts, graphGpuBrush, graphGpuPen);
            if (drawCpu) DrawSeries(cpuHistory, cpuPts, graphCpuBrush, graphCpuPen);

            g.Restore(saved);
            g.DrawLine(axPen, x, y + h, x + w, y + h);
        }

    }
}
