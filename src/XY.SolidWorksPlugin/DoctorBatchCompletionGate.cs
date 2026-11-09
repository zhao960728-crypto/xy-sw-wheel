namespace XY.SolidWorksPlugin;

internal sealed class DoctorBatchCompletionGate
{
	private bool _wasWindowVisible;

	public bool SawWindow { get; private set; }

	public bool Started { get; private set; }

	public bool SawDisabledStartButton { get; private set; }

	public bool ClosedWithoutStarting { get; private set; }

	public bool Observe(bool windowVisible, bool startButtonFound, bool startButtonEnabled, bool startActionReleased)
	{
		SawWindow |= windowVisible;
		Started |= startActionReleased || (startButtonFound && !startButtonEnabled);
		SawDisabledStartButton |= startButtonFound && !startButtonEnabled;

		bool windowJustHidden = SawWindow && _wasWindowVisible && !windowVisible;
		ClosedWithoutStarting |= windowJustHidden && !Started;
		bool completed = (windowJustHidden && Started)
			|| (Started && SawDisabledStartButton && startButtonFound && startButtonEnabled);
		_wasWindowVisible = windowVisible;
		return completed;
	}
}
