using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 单个动作的操作快照：撤销要用的那份「动手之前长什么样」的备份，以及照着它恢复回去。
///
/// 从 <see cref="SequenceFrameService"/> 里搬出来的。快照有自己的一套目录规矩和自己的
/// 清单文件名（<c>sequence.snapshot.json</c>），跟正式清单不是一回事，混在一起读很容易看串。
///
/// 快照目录是<b>自洽</b>的：里面的清单只用平铺的文件名做相对路径，恢复时才不依赖
/// 原动作目录还在不在、帧池里那张图有没有被别的动作清理掉。
/// 空白帧在快照里落一个 <c>.blank</c> 占位文件——撤销要恢复的是帧序，不是只有图片。
/// </summary>
internal static class SequenceFrameSnapshotStore
{
    private const string SnapshotManifestFileName = "sequence.snapshot.json";

    public static IReadOnlyList<string> Create(
        CharacterCard character,
        SequenceFrameAction action,
        SequenceFrameManifest manifest)
    {
        var snapshotFolderPath = Path.Combine(
            character.ToolFolderPath,
            "OperationSnapshots",
            "SequenceFrames",
            $"{action.Code}-{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(snapshotFolderPath);
        var snapshotPaths = new List<string>();
        var snapshotIndexWidth = MaterialSequenceNaming.GetWidth(manifest.Frames.Count);
        var snapshotManifest = new SequenceFrameManifest
        {
            SchemaVersion = 3,
            ActionCode = action.Code,
            Fps = manifest.Fps
        };
        for (var index = 0; index < manifest.Frames.Count; index++)
        {
            var entry = manifest.Frames[index];
            if (entry.IsBlank)
            {
                var blankPath = Path.Combine(
                    snapshotFolderPath,
                    $"{(index + 1).ToString().PadLeft(snapshotIndexWidth, '0')}.blank");
                File.WriteAllText(blankPath, "blank", Encoding.UTF8);
                snapshotPaths.Add(blankPath);
                snapshotManifest.Frames.Add(SequenceManifestStore.CloneEntry(entry));
                continue;
            }

            var sourcePath = SequenceActionFolderLayout.ResolveManifestPath(character, action, entry.RelativePath);
            if (!File.Exists(sourcePath) || !SequenceFramePool.IsSupportedImage(sourcePath))
            {
                continue;
            }

            var extension = Path.GetExtension(sourcePath);
            var targetPath = Path.Combine(
                snapshotFolderPath,
                $"{(index + 1).ToString().PadLeft(snapshotIndexWidth, '0')}{extension}");
            File.Copy(sourcePath, targetPath, overwrite: true);
            snapshotPaths.Add(targetPath);
            snapshotManifest.Frames.Add(new SequenceFrameManifestEntry
            {
                SyncId = entry.SyncId,
                // 快照里只写文件名：恢复时以快照目录为基准，不牵扯原动作目录。
                RelativePath = Path.GetFileName(targetPath),
                DurationFrames = entry.DurationFrames,
                VoiceRelativePath = entry.VoiceRelativePath
            });
        }

        var snapshotManifestPath = Path.Combine(snapshotFolderPath, SnapshotManifestFileName);
        SequenceManifestStore.WriteFile(snapshotManifestPath, snapshotManifest);
        snapshotPaths.Add(snapshotManifestPath);

        return snapshotPaths;
    }

    public static void Restore(
        CharacterCard character,
        SequenceFrameAction action,
        IReadOnlyList<string> snapshotFilePaths)
    {
        var snapshotManifestPath = snapshotFilePaths.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), SnapshotManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(snapshotManifestPath) || !File.Exists(snapshotManifestPath))
        {
            throw new InvalidDataException("序列帧快照清单不存在，无法恢复。");
        }

        // 这里刻意不吞 JsonException：快照读坏了要当场炸出来，
        // 静悄悄地按「空快照」恢复等于把用户想撤销的那一版彻底抹掉。
        var snapshotManifest = JsonSerializer.Deserialize(
            File.ReadAllText(snapshotManifestPath, Encoding.UTF8),
            AppJsonSerializerContext.Default.SequenceFrameManifest)
            ?? throw new InvalidDataException("序列帧快照清单内容无效。");
        var folderPath = SequenceActionFolderLayout.GetActionFolderPath(character, action);
        Directory.CreateDirectory(folderPath);
        SequenceFramePool.ClearActionFolder(folderPath);
        var manifest = SequenceManifestStore.Create(action);
        manifest.Fps = snapshotManifest.Fps;
        var snapshotFolderPath = Path.GetDirectoryName(snapshotManifestPath)!;
        foreach (var entry in snapshotManifest.Frames)
        {
            if (entry.IsBlank)
            {
                manifest.Frames.Add(SequenceManifestStore.CloneEntry(entry));
                continue;
            }

            var sourcePath = Path.Combine(
                snapshotFolderPath,
                SequenceActionFolderLayout.NormalizeRelativePath(entry.RelativePath));
            if (!File.Exists(sourcePath) || !SequenceFramePool.IsSupportedImage(sourcePath))
            {
                throw new FileNotFoundException("序列帧快照中的图片不存在。", sourcePath);
            }

            manifest.Frames.Add(new SequenceFrameManifestEntry
            {
                SyncId = entry.SyncId,
                RelativePath = SequenceFramePool.ImportSource(character, action, sourcePath),
                DurationFrames = entry.DurationFrames,
                VoiceRelativePath = entry.VoiceRelativePath
            });
        }

        SequenceManifestStore.Save(character, action, manifest);
    }
}
