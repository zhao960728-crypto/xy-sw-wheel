namespace XY.SolidWorksPlugin;

internal sealed class DoctorPropertyTaskPaneCompletionGate
{
	private long _actionIdleBaseline;
	private bool _sawButtonDisabled;
	private bool _sawRunningCommand;

	public bool SawPane { get; private set; }

	public bool SawAction { get; private set; }

	public bool ClosedWithoutAction { get; private set; }

	public bool Observe(bool paneVisible, bool actionButtonFound, bool actionButtonEnabled, bool actionReleased, bool commandRunning, long idleSequence)
	{
		SawPane |= paneVisible;
		if (actionReleased)
		{
			SawAction = true;
			_actionIdleBaseline = idleSequence;
		}
		if (!SawAction)
		{
			ClosedWithoutAction = SawPane && !paneVisible;
			return false;
		}

		_sawButtonDisabled |= actionButtonFound && !actionButtonEnabled;
		_sawRunningCommand |= commandRunning;
		if (!paneVisible)
		{
			return true;
		}
		if (_sawButtonDisabled && actionButtonFound && actionButtonEnabled)
		{
			return true;
		}
		if (_sawRunningCommand && !commandRunning)
		{
			return true;
		}

		// Some Doctor versions keep the action button enabled throughout processing.
		// Two host idle notifications after the user's click are the fallback signal.
		return actionButtonFound
			&& actionButtonEnabled
			&& !commandRunning
			&& idleSequence >= _actionIdleBaseline + 2;
	}
}
