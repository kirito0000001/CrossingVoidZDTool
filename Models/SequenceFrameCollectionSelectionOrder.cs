using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool;

internal sealed class SequenceFrameCollectionSelectionOrder
{
    private static readonly IEqualityComparer<SequenceFrameCollectionItem> ReferenceComparer =
        ReferenceEqualityComparer.Instance;
    private readonly List<SequenceFrameCollectionItem> _orderedItems = [];

    public IReadOnlyList<SequenceFrameCollectionItem> OrderedItems => _orderedItems;

    public void ApplySelectionChange(
        IReadOnlyList<SequenceFrameCollectionItem> addedItems,
        IReadOnlyList<SequenceFrameCollectionItem> removedItems,
        IReadOnlyList<SequenceFrameCollectionItem> selectedItems,
        IReadOnlyList<SequenceFrameCollectionItem> displayOrder)
    {
        var selected = selectedItems.ToHashSet(ReferenceComparer);
        var added = addedItems.ToHashSet(ReferenceComparer);
        foreach (var item in _orderedItems.Where(item =>
                     removedItems.Contains(item, ReferenceComparer) || !selected.Contains(item)))
        {
            item.SelectionOrder = 0;
        }

        _orderedItems.RemoveAll(item =>
            removedItems.Contains(item, ReferenceComparer) || !selected.Contains(item));

        if (_orderedItems.Count == 0)
        {
            AddInDisplayOrder(
                selectedItems.Where(item => !added.Contains(item)),
                displayOrder);
        }

        AddInDisplayOrder(addedItems, displayOrder);
        AddInDisplayOrder(selectedItems, displayOrder);
        Renumber();
    }

    public void Clear()
    {
        foreach (var item in _orderedItems)
        {
            item.SelectionOrder = 0;
        }

        _orderedItems.Clear();
    }

    private void AddInDisplayOrder(
        IEnumerable<SequenceFrameCollectionItem> items,
        IReadOnlyList<SequenceFrameCollectionItem> displayOrder)
    {
        foreach (var item in items
                     .Distinct(ReferenceComparer)
                     .OrderBy(item => IndexOfReference(displayOrder, item)))
        {
            if (!_orderedItems.Contains(item, ReferenceComparer))
            {
                _orderedItems.Add(item);
            }
        }
    }

    private void Renumber()
    {
        for (var index = 0; index < _orderedItems.Count; index++)
        {
            _orderedItems[index].SelectionOrder = index + 1;
        }
    }

    private static int IndexOfReference(
        IReadOnlyList<SequenceFrameCollectionItem> items,
        SequenceFrameCollectionItem target)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], target))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
