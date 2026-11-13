using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VoiceKeyer.Services;

/// <summary>What HRD's open Add/Edit window holds. Empty strings mean the boxes are blank.</summary>
public sealed record HrdQso(string Call, string RstSent);

/// <summary>
/// Reads the callsign and the report sent from Ham Radio Deluxe Logbook's Add / Edit window through Windows UI Automation
/// (the interface screen readers use), so HRD needs no add-ons or settings. Read-only: Keyer never changes HRD.
/// Same approach as the Callsign Lookup app; the fields are the "edtCALL" box and the "cbxRST_SENT" combo box of HRD's "HRDLBEdit" window.
/// </summary>
public static class HrdLink
{
    private const string WindowId = "QApplication.HRDLBEdit";
    private const string CallId = WindowId + ".edtCALL";
    private const string RstSentId = WindowId + ".cbxRST_SENT";

    /// <summary>
    /// The callsign (upper case) and report sent in HRD's open Add/Edit window. Null means HRD Logbook is not running or has
    /// no such window open (or could not be read right now). Blocks on HRD, so call it off the UI thread.
    /// </summary>
    public static HrdQso? ReadQso()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("HRDLogBook"))
            {
                using (process)
                {
                    var byProcess = new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id);
                    var isEditWindow = new PropertyCondition(AutomationElement.AutomationIdProperty, WindowId);
                    foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children, byProcess))
                    {
                        // Qt reports the Edit window as a child of the main logbook window rather than as a window of its own.
                        var window = top.FindFirst(TreeScope.Subtree, isEditWindow);
                        string? call = window is null ? null : ReadBox(window, CallId);
                        if (call is not null)
                        {
                            // RST Sent is a combo box; its text lives in the line edit inside it (the combo itself may not expose a value).
                            string rst = ReadBox(window!, RstSentId + ".QLineEdit") ?? ReadBox(window!, RstSentId) ?? string.Empty;
                            return new HrdQso(call.ToUpperInvariant(), rst);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // HRD closed or was busy while being read; the next poll tries again.
        }

        return null;
    }

    private static string? ReadBox(AutomationElement window, string automationId)
    {
        var box = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        return box is not null && box.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern)
            ? ((ValuePattern)pattern).Current.Value.Trim()
            : null;
    }
}
