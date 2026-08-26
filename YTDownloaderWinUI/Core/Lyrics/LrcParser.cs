using System.Text.RegularExpressions;

namespace YTDownloader.Core;

/// <summary>Lectura del formato LRC (sincronización por línea), compartida por los proveedores.</summary>
public static class LrcParser
{
    private static readonly Regex Tag =
        new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    /// <summary>Convierte un LRC en líneas ordenadas. Las líneas vacías se conservan (pausas).</summary>
    public static List<LyricLineVm> Parse(string lrc)
    {
        var lines = new List<LyricLineVm>();
        foreach (var raw in lrc.Split('\n'))
        {
            var matches = Tag.Matches(raw);
            if (matches.Count == 0) continue;

            string text = Tag.Replace(raw, "").Trim();
            foreach (Match m in matches)
                lines.Add(new LyricLineVm(TimeFrom(m), text));
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static TimeSpan TimeFrom(Match m)
    {
        int min = int.Parse(m.Groups[1].Value);
        int sec = int.Parse(m.Groups[2].Value);
        int ms = 0;
        if (m.Groups[3].Success)
        {
            // Las centésimas (2 dígitos) y milésimas (3) conviven en el formato.
            string f = m.Groups[3].Value;
            ms = int.Parse(f.PadRight(3, '0')[..3]);
        }
        return new TimeSpan(0, 0, min, sec, ms);
    }

    /// <summary>
    /// Descarta letras que en realidad no lo son: cabeceras de metadatos, créditos o
    /// resultados vacíos que algunas fuentes devuelven igualmente.
    /// </summary>
    public static bool LooksUsable(List<LyricLineVm> lines) =>
        lines.Count(l => !string.IsNullOrWhiteSpace(l.Text)) >= 3;
}
