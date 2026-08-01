using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace EveIskTracker;

/// <summary>
/// Das Hauptfenster: ein WebView2, das die eingebettete Oberfläche vom internen Server lädt.
/// Schließen minimiert in den Infobereich (Tray) — der Server und der Datenabgleich laufen
/// weiter, damit das Streamlabs-Widget während des Streams nicht ausfällt.
/// </summary>
public class MainForm : Form
{
    private readonly WebView2 _web = new();
    private readonly NotifyIcon _tray = new();
    private bool _reallyClose;
    private bool _trayHintShown;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainForm()
    {
        Text = "EVE ISK Tracker";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 860);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(18, 22, 28);
        Icon = AppIcon.Create();

        // Dunkle Titelleiste (Windows 10 1809+; Attribut 20, davor 19)
        try
        {
            var on = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0)
                DwmSetWindowAttribute(Handle, 19, ref on, 4);
        }
        catch { /* rein kosmetisch */ }

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = Color.FromArgb(18, 22, 28);
        Controls.Add(_web);

        SetupTray();
        Load += async (_, _) => await InitWebView();
        FormClosing += OnClosing;
    }

    private async Task InitWebView()
    {
        // Eigener Profilordner, damit WebView2 nicht ins Programmverzeichnis schreiben will
        var env = await CoreWebView2Environment.CreateAsync(null,
            Path.Combine(Config.DataDir, "webview2"));
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = false;

        // Links mit target=_blank (z.B. developers.eveonline.com) gehören in den
        // richtigen Browser, nicht in ein zweites WebView-Fenster.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
        };

        core.Navigate($"http://localhost:{Config.Port}/");
    }

    private void SetupTray()
    {
        _tray.Icon = AppIcon.Create();
        _tray.Text = "EVE ISK Tracker";
        _tray.Visible = true;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Widget-URL kopieren", null, (_, _) => CopyOverlayUrl());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => { _reallyClose = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void CopyOverlayUrl()
    {
        // Ersten Charakter aus der Datenbank nehmen; ohne Charakter Platzhalter kopieren
        var id = Db.Scalar("SELECT character_id FROM characters WHERE enabled=1 ORDER BY name LIMIT 1");
        var url = $"http://localhost:{Config.Port}/overlay?charId=" +
                  (id == null || id == DBNull.Value ? "<ID>" : id.ToString());
        try
        {
            Clipboard.SetText(url);
            _tray.ShowBalloonTip(2500, "EVE ISK Tracker",
                "Widget-URL kopiert — in Streamlabs als Browser-Quelle (1920×1080) einfügen.",
                ToolTipIcon.Info);
        }
        catch { }
    }

    private void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (_reallyClose || e.CloseReason != CloseReason.UserClosing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            return;
        }

        // X gedrückt: in den Tray statt beenden, damit das Widget weiterläuft
        e.Cancel = true;
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(3000, "EVE ISK Tracker",
                "Läuft im Hintergrund weiter (Widget bleibt aktiv). Beenden über das Tray-Symbol.",
                ToolTipIcon.Info);
        }
    }
}

/// <summary>
/// Zeichnet das Anwendungssymbol zur Laufzeit: dunkler Kreis, blauer Ring, "Ƶ" —
/// so braucht die EXE keine eingebettete Icon-Datei.
/// </summary>
public static class AppIcon
{
    private static Icon _cached;

    public static Icon Create()
    {
        if (_cached != null) return _cached;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var back = new SolidBrush(Color.FromArgb(26, 31, 39));
            using var ring = new Pen(Color.FromArgb(108, 178, 245), 2.5f);
            g.FillEllipse(back, 1, 1, 30, 30);
            g.DrawEllipse(ring, 2, 2, 28, 28);
            using var f = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
            using var txt = new SolidBrush(Color.FromArgb(108, 178, 245));
            var s = g.MeasureString("Ƶ", f);
            g.DrawString("Ƶ", f, txt, (32 - s.Width) / 2, (32 - s.Height) / 2 + 1);
        }
        _cached = Icon.FromHandle(bmp.GetHicon());
        return _cached;
    }
}
