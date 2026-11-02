using System.Diagnostics;
using Microsoft.Win32;

namespace MusicRpc;

public sealed class MainForm : Form
{
    private Settings settings = Settings.Load();
    private readonly IMediaReader reader = new MediaReader();
    private readonly TrackSelector selector = new();
    private readonly Catalog catalog = new();
    private readonly TelegramTimeline telegramTimeline = new();
    private Task<CatalogMatch?>? coverTask;
    private string? coverKey;
    private CatalogMatch? latestCover;
    private DateTimeOffset coverAttempt;
    private readonly DiscordRpc rpc = new();
    private readonly PlaybackPublisher publisher;
    private readonly PlaybackClock playbackClock = new();
    private readonly TextBox applicationId = new() { Dock = DockStyle.Fill, PlaceholderText = "Application ID из Discord Developer Portal" };
    private readonly CheckBox telegram = new() { Text = "AyuGram / Telegram", AutoSize = true };
    private readonly CheckBox apple = new() { Text = "Apple Music для Windows", AutoSize = true };
    private readonly CheckBox autoStart = new() { Text = "Запускать при входе в Windows", AutoSize = true };
    private readonly ComboBox priority = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(660, 0), Text = "Чтение медиасессий…" };
    private readonly TextBox sessions = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly Button save = new() { Text = "Сохранить и подключить", AutoSize = true };
    private readonly Button toggle = new() { Text = "Приостановить", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly NotifyIcon tray;
    private bool busy, exiting, paused;
    private readonly bool background;
    private DateTimeOffset retryAfter;
    private string lastError = "";

    public MainForm(bool background)
    {
        this.background = background;
        publisher = new PlaybackPublisher(rpc.SetActivityAsync);
        Text = "Music RPC — AyuGram и Apple Music";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(730, 640);
        MinimumSize = new Size(650, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 11 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 10; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Add(layout, new Label { Text = "Музыка в твоём профиле Discord", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        Add(layout, new Label { Text = "1. Создай приложение Music в Developer Portal.\n2. Скопируй General Information → Application ID и вставь ниже.", AutoSize = true });
        var portal = new LinkLabel { Text = "Открыть Discord Developer Portal", AutoSize = true, Margin = new Padding(0, 6, 0, 8) };
        portal.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true });
        Add(layout, portal);
        applicationId.Text = settings.ApplicationId;
        Add(layout, applicationId);
        var sources = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4) };
        telegram.Checked = settings.Telegram; apple.Checked = settings.AppleMusic;
        sources.Controls.AddRange([telegram, apple]); Add(layout, sources);
        var priorityRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        priorityRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        priorityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        priority.Items.AddRange(["Автоматически", "Telegram / AyuGram", "Apple Music"]);
        priority.SelectedItem = settings.Priority;
        if (priority.SelectedIndex < 0) priority.SelectedIndex = 0;
        priorityRow.Controls.Add(new Label { Text = "Если играют оба:", AutoSize = true }, 0, 0);
        priorityRow.Controls.Add(priority, 1, 0); Add(layout, priorityRow);
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            autoStart.Checked = key?.GetValue("MusicRpc") != null;
        Add(layout, autoStart);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 10) };
        var hide = new Button { Text = "Свернуть в трей", AutoSize = true };
        hide.Click += (_, _) => Hide();
        save.Click += async (_, _) => await SaveAsync();
        toggle.Click += async (_, _) =>
        {
            if (busy) return;
            paused = !paused;
            toggle.Text = paused ? "Продолжить" : "Приостановить";
            await TickAsync();
        };
        actions.Controls.AddRange([save, toggle, hide]); Add(layout, actions);
        Add(layout, status);
        Add(layout, new Label { Text = "Медиасессии Windows (обновляются автоматически):", AutoSize = true, Margin = new Padding(0, 12, 0, 6) });
        Add(layout, sessions);
        Controls.Add(layout);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть настройки", null, (_, _) => ShowSettings());
        menu.Items.Add("Выход", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Music RPC", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowSettings();
        timer.Tick += async (_, _) => await TickAsync();
        Shown += async (_, _) =>
        {
            if (this.background && Settings.ValidId(settings.ApplicationId)) Hide();
            timer.Start();
            await TickAsync();
        };
        FormClosing += (_, e) =>
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            exiting = true;
            timer.Stop(); rpc.Dispose(); tray.Dispose();
        };
    }

    private static void Add(TableLayoutPanel layout, Control control) => layout.Controls.Add(control, 0, layout.Controls.Count);
    private void ShowSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private async Task SaveAsync()
    {
        if (busy) return;
        var id = applicationId.Text.Trim();
        if (!Settings.ValidId(id))
        {
            MessageBox.Show(this, "Application ID должен содержать 17–20 цифр. Скопируй его из General Information приложения в Discord Developer Portal.", "Нужен Application ID");
            return;
        }
        try
        {
            var updated = new Settings { ApplicationId = id, Telegram = telegram.Checked,
                AppleMusic = apple.Checked, Priority = priority.SelectedItem?.ToString() ?? "Автоматически" };
            updated.Save();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (autoStart.Checked) key.SetValue("MusicRpc", $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("MusicRpc", false);
            settings = updated;
            rpc.Dispose(); publisher.Reset(); retryAfter = default;
            await TickAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось сохранить настройки"); }
    }

    private async Task TickAsync()
    {
        if (busy || exiting) return;
        busy = true;
        save.Enabled = toggle.Enabled = false;
        try
        {
            var tracks = await reader.ReadAsync();
            if (exiting) return;
            sessions.Text = tracks.Count == 0
                ? "Пока нет медиасессий. Включи музыку в AyuGram или Apple Music.\r\nЕсли AyuGram не появляется, проверь настройку системных медиаклавиш в нём."
                : string.Join("\r\n\r\n", tracks.Select(t => $"{t.Source} — {(t.Playing ? "▶ играет" : "пауза / остановлено")}\r\n{t.Artist} — {t.Title}\r\nID: {t.AppId}"));
            var track = paused ? null : selector.Select(tracks, settings);
            if (!Settings.ValidId(settings.ApplicationId)) { status.Text = "Вставь Application ID, чтобы включить статус в Discord."; return; }
            if (track != null)
            {
                // Network artwork requests run independently of playback updates and pauses.
                var key = track.Artist + "\n" + track.Title + "\n" + track.Album;
                if (key != coverKey || (coverTask?.IsCompleted == true && latestCover == null
                    && DateTimeOffset.UtcNow - coverAttempt > TimeSpan.FromSeconds(10)))
                {
                    coverKey = key; latestCover = null; coverAttempt = DateTimeOffset.UtcNow;
                    coverTask = catalog.FindAsync(track);
                }
                if (coverTask?.IsCompleted == true) latestCover = await coverTask;
                if (latestCover != null) track = track with { ArtworkUrl = latestCover.ArtworkUrl, TrackUrl = latestCover.TrackUrl,
                    Album = string.IsNullOrWhiteSpace(track.Album) ? latestCover.Album : track.Album };
                track = await telegramTimeline.EnrichAsync(track, latestCover?.Duration ?? 0);
            }
            var now = DateTimeOffset.UtcNow;
            track = playbackClock.Update(track, now);
            if (!rpc.Connected)
            {
                if (now < retryAfter) return;
                await rpc.ConnectAsync(settings.ApplicationId);
                if (exiting) { rpc.Dispose(); return; }
                publisher.Reset();
            }
            await publisher.UpdateAsync(track, now);
            lastError = "";
            status.Text = paused ? "Передача приостановлена. Статус скрыт." : track == null
                ? "Discord подключён. Жду музыку; на паузе статус скрывается."
                : $"В Discord: {track.Artist} — {track.Title} ({Presence.DisplaySource(track)})\n"
                    + (track.Updated <= DateTimeOffset.UnixEpoch ? "Позиция недоступна: открой окно AyuGram для чтения плеера."
                        : $"Позиция: {TimeSpan.FromSeconds(Presence.PositionAt(track, now)):mm\\:ss} · {track.TimingSource}")
                    + (track.ArtworkUrl != null ? " · обложка найдена"
                        : coverTask?.IsCompleted == false ? " · поиск обложки…"
                        : catalog.LastError != null ? " · " + catalog.LastError : " · обложка не найдена в каталогах");
        }
        catch (Exception ex)
        {
            if (exiting) return;
            rpc.Dispose(); publisher.Reset();
            retryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
            status.Text = "Нет подключения / ошибка: " + ex.Message;
            if (lastError != ex.Message) { AppLog.Write(ex.ToString()); lastError = ex.Message; }
        }
        finally { busy = false; if (!exiting) save.Enabled = toggle.Enabled = true; }
    }
}
