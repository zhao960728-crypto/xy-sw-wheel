using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

// This dialog only reads entry-point names. Copying is done by the owner after
// OK; neither choosing a file nor cancelling this dialog imports/runs a macro.
internal sealed class UserMacroImportDialog : PluginToolWindow
{
    internal const string MacroFileFilter = "SOLIDWORKS VBA 宏 (*.swp;*.swb)|*.swp;*.swb";
    private readonly Func<string, List<UserMacroEntryPoint>> _read;
    private readonly Action<string> _validateName;
    private readonly TextBox _source = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _name = new TextBox { Dock = DockStyle.Fill, MaxLength = 64 };
    private readonly ComboBox _module = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _procedure = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(78, 94, 108) };
    private readonly Button _confirm = new Button { Text = "确定加入", AutoSize = true, Enabled = false };
    private readonly List<UserMacroEntryPoint> _entries = new List<UserMacroEntryPoint>();
    private sealed class ModuleChoice
    {
        internal string Name;
        public override string ToString() => Name.Length == 0 ? "（SWB 无模块）" : Name;
    }
    private sealed class AcceptedImport
    {
        internal readonly string SourcePath, FunctionName, ModuleName, ProcedureName;
        internal AcceptedImport(string source, string name, string module, string procedure)
        { SourcePath = source; FunctionName = name; ModuleName = module; ProcedureName = procedure; }
    }
    private AcceptedImport _accepted;
    private bool _reading;
    internal string SourcePath => _accepted?.SourcePath ?? _source.Text;
    internal string FunctionName => _accepted?.FunctionName ?? _name.Text.Trim();
    internal string ModuleName => _accepted?.ModuleName ?? (_module.SelectedItem as ModuleChoice)?.Name ?? "";
    internal string ProcedureName => _accepted?.ProcedureName ?? Convert.ToString(_procedure.SelectedItem) ?? "";

    internal UserMacroImportDialog(Func<string, List<UserMacroEntryPoint>> read, Action<string> validateName)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _validateName = validateName ?? throw new ArgumentNullException(nameof(validateName));
        Text = "加入宏"; Width = 660; Height = 380; MinimumSize = new Size(540, 350); StartPosition = FormStartPosition.CenterParent;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 6 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 4; row++) body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var fileRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "选择宏文件…", AutoSize = true };
        browse.Click += (_, __) => ChooseSource();
        fileRow.Controls.Add(_source, 0, 0); fileRow.Controls.Add(browse, 1, 0);
        var labels = new[] { "宏文件", "功能名（必填）", "模块 / SWB 可为空", "无参数过程" };
        Control[] inputs = { fileRow, _name, _module, _procedure };
        for (int row = 0; row < labels.Length; row++)
        {
            body.Controls.Add(new Label { Text = labels[row], AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            inputs[row].Margin = new Padding(0, 5, 0, 5); body.Controls.Add(inputs[row], 1, row);
        }
        var hint = new Label { Text = "功能名由你命名，并显示在选项卡上。读取入口和加入宏不会运行宏；保存设置后，点击宏按钮才会执行。", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
        body.Controls.Add(hint, 0, 4); body.SetColumnSpan(hint, 2);
        body.Controls.Add(_status, 0, 5); body.SetColumnSpan(_status, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 8, 12, 8), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel); buttons.Controls.Add(_confirm); Controls.Add(body); Controls.Add(buttons);
        CancelButton = cancel; AcceptButton = _confirm;
        _name.TextChanged += (_, __) => UpdateValidation();
        _module.SelectedIndexChanged += (_, __) => LoadProcedures();
        _procedure.SelectedIndexChanged += (_, __) => UpdateValidation();
        _confirm.Click += (_, __) =>
        {
            if (!TryValidate(out string message)) { _status.Text = message; return; }
            // Close disposes a modeless form before its owner's await may
            // continue. The accepted request must outlive all child controls.
            _accepted = new AcceptedImport(SourcePath, FunctionName, ModuleName, ProcedureName);
            DialogResult = DialogResult.OK; Close();
        };
        UpdateValidation();
    }

    private void ChooseSource()
    {
        using var picker = new OpenFileDialog { Title = "选择要加入的 SOLIDWORKS 宏", Filter = MacroFileFilter, CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) == DialogResult.OK) LoadSource(picker.FileName);
    }

    internal void LoadSource(string path)
    {
        _reading = true; _entries.Clear(); _module.Items.Clear(); _procedure.Items.Clear();
        try
        {
            _source.Text = Path.GetFullPath(path);
            _entries.AddRange((_read(_source.Text) ?? new List<UserMacroEntryPoint>()).Where(entry => entry != null));
            foreach (string module in _entries.Select(entry => entry.ModuleName ?? "").Distinct(StringComparer.OrdinalIgnoreCase)) _module.Items.Add(new ModuleChoice { Name = module });
            if (_module.Items.Count == 1) _module.SelectedIndex = 0;
        }
        catch (Exception error) { _status.Text = "读取宏入口失败：" + error.Message; _reading = false; _confirm.Enabled = false; return; }
        finally { _reading = false; }
        LoadProcedures();
    }

    private void LoadProcedures()
    {
        if (_reading) return;
        _procedure.Items.Clear();
        if (_module.SelectedIndex < 0) { UpdateValidation(); return; }
        foreach (string procedure in _entries.Where(entry => string.Equals(entry.ModuleName, ModuleName, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.ProcedureName).Distinct(StringComparer.OrdinalIgnoreCase)) _procedure.Items.Add(procedure);
        if (_procedure.Items.Count == 1) _procedure.SelectedIndex = 0;
        UpdateValidation();
    }

    private bool TryValidate(out string message)
    {
        message = "";
        if (_source.Text.Length == 0) { message = "请选择宏文件。"; return false; }
        if (_entries.Count == 0) { message = "没有读到可运行的无参数过程，请检查宏文件。"; return false; }
        try { _validateName(FunctionName); }
        catch (Exception error) { message = error.Message; return false; }
        if (_module.SelectedIndex < 0 || _procedure.SelectedIndex < 0 || !_entries.Any(entry => string.Equals(entry.ModuleName, ModuleName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.ProcedureName, ProcedureName, StringComparison.OrdinalIgnoreCase)))
        { message = "请选择要运行的模块和无参数过程。"; return false; }
        return true;
    }

    private void UpdateValidation()
    {
        if (_reading) return;
        bool valid = TryValidate(out string message);
        _confirm.Enabled = valid; _status.Text = valid ? "确认后先加入待保存列表，关闭设置可取消本次加入。" : message;
    }
}
