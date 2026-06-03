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
        Loaded += (_, _) => { ShowHardware(); UpdateEngineStatus(); };
    }

    private void ShowHardware()
    {
        var (_, label, detail) = Core.HardwareInfo.Recommend();
        HwMode.Text = label;
        HwDetail.Text = detail;
    }

    private void UpdateEngineStatus()
    {
        bool installed = App.Stems.IsEngineInstalled();
        EngineBar.IsOpen = !installed;
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
            "¿Deseas iniciar la instalación del motor de IA ahora?";

        var dlg = new ContentDialog
        {
            Title = "Instalar motor de IA",
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Instalar",
            CloseButtonText = "Cancelar",
            XamlRoot = XamlRoot
        };
        
        var res = await dlg.ShowAsync();
        if (res == ContentDialogResult.Primary)
        {
            await StartEngineInstallationAsync();
        }
    }

    private async System.Threading.Tasks.Task StartEngineInstallationAsync()
    {
        var task = Core.NotificationCenter.Start("Instalando motor de IA", char.ConvertFromUtf32(0xE950));
        task.Status = "Iniciando descarga...";
        task.Progress = 0;
        task.Indeterminate = true;

        EngineBar.IsEnabled = false;
        BtnSeparate.IsEnabled = false;

        try
        {
            var progress = new Progress<(double pct, string msg)>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (p.pct >= 0)
                        task.Report(p.pct, p.msg);
                    else
                        task.Status = p.msg;
                });
            });

            await System.Threading.Tasks.Task.Run(() => App.Stems.InstallEngineAsync(progress, System.Threading.CancellationToken.None));
            
            task.Done("Instalación completada con éxito");

            var successDlg = new ContentDialog
            {
                Title = "Instalación completada",
                Content = "El motor de IA para la separación de stems se ha instalado correctamente.",
                CloseButtonText = "Entendido",
                XamlRoot = XamlRoot
            };
            try { await successDlg.ShowAsync(); } catch { }
        }
        catch (Exception ex)
        {
            task.Fail($"Error al instalar: {ex.Message}");
            var failDlg = new ContentDialog
            {
                Title = "Error de instalación",
                Content = $"No se pudo instalar el motor de IA: {ex.Message}",
                CloseButtonText = "Cerrar",
                XamlRoot = XamlRoot
            };
            try { await failDlg.ShowAsync(); } catch { }
        }
        finally
        {
            EngineBar.IsEnabled = true;
            BtnSeparate.IsEnabled = true;
            UpdateEngineStatus();
        }
    }

    private async void BtnSeparate_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Stems.IsEngineInstalled())
        {
            BtnInstallEngine_Click(sender, e);
            return;
        }

        if (string.IsNullOrEmpty(_file) || !File.Exists(_file))
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

        BtnSeparate.IsEnabled = false;

        string songName = Path.GetFileName(_file);
        int stemsCount = StemsMode.SelectedIndex == 1 ? 4 : 2;
        string modeText = stemsCount == 4 ? "4 pistas" : "2 pistas";

        var task = Core.NotificationCenter.Start($"Separando: {songName}", char.ConvertFromUtf32(0xEC4F));
        task.Status = $"Preparando separación ({modeText})...";
        task.Progress = 0;
        task.Indeterminate = true;

        try
        {
            var progress = new Progress<(double pct, string msg)>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (p.pct >= 0)
                        task.Report(p.pct, p.msg);
                    else
                        task.Status = p.msg;
                });
            });

            await System.Threading.Tasks.Task.Run(() => App.Stems.SeparateAsync(_file, stemsCount, progress, System.Threading.CancellationToken.None));
            
            task.Done($"Listo: {modeText} en la Biblioteca");

            var successDlg = new ContentDialog
            {
                Title = "Separación completada",
                Content = $"La separación de pistas ha finalizado con éxito.\nLas pistas resultantes se han guardado en la Biblioteca en la carpeta:\n\"Stems de {Path.GetFileNameWithoutExtension(songName)}\"",
                CloseButtonText = "Entendido",
                XamlRoot = XamlRoot
            };
            try { await successDlg.ShowAsync(); } catch { }
        }
        catch (Exception ex)
        {
            task.Fail($"Error al separar: {ex.Message}");
            var failDlg = new ContentDialog
            {
                Title = "Error en separación",
                Content = $"No se pudo completar la separación: {ex.Message}",
                CloseButtonText = "Cerrar",
                XamlRoot = XamlRoot
            };
            try { await failDlg.ShowAsync(); } catch { }
        }
        finally
        {
            BtnSeparate.IsEnabled = true;
        }
    }
}
