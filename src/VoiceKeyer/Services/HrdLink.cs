using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace VoiceKeyer.Services;

/// <summary>
/// Reads the callsign from Ham Radio Deluxe Logbook's Add / Edit window through Windows UI Automation
/// (the interface screen readers use), so HRD needs no add-ons or settings. Read-only: Keyer never changes HRD.
/// Same approach as the Callsign Lookup app; the field is the "edtCALL" box of HRD's "HRDLBEdit" window.
/// </summary>
public static class HrdLink
{
    private const string WindowId = "QApplication.HRDLBEdit";
    private const string CallId = WindowId + ".edtCALL";

    /// <summary>
    /// The callsign in HRD's open Add/Edit window, upper case. An empty string means a window is open with the call box
    /// blank; null means HRD Logbook is not running or has no such window open (or could not be read right now).
    /// Blocks on HRD, so call it off the UI thread.
    /// </summary>
    public static string? ReadCallsign()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("HRDLogBook"))
            {
                using (process)
                {
                    var byProcess = new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id);
                    var isEditWindow = new PropertyCondition(AutomationElement.AutomationIdProperty, WindowId);
                    var isCallBox = new PropertyCondition(AutomationElement.AutomationIdProperty, CallId);
                    foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children, byProcess))
                    {
                        // Qt reports the Edit window as a child of the main logbook window rather than as a window of its own.
                        var window = top.FindFirst(TreeScope.Subtree, isEditWindow);
                        var box = window?.FindFirst(TreeScope.Descendants, isCallBox);
                        if (box is not null && box.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                        {
                            return ((ValuePattern)pattern).Current.Value.Trim().ToUpperInvariant();
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
}
