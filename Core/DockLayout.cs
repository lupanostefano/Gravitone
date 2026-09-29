namespace Gravitone.Core;

/// <summary>
/// Dock geometry in "dock space": <c>u</c> runs along the dock, <c>v</c> is the distance from the
/// screen edge. Everything is in device-independent pixels and derived from the icon size, so the
/// proportions stay the same at every size, like the macOS Dock.
/// </summary>
internal sealed class DockMetrics(double iconSize, double maxSize, DockEdge edge)
{
    public double IconSize { get; } = iconSize;
    public double MaxSize { get; } = maxSize;
    public DockEdge Edge { get; } = edge;

    /// <summary>Gap between the screen edge and the dock plate.</summary>
    public double EdgeGap => Math.Round(IconSize * 0.09);
    /// <summary>Plate padding around the icons.</summary>
    public double Pad => Math.Round(IconSize * 0.15);
    /// <summary>Space between two resting icons.</summary>
    public double Gap => IconSize * 0.06;
    /// <summary>Width of the separator slot between pinned and running apps.</summary>
    public double SeparatorWidth => Math.Round(IconSize * 0.22);
    /// <summary>How far from the pointer icons are still magnified.</summary>
    public double Range => IconSize * 3.2;
    public double PlateThickness => IconSize + 2 * Pad;
    public double DotSize => Math.Max(3, Math.Round(IconSize * 0.085));
    /// <summary>Highest an icon can reach: magnified, or resting and bouncing.</summary>
    public double TallestIcon => Math.Max(MaxSize, IconSize * 1.7);
    /// <summary>Room for the name label beyond the tallest icon.</summary>
    public double LabelRoom => Edge == DockEdge.Bottom ? 44 + IconSize * 1.6 : 260; // also room to drag an icon out of the dock
    public double WindowThickness => EdgeGap + Pad + TallestIcon + LabelRoom;
}

/// <summary>One entry of the strip: an icon (magnifies) or a separator (fixed width).</summary>
internal readonly record struct DockSlot(bool IsIcon, double Presence);

internal sealed class DockLayout
{
    double[] _restStarts = [], _restSlots = [], _magSlots = [];

    /// <summary>Start of each entry along the dock.</summary>
    public double[] Starts { get; private set; } = [];
    /// <summary>Drawn size of each entry (icons are square; separators use it as their width).</summary>
    public double[] Sizes { get; private set; } = [];
    public double PlateStart { get; private set; }
    public double PlateEnd { get; private set; }
    public double RestPlateStart { get; private set; }
    public double RestPlateEnd { get; private set; }

    /// <param name="length">Length of the dock window along the edge.</param>
    /// <param name="pointer">Pointer position along the dock, or null when it has never been over it.</param>
    /// <param name="amount">0 = at rest, 1 = fully magnified (animated on enter / leave).</param>
    public void Compute(DockMetrics m, IReadOnlyList<DockSlot> slots, double length, double? pointer, double amount)
    {
        int n = slots.Count;
        if (Starts.Length != n)
        {
            Starts = new double[n];
            Sizes = new double[n];
            _restStarts = new double[n];
            _restSlots = new double[n];
            _magSlots = new double[n];
        }

        // Each entry owns its trailing gap and shrinks with it while it appears / disappears,
        // so the total is simply the sum minus the one gap after the last entry.
        double s = m.IconSize, g = m.Gap;
        double restTotal = 0;
        for (int i = 0; i < n; i++)
        {
            double width = slots[i].IsIcon ? s : m.SeparatorWidth;
            _restSlots[i] = (width + g) * slots[i].Presence;
            restTotal += _restSlots[i];
        }
        double restLength = Math.Max(s, restTotal - g);
        double restStart = (length - restLength) / 2;
        RestPlateStart = restStart - m.Pad;
        RestPlateEnd = restStart + restLength + m.Pad;

        // 1. Magnification: a smooth bump centred on the pointer.
        double pos = restStart, magTotal = 0;
        for (int i = 0; i < n; i++)
        {
            _restStarts[i] = pos;
            double scale = 1;
            if (slots[i].IsIcon && pointer is double p && amount > 0)
            {
                double d = Math.Abs(p - (pos + s * slots[i].Presence / 2));
                if (d < m.Range) scale += (m.MaxSize / s - 1) * (1 + Math.Cos(Math.PI * d / m.Range)) / 2 * amount;
            }
            _magSlots[i] = _restSlots[i] * scale;
            Sizes[i] = (slots[i].IsIcon ? s : m.SeparatorWidth) * slots[i].Presence * scale;
            magTotal += _magSlots[i];
            pos += _restSlots[i];
        }
        double magLength = Math.Max(s, magTotal - g);

        // 2. Keep the point under the pointer still, so the dock grows away from it on both sides.
        double origin = (length - magLength) / 2;
        if (pointer is double pt && amount > 0 && n > 0)
            origin = pt - MapRestToMagnified(pt, restStart, restLength, magLength);

        // 3. Never leave the screen.
        if (origin - m.Pad < 0) origin = m.Pad;
        if (origin + magLength + m.Pad > length) origin = length - magLength - m.Pad;

        pos = origin;
        for (int i = 0; i < n; i++)
        {
            Starts[i] = pos;
            pos += _magSlots[i];
        }
        PlateStart = origin - m.Pad;
        PlateEnd = origin + magLength + m.Pad;
    }

    /// <summary>Where a resting position ends up in the magnified strip (relative to its start).</summary>
    double MapRestToMagnified(double p, double restStart, double restLength, double magLength)
    {
        if (p <= restStart) return p - restStart;
        if (p >= restStart + restLength) return magLength + (p - restStart - restLength);

        double mag = 0;
        for (int i = 0; i < _restSlots.Length; i++)
        {
            double local = p - _restStarts[i];
            if (local < _restSlots[i] || i == _restSlots.Length - 1)
                return mag + (_restSlots[i] > 0 ? local / _restSlots[i] * _magSlots[i] : 0);
            mag += _magSlots[i];
        }
        return mag;
    }

    /// <summary>Index of the entry at <paramref name="u"/> (gaps belong to the nearer entry), or -1.</summary>
    public int ItemAt(double u)
    {
        for (int i = 0; i < Starts.Length; i++)
        {
            if (_magSlots[i] <= 0.01) continue;
            double gapAfter = _magSlots[i] - Sizes[i];
            double gapBefore = i == 0 ? 0 : _magSlots[i - 1] - Sizes[i - 1];
            if (u >= Starts[i] - gapBefore / 2 && u <= Starts[i] + Sizes[i] + gapAfter / 2) return i;
        }
        return -1;
    }
}
