using System.IO.Compression;

namespace VoiceKeyer.Services;

/// <summary>
/// Moves a Piper voice between PCs as a single zip holding the voice's <c>.onnx</c> and <c>.onnx.json</c>.
/// The same layout the Colab trainer produces, so a trained voice and an exported one import the same way.
/// </summary>
public static class VoicePackage
{
    /// <summary>Zips the named voice from <paramref name="voiceFolder"/> into <paramref name="zipPath"/>.</summary>
    public static void Export(string voiceFolder, string voiceName, string zipPath)
    {
        string model = Path.Combine(voiceFolder, voiceName + ".onnx");
        string config = model + ".json";
        if (!File.Exists(model) || !File.Exists(config))
        {
            throw new FileNotFoundException($"Voice \"{voiceName}\" needs both {Path.GetFileName(model)} and {Path.GetFileName(config)}.");
        }

        File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(model, Path.GetFileName(model), CompressionLevel.Optimal);
        zip.CreateEntryFromFile(config, Path.GetFileName(config), CompressionLevel.Optimal);
    }

    /// <summary>
    /// Imports a voice into <paramref name="voiceFolder"/> from a zip or from a <c>.onnx</c> file with its
    /// <c>.onnx.json</c> beside it. Returns the names of the voices added.
    /// </summary>
    public static IReadOnlyList<string> Import(string sourcePath, string voiceFolder)
    {
        Directory.CreateDirectory(voiceFolder);
        return sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? ImportZip(sourcePath, voiceFolder)
            : ImportFile(sourcePath, voiceFolder);
    }

    private static List<string> ImportFile(string onnxPath, string voiceFolder)
    {
        string config = onnxPath + ".json";
        if (!File.Exists(config))
        {
            throw new InvalidOperationException("The voice's .onnx.json file must be next to the .onnx file.");
        }

        string target = Path.Combine(voiceFolder, Path.GetFileName(onnxPath));
        File.Copy(onnxPath, target, overwrite: true);
        File.Copy(config, target + ".json", overwrite: true);
        return [Path.GetFileNameWithoutExtension(target)];
    }

    private static List<string> ImportZip(string zipPath, string voiceFolder)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        // Entries are flattened to their file name, so a zip can never write outside the voice folder.
        var models = zip.Entries.Where(entry => entry.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)).ToList();
        var configs = zip.Entries.Where(entry => entry.Name.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase)).ToList();
        var added = new List<string>();
        foreach (var model in models)
        {
            var config = configs.FirstOrDefault(entry => entry.Name.Equals(model.Name + ".json", StringComparison.OrdinalIgnoreCase));
            if (config is null)
            {
                continue;
            }

            string target = Path.Combine(voiceFolder, model.Name);
            model.ExtractToFile(target, overwrite: true);
            config.ExtractToFile(target + ".json", overwrite: true);
            added.Add(Path.GetFileNameWithoutExtension(target));
        }

        if (added.Count == 0)
        {
            throw new InvalidOperationException("The zip does not contain a voice (a .onnx file with its .onnx.json).");
        }

        return added;
    }
}
