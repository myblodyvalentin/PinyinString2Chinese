using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;

namespace PinyinString2Chinese;

internal static class SettingsStore
{
    private const string RunValueName = "PinyinString2Chinese";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PinyinString2Chinese");

    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    internal static (AppSettings Settings, bool FirstRun) Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return (new AppSettings(), true);

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return (settings, false);
        }
        catch (Exception exception)
        {
            AppLog.Error(exception);
            return (new AppSettings(), false);
        }
    }

    internal static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePath, json);
        ApplyStartup(settings.StartWithWindows);
    }

    internal static void ApplyStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
                return;

            if (enabled)
                key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\"");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch (Exception exception)
        {
            AppLog.Error(exception);
        }
    }
}
