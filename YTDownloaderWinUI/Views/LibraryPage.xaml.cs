using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YTDownloader.Models;

namespace YTDownloader.Views;

public sealed partial class LibraryPage : Page
{
    /// <summary>Entradas mezcladas: álbumes (carpetas) primero, luego archivos sueltos.</summary>
    public ObservableCollection<object> Entries { get; } = new();

    private readonly Core.FfmpegService _ffmpeg = new();
    private CancellationTokenSource? _coverCts;

    /// <summary>Carpeta/disco que la biblioteca está mostrando ahora mismo.</summary>
    private string _currentRoot = Core.AppSettings.Current.OutputFolder;
    private StorageDrive? _selectedDrive;
    private List<StorageDrive> _drives = new();

    public LibraryPage()
    {
        InitializeComponent();
        FilesList.ItemsSource = Entries;
        Loaded += (_, _) => { RefreshDrives(); LoadFiles(); };
        Core.AppSettings.Current.PropertyChanged += OnSettingsChanged;
    }

    // ── Unidades / discos ──────────────────────────────────────
    private void RefreshDrives()
    {
        var list = new List<StorageDrive> { BuildHome(Core.AppSettings.Current.OutputFolder) };
        list.AddRange(Core.DriveService.GetDrives());
        _drives = list;
        DriveList.ItemsSource = null;
        DriveList.ItemsSource = _drives;

        // Mantener selección actual; si no hay, usar "hogar"
        var match = _drives.FirstOrDefault(d => string.Equals(d.Root, _currentRoot, StringComparison.OrdinalIgnoreCase));
        _selectedDrive = match ?? _drives[0];
        UpdateDriveWidget();
    }

    private static StorageDrive BuildHome(string path)
    {
        long free = 0, total = 0;
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                var di = new DriveInfo(root);
                if (di.IsReady) { free = di.AvailableFreeSpace; total = di.TotalSize; }
            }
        }
        catch { }
        return new StorageDrive { Root = path, Label = "Descargas de MediaFy", IsHome = true, FreeBytes = free, TotalBytes = total };
    }

    private void UpdateDriveWidget()
    {
        if (_selectedDrive is null) return;
        DriveIcon.Glyph  = _selectedDrive.Icon;
        DriveName.Text   = _selectedDrive.DisplayName;
        DriveBar.Value   = _selectedDrive.UsedFraction;
        DriveSpace.Text  = _selectedDrive.SpaceText;
        DriveEject.Visibility = _selectedDrive.IsRemovable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectDrive(StorageDrive d)
    {
        _selectedDrive = d;
        _currentRoot = d.Root;
        UpdateDriveWidget();
        LoadFiles();
    }

    private void DriveFlyout_Opening(object? sender, object e) => RefreshDrives();

    private void DriveList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StorageDrive d) { SelectDrive(d); DriveFlyout.Hide(); }
    }

    private async void DriveEject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrive is null || !_selectedDrive.IsRemovable) return;
        string letter = _selectedDrive.Letter;
        bool ok = Core.DriveService.Eject(letter);

        // Volver a la carpeta de descargas y refrescar
        RefreshDrives();
        var home = _drives.FirstOrDefault(x => x.IsHome);
        if (home != null) SelectDrive(home);

        var dlg = new ContentDialog
        {
            Title = ok ? "Unidad expulsada" : "No se pudo expulsar",
            Content = ok
                ? $"Ya puedes retirar {letter} con seguridad."
                : $"No se pudo expulsar {letter}. Cierra los archivos en uso (incluido el reproductor) e inténtalo de nuevo.",
            CloseButtonText = "Entendido",
            XamlRoot = XamlRoot
        };
        try { await dlg.ShowAsync(); } catch { }
    }

    /// <summary>Todas las pistas/archivos (sueltos + dentro de álbumes), para portadas y búsquedas.</summary>
    private IEnumerable<LibraryFile> AllFiles()
    {
        foreach (var e in Entries)
        {
            if (e is LibraryFile f) yield return f;
            else if (e is LibraryAlbum a) foreach (var t in a.Tracks) yield return t;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _coverCts?.Cancel();
        Core.AppSettings.Current.PropertyChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Core.AppSettings.LibraryShowCovers)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var f in AllFiles()) f.RefreshCoverVisibility();
            StartCoverExtraction();
        });
    }

    private void LoadFiles()
    {
        Entries.Clear();
        string folder = _currentRoot;
        try
        {
            if (Directory.Exists(folder))
            {
                var root = new DirectoryInfo(folder);

                // 1) Álbumes = subcarpetas que contienen medios (ordenadas por nombre)
                foreach (var sub in root.EnumerateDirectories().OrderBy(d => d.Name))
                {
                    var tracks = sub
                        .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                        .Where(f => LibraryFile.MediaExtensions.Contains(f.Extension.ToLowerInvariant()))
                        .OrderBy(f => f.Name)
                        .Select(LibraryFile.From)
                        .ToList();
                    if (tracks.Count == 0) continue;

                    var album = new LibraryAlbum { Name = sub.Name, FolderPath = sub.FullName };
                    foreach (var t in tracks) album.Tracks.Add(t);
                    Entries.Add(album);
                }

                // 2) Archivos sueltos en la raíz (más recientes primero)
                var loose = root
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => LibraryFile.MediaExtensions.Contains(f.Extension.ToLowerInvariant()))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(LibraryFile.From);
                foreach (var f in loose) Entries.Add(f);
            }
        }
        catch { }

        int total = AllFiles().Count();
        TxtCount.Text = total.ToString();
        EmptyState.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilesList.Visibility  = Entries.Count > 0  ? Visibility.Visible : Visibility.Collapsed;

        StartCoverExtraction();
    }

    /// <summary>Extrae portadas en segundo plano (si está activado) y las asigna a cada ítem.</summary>
    private void StartCoverExtraction()
    {
        _coverCts?.Cancel();
        if (!Core.AppSettings.Current.LibraryShowCovers) return;

        _coverCts = new CancellationTokenSource();
        var ct = _coverCts.Token;
        var snapshot = AllFiles().ToList();

        _ = Task.Run(async () =>
        {
            foreach (var f in snapshot)
            {
                if (ct.IsCancellationRequested) return;
                if (!string.IsNullOrEmpty(f.CoverPath)) continue;
                string? cover = await _ffmpeg.ExtractCoverAsync(f.FullPath, ct);
                if (cover != null && !ct.IsCancellationRequested)
                    DispatcherQueue.TryEnqueue(() => f.CoverPath = cover);
            }
        }, ct);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) { RefreshDrives(); LoadFiles(); }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(_currentRoot))
                Process.Start("explorer.exe", $"\"{_currentRoot}\"");
        }
        catch { }
    }

    // ── Reproducir ─────────────────────────────────────────────
    private async void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path || !File.Exists(path)) return;

        var mode = Core.AppSettings.Current.PlayerMode;
        if (mode == Core.PlayerMode.Ask)
        {
            var chosen = await AskPlayerModeAsync();
            if (chosen is null) return;
            mode = chosen.Value;
        }

        if (mode == Core.PlayerMode.Integrated)
        {
            // Pasa portada del archivo (si ya estaba cacheada) para SMTC
            var lf = AllFiles().FirstOrDefault(f => f.FullPath == path);
            await App.Playback.PlayAsync(path, Path.GetFileNameWithoutExtension(path), null, lf?.CoverPath);
        }
        else
            OpenWithWindows(path);
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path && File.Exists(path))
            Frame.Navigate(typeof(EditorPage), path);
    }

    private async Task<Core.PlayerMode?> AskPlayerModeAsync()
    {
        var remember = new CheckBox { Content = "Recordar mi elección", Margin = new Thickness(0, 12, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "¿Con qué quieres reproducir este archivo?", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(remember);

        var dlg = new ContentDialog
        {
            Title = "Reproducir",
            Content = panel,
            PrimaryButtonText = "Reproductor integrado",
            SecondaryButtonText = "Otro programa…",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var r = await dlg.ShowAsync();
        Core.PlayerMode? choice = r switch
        {
            ContentDialogResult.Primary => Core.PlayerMode.Integrated,
            ContentDialogResult.Secondary => Core.PlayerMode.System,
            _ => null
        };
        if (choice is not null && remember.IsChecked == true)
            Core.AppSettings.Current.PlayerMode = choice.Value;
        return choice;
    }

    private static void OpenWithWindows(string path) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = $"shell32.dll,OpenAs_RunDLL {path}",
            UseShellExecute = false
        });

    private void BtnReveal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path && File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private void BtnRevealFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string folder && Directory.Exists(folder))
            Process.Start("explorer.exe", $"\"{folder}\"");
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path || !File.Exists(path)) return;
        var dlg = new ContentDialog
        {
            Title = "Eliminar archivo",
            Content = $"¿Seguro que quieres eliminar \"{Path.GetFileName(path)}\"? Esta acción no se puede deshacer.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            try { File.Delete(path); } catch { }
            LoadFiles();
        }
    }
}
