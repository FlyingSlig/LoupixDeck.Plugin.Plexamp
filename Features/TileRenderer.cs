using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>
/// Draws an artwork tile: the cover fills the key, a dark band carries the title (and an
/// optional year) so the text stays readable over busy covers.
/// </summary>
internal static class TileRenderer
{
    public static void Render(IRenderCanvas canvas, byte[] image, string title, string? subtitle,
        string? status = null)
    {
        int width = canvas.Width;
        int height = canvas.Height;

        canvas.DrawImage(image, 0, 0, width, height);

        int band = Math.Max(16, (int)Math.Round(height * 0.42));
        canvas.FillRectangle(0, height - band, width, band, new PluginColor(0, 0, 0, 170));

        int pad = Math.Max(2, width / 32);
        float titleSize = Math.Clamp(height * 0.115f, 8f, 16f);

        if (status is not null)
            DrawStatusBadge(canvas, status, pad);

        if (subtitle is null)
        {
            canvas.DrawText(title, pad, height - band, width - (pad * 2), band,
                PluginColor.White, titleSize, TextHAlign.Center, TextVAlign.Middle,
                bold: true, outlined: true, outlineColor: new PluginColor(0, 0, 0, 220));
            return;
        }

        int titleHeight = Math.Max(12, (int)Math.Round(band * 0.64));
        int titleY = height - band + Math.Max(1, (band - titleHeight) / 2);

        canvas.DrawText(title, pad, titleY, width - (pad * 2), titleHeight,
            PluginColor.White, titleSize, TextHAlign.Center, TextVAlign.Middle,
            bold: true, outlined: true, outlineColor: new PluginColor(0, 0, 0, 220));

        float subtitleSize = Math.Clamp(height * 0.09f, 7f, 12f);
        int subtitleHeight = Math.Max(8, (int)Math.Round(band * 0.24));
        int subtitleY = height - subtitleHeight - Math.Max(1, (int)Math.Round(band * 0.04));

        canvas.DrawText(subtitle, pad, subtitleY, width - (pad * 2), subtitleHeight,
            new PluginColor(0xE0, 0xE0, 0xE0), subtitleSize, TextHAlign.Center, TextVAlign.Bottom,
            outlined: true, outlineColor: new PluginColor(0, 0, 0, 200));
    }

    private static void DrawStatusBadge(IRenderCanvas canvas, string status, int pad)
    {
        float size = Math.Clamp(canvas.Height * 0.085f, 7f, 12f);
        int height = Math.Max(12, (int)Math.Round(canvas.Height * 0.20));
        int width = Math.Min(
            canvas.Width - (pad * 2),
            (int)Math.Ceiling(canvas.MeasureText(status, size, bold: true)) + (pad * 4));

        int x = (canvas.Width - width) / 2;
        int y = Math.Max(2, (int)Math.Round(canvas.Height * 0.06));

        canvas.FillRoundedRectangle(x, y, width, height, height / 2, new PluginColor(0, 0, 0, 185));
        canvas.DrawText(status, x, y, width, height, PluginColor.White, size,
            TextHAlign.Center, TextVAlign.Middle, bold: true);
    }
}
