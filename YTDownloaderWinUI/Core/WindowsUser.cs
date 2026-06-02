using System.IO;
using Windows.Storage.Streams;
using Windows.System;

namespace YTDownloader.Core;

/// <summary>
/// Obtiene el primer nombre del usuario de Windows y, si está disponible,
/// la foto de perfil de la cuenta. Fallback elegante a la inicial si no hay foto.
/// </summary>
public static class WindowsUser
{
    /// <summary>Solo el primer nombre (ej. "Camilo G." → "Camilo"). Si no hay, usa el username.</summary>
    public static async Task<string> GetFirstNameAsync()
    {
        try
        {
            var users = await User.FindAllAsync(UserType.LocalUser, UserAuthenticationStatus.LocallyAuthenticated);
            foreach (var u in users)
            {
                var first = await u.GetPropertyAsync(KnownUserProperties.FirstName) as string;
                if (!string.IsNullOrWhiteSpace(first)) return first;
                var display = await u.GetPropertyAsync(KnownUserProperties.DisplayName) as string;
                if (!string.IsNullOrWhiteSpace(display)) return display.Split(' ')[0];
            }
        }
        catch { }
        try
        {
            string env = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(env)) return Capitalize(env);
        }
        catch { }
        return "tú";
    }

    /// <summary>Foto de perfil del usuario o null si no hay. Ruta a un PNG en %TEMP%.</summary>
    public static async Task<string?> GetPictureAsync()
    {
        try
        {
            var users = await User.FindAllAsync(UserType.LocalUser, UserAuthenticationStatus.LocallyAuthenticated);
            foreach (var u in users)
            {
                var stream = await u.GetPictureAsync(UserPictureSize.Size208x208) as IRandomAccessStreamReference;
                if (stream != null)
                {
                    string path = Path.Combine(Path.GetTempPath(), $"mfuser_{Guid.NewGuid():N}.png");
                    using var src = await stream.OpenReadAsync();
                    using var fs = File.Create(path);
                    await src.AsStreamForRead().CopyToAsync(fs);
                    return path;
                }
            }
        }
        catch { }
        return null;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}
