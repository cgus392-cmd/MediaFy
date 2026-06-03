using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace YTDownloader.Views;

public sealed partial class ExperimentalPage : Page
{
    private string? _file;
    private bool _busy;
    private readonly DispatcherTimer _monitor = new() { Interval = TimeSpan.FromMilliseconds(1000) };

    public ExperimentalPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += (_, _) => { ShowHardware(); UpdateEngineStatus(); };
        _monitor.Tick += Monitor_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); _monitor.Start(); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { base.OnNavigatedFrom(e); _monitor.Stop(); }

    private void ShowHardware()
    {
        var (_, label, detail) = Core.HardwareInfo.Recommend();
        HwMode.Text = label;
        HwDetail.Text = detail;
        GpuNote.Visibility = Core.HardwareInfo.HasNvidiaGpu ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Monitor de consumo CPU/GPU en vivo ──
    private void Monitor_Tick(object? sender, object e)
    {
        double cpu = Core.HardwareInfo.CpuUsagePercent();
        CpuBar.Value = cpu; CpuPctText.Text = $"{cpu:F0}%";

        if (!Core.HardwareInfo.HasNvidiaGpu) { GpuBar.Value = 0; GpuPctText.Text = "—"; return; }
        _ = Task.Run(() =>
        {
            double g = Core.HardwareInfo.GpuUsagePercent();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (g < 0) { GpuPctText.Text = "—"; }
                else { GpuBar.Value = g; GpuPctText.Text = $"{g:F0}%"; }
            });
        });
    }

    private void UpdateEngineStatus()
    {
        bool installed = App.Stems.IsEngineInstalled();
        EngineBar.IsOpen = !installed;
        if (installed)
        {
            EngineSizeText.Text = $"Motor instalado · ocupa {FormatBytes(App.Stems.GetEngineSizeBytes())}";
            BtnDeleteEngine.Visibility = Visibility.Visible;
        }
        else
        {
            EngineSizeText.Text = "Motor no instalado.";
            BtnDeleteEngine.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatBytes(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):F1} GB" :
        b >= 1L << 20 ? $"{b / (double)(1L << 20):F0} MB" :
        $"{b / (double)(1L << 10):F0} KB";

    private void SetInline(bool visible, double pct, string status)
    {
        ProgressPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (pct >= 0) { InlineProgress.IsIndeterminate = false; InlineProgress.Value = pct; }
        else InlineProgress.IsIndeterminate = true;
        if (status != null) InlineStatus.Text = status;
    }

    private async void BtnPickFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".opus", ".mp4", ".webm", ".mkv" })
            picker.FileTypeFilter.Add(ext);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var f = await picker.PickSingleFileAsync();
        if (f != null) { _file = f.Path; TxtFile.Text = Path.GetFileName(_file); }
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
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await StartEngineInstallationAsync();
    }

    private async Task StartEngineInstallationAsync()
    {
        if (_busy) return;
        _busy = true;
        var task = Core.NotificationCenter.Start("Instalando motor de IA", char.ConvertFromUtf32(0xE950));
        task.Status = "Iniciando descarga..."; task.Indeterminate = true;
        EngineBar.IsEnabled = false; BtnSeparate.IsEnabled = false;
        SetInline(true, -1, "Iniciando descarga del motor…");

        try
        {
            var progress = new Progress<(double pct, string msg)>(p => DispatcherQueue.TryEnqueue(() =>
            {
                if (p.pct >= 0) { task.Report(p.pct, p.msg); SetInline(true, p.pct, p.msg); }
                else { task.Status = p.msg; SetInline(true, -1, p.msg); }
            }));

            await Task.Run(() => App.Stems.InstallEngineAsync(progress, CancellationToken.None));
            task.Done("Instalación completada con éxito");
            SetInline(false, 100, "");

            var ok = new ContentDialog
            {
                Title = "Instalación completada",
                Content = "El motor de IA se instaló correctamente. Ya puedes separar pistas.",
                CloseButtonText = "Entendido", XamlRoot = XamlRoot
            };
            try { await ok.ShowAsync(); } catch { }
        }
        catch (Exception ex)
        {
            task.Fail($"Error al instalar: {ex.Message}");
            SetInline(false, 0, "");
            var fail = new ContentDialog
            {
                Title = "Error de instalación",
                Content = $"No se pudo instalar el motor de IA: {ex.Message}",
                CloseButtonText = "Cerrar", XamlRoot = XamlRoot
            };
            try { await fail.ShowAsync(); } catch { }
        }
        finally
        {
            EngineBar.IsEnabled = true; BtnSeparate.IsEnabled = true; _busy = false;
            UpdateEngineStatus();
        }
    }

    private async void BtnSeparate_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Stems.IsEngineInstalled()) { BtnInstallEngine_Click(sender, e); return; }
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
        if (_busy) return;
        _busy = true;
        BtnSeparate.IsEnabled = false;

        // Modelo según pistas + calidad (0=estándar, 1=alta, 2=máxima/Roformer).
        // (Nombres a verificar en la prueba real del motor.)
        int stems = StemsMode.SelectedIndex == 1 ? 4 : 2;
        int q = QualityMode.SelectedIndex;
        string model;
        if (stems == 4)
            // 4 pistas: htdemucs (rápido) → htdemucs_ft (lo más fuerte para multipista)
            model = q == 0 ? "htdemucs.yaml" : "htdemucs_ft.yaml";
        else
            // 2 pistas (voz/instrumental): MDX-HQ → MDX23C → BS-Roformer (estado del arte)
            model = q switch
            {
                0 => "UVR-MDX-NET-Inst_HQ_3.onnx",
                1 => "MDX23C-8KFFT-InstVoc_HQ.ckpt",
                _ => "model_bs_roformer_ep_317_sdr_12.9755.ckpt"
            };

        string songName = Path.GetFileName(_file);
        string qLabel = q == 0 ? "" : q == 1 ? " · alta calidad" : " · máxima (Roformer)";
        string modeText = (stems == 4 ? "4 pistas" : "2 pistas") + qLabel;
        var task = Core.NotificationCenter.Start($"Separando: {songName}", char.ConvertFromUtf32(0xEC4F));
        task.Status = $"Preparando ({modeText})..."; task.Indeterminate = true;
        SetInline(true, -1, $"Preparando separación ({modeText})…");

        try
        {
            var progress = new Progress<(double pct, string msg)>(p => DispatcherQueue.TryEnqueue(() =>
            {
                if (p.pct >= 0) { task.Report(p.pct, p.msg); SetInline(true, p.pct, p.msg); }
                else { task.Status = p.msg; SetInline(true, -1, p.msg); }
            }));

            await Task.Run(() => App.Stems.SeparateAsync(_file!, model, progress, CancellationToken.None));
            task.Done($"Listo: {modeText} en la Biblioteca");
            SetInline(false, 100, "");

            var ok = new ContentDialog
            {
                Title = "Separación completada",
                Content = $"Las pistas se guardaron en la Biblioteca, en la carpeta:\n\"Stems de {Path.GetFileNameWithoutExtension(songName)}\"",
                CloseButtonText = "Entendido", XamlRoot = XamlRoot
            };
            try { await ok.ShowAsync(); } catch { }
        }
        catch (Exception ex)
        {
            task.Fail($"Error al separar: {ex.Message}");
            SetInline(false, 0, "");
            var fail = new ContentDialog
            {
                Title = "Error en separación",
                Content = $"No se pudo completar la separación: {ex.Message}",
                CloseButtonText = "Cerrar", XamlRoot = XamlRoot
            };
            try { await fail.ShowAsync(); } catch { }
        }
        finally { BtnSeparate.IsEnabled = true; _busy = false; }
    }

    private async void BtnDeleteEngine_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "Borrar motor de IA",
            Content = "Se eliminará el motor de IA y todos los modelos descargados. La sección Experimental quedará como recién instalada. ¿Continuar?",
            PrimaryButtonText = "Borrar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var task = Core.NotificationCenter.Start("Borrando motor de IA", char.ConvertFromUtf32(0xE74D));
        task.Indeterminate = true;
        try
        {
            await Task.Run(() => App.Stems.UninstallEngine());
            task.Done("Motor de IA borrado · espacio liberado");
        }
        catch (Exception ex) { task.Fail($"Error: {ex.Message}"); }
        finally { UpdateEngineStatus(); }
    }
}
