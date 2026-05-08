namespace BgToggle;

/// <summary>
/// Manage the Apps list: add (from recipe or custom), remove, edit per-app
/// fields. Edits are held in memory and only persisted on Save.
/// </summary>
public class AppEditor : Form
{
    private readonly List<AppDefinition> _apps;
    private readonly List<Recipe> _recipes;

    private readonly ListBox _appList;
    private readonly Panel _editPanel;
    private readonly TextBox _idBox, _nameBox, _exeBox, _launchPathBox, _launchArgsBox, _autostartKeyBox;
    private readonly ComboBox _recipeCombo, _autostartCombo, _killCombo;
    private readonly NumericUpDown _gracefulMs;
    private readonly Button _addRecipeBtn, _addCustomBtn, _removeBtn;

    private int _currentIndex = -1;
    private bool _suppressFieldEvents;

    public IReadOnlyList<AppDefinition> Result => _apps;

    public AppEditor(IEnumerable<AppDefinition> apps, IReadOnlyList<Recipe> recipes)
    {
        _apps = apps.Select(a => a with { }).ToList(); // shallow copy of records
        _recipes = recipes.OrderBy(r => r.DisplayName).ToList();

        Text = "BgToggle — Manage apps";
        Width = 820;
        Height = 540;
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

        // --- left ---
        _appList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _appList.SelectedIndexChanged += (_, _) => OnAppSelected();

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 36,
            Padding = new Padding(4)
        };
        _addRecipeBtn = new Button { Text = "+ Recipe", Width = 76 };
        _addCustomBtn = new Button { Text = "+ Custom", Width = 76 };
        _removeBtn = new Button { Text = "Remove", Width = 72 };
        _addRecipeBtn.Click += (_, _) => AddFromRecipe();
        _addCustomBtn.Click += (_, _) => AddCustom();
        _removeBtn.Click += (_, _) => RemoveCurrent();
        leftButtons.Controls.Add(_addRecipeBtn);
        leftButtons.Controls.Add(_addCustomBtn);
        leftButtons.Controls.Add(_removeBtn);

        split.Panel1.Controls.Add(_appList);
        split.Panel1.Controls.Add(leftButtons);

        // --- right: edit panel ---
        _editPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true };
        int y = 8, lblW = 110, fieldW = 400;
        Label L(string text) { var l = new Label { Left = 0, Top = y + 4, Width = lblW, Text = text }; _editPanel.Controls.Add(l); return l; }
        TextBox T() { var t = new TextBox { Left = lblW, Top = y, Width = fieldW }; _editPanel.Controls.Add(t); y += 32; return t; }
        ComboBox C(IEnumerable<string> items) { var c = new ComboBox { Left = lblW, Top = y, Width = fieldW, DropDownStyle = ComboBoxStyle.DropDownList }; foreach (var i in items) c.Items.Add(i); _editPanel.Controls.Add(c); y += 32; return c; }

        L("Id (stable):");          _idBox = T();
        L("Display name:");         _nameBox = T();
        L("Recipe (optional):");    _recipeCombo = C(new[] { "(none)" }.Concat(_recipes.Select(r => r.Id)));
        L("Exe name:");             _exeBox = T();
        L("Launch path:");          _launchPathBox = T();
        L("Launch args:");          _launchArgsBox = T();
        L("Kill method (legacy):"); _killCombo = C(Enum.GetNames<KillMethod>());
        L("Graceful timeout ms:");  _gracefulMs = new NumericUpDown { Left = lblW, Top = y, Width = 120, Minimum = 0, Maximum = 60000, Increment = 500 }; _editPanel.Controls.Add(_gracefulMs); y += 32;
        L("Autostart:");            _autostartCombo = C(Enum.GetNames<AutostartMethod>());
        L("Autostart key:");        _autostartKeyBox = T();

        // Wire change handlers — apply edits to current app immediately.
        _idBox.TextChanged += (_, _) => UpdateField(a => a with { Id = _idBox.Text.Trim() });
        _nameBox.TextChanged += (_, _) => UpdateField(a => a with { DisplayName = _nameBox.Text });
        _recipeCombo.SelectedIndexChanged += (_, _) =>
        {
            var sel = _recipeCombo.SelectedItem as string;
            UpdateField(a => a with { RecipeId = (sel == "(none)" || string.IsNullOrEmpty(sel)) ? null : sel });
        };
        _exeBox.TextChanged += (_, _) => UpdateField(a => a with { ExeName = _exeBox.Text.Trim() });
        _launchPathBox.TextChanged += (_, _) => UpdateField(a => a with { LaunchPath = NullIfEmpty(_launchPathBox.Text) });
        _launchArgsBox.TextChanged += (_, _) => UpdateField(a => a with { LaunchArgs = NullIfEmpty(_launchArgsBox.Text) });
        _killCombo.SelectedIndexChanged += (_, _) => UpdateField(a => a with { KillMethod = Enum.Parse<KillMethod>((string)_killCombo.SelectedItem!) });
        _gracefulMs.ValueChanged += (_, _) => UpdateField(a => a with { GracefulTimeoutMs = (int)_gracefulMs.Value });
        _autostartCombo.SelectedIndexChanged += (_, _) => UpdateField(a => a with { Autostart = Enum.Parse<AutostartMethod>((string)_autostartCombo.SelectedItem!) });
        _autostartKeyBox.TextChanged += (_, _) => UpdateField(a => a with { AutostartKey = NullIfEmpty(_autostartKeyBox.Text) });

        split.Panel2.Controls.Add(_editPanel);

        // --- bottom buttons ---
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            Height = 48
        };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 96 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        bottom.Controls.Add(save);
        bottom.Controls.Add(cancel);

        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(split);
        Controls.Add(bottom);

        ReloadAppList(_apps.Count > 0 ? 0 : -1);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private void ReloadAppList(int select)
    {
        _appList.BeginUpdate();
        _appList.Items.Clear();
        foreach (var a in _apps) _appList.Items.Add($"{a.DisplayName}  ({a.Id})");
        _appList.EndUpdate();
        if (select >= 0 && select < _apps.Count) _appList.SelectedIndex = select;
        else ClearFields();
    }

    private void OnAppSelected()
    {
        _currentIndex = _appList.SelectedIndex;
        if (_currentIndex < 0) { ClearFields(); return; }
        var a = _apps[_currentIndex];

        _suppressFieldEvents = true;
        _idBox.Text = a.Id;
        _nameBox.Text = a.DisplayName;
        _recipeCombo.SelectedItem = a.RecipeId ?? "(none)";
        _exeBox.Text = a.ExeName;
        _launchPathBox.Text = a.LaunchPath ?? "";
        _launchArgsBox.Text = a.LaunchArgs ?? "";
        _killCombo.SelectedItem = a.KillMethod.ToString();
        _gracefulMs.Value = Math.Max(_gracefulMs.Minimum, Math.Min(_gracefulMs.Maximum, a.GracefulTimeoutMs));
        _autostartCombo.SelectedItem = a.Autostart.ToString();
        _autostartKeyBox.Text = a.AutostartKey ?? "";
        _suppressFieldEvents = false;
    }

    private void ClearFields()
    {
        _suppressFieldEvents = true;
        _idBox.Text = _nameBox.Text = _exeBox.Text = _launchPathBox.Text = _launchArgsBox.Text = _autostartKeyBox.Text = "";
        _recipeCombo.SelectedItem = "(none)";
        _killCombo.SelectedIndex = 0;
        _autostartCombo.SelectedIndex = 0;
        _gracefulMs.Value = 3000;
        _suppressFieldEvents = false;
    }

    private void UpdateField(Func<AppDefinition, AppDefinition> update)
    {
        if (_suppressFieldEvents || _currentIndex < 0) return;
        _apps[_currentIndex] = update(_apps[_currentIndex]);
        // Refresh display label without resetting selection.
        var prev = _currentIndex;
        _suppressFieldEvents = true;
        _appList.Items[prev] = $"{_apps[prev].DisplayName}  ({_apps[prev].Id})";
        _suppressFieldEvents = false;
    }

    private void AddFromRecipe()
    {
        if (_recipes.Count == 0)
        {
            MessageBox.Show("No recipes loaded.", "BgToggle", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new RecipePickDialog(_recipes, _apps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
        if (dlg.ShowDialog() != DialogResult.OK || dlg.Picked is null) return;
        var r = dlg.Picked;
        _apps.Add(new AppDefinition(
            Id: r.Id,
            DisplayName: r.DisplayName,
            ExeName: r.ProcessNames.FirstOrDefault() ?? "",
            LaunchPath: r.DefaultLaunchPath,
            LaunchArgs: r.DefaultLaunchArgs,
            RecipeId: r.Id
        ));
        ReloadAppList(_apps.Count - 1);
    }

    private void AddCustom()
    {
        var newApp = new AppDefinition(Id: $"custom-{_apps.Count + 1}", DisplayName: "New app");
        _apps.Add(newApp);
        ReloadAppList(_apps.Count - 1);
    }

    private void RemoveCurrent()
    {
        if (_currentIndex < 0) return;
        var a = _apps[_currentIndex];
        var ok = MessageBox.Show($"Remove app \"{a.DisplayName}\"?", "BgToggle",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ok != DialogResult.Yes) return;
        _apps.RemoveAt(_currentIndex);
        var next = Math.Min(_currentIndex, _apps.Count - 1);
        ReloadAppList(next);
    }

    private sealed class RecipePickDialog : Form
    {
        private readonly ListBox _list;
        public Recipe? Picked { get; private set; }

        public RecipePickDialog(IReadOnlyList<Recipe> recipes, HashSet<string> excludeIds)
        {
            Text = "Pick recipe";
            Width = 420;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;

            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (var r in recipes)
            {
                var label = excludeIds.Contains(r.Id)
                    ? $"{r.DisplayName}  ({r.Id})  [already added]"
                    : $"{r.DisplayName}  ({r.Id})";
                _list.Items.Add(new Item(r, label));
            }
            _list.DisplayMember = nameof(Item.Label);
            _list.DoubleClick += (_, _) => { Accept(); };

            var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Width = 80 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
            ok.Click += (_, _) => Accept();
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                Height = 48
            };
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(_list);
            Controls.Add(bottom);
        }

        private void Accept()
        {
            Picked = (_list.SelectedItem as Item)?.Recipe;
            DialogResult = DialogResult.OK;
        }

        private record Item(Recipe Recipe, string Label);
    }
}
