using System;
using System.Collections.Generic;

namespace CrossingVoidZDTool.Services;

/// <summary>面板条目的种类，和界面上的配色一一对应。</summary>
internal enum LogEntryKind
{
    Info,
    User,
    Warning,
    Error,
}

internal sealed record LogEntry(
    long Sequence,
    LogEntryKind Kind,
    string DisplayText,
    string CopyText,
    bool Sticky);

/// <summary>
/// 日志面板里那 300 条的淘汰规则。
///
/// 以前是「队列满了就 <c>Dequeue</c> 掉最旧的一条」，于是跑完第五步
/// （一次检测上百行）第三步以前的日志全被挤掉，出问题时看不到是从哪一步开始坏的。
///
/// 现在多了 Sticky 这一类：步骤标题行和批次首行**不参与淘汰**，
/// 所以哪怕中间几百条明细都过期了，面板里仍然能顺着
/// <c>▶1 ■1 ▶2 ■2 …</c> 看出这次走到了哪、每一步的结论是什么。
///
/// 淘汰可能发生在**中间**（要跳过 Sticky 行），所以 <see cref="Append"/>
/// 返回被挤掉那条的下标，界面照着这个下标删自己的那一格——两边才不会错位。
/// </summary>
internal sealed class LogPanelBuffer
{
    public const int DefaultCapacity = 300;

    /// <summary>Sticky 的硬上限。步骤标题行本来就没几条，这只是防呆。</summary>
    public const int DefaultStickyCapacity = 200;

    private readonly List<LogEntry> _entries = [];
    private long _sequence;

    public LogPanelBuffer(int capacity = DefaultCapacity, int stickyCapacity = DefaultStickyCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "面板容量必须为正数。");
        }

        if (stickyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stickyCapacity), stickyCapacity, "Sticky 容量必须为正数。");
        }

        Capacity = capacity;
        StickyCapacity = stickyCapacity;
    }

    public int Capacity { get; }

    public int StickyCapacity { get; }

    public IReadOnlyList<LogEntry> Entries => _entries;

    public int Count => _entries.Count;

    public int NormalCount
    {
        get
        {
            var count = 0;
            foreach (var entry in _entries)
            {
                if (!entry.Sticky)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int StickyCount => _entries.Count - NormalCount;

    /// <summary>
    /// 追加一条，返回**被挤掉那条的下标**（没有挤掉就是 -1）。
    /// 界面拿到下标后先 <c>RemoveAt</c> 再追加，顺序和这里完全一致。
    /// </summary>
    public int Append(LogEntryKind kind, string displayText, string copyText, bool sticky)
    {
        ArgumentNullException.ThrowIfNull(displayText);
        ArgumentNullException.ThrowIfNull(copyText);

        var removedIndex = -1;
        if (sticky)
        {
            if (StickyCount >= StickyCapacity)
            {
                removedIndex = IndexOfFirst(sticky: true);
            }
        }
        else if (NormalCount >= Capacity)
        {
            removedIndex = IndexOfFirst(sticky: false);
        }

        if (removedIndex >= 0)
        {
            _entries.RemoveAt(removedIndex);
        }

        _entries.Add(new LogEntry(++_sequence, kind, displayText, copyText, sticky));
        return removedIndex;
    }

    public void Clear()
    {
        _entries.Clear();
        _sequence = 0;
    }

    private int IndexOfFirst(bool sticky)
    {
        for (var index = 0; index < _entries.Count; index++)
        {
            if (_entries[index].Sticky == sticky)
            {
                return index;
            }
        }

        return -1;
    }
}
