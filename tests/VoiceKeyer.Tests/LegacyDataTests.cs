using VoiceKeyer.Services;

namespace VoiceKeyer.Tests;

public sealed class LegacyDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "voicekeyer-legacy-test-" + Guid.NewGuid().ToString("N"));

    public LegacyDataTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MoveFolder_MovesEverythingWhenOnlyTheOldFolderExists()
    {
        string old = Path.Combine(_root, "Keyer");
        string target = Path.Combine(_root, "VoiceKeyer");
        Directory.CreateDirectory(Path.Combine(old, "voices"));
        File.WriteAllText(Path.Combine(old, "settings.json"), "{}");
        File.WriteAllBytes(Path.Combine(old, "voices", "Me.onnx"), [1, 2, 3]);

        Assert.True(LegacyData.MoveFolder(old, target));

        Assert.False(Directory.Exists(old));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "settings.json")));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(target, "voices", "Me.onnx")));
    }

    [Fact]
    public void MoveFolder_NeverTouchesAnExistingNewFolder()
    {
        string old = Path.Combine(_root, "Keyer");
        string target = Path.Combine(_root, "VoiceKeyer");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(old, "old.txt"), "old");
        File.WriteAllText(Path.Combine(target, "new.txt"), "new");

        Assert.False(LegacyData.MoveFolder(old, target));

        Assert.True(File.Exists(Path.Combine(old, "old.txt")));
        Assert.True(File.Exists(Path.Combine(target, "new.txt")));
        Assert.False(File.Exists(Path.Combine(target, "old.txt")));
    }

    [Fact]
    public void MoveFolder_MissingSourceDoesNothing() =>
        Assert.False(LegacyData.MoveFolder(Path.Combine(_root, "nope"), Path.Combine(_root, "VoiceKeyer")));

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Roaming\Keyer\voices", @"C:\Users\x\AppData\Roaming\VoiceKeyer\voices")]
    [InlineData(@"c:\users\x\appdata\roaming\keyer", @"C:\Users\x\AppData\Roaming\VoiceKeyer")]
    [InlineData(@"C:\Users\x\AppData\Roaming\Keyer2\voices", @"C:\Users\x\AppData\Roaming\Keyer2\voices")]
    [InlineData(@"D:\Voices", @"D:\Voices")]
    [InlineData("", "")]
    public void RewritePrefix_OnlyRewritesPathsUnderTheOldFolder(string path, string expected) =>
        Assert.Equal(expected, LegacyData.RewritePrefix(path, @"C:\Users\x\AppData\Roaming\Keyer", @"C:\Users\x\AppData\Roaming\VoiceKeyer"));
}
