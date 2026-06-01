using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using YTDownloader.Models;

namespace YTDownloader.Views;

public sealed partial class OrganizerPage : Page
{
    public ObservableCollection<FileEntry> LeftItems { get; } = new();
    public ObservableCollection<FileEntry> RightItems { get; } = new();

    private string _leftDir = "";
    private string _rightDir = "";
    private ListView? _dragSource;
    private const string DataKey = "mediafy-files";

    public OrganizerPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        LeftList.ItemsSource = LeftItems;
        RightList.ItemsSource = RightItems;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Izquierda: SIEMPRE arranca en la carpeta de descargas de MediaFy
        string downloads = Core.AppSettings.Current.OutputFolder;
        Directory.CreateDirectory(downloads);
        NavigateLeft(downloads);

        // Derecha: última ruta guardada, o Música del usuario por defecto
        string right = Core.AppSettings.Current.OrganizerRightPath;
        if (string.IsNullOrEmpty(right) || !Directory.Exists(right))
            right = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        NavigateRight(right);
    }

    // ── Navegación ─────────────────────────────────────────────
    private void NavigateLeft(string path)
    {
        _leftDir = path;
        LeftPath.Text = Compact(path);
        Reload(LeftItems, path, LeftCount, LeftStatus);
    }

    private void NavigateRight(string path)
    {
        _rightDir = path;
        RightPath.Text = Compact(path);
        Core.AppSettings.Current.OrganizerRightPath = path;
        Reload(RightItems, path, RightCount, RightStatus);
    }

    private static void Reload(ObservableCollection<FileEntry> list, string path, TextBlock count, TextBlock status)
    {
        list.Clear();
        try
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists) { status.Text = "(carpeta no encontrada)"; count.Text = "0"; return; }
            foreach (var d in di.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                list.Add(FileEntry.FromDir(d));
            foreach (var f in di.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                list.Add(FileEntry.FromFile(f));
            count.Text = list.Count.ToString();
            status.Text = "";
        }
        catch (Exception ex)
        {
            status.Text = $"Error: {ex.Message}";
            count.Text = "0";
        }
    }

    private static string Compact(string path)
    {
        if (path.Length <= 60) return path;
        return path[..3] + "..." + path[^54..];
    }

    private void LeftUp_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_leftDir);
        if (parent != null) NavigateLeft(parent.FullName);
    }

    private void RightUp_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_rightDir);
        if (parent != null) NavigateRight(parent.FullName);
    }

    private void LeftRefresh_Click(object sender, RoutedEventArgs e) => NavigateLeft(_leftDir);
    private void RightRefresh_Click(object sender, RoutedEventArgs e) => NavigateRight(_rightDir);

    private void LeftList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LeftList.SelectedItem is FileEntry f && f.IsDirectory) NavigateLeft(f.FullPath);
    }
    private void RightList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RightList.SelectedItem is FileEntry f && f.IsDirectory) NavigateRight(f.FullPath);
    }

    private async void RightChoose_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null) NavigateRight(folder.Path);
    }

    // ── Drag & drop ────────────────────────────────────────────
    private void List_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragSource = sender as ListView;
        var paths = e.Items.OfType<FileEntry>().Select(x => x.FullPath).ToList();
        e.Data.Properties[DataKey] = string.Join("|", paths);
        e.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
    }

    private void LeftList_DragOver(object sender, DragEventArgs e)
    {
        if (_dragSource == RightList)
        {
            bool copy = IsCtrlDown();
            e.AcceptedOperation = copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
            e.DragUIOverride.Caption = copy ? "Copiar a Descargas" : "Mover a Descargas";
        }
        else e.AcceptedOperation = DataPackageOperation.None;
    }

    private void RightList_DragOver(object sender, DragEventArgs e)
    {
        if (_dragSource == LeftList)
        {
            bool copy = IsCtrlDown();
            e.AcceptedOperation = copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
            e.DragUIOverride.Caption = copy ? "Copiar a Destino" : "Mover a Destino";
        }
        else e.AcceptedOperation = DataPackageOperation.None;
    }

    private async void LeftList_Drop(object sender, DragEventArgs e)
    {
        if (_dragSource != RightList) return;
        await HandleDrop(e, _rightDir, _leftDir);
    }

    private async void RightList_Drop(object sender, DragEventArgs e)
    {
        if (_dragSource != LeftList) return;
        await HandleDrop(e, _leftDir, _rightDir);
    }

    private async Task HandleDrop(DragEventArgs e, string fromDir, string toDir)
    {
        var def = e.GetDeferral();
        try
        {
            bool copy = e.AcceptedOperation == DataPackageOperation.Copy;
            if (e.DataView.Properties.TryGetValue(DataKey, out object? v) && v is string s)
            {
                var paths = s.Split('|', StringSplitOptions.RemoveEmptyEntries);
                await TransferAsync(paths, toDir, copy);
                NavigateLeft(_leftDir);
                NavigateRight(_rightDir);
            }
        }
        finally { def.Complete(); }
    }

    private static bool IsCtrlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ── Operaciones (botones) ──────────────────────────────────
    private async void MoveRight_Click(object sender, RoutedEventArgs e) => await TransferSelected(LeftList, _rightDir, copy: false);
    private async void CopyRight_Click(object sender, RoutedEventArgs e) => await TransferSelected(LeftList, _rightDir, copy: true);
    private async void MoveLeft_Click(object sender, RoutedEventArgs e)  => await TransferSelected(RightList, _leftDir, copy: false);
    private async void CopyLeft_Click(object sender, RoutedEventArgs e)  => await TransferSelected(RightList, _leftDir, copy: true);

    private async Task TransferSelected(ListView source, string toDir, bool copy)
    {
        var items = source.SelectedItems.OfType<FileEntry>().Select(f => f.FullPath).ToList();
        if (items.Count == 0) { TxtStatus.Text = "Selecciona archivos primero"; return; }
        await TransferAsync(items, toDir, copy);
        NavigateLeft(_leftDir);
        NavigateRight(_rightDir);
    }

    private async Task TransferAsync(IEnumerable<string> paths, string toDir, bool copy)
    {
        int ok = 0, fail = 0;
        foreach (var src in paths)
        {
            try
            {
                string name = Path.GetFileName(src);
                string dest = Path.Combine(toDir, name);
                // Evita conflicto si ya existe
                dest = EnsureUnique(dest);

                if (Directory.Exists(src))
                {
                    if (copy) CopyDir(src, dest);
                    else Directory.Move(src, dest);
                }
                else if (File.Exists(src))
                {
                    if (copy) File.Copy(src, dest, overwrite: false);
                    else File.Move(src, dest);
                }
                else continue;
                ok++;
            }
            catch { fail++; }
        }
        TxtStatus.Text = $"{(copy ? "Copiado" : "Movido")}: {ok}" + (fail > 0 ? $" · errores: {fail}" : "");
        await Task.CompletedTask;
    }

    private static string EnsureUnique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; i < 9999; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return path;
    }

    private static void CopyDir(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.EnumerateFiles(source))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), false);
        foreach (var d in Directory.EnumerateDirectories(source))
            CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    // ── Nueva carpeta / renombrar / eliminar ──────────────────
    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        string? name = await PromptAsync("Nueva carpeta", "Nombre de la carpeta", "Nueva carpeta");
        if (string.IsNullOrWhiteSpace(name)) return;
        // El destino es el panel derecho (la idea es organizar EN el destino)
        string path = EnsureUnique(Path.Combine(_rightDir, Sanitize(name)));
        try { Directory.CreateDirectory(path); NavigateRight(_rightDir); TxtStatus.Text = $"Creada: {Path.GetFileName(path)}"; }
        catch (Exception ex) { TxtStatus.Text = $"Error: {ex.Message}"; }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var (list, _) = GetActive();
        if (list.SelectedItem is not FileEntry f) { TxtStatus.Text = "Selecciona un elemento"; return; }
        string? newName = await PromptAsync("Renombrar", "Nuevo nombre", f.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == f.Name) return;
        try
        {
            string newPath = Path.Combine(Path.GetDirectoryName(f.FullPath)!, Sanitize(newName));
            if (f.IsDirectory) Directory.Move(f.FullPath, newPath);
            else File.Move(f.FullPath, newPath);
            NavigateLeft(_leftDir); NavigateRight(_rightDir);
        }
        catch (Exception ex) { TxtStatus.Text = $"Error: {ex.Message}"; }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var (list, _) = GetActive();
        var items = list.SelectedItems.OfType<FileEntry>().ToList();
        if (items.Count == 0) { TxtStatus.Text = "Selecciona elementos"; return; }

        var dlg = new ContentDialog
        {
            Title = "Eliminar",
            Content = $"¿Eliminar {items.Count} elemento(s)? Esta acción no se puede deshacer.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        int ok = 0, fail = 0;
        foreach (var f in items)
        {
            try
            {
                if (f.IsDirectory) Directory.Delete(f.FullPath, recursive: true);
                else File.Delete(f.FullPath);
                ok++;
            }
            catch { fail++; }
        }
        TxtStatus.Text = $"Eliminados: {ok}" + (fail > 0 ? $" · errores: {fail}" : "");
        NavigateLeft(_leftDir); NavigateRight(_rightDir);
    }

    private (ListView list, string dir) GetActive()
    {
        // "Activo" = el que tiene selección. Por defecto, el izquierdo.
        if (RightList.SelectedItems.Count > 0) return (RightList, _rightDir);
        return (LeftList, _leftDir);
    }

    private async Task<string?> PromptAsync(string title, string label, string initial)
    {
        var tb = new TextBox { Text = initial, Header = label };
        var dlg = new ContentDialog
        {
            Title = title,
            Content = tb,
            PrimaryButtonText = "Aceptar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? tb.Text : null;
    }

    private static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }
}
