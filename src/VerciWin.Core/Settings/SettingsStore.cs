using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VerciWin.Core.Settings;

/// <summary>
/// Persists and loads user settings to/from %AppData%/VerciWin/settings.json.
/// Uses atomic write (temp file + replace) to prevent corruption.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsStore(string? overridePath = null)
    {
        if (overridePath != null)
        {
            _settingsFilePath = overridePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "VerciWin");
            Directory.CreateDirectory(appDir);
            _settingsFilePath = Path.Combine(appDir, "settings.json");
        }
    }

    /// <summary>
    /// Loads settings from disk. Returns <see cref="AppSettings.Defaults"/> if the
    /// file does not exist or fails to parse. Never throws.
    /// </summary>
    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaults = AppSettings.Defaults;
                await SaveAsync(defaults);
                return defaults;
            }

            await using var fs = File.OpenRead(_settingsFilePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(fs, JsonOpts);
            return settings ?? AppSettings.Defaults;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsStore] Failed to load settings, returning defaults: {ex.Message}");
            return AppSettings.Defaults;
        }
    }

    /// <summary>
    /// Atomically saves the settings to disk using a temporary file.
    /// </summary>
    public async Task SaveAsync(AppSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        string tempPath = _settingsFilePath + ".tmp";
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(fs, settings, JsonOpts);
                await fs.FlushAsync();
            }

            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsStore] Failed to save settings: {ex.Message}");
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }
    }
}
