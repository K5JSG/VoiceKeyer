namespace VoiceKeyer.Services;

/// <summary>
/// One-time move of data from the program's earlier name ("Keyer") to "VoiceKeyer": settings, voices and cache.
/// Runs at startup, only moves when the old folder exists and the new one does not, and never overwrites anything.
/// </summary>
public static class LegacyData
{
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string OldDataFolder => Path.Combine(Roaming, "Keyer");

    public static string OldTrainerFolder => Path.Combine(Documents, "Keyer Voice");

    /// <summary>Moves the settings/voices, cache and voice-training folders to their Voice Keyer names.</summary>
    public static void MigrateAll()
    {
        MoveFolder(OldDataFolder, AppSettings.DataFolder);
        MoveFolder(Path.Combine(Local, "Keyer"), Path.GetDirectoryName(AppSettings.CacheFolder)!);
        MoveFolder(OldTrainerFolder, AppSettings.DefaultTrainerFolder);
    }

    /// <summary>Renames a folder. False (and nothing changes) if the source is missing, the target exists, or the move fails.</summary>
    public static bool MoveFolder(string from, string to)
    {
        try
        {
            if (!Directory.Exists(from) || Directory.Exists(to))
            {
                return false;
            }

            Directory.Move(from, to);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use (an old copy still running, say): leave it where it is; the new name is tried again next start.
            return false;
        }
    }

    /// <summary>Replaces a leading <paramref name="oldPrefix"/> folder in <paramref name="path"/>; any other path is returned unchanged.</summary>
    public static string RewritePrefix(string path, string oldPrefix, string newPrefix)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string rest = path[oldPrefix.Length..];
        return rest.Length == 0 || rest[0] == Path.DirectorySeparatorChar || rest[0] == Path.AltDirectorySeparatorChar
            ? newPrefix + rest
            : path; // "Keyer2" must not match the prefix "Keyer"
    }
}
