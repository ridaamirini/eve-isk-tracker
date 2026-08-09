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

        // Wer die (jetzt eingebaute) Standard-ID früher manuell eingetragen hat, soll sie
        // nicht im Settings-Feld sehen — die eingebaute ID bleibt in der Oberfläche unsichtbar
        if (!string.IsNullOrEmpty(Config.DefaultClientId) && Config.ClientIdRaw == Config.DefaultClientId)
            Config.ClientId = "";

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

        // Game-Overlay: die Oberfläche (WebView2) steuert das WinForms-Fenster über
        // diese Haken — Aufrufe kommen von Kestrel-Threads, daher immer BeginInvoke
        WebHost.SetGameOverlay = on => form.BeginInvoke(() => form.SetGameOverlay(on));
        WebHost.SetGameOverlayMove = on => form.BeginInvoke(() => form.SetGameOverlayMove(on));
        WebHost.GameOverlayVisible = () => form.GameOverlayVisible;
        WebHost.GameOverlayMoving = () => form.GameOverlayMoving;
        WebHost.ApplyGameOverlaySettings = () => form.BeginInvoke(form.ApplyGameOverlaySettings);

        // War das Overlay beim letzten Beenden an, kommt es beim Start wieder
        if (Config.GameOverlayOn)
            form.Load += (_, _) => form.BeginInvoke(() => form.SetGameOverlay(true));

        // --tray: unsichtbar starten (für den Autostart mit Windows); Fenster kommt
        // dann per Doppelklick aufs Tray-Symbol
        if (args.Contains("--tray"))
            form.Load += (_, _) => { form.BeginInvoke(form.Hide); };

        Application.Run(form);
        server.StopAsync().Wait(TimeSpan.FromSeconds(3));

        // Sauber abschließen: alles aus dem WAL in die Hauptdatei, Verbindungen zu
        Db.Checkpoint();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
