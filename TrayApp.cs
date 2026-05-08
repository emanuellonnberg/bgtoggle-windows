using System.Diagnostics;

namespace BgToggle;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private Config _config;

    public TrayApp()
    {
        _config = ConfigStore.Load();
        // Eager-load recipes so missing recipes.json shows up in Debug early.
        RecipeStore.Load();

        _icon = new NotifyIcon
        {
            Icon = IconFactory.Create(active: true),
            Text = "BgToggle",
            Visible = true
        };
        RebuildMenu();
    }

    private void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        if (_config.Profiles.Count > 0)
        {
            var profilesItem = new ToolStripMenuItem("Profiles");
            foreach (var profile in _config.Profiles)
            {
                var item = new ToolStripMenuItem(profile.Name)
                {
                    Checked = profile.Name == _config.ActiveProfile
                };
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
        menu.Items.Add("Manage profiles…", null, (_, _) => ManageProfiles());
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

    private void ApplyProfile(Profile profile)
    {
        _icon.Text = $"BgToggle — applying {profile.Name}…";
        try
        {
            ProfileApplier.Apply(profile, _config, msg => Debug.WriteLine(msg));
            _config = _config with { ActiveProfile = profile.Name };
            ConfigStore.Save(_config);
            _icon.ShowBalloonTip(2000, "BgToggle", $"Profile applied: {profile.Name}", ToolTipIcon.Info);
        }
        finally
        {
            _icon.Text = "BgToggle";
            RebuildMenu();
        }
    }

    private void ToggleApp(ResolvedApp app)
    {
        if (ProcessManager.IsRunning(app.ProcessNames))
            ProcessManager.Stop(app, msg => Debug.WriteLine(msg));
        else
            ProcessManager.Launch(app);
    }

    private void RunScanner()
    {
        var existingIds = _config.Apps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = RecipeScanner.Scan()
            .Where(m => !existingIds.Contains(m.Recipe.Id))
            .ToList();

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

    private void ManageProfiles()
    {
        using var dlg = new ProfileEditor(_config.Profiles, _config.Apps);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var newProfiles = dlg.Result.ToList();
        // If the previously active profile got renamed/deleted, clear ActiveProfile.
        var active = _config.ActiveProfile;
        if (active is not null && !newProfiles.Any(p => p.Name == active))
            active = null;

        _config = _config with { Profiles = newProfiles, ActiveProfile = active };
        ConfigStore.Save(_config);
        RebuildMenu();
    }

    private static void OpenConfigInEditor()
    {
        Process.Start(new ProcessStartInfo(ConfigStore.ConfigPath) { UseShellExecute = true });
    }

    private void Reload()
    {
        _config = ConfigStore.Load();
        RecipeStore.Load(forceReload: true);
        RebuildMenu();
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        var ico = _icon.Icon;
        _icon.Dispose();
        ico?.Dispose();
        base.ExitThreadCore();
    }
}
