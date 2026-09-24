using System;
using System.Text;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// PSD 图层名的字节编解码。
///
/// PSD 的图层名是「Pascal 串」（长度前缀 + 原始字节），**没有规定用哪种编码**：
/// Photoshop 和画世界在中文环境下写的是 **GBK**（实测画世界导出的层名是 <c>背景</c>
/// 的 GBK 字节 <c>B1 B3 BE B0</c>），而我们的帧号 <c>0001</c> 本来就是 ASCII。
///
/// 两边必须用同一套：写的时候按 ASCII 编码中文名会变成 <c>??</c>（信息直接丢掉，
/// 读回来也认不出"背景"这种层），读的时候按 Latin1 解会变成乱码。
/// </summary>
internal static class PsdLayerNameEncoding
{
    public static byte[] Encode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return IsAscii(name) ? Encoding.ASCII.GetBytes(name) : Gbk.GetBytes(name);
    }

    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return IsAscii(bytes) ? Encoding.ASCII.GetString(bytes) : Gbk.GetString(bytes);
    }

    private static bool IsAscii(string value)
    {
        foreach (var character in value)
        {
            if (character >= 0x80)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAscii(byte[] bytes)
    {
        foreach (var value in bytes)
        {
            if (value >= 0x80)
            {
                return false;
            }
        }

        return true;
    }

    private static Encoding Gbk { get; } = CreateGbk();

    private static Encoding CreateGbk()
    {
        // .NET 默认只带 Unicode 系，GBK(936) 要先注册代码页提供程序。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(936);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            // 极少数环境取不到：退回 Latin1。帧号是 ASCII，不受影响；中文名会显示成乱码。
            ToolboxLog.Warn("取不到 GBK(936) 编码，PSD 图层名里的中文可能显示成乱码。", error);
            return Encoding.Latin1;
        }
    }
}
