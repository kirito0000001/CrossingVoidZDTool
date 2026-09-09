using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrossingVoidZDTool.Services;

/// <summary>
/// 维护「语音文件 ↔ 序列帧绑定」这层跨域关系：语音在磁盘上改名、搬家或被删掉之后，
/// 把各动作 sequence.json 里指向它的绑定一并改掉。
///
/// 单独成类是为了断开全仓唯一的一条循环依赖——
/// <see cref="VoiceMaterialService"/> 有六处要在改动语音文件后同步绑定，
/// 于是它调 <c>SequenceFrameService.RemapVoiceReferences</c>；
/// 而 <see cref="SequenceFrameService"/> 校验绑定目标时又要问语音服务「这是不是 wav」。
/// 这层关系本来就不属于任何一边：它既不是语音素材的组织方式，也不是序列帧的编辑操作，
/// 而是两者之间的引用完整性。放在这里之后，两边都只依赖它，谁也不依赖谁。
///
/// 绑定在清单里存的是<b>相对角色目录</b>的路径——角色目录要能在 Draft/Completed
/// 之间整体搬家，这里绝不能写回绝对路径。
/// </summary>
internal static class SequenceVoiceBindingService
{
    /// <summary>
    /// 按「旧绝对路径 → 新绝对路径」重写角色名下所有动作清单里的语音绑定。
    /// 目标为 <c>null</c> 表示语音已被删除，对应的绑定清空。
    /// </summary>
    public static void RemapVoiceReferences(
        CharacterCard character,
        IReadOnlyDictionary<string, string?> pathMappings)
    {
        if (pathMappings.Count == 0)
        {
            return;
        }

        var normalizedMappings = pathMappings.ToDictionary(
            pair => Path.GetFullPath(pair.Key),
            pair => string.IsNullOrWhiteSpace(pair.Value) ? null : Path.GetFullPath(pair.Value),
            StringComparer.OrdinalIgnoreCase);

        foreach (var manifestPath in SequenceManifestStore.EnumerateManifestPaths(character))
        {
            // 读不出来的清单跳过：这一轮只改绑定，不会照着残缺的结论删任何文件。
            // （会删文件的那条路径走 SequenceManifestStore.TryEnumerateReferencedFramePaths，
            //   那里读不出来是要整轮放弃的，两者的取舍不一样。）
            var manifest = SequenceManifestStore.ReadFileOrNull(manifestPath);
            if (manifest?.Frames is null)
            {
                continue;
            }

            var changed = false;
            foreach (var entry in manifest.Frames)
            {
                if (string.IsNullOrWhiteSpace(entry.VoiceRelativePath))
                {
                    continue;
                }

                var currentPath = Path.GetFullPath(Path.Combine(
                    character.FolderPath,
                    SequenceActionFolderLayout.NormalizeRelativePath(entry.VoiceRelativePath)));
                if (!normalizedMappings.TryGetValue(currentPath, out var targetPath))
                {
                    continue;
                }

                entry.VoiceRelativePath = targetPath is null
                    ? string.Empty
                    : SequenceActionFolderLayout.ToCharacterRelativePath(character, targetPath);
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            SequenceManifestStore.WriteFile(manifestPath, manifest);
        }
    }
}
