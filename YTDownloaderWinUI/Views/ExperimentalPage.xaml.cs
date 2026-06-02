using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace YTDownloader.Views;

public sealed partial class ExperimentalPage : Page
{
    private string? _file;

    public ExperimentalPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += (_, _) => ShowHardware();
    }

    private void ShowHardware()
    {
        var (_, label, detail) = Core.HardwareInfo.Recommend();
        HwMode.Text = label;
        HwDetail.Text = detail;
    }

    private async void BtnPickFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".opus", ".mp4", ".webm", ".mkv" })
            picker.FileTypeFilter.Add(ext);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var f = await picker.PickSingleFileAsync();
        if (f != null)
        {
            _file = f.Path;
            TxtFile.Text = Path.GetFileName(_file);
        }
    }

    private async void BtnInstallEngine_Click(object sender, RoutedEventArgs e)
    {
        var (useGpu, label, detail) = Core.HardwareInfo.Recommend();
        string body =
            $"Tu equipo: {label}\n({detail})\n\n" +
            "La primera vez, MediaFy descargará e instalará el motor de IA de separación " +
            "(Python portable + Demucs/UVR) y el modelo correspondiente:\n\n" +
            $"• Descarga aproximada: {(useGpu ? "~2-3 GB (versión GPU/CUDA)" : "~1 GB (versión CPU)")}\n" +
            "• Modelos open-source (MIT) — Demucs (4 pistas) y MDX-Net (2 pistas)\n" +
            "• Se descarga una sola vez; luego la separación es local y sin internet\n\n" +
            "El instalador del motor llega en la próxima actualización de esta sección " +
            "experimental. Por ahora puedes preparar tu canción y revisar las expectativas.";

        var dlg = new ContentDialog
        {
            Title = "Instalar motor de IA",
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Entendido",
            XamlRoot = XamlRoot
        };
        try { await dlg.ShowAsync(); } catch { }
    }

    private async void BtnSeparate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_file))
        {
            var d = new ContentDialog
            {
                Title = "Elige una canción",
                Content = "Primero selecciona el archivo de audio que quieres separar.",
                CloseButtonText = "OK", XamlRoot = XamlRoot
            };
            try { await d.ShowAsync(); } catch { }
            return;
        }

        // El motor aún no está instalado: registramos una notificación informativa y
        // mostramos los detalles de instalación. (La separación real llega en la próxima fase.)
        var task = Core.NotificationCenter.Start("Separación de pistas", char.ConvertFromUtf32(0xEC4F));
        task.Status = "Pendiente: instala el motor de IA";
        task.Fail("Motor de IA no instalado todavía");
        BtnInstallEngine_Click(sender, e);
    }
}
