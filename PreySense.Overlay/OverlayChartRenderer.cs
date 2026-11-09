using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PreySense.Overlay
{
    public static class OverlayChartRenderer
    {
        public static void DrawHistoryChart(
            Graphics graphics,
            int x, int y, int width, int height,
            float[] cpuHistory,
            float[] gpuHistory,
            int historyHead,
            int historyLength,
            PointF[] baselinePoints,
            PointF[] gpuPoints,
            PointF[] cpuPoints,
            SolidBrush graphGpuBrush,
            Pen graphGpuPen,
            SolidBrush graphCpuBrush,
            Pen graphCpuPen,
            Pen axisPen,
            bool drawCpu = true,
            bool drawGpu = true)
        {
            float peak = 10f;
            for (int i = 0; i < historyLength; i++)
            {
                if (drawCpu && float.IsFinite(cpuHistory[i]) && cpuHistory[i] > peak) peak = cpuHistory[i];
                if (drawGpu && float.IsFinite(gpuHistory[i]) && gpuHistory[i] > peak) peak = gpuHistory[i];
            }

            float horizontalStep = (float)width / (historyLength - 1);
            int HistoryIndex(int i) => (historyHead + i) % historyLength;

            for (int i = 0; i < historyLength; i++)
            {
                int historyIndex = HistoryIndex(i);
                float pointX = x + i * horizontalStep;
                float cpuHeight = (cpuHistory[historyIndex] / peak) * height;
                float gpuHeight = (gpuHistory[historyIndex] / peak) * height;

                baselinePoints[i] = new PointF(pointX, y + height);
                gpuPoints[i]  = new PointF(pointX, y + height - gpuHeight);
                cpuPoints[i]  = new PointF(pointX, y + height - cpuHeight);
            }

            var graphicsState = graphics.Save();
            graphics.SetClip(new RectangleF(x, y, width, height), CombineMode.Intersect);

            void DrawSeries(float[] history, PointF[] points, SolidBrush brush, Pen pen)
            {
                using var outline = new GraphicsPath();
                using var area = new GraphicsPath();
                int segmentStart = -1;
                for (int i = 0; i <= historyLength; i++)
                {
                    bool isValidSample = i < historyLength && float.IsFinite(history[HistoryIndex(i)]) && history[HistoryIndex(i)] >= 0;
                    if (isValidSample)
                    {
                        if (segmentStart < 0) segmentStart = i;
                        continue;
                    }
                    if (segmentStart >= 0 && i - segmentStart >= 2)
                    {
                        outline.Reset(); area.Reset();
                        area.AddLine(baselinePoints[segmentStart], points[segmentStart]);
                        for (int j = segmentStart + 1; j < i; j++)
                        {
                            outline.AddLine(points[j - 1], points[j]);
                            area.AddLine(points[j - 1], points[j]);
                        }
                        area.AddLine(points[i - 1], baselinePoints[i - 1]);
                        area.CloseFigure();
                        graphics.FillPath(brush, area);
                        graphics.DrawPath(pen, outline);
                    }
                    segmentStart = -1;
                }
            }
            if (drawGpu) DrawSeries(gpuHistory, gpuPoints, graphGpuBrush, graphGpuPen);
            if (drawCpu) DrawSeries(cpuHistory, cpuPoints, graphCpuBrush, graphCpuPen);

            graphics.Restore(graphicsState);
            graphics.DrawLine(axisPen, x, y + height, x + width, y + height);
        }

    }
}
