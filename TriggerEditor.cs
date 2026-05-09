namespace BgToggle;

/// <summary>
/// Manage triggers: when an exe starts, switch to a profile; when it
/// exits, restore (or switch to a different profile). Edits are held in
/// memory and only persisted on Save.
/// </summary>
public class TriggerEditor : Form
{
    private readonly List<Trigger> _triggers;
    private readonly string[] _profileNames;

    private readonly ListBox _list;
    private readonly TextBox _exeBox;
    private readonly ComboBox _whileCombo, _onExitCombo;
    private readonly CheckBox _enabledCheck;
    private readonly Button _addBtn, _removeBtn;

    private int _currentIndex = -1;
    private bool _suppress;

    public IReadOnlyList<Trigger> Result => _triggers;

    public TriggerEditor(IEnumerable<Trigger> triggers, IEnumerable<string> profileNames)
    {
        _triggers = triggers.Select(t => t with { }).ToList();
        _profileNames = profileNames.ToArray();

        Text = "BgToggle — Triggers";
        Width = 760;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 280,
            Panel1MinSize = 240
        };

        // --- left ---
        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _list.SelectedIndexChanged += (_, _) => OnSelectionChanged();

        var leftBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 36,
            Padding = new Padding(4)
        };
        _addBtn = new Button { Text = "Add", Width = 64 };
        _removeBtn = new Button { Text = "Remove", Width = 72 };
        _addBtn.Click += (_, _) => AddTrigger();
        _removeBtn.Click += (_, _) => RemoveTrigger();
        leftBtns.Controls.Add(_addBtn);
        leftBtns.Controls.Add(_removeBtn);

        split.Panel1.Controls.Add(_list);
        split.Panel1.Controls.Add(leftBtns);

        // --- right (edit panel) ---
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true };
        int y = 8, lblW = 150, fieldW = 330;
        Label L(string text) { var l = new Label { Left = 0, Top = y + 4, Width = lblW, Text = text }; panel.Controls.Add(l); return l; }
        TextBox T() { var t = new TextBox { Left = lblW, Top = y, Width = fieldW }; panel.Controls.Add(t); y += 32; return t; }
        ComboBox C(IEnumerable<string> items) { var c = new ComboBox { Left = lblW, Top = y, Width = fieldW, DropDownStyle = ComboBoxStyle.DropDownList }; foreach (var i in items) c.Items.Add(i); panel.Controls.Add(c); y += 32; return c; }

        L("Exe name (no .exe):"); _exeBox = T();
        var exeHint = new Label
        {
            Left = lblW, Top = y, Width = fieldW, Height = 18,
            ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8.25f),
            Text = "e.g. Cyberpunk2077, eldenring, csgo. Match is case-insensitive."
        };
        panel.Controls.Add(exeHint); y += 24;

        L("While running, apply:"); _whileCombo = C(_profileNames);
        L("On exit, apply:");       _onExitCombo = C(new[] { "(restore previous)" }.Concat(_profileNames));
        var onExitHint = new Label
        {
            Left = lblW, Top = y, Width = fieldW, Height = 18,
            ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 8.25f),
            Text = "\"Restore previous\" returns to whatever profile was active before the trigger fired."
        };
        panel.Controls.Add(onExitHint); y += 24;

        _enabledCheck = new CheckBox { Left = lblW, Top = y, Width = fieldW, Text = "Enabled" };
        panel.Controls.Add(_enabledCheck); y += 32;

        _exeBox.TextChanged += (_, _) => Update(t => t with { ExeName = StripExe(_exeBox.Text.Trim()) });
        _whileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_whileCombo.SelectedItem is string s)
                Update(t => t with { WhileRunningProfile = s });
        };
        _onExitCombo.SelectedIndexChanged += (_, _) =>
        {
            var sel = _onExitCombo.SelectedItem as string;
            Update(t => t with { OnExitProfile = (sel == "(restore previous)") ? null : sel });
        };
        _enabledCheck.CheckedChanged += (_, _) => Update(t => t with { Enabled = _enabledCheck.Checked });

        split.Panel2.Controls.Add(panel);

        // --- bottom ---
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 96 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            Height = 48
        };
        bottom.Controls.Add(save);
        bottom.Controls.Add(cancel);

        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(split);
        Controls.Add(bottom);

        ReloadList(_triggers.Count > 0 ? 0 : -1);
    }

    private static string StripExe(string s) =>
        s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;

    private void ReloadList(int select)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var t in _triggers)
        {
            var status = t.Enabled ? "" : " [disabled]";
            _list.Items.Add($"{t.ExeName} → {t.WhileRunningProfile}{status}");
        }
        _list.EndUpdate();
        if (select >= 0 && select < _triggers.Count) _list.SelectedIndex = select;
        else ClearFields();
    }

    private void OnSelectionChanged()
    {
        _currentIndex = _list.SelectedIndex;
        if (_currentIndex < 0) { ClearFields(); return; }
        var t = _triggers[_currentIndex];

        _suppress = true;
        _exeBox.Text = t.ExeName;
        _whileCombo.SelectedItem = _profileNames.Contains(t.WhileRunningProfile) ? t.WhileRunningProfile : null;
        _onExitCombo.SelectedItem = t.OnExitProfile is null ? "(restore previous)" : t.OnExitProfile;
        _enabledCheck.Checked = t.Enabled;
        _suppress = false;
    }

    private void ClearFields()
    {
        _suppress = true;
        _exeBox.Text = "";
        _whileCombo.SelectedItem = null;
        _onExitCombo.SelectedItem = "(restore previous)";
        _enabledCheck.Checked = true;
        _suppress = false;
    }

    private void Update(Func<Trigger, Trigger> update)
    {
        if (_suppress || _currentIndex < 0) return;
        _triggers[_currentIndex] = update(_triggers[_currentIndex]);
        var prev = _currentIndex;
        _suppress = true;
        var t = _triggers[prev];
        var status = t.Enabled ? "" : " [disabled]";
        _list.Items[prev] = $"{t.ExeName} → {t.WhileRunningProfile}{status}";
        _suppress = false;
    }

    private void AddTrigger()
    {
        var defaultProfile = _profileNames.FirstOrDefault() ?? "";
        _triggers.Add(new Trigger(ExeName: "newtrigger", WhileRunningProfile: defaultProfile));
        ReloadList(_triggers.Count - 1);
    }

    private void RemoveTrigger()
    {
        if (_currentIndex < 0) return;
        _triggers.RemoveAt(_currentIndex);
        ReloadList(Math.Min(_currentIndex, _triggers.Count - 1));
    }
}
