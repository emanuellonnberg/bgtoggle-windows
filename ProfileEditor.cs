namespace BgToggle;

/// <summary>
/// Manages profiles without leaving the app: create / rename / delete on the
/// left, app membership checklist on the right. Edits are held in memory and
/// only persisted when the user clicks Save.
/// </summary>
public class ProfileEditor : Form
{
    private readonly List<Profile> _profiles;
    private readonly List<AppDefinition> _apps;

    private readonly ListBox _profileList;
    private readonly CheckedListBox _appList;
    private readonly Button _newBtn, _renameBtn, _deleteBtn, _saveBtn, _cancelBtn;
    private readonly Label _hint;

    private int _currentIndex = -1;
    private bool _suppressItemCheck;

    public IReadOnlyList<Profile> Result => _profiles;

    public ProfileEditor(IEnumerable<Profile> profiles, IEnumerable<AppDefinition> apps)
    {
        // Deep-copy profiles so Cancel really cancels.
        _profiles = profiles.Select(p => new Profile(p.Name, new HashSet<string>(p.AppIds))).ToList();
        _apps = apps.ToList();

        Text = "BgToggle — Manage profiles";
        Width = 720;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 240,
            Panel1MinSize = 200
        };

        // --- left: profile list + buttons ---
        _profileList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _profileList.SelectedIndexChanged += (_, _) => OnProfileSelected();

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 36,
            Padding = new Padding(4)
        };
        _newBtn = new Button { Text = "New", Width = 64 };
        _renameBtn = new Button { Text = "Rename", Width = 72 };
        _deleteBtn = new Button { Text = "Delete", Width = 64 };
        _newBtn.Click += (_, _) => NewProfile();
        _renameBtn.Click += (_, _) => RenameProfile();
        _deleteBtn.Click += (_, _) => DeleteProfile();
        leftButtons.Controls.Add(_newBtn);
        leftButtons.Controls.Add(_renameBtn);
        leftButtons.Controls.Add(_deleteBtn);

        split.Panel1.Controls.Add(_profileList);
        split.Panel1.Controls.Add(leftButtons);

        // --- right: hint + app checklist ---
        _hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(8, 8, 8, 0),
            Text = "Apps to keep running for this profile:"
        };
        _appList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false
        };
        foreach (var a in _apps) _appList.Items.Add(a.DisplayName, false);
        _appList.ItemCheck += OnAppItemCheck;

        split.Panel2.Controls.Add(_appList);
        split.Panel2.Controls.Add(_hint);

        // --- bottom: save / cancel ---
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            Height = 48
        };
        _saveBtn = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 96 };
        _cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        bottom.Controls.Add(_saveBtn);
        bottom.Controls.Add(_cancelBtn);

        AcceptButton = _saveBtn;
        CancelButton = _cancelBtn;

        Controls.Add(split);
        Controls.Add(bottom);

        ReloadProfileList(selectIndex: _profiles.Count > 0 ? 0 : -1);
    }

    private void ReloadProfileList(int selectIndex)
    {
        _profileList.BeginUpdate();
        _profileList.Items.Clear();
        foreach (var p in _profiles) _profileList.Items.Add(p.Name);
        _profileList.EndUpdate();

        if (selectIndex >= 0 && selectIndex < _profiles.Count)
            _profileList.SelectedIndex = selectIndex;
        else
            ClearAppChecks();
    }

    private void OnProfileSelected()
    {
        _currentIndex = _profileList.SelectedIndex;
        if (_currentIndex < 0) { ClearAppChecks(); return; }

        var profile = _profiles[_currentIndex];
        _suppressItemCheck = true;
        for (int i = 0; i < _apps.Count; i++)
            _appList.SetItemChecked(i, profile.AppIds.Contains(_apps[i].Id));
        _suppressItemCheck = false;
        _hint.Text = $"Apps to keep running for \"{profile.Name}\":";
    }

    private void ClearAppChecks()
    {
        _suppressItemCheck = true;
        for (int i = 0; i < _appList.Items.Count; i++)
            _appList.SetItemChecked(i, false);
        _suppressItemCheck = false;
        _hint.Text = "Select or create a profile.";
    }

    private void OnAppItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppressItemCheck || _currentIndex < 0) return;
        var appId = _apps[e.Index].Id;
        var set = _profiles[_currentIndex].AppIds;
        if (e.NewValue == CheckState.Checked) set.Add(appId);
        else set.Remove(appId);
    }

    private void NewProfile()
    {
        var name = Prompt("New profile name:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A profile with that name already exists.", "BgToggle",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _profiles.Add(new Profile(name, new HashSet<string>()));
        ReloadProfileList(selectIndex: _profiles.Count - 1);
    }

    private void RenameProfile()
    {
        if (_currentIndex < 0) return;
        var current = _profiles[_currentIndex];
        var name = Prompt("Rename profile:", current.Name);
        if (string.IsNullOrWhiteSpace(name) || name == current.Name) return;
        if (_profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A profile with that name already exists.", "BgToggle",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _profiles[_currentIndex] = current with { Name = name };
        ReloadProfileList(selectIndex: _currentIndex);
    }

    private void DeleteProfile()
    {
        if (_currentIndex < 0) return;
        var current = _profiles[_currentIndex];
        var ok = MessageBox.Show($"Delete profile \"{current.Name}\"?", "BgToggle",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ok != DialogResult.Yes) return;
        _profiles.RemoveAt(_currentIndex);
        var next = Math.Min(_currentIndex, _profiles.Count - 1);
        ReloadProfileList(selectIndex: next);
    }

    /// <summary>Tiny inline prompt — WinForms has no built-in InputBox.</summary>
    private static string Prompt(string label, string initial)
    {
        using var dlg = new Form
        {
            Width = 360,
            Height = 150,
            Text = "BgToggle",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var lbl = new Label { Left = 12, Top = 12, Width = 320, Text = label };
        var box = new TextBox { Left = 12, Top = 36, Width = 320, Text = initial };
        var ok = new Button { Text = "OK", Left = 176, Top = 72, Width = 70, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 252, Top = 72, Width = 70, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        return dlg.ShowDialog() == DialogResult.OK ? box.Text.Trim() : "";
    }
}
