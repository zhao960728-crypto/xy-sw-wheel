namespace XY.SolidWorksPlugin;

internal sealed class ManualInteractionGate
{
	private bool _sawTarget;
	private bool _sawWindow;

	public bool HasObservedInteraction => _sawTarget || _sawWindow;

	public bool Observe(bool targetVisible, bool windowVisible, bool actionCommitted = false)
	{
		if (actionCommitted)
		{
			return true;
		}
		if (targetVisible)
		{
			_sawTarget = true;
		}
		if (windowVisible)
		{
			_sawWindow = true;
		}
		if (!HasObservedInteraction)
		{
			return false;
		}
		if (_sawWindow)
		{
			return !windowVisible;
		}
		return _sawTarget && !targetVisible;
	}
}
