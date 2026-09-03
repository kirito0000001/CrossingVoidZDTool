using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CrossingVoidZDTool.Services;

internal sealed class WaveAudioDurationReader
{
    private const ushort PcmFormat = 1;
    private const ushort IeeeFloatFormat = 3;
    private const ushort ExtensibleFormat = 0xFFFE;
    private const double WindowSeconds = 0.01d;
    private const double TailPaddingSeconds = 0.05d;
    private const double RelativeAudibleThreshold = 0.0056d;
    private const double AbsoluteAudibleThreshold = 0.0005d;

    private static readonly object EffectiveDurationCacheLock = new();
    private static readonly Dictionary<string, EffectiveDurationCacheEntry> EffectiveDurationCache =
        new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan? GetDuration(string filePath)
    {
        var waveInfo = TryReadWaveInfo(filePath);
        return waveInfo is null ? null : GetFullDuration(waveInfo.Value);
    }

    public TimeSpan? GetEffectiveDuration(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            var fullPath = fileInfo.FullName;
            lock (EffectiveDurationCacheLock)
            {
                if (EffectiveDurationCache.TryGetValue(fullPath, out var cached) &&
                    cached.FileLength == fileInfo.Length &&
                    cached.LastWriteTimeUtcTicks == fileInfo.LastWriteTimeUtc.Ticks)
                {
                    return cached.Duration;
                }
            }

            var waveInfo = TryReadWaveInfo(fullPath);
            if (waveInfo is null)
            {
                return null;
            }

            var fullDuration = GetFullDuration(waveInfo.Value);
            var effectiveDuration = TryMeasureEffectiveDuration(fullPath, waveInfo.Value, fullDuration) ?? fullDuration;
            lock (EffectiveDurationCacheLock)
            {
                EffectiveDurationCache[fullPath] = new EffectiveDurationCacheEntry(
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc.Ticks,
                    effectiveDuration);
            }

            return effectiveDuration;
        }
        catch (IOException)
        {
            return GetDuration(filePath);
        }
        catch (UnauthorizedAccessException)
        {
            return GetDuration(filePath);
        }
    }

    private static TimeSpan? TryMeasureEffectiveDuration(
        string filePath,
        WaveInfo waveInfo,
        TimeSpan fullDuration)
    {
        if (!CanDecodeSamples(waveInfo))
        {
            return null;
        }

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        stream.Position = waveInfo.DataOffset;

        var windowFrameCount = Math.Max(1, (int)Math.Round(waveInfo.SampleRate * WindowSeconds));
        var dataEnd = Math.Min(stream.Length, waveInfo.DataOffset + waveInfo.DataSize);
        var windowEndTimes = new List<double>();
        var windowRootMeanSquares = new List<double>();
        var framesRead = 0L;
        var peakRootMeanSquare = 0d;

        while (stream.Position + waveInfo.BlockAlign <= dataEnd)
        {
            var sumSquares = 0d;
            var samplesRead = 0;
            var framesInWindow = 0;
            while (framesInWindow < windowFrameCount && stream.Position + waveInfo.BlockAlign <= dataEnd)
            {
                for (var channel = 0; channel < waveInfo.Channels; channel++)
                {
                    var sample = ReadNormalizedSample(reader, waveInfo.EffectiveFormat, waveInfo.BitsPerSample);
                    if (sample is null)
                    {
                        return null;
                    }

                    var normalized = double.IsFinite(sample.Value) ? Math.Clamp(sample.Value, -1d, 1d) : 0d;
                    sumSquares += normalized * normalized;
                    samplesRead++;
                }

                var remainingFrameBytes = waveInfo.BlockAlign - waveInfo.Channels * (waveInfo.BitsPerSample / 8);
                if (remainingFrameBytes > 0)
                {
                    stream.Position += remainingFrameBytes;
                }

                framesInWindow++;
                framesRead++;
            }

            if (samplesRead == 0)
            {
                break;
            }

            var rootMeanSquare = Math.Sqrt(sumSquares / samplesRead);
            windowRootMeanSquares.Add(rootMeanSquare);
            windowEndTimes.Add(framesRead / (double)waveInfo.SampleRate);
            peakRootMeanSquare = Math.Max(peakRootMeanSquare, rootMeanSquare);
        }

        if (peakRootMeanSquare <= AbsoluteAudibleThreshold)
        {
            return null;
        }

        var threshold = Math.Max(AbsoluteAudibleThreshold, peakRootMeanSquare * RelativeAudibleThreshold);
        for (var index = windowRootMeanSquares.Count - 1; index >= 0; index--)
        {
            if (windowRootMeanSquares[index] < threshold)
            {
                continue;
            }

            var effectiveSeconds = Math.Min(
                fullDuration.TotalSeconds,
                windowEndTimes[index] + TailPaddingSeconds);
            return TimeSpan.FromSeconds(effectiveSeconds);
        }

        return null;
    }

    private static double? ReadNormalizedSample(BinaryReader reader, ushort format, ushort bitsPerSample)
    {
        if (format == PcmFormat)
        {
            return bitsPerSample switch
            {
                8 => (reader.ReadByte() - 128) / 128d,
                16 => reader.ReadInt16() / 32768d,
                24 => ReadInt24(reader) / 8388608d,
                32 => reader.ReadInt32() / 2147483648d,
                _ => null
            };
        }

        if (format == IeeeFloatFormat)
        {
            return bitsPerSample switch
            {
                32 => reader.ReadSingle(),
                64 => reader.ReadDouble(),
                _ => null
            };
        }

        return null;
    }

    private static int ReadInt24(BinaryReader reader)
    {
        var value = reader.ReadByte() | (reader.ReadByte() << 8) | (reader.ReadByte() << 16);
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xFF000000);
    }

    private static bool CanDecodeSamples(WaveInfo waveInfo)
    {
        if (waveInfo.Channels == 0 || waveInfo.SampleRate == 0 || waveInfo.BlockAlign == 0)
        {
            return false;
        }

        var bytesPerSample = waveInfo.BitsPerSample / 8;
        if (bytesPerSample == 0 || waveInfo.BlockAlign < waveInfo.Channels * bytesPerSample)
        {
            return false;
        }

        return waveInfo.EffectiveFormat switch
        {
            PcmFormat => waveInfo.BitsPerSample is 8 or 16 or 24 or 32,
            IeeeFloatFormat => waveInfo.BitsPerSample is 32 or 64,
            _ => false
        };
    }

    private static TimeSpan GetFullDuration(WaveInfo waveInfo)
    {
        return TimeSpan.FromSeconds(waveInfo.DataSize / (double)waveInfo.ByteRate);
    }

    private static WaveInfo? TryReadWaveInfo(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
            if (stream.Length < 12 || ReadFourCc(reader) != "RIFF")
            {
                return null;
            }

            _ = reader.ReadUInt32();
            if (ReadFourCc(reader) != "WAVE")
            {
                return null;
            }

            ushort format = 0;
            ushort effectiveFormat = 0;
            ushort channels = 0;
            uint sampleRate = 0;
            uint byteRate = 0;
            ushort blockAlign = 0;
            ushort bitsPerSample = 0;
            long dataOffset = 0;
            uint dataSize = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadFourCc(reader);
                var chunkSize = reader.ReadUInt32();
                var chunkStart = stream.Position;
                var chunkEnd = chunkStart + chunkSize;
                if (chunkEnd > stream.Length)
                {
                    return null;
                }

                if (chunkId == "fmt " && chunkSize >= 16)
                {
                    format = reader.ReadUInt16();
                    effectiveFormat = format;
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadUInt32();
                    byteRate = reader.ReadUInt32();
                    blockAlign = reader.ReadUInt16();
                    bitsPerSample = reader.ReadUInt16();
                    if (format == ExtensibleFormat && chunkSize >= 40)
                    {
                        stream.Position = chunkStart + 24;
                        var subFormatData1 = reader.ReadUInt32();
                        effectiveFormat = subFormatData1 is PcmFormat or IeeeFloatFormat
                            ? (ushort)subFormatData1
                            : format;
                    }
                }
                else if (chunkId == "data")
                {
                    dataOffset = chunkStart;
                    dataSize = chunkSize;
                }

                var paddedChunkEnd = chunkEnd + (chunkSize & 1);
                stream.Position = Math.Min(paddedChunkEnd, stream.Length);
            }

            if (byteRate == 0 || dataSize == 0 || dataOffset == 0)
            {
                return null;
            }

            return new WaveInfo(
                format,
                effectiveFormat,
                channels,
                sampleRate,
                byteRate,
                blockAlign,
                bitsPerSample,
                dataOffset,
                dataSize);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : string.Empty;
    }

    private readonly record struct WaveInfo(
        ushort Format,
        ushort EffectiveFormat,
        ushort Channels,
        uint SampleRate,
        uint ByteRate,
        ushort BlockAlign,
        ushort BitsPerSample,
        long DataOffset,
        uint DataSize);

    private readonly record struct EffectiveDurationCacheEntry(
        long FileLength,
        long LastWriteTimeUtcTicks,
        TimeSpan Duration);
}
