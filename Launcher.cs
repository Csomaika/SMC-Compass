using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Launcher
{
    internal const string ProgramName = "SMC Compass";
    internal static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory;
    internal static string DataFolder
    {
        get
        {
            return Environment.GetEnvironmentVariable("COMPASS_DATA") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProgramName);
        }
    }

    internal static string Version
    {
        get { return File.ReadAllText(Path.Combine(Folder, "version.txt")).Trim(); }
    }

    internal static void StartProgram(string exe)
    {
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true });
    }

    internal static void OpenWeb(string url)
    {
        Uri uri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Érvényes HTTPS cím szükséges.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static bool ForwardToInstalledVersion()
    {
        string pointer = Path.Combine(DataFolder, "active-version.txt");
        if (!File.Exists(pointer)) return false;
        string version = File.ReadAllText(pointer).Trim();
        if (!Regex.IsMatch(version, @"^\d{1,5}\.\d{1,5}\.\d{1,5}$")) return false;
        if (new Version(version) <= new Version(Version)) return false;
        string candidate = Path.Combine(DataFolder, "versions", version, "SMC-Compass.exe");
        if (!File.Exists(candidate)) return false;
        StartProgram(candidate);
        return true;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        bool smoke = Array.IndexOf(args, "--self-test") >= 0;
        try
        {
            if (!smoke && ForwardToInstalledVersion()) return 0;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var mutex = new Mutex(false, InstanceKey()))
            {
                bool locked;
                try { locked=mutex.WaitOne(8000); }
                catch(AbandonedMutexException) { locked=true; }
                if(!locked)throw new InvalidOperationException("Az SMC Compass már meg van nyitva.");
                try
                {
                    using (var worker = new Worker(Folder))
                    {
                        if (smoke)
                        {
                            var state = worker.Call("state", null);
                            var forecast = worker.Call("forecast", Json.Fields("year", 2026));
                            if (Json.Get(state, "config") == null || Json.Items(forecast, "actual").Count != 12) return 3;
                            using (var window = new DesktopWindow(worker)) window.RunLayoutTest(state, forecast);
                            return 0;
                        }
                        Application.Run(new DesktopWindow(worker));
                    }
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
        catch (Exception error)
        {
            if (smoke)
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(Path.Combine(DataFolder, "smoke-error.txt"), error.ToString());
            }
            else MessageBox.Show(error.Message, ProgramName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static string InstanceKey()
    {
        using (var hash=SHA256.Create())
        {
            var folder=Path.GetFullPath(DataFolder).ToUpperInvariant();
            return "Local\\SMC-Compass-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(folder))).Replace("-","");
        }
    }
}

internal sealed class Worker : IDisposable
{
    private readonly object gate = new object();
    private readonly Process process;
    private readonly StreamWriter input;
    private readonly StreamReader output;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32000000, RecursionLimit = 80 };

    internal Worker(string folder)
    {
        string python = Path.Combine(folder, "runtime", "python.exe");
        string desktop = Path.Combine(folder, "app", "desktop.py");
        if (!File.Exists(python) || !File.Exists(desktop))
            throw new FileNotFoundException("Bontsd ki a teljes SMC Compass ZIP-et egy mappába, és onnan indítsd az alkalmazást.");
        process = Process.Start(new ProcessStartInfo(python, "-u \"" + desktop + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false)
        });
        process.BeginErrorReadLine();
        input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
        output = process.StandardOutput;
    }

    internal Dictionary<string, object> Call(string action, Dictionary<string, object> data)
    {
        lock (gate)
        {
            if (process.HasExited) throw new InvalidOperationException("Az alkalmazás adatfeldolgozója leállt.");
            input.WriteLine(json.Serialize(Json.Fields("action", action, "data", data ?? Json.Fields())));
            string reply = output.ReadLine();
            if (reply == null) throw new InvalidOperationException("Az adatfeldolgozó nem válaszolt.");
            var result = Json.Map(json.DeserializeObject(reply));
            if (Json.Get(result, "error") != null) throw new InvalidOperationException(Json.Text(result, "error"));
            return Json.Map(Json.Get(result, "value"));
        }
    }

    internal Task<Dictionary<string, object>> CallAsync(string action, Dictionary<string, object> data = null)
    {
        return Task.Run(() => Call(action, data));
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            try { input.WriteLine("{\"action\":\"quit\"}"); } catch (IOException) { }
            if (!process.WaitForExit(1500)) process.Kill();
        }
        input.Dispose();
        output.Dispose();
        process.Dispose();
    }
}

internal static class Json
{
    internal static Dictionary<string, object> Fields(params object[] pairs)
    {
        var data = new Dictionary<string, object>();
        for (int i = 0; i < pairs.Length; i += 2) data.Add((string)pairs[i], pairs[i+1]);
        return data;
    }

    internal static Dictionary<string, object> Map(object value)
    {
        return value as Dictionary<string, object> ?? Fields();
    }

    internal static object Get(Dictionary<string, object> value, string key)
    {
        object found;
        return value != null && value.TryGetValue(key, out found) ? found : null;
    }

    internal static string Text(Dictionary<string, object> value, string key, string fallback = "")
    {
        var item = Get(value,key);
        return item == null ? fallback : Convert.ToString(item, CultureInfo.InvariantCulture);
    }

    internal static bool Flag(Dictionary<string, object> value, string key)
    {
        return Get(value,key) is bool && (bool)Get(value,key);
    }

    internal static double? Number(object value)
    {
        double amount;
        return value != null && Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture,out amount) ? (double?)amount : null;
    }

    internal static IList<object> Items(Dictionary<string, object> value, string key)
    {
        return Get(value,key) as IList<object> ?? new object[0];
    }
}
