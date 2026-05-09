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
        _config = ConfigStore.Load();
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

        _hotkeys = new HotkeyManager();
        RebuildMenu();
        RegisterHotkeys();
        StartTriggerWatcher();
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
            foreach (var app in _config.Apps)
            {
                var resolved = AppResolver.Resolve(app);
                var running = ProcessManager.IsRunning(resolved.ProcessNames);
                var label = resolved.RequiresAdmin ? $"{app.DisplayName}  (admin)" : app.DisplayName;
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
        var installAuto = new ToolStripMenuItem(
            AutostartTask.IsInstalled() ? "Reinstall autostart (admin)…" : "Install autostart (admin)…",
            null, (_, _) => InstallAutostartTask());
        var removeAuto = new ToolStripMenuItem("Remove autostart…", null, (_, _) => UninstallAutostartTask())
        { Enabled = AutostartTask.IsInstalled() };
        var openLogs = new ToolStripMenuItem("Open logs folder", null, (_, _) => OpenFolder(Logger.LogDir));
        sysMenu.DropDownItems.Add(installAuto);
        sysMenu.DropDownItems.Add(removeAuto);
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

        menu.Opening += (_, _) => RefreshAppCheckmarks(menu);
        _icon.ContextMenuStrip = menu;
    }

    private void RefreshAppCheckmarks(ContextMenuStrip menu)
    {
        foreach (ToolStripItem topItem in menu.Items)
        {
            if (topItem is ToolStripMenuItem { Text: "Apps" } appsItem)
            {
                foreach (ToolStripItem sub in appsItem.DropDownItems)
                {
                    if (sub is ToolStripMenuItem mi && mi.Tag is string id)
                    {
                        var app = _config.Apps.FirstOrDefault(a => a.Id == id);
                        if (app is not null)
                        {
                            var resolved = AppResolver.Resolve(app);
                            mi.Checked = ProcessManager.IsRunning(resolved.ProcessNames);
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

    private void ApplyProfile(Profile profile, bool isTriggerDriven)
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

        _icon.Text = $"BgToggle — applying {profile.Name}…";
        try
        {
            Logger.Info($"Applying profile '{profile.Name}' (start {diff.ToStart.Count}, stop {diff.ToStop.Count})");
            ProfileApplier.Apply(profile, _config, msg => Logger.Info(msg));
            _config = _config with { ActiveProfile = profile.Name };
            ConfigStore.Save(_config);
            _icon.ShowBalloonTip(2000, "BgToggle", $"Profile applied: {profile.Name}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Logger.Error($"ApplyProfile '{profile.Name}' threw", ex);
            _icon.ShowBalloonTip(3000, "BgToggle", $"Apply failed: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _icon.Text = "BgToggle";
            RebuildMenu();
        }
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

    private void ToggleApp(ResolvedApp app)
    {
        if (ProcessManager.IsRunning(app.ProcessNames))
            ProcessManager.Stop(app, msg => Logger.Info(msg));
        else
            ProcessManager.Launch(app);
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

    private void InstallAutostartTask()
    {
        var exe = Application.ExecutablePath;
        var ok = AutostartTask.Install(exe);
        if (ok)
        {
            Logger.Info($"Scheduled task installed for {exe}");
            MessageBox.Show("BgToggle will now start automatically with admin rights at logon.",
                "BgToggle", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RebuildMenu();
        }
        else
        {
            MessageBox.Show("Could not install scheduled task. Did you accept the UAC prompt?",
                "BgToggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UninstallAutostartTask()
    {
        var ok = AutostartTask.Uninstall();
        if (ok)
        {
            Logger.Info("Scheduled task removed");
            RebuildMenu();
        }
        else
        {
            MessageBox.Show("Could not remove scheduled task.",
                "BgToggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowRecipeIssues()
    {
        var text = string.Join(Environment.NewLine, _recipeIssues.Select(i => i.ToString()));
        MessageBox.Show(text, "Recipe issues", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ConfigStore.ConfigPath) { UseShellExecute = true });
    }

    private void Reload()
    {
        _config = ConfigStore.Load();
        var recipes = RecipeStore.Load(forceReload: true);
        _recipeIssues = RecipeValidator.Validate(recipes);
        RegisterHotkeys();
        RebuildMenu();
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        var ico = _icon.Icon;
        _icon.Dispose();
        ico?.Dispose();
        _hotkeys.Dispose();
        _triggers?.Dispose();
        base.ExitThreadCore();
    }
}
