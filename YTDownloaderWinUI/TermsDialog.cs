using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YTDownloader.Core;

namespace YTDownloader;

/// <summary>Muestra el descargo de responsabilidad / términos de uso (ES + EN).</summary>
public static class TermsDialog
{
    /// <summary>Devuelve true si se aceptó (o si es solo lectura).</summary>
    public static async Task<bool> ShowAsync(XamlRoot root, bool requireAccept)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = "Lee atentamente antes de usar MediaFy.  ·  Please read carefully before using MediaFy.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(0, 0, 0, 10)
        });

        AddSections(panel, "Español", LegalText.Es);
        panel.Children.Add(new Border
        {
            Height = 1, Margin = new Thickness(0, 14, 0, 14),
            Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
        });
        AddSections(panel, "English", LegalText.En);

        var sv = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 440
        };

        var dlg = new ContentDialog
        {
            Title = "Términos de uso y descargo de responsabilidad",
            Content = sv,
            XamlRoot = root,
            PrimaryButtonText = requireAccept ? "Acepto · I accept" : "Cerrar",
            DefaultButton = ContentDialogButton.Primary
        };
        if (requireAccept) dlg.CloseButtonText = "No acepto y salir";

        var result = await dlg.ShowAsync();
        return !requireAccept || result == ContentDialogResult.Primary;
    }

    private static void AddSections(StackPanel panel, string langTitle, (string H, string B)[] sections)
    {
        panel.Children.Add(new TextBlock
        {
            Text = langTitle, FontWeight = FontWeights.Bold, FontSize = 15,
            Margin = new Thickness(0, 2, 0, 6)
        });
        foreach (var (h, b) in sections)
        {
            panel.Children.Add(new TextBlock
            {
                Text = h, FontWeight = FontWeights.SemiBold, FontSize = 13,
                Margin = new Thickness(0, 8, 0, 2)
            });
            panel.Children.Add(new TextBlock
            {
                Text = b, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
    }
}
