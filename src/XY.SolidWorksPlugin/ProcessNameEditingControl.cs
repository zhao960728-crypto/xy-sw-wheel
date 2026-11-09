using System;
using System.Drawing;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed class ProcessNameComboCell : DataGridViewTextBoxCell
{
    internal string[] Candidates { get; set; } = Array.Empty<string>();
    public override Type EditType => typeof(ProcessNameEditingControl);
    public override void InitializeEditingControl(int rowIndex, object initialFormattedValue, DataGridViewCellStyle style)
    {
        base.InitializeEditingControl(rowIndex, initialFormattedValue, style);
        var editor = (ProcessNameEditingControl)DataGridView.EditingControl;
        editor.Items.Clear(); editor.Items.AddRange(Candidates);
        editor.Text = Convert.ToString(Value) ?? "";
    }
    public override object Clone()
    {
        var copy = (ProcessNameComboCell)base.Clone();
        copy.Candidates = Candidates;
        return copy;
    }
}

// Editable dropdown: suggestions do not restrict a user's own process names.
internal sealed class ProcessNameEditingControl : ComboBox, IDataGridViewEditingControl
{
    public ProcessNameEditingControl()
    {
        DropDownStyle = ComboBoxStyle.DropDown;
        AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        AutoCompleteSource = AutoCompleteSource.ListItems;
        IntegralHeight = false; MaxDropDownItems = 12;
    }
    public DataGridView EditingControlDataGridView { get; set; }
    public object EditingControlFormattedValue { get => Text; set => Text = Convert.ToString(value) ?? ""; }
    public int EditingControlRowIndex { get; set; }
    public bool EditingControlValueChanged { get; set; }
    public Cursor EditingPanelCursor => Cursors.Default;
    public bool RepositionEditingControlOnValueChange => false;
    public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context) => Text;
    public void ApplyCellStyleToEditingControl(DataGridViewCellStyle style)
    { Font = style.Font; ForeColor = style.ForeColor; BackColor = style.BackColor; }
    public void PrepareEditingControlForEdit(bool selectAll) { if (selectAll) SelectAll(); }
    public bool EditingControlWantsInputKey(Keys keyData, bool dataGridViewWantsInputKey)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
            case Keys.Home: case Keys.End: case Keys.PageDown: case Keys.PageUp:
                return true;
            case Keys.Enter: case Keys.Escape: return DroppedDown;
            default: return !dataGridViewWantsInputKey;
        }
    }
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        EditingControlValueChanged = true;
        EditingControlDataGridView?.NotifyCurrentCellDirty(true);
    }
}
