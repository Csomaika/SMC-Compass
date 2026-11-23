using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class Launcher
{
    [STAThread]
    private static void Main()
    {
        string basePath = AppDomain.CurrentDomain.BaseDirectory;
        string python = Path.Combine(basePath, "runtime", "pythonw.exe");
        string app = Path.Combine(basePath, "app", "app.py");
        try
        {
            if (!File.Exists(python) || !File.Exists(app))
                throw new FileNotFoundException("Bontsd ki a teljes SMC Compass ZIP-et egy mappába, és onnan indítsd az alkalmazást.");
            Process.Start(new ProcessStartInfo(python, "\"" + app + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = basePath
            });
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "SMC Compass", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
