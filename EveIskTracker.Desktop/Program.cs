using Microsoft.AspNetCore.Builder;

namespace EveIskTracker;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Zweitstart abfangen: läuft schon eine Instanz, deren Fenster nach vorn holen
        using var mutex = new Mutex(true, @"Local\EveIskTracker", out var isFirst);
        if (!isFirst)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                http.PostAsync($"http://localhost:{Config.Port}/api/show-window", null).Wait();
            }
            catch { }
            return;
        }

        Db.Init(Config.DataDir);

        WebApplication server;
        try { server = WebHost.Start(); }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Der interne Server konnte nicht starten (Port " + Config.Port + " belegt?).\n\n" + ex.Message,
                "EVE ISK Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new MainForm();
        WebHost.ShowWindow = () => form.BeginInvoke(() =>
        {
            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.Activate();
        });

        // --tray: unsichtbar starten (für den Autostart mit Windows); Fenster kommt
        // dann per Doppelklick aufs Tray-Symbol
        if (args.Contains("--tray"))
            form.Load += (_, _) => { form.BeginInvoke(form.Hide); };

        Application.Run(form);
        server.StopAsync().Wait(TimeSpan.FromSeconds(3));
    }
}
