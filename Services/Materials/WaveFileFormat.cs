using System;
using System.IO;
using System.Text;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 判断一个文件是不是真的 WAV：扩展名 + RIFF/WAVE 文件头。
///
/// 从 <see cref="VoiceMaterialService"/> 里搬出来的，为的是断开
/// <c>SequenceFrameService</c> ⇄ <c>VoiceMaterialService</c> 这条循环依赖——
/// 序列帧那边只是想问一句「这是不是有效的 wav」，却因此整个依赖上了语音素材服务。
///
/// 这里刻意保留「连文件头一起验」而不是只看扩展名：改名成 .wav 的 mp3 能被导入，
/// 但引擎侧读不了，问题要到发布之后才暴露，那时素材已经进了工程。
/// </summary>
internal static class WaveFileFormat
{
    private static readonly byte[] RiffMagic = Encoding.ASCII.GetBytes("RIFF");
    private static readonly byte[] WaveMagic = Encoding.ASCII.GetBytes("WAVE");

    public static bool IsWaveFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return false;
        }

        try
        {
            // FileShare.ReadWrite：别人正在写这个文件时也要能判断，不能因为占用就当它不是 wav。
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < 12)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[12];
            return stream.Read(header) == header.Length &&
                   header[..4].SequenceEqual(RiffMagic) &&
                   header[8..12].SequenceEqual(WaveMagic);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
