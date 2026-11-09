namespace XY.SolidWorksPlugin;

internal sealed class ComboAdvanceGate
{
	private bool _lastDown;
	private bool _releasedSinceArm;

	public void Arm(bool keyDown)
	{
		_lastDown = keyDown;
		_releasedSinceArm = !keyDown;
	}

	public bool Observe(bool keyDown, bool solidWorksForeground)
	{
		if (!keyDown)
		{
			_releasedSinceArm = true;
		}
		bool pressedNow = _releasedSinceArm && keyDown && !_lastDown;
		_lastDown = keyDown;
		return pressedNow && solidWorksForeground;
	}
}
