using System;
using System.Collections.Generic;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class PageScrollPositionStore
{
    private readonly Dictionary<string, double> _positions = new(StringComparer.Ordinal);

    public void Save(string pageKey, double verticalOffset)
    {
        _positions[pageKey] = double.IsFinite(verticalOffset)
            ? Math.Max(0, verticalOffset)
            : 0;
    }

    public double Get(string pageKey)
    {
        return _positions.TryGetValue(pageKey, out var verticalOffset)
            ? verticalOffset
            : 0;
    }
}
