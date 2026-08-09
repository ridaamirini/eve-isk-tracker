using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace EveIskTracker;

/// <summary>
/// Das In-Game-Overlay: eine randlose, immer im Vordergrund liegende HUD-Karte
/// (DPS-Graph + Session-Werte) über dem EVE-Client — nach dem Vorbild des
/// Discord-Overlays. Im Normalzustand ist das Fenster durchklickbar und stiehlt
/// dem Spiel nie den Fokus; der Verschieben-Modus macht es kurz anfassbar.
/// Voraussetzung: EVE läuft im Fenstermodus oder randlosen Vollbild — exklusives
/// Vollbild zeichnet direkt auf den Bildschirm und übermalt jedes Fenster.
/// </summary>
public class GameOverlayForm : Form
{
    private readonly WebView2 _web = new();
    private readonly System.Windows.Forms.Timer _topTimer = new();
    private bool _moveMode;
    private Panel _movePanel;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_NOACTIVATE = 0x8000000;
    private const int WM_NCHITTEST = 0x84;
    private const int HTCAPTION = 2;
    private const int WM_EXITSIZEMOVE = 0x232;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int idx, int val);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lp);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;

    public GameOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        MaximizeBox = false;   // sonst maximiert ein Doppelklick die "Titelleiste" im Verschieben-Modus
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(440, 300);
        BackColor = Color.FromArgb(10, 15, 22);
        ApplySettings();
        RestorePosition();

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = Color.FromArgb(10, 15, 22);
        Controls.Add(_web);

        // Immer-im-Vordergrund regelmäßig erneuern: Spiele und andere Topmost-Fenster
        // schieben sich sonst gern davor. Gleichzeitig die Durchklick-Styles auffrischen,
        // weil WebView2 seine Kind-Fenster bei Bedarf neu erzeugt.
        _topTimer.Interval = 3000;
        _topTimer.Tick += (_, _) =>
        {
            if (!Visible) return;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            ApplyClickThrough();
        };
        _topTimer.Start();

        Load += async (_, _) => await InitWebView();
    }

    /// <summary>Nie den Fokus stehlen — das Spiel soll weiterlaufen, als wäre das Overlay nicht da.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    private async Task InitWebView()
    {
        // Eigener Profilordner UND eigener Browserprozess ohne GPU-Beschleunigung:
        // Immer-im-Vordergrund-Overlays über Spielen sind der klassische Auslöser
        // für Grafiktreiber-Abstürze. Die kleine Karte mit ihrem 1-fps-Graph braucht
        // keine GPU — Software-Rendering umgeht den Treiber komplett, und durch die
        // Prozesstrennung kann ein Absturz weder Hauptfenster noch Spiel mitreißen.
        var opts = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-gpu --disable-gpu-compositing",
        };
        var env = await CoreWebView2Environment.CreateAsync(null,
            Path.Combine(Config.DataDir, "webview2-overlay"), opts);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        // Stirbt der Render-Prozess doch einmal, neu laden statt weißer Karte
        core.ProcessFailed += (_, _) =>
        {
            try { BeginInvoke(() => { try { _web.CoreWebView2?.Reload(); } catch { } }); }
            catch { }
        };

        // Die Seite meldet sich für Fensteraktionen, die HTML allein nicht kann:
        // close = Overlay ausblenden, h:/w:<px> = Wunschmaße nach Modul-/Ansichtswahl
        core.WebMessageReceived += (_, e) =>
        {
            string msg;
            try { msg = e.TryGetWebMessageAsString(); } catch { return; }
            if (msg == "close") SetOverlayVisible(false);
            else if (msg != null && msg.StartsWith("h:") && int.TryParse(msg[2..], out var h))
                ClientSize = new Size(ClientSize.Width, Math.Clamp(h, 100, 640));
            else if (msg != null && msg.StartsWith("w:") && int.TryParse(msg[2..], out var w))
                ClientSize = new Size(Math.Clamp(w, 200, 700), ClientSize.Height);
        };

        core.Navigate($"http://localhost:{Config.Port}/gameoverlay");
        ApplyClickThrough();
    }

    // ---- Sichtbarkeit & Modi (immer auf dem UI-Thread aufrufen) ----

    public void SetOverlayVisible(bool on)
    {
        if (on)
        {
            try { _web.CoreWebView2?.Resume(); } catch { }
            RestorePosition();
            Show();
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            ApplyClickThrough();
        }
        else
        {
            if (_moveMode) SetMoveMode(false);
            Hide();
            // Ausgeblendet komplett schlafen legen: keine Timer, kein Rendering,
            // keine Abfragen — das Overlay kostet dann exakt nichts
            try { _web.CoreWebView2?.TrySuspendAsync(); } catch { }
        }
        Config.GameOverlayOn = on;
    }

    public bool MoveMode => _moveMode;

    /// <summary>
    /// Verschieben-Modus: statt der Webseite erscheint eine native Karte, die sich
    /// mit der Maus ziehen lässt. Der frühere Weg (Ziehen über die HTML-Griffleiste
    /// per WM_NCLBUTTONDOWN) scheitert an WebView2 — der Browserprozess hält die
    /// Maus-Capture, die Nachricht kommt nie als Fenster-Drag an.
    /// </summary>
    public void SetMoveMode(bool on)
    {
        _moveMode = on;
        if (on)
        {
            // jedes Mal frisch bauen, damit die Sprache aus den Settings stimmt
            _movePanel?.Dispose();
            _movePanel = BuildMovePanel();
            Controls.Add(_movePanel);
            _movePanel.BringToFront();
            _web.Visible = false;
        }
        else
        {
            if (_movePanel != null) _movePanel.Visible = false;
            _web.Visible = true;
            Config.GameOverlayPos = $"{Left},{Top}";
        }
        ApplyClickThrough();
    }

    /// <summary>
    /// Panel/Labels, die für den Maus-Treffertest unsichtbar sind (HTTRANSPARENT):
    /// der Klick fällt zum Formular durch, das im Verschieben-Modus überall
    /// HTCAPTION meldet — Windows übernimmt dann das komplette Fenster-Schleppen
    /// nativ, robuster als jedes handgebaute Capture-Tracking.
    /// </summary>
    private sealed class GhostPanel : Panel
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)(-1); return; }
            base.WndProc(ref m);
        }
    }
    private sealed class GhostLabel : Label
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)(-1); return; }
            base.WndProc(ref m);
        }
    }

    private Panel BuildMovePanel()
    {
        var de = Config.Lang == "de";
        var accent = Color.FromArgb(77, 163, 255);
        var panel = new GhostPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(13, 20, 30),
            Cursor = Cursors.SizeAll,
        };
        var title = new GhostLabel
        {
            Text = de ? "⠿  OVERLAY VERSCHIEBEN" : "⠿  MOVE OVERLAY",
            ForeColor = accent,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 54,
            TextAlign = ContentAlignment.BottomCenter,
            Cursor = Cursors.SizeAll,
        };
        var hint = new GhostLabel
        {
            Text = de
                ? "Karte mit der Maus an die Wunschposition ziehen.\nDie Position wird automatisch gespeichert."
                : "Drag this card to where you want the overlay.\nThe position is saved automatically.",
            ForeColor = Color.FromArgb(153, 164, 181),
            Font = new Font("Segoe UI", 9),
            Dock = DockStyle.Top,
            Height = 60,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.SizeAll,
        };
        var ok = new Button
        {
            Text = de ? "OK — fixieren" : "OK — lock",
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(228, 235, 244),
            BackColor = Color.FromArgb(28, 61, 97),
            Size = new Size(150, 34),
            Cursor = Cursors.Hand,
        };
        ok.FlatAppearance.BorderColor = accent;
        ok.Click += (_, _) => SetMoveMode(false);

        panel.Controls.Add(ok);
        panel.Controls.Add(hint);
        panel.Controls.Add(title);
        void PlaceOk() => ok.Location = new Point((panel.Width - ok.Width) / 2, panel.Height - ok.Height - 20);
        panel.Resize += (_, _) => PlaceOk();
        PlaceOk();
        return panel;
    }

    /// <summary>
    /// Ziehen von Hand, ohne Maus-Capture und ohne Windows' Modal-Loop: solange die
    /// linke Taste unten ist, führt ein UI-Timer das Fenster dem Cursor nach.
    /// Damit ist es egal, an welches Kind-Fenster die Maus-Ereignisse gehen —
    /// wichtig, weil dieses Fenster nie aktiviert wird (WS_EX_NOACTIVATE).
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_moveMode || e.Button != MouseButtons.Left) return;
        var offset = e.Location;
        var drag = new System.Windows.Forms.Timer { Interval = 15 };
        drag.Tick += (_, _) =>
        {
            if ((MouseButtons & MouseButtons.Left) == 0)
            {
                drag.Stop();
                drag.Dispose();
                Config.GameOverlayPos = $"{Left},{Top}";
                return;
            }
            var cur = Cursor.Position;
            Location = new Point(cur.X - offset.X, cur.Y - offset.Y);
        };
        drag.Start();
    }

    /// <summary>Deckkraft (und künftige Optik-Einstellungen) aus der Config übernehmen.</summary>
    public void ApplySettings()
    {
        // Knapp unter 1 bleiben: erst die Ebenen-Transparenz (WS_EX_LAYERED) macht
        // das Fenster fürs Durchklicken zuverlässig "unsichtbar" für Mausklicks
        Opacity = Math.Min(Config.GameOverlayOpacity, 99) / 100.0;
    }

    // ---- Durchklicken ----

    /// <summary>
    /// WS_EX_TRANSPARENT aufs Fenster UND alle WebView2-Kind-Fenster legen (bzw.
    /// entfernen im Verschieben-Modus). Nur das Top-Level-Fenster zu markieren
    /// reicht nicht — der Treffer-Test landet sonst im Chromium-Kind-Fenster.
    /// </summary>
    private void ApplyClickThrough()
    {
        if (!IsHandleCreated) return;
        var through = !_moveMode;
        SetTransparent(Handle, through);
        EnumChildWindows(Handle, (h, _) => { SetTransparent(h, through); return true; }, IntPtr.Zero);
    }

    private static void SetTransparent(IntPtr h, bool on)
    {
        var ex = GetWindowLong(h, GWL_EXSTYLE);
        var want = on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        if (want != ex) SetWindowLong(h, GWL_EXSTYLE, want);
    }

    // ---- Position & Form ----

    private void RestorePosition()
    {
        var saved = Config.GameOverlayPos.Split(',');
        var area = Screen.PrimaryScreen.WorkingArea;
        var x = area.Right - Width - 40;
        var y = area.Top + 120;
        if (saved.Length == 2 && int.TryParse(saved[0], out var sx) && int.TryParse(saved[1], out var sy))
        { x = sx; y = sy; }

        // Auf den sichtbaren Bereich klemmen — Monitor-Setups ändern sich
        var all = SystemInformation.VirtualScreen;
        Location = new Point(
            Math.Clamp(x, all.Left, Math.Max(all.Left, all.Right - Width)),
            Math.Clamp(y, all.Top, Math.Max(all.Top, all.Bottom - Height)));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // abgerundete Ecken passend zum HUD-Design; FromHrgn kopiert, Handle danach freigeben
        var rgn = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 14, 14);
        Region = Region.FromHrgn(rgn);
        DeleteObject(rgn);
    }

}
