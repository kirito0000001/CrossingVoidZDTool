using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class SequenceVoiceSyncAnalyzer
{
    private readonly WaveAudioDurationReader _durationReader;

    public SequenceVoiceSyncAnalyzer()
        : this(new WaveAudioDurationReader())
    {
    }

    internal SequenceVoiceSyncAnalyzer(WaveAudioDurationReader durationReader)
    {
        _durationReader = durationReader;
    }

    public IReadOnlyList<SequenceVoiceSyncResult> Analyze(
        IReadOnlyList<SequenceFrameItem> frames,
        int fps)
    {
        if (frames.Count == 0)
        {
            return [];
        }

        var normalizedFps = Math.Clamp(fps, 1, 60);
        var orderedFrames = frames
            .OrderBy(frame => frame.Index)
            .ThenBy(frame => frame.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var results = new List<SequenceVoiceSyncResult>();
        for (var framePosition = 0; framePosition < orderedFrames.Count; framePosition++)
        {
            var frame = orderedFrames[framePosition];
            if (string.IsNullOrWhiteSpace(frame.VoiceFilePath))
            {
                continue;
            }

            var nextVoicePosition = -1;
            for (var index = framePosition + 1; index < orderedFrames.Count; index++)
            {
                if (!string.IsNullOrWhiteSpace(orderedFrames[index].VoiceFilePath))
                {
                    nextVoicePosition = index;
                    break;
                }
            }

            var boundaryPosition = nextVoicePosition >= 0 ? nextVoicePosition : orderedFrames.Count;
            var availableFrames = orderedFrames
                .Skip(framePosition)
                .Take(boundaryPosition - framePosition)
                .Sum(item => Math.Max(1, item.DurationFrames));
            var suggestedFrame = orderedFrames[Math.Max(framePosition, boundaryPosition - 1)];
            var duration = _durationReader.GetEffectiveDuration(frame.VoiceFilePath);
            if (duration is null)
            {
                results.Add(new SequenceVoiceSyncResult(
                    frame.Index,
                    frame.VoiceFilePath,
                    frame.VoiceFileName,
                    null,
                    0,
                    availableFrames,
                    normalizedFps,
                    0,
                    SequenceVoiceSyncStatusKind.Unavailable,
                    nextVoicePosition >= 0 ? orderedFrames[nextVoicePosition].Index : 0,
                    suggestedFrame.Index,
                    suggestedFrame.DurationFrames,
                    suggestedFrame.DurationFrames));
                continue;
            }

            var requiredFrames = Math.Max(
                1,
                (int)Math.Ceiling(duration.Value.TotalSeconds * normalizedFps - 0.0000001d));
            var differenceFrames = requiredFrames - availableFrames;
            var statusKind = differenceFrames switch
            {
                > 0 when nextVoicePosition >= 0 => SequenceVoiceSyncStatusKind.Interrupted,
                > 0 => SequenceVoiceSyncStatusKind.AnimationShorter,
                < 0 => SequenceVoiceSyncStatusKind.AnimationLonger,
                _ => SequenceVoiceSyncStatusKind.Aligned
            };
            results.Add(new SequenceVoiceSyncResult(
                frame.Index,
                frame.VoiceFilePath,
                frame.VoiceFileName,
                duration,
                requiredFrames,
                availableFrames,
                normalizedFps,
                differenceFrames,
                statusKind,
                nextVoicePosition >= 0 ? orderedFrames[nextVoicePosition].Index : 0,
                suggestedFrame.Index,
                suggestedFrame.DurationFrames,
                suggestedFrame.DurationFrames + Math.Max(0, differenceFrames)));
        }

        return results;
    }
}
