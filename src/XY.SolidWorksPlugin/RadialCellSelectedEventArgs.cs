using System;

namespace XY.SolidWorksPlugin;

internal sealed class RadialCellSelectedEventArgs : EventArgs
{
	public int RingIndex { get; }

	public int Slot { get; }

	public RadialCellSelectedEventArgs(int ringIndex, int slot)
	{
		RingIndex = ringIndex;
		Slot = slot;
	}
}
