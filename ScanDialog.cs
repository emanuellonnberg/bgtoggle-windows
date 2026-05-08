namespace BgToggle;

/// <summary>Checklist of detected recipes; user picks which to add to config.</summary>
public class ScanDialog : Form
{
    private readonly CheckedListBox _list;
    private readonly List<RecipeMatch> _matches;

    public IReadOnlyList<RecipeMatch> Selected { get; private set; } = Array.Empty<RecipeMatch>();

    public ScanDialog(List<RecipeMatch> matches)
    {
        _matches = matches;
        Text = "BgToggle — Add detected apps";
        Width = 520;
        Height = 460;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;

        var label = new Label
        {
            Text = $"Found {matches.Count} known app(s) running. Pick which to add:",
            Dock = DockStyle.Top,
            Padding = new Padding(8, 8, 8, 0),
            Height = 32
        };

        _list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false
        };
        foreach (var m in matches)
        {
            var admin = m.Recipe.RequiresAdmin ? "  [admin]" : "";
            _list.Items.Add($"{m.Recipe.DisplayName}  ({m.RunningProcessCount} proc){admin}", true);
        }

        var ok = new Button { Text = "Add selected", DialogResult = DialogResult.OK, Width = 120 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            Height = 48
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(label);

        ok.Click += (_, _) =>
        {
            var picked = new List<RecipeMatch>();
            for (int i = 0; i < _list.Items.Count; i++)
                if (_list.GetItemChecked(i)) picked.Add(_matches[i]);
            Selected = picked;
        };
    }
}
