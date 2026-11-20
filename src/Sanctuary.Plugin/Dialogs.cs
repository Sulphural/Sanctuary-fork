using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sanctuary.Plugin;

public static partial class Dialogs
{
    private const uint MbOk = 0x0;
    private const uint MbYesNo = 0x4;
    private const uint MbIconError = 0x10;
    private const uint MbIconQuestion = 0x20;
    private const uint MbIconInformation = 0x40;
    private const uint MbSetForeground = 0x10000;
    private const uint MbTopMost = 0x40000;
    private const int IdYes = 6;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr window, string text, string caption, uint type);

    public static void Info(string message) => Show(message, false, false);

    public static void Error(string message) => Show(message, false, true);

    public static bool Confirm(string message) => Show(message, true, false);

    private static bool Show(string message, bool question, bool error)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var type = (question ? MbYesNo | MbIconQuestion : MbOk | (error ? MbIconError : MbIconInformation)) | MbSetForeground | MbTopMost;
                return MessageBox(IntPtr.Zero, message, PluginInfo.Name, type) == IdYes;
            }

            if (OperatingSystem.IsMacOS())
                return ShowMac(message, question, error);

            return ShowLinux(message, question, error);
        }
        catch (Exception)
        {
            Console.Error.WriteLine(message);
            return false;
        }
    }

    private static bool ShowMac(string message, bool question, bool error)
    {
        var buttons = question ? "buttons {\"Cancel\", \"Allow\"} default button \"Allow\" cancel button \"Cancel\"" : "buttons {\"OK\"} default button \"OK\"";
        var icon = error ? "with icon stop" : question ? "with icon caution" : "with icon note";
        var script = $"on run argv\ndisplay dialog (item 1 of argv) with title (item 2 of argv) {buttons} {icon}\nend run";

        return Run("osascript", ["-e", script, message, PluginInfo.Name]) == 0;
    }

    private static bool ShowLinux(string message, bool question, bool error)
    {
        var escaped = message.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        var zenityKind = question ? "--question" : error ? "--error" : "--info";
        var zenity = Run("zenity", [zenityKind, "--title", PluginInfo.Name, "--text", escaped, "--width", "420"]);

        if (zenity >= 0)
            return zenity == 0;

        var kdialogKind = question ? "--yesno" : error ? "--error" : "--msgbox";
        var kdialog = Run("kdialog", ["--title", PluginInfo.Name, kdialogKind, message]);

        if (kdialog >= 0)
            return kdialog == 0;

        Run("notify-send", [PluginInfo.Name, message]);
        Console.Error.WriteLine(message);
        return false;
    }

    private static int Run(string fileName, string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);

            if (process is null)
                return -1;

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;
        }
    }
}
