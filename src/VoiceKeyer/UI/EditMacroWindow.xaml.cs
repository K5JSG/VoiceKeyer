using System.Windows;
using VoiceKeyer.Macros;
using VoiceKeyer.Services;

namespace VoiceKeyer.UI;

public partial class EditMacroWindow : Window
{
    private readonly MessageSender _sender;

    public EditMacroWindow(MacroEntry entry, MessageSender sender)
    {
        InitializeComponent();
        _sender = sender;
        Heading.Text = $"F{entry.Key}";
        LabelBox.Text = entry.Label;
        ScriptBox.Text = entry.Message;
    }

    public string Label => LabelBox.Text.Trim();

    public string Message => ScriptBox.Text.Trim();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Preview_Click(object sender, RoutedEventArgs e) => _ = _sender.SendAsync(Message, key: false);
}
