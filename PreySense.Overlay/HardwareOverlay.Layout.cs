using System.Drawing;

namespace PreySense.Overlay;

public partial class HardwareOverlay
{
    private float GetScale() => BaseScale * (_scalePercent / 100f);

    public Size CalculateOverlaySize(int scalePercent)
    {
        float sc = BaseScale * (scalePercent / 100f);
        int padX = S(sc, BasePadX);
        int padY = S(sc, BasePadY);
        int lineH = S(sc, BaseLineHeight);
        int lineGap = S(sc, BaseLineSpacing);
        int fpsColW = S(sc, BaseFpsColWidth);
        int innerPadX = S(sc, BaseInnerPadX);
        int innerH = lineH * 2 + lineGap;
        int totalH = padY * 2 + innerH;
        int labelColW = S(sc, 18);
        int colGap = 0;

        int cursor = padX + innerPadX;
        if (ShouldShowFps()) cursor += fpsColW + colGap;
        cursor += labelColW + colGap;

        for (int i = 1; i <= 8; i++)
        {
            int colW = ColumnWidth(i - 1, sc);
            if (colW <= 0) continue;
            cursor += colW + colGap;
            if (_columnGraphs[i - 1]) cursor += S(sc, 6) + S(sc, 72) + colGap;
        }

        return new Size(cursor + padX + innerPadX, totalH);
    }

    private static int S(float sc, int v) => (int)(v * sc);
    private static double D(object? v) { try { return v is null ? 0.0 : Convert.ToDouble(v); } catch { return 0.0; } }
    private static string FmtTemp(double t, bool zeroWhenUnavailable = false) => ((int)Math.Max(0, Math.Round(t)) + "°C").PadLeft(5);
    private static string FmtPow(double p) => Math.Max(0, Math.Round(p, 1)).ToString("F1") + "W";

    private static string FormatFan(int? fan)
    {
        if (fan is null || fan < 0) return "0";
        return fan.Value.ToString();
    }
}
