using VoiceKeyer.Macros;
using VoiceKeyer.Services;
using VoiceKeyer.Tts;
using VoiceKeyer.UI;

namespace VoiceKeyer.Tests;

public sealed class WindowSmokeTests
{
    [Fact]
    public void DialogWindows_ConstructWithoutError()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                var settings = AppSettings.Load();
                var sender = new MessageSender(settings, new PiperSynthesizer(Path.GetTempPath()), () => string.Empty, () => { });

                _ = new SettingsWindow(settings);
                _ = new TrainerWindow(settings);
                _ = new EditMacroWindow(new MacroEntry(1, "CQ", "{TTS CQ}"), sender);
                _ = new MainWindow();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
