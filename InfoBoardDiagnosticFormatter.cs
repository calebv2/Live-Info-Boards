using System.Globalization;

public static class InfoBoardDiagnosticFormatter
{
    public static string Format(uint entityId, float x, float y, float z, string channel)
    {
        return "Infoboard entity=" + entityId + " position=(" +
            x.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
            y.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
            z.ToString("0.0", CultureInfo.InvariantCulture) + ") channel='" + channel + "'.";
    }
}
