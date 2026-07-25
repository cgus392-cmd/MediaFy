using System.IO;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YTDownloader.Core;

namespace YTDownloader;

/// <summary>
/// Anuncio de "Novedades" que aparece una vez por versión al abrir la app tras actualizar.
/// Comunica cambios importantes al usuario. En 1.8.0 explica el nuevo reproductor en vivo y
/// el requisito de cookies de YouTube, mostrando el estado real de autenticación del equipo.
/// </summary>
public static class WhatsNewDialog
{
    // Glyphs de Segoe Fluent Icons (nunca literales en el código → ConvertFromUtf32).
    private const int GlyphPlay = 0xE768;    // reproductor
    private const int GlyphInfo = 0xE946;    // aviso / información

    /// <summary>True si esta versión tiene notas que mostrar (evita abrir un diálogo vacío).</summary>
    public static bool HasNotesFor(string version) => NotesBuilder(version) != null;

    public static async Task ShowAsync(XamlRoot root, string version)
    {
        var build = NotesBuilder(version);
        if (build == null) return;

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = "Esto cambió en esta versión de MediaFy.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = Secondary(),
            Margin = new Thickness(0, 0, 0, 8)
        });
        build(panel);

        var dlg = new ContentDialog
        {
            Title = $"Novedades — MediaFy {version}",
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 460
            },
            XamlRoot = root,
            PrimaryButtonText = "Entendido",
            SecondaryButtonText = "Abrir Ajustes",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Secondary)
            (App.MainWindow as MainWindow)?.OpenSettings();
    }

    /// <summary>Devuelve el constructor de notas para la versión dada, o null si no hay notas.</summary>
    private static Action<StackPanel>? NotesBuilder(string version) => version switch
    {
        "1.8.0" => BuildNotes_1_8_0,
        _ => null
    };

    private static void BuildNotes_1_8_0(StackPanel panel)
    {
        // Reproductor en vivo
        AddHeading(panel, GlyphPlay, "Reproductor en vivo (streaming)");
        AddParagraph(panel,
            "Ahora puedes escuchar cualquier canción directamente desde el buscador, sin " +
            "descargarla. Búscala en la pestaña Descargas y pulsa el botón de reproducción de un " +
            "resultado: suena al instante, con controles del sistema y mini-reproductor.");

        // YouTube / autenticación
        AddHeading(panel, GlyphInfo, "Cambios importantes con YouTube");
        AddParagraph(panel,
            "Desde 2025, YouTube exige que las apps demuestren una sesión iniciada para descargar " +
            "o reproducir la mayoría de videos (el aviso “confirma que no eres un robot”). " +
            "Esto afectó a todas las apps de este tipo, no solo a MediaFy.");
        AddParagraph(panel,
            "La solución es sencilla y de una sola vez: importar tus cookies de YouTube (un archivo " +
            "cookies.txt de tu navegador con la sesión iniciada). MediaFy ya trae todo lo demás listo.");

        // Estado real del equipo (cookies + Node)
        AddStatusBar(panel);

        AddParagraph(panel,
            "Cómo hacerlo: Ajustes → Cuenta de YouTube → Importar. Ahí encontrarás una guía " +
            "paso a paso para exportar tu cookies.txt. Las cookies se guardan solo en tu equipo.");
    }

    /// <summary>InfoBar con el estado real de autenticación (cookies + Node) de este equipo.</summary>
    private static void AddStatusBar(StackPanel panel)
    {
        string cookies = AppSettings.Current.YouTubeCookiesPath;
        bool hasCookies = !string.IsNullOrWhiteSpace(cookies) && File.Exists(cookies);
        bool hasNode = YtDlpService.NodeInstalled;

        string title, message;
        InfoBarSeverity sev;

        if (hasCookies && hasNode)
        {
            sev = InfoBarSeverity.Success;
            title = "Tu MediaFy ya está listo";
            message = "Detectamos tus cookies de YouTube y el motor necesario. Descargas y " +
                      "reproducción funcionando — no tienes que hacer nada.";
        }
        else if (!hasCookies && hasNode)
        {
            sev = InfoBarSeverity.Warning;
            title = "Falta un paso: importar tus cookies";
            message = "Aún no has configurado el cookies.txt. Sin él, la mayoría de videos " +
                      "fallarán. Ve a Ajustes → Cuenta de YouTube → Importar.";
        }
        else if (hasCookies && !hasNode)
        {
            sev = InfoBarSeverity.Warning;
            title = "Falta instalar Node.js";
            message = "Tus cookies están, pero no se detectó Node.js (necesario para procesar " +
                      "YouTube). Instálalo desde nodejs.org y reinicia MediaFy.";
        }
        else
        {
            sev = InfoBarSeverity.Warning;
            title = "Faltan dos cosas por configurar";
            message = "Instala Node.js (nodejs.org) e importa tu cookies.txt en " +
                      "Ajustes → Cuenta de YouTube para activar descargas y reproducción.";
        }

        panel.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = sev,
            Title = title,
            Message = message,
            Margin = new Thickness(0, 6, 0, 6)
        });
    }

    // ── Helpers de construcción ─────────────────────────────────
    private static void AddHeading(StackPanel panel, int glyph, string text)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 12, 0, 2)
        };
        row.Children.Add(new FontIcon
        {
            Glyph = char.ConvertFromUtf32(glyph),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(row);
    }

    private static void AddParagraph(StackPanel panel, string text) =>
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Secondary(),
            Margin = new Thickness(0, 2, 0, 2)
        });

    private static Brush Secondary() =>
        (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
}
