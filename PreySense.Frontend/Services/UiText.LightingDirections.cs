namespace PreySense.Frontend.Services;

internal static partial class UiText
{
    private static void AddLightingDirections()
    {
        Direction(Chinese, "方向", "上", "下", "水平", "垂直");
        Direction(Hindi, "दिशा", "ऊपर", "नीचे", "क्षैतिज", "लंबवत");
        Direction(Spanish, "Dirección", "Arriba", "Abajo", "Horizontal", "Vertical");
        Direction(French, "Direction", "Haut", "Bas", "Horizontal", "Vertical");
        Direction(Turkish, "Yön", "Yukarı", "Aşağı", "Yatay", "Dikey");
        Chinese["Sync All LEDs"] = "同步所有 LED";
        Hindi["Sync All LEDs"] = "सभी LED सिंक करें";
        Spanish["Sync All LEDs"] = "Sincronizar todos los LED";
        French["Sync All LEDs"] = "Synchroniser toutes les LED";
        Turkish["Sync All LEDs"] = "Tüm LED'leri eşitle";
    }

    private static void Direction(Dictionary<string, string> table, string direction, string up, string down,
        string horizontal, string vertical)
    {
        table["Direction"] = direction;
        table["Up"] = up;
        table["Down"] = down;
        table["Horizontal"] = horizontal;
        table["Vertical"] = vertical;
    }
}
