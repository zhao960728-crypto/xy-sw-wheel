using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal static class ToolNotice
{
	public static void Show(string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
		=> Show(null, text, caption, buttons, icon);
	// Informational results never disable the SOLIDWORKS model window.
	public static void Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
	{
		if (buttons != MessageBoxButtons.OK) throw new System.ArgumentException("Use an explicit confirmation for decision prompts.", nameof(buttons));
		TextResultDialog dialog = new TextResultDialog(caption, text, "");
		dialog.ShowModeless(owner as Form);
	}
}
