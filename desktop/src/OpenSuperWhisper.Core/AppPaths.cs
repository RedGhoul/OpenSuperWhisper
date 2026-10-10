namespace OpenSuperWhisper.Core;

/// <summary>
/// Per-user data folder: %LOCALAPPDATA%\OpenSuperWhisper on Windows, ~/Library/Application Support/OpenSuperWhisper
/// on macOS and ~/.local/share/OpenSuperWhisper on Linux. OSW_DATA_DIR overrides it (used by tests).
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Ensure(
        Environment.GetEnvironmentVariable("OSW_DATA_DIR") is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create), "OpenSuperWhisper"));

    public static string ModelsDirectory { get; } = Ensure(Path.Combine(DataDirectory, "models"));

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
