using System.Text;
using System.Text.Json;

namespace BgToggle;

/// <summary>
/// Pick a running process, see the heuristic-derived recipe, edit the JSON
/// freely, and save it locally or copy to clipboard for a PR.
/// Local saves go to %APPDATA%\BgToggle\recipes.local.json which the
/// RecipeStore merges over the bundled file.
/// </summary>
public class RecipeSuggestDialog : Form
{
    private readonly List<ProcessCandidate> _candidates;
    private readonly ListBox _list;
    private readonly TextBox _json;
    private readonly Label _rationale;
    private readonly Button _saveBtn, _copyBtn;

    public RecipeSuggestDialog(List<ProcessCandidate> candidates)
    {
        _candidates = candidates;

        Text = "BgToggle — Suggest recipe from running process";
        Width = 980;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 320,
            Panel1MinSize = 240
        };

        // --- left ---
        var leftHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(8, 8, 8, 0),
            Text = $"{_candidates.Count} running process(es) without an existing recipe:"
        };
        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var c in _candidates)
        {
            var label = c.ProductName is not null && !string.Equals(c.ProductName, c.ProcessName, StringComparison.OrdinalIgnoreCase)
                ? $"{c.ProductName}  ({c.ProcessName}, PID {c.Pid})"
                : $"{c.ProcessName}  (PID {c.Pid})";
            _list.Items.Add(label);
        }
        _list.SelectedIndexChanged += (_, _) => OnSelectionChanged();

        split.Panel1.Controls.Add(_list);
        split.Panel1.Controls.Add(leftHeader);

        // --- right ---
        var rightHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(8, 8, 8, 0),
            Text = "Suggested recipe (edit before saving):"
        };
        _rationale = new Label
        {
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(8, 4, 8, 4),
            ForeColor = Color.DarkSlateGray,
            Text = "Pick a process on the left."
        };
        _json = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = false
        };

        split.Panel2.Controls.Add(_json);
        split.Panel2.Controls.Add(_rationale);
        split.Panel2.Controls.Add(rightHeader);

        // --- bottom buttons ---
        _saveBtn = new Button { Text = "Save to local recipes", Width = 180, Enabled = false };
        _copyBtn = new Button { Text = "Copy JSON", Width = 110, Enabled = false };
        var closeBtn = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Width = 80 };
        _saveBtn.Click += (_, _) => SaveLocal();
        _copyBtn.Click += (_, _) => CopyJson();

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            Height = 48
        };
        bottom.Controls.Add(closeBtn);
        bottom.Controls.Add(_saveBtn);
        bottom.Controls.Add(_copyBtn);

        CancelButton = closeBtn;

        Controls.Add(split);
        Controls.Add(bottom);

        if (_candidates.Count > 0) _list.SelectedIndex = 0;
    }

    private void OnSelectionChanged()
    {
        var i = _list.SelectedIndex;
        if (i < 0 || i >= _candidates.Count) return;
        var c = _candidates[i];

        _json.Text = JsonSerializer.Serialize(c.Suggested, RecipeStore.Options);
        _saveBtn.Enabled = true;
        _copyBtn.Enabled = true;

        var sb = new StringBuilder();
        sb.AppendLine($"Exe: {c.ExePath}");
        sb.AppendLine($"Install dir: {c.InstallDir}");
        sb.AppendLine($"Sibling procs: {string.Join(", ", c.SiblingProcessNames)}");
        if (c.CandidateServices.Count > 0)
            sb.AppendLine($"Services under dir: {string.Join(", ", c.CandidateServices.Select(s => s.Name))}");
        if (c.AutostartEntry is not null)
            sb.AppendLine($"Autostart: {c.AutostartEntry.Method}\\{c.AutostartEntry.Name}");
        sb.AppendLine();
        sb.AppendLine("Heuristic: " + c.Rationale.Replace("\n", " · "));
        _rationale.Text = sb.ToString().TrimEnd();
    }

    private void SaveLocal()
    {
        try
        {
            var recipe = JsonSerializer.Deserialize<Recipe>(_json.Text, RecipeStore.Options);
            if (recipe is null || string.IsNullOrWhiteSpace(recipe.Id))
            {
                MessageBox.Show("Recipe JSON is invalid or has no id.", "BgToggle",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RecipeStore.SaveLocal(recipe);
            MessageBox.Show(
                $"Saved recipe '{recipe.Id}' to:\n{RecipeStore.LocalPath}\n\nReload config to merge it into the live recipe set.",
                "BgToggle", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (JsonException ex)
        {
            MessageBox.Show($"JSON parse error: {ex.Message}", "BgToggle",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyJson()
    {
        try
        {
            Clipboard.SetText(_json.Text);
            MessageBox.Show("Copied. Paste into a PR against recipes.json.",
                "BgToggle", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("Clipboard copy failed", ex);
        }
    }
}
