namespace XY.SolidWorksPlugin;

internal sealed class DoctorTaskPaneCompletionGate
{
	public bool SawFloatingWindow { get; private set; }

	public bool Observe(bool floatingWindowVisible, bool paneShellVisible, bool paneContentVisible)
	{
		SawFloatingWindow |= floatingWindowVisible;
		return paneShellVisible
			&& paneContentVisible
			&& (!SawFloatingWindow || !floatingWindowVisible);
	}
}
