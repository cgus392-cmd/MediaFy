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
        "2.0.1" => BuildNotes_2_0_1,
        "2.0.0" => BuildNotes_2_0_0,
        "1.9.0" => BuildNotes_1_9_0,
        "1.8.2" => BuildNotes_1_8_2,
        "1.8.1" => BuildNotes_1_8_1,
        "1.8.0" => BuildNotes_1_8_0,
        _ => null
    };

    private static void BuildNotes_2_0_1(StackPanel panel)
    {
        AddHeading(panel, GlyphInfo, "Descargas de YouTube arregladas");
        AddParagraph(panel,
            "Algunas descargas fallaban con un error 403. La causa era que el motor de descargas " +
            "llevaba tiempo sin actualizarse: su actualización automática fallaba en silencio y, " +
            "con el motor desfasado, YouTube dejaba de entregar los datos.");
        AddParagraph(panel,
            "Esta versión incluye el motor al día, corrige su actualización automática y —para que " +
            "no vuelva a pasar sin avisar— el Estado del sistema ahora vigila su versión.");

        AddHeading(panel, GlyphPlay, "La app va más fluida");
        AddParagraph(panel,
            "Se eliminaron los tirones al usar la aplicación, el reproductor ya no se queda a medias " +
            "cuando la ventana está en segundo plano, y MediaFy dejó de acaparar el procesador " +
            "mientras descarga o convierte, así que el resto del equipo sigue respondiendo.");

        AddStatusBar(panel);
    }

    private static void BuildNotes_2_0_0(StackPanel panel)
    {
        AddHeading(panel, GlyphPlay, "Letras sincronizadas (karaoke)");
        AddParagraph(panel,
            "Nueva vista de letra a pantalla completa: la carátula difuminada de fondo, la línea " +
            "actual se llena al ritmo, con transiciones suaves y tamaño/alineación ajustables. " +
            "Reconoce la canción por sus etiquetas del archivo, así que acierta incluso en álbumes.");

        AddHeading(panel, GlyphInfo, "Cola y estado del sistema");
        AddParagraph(panel,
            "Ahora puedes ver y reordenar la cola de reproducción, y hay un diagnóstico integrado " +
            "(el semáforo de la barra superior) que comprueba que todo funcione —incluida una prueba " +
            "real de YouTube— para avisarte si algo se rompe antes de que te afecte.");
    }

    private static void BuildNotes_1_9_0(StackPanel panel)
    {
        AddHeading(panel, GlyphPlay, "Reproductor renovado (liquid glass)");
        AddParagraph(panel,
            "El mini-reproductor ahora luce la carátula del álbum difuminada de fondo, con un " +
            "acabado tipo vidrio. Y por fin muestra la barra de progreso de la canción: haz clic " +
            "o arrastra sobre ella para saltar a cualquier punto.");

        AddHeading(panel, GlyphInfo, "Cola y fundido entre canciones");
        AddParagraph(panel,
            "Al reproducir una canción de la Biblioteca se arma una cola con la carpeta y la " +
            "reproducción avanza sola a la siguiente (con botones anterior/siguiente y teclas " +
            "multimedia). Puedes activar un fundido suave entre canciones en " +
            "Ajustes → Fundido entre canciones.");
    }

    private static void BuildNotes_1_8_2(StackPanel panel)
    {
        AddHeading(panel, GlyphInfo, "Actualizador arreglado");
        AddParagraph(panel,
            "Corregimos un fallo que hacía cerrarse la app y el instalador al aplicar una " +
            "actualización desde el propio programa. Ahora el proceso se completa sin " +
            "interrupciones y MediaFy se reabre solo al terminar.");
        AddParagraph(panel,
            "El resto sigue igual: si ya importaste tu cookies.txt, no tienes que hacer nada.");
        AddStatusBar(panel);
    }

    private static void BuildNotes_1_8_1(StackPanel panel)
    {
        AddHeading(panel, GlyphInfo, "Ya no necesitas instalar nada");
        AddParagraph(panel,
            "MediaFy ahora incluye su propio motor para procesar YouTube — antes hacía falta " +
            "tener Node.js instalado en el equipo. Con esta actualización, las descargas y la " +
            "reproducción funcionan de fábrica.");
        AddParagraph(panel,
            "Solo sigue siendo necesario importar una vez tu cookies.txt de YouTube para " +
            "iniciar sesión (por el requisito anti-bot de YouTube).");

        AddStatusBar(panel);

        AddParagraph(panel,
            "¿Aún no importaste tus cookies? Ajustes → Cuenta de YouTube → Importar. " +
            "Ahí tienes la guía paso a paso.");
    }

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
        bool hasRuntime = YtDlpService.JsRuntimeAvailable; // el motor (deno) viene incluido

        string title, message;
        InfoBarSeverity sev;

        if (hasCookies && hasRuntime)
        {
            sev = InfoBarSeverity.Success;
            title = "Tu MediaFy ya está listo";
            message = "El motor viene incluido y detectamos tus cookies de YouTube. Descargas y " +
                      "reproducción funcionando — no tienes que hacer nada.";
        }
        else if (!hasCookies && hasRuntime)
        {
            sev = InfoBarSeverity.Warning;
            title = "Falta un paso: importar tus cookies";
            message = "El motor ya viene incluido, pero aún no has configurado el cookies.txt. " +
                      "Sin él, la mayoría de videos fallarán. Ve a Ajustes → Cuenta de YouTube → Importar.";
        }
        else
        {
            sev = InfoBarSeverity.Warning;
            title = "Falta el motor incluido";
            message = "No se encontró el motor de YouTube que trae MediaFy. Reinstala la app para " +
                      "restaurarlo, y luego importa tu cookies.txt en Ajustes → Cuenta de YouTube.";
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
