using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YTDownloader.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Core.DiagnosticsService.Updated -= OnHealthUpdated;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Core.DiagnosticsService.Updated -= OnHealthUpdated;
        Core.DiagnosticsService.Updated += OnHealthUpdated;
        if (Core.DiagnosticsService.LastResults.Count == 0)
            Core.DiagnosticsService.RefreshLight(); // dispara Updated → pinta la tarjeta
        RefreshCard();
    }

    private void OnHealthUpdated() => DispatcherQueue.TryEnqueue(RefreshCard);

    private void RefreshCard()
    {
        var overall = Core.DiagnosticsService.Overall;

        HealthCardIcon.Glyph = char.ConvertFromUtf32(overall switch
        {
            Core.HealthStatus.Ok      => 0xEC61,
            Core.HealthStatus.Warning => 0xE7BA,
            _                         => 0xEA39,
        });
        HealthCardIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(overall switch
        {
            Core.HealthStatus.Ok      => Windows.UI.Color.FromArgb(255, 0x3F, 0xB9, 0x50),
            Core.HealthStatus.Warning => Windows.UI.Color.FromArgb(255, 0xFF, 0xB0, 0x5C),
            _                         => Windows.UI.Color.FromArgb(255, 0xFF, 0x6B, 0x6B),
        });

        var problems = Core.DiagnosticsService.LastResults
            .Where(c => c.Status != Core.HealthStatus.Ok)
            .Select(c => c.Name)
            .ToList();

        HealthCardSummary.Text = overall switch
        {
            Core.HealthStatus.Ok      => "Todo funcionando correctamente.",
            Core.HealthStatus.Warning => $"Hay avisos: {string.Join(", ", problems)}.",
            _                         => $"Hay un problema: {string.Join(", ", problems)}.",
        };
    }

    private void HealthCard_Recheck(object sender, RoutedEventArgs e)
    {
        HealthCardSummary.Text = "Comprobando…";
        _ = Core.DiagnosticsService.RunFullAsync();
    }

    private void Go(string tag) => (App.MainWindow as MainWindow)?.NavigateTo(tag);

    private void Go_Downloads(object sender, RoutedEventArgs e) => Go("downloads");
    private void Go_Cascade(object sender, RoutedEventArgs e)   => Go("cascade");
    private void Go_Library(object sender, RoutedEventArgs e)   => Go("library");
    private void Go_Editor(object sender, RoutedEventArgs e)    => Go("editor");
}
