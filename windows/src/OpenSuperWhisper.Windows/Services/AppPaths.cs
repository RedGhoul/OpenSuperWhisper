using System.IO;

namespace OpenSuperWhisper.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSuperWhisper"));

    public static string ModelsDirectory { get; } = Ensure(Path.Combine(DataDirectory, "models"));

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
