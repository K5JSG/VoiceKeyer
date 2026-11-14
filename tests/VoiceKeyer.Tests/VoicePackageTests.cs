using System.IO.Compression;
using VoiceKeyer.Services;

namespace VoiceKeyer.Tests;

public sealed class VoicePackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "keyer-voice-test-" + Guid.NewGuid().ToString("N"));

    public VoicePackageTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ExportThenImport_MovesAVoiceToAnotherFolder()
    {
        string from = Path.Combine(_root, "pc1");
        string to = Path.Combine(_root, "pc2", "voices");
        Directory.CreateDirectory(from);
        File.WriteAllBytes(Path.Combine(from, "Me.onnx"), [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(from, "Me.onnx.json"), "{}");
        string zip = Path.Combine(_root, "Me_voice.zip");

        VoicePackage.Export(from, "Me", zip);
        var added = VoicePackage.Import(zip, to);

        Assert.Equal(["Me"], added);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(Path.Combine(to, "Me.onnx")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(to, "Me.onnx.json")));
    }

    [Fact]
    public void Import_ZipEntriesInSubfoldersAreFlattenedIntoTheVoiceFolder()
    {
        string zipPath = Path.Combine(_root, "trainer.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("out/../Evil.onnx").Open().Dispose();
            zip.CreateEntry("out/../Evil.onnx.json").Open().Dispose();
        }

        string to = Path.Combine(_root, "voices");
        var added = VoicePackage.Import(zipPath, to);

        Assert.Equal(["Evil"], added);
        Assert.True(File.Exists(Path.Combine(to, "Evil.onnx")));
        Assert.False(File.Exists(Path.Combine(_root, "Evil.onnx")));
    }

    [Fact]
    public void Import_ZipWithoutAVoiceIsRejected()
    {
        string zipPath = Path.Combine(_root, "empty.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt").Open().Dispose();
        }

        Assert.Throws<InvalidOperationException>(() => VoicePackage.Import(zipPath, Path.Combine(_root, "voices")));
    }

    [Fact]
    public void Export_MissingVoiceFileThrows()
    {
        Assert.Throws<FileNotFoundException>(() => VoicePackage.Export(_root, "Nope", Path.Combine(_root, "x.zip")));
    }

    [Fact]
    public void Import_DropsMultiCharacterPhonemesThatTheBundledPiperRejects()
    {
        string from = Path.Combine(_root, "trained");
        Directory.CreateDirectory(from);
        File.WriteAllBytes(Path.Combine(from, "Me.onnx"), [1]);
        File.WriteAllText(Path.Combine(from, "Me.onnx.json"),
            "{\"audio\":{\"sample_rate\":22050},\"phoneme_id_map\":{\"a\":[14],\"a\u026a\":[161],\"\u026a\":[102]}}");
        string to = Path.Combine(_root, "voices");

        VoicePackage.Import(Path.Combine(from, "Me.onnx"), to);

        var map = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(to, "Me.onnx.json")))!["phoneme_id_map"]!.AsObject();
        Assert.Equal(["a", "ɪ"], map.Select(entry => entry.Key));
    }
}
