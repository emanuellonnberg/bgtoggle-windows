namespace BgToggle;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private Config _config;
    private HotkeyManager _hotkeys;
    private List<RecipeIssue> _recipeIssues = new();
    private TriggerWatcher? _triggers;
    // Per-trigger stash: profile that was active when the trigger fired.
    // Keyed by Trigger.ExeName so nested triggers (launcher → game) restore
    // in LIFO order naturally — each one stashes whatever was current.
    private readonly Dictionary<string, string?> _triggerStash =
        new(StringComparer.OrdinalIgnoreCase);

    public TrayApp()
    {
        _config = LoadConfigOrDefault();
        var recipes = RecipeStore.Load();
        _recipeIssues = RecipeValidator.Validate(recipes);
        if (_recipeIssues.Count > 0)
        {
            Logger.Info($"Recipe validator found {_recipeIssues.Count} issue(s):");
            foreach (var i in _recipeIssues) Logger.Info($"  {i}");
        }

        _icon = new NotifyIcon
        {
            Icon = IconFactory.Create(active: true),
            Text = "BgToggle",
            Visible = true
        };

        ProcessManager.Helper = new HelperClient(HelperProtocol.PipeName,
            () => ServiceManager.ProcessId(HelperProtocol.ServiceName));

        _hotkeys = new HotkeyManager();
        RebuildMenu();
        WarnIfElevated();
        StartUpdateChecks();
        RegisterHotkeys();
        StartTriggerWatcher();
    }

    /// <summary>
    /// A hand-edited config with a typo must not keep the app from starting.
    /// Back the broken file up first: the next Save overwrites it.
    /// </summary>
    private static Config LoadConfigOrDefault()
    {
        try
        {
            return ConfigStore.Load();
        }
        catch (ConfigLoadException ex)
        {
            Logger.Error("Config load failed at startup; using defaults", ex);
            var backup = ConfigStore.BackupBroken(ex.Path);
            var where = backup is null
                ? "The broken file could not be backed up and will be overwritten on the next save."
                : $"The broken file was copied to:\n{backup}";
            MessageBox.Show(
                $"{ex.Message}\n\nStarting with an empty default config. {where}",
                "BgToggle — config error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return ConfigStore.Default();
        }
    }

    /// <summary>
    /// Anything BgToggle launches inherits its token, so an elevated tray
    /// starts Spotify & co. as admin. The installer never does this; say so
    /// if someone ran it that way by hand.
    /// </summary>
    private void WarnIfElevated()
    {
        if (!Elevation.IsElevated()) return;
        Logger.Warn("BgToggle is running as administrator; apps it starts will be too");
        _icon.ShowBalloonTip(5000, "BgToggle",
            "BgToggle is running as administrator; apps it starts will be too.", ToolTipIcon.Warning);
    }

    // --- update check ---

    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 30_000 };
    private LatestRelease? _availableUpdate;
    private string? _balloonUrl;

    private void StartUpdateChecks()
    {
        _icon.BalloonTipClicked += (_, _) => { if (_balloonUrl is { } url) OpenUrl(url); };
        _icon.BalloonTipClosed += (_, _) => _balloonUrl = null;
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = 60 * 60 * 1000;   // first tick 30 s after start, then hourly
            await CheckForUpdateAsync();
        };
        _updateTimer.Start();
    }

    private async Task CheckForUpdateAsync()
    {
        var current = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
        if (!_config.CheckForUpdates || UpdateCheck.IsDevBuild(current)) return;

        var state = AppStateStore.Load();
        if (!UpdateCheck.ShouldCheck(state.LastUpdateCheckUtc, DateTime.UtcNow)) return;

        LatestRelease? latest = null;
        try { latest = await Task.Run(() => UpdateCheck.FetchLatestAsync(current)); }
        catch (Exception ex) { Logger.Info($"Update check failed: {ex.GetType().Name}: {ex.Message}"); }

        // Record every attempt, failed or not, so a bad network doesn't mean
        // hitting GitHub every hour.
        state = state with { LastUpdateCheckUtc = DateTime.UtcNow };
        if (latest is not null && UpdateCheck.IsNewer(latest, current))
        {
            Logger.Info($"Update available: {latest.TagName}");
            _availableUpdate = latest;
            RebuildMenu();
            if (state.LastNotifiedVersion != latest.TagName)
            {
                _balloonUrl = latest.HtmlUrl;
                _icon.ShowBalloonTip(5000, "BgToggle", $"Version {latest.TagName} is available. Click to open the release page.", ToolTipIcon.Info);
                state = state with { LastNotifiedVersion = latest.TagName };
            }
        }
        try { AppStateStore.Save(state); }
        catch (Exception ex) { Logger.Error("Saving app state failed", ex); }
    }

    private void StartTriggerWatcher()
    {
        _triggers?.Dispose();
        _triggers = new TriggerWatcher(
            triggersProvider: () => (IReadOnlyList<Trigger>)(_config.Triggers ?? new()),
            isRunning: ProcessManager.IsRunning,
            onEvent: HandleTriggerEvent
        );
        _triggers.Start();
    }

    private void HandleTriggerEvent(Trigger trigger, TriggerEvent ev)
    {
        var targetProfileName = ev == TriggerEvent.Running
            ? trigger.WhileRunningProfile
            : trigger.OnExitProfile ?? GetStashedProfile(trigger.ExeName);

        if (string.IsNullOrEmpty(targetProfileName))
        {
            Logger.Info($"Trigger '{trigger.ExeName}' {ev}: no target profile (stash empty); skipping");
            return;
        }
        if (string.Equals(targetProfileName, _config.ActiveProfile, StringComparison.Ordinal))
        {
            Logger.Info($"Trigger '{trigger.ExeName}' {ev}: target profile '{targetProfileName}' already active; skipping");
            return;
        }

        var profile = _config.Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, targetProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            Logger.Warn($"Trigger '{trigger.ExeName}' {ev}: profile '{targetProfileName}' not found");
            return;
        }

        if (ev == TriggerEvent.Running)
            _triggerStash[trigger.ExeName] = _config.ActiveProfile;
        else
            _triggerStash.Remove(trigger.ExeName);

        Logger.Info($"Trigger '{trigger.ExeName}' {ev} → applying profile '{profile.Name}'");
        _icon.ShowBalloonTip(2000, "BgToggle",
            $"{trigger.ExeName} {(ev == TriggerEvent.Running ? "started" : "exited")} → {profile.Name}",
            ToolTipIcon.Info);
        ApplyProfile(profile, isTriggerDriven: true);
    }

    private string? GetStashedProfile(string exeName) =>
        _triggerStash.TryGetValue(exeName, out var v) ? v : null;

    private void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        if (_availableUpdate is { } update)
        {
            menu.Items.Add(new ToolStripMenuItem($"Update available: {update.TagName}…", null, (_, _) => OpenUrl(update.HtmlUrl))
            { Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());
        }

        if (_config.Profiles.Count > 0)
        {
            var profilesItem = new ToolStripMenuItem("Profiles");
            foreach (var profile in _config.Profiles)
            {
                var label = string.IsNullOrEmpty(profile.Hotkey)
                    ? profile.Name
                    : $"{profile.Name}\t{profile.Hotkey}";
                var item = new ToolStripMenuItem(label) { Checked = profile.Name == _config.ActiveProfile };
                var captured = profile;
                item.Click += (_, _) => ApplyProfile(captured);
                profilesItem.DropDownItems.Add(item);
            }
            menu.Items.Add(profilesItem);
            menu.Items.Add(new ToolStripSeparator());
        }

        if (_config.Apps.Count > 0)
        {
            var appsItem = new ToolStripMenuItem("Apps");
            var snapshot = ProcessSnapshot.Take();
            foreach (var app in _config.Apps)
            {
                var resolved = AppResolver.Resolve(app);
                var running = ProcessManager.IsRunning(resolved, snapshot);
                var label = AppLabel(app.DisplayName, resolved.RequiresAdmin, ProcessManager.Helper?.Status);
                var item = new ToolStripMenuItem(label) { Checked = running, Tag = app.Id };
                if (!string.IsNullOrEmpty(resolved.Notes))
                    item.ToolTipText = resolved.Notes;
                var captured = resolved;
                item.Click += (_, _) => ToggleApp(captured);
                appsItem.DropDownItems.Add(item);
            }
            menu.Items.Add(appsItem);
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Scan running processes…", null, (_, _) => RunScanner());
        menu.Items.Add("Suggest recipe from running…", null, (_, _) => SuggestRecipe());
        menu.Items.Add("Manage profiles…", null, (_, _) => ManageProfiles());
        menu.Items.Add("Manage apps…", null, (_, _) => ManageApps());
        menu.Items.Add("Triggers…", null, (_, _) => ManageTriggers());

        var sysMenu = new ToolStripMenuItem("System");
        var helperItem = new ToolStripMenuItem(HelperStatusText(), null, (_, _) => OpenUrl(HelperHelpUrl));
        sysMenu.DropDownItems.Add(helperItem);
        sysMenu.DropDownItems.Add(new ToolStripSeparator());
        var startAtLogon = new ToolStripMenuItem("Start BgToggle at logon", null, (_, _) => ToggleStartAtLogon())
        { Checked = SafeIsStartAtLogon() };
        var restoreAll = new ToolStripMenuItem("Restore all autostart entries", null, (_, _) => RestoreAllAutostart());
        var openLogs = new ToolStripMenuItem("Open logs folder", null, (_, _) => OpenFolder(Logger.LogDir));
        sysMenu.DropDownItems.Add(startAtLogon);
        sysMenu.DropDownItems.Add(restoreAll);
        sysMenu.DropDownItems.Add(new ToolStripSeparator());
        sysMenu.DropDownItems.Add(openLogs);
        menu.Items.Add(sysMenu);

        if (_recipeIssues.Count > 0)
        {
            var issuesItem = new ToolStripMenuItem($"Recipe issues: {_recipeIssues.Count}", null,
                (_, _) => ShowRecipeIssues())
            { ForeColor = Color.DarkOrange };
            menu.Items.Add(issuesItem);
        }

        menu.Items.Add("Edit config…", null, (_, _) => OpenConfigInEditor());
        menu.Items.Add("Reload config", null, (_, _) => Reload());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        menu.Opening += (_, _) =>
        {
            RefreshAppCheckmarks(menu);
            helperItem.Text = HelperStatusText();
        };
        _icon.ContextMenuStrip = menu;
    }

    private void RefreshAppCheckmarks(ContextMenuStrip menu)
    {
        foreach (ToolStripItem topItem in menu.Items)
        {
            if (topItem is ToolStripMenuItem { Text: "Apps" } appsItem)
            {
                var snapshot = ProcessSnapshot.Take();
                foreach (ToolStripItem sub in appsItem.DropDownItems)
                {
                    if (sub is ToolStripMenuItem mi && mi.Tag is string id)
                    {
                        var app = _config.Apps.FirstOrDefault(a => a.Id == id);
                        if (app is not null)
                        {
                            var resolved = AppResolver.Resolve(app);
                            mi.Checked = ProcessManager.IsRunning(resolved, snapshot);
                        }
                    }
                }
            }
        }
    }

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        foreach (var p in _config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Hotkey)) continue;
            var captured = p;
            _hotkeys.Register(p.Hotkey, () => ApplyProfile(captured));
        }
    }

    private void ApplyProfile(Profile profile) => ApplyProfile(profile, isTriggerDriven: false);

    // Applies and toggles run off the UI thread: graceful waits and service
    // stops can take tens of seconds, which used to freeze the tray, hotkeys
    // and trigger polling. One runs at a time; a profile requested meanwhile
    // is queued (latest wins) and applied when the current one finishes.
    private bool _busy;
    private (string ProfileName, bool IsTriggerDriven)? _queued;

    private async void ApplyProfile(Profile profile, bool isTriggerDriven)
    {
        if (_busy)
        {
            Logger.Info($"Busy; queued profile '{profile.Name}'");
            _queued = (profile.Name, isTriggerDriven);
            return;
        }

        // Busy from here on: the confirm dialog pumps messages, so a hotkey
        // or trigger could otherwise start a second apply underneath it.
        _busy = true;
        try
        {
            var diff = ProfileApplier.Diff(profile, _config);
            var changes = diff.ToStart.Count + diff.ToStop.Count;

            // Trigger-driven applies skip the confirm dialog — the user is in a
            // game and can't see / click a UAC-style prompt under fullscreen.
            if (!isTriggerDriven && _config.ConfirmLargeDiffs && changes >= _config.LargeDiffConfirmThreshold)
            {
                var summary = BuildDiffSummary(diff);
                var ok = MessageBox.Show(
                    $"Applying \"{profile.Name}\" will make {changes} changes:\n\n{summary}\n\nContinue?",
                    "BgToggle — confirm profile apply",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok != DialogResult.Yes) return;
            }

            // Record the target up front so a trigger firing mid-apply
            // stashes the profile we are switching to, not the old one.
            _config = _config with { ActiveProfile = profile.Name };
            ConfigStore.Save(_config);

            _icon.Text = $"BgToggle — applying {profile.Name}…";
            Logger.Info($"Applying profile '{profile.Name}' (start {diff.ToStart.Count}, stop {diff.ToStop.Count})");
            await Task.Run(() => ProfileApplier.Apply(diff, msg => Logger.Info(msg)));
            _icon.ShowBalloonTip(2000, "BgToggle", $"Profile applied: {profile.Name}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Logger.Error($"ApplyProfile '{profile.Name}' threw", ex);
            _icon.ShowBalloonTip(3000, "BgToggle", $"Apply failed: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            _icon.Text = "BgToggle";
            RebuildMenu();
            RunQueued();
        }
    }

    private void RunQueued()
    {
        if (_queued is not { } next) return;
        _queued = null;

        // Look the profile up again: it may have been edited while queued.
        var profile = _config.Profiles.FirstOrDefault(p => p.Name == next.ProfileName);
        if (profile is null)
        {
            Logger.Warn($"Queued profile '{next.ProfileName}' no longer exists; skipping");
            return;
        }
        ApplyProfile(profile, next.IsTriggerDriven);
    }

    private static string BuildDiffSummary(DiffResult diff)
    {
        var sb = new System.Text.StringBuilder();
        if (diff.ToStop.Count > 0)
        {
            sb.AppendLine($"Stop ({diff.ToStop.Count}):");
            foreach (var a in diff.ToStop.Take(10)) sb.AppendLine($"  • {a.DisplayName}");
            if (diff.ToStop.Count > 10) sb.AppendLine($"  …and {diff.ToStop.Count - 10} more");
        }
        if (diff.ToStart.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"Start ({diff.ToStart.Count}):");
            foreach (var a in diff.ToStart.Take(10)) sb.AppendLine($"  • {a.DisplayName}");
            if (diff.ToStart.Count > 10) sb.AppendLine($"  …and {diff.ToStart.Count - 10} more");
        }
        return sb.ToString().TrimEnd();
    }

    private async void ToggleApp(ResolvedApp app)
    {
        if (_busy)
        {
            _icon.ShowBalloonTip(2000, "BgToggle", "Busy applying a profile; try again in a moment.", ToolTipIcon.Info);
            return;
        }

        _busy = true;
        try
        {
            await Task.Run(() =>
            {
                if (ProcessManager.IsRunning(app))
                    ProcessManager.Stop(app, msg => Logger.Info(msg));
                else
                    ProcessManager.Start(app, msg => Logger.Info(msg));
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Toggle '{app.DisplayName}' threw", ex);
        }
        finally
        {
            _busy = false;
            RunQueued();
        }
    }

    private void RunScanner()
    {
        var existingIds = _config.Apps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = RecipeScanner.Scan().Where(m => !existingIds.Contains(m.Recipe.Id)).ToList();

        if (matches.Count == 0)
        {
            MessageBox.Show("No new known apps detected (or all already in config).",
                "BgToggle scan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new ScanDialog(matches);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        foreach (var picked in dlg.Selected)
        {
            _config.Apps.Add(new AppDefinition(
                Id: picked.Recipe.Id,
                DisplayName: picked.Recipe.DisplayName,
                ExeName: picked.Recipe.ProcessNames.FirstOrDefault() ?? "",
                LaunchPath: picked.DetectedLaunchPath,
                RecipeId: picked.Recipe.Id
            ));
        }
        ConfigStore.Save(_config);
        RebuildMenu();
    }

    private void SuggestRecipe()
    {
        Cursor.Current = Cursors.WaitCursor;
        List<ProcessCandidate> candidates;
        try { candidates = ProcessSuggester.Suggest(); }
        finally { Cursor.Current = Cursors.Default; }

        if (candidates.Count == 0)
        {
            MessageBox.Show(
                "No unmatched user processes found. Either everything running is already covered by a recipe, or all candidates are Windows-internal.",
                "BgToggle", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new RecipeSuggestDialog(candidates);
        dlg.ShowDialog();
        // After save, force a recipe reload so saved local recipes appear.
        RecipeStore.Load(forceReload: true);
        RebuildMenu();
    }

    private void ManageProfiles()
    {
        using var dlg = new ProfileEditor(_config.Profiles, _config.Apps);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var newProfiles = dlg.Result.ToList();
        var active = _config.ActiveProfile;
        if (active is not null && !newProfiles.Any(p => p.Name == active)) active = null;

        _config = _config with { Profiles = newProfiles, ActiveProfile = active };
        ConfigStore.Save(_config);
        RegisterHotkeys();
        RebuildMenu();
    }

    private void ManageApps()
    {
        using var dlg = new AppEditor(_config.Apps, RecipeStore.Load());
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var newApps = dlg.Result.ToList();
        var newIds = newApps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Profiles may reference removed apps — strip dangling ids.
        var newProfiles = _config.Profiles
            .Select(p => p with { AppIds = new HashSet<string>(p.AppIds.Where(id => newIds.Contains(id))) })
            .ToList();

        _config = _config with { Apps = newApps, Profiles = newProfiles };
        ConfigStore.Save(_config);
        RebuildMenu();
    }

    private void ManageTriggers()
    {
        var existing = _config.Triggers ?? new List<Trigger>();
        using var dlg = new TriggerEditor(existing, _config.Profiles.Select(p => p.Name));
        if (dlg.ShowDialog() != DialogResult.OK) return;

        _config = _config with { Triggers = dlg.Result.ToList() };
        ConfigStore.Save(_config);
        // Re-prime so newly added triggers don't fire for already-running apps.
        _triggers?.Prime();
        Logger.Info($"Saved {_config.Triggers!.Count} trigger(s); re-primed watcher");
    }

    private static bool SafeIsStartAtLogon()
    {
        try { return TrayAutostart.IsEnabled(); }
        catch (Exception ex) { Logger.Error("Reading start-at-logon failed", ex); return false; }
    }

    private void ToggleStartAtLogon()
    {
        try
        {
            var enable = !TrayAutostart.IsEnabled();
            TrayAutostart.Set(enable, Application.ExecutablePath);
            Logger.Info($"Start at logon {(enable ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            Logger.Error("Changing start-at-logon failed", ex);
        }
        RebuildMenu();
    }

    private void RestoreAllAutostart()
    {
        try
        {
            var n = AutostartManager.RestoreAllForCurrentUser();
            Logger.Info($"Restored {n} autostart entr{(n == 1 ? "y" : "ies")}");
            _icon.ShowBalloonTip(2000, "BgToggle", $"Restored {n} autostart entr{(n == 1 ? "y" : "ies")}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Logger.Error("Restore all autostart entries failed", ex);
            _icon.ShowBalloonTip(3000, "BgToggle", $"Restore failed: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void ShowRecipeIssues()
    {
        var text = string.Join(Environment.NewLine, _recipeIssues.Select(i => i.ToString()));
        MessageBox.Show(text, "Recipe issues", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private const string HelperHelpUrl = "https://github.com/emanuellonnberg/bgtoggle-windows#helper-service";

    /// <summary>
    /// "(admin)" only matters when nothing can act with admin rights for the
    /// user; with the helper running it is noise.
    /// </summary>
    internal static string AppLabel(string displayName, bool requiresAdmin, HelperStatus? helperStatus) =>
        requiresAdmin && helperStatus != HelperStatus.Running ? $"{displayName}  (admin)" : displayName;

    private static string HelperStatusText() => ProcessManager.Helper?.Status switch
    {
        HelperStatus.Running => "Helper: running",
        HelperStatus.Incompatible => "Helper: incompatible version (reinstall)",
        _ => "Helper: not installed"
    };

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.Error($"OpenUrl({url}) failed", ex); }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Error($"OpenFolder({path}) failed", ex); }
    }

    private static void OpenConfigInEditor()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ConfigStore.ConfigPath) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No .json file association on this machine.
            System.Diagnostics.Process.Start("notepad.exe", $"\"{ConfigStore.ConfigPath}\"");
        }
    }

    private void Reload()
    {
        Config loaded;
        try
        {
            loaded = ConfigStore.Load();
        }
        catch (ConfigLoadException ex)
        {
            Logger.Error("Reload failed; keeping current config", ex);
            MessageBox.Show($"{ex.Message}\n\nFix the file and reload again. The current config stays active.",
                "BgToggle — config error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _config = loaded;
        var recipes = RecipeStore.Load(forceReload: true);
        _recipeIssues = RecipeValidator.Validate(recipes);
        RegisterHotkeys();
        // Re-prime so triggers added or re-enabled in the file don't fire on
        // the next poll for apps that are simply already running (or not).
        _triggers?.Prime();
        RebuildMenu();
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        var ico = _icon.Icon;
        _icon.Dispose();
        ico?.Dispose();
        _hotkeys.Dispose();
        _updateTimer.Dispose();
        _triggers?.Dispose();
        base.ExitThreadCore();
    }
}
