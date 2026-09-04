using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using CrossingVoidZDTool;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml;

var tests = new (string Name, Action Run)[]
{
    ("BattleAvatar 固定槽位映射", BattleAvatarSlotsMapToFixedNames),
    ("BattleAvatar 指定槽位导入", BattleAvatarImportWritesRequestedIndex),
    ("BattleAvatar 缺槽不能被额外文件抵消", BattleAvatarExtraFileDoesNotFillMissingSlot),
    ("BattleAvatar 批量导入限制为四张", BattleAvatarBulkImportStopsAtFourSlots),
    ("BattleAvatar 常规追加不会生成第五张", BattleAvatarAppendRejectsFifthImage),
    ("统一素材编号覆盖个位两位和三位", MaterialSequenceNumberWidthsMatchCategoryCount),
    ("基础图片数量跨位数时整类动态补零", BaseMaterialNumbersFollowCategoryCount),
    ("语音分类区分必填多文件和可空多文件", VoiceSpecsDefineRequiredAndOptionalGroups),
    ("必填语音从 1 开始编号并支持追加", RequiredVoiceStartsAtOneAndAllowsMore),
    ("多文件语音连续编号且只接受真实 WAV", MultipleVoicesAreNumberedAndValidated),
    ("语音数量跨位数时整类动态补零", VoiceNumbersExpandAndShrinkWithCategoryCount),
    ("语音重排后同步序列帧绑定路径", VoiceRenumberingUpdatesSequenceFrameBindings),
    ("待分配语音批量移动并同步序列帧引用", PendingVoicesMoveToCategoryAndUpdateBindings),
    ("待分配语音 Delete 批量删除并同步序列帧引用", PendingVoiceDeleteKeyBatchDeletesSelectedItems),
    ("语音删除确认只强调删除按钮", VoiceDeleteDialogsOnlyAccentDeleteButton),
    ("待分配语音检查已分类和内部音频重复", PendingVoiceDuplicatesIncludeAssignedAndInternalMatches),
    ("语音试听使用缓存副本且不锁定正式素材", VoicePlaybackUsesCachedCopy),
    ("St2 刷新不清空素材区集合", LineArtRefreshDoesNotResetSections),
    ("St2 刷新同步语音分类和必填状态", LineArtRefreshLoadsVoiceSections),
    ("St2 语音区域提供导入试听替换删除", St2VoiceAreaProvidesCompleteControls),
    ("待分配语音管理窗口支持集中分配", PendingVoiceManagerSupportsAssignment),
    ("统一弹窗跟随当前窗口主题", DialogsFollowCurrentWindowTheme),
    ("St2 同时监听图片和语音目录变化", St2WatchesImageAndVoiceFolders),
    ("St2 BUFF 图标允许不设置", St2BuffIconsAreOptional),
    ("角色完成度统计包含六个必填语音", ProductionStatusIncludesRequiredVoices),
    ("自由画布在图片外保留透明区域", FreeCanvasPreservesTransparentPadding),
    ("自由画布允许图片完全移出", FreeCanvasAllowsFullyTransparentOutput),
    ("Tag 角色名字自动生成且只读", KeywordTagNamesAreAutomaticAndReadOnly),
    ("Tag 分类始终保留一个输入位", KeywordTagCategoryKeepsOneEntry),
    ("Tag 缺失分类可以被识别", KeywordTagMissingCategoriesAreReported),
    ("Tag 最终列表合并去重并保留旧数据", KeywordTagsFlattenAndPreserveLegacyValues),
    ("旧版 Tag 自动迁移到外号并清理界面", LegacyKeywordTagsMigrateToAliasesAndRemoveLegacyUi),
    ("Tag 分类通过角色信息保存往返", KeywordTagCategoriesRoundTripThroughCharacterInfo),
    ("Tag 分类区域允许内容高度增长", KeywordTagCategoryPanelAllowsDynamicHeight),
    ("St3 抗性类别开关默认物理并保存异能状态", CharacterAntiSwitchDefaultsAndPersists),
    ("零境角色台使用可滚动角色卡和独立详情弹窗", CharacterDeskUsesScrollableCardsAndDetailDialog),
    ("完成制作后返回角色台并打开角色详情", CompletingCharacterReturnsToDeskAndOpensDetail),
    ("角色只从 Draft 和 Completed 固定目录加载", CharactersLoadOnlyFromFixedStateFolders),
    ("已完成角色保存在 Completed", CompletedCharactersUseCompletedFolder),
    ("新角色创建在 Draft 且代码检查两个层级", NewCharactersUseDraftFolderAndCodesAreUniqueAcrossBothLevels),
    ("完成状态在 Completed 和 Draft 间移动角色", CompletionStateMovesCharacterBetweenRootAndDraft),
    ("Draft 和 Completed 同代码角色拒绝静默重复", DuplicateCharacterCodesAcrossLevelsAreRejected),
    ("完成移动冲突时保留草稿状态", CompletionMoveConflictPreservesDraftState),
    ("角色改码检查 Completed 和 Draft", RenamingCharacterCodeChecksBothLevels),
    ("角色台点击卡片只打开详情", CharacterDeskCardClickOnlyOpensDetail),
    ("St1 只列出全部草稿角色", St1ListsAllDraftCharacters),
    ("继续编辑把已完成角色恢复为草稿", ReopeningCompletedCharacterMovesToDraft),
    ("详情继续编辑先恢复草稿再导航", CharacterDetailContinueReopensBeforeNavigation),
    ("详情继续编辑先选择草稿角色再导航", CharacterDetailContinueSelectsDraftBeforeNavigation),
    ("详情前往虚幻同步台先选择对应角色", CharacterDetailUnrealSyncSelectsCharacterBeforeNavigation),
    ("虚幻角色使用当前摘要和弹出选择列表", UnrealSyncCharacterSelectorUsesSummaryAndFlyoutList),
    ("虚幻项目角色摘要显示中文名且不冒充完整详情", UnrealProjectCharacterSummaryUsesChineseNameWithoutFreshDetails),
    ("虚幻项目角色从角色条目文件读取中文名", UnrealProjectCharacterReadsChineseNameFromItemAsset),
    ("虚幻项目角色获取使用离线条目扫描", UnrealProjectCharacterRefreshUsesOfflineItemScan),
    ("虚幻详情导出拒绝 Unreal 非零退出", UnrealProjectDetailExportRejectsFailedProcess),
    ("虚幻详情导出提前识别项目模块版本不匹配", UnrealProjectDetailExportRejectsMismatchedModuleBuildIds),
    ("虚幻双向差异新增和更新默认选中", UnrealBridgeChangesSelectSafeUpdatesByDefault),
    ("虚幻双向差异冲突和删除默认不选", UnrealBridgeChangesProtectConflictsAndDeletes),
    ("虚幻双向差异按同步方向识别目标端改动", UnrealBridgeChangesRespectSyncDirection),
    ("虚幻同步台方向切换整套工作区", UnrealSyncWorkspaceSwitchesWholeDirection),
    ("虚幻同步台统一选择器包含搜索和通用素材", UnrealSyncSourcePickerIncludesSearchAndSharedMaterials),
    ("虚幻同步检测树保护冲突项", UnrealSyncSelectionTreeProtectsUnsafeChanges),
    ("虚幻同步检测树支持父子三态自由选择", UnrealSyncSelectionTreeSupportsTriStateSelection),
    ("虚幻导入操作摘要跟随具体勾选实时更新", UnrealImportOperationSummaryTracksLeafSelection),
    ("虚幻导入操作面板显示步骤影响和常驻结果", UnrealImportOperationPanelShowsGuidanceAndPersistentResult),
    ("虚幻同步状态按角色和项目隔离", UnrealBridgeStateIsScopedToCharacterAndProject),
    ("部分导入只更新选中项同步基线", UnrealBridgePartialImportUpdatesOnlySelectedBaselineEntries),
    ("虚幻同步仅在更新删除时默认备份", UnrealBridgeBackupPolicyProtectsRiskyChanges),
    ("虚幻项目备份使用当前引擎 ZipProjectUp", UnrealBridgeBackupUsesEngineAutomationTool),
    ("虚幻发布快照拒绝草稿角色", UnrealBridgeToolboxSnapshotRejectsDraftCharacter),
    ("虚幻工具箱快照覆盖角色六类模块", UnrealBridgeToolboxSnapshotCoversAllModules),
    ("虚幻工具箱快照只让修改项哈希变化", UnrealBridgeToolboxSnapshotHashesAreItemScoped),
    ("抗性类别变化会更新角色信息同步快照", CharacterAntiChangesSynchronizationSnapshot),
    ("序列帧同步身份在重排时保留且复制时新建", SequenceFrameSyncIdentitySurvivesReorderAndChangesOnCopy),
    ("BUFF 同步身份在改名和重排后保留", BuffSyncIdentitySurvivesRenameAndReorder),
    ("技能同步身份在保存往返后保留", SkillSyncIdentitySurvivesSaveRoundTrip),
    ("虚幻同步状态保存原始资产身份和规范路径", UnrealBridgeStatePreservesOriginIdentity),
    ("文件素材身份在移动改名后保留", UnrealBridgeFileIdentitySurvivesMoveAndRename),
    ("工具箱快照用文件身份识别重新分类素材", UnrealBridgeSnapshotUsesFileIdentityAcrossReclassification),
    ("重复语音重新分类后保留各自同步身份", DuplicateVoicesKeepSyncIdentityAfterClassification),
    ("从虚幻导入的文件素材认领原始同步身份", UnrealBridgeFileIdentityAdoptsImportedStableId),
    ("从虚幻导入图片后两端保持同一稳定身份", UnrealBridgeImportedMaterialKeepsSemanticStableId),
    ("从虚幻导入技能和BUFF后保持原始稳定身份", UnrealBridgeImportedSemanticItemsKeepStableIds),
    ("从虚幻导入序列帧后保持原始稳定身份", UnrealBridgeImportedSequenceFramesKeepStableIds),
    ("虚幻语音按路径分类并在导入后保持稳定身份", UnrealBridgeVoicesClassifyAndKeepStableIds),
    ("虚幻旧语音名称只自动识别明确分类", UnrealBridgeLegacyVoiceNamesClassifySafely),
    ("虚幻语义导出脚本导出SoundWave并生成语音分组", UnrealBridgeExporterProducesVoiceBuckets),
    ("虚幻序列PlaySound通知规整语音并绑定帧", UnrealBridgeSequenceVoiceNotificationsClassifyAndBindFrames),
    ("虚幻语义导出脚本读取序列PlaySound通知", UnrealBridgeExporterReadsSequenceSoundNotifies),
    ("项目共享素材库按哈希去重并合并使用位置", ProjectSharedMaterialLibraryDeduplicatesAndTracksUsages),
    ("虚幻序列公共音效导入项目共享素材库", UnrealBridgeImportCopiesSharedSequenceSounds),
    ("虚幻角色导出读取 Anti 抗性类别", UnrealBridgeExporterReadsCharacterAnti),
    ("虚幻导出包含通用 BUFF 图标目录", UnrealBridgeExporterIncludesSharedBuffIcons),
    ("虚幻扫描按原始资产身份恢复同步 ID", UnrealBridgeScanRestoresSyncIdFromOriginIdentity),
    ("虚幻扫描使用固定版本脚本和命令进程", UnrealBridgeScanUsesVersionedScriptAndCommandProcess),
    ("虚幻扫描器通过 AssetRegistry 引用关系识别素材", UnrealBridgeScannerUsesAssetRegistryReferences),
    ("工具箱规范化改名识别为 Rename 操作", UnrealBridgeDiffRecognizesNormalizationRename),
    ("双端格式哈希不同但各自未变时保持未修改", UnrealBridgeDiffUsesEndpointBaselinesForUnchangedItems),
    ("无旧基线时已有配对素材先迁移为未修改", UnrealBridgeDiffMigratesMatchedItemsWithoutBaseline),
    ("特殊字符清洗后的语音仍按规范路径配对", UnrealBridgeDiffPairsSanitizedVoiceNames),
    ("规范连字符语音不会误报改名", UnrealBridgeDiffKeepsCanonicalVoiceNameUnchanged),
    ("新基线按源文件哈希识别工具箱更新", UnrealBridgeDiffDetectsSourceFileChangeAfterMigration),
    ("虚幻执行计划只包含选中项且删除排最后", UnrealBridgeExecutionPlanUsesSelectedChangesAndDeletesLast),
    ("虚幻语音发布计划创建分类目录并规范名称", UnrealBridgeVoicePublishCreatesCategoryFolder),
    ("虚幻执行计划要求删除确认和首次发布模板", UnrealBridgeExecutionPlanRequiresDeleteConfirmationAndTemplate),
    ("虚幻发布只自动执行已有文件素材更新和改名", UnrealBridgePublishPolicySelectsOnlyMappedFileAssets),
    ("虚幻改名执行后用结果路径恢复原同步身份", UnrealBridgePostExecutionRestoresRenamedIdentity),
    ("虚幻执行器使用固定脚本和结果协议", UnrealBridgeExecutorUsesFixedScriptAndResultProtocol),
    ("虚幻执行结果全部验证后才生成同步状态", UnrealBridgeVerificationRequiresCompleteSuccess),
    ("部分同步验证保留未选素材基线", UnrealBridgeVerificationPreservesUnselectedBaselineEntries),
    ("从虚幻导入统一创建 Draft 并写入可读模块", UnrealBridgeImportCreatesDraftAndWritesReadableModules),
    ("从虚幻导入只写入勾选的具体素材", UnrealBridgeImportUsesSelectedLeafItems),
    ("从虚幻导入失败不保留新建 Draft 半成品", UnrealBridgeFailedImportRemovesNewDraft),
    ("从虚幻导入失败自动恢复已有 Draft", UnrealBridgeFailedImportRestoresExistingDraft),
    ("从虚幻重复导入图片会更新原文件而不是追加", UnrealBridgeRepeatedImportUpdatesMaterialInPlace),
    ("从虚幻导入会清理同一素材的旧追加残留", UnrealBridgeImportCleansLegacyMaterialDuplicates),
    ("从虚幻重复导入不会追加重复语音", UnrealBridgeRepeatedImportReplacesVoiceCategory),
    ("已完成角色不能直接进入 St2 至 St6", CompletedCharacterCannotEnterProductionSteps),
    ("导出角色复制完整文件夹并保护已有目标", ExportCharacterCopiesWholeFolderAndRequiresOverwrite),
    ("角色详情使用完整文件夹导出流程", CharacterDetailUsesFolderExportFlow),
    ("切换角色不会把程序性清空当成草稿编辑", SwitchingCharacterDoesNotRaiseDraftEdited),
    ("刷新同一角色元数据保留已打开草稿", ReplacingSameCharacterPreservesOpenDraft),
    ("被动技能文本修改触发角色信息保存", PassiveSkillTextChangeTriggersSave),
    ("大分区滚动位置彼此独立", PageScrollPositionsRemainIndependent),
    ("大分区主滚动区具有稳定名称", MainPageScrollViewersHaveStableNames),
    ("左侧大分区列表可滚动且设置保持固定", ShellNavigationMenuIsScrollable),
    ("BUFF 编辑器使用固定身份区和滚动属性区", BuffEditorUsesFixedIdentityAndScrollableProperties),
    ("BUFF 图标覆盖后刷新同路径图片", ReplacingBuffIconRefreshesVersionedUri),
    ("BUFF 数量跨位数时目录和编号动态补零", BuffNumbersFollowCategoryCount),
    ("BUFF 普通保存不会删除其他磁盘条目", SavingBuffsDoesNotDeleteUnloadedEntries),
    ("BUFF 效果模块和生命周期完整往返", BuffEffectsAndLifecycleRoundTrip),
    ("BUFF 旧字符串数值迁移为整数", LegacyBuffNumericStringsMigrateToIntegers),
    ("BUFF 切换角色先保存旧角色且拒绝跨角色写入", SwitchingBuffCharacterSavesPreviousAndRejectsCrossWrite),
    ("BUFF 编辑器复用默认数字框并提供多效果模块", BuffEditorUsesNumberBoxesAndEffectModules),
    ("BUFF 类型允许为空并表示仅标记", BuffDamageTypeAllowsMarkerOnly),
    ("技能倍率方向键按二维表格移动", SkillMultiplierArrowKeysNavigateGrid),
    ("技能倍率焦点切换延迟到按键事件之后", SkillMultiplierFocusMoveIsDeferred),
    ("技能图标编号宽度变化后自动恢复并回写", SkillIconPathRepairsAfterNumberWidthChanges),
    ("序列帧保存持续帧格和语音绑定", SequenceFramePersistsDurationAndVoice),
    ("序列帧语音按动作分类缩小范围", SequenceFrameVoicesFilterByActionCategory),
    ("序列帧语音绑定后重新显示当前选项", SequenceFrameVoiceSelectionRefreshesAfterOptions),
    ("替换序列帧素材保留帧属性", ReplacingSequenceFramePreservesMetadata),
    ("批量选入合集素材替换当前帧并依次追加", ReplacingSequenceFrameWithMultipleSourcesPreservesTargetMetadata),
    ("跨动作选择合集素材直接复用现有文件", SelectingCollectionFrameAcrossActionsReusesExistingFile),
    ("帧合集把同一资源的多个使用位置合并显示", SequenceCollectionAggregatesSharedResourceUsages),
    ("复制序列帧同时复制帧属性", DuplicatingSequenceFrameCopiesMetadata),
    ("复用素材显示总次数和当前使用次数", ReusedSequenceFramesExposeTimelineRelationship),
    ("空白帧可以在当前帧左右插入", BlankSequenceFrameCanInsertBeforeOrAfterCurrentFrame),
    ("批量删除序列帧只保留未选帧", DeletingMultipleSequenceFramesKeepsUnselectedFrames),
    ("批量复制序列帧按原顺序插入目标后", DuplicatingMultipleSequenceFramesPreservesOrder),
    ("序列帧排序保持各自帧属性", ReorderingSequenceFramesPreservesMetadata),
    ("时间轴高亮跟随播放且不会暂停", SequenceTimelineSelectionFollowsPlayback),
    ("帧序列编辑器区分单次和循环播放", SequenceEditorPlaybackModesControlAdvancement),
    ("新建帧后时间轴高亮同步到新帧", NewSequenceFrameSynchronizesTimelineSelection),
    ("帧属性修改不会清空时间轴集合", SequenceFrameMetadataEditDoesNotResetTimeline),
    ("序列帧撤销快照保留帧属性", SequenceFrameSnapshotPreservesMetadata),
    ("序列帧可见编号按总帧数动态补零", SequenceFrameNumbersUseDynamicWidth),
    ("编号序列帧导入按末尾数字排序", NumberedSequenceFrameImportsUseNumericSuffixOrder),
    ("非统一编号序列帧导入保留原顺序", UnnumberedSequenceFrameImportsPreservePickerOrder),
    ("帧时间轴 Delete 删除当前选中帧", SequenceTimelineDeleteKeyUsesSelectedFrame),
    ("持续帧格输入跟随当前帧", SequenceEditorDurationInputFollowsSelectedFrame),
    ("帧序列编辑器提供完整时间轴操作", SequenceEditorProvidesCompleteTimelineControls),
    ("帧时间轴使用动态宽度和统一高度的紧凑帧块", SequenceTimelineUsesCompactUniformHeightCards),
    ("编辑器顶部只显示稳定的帧位置摘要", SequenceEditorHeaderUsesStableFramePositionSummary),
    ("空序列可以新建帧且合集支持快捷键多选", SequenceEditorCreatesFirstFrameAndCollectionSupportsModifierMultiSelect),
    ("帧合集仅在手动触发时检测重复", SequenceCollectionDetectsDuplicatesOnlyWhenRequested),
    ("帧合集保存重复检测结果且仅素材数量变化时失效", SequenceCollectionPersistsDuplicateResultsUntilMaterialCountChanges),
    ("帧合集一键处理保留使用最多的重复资源", SequenceCollectionResolvesAllDuplicatesKeepingMostUsedResource),
    ("外层序列预览提供帧素材合集入口", OuterSequencePreviewProvidesFrameCollectionEntry),
    ("帧合集按动作顺序和帧编号排列", SequenceCollectionOrdersByActionThenFrameIndex),
    ("帧合集使用紧凑的使用位置优先卡片", SequenceCollectionUsesCompactUsageFirstCards),
    ("帧合集多选记录点击顺序并连续编号", SequenceCollectionMultiSelectionTracksClickOrder),
    ("帧序列编辑器支持扩展多选和批量操作", SequenceEditorSupportsExtendedMultiSelection),
    ("批量复制直接点击时间轴选择插入位置", SequenceBatchCopyUsesTimelineTargetSelection),
    ("序列帧排序完成后恢复当前预览", SequenceFrameReorderRefreshesPreview),
    ("外层序列帧导入保持页面滚动位置", SequenceFrameImportPreservesOuterScrollPosition),
    ("外层与编辑器复用双缓冲序列帧预览", SequencePreviewsShareDoubleBufferedPresenter),
    ("序列帧预览提前装载下一帧", SequencePreviewPrimesNextFrame),
    ("序列帧预加载提交解码后的像素", SequencePreviewInvalidatesDecodedWriteableBitmaps),
    ("帧序列编辑器提供播放模式和空格快捷键", SequenceEditorProvidesPlaybackModeAndSpaceShortcut),
    ("帧序列编辑器可切换有效语音结束时暂停", SequenceEditorCanPauseWhenEffectiveVoiceEnds),
    ("序列预览语音持续播放且新语音抢占", SequencePreviewVoiceUsesPersistentSingleChannel),
    ("WAV 时长读取支持标准块和额外元数据", WaveDurationReaderHandlesStandardAndExtraChunks),
    ("WAV 有效语音保留开头并忽略尾部静音", WaveEffectiveDurationKeepsLeadingAndTrimsTrailingSilence),
    ("语音同步分析计算顶替和序列结束帧差", SequenceVoiceSyncAnalysisCalculatesFrameDifferences),
    ("语音同步分析跟随 FPS 即时重算", SequenceVoiceSyncAnalysisRefreshesWhenFpsChanges),
    ("语音同步结果显示在右侧和时间轴", SequenceVoiceSyncResultsAppearInInspectorAndTimeline),
    ("主项目资源排除测试构建输出", MainProjectExcludesTestBuildOutputsFromResources)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex}");
    }
}

return failed == 0 ? 0 : 1;

static void MainProjectExcludesTestBuildOutputsFromResources()
{
    var projectPath = Path.Combine(Directory.GetCurrentDirectory(), "CrossingVoidZDTool.csproj");
    var document = XDocument.Load(projectPath);
    var elements = document.Descendants().ToArray();

    foreach (var itemName in new[] { "Content", "None", "PRIResource" })
    {
        AssertEqual(
            true,
            elements.Any(element =>
                element.Name.LocalName == itemName &&
                string.Equals(
                    element.Attribute("Remove")?.Value,
                    @"Tests\**\*",
                    StringComparison.OrdinalIgnoreCase)));
    }

    AssertEqual(
        true,
        elements.Any(element =>
            element.Name.LocalName == "Content" &&
            string.Equals(
                element.Attribute("Include")?.Value,
                @"Tools\**\*",
                StringComparison.OrdinalIgnoreCase)));
}

static void BattleAvatarSlotsMapToFixedNames()
{
    WithCharacterWorkspace((service, character, root) =>
    {
        var folder = service.GetMaterialFolderPath(character, BaseMaterialKind.BattleAvatar);
        Directory.CreateDirectory(folder);
        for (var index = 1; index <= 4; index++)
        {
            WriteImage(Path.Combine(folder, $"{character.Code}-BattleAvatar-{index:00}.png"));
        }

        var section = service.LoadSections(character).Single(item => item.Spec.Kind == BaseMaterialKind.BattleAvatar);
        var names = section.SlotGroups.SelectMany(group => group.Slots).Select(slot => slot.DisplayName).ToArray();
        AssertSequence(["1P主战", "1P护援", "2P主战", "2P护援"], names);
        AssertSequence([1, 2, 3, 4], section.SlotGroups.SelectMany(group => group.Slots).Select(slot => slot.Index).ToArray());
    });
}

static void BattleAvatarImportWritesRequestedIndex()
{
    WithCharacterWorkspace((service, character, root) =>
    {
        var sourcePath = Path.Combine(root, "source.png");
        WriteImage(sourcePath);

        var item = service.ImportAtIndex(character, BaseMaterialKind.BattleAvatar, sourcePath, 4);

        AssertEqual($"{character.Code}-BattleAvatar-4.png", item.FileName);
        AssertEqual(4, item.Index);
        AssertEqual(true, File.Exists(item.FilePath));
        AssertEqual(false, File.Exists(Path.Combine(Path.GetDirectoryName(item.FilePath)!, $"{character.Code}-BattleAvatar-05.png")));
    });
}

static void BattleAvatarExtraFileDoesNotFillMissingSlot()
{
    WithCharacterWorkspace((service, character, root) =>
    {
        var folder = service.GetMaterialFolderPath(character, BaseMaterialKind.BattleAvatar);
        Directory.CreateDirectory(folder);
        foreach (var index in new[] { 1, 2, 3, 5 })
        {
            WriteImage(Path.Combine(folder, $"{character.Code}-BattleAvatar-{index:00}.png"));
        }

        var section = service.LoadSections(character).Single(item => item.Spec.Kind == BaseMaterialKind.BattleAvatar);
        AssertEqual(1, section.MissingCount);
        AssertEqual(1, section.ExtraCount);
        AssertEqual(true, section.HasWarning);
        AssertEqual(true, section.StatusText.Contains("还差 1 张", StringComparison.Ordinal));
        AssertEqual(true, section.StatusText.Contains("额外文件 1 张", StringComparison.Ordinal));
    });
}

static void BattleAvatarBulkImportStopsAtFourSlots()
{
    WithCharacterWorkspace((service, character, root) =>
    {
        var sources = Enumerable.Range(1, 5)
            .Select(index => Path.Combine(root, $"source-{index}.png"))
            .ToArray();
        foreach (var source in sources)
        {
            WriteImage(source);
        }

        var imported = service.ReplaceWithImages(character, BaseMaterialKind.BattleAvatar, sources);
        var folder = service.GetMaterialFolderPath(character, BaseMaterialKind.BattleAvatar);

        AssertEqual(4, imported);
        AssertEqual(4, Directory.EnumerateFiles(folder, "*.png").Count());
        AssertEqual(false, File.Exists(Path.Combine(folder, $"{character.Code}-BattleAvatar-05.png")));
    });
}

static void BattleAvatarAppendRejectsFifthImage()
{
    WithCharacterWorkspace((service, character, root) =>
    {
        var sourcePath = Path.Combine(root, "source.png");
        WriteImage(sourcePath);
        for (var index = 1; index <= 4; index++)
        {
            service.ImportAtIndex(character, BaseMaterialKind.BattleAvatar, sourcePath, index);
        }

        var rejected = false;
        try
        {
            service.ImportAndCrop(character, BaseMaterialKind.BattleAvatar, sourcePath);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        AssertEqual(true, rejected);
        var folder = service.GetMaterialFolderPath(character, BaseMaterialKind.BattleAvatar);
        AssertEqual(false, File.Exists(Path.Combine(folder, $"{character.Code}-BattleAvatar-05.png")));
    });
}

static void BaseMaterialNumbersFollowCategoryCount()
{
    WithCharacterWorkspace((service, character, root) =>
    {
        var sourcePath = Path.Combine(root, "source.png");
        WriteImage(sourcePath);

        var single = service.ImportAndCrop(character, BaseMaterialKind.ItemIcon, sourcePath);
        AssertEqual($"{character.Code}-ItemIcon-1.png", single.FileName);

        for (var index = 0; index < 10; index++)
        {
            service.ImportAndCrop(character, BaseMaterialKind.OtherImage, sourcePath);
        }

        var expanded = service.LoadSections(character)
            .Single(section => section.Spec.Kind == BaseMaterialKind.OtherImage)
            .Items;
        AssertSequence(
            Enumerable.Range(1, 10).Select(index => $"{character.Code}-OtherImage-{index:00}-source.png").ToArray(),
            expanded.Select(item => item.FileName).ToArray());

        File.Delete(expanded[^1].FilePath);
        var shrunk = service.LoadSections(character)
            .Single(section => section.Spec.Kind == BaseMaterialKind.OtherImage)
            .Items;
        AssertSequence(
            Enumerable.Range(1, 9).Select(index => $"{character.Code}-OtherImage-{index}-source.png").ToArray(),
            shrunk.Select(item => item.FileName).ToArray());
    });
}

static void MaterialSequenceNumberWidthsMatchCategoryCount()
{
    AssertEqual("1", MaterialSequenceNaming.FormatIndex(1, 9));
    AssertEqual("01", MaterialSequenceNaming.FormatIndex(1, 10));
    AssertEqual("09", MaterialSequenceNaming.FormatIndex(9, 99));
    AssertEqual("001", MaterialSequenceNaming.FormatIndex(1, 100));
    AssertEqual("100", MaterialSequenceNaming.FormatIndex(100, 100));
}

static void VoiceSpecsDefineRequiredAndOptionalGroups()
{
    AssertEqual(12, VoiceMaterialService.Specs.Count);
    AssertSequence(
        [
            VoiceMaterialKind.Formation,
            VoiceMaterialKind.Click,
            VoiceMaterialKind.Hurt,
            VoiceMaterialKind.Death,
            VoiceMaterialKind.Defeat,
            VoiceMaterialKind.Victory
        ],
        VoiceMaterialService.Specs.Where(spec => spec.IsRequired).Select(spec => spec.Kind).ToArray());
    AssertSequence(
        [
            VoiceMaterialKind.Skill1,
            VoiceMaterialKind.Skill2,
            VoiceMaterialKind.Ultimate,
            VoiceMaterialKind.Support,
            VoiceMaterialKind.Combo,
            VoiceMaterialKind.Other
        ],
        VoiceMaterialService.Specs.Where(spec => !spec.IsRequired).Select(spec => spec.Kind).ToArray());
    AssertEqual("支持多个，至少 1 个", VoiceMaterialService.GetSpec(VoiceMaterialKind.Formation).RequirementText);
}

static void RequiredVoiceStartsAtOneAndAllowsMore()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var firstSource = Path.Combine(root, "first.wav");
        var replacementSource = Path.Combine(root, "replacement.wav");
        WriteWaveFile(firstSource, 1);
        WriteWaveFile(replacementSource, 2);
        var service = new VoiceMaterialService();

        var first = service.Import(character, VoiceMaterialKind.Formation, firstSource);
        var second = service.Import(character, VoiceMaterialKind.Formation, replacementSource);
        var section = service.LoadSections(character).Single(item => item.Spec.Kind == VoiceMaterialKind.Formation);

        AssertEqual("Misaka-Formation-1.wav", first.FileName);
        AssertEqual("Misaka-Formation-2.wav", second.FileName);
        AssertEqual(false, string.Equals(first.FilePath, second.FilePath, StringComparison.OrdinalIgnoreCase));
        AssertEqual(2, section.Items.Count);
        AssertEqual(VoiceMaterialStatus.Ready, section.Items[0].Status);
        AssertEqual(VoiceMaterialStatus.Ready, section.Items[1].Status);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void MultipleVoicesAreNumberedAndValidated()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var firstSource = Path.Combine(root, "first.wav");
        var secondSource = Path.Combine(root, "second.wav");
        var fakeSource = Path.Combine(root, "fake.wav");
        WriteWaveFile(firstSource, 1);
        WriteWaveFile(secondSource, 2);
        File.WriteAllText(fakeSource, "not a wave file");
        var service = new VoiceMaterialService();

        var imported = service.ImportMany(
            character,
            VoiceMaterialKind.Skill1,
            [firstSource, secondSource]);

        AssertSequence(
            ["Misaka-Skill1-1.wav", "Misaka-Skill1-2.wav"],
            imported.Select(item => item.FileName).ToArray());

        var rejected = false;
        try
        {
            service.Import(character, VoiceMaterialKind.Skill1, fakeSource);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        AssertEqual(true, rejected);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void VoiceNumbersExpandAndShrinkWithCategoryCount()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var sources = Enumerable.Range(1, 10)
            .Select(index => Path.Combine(root, $"voice-{index}.wav"))
            .ToArray();
        for (var index = 0; index < sources.Length; index++)
        {
            WriteWaveFile(sources[index], (byte)(index + 1));
        }

        var service = new VoiceMaterialService();
        service.ImportMany(character, VoiceMaterialKind.Other, sources);
        var expanded = service.LoadSections(character)
            .Single(section => section.Spec.Kind == VoiceMaterialKind.Other)
            .Items;
        AssertSequence(
            Enumerable.Range(1, 10).Select(index => $"Misaka-OtherVoice-{index:00}-voice-{index}.wav").ToArray(),
            expanded.Select(item => item.FileName).ToArray());

        service.Delete(character, expanded[^1]);
        var shrunk = service.LoadSections(character)
            .Single(section => section.Spec.Kind == VoiceMaterialKind.Other)
            .Items;
        AssertSequence(
            Enumerable.Range(1, 9).Select(index => $"Misaka-OtherVoice-{index}-voice-{index}.wav").ToArray(),
            shrunk.Select(item => item.FileName).ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void VoiceRenumberingUpdatesSequenceFrameBindings()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var voiceSources = Enumerable.Range(1, 10)
            .Select(index => Path.Combine(root, $"voice-{index}.wav"))
            .ToArray();
        for (var index = 0; index < voiceSources.Length; index++)
        {
            WriteWaveFile(voiceSources[index], (byte)(index + 1));
        }

        var voiceService = new VoiceMaterialService();
        var voices = voiceService.ImportMany(character, VoiceMaterialKind.Skill1, voiceSources);
        var frameSource = Path.Combine(root, "frame.png");
        WriteSolidImage(frameSource, Color.Red, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        var frameService = new SequenceFrameService();
        var action = SequenceFrameService.BuildActions(new CharacterSkillsData()).First();
        var frame = frameService.ImportFrames(character, action, [frameSource]).Single();
        frameService.SetFrameVoice(character, action, frame, voices[^1].FilePath);

        voiceService.Delete(character, voices[0]);

        var rebound = LoadSequenceSection(frameService, character, action).Frames.Single();
        AssertEqual("Misaka-Skill1-9.wav", Path.GetFileName(rebound.VoiceFilePath));
        AssertEqual(true, File.Exists(rebound.VoiceFilePath));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void PendingVoicesMoveToCategoryAndUpdateBindings()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var formationSource = Path.Combine(root, "formation.wav");
        var pendingFirstSource = Path.Combine(root, "pending-first.wav");
        var pendingSecondSource = Path.Combine(root, "pending-second.wav");
        WriteWaveFile(formationSource, 1);
        WriteWaveFile(pendingFirstSource, 2);
        WriteWaveFile(pendingSecondSource, 3);

        var voiceService = new VoiceMaterialService();
        voiceService.Import(character, VoiceMaterialKind.Formation, formationSource);
        var pending = voiceService.ImportMany(
            character,
            VoiceMaterialKind.Other,
            [pendingFirstSource, pendingSecondSource]);
        var originalPendingPath = pending[1].FilePath;

        var frameSource = Path.Combine(root, "frame.png");
        WriteSolidImage(frameSource, Color.Red, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        var frameService = new SequenceFrameService();
        var action = SequenceFrameService.BuildActions(new CharacterSkillsData()).First();
        var frame = frameService.ImportFrames(character, action, [frameSource]).Single();
        frameService.SetFrameVoice(character, action, frame, originalPendingPath);

        voiceService.MovePendingVoices(character, [pending[1]], VoiceMaterialKind.Formation);

        var sections = voiceService.LoadSections(character);
        var remainingPending = sections.Single(section => section.Spec.Kind == VoiceMaterialKind.Other).Items;
        var formation = sections.Single(section => section.Spec.Kind == VoiceMaterialKind.Formation).Items;
        var rebound = LoadSequenceSection(frameService, character, action).Frames.Single();

        AssertSequence(["Misaka-OtherVoice-1-pending-first.wav"], remainingPending.Select(item => item.FileName).ToArray());
        AssertSequence(["Misaka-Formation-1.wav", "Misaka-Formation-2.wav"], formation.Select(item => item.FileName).ToArray());
        AssertEqual("Misaka-Formation-2.wav", Path.GetFileName(rebound.VoiceFilePath));
        AssertEqual(true, File.Exists(rebound.VoiceFilePath));
        AssertEqual(false, File.Exists(originalPendingPath));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void PendingVoiceDeleteKeyBatchDeletesSelectedItems()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var sources = Enumerable.Range(1, 4)
            .Select(index => Path.Combine(root, $"pending-{index}.wav"))
            .ToArray();
        for (var index = 0; index < sources.Length; index++)
        {
            WriteWaveFile(sources[index], (byte)(index + 1));
        }

        var voiceService = new VoiceMaterialService();
        var pending = voiceService.ImportMany(character, VoiceMaterialKind.Other, sources);
        var frameSource = Path.Combine(root, "frame.png");
        WriteSolidImage(frameSource, Color.Red, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        var frameService = new SequenceFrameService();
        var action = SequenceFrameService.BuildActions(new CharacterSkillsData()).First();
        var frame = frameService.ImportFrames(character, action, [frameSource]).Single();
        frameService.SetFrameVoice(character, action, frame, pending[2].FilePath);

        var deleteMethod = typeof(VoiceMaterialService).GetMethod("DeletePendingVoices")
            ?? throw new InvalidOperationException("VoiceMaterialService 缺少 DeletePendingVoices 批量删除接口。");
        deleteMethod.Invoke(voiceService, [character, new[] { pending[0], pending[2] }]);

        var remaining = voiceService.LoadSections(character)
            .Single(section => section.Spec.Kind == VoiceMaterialKind.Other)
            .Items;
        var rebound = LoadSequenceSection(frameService, character, action).Frames.Single();
        AssertSequence(
            ["Misaka-OtherVoice-1-pending-2.wav", "Misaka-OtherVoice-2-pending-4.wav"],
            remaining.Select(item => item.FileName).ToArray());
        AssertEqual(string.Empty, rebound.VoiceFilePath);

        var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.VoiceMaterials.cs"));
        AssertEqual(true, source.Contains("Windows.System.VirtualKey.Delete", StringComparison.Ordinal));
        AssertEqual(true, source.Contains("DeleteSelectedPendingVoicesAsync", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void VoiceDeleteDialogsOnlyAccentDeleteButton()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.VoiceMaterials.cs"));
    AssertEqual(
        0,
        CountOccurrences(source, "DefaultButton: ContentDialogButton.Secondary"));
    AssertEqual(
        2,
        CountOccurrences(source, "DefaultButton: ContentDialogButton.None"));
    AssertEqual(
        2,
        CountOccurrences(source, "PrimaryButtonStyle: (Style)Application.Current.Resources[\"DialogAccentButtonStyle\"]"));
}

static void PendingVoiceDuplicatesIncludeAssignedAndInternalMatches()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var assignedSource = Path.Combine(root, "assigned.wav");
        var pendingAssignedSource = Path.Combine(root, "pending-assigned.wav");
        var pendingLoopFirstSource = Path.Combine(root, "pending-loop-a.wav");
        var pendingLoopSecondSource = Path.Combine(root, "pending-loop-b.wav");
        var pendingUniqueSource = Path.Combine(root, "pending-unique.wav");
        WriteWaveFile(assignedSource, 1);
        WriteWaveFileWithJunkMetadata(pendingAssignedSource, 1);
        WriteWaveFile(pendingLoopFirstSource, 2);
        WriteWaveFile(pendingLoopSecondSource, 2);
        WriteWaveFile(pendingUniqueSource, 3);

        var service = new VoiceMaterialService();
        service.Import(character, VoiceMaterialKind.Skill1, assignedSource);
        service.ImportMany(
            character,
            VoiceMaterialKind.Other,
            [pendingAssignedSource, pendingLoopFirstSource, pendingLoopSecondSource, pendingUniqueSource]);

        var matches = service.FindPendingVoiceDuplicates(character);
        var assignedMatch = matches.Single(match => match.PendingItem.FileName.Contains("pending-assigned", StringComparison.Ordinal));
        var internalMatches = matches
            .Where(match => match.PendingItem.FileName.Contains("pending-loop", StringComparison.Ordinal))
            .ToArray();

        AssertEqual(3, matches.Count);
        AssertEqual(1, assignedMatch.AssignedMatches.Count);
        AssertEqual(VoiceMaterialKind.Skill1, assignedMatch.AssignedMatches[0].Kind);
        AssertEqual(0, assignedMatch.PendingMatches.Count);
        AssertEqual(2, internalMatches.Length);
        AssertEqual(true, internalMatches.All(match => match.AssignedMatches.Count == 0));
        AssertEqual(true, internalMatches.All(match => match.PendingMatches.Count == 1));
        AssertEqual(false, matches.Any(match => match.PendingItem.FileName.Contains("pending-unique", StringComparison.Ordinal)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void VoicePlaybackUsesCachedCopy()
{
    var root = CreateTemporaryTestFolder();
    string? previewPath = null;
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var firstSource = Path.Combine(root, "first.wav");
        var replacementSource = Path.Combine(root, "replacement.wav");
        WriteWaveFile(firstSource, 1);
        WriteWaveFile(replacementSource, 2);
        var service = new VoiceMaterialService();
        var imported = service.Import(character, VoiceMaterialKind.Formation, firstSource);

        previewPath = VoiceMaterialService.CreatePlaybackCopy(imported.FilePath);
        AssertEqual(false, string.Equals(imported.FilePath, previewPath, StringComparison.OrdinalIgnoreCase));
        AssertSequence(File.ReadAllBytes(imported.FilePath), File.ReadAllBytes(previewPath));

        using (File.Open(previewPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            service.Replace(character, VoiceMaterialKind.Formation, 1, replacementSource);
        }

        AssertSequence(File.ReadAllBytes(replacementSource), File.ReadAllBytes(imported.FilePath));
        var playbackSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.VoiceMaterials.cs"));
        AssertEqual(true, playbackSource.Contains("VoiceMaterialService.CreatePlaybackCopy(filePath)", StringComparison.Ordinal));
        VoiceMaterialService.CleanupPlaybackCopies();
        AssertEqual(false, File.Exists(previewPath));
        previewPath = null;
    }
    finally
    {
        VoiceMaterialService.TryDeletePlaybackCopy(previewPath);
        Directory.Delete(root, recursive: true);
    }
}

static void LineArtRefreshDoesNotResetSections()
{
    WithCharacterWorkspace((service, character, root) =>
    {
        var viewModel = new LineArtViewModel(service);
        viewModel.RefreshAsync(character).GetAwaiter().GetResult();
        var originalCount = viewModel.Sections.Count;
        var minimumCount = originalCount;
        var resetCount = 0;
        viewModel.Sections.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }

            minimumCount = Math.Min(minimumCount, viewModel.Sections.Count);
        };

        var folder = service.GetMaterialFolderPath(character, BaseMaterialKind.BattleAvatar);
        WriteImage(Path.Combine(folder, $"{character.Code}-BattleAvatar-01.png"));
        viewModel.RefreshAsync(character).GetAwaiter().GetResult();

        AssertEqual(0, resetCount);
        AssertEqual(originalCount, minimumCount);
        AssertEqual(originalCount, viewModel.Sections.Count);
    });
}

static void LineArtRefreshLoadsVoiceSections()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var viewModel = new LineArtViewModel(new BaseMaterialService(), new VoiceMaterialService());

        viewModel.RefreshAsync(character).GetAwaiter().GetResult();

        AssertEqual(12, viewModel.VoiceSections.Count);
        AssertEqual(6, viewModel.VoiceSections.Sum(section => section.MissingCount));
        AssertEqual(
            0,
            viewModel.VoiceSections
                .Where(section => !section.Spec.IsRequired)
                .Sum(section => section.MissingCount));
        AssertEqual(true, viewModel.NoticeMessage.Contains("缺少素材 ", StringComparison.Ordinal));
        AssertEqual(false, viewModel.NoticeMessage.Contains("不合规素材 0", StringComparison.Ordinal));
        AssertEqual(false, viewModel.NoticeMessage.Contains("额外文件 0", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void St2VoiceAreaProvidesCompleteControls()
{
    var xamlPath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml");
    var document = XDocument.Load(xamlPath);
    var xamlText = File.ReadAllText(xamlPath);
    var codePath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.VoiceMaterials.cs");
    var codeText = File.ReadAllText(codePath);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var primaryTemplate = document.Descendants().Single(element =>
        element.Name.LocalName == "DataTemplate" &&
        string.Equals(element.Attribute(x + "Key")?.Value, "VoiceMaterialPrimaryItemTemplate", StringComparison.Ordinal));
    var primaryColumns = primaryTemplate.Descendants()
        .First(element => element.Name.LocalName == "Grid.ColumnDefinitions")
        .Elements()
        .Count();
    var primaryContent = document.Descendants().Single(element =>
        element.Name.LocalName == "ContentControl" &&
        string.Equals(
            element.Attribute("Content")?.Value,
            "{Binding PrimaryItem, Mode=OneWay}",
            StringComparison.Ordinal));
    var emptyState = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "VoiceMaterialEmptyStateText",
        StringComparison.Ordinal));
    var managerButton = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "VoiceMaterialManagerButton",
        StringComparison.Ordinal));

    AssertEqual(true, document.Descendants().Any(element => element.Attributes().Any(attribute =>
        attribute.Value.Contains("LineArt.RequiredVoiceSections", StringComparison.Ordinal))));
    AssertEqual(true, document.Descendants().Any(element => element.Attributes().Any(attribute =>
        attribute.Value.Contains("LineArt.OptionalVoiceSections", StringComparison.Ordinal))));
    AssertEqual(true, xamlText.Contains("AddVoiceMaterialButton_Click", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("PlayVoiceMaterialButton_Click", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("ReplaceVoiceMaterialButton_Click", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("DeleteVoiceMaterialButton_Click", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("x:Name=\"VoiceMaterialManagerButton\"", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("Click=\"OpenVoiceMaterialManagerButton_Click\"", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("x:Name=\"VoiceMaterialManagerHost\"", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("x:Name=\"VoiceMaterialManagerListView\"", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("KeyDown=\"VoiceMaterialManagerHost_KeyDown\"", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("RightTapped=\"VoiceMaterialManagerHost_RightTapped\"", StringComparison.Ordinal));
    AssertEqual(true, xamlText.Contains("Content=\"{Binding PrimaryItem, Mode=OneWay}\"", StringComparison.Ordinal));
    AssertEqual("Stretch", primaryContent.Attribute("HorizontalContentAlignment")?.Value ?? string.Empty);
    AssertEqual(
        "{Binding HasItems, Converter={StaticResource BooleanToVisibilityConverter}, Mode=OneWay}",
        primaryContent.Attribute("Visibility")?.Value ?? string.Empty);
    AssertEqual("未设置", emptyState.Attribute("Text")?.Value ?? string.Empty);
    AssertEqual(
        "{Binding HasItems, Converter={StaticResource BooleanToVisibilityConverter}, ConverterParameter=Invert, Mode=OneWay}",
        emptyState.Attribute("Visibility")?.Value ?? string.Empty);
    AssertEqual(
        "{Binding HasItems, Converter={StaticResource BooleanToVisibilityConverter}, Mode=OneWay}",
        managerButton.Attribute("Visibility")?.Value ?? string.Empty);
    AssertEqual(6, primaryColumns);
    AssertEqual(true, codeText.Contains("ShowVoiceMaterialManager", StringComparison.Ordinal));
    AssertEqual(true, codeText.Contains("HideVoiceMaterialManager", StringComparison.Ordinal));
    AssertEqual(true, codeText.Contains("Windows.System.VirtualKey.Escape", StringComparison.Ordinal));
    AssertEqual(true, codeText.Contains("PickWaveFilesAsync(allowMultiple: true)", StringComparison.Ordinal));
    AssertEqual(true, File.Exists(codePath));
}

static void DialogsFollowCurrentWindowTheme()
{
    var source = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(),
        "Services",
        "WinUiDialogService.cs"));

    AssertEqual(true, source.Contains("var xamlRoot = _getXamlRoot();", StringComparison.Ordinal));
    AssertEqual(true, source.Contains(
        "RequestedTheme = (xamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default",
        StringComparison.Ordinal));
}

static void PendingVoiceManagerSupportsAssignment()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.VoiceMaterials.cs"));

    AssertEqual(true, xaml.Contains("x:Name=\"VoiceMaterialAssignmentBar\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"VoiceMaterialManagerSelectionText\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"AssignVoiceMaterialsButton\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"CheckVoiceMaterialDuplicatesButton\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"ShowAllVoiceMaterialsButton\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Text=\"{Binding DuplicateStatusText, Mode=OneWay}\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("SelectionChanged=\"VoiceMaterialManagerListView_SelectionChanged\"", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("ListViewSelectionMode.Extended", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("AssignSelectedVoiceMaterialsAsync", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("MovePendingVoicesAsync", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("FindPendingVoiceDuplicatesAsync", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("ShowPendingVoiceDuplicateResults", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("ClearVoiceMaterialManagerSelection", StringComparison.Ordinal));
    AssertEqual(true, source.Contains(
        "VoiceMaterialManagerListView.SelectionMode == ListViewSelectionMode.None",
        StringComparison.Ordinal));
    AssertEqual(1, CountOccurrences(
        source,
        "VoiceMaterialManagerListView.SelectedItems.Clear();"));
}

static void St2WatchesImageAndVoiceFolders()
{
    var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.BaseMaterials.cs");
    var source = File.ReadAllText(sourcePath);

    AssertEqual(true, source.Contains("_voiceMaterialWatcher", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("Path.Combine(CharacterDesk.CurrentCharacter.FolderPath, \"Sound\")", StringComparison.Ordinal));
}

static void ProductionStatusIncludesRequiredVoices()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");

        var status = new ProductionStatusService().Evaluate(character);

        AssertEqual(true, status.Message.Contains("必填语音缺少 6 个", StringComparison.Ordinal));
        AssertEqual(false, status.Message.Contains("图片缺少 0", StringComparison.Ordinal));
        AssertEqual(false, status.Message.Contains("图片不合规 0", StringComparison.Ordinal));
        AssertEqual(false, status.Message.Contains("额外图片 0", StringComparison.Ordinal));
        AssertEqual(false, status.Message.Contains("语音不合规 0", StringComparison.Ordinal));
        AssertEqual(false, status.Message.Contains("待处理动作 0", StringComparison.Ordinal));
        AssertEqual(false, status.Message.Contains("缺图标 BUFF 0", StringComparison.Ordinal));
        AssertEqual(false, status.CanComplete);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void St2BuffIconsAreOptional()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "ALO_Yuki", "优纪 ALO");
        var section = new BaseMaterialService()
            .LoadSections(character)
            .Single(item => item.Spec.Kind == BaseMaterialKind.BuffIcon);

        AssertEqual(0, section.Spec.MinimumCount);
        AssertEqual("可留空", section.Spec.CountRequirementText);
        AssertEqual(0, section.MissingCount);
        AssertEqual(false, section.HasWarning);
        AssertEqual("未设置，可留空", section.StatusText);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void FreeCanvasPreservesTransparentPadding()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var sourcePath = Path.Combine(root, "source.png");
        var outputPath = Path.Combine(root, "output.png");
        WriteSolidImage(sourcePath, Color.Red, 100, 100);

        BaseMaterialService.SaveCropToPng(
            sourcePath,
            outputPath,
            new Rectangle(-50, 0, 200, 100),
            200,
            100,
            allowTransparentPadding: true);

        using var output = new Bitmap(outputPath);
        AssertEqual(0, output.GetPixel(10, 50).A);
        AssertEqual(255, output.GetPixel(100, 50).A);
        AssertEqual(0, output.GetPixel(190, 50).A);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void FreeCanvasAllowsFullyTransparentOutput()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var sourcePath = Path.Combine(root, "source.png");
        var outputPath = Path.Combine(root, "output.png");
        WriteSolidImage(sourcePath, Color.Red, 100, 100);

        BaseMaterialService.SaveCropToPng(
            sourcePath,
            outputPath,
            new Rectangle(200, 0, 100, 100),
            100,
            100,
            allowTransparentPadding: true);

        using var output = new Bitmap(outputPath);
        AssertEqual(0, output.GetPixel(50, 50).A);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void KeywordTagNamesAreAutomaticAndReadOnly()
{
    var category = CharacterKeywordTagCategory.CreateCharacterNames("Misaka", "御坂美琴");

    AssertEqual(true, category.IsReadOnly);
    AssertSequence(["Misaka", "御坂美琴"], category.Entries.Select(entry => entry.Value).ToArray());
    AssertEqual(false, category.TryAddEntry());
    AssertEqual(false, category.TryRemoveEntry(category.Entries[0]));
}

static void KeywordTagCategoryKeepsOneEntry()
{
    var category = CharacterKeywordTagCategory.CreateEditable(
        CharacterKeywordTagCategoryKind.Work,
        "所属作品",
        []);

    AssertEqual(1, category.Entries.Count);
    AssertEqual(false, category.TryRemoveEntry(category.Entries[0]));
    AssertEqual(true, category.TryAddEntry());
    AssertEqual(2, category.Entries.Count);
    AssertEqual(true, category.TryRemoveEntry(category.Entries[1]));
    AssertEqual(1, category.Entries.Count);
}

static void KeywordTagMissingCategoriesAreReported()
{
    var groups = new CharacterKeywordTagGroups
    {
        Works = { "某科学的超电磁炮" },
        Periods = { "大霸星祭篇" },
        AbilityTypes = { "电击使" },
        Affiliations = { "常盘台中学" }
    };

    AssertSequence(
        ["外号"],
        CharacterKeywordTagRules.GetMissingCategoryNames("Misaka", "御坂美琴", groups).ToArray());
}

static void KeywordTagsFlattenAndPreserveLegacyValues()
{
    var groups = new CharacterKeywordTagGroups
    {
        Works = { "某科学的超电磁炮" },
        Periods = { "大霸星祭篇" },
        AbilityTypes = { "电击使" },
        Affiliations = { "常盘台中学" },
        Aliases = { "超电磁炮", " misaka " },
        LegacyKeywordTags = { "超能力者", "御坂美琴" }
    };

    AssertSequence(
        ["Misaka", "御坂美琴", "某科学的超电磁炮", "大霸星祭篇", "电击使", "常盘台中学", "超电磁炮", "超能力者"],
        CharacterKeywordTagRules.Flatten("Misaka", "御坂美琴", groups).ToArray());
}

static void KeywordTagCategoriesRoundTripThroughCharacterInfo()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var service = new CharacterInfoService();
        var legacyData = new CharacterInfoData
        {
            Code = character.Code,
            Name = character.Name,
            KeywordTags = { "超能力者" }
        };
        service.Save(character, legacyData);

        var viewModel = new UnrealSyncViewModel(service);
        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        AssertEqual(6, viewModel.KeywordTagCategories.Count);
        AssertSequence(
            ["超能力者"],
            viewModel.KeywordTagCategories
                .Single(category => category.Kind == CharacterKeywordTagCategoryKind.Alias)
                .GetValues()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray());

        SetFirstTag(viewModel, CharacterKeywordTagCategoryKind.Work, "某科学的超电磁炮");
        SetFirstTag(viewModel, CharacterKeywordTagCategoryKind.Period, "大霸星祭篇");
        SetFirstTag(viewModel, CharacterKeywordTagCategoryKind.AbilityType, "电击使");
        SetFirstTag(viewModel, CharacterKeywordTagCategoryKind.Affiliation, "常盘台中学");
        var aliases = viewModel.KeywordTagCategories.Single(category => category.Kind == CharacterKeywordTagCategoryKind.Alias);
        AssertEqual(true, aliases.TryAddEntry());
        aliases.Entries[^1].Value = "超电磁炮";
        AssertEqual(true, viewModel.SaveNow(character));

        var reloaded = service.Load(character);
        AssertSequence(["某科学的超电磁炮"], reloaded.KeywordTagGroups!.Works.ToArray());
        AssertSequence(
            ["Misaka", "御坂美琴", "某科学的超电磁炮", "大霸星祭篇", "电击使", "常盘台中学", "超能力者", "超电磁炮"],
            reloaded.KeywordTags.ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void LegacyKeywordTagsMigrateToAliasesAndRemoveLegacyUi()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "ALO_Yuki", "优纪 ALO");
        var legacyData = new CharacterInfoData
        {
            Code = character.Code,
            Name = character.Name,
            KeywordTagGroups = new CharacterKeywordTagGroups
            {
                Works = { "刀剑神域" },
                Aliases = { "已有外号", "Alice" },
                LegacyKeywordTags = { "优纪", " alice " }
            },
            KeywordTags = { "ALO_Yuki", "优纪 ALO", "刀剑神域", "异能攻击", "Alice" }
        };
        new CharacterToolboxDataService().Update(character, data => data.CharacterInfo = legacyData);

        var loaded = new CharacterInfoService().Load(character);
        var persisted = new CharacterToolboxDataService().Load(character).CharacterInfo!;

        AssertSequence(["已有外号", "Alice", "优纪", "异能攻击"], loaded.KeywordTagGroups!.Aliases.ToArray());
        AssertEqual(0, loaded.KeywordTagGroups.LegacyKeywordTags.Count);
        AssertSequence(loaded.KeywordTagGroups.Aliases.ToArray(), persisted.KeywordTagGroups!.Aliases.ToArray());
        AssertEqual(0, persisted.KeywordTagGroups.LegacyKeywordTags.Count);

        var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "ViewModels", "UnrealSyncViewModel.cs"));
        AssertEqual(false, xaml.Contains("旧版未分类 Tag", StringComparison.Ordinal));
        AssertEqual(false, xaml.Contains("HasLegacyKeywordTags", StringComparison.Ordinal));
        AssertEqual(false, viewModel.Contains("LegacyKeywordTagCategory", StringComparison.Ordinal));
        AssertEqual(false, viewModel.Contains("HasLegacyKeywordTags", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void KeywordTagCategoryPanelAllowsDynamicHeight()
{
    var xamlPath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml");
    var document = XDocument.Load(xamlPath);
    var categoryItemsControl = document
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "ItemsControl" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "ItemsSource" &&
                attribute.Value.Contains("KeywordTagCategories", StringComparison.Ordinal)));
    var itemsPanel = categoryItemsControl
        .Descendants()
        .First(element => element.Name.LocalName is "ItemsWrapGrid" or "StackPanel");

    AssertEqual("StackPanel", itemsPanel.Name.LocalName);
}

static void CharacterAntiSwitchDefaultsAndPersists()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "ALO_Yuki", "优纪 ALO");
        var service = new CharacterInfoService();
        var viewModel = new UnrealSyncViewModel(service);
        viewModel.LoadAsync(character).GetAwaiter().GetResult();

        AssertEqual(false, viewModel.IsAnti);
        viewModel.IsAnti = true;
        AssertEqual(true, viewModel.SaveNow(character));
        AssertEqual(true, service.Load(character).Anti);

        var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
        var formLimitIndex = xaml.IndexOf("UnrealSync.FormLimitStat", StringComparison.Ordinal);
        var antiIndex = xaml.IndexOf("Text=\"抗性类别\"", StringComparison.Ordinal);
        AssertEqual(true, formLimitIndex >= 0 && antiIndex > formLimitIndex);
        AssertEqual(true, xaml.Contains("IsOn=\"{Binding UnrealSync.IsAnti, Mode=TwoWay}\"", StringComparison.Ordinal));
        AssertEqual(true, xaml.Contains("OffContent=\"物理\"", StringComparison.Ordinal));
        AssertEqual(true, xaml.Contains("OnContent=\"异能\"", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CharacterDeskUsesScrollableCardsAndDetailDialog()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var characterDeskSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.CharacterDesk.cs"));
    var navigationSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.Navigation.cs"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var characterCardsScrollViewer = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "CharacterCardsScrollViewer",
        StringComparison.Ordinal));
    var detailHost = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "CharacterDetailHost",
        StringComparison.Ordinal));

    AssertEqual("ScrollViewer", characterCardsScrollViewer.Name.LocalName);
    AssertEqual("Auto", characterCardsScrollViewer.Attribute("VerticalScrollBarVisibility")?.Value ?? string.Empty);
    var characterCardsPanel = characterCardsScrollViewer.Descendants().Single(element =>
        element.Name.LocalName == "ItemsWrapGrid");
    AssertEqual(null, characterCardsPanel.Attribute("MaximumRowsOrColumns")?.Value);
    AssertEqual("Grid", detailHost.Name.LocalName);
    AssertEqual("Collapsed", detailHost.Attribute("Visibility")?.Value ?? string.Empty);
    AssertEqual("CharacterDetailHost_KeyDown", detailHost.Attribute("KeyDown")?.Value ?? string.Empty);
    AssertEqual("CharacterDetailHost_RightTapped", detailHost.Attribute("RightTapped")?.Value ?? string.Empty);
    AssertEqual("CharacterDetailHost_Tapped", detailHost.Attribute("Tapped")?.Value ?? string.Empty);
    foreach (var buttonName in new[]
    {
        "CharacterDetailContinueButton",
        "CharacterDetailExportButton",
        "CharacterDetailOpenFolderButton",
        "CharacterDetailUnrealSyncButton"
    })
    {
        AssertEqual(true, document.Descendants().Any(element => string.Equals(
            element.Attribute(x + "Name")?.Value,
            buttonName,
            StringComparison.Ordinal)));
    }

    AssertEqual(true, characterDeskSource.Contains("ShowCharacterDetail(character);", StringComparison.Ordinal));
    AssertEqual(true, characterDeskSource.Contains("HideCharacterDetail();", StringComparison.Ordinal));
    AssertEqual(true, navigationSource.Contains("CharacterDetailHost.Visibility == Visibility.Visible", StringComparison.Ordinal));
}

static void CompletingCharacterReturnsToDeskAndOpensDetail()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.ProductionStatus.cs"));
    var methodStart = source.IndexOf("private async void CompleteCharacterButton_Click", StringComparison.Ordinal);
    AssertEqual(true, methodStart >= 0);
    var method = source[methodStart..];
    var replaceIndex = method.IndexOf("CharacterDesk.ReplaceCharacter(completed);", StringComparison.Ordinal);
    var deskIndex = method.IndexOf("ShowCharacterDeskPage();", StringComparison.Ordinal);
    var detailIndex = method.IndexOf("ShowCharacterDetail(completed);", StringComparison.Ordinal);

    AssertEqual(true, replaceIndex >= 0);
    AssertEqual(true, deskIndex > replaceIndex);
    AssertEqual(true, detailIndex > deskIndex);
}

static void CharactersLoadOnlyFromFixedStateFolders()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var legacyPath = CreateWorkspaceCharacterFolder(root, "Legacy", "旧根目录角色", isCompleted: false);
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Draft"), "Misaka", "御坂美琴", isCompleted: false);
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Completed"), "Kuroko", "白井黑子", isCompleted: true);
        var service = new CharacterWorkspaceService();

        var characters = service.LoadCharacters(root);

        AssertSequence(["Kuroko", "Misaka"], characters.Select(character => character.Code).OrderBy(code => code).ToArray());
        AssertEqual(true, Directory.Exists(legacyPath));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CompletedCharactersUseCompletedFolder()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var completedPath = CreateWorkspaceCharacterFolder(Path.Combine(root, "Completed"), "Misaka", "御坂美琴", isCompleted: true);
        Directory.CreateDirectory(Path.Combine(root, "Export"));
        var service = new CharacterWorkspaceService();

        var character = service.LoadCharacters(root).Single();

        AssertEqual(Path.GetFullPath(completedPath), Path.GetFullPath(character.FolderPath));
        AssertEqual(true, character.IsCompleted);
        AssertEqual(true, Directory.Exists(Path.Combine(root, "Draft")));
        AssertEqual(true, Directory.Exists(Path.Combine(root, "Completed")));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void NewCharactersUseDraftFolderAndCodesAreUniqueAcrossBothLevels()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Completed"), "misaka", "已完成御坂美琴", isCompleted: true);
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Draft"), "misaka_02", "草稿御坂美琴", isCompleted: false);
        var service = new CharacterWorkspaceService();

        var created = service.CreateCharacter(root, "misaka").Character;
        var ensured = service.EnsureCharacterByCode(root, "Kuroko", "白井黑子").Character;

        AssertEqual("misaka_03", created.Code);
        AssertEqual(Path.GetFullPath(Path.Combine(root, "Draft", "misaka_03")), Path.GetFullPath(created.FolderPath));
        AssertEqual(Path.GetFullPath(Path.Combine(root, "Draft", "Kuroko")), Path.GetFullPath(ensured.FolderPath));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CompletionStateMovesCharacterBetweenRootAndDraft()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var service = new CharacterWorkspaceService();
        var draft = service.EnsureCharacterByCode(root, "Misaka", "御坂美琴").Character;

        var completed = service.SetCompleted(draft, isCompleted: true);
        AssertEqual(true, completed.IsCompleted);
        AssertEqual(Path.GetFullPath(Path.Combine(root, "Completed", "Misaka")), Path.GetFullPath(completed.FolderPath));
        AssertEqual(false, Directory.Exists(Path.Combine(root, "Draft", "Misaka")));

        var reopened = service.SetCompleted(completed, isCompleted: false);
        AssertEqual(false, reopened.IsCompleted);
        AssertEqual(Path.GetFullPath(Path.Combine(root, "Draft", "Misaka")), Path.GetFullPath(reopened.FolderPath));
        AssertEqual(false, Directory.Exists(Path.Combine(root, "Completed", "Misaka")));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void DuplicateCharacterCodesAcrossLevelsAreRejected()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Completed"), "Misaka", "已完成御坂美琴", isCompleted: true);
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Draft"), "Misaka", "草稿御坂美琴", isCompleted: false);
        var rejected = false;
        try
        {
            new CharacterWorkspaceService().LoadCharacters(root);
        }
        catch (IOException)
        {
            rejected = true;
        }

        AssertEqual(true, rejected);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CompletionMoveConflictPreservesDraftState()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var service = new CharacterWorkspaceService();
        var draft = service.EnsureCharacterByCode(root, "Misaka", "草稿御坂美琴").Character;
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Completed"), "Misaka", "已完成御坂美琴", isCompleted: true);
        var rejected = false;
        try
        {
            service.SetCompleted(draft, isCompleted: true);
        }
        catch (IOException)
        {
            rejected = true;
        }

        var metadata = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(draft.ToolFolderPath, "character.json")),
            AppJsonSerializerContext.Default.CharacterMetadata);
        AssertEqual(true, rejected);
        AssertEqual(false, metadata?.IsCompleted ?? true);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void RenamingCharacterCodeChecksBothLevels()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var service = new CharacterWorkspaceService();
        var draft = service.EnsureCharacterByCode(root, "Misaka", "御坂美琴").Character;
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Completed"), "Kuroko", "白井黑子", isCompleted: true);
        var rejected = false;
        try
        {
            service.RenameCharacterCode(draft, "Kuroko");
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        AssertEqual(true, rejected);
        AssertEqual(true, Directory.Exists(draft.FolderPath));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CharacterDeskCardClickOnlyOpensDetail()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.CharacterDesk.cs"));
    var methodStart = source.IndexOf("private void CharacterCardButton_Click", StringComparison.Ordinal);
    AssertEqual(true, methodStart >= 0);
    var methodEnd = source.IndexOf("private ", methodStart + 20, StringComparison.Ordinal);
    var method = source[methodStart..methodEnd];

    AssertEqual(true, method.Contains("ShowCharacterDetail(character);", StringComparison.Ordinal));
    AssertEqual(false, method.Contains("SetCurrentCharacterAsync", StringComparison.Ordinal));
    AssertEqual(false, method.Contains("PersistCurrentCharacterSelection", StringComparison.Ordinal));
}

static void St1ListsAllDraftCharacters()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Draft"), "Misaka", "御坂美琴", isCompleted: false);
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Draft"), "Kuroko", "白井黑子", isCompleted: false);
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Completed"), "Asuna", "亚丝娜", isCompleted: true);
        var viewModel = new CharacterDeskViewModel(new CharacterWorkspaceService());
        viewModel.LoadCharactersAsync(root, null, null).GetAwaiter().GetResult();
        var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));

        AssertSequence(["Kuroko", "Misaka"], viewModel.DraftCharacters.Select(character => character.Code).OrderBy(code => code).ToArray());
        AssertEqual(false, viewModel.DraftCharacters.Any(character => character.IsCompleted));
        AssertEqual(true, xaml.Contains("ItemsSource=\"{Binding CharacterDesk.DraftCharacters, Mode=OneWay}\"", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void ReopeningCompletedCharacterMovesToDraft()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Completed"), "Misaka", "御坂美琴", isCompleted: true);
        var viewModel = new CharacterDeskViewModel(new CharacterWorkspaceService());
        viewModel.LoadCharactersAsync(root, "Misaka", "Misaka").GetAwaiter().GetResult();
        var completed = viewModel.CurrentCharacter ?? throw new InvalidOperationException("没有加载已完成角色。");

        var draft = viewModel.ReopenCompletedCharacterAsync(completed).GetAwaiter().GetResult();

        AssertEqual(false, draft.IsCompleted);
        AssertEqual(Path.GetFullPath(Path.Combine(root, "Draft", "Misaka")), Path.GetFullPath(draft.FolderPath));
        AssertEqual(false, Directory.Exists(Path.Combine(root, "Completed", "Misaka")));
        AssertEqual(false, viewModel.CurrentCharacter?.IsCompleted ?? true);
        AssertEqual(true, viewModel.DraftCharacters.Any(character => character.Code == "Misaka"));
        AssertEqual(false, viewModel.CompletedCharacters.Any(character => character.Code == "Misaka"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CharacterDetailContinueReopensBeforeNavigation()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.CharacterDesk.cs"));
    var methodStart = source.IndexOf("private async void CharacterDetailContinueButton_Click", StringComparison.Ordinal);
    AssertEqual(true, methodStart >= 0);
    var methodEnd = source.IndexOf("private ", methodStart + 20, StringComparison.Ordinal);
    var method = source[methodStart..methodEnd];
    var reopenIndex = method.IndexOf("ReopenCompletedCharacterAsync", StringComparison.Ordinal);
    var hideIndex = method.IndexOf("HideCharacterDetail();", StringComparison.Ordinal);
    var navigationIndex = method.IndexOf("ShowLastEditedPage", StringComparison.Ordinal);

    AssertEqual(true, reopenIndex >= 0);
    AssertEqual(true, hideIndex > reopenIndex);
    AssertEqual(true, navigationIndex > hideIndex);
}

static void CharacterDetailContinueSelectsDraftBeforeNavigation()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.CharacterDesk.cs"));
    var methodStart = source.IndexOf("private async void CharacterDetailContinueButton_Click", StringComparison.Ordinal);
    AssertEqual(true, methodStart >= 0);
    var methodEnd = source.IndexOf("private ", methodStart + 20, StringComparison.Ordinal);
    var method = source[methodStart..methodEnd];
    var selectIndex = method.IndexOf("SetCurrentCharacterAsync(character)", StringComparison.Ordinal);
    var hideIndex = method.IndexOf("HideCharacterDetail();", StringComparison.Ordinal);
    var navigationIndex = method.IndexOf("ShowLastEditedPage", StringComparison.Ordinal);

    AssertEqual(true, selectIndex >= 0);
    AssertEqual(true, hideIndex > selectIndex);
    AssertEqual(true, navigationIndex > hideIndex);
}

static void CharacterDetailUnrealSyncSelectsCharacterBeforeNavigation()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.CharacterDesk.cs"));
    var methodStart = source.IndexOf("private async void CharacterDetailUnrealSyncButton_Click", StringComparison.Ordinal);
    AssertEqual(true, methodStart >= 0);
    var methodEnd = source.IndexOf("private ", methodStart + 20, StringComparison.Ordinal);
    var method = source[methodStart..methodEnd];
    var selectIndex = method.IndexOf("SetCurrentCharacterAsync(character)", StringComparison.Ordinal);
    var hideIndex = method.IndexOf("HideCharacterDetail();", StringComparison.Ordinal);
    var navigationIndex = method.IndexOf("ShowUnrealProjectSyncPage();", StringComparison.Ordinal);

    AssertEqual(true, selectIndex >= 0);
    AssertEqual(true, hideIndex > selectIndex);
    AssertEqual(true, navigationIndex > hideIndex);
}

static void CompletedCharacterCannotEnterProductionSteps()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.Navigation.cs"));
    AssertEqual(true, source.Contains("TryEnterCharacterEditingPage", StringComparison.Ordinal));
    var st1Start = source.IndexOf("private void ShowSt1DesignPage", StringComparison.Ordinal);
    var st1End = source.IndexOf("private ", st1Start + 20, StringComparison.Ordinal);
    AssertEqual(false, source[st1Start..st1End].Contains("TryEnterCharacterEditingPage()", StringComparison.Ordinal));
    foreach (var methodName in new[]
    {
        "ShowSt2MaterialPage",
        "ShowSt3CharacterInfoPage",
        "ShowSt4SkillsPage",
        "ShowSt5SequenceFramesPage",
        "ShowSt6BuffsPage"
    })
    {
        var methodStart = source.IndexOf($"private void {methodName}", StringComparison.Ordinal);
        AssertEqual(true, methodStart >= 0);
        var methodEnd = source.IndexOf("private ", methodStart + 20, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];
        AssertEqual(true, method.Contains("TryEnterCharacterEditingPage()", StringComparison.Ordinal));
    }
}

static void ExportCharacterCopiesWholeFolderAndRequiresOverwrite()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var service = new CharacterWorkspaceService();
        var character = service.EnsureCharacterByCode(root, "Misaka", "御坂美琴").Character;
        var nestedSource = Path.Combine(character.FolderPath, "tool", "CharacterBackups", "history.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedSource)!);
        File.WriteAllText(nestedSource, "backup");
        var exportRoot = service.GetDefaultExportRootPath(root);

        var exportedPath = service.ExportCharacterFolder(character, exportRoot, overwrite: false);

        AssertEqual(Path.GetFullPath(Path.Combine(root, "Export", "Misaka")), Path.GetFullPath(exportedPath));
        AssertEqual("backup", File.ReadAllText(Path.Combine(exportedPath, "tool", "CharacterBackups", "history.zip")));
        AssertEqual(true, File.Exists(nestedSource));

        File.WriteAllText(Path.Combine(exportedPath, "stale.txt"), "stale");
        var refused = false;
        try
        {
            service.ExportCharacterFolder(character, exportRoot, overwrite: false);
        }
        catch (IOException)
        {
            refused = true;
        }

        AssertEqual(true, refused);
        service.ExportCharacterFolder(character, exportRoot, overwrite: true);
        AssertEqual(false, File.Exists(Path.Combine(exportedPath, "stale.txt")));
        AssertEqual(true, File.Exists(nestedSource));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CharacterDetailUsesFolderExportFlow()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.CharacterDesk.cs"));

    AssertEqual(true, xaml.Contains("x:Name=\"CharacterDetailExportButton\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Content=\"导出角色\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Click=\"CharacterDetailExportButton_Click\"", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("GetDefaultExportRootPath(Settings.ProjectRootPath)", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("ExportCharacterFolderAsync", StringComparison.Ordinal));
}

static void SwitchingCharacterDoesNotRaiseDraftEdited()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var workspaceService = new CharacterWorkspaceService();
        var first = CreateCharacter(Path.Combine(root, "First"), "First", "第一个角色");
        var second = CreateCharacter(Path.Combine(root, "Second"), "Second", "第二个角色");
        Directory.CreateDirectory(first.ReferenceFolderPath);
        Directory.CreateDirectory(second.ReferenceFolderPath);
        workspaceService.SaveDraft(first, "不能丢失的草稿");

        var viewModel = new CharacterDeskViewModel(workspaceService);
        viewModel.SetCurrentCharacterAsync(first).GetAwaiter().GetResult();
        viewModel.OpenCurrentCharacterDraftAsync().GetAwaiter().GetResult();
        var editedCount = 0;
        viewModel.DraftTextEdited += (_, _) => editedCount++;

        viewModel.SetCurrentCharacterAsync(second).GetAwaiter().GetResult();

        AssertEqual(0, editedCount);
        AssertEqual(string.Empty, viewModel.DraftText);
        AssertEqual("不能丢失的草稿", workspaceService.LoadDraft(first));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void ReplacingSameCharacterPreservesOpenDraft()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var workspaceService = new CharacterWorkspaceService();
        var character = CreateCharacter(Path.Combine(root, "Character"), "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ReferenceFolderPath);
        workspaceService.SaveDraft(character, "不能丢失的草稿");
        var viewModel = new CharacterDeskViewModel(workspaceService);
        viewModel.SetCurrentCharacterAsync(character).GetAwaiter().GetResult();
        viewModel.OpenCurrentCharacterDraftAsync().GetAwaiter().GetResult();
        var editedCount = 0;
        viewModel.DraftTextEdited += (_, _) => editedCount++;

        viewModel.ReplaceCharacter(character with { DisplayName = "御坂美琴（已刷新）" }, character.Code);

        AssertEqual(0, editedCount);
        AssertEqual(true, viewModel.IsDraftOpen);
        AssertEqual("不能丢失的草稿", viewModel.DraftText);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void PassiveSkillTextChangeTriggersSave()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var service = new CharacterInfoService();
        service.Save(character, new CharacterInfoData
        {
            Code = character.Code,
            Name = character.Name,
            PassiveSkills = { string.Empty }
        });
        var viewModel = new UnrealSyncViewModel(service);
        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        var editedCount = 0;
        viewModel.CharacterInfoEdited += (_, _) => editedCount++;

        viewModel.PassiveSkills[0].Value = "电击使被动循环";

        AssertEqual(1, editedCount);
        AssertEqual(true, viewModel.SaveNow(character));
        AssertSequence(["电击使被动循环"], service.Load(character).PassiveSkills.ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void PageScrollPositionsRemainIndependent()
{
    var positions = new PageScrollPositionStore();

    positions.Save("UnrealSync", 640.5);
    positions.Save("Skills", 128.25);

    AssertEqual(640.5, positions.Get("UnrealSync"));
    AssertEqual(128.25, positions.Get("Skills"));
    AssertEqual(0d, positions.Get("Settings"));
}

static void MainPageScrollViewersHaveStableNames()
{
    var xamlPath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml");
    var document = XDocument.Load(xamlPath);
    XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    var names = document
        .Descendants()
        .Where(element => element.Name.LocalName == "ScrollViewer")
        .Select(element => (string?)element.Attribute(xaml + "Name"))
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .ToHashSet(StringComparer.Ordinal);

    AssertSequence(
        [
            "LineArtScrollViewer",
            "UnrealSyncScrollViewer",
            "SkillsScrollViewer",
            "SequenceFramesScrollViewer",
            "BuffsScrollViewer",
            "UnrealProjectSyncPage",
            "SettingsPage"
        ],
        new[]
        {
            "LineArtScrollViewer",
            "UnrealSyncScrollViewer",
            "SkillsScrollViewer",
            "SequenceFramesScrollViewer",
            "BuffsScrollViewer",
            "UnrealProjectSyncPage",
            "SettingsPage"
        }.Where(names.Contains).ToArray());
}

static void ShellNavigationMenuIsScrollable()
{
    var xamlPath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml");
    var document = XDocument.Load(xamlPath);
    XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    var navigation = document
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "NavigationView" &&
            (string?)element.Attribute(xaml + "Name") == "ShellNavigation");
    var footerItems = navigation
        .Elements()
        .Single(element => element.Name.LocalName == "NavigationView.FooterMenuItems");
    var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.Navigation.cs");
    var source = File.ReadAllText(sourcePath);

    AssertEqual("ShellNavigation_Loaded", (string?)navigation.Attribute("Loaded"));
    AssertEqual(true, footerItems.Descendants().Any(element =>
        (string?)element.Attribute(xaml + "Name") == "GlobalSettingsNavItem"));
    AssertEqual(true, source.Contains("MenuItemsScrollViewer", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("VerticalScrollMode = ScrollMode.Enabled", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("IsVerticalRailEnabled = true", StringComparison.Ordinal));
}

static void BuffEditorUsesFixedIdentityAndScrollableProperties()
{
    var xamlPath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml");
    var document = XDocument.Load(xamlPath);
    XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    var buffEditor = document.Descendants().Single(element =>
        (string?)element.Attribute(xaml + "Name") == "BuffEditorCard");
    var identityPanel = buffEditor.Descendants().Single(element =>
        (string?)element.Attribute(xaml + "Name") == "BuffEditorIdentityPanel");
    var propertiesScrollViewer = buffEditor.Descendants().Single(element =>
        (string?)element.Attribute(xaml + "Name") == "BuffEditorPropertiesScrollViewer");
    var sectionTitles = buffEditor
        .Descendants()
        .Where(element => element.Name.LocalName == "TextBlock")
        .Select(element => (string?)element.Attribute("Text"))
        .Where(text => !string.IsNullOrWhiteSpace(text))
        .ToHashSet(StringComparer.Ordinal);

    AssertEqual("1", (string?)propertiesScrollViewer.Attribute("Grid.Column"));
    AssertEqual("Auto", (string?)propertiesScrollViewer.Attribute("VerticalScrollBarVisibility"));
    AssertEqual(false, identityPanel.Ancestors().Contains(propertiesScrollViewer));
    AssertEqual(true, sectionTitles.Contains("基础定义"));
    AssertEqual(true, sectionTitles.Contains("数值与触发"));
    AssertEqual(true, sectionTitles.Contains("说明与条件"));
    AssertEqual(true, sectionTitles.Contains("原始来源"));
}

static void ReplacingBuffIconRefreshesVersionedUri()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var firstSource = Path.Combine(root, "first.png");
        var replacementSource = Path.Combine(root, "replacement.png");
        WriteSolidImage(firstSource, Color.Red, 125, 125);
        WriteSolidImage(replacementSource, Color.Blue, 125, 125);
        var service = new BuffService();
        var buff = BuffService.CreateBuff();
        buff.GeneratedCode = "Misaka_BUFF-1";

        service.ImportIcon(character, buff, firstSource);
        var stablePath = buff.IconPath;
        var firstUri = buff.IconUri;
        File.SetLastWriteTimeUtc(stablePath, DateTime.UtcNow.AddMinutes(-1));
        var iconUriNotifications = 0;
        buff.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(BuffEntry.IconUri), StringComparison.Ordinal))
            {
                iconUriNotifications++;
            }
        };

        service.ImportIcon(character, buff, replacementSource);

        AssertEqual(stablePath, buff.IconPath);
        AssertEqual(true, firstUri.Contains("?v=", StringComparison.Ordinal));
        AssertEqual(true, buff.IconUri.Contains("?v=", StringComparison.Ordinal));
        AssertEqual(false, string.Equals(firstUri, buff.IconUri, StringComparison.Ordinal));
        AssertEqual(1, iconUriNotifications);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void BuffNumbersFollowCategoryCount()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var service = new BuffService();
        foreach (var index in Enumerable.Range(1, 10))
        {
            var folderPath = Path.Combine(root, "BUFF", $"Misaka_BUFF-{index}");
            Directory.CreateDirectory(folderPath);
            File.WriteAllText(
                Path.Combine(folderPath, "buff.json"),
                $$"""
                {
                  "Buff": {
                    "Index": {{index}},
                    "GeneratedCode": "Misaka_BUFF-{{index}}",
                    "Name": "BUFF {{index}}"
                  },
                  "UpdatedAt": "2026-08-17T00:00:00"
                }
                """);
        }

        var expanded = service.Load(character);
        AssertSequence(
            Enumerable.Range(1, 10).Select(index => $"Misaka_BUFF-{index:00}").ToArray(),
            expanded.Buffs.Select(buff => buff.GeneratedCode).ToArray());
        AssertSequence(
            Enumerable.Range(1, 10).Select(index => $"Misaka_BUFF-{index:00}").ToArray(),
            Directory.EnumerateDirectories(Path.Combine(root, "BUFF"))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()!);

        var removed = expanded.Buffs[^1];
        service.DeleteBuffFolder(character, removed);
        expanded.Buffs.RemoveAt(expanded.Buffs.Count - 1);
        BuffService.RefreshNaming(character, expanded.Buffs);
        service.Save(character, expanded);
        var shrunk = service.Load(character);
        AssertSequence(
            Enumerable.Range(1, 9).Select(index => $"Misaka_BUFF-{index}").ToArray(),
            shrunk.Buffs.Select(buff => buff.GeneratedCode).ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SavingBuffsDoesNotDeleteUnloadedEntries()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var service = new BuffService();
        var first = BuffService.CreateBuff();
        first.Name = "电击增幅";
        var second = BuffService.CreateBuff();
        second.Name = "电磁护盾";
        var all = new BuffData();
        all.Buffs.Add(first);
        all.Buffs.Add(second);
        service.Save(character, all);
        var secondFolder = Path.Combine(root, "BUFF", "Misaka_BUFF-2");

        var partial = new BuffData();
        partial.Buffs.Add(service.Load(character).Buffs[0]);
        service.Save(character, partial);

        AssertEqual(true, Directory.Exists(secondFolder));
        AssertEqual(true, File.Exists(Path.Combine(secondFolder, "buff.json")));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void BuffEffectsAndLifecycleRoundTrip()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var service = new BuffService();
        var buff = BuffService.CreateBuff();
        AssertEqual(false, buff.EndsWhenStacksReachZero);
        buff.Name = "复合效果";
        buff.Stacks = 2;
        buff.CompleteStacks = 6;
        buff.Strength = 15;
        buff.CompleteStrength = 40;
        buff.CompletedCount = 1;
        buff.EndsWhenStacksReachZero = true;
        buff.InitializationNotes = "注册受伤事件";
        buff.ConditionUpdateNotes = "每次受伤增加一层";
        buff.CompletionNotes = "达到上限后触发终结效果";
        buff.RemovalNotes = "解绑事件并恢复属性";
        buff.Effects.Add(new BuffEffectModule
        {
            EffectType = "属性修改",
            Target = "自身主战",
            Attribute = "物理攻击",
            Operation = "乘算",
            Value = 0.15,
            PerStackValue = 0.05,
            ImplementationNotes = "对应 BI_Buff.PhyMulti"
        });
        buff.Effects.Add(new BuffEffectModule
        {
            EffectType = "自定义效果",
            Target = "自身",
            Attribute = "受伤事件",
            Operation = "蓝图逻辑",
            ImplementationNotes = "蓝图内监听 OnTakeDamage"
        });
        var data = new BuffData();
        data.Buffs.Add(buff);

        service.Save(character, data);
        var loaded = service.Load(character).Buffs.Single();

        AssertEqual(2, loaded.Stacks);
        AssertEqual(6, loaded.CompleteStacks);
        AssertEqual(15, loaded.Strength);
        AssertEqual(40, loaded.CompleteStrength);
        AssertEqual(1, loaded.CompletedCount);
        AssertEqual(true, loaded.EndsWhenStacksReachZero);
        AssertEqual("注册受伤事件", loaded.InitializationNotes);
        AssertEqual("每次受伤增加一层", loaded.ConditionUpdateNotes);
        AssertEqual("达到上限后触发终结效果", loaded.CompletionNotes);
        AssertEqual("解绑事件并恢复属性", loaded.RemovalNotes);
        AssertEqual(2, loaded.Effects.Count);
        AssertEqual("物理攻击", loaded.Effects[0].Attribute);
        AssertEqual(0.05, loaded.Effects[0].PerStackValue);
        AssertEqual("自定义效果", loaded.Effects[1].EffectType);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void LegacyBuffNumericStringsMigrateToIntegers()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var folder = Path.Combine(root, "BUFF", "Misaka_BUFF-1");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "buff.json"),
            """
            {
              "Buff": {
                "GeneratedCode": "Misaka_BUFF-1",
                "Name": "旧数据",
                "Stacks": "3",
                "CompleteStacks": "8",
                "Strength": "12",
                "CompleteStrength": "30"
              },
              "UpdatedAt": "2026-08-17T00:00:00"
            }
            """);

        var loaded = new BuffService().Load(character).Buffs.Single();

        AssertEqual(3, loaded.Stacks);
        AssertEqual(8, loaded.CompleteStacks);
        AssertEqual(12, loaded.Strength);
        AssertEqual(30, loaded.CompleteStrength);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SwitchingBuffCharacterSavesPreviousAndRejectsCrossWrite()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var first = CreateCharacter(Path.Combine(root, "First"), "First", "第一个角色");
        var second = CreateCharacter(Path.Combine(root, "Second"), "Second", "第二个角色");
        var service = new BuffService();
        var viewModel = new BuffsViewModel(service, dispatcherQueue: null);
        viewModel.LoadAsync(first).GetAwaiter().GetResult();
        viewModel.AddBuffAsync(first).GetAwaiter().GetResult();
        viewModel.Buffs.Single().Name = "不会丢失";

        AssertEqual(false, viewModel.SaveAsync(second).GetAwaiter().GetResult());
        AssertEqual(0, service.Load(second).Buffs.Count);

        viewModel.LoadAsync(second).GetAwaiter().GetResult();

        AssertEqual("不会丢失", service.Load(first).Buffs.Single().Name);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void BuffEditorUsesNumberBoxesAndEffectModules()
{
    var document = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var buffEditor = document.Descendants().Single(element =>
        (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "BuffEditorCard");
    var numericBindings = buffEditor
        .Descendants()
        .Where(element => element.Name.LocalName == "NumberBox")
        .Select(element => (string?)element.Attribute("Value"))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();
    var effectItems = buffEditor.Descendants().Single(element =>
        element.Name.LocalName == "ItemsControl" &&
        ((string?)element.Attribute("ItemsSource"))?.Contains("SelectedBuff.Effects", StringComparison.Ordinal) == true);

    AssertEqual(true, numericBindings.Any(value => value!.Contains("SelectedBuff.Stacks", StringComparison.Ordinal)));
    AssertEqual(true, numericBindings.Any(value => value!.Contains("SelectedBuff.CompleteStacks", StringComparison.Ordinal)));
    AssertEqual(true, numericBindings.Any(value => value!.Contains("SelectedBuff.Strength", StringComparison.Ordinal)));
    AssertEqual(true, numericBindings.Any(value => value!.Contains("SelectedBuff.CompleteStrength", StringComparison.Ordinal)));
    AssertEqual(false, numericBindings.Any(value => value!.Contains("SelectedBuff.CompletedCount", StringComparison.Ordinal)));
    AssertEqual(true, buffEditor.Descendants().Any(element =>
        element.Name.LocalName == "ToggleSwitch" &&
        ((string?)element.Attribute("IsOn"))?.Contains("SelectedBuff.EndsWhenStacksReachZero", StringComparison.Ordinal) == true));
    AssertEqual(true, effectItems.Descendants().Any(element =>
        element.Name.LocalName == "Button" && (string?)element.Attribute("Click") == "RemoveBuffEffectButton_Click"));
}

static void BuffDamageTypeAllowsMarkerOnly()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var service = new BuffService();
        var buff = BuffService.CreateBuff();
        buff.Name = "仅标记";
        buff.DamageType = string.Empty;
        var data = new BuffData();
        data.Buffs.Add(buff);

        service.Save(character, data);
        var loaded = service.Load(character).Buffs.Single();

        AssertEqual(string.Empty, loaded.DamageType);
        AssertEqual(true, loaded.TypeSummaryText.Contains("仅标记", StringComparison.Ordinal));

        var document = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
        var typeComboBox = document.Descendants().Single(element =>
            element.Name.LocalName == "ComboBox" && (string?)element.Attribute("Header") == "类型枚举");
        var emptyOption = typeComboBox.Elements().Single(element =>
            element.Name.LocalName == "ComboBoxItem" && (string?)element.Attribute("Tag") == string.Empty);
        AssertEqual("无（仅标记）", (string?)emptyOption.Attribute("Content"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SkillMultiplierArrowKeysNavigateGrid()
{
    var physicalLevel3 = new SkillMultiplierCell(3, SkillMultiplierColumn.Physical);
    var energyLevel3 = new SkillMultiplierCell(3, SkillMultiplierColumn.Energy);

    AssertEqual(
        new SkillMultiplierCell(2, SkillMultiplierColumn.Physical),
        SkillMultiplierNavigation.GetTarget(physicalLevel3, SkillMultiplierMoveDirection.Up, 1, 5));
    AssertEqual(
        new SkillMultiplierCell(4, SkillMultiplierColumn.Energy),
        SkillMultiplierNavigation.GetTarget(energyLevel3, SkillMultiplierMoveDirection.Down, 1, 5));
    AssertEqual(
        new SkillMultiplierCell(3, SkillMultiplierColumn.Physical),
        SkillMultiplierNavigation.GetTarget(energyLevel3, SkillMultiplierMoveDirection.Left, 1, 5));
    AssertEqual(
        new SkillMultiplierCell(3, SkillMultiplierColumn.Energy),
        SkillMultiplierNavigation.GetTarget(physicalLevel3, SkillMultiplierMoveDirection.Right, 1, 5));

    AssertEqual<SkillMultiplierCell?>(
        null,
        SkillMultiplierNavigation.GetTarget(
            new SkillMultiplierCell(1, SkillMultiplierColumn.Physical),
            SkillMultiplierMoveDirection.Up,
            1,
            5));
    AssertEqual<SkillMultiplierCell?>(
        null,
        SkillMultiplierNavigation.GetTarget(
            new SkillMultiplierCell(5, SkillMultiplierColumn.Energy),
            SkillMultiplierMoveDirection.Down,
            1,
            5));
    AssertEqual<SkillMultiplierCell?>(
        null,
        SkillMultiplierNavigation.GetTarget(physicalLevel3, SkillMultiplierMoveDirection.Left, 1, 5));
    AssertEqual<SkillMultiplierCell?>(
        null,
        SkillMultiplierNavigation.GetTarget(energyLevel3, SkillMultiplierMoveDirection.Right, 1, 5));
}

static void SkillMultiplierFocusMoveIsDeferred()
{
    var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "SkillEditorControl.xaml.cs");
    var source = File.ReadAllText(sourcePath);

    AssertEqual(true, source.Contains("QueueMultiplierFocus(targetTextBox);", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("DispatcherQueue.TryEnqueue(() =>", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("Focus(FocusState.Programmatic)", StringComparison.Ordinal));
    AssertEqual(false, source.Contains("Focus(FocusState.Keyboard)", StringComparison.Ordinal));
}

static void SkillIconPathRepairsAfterNumberWidthChanges()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var iconFolder = Path.Combine(root, "AssetMaterial", "SkillIcon");
        Directory.CreateDirectory(iconFolder);
        var oldIconPath = Path.Combine(iconFolder, "Misaka-SkillIcon-01.png");
        var currentIconPath = Path.Combine(iconFolder, "Misaka-SkillIcon-1.png");
        WriteImage(oldIconPath);

        var skills = new CharacterSkillsData();
        var firstSkill = CharacterSkillsService.CreateEntry();
        firstSkill.TrueName = "掌心雷";
        firstSkill.IconPath = oldIconPath;
        skills.FirstSkill.Add(firstSkill);
        var service = new CharacterSkillsService();
        service.Save(character, skills);
        File.Move(oldIconPath, currentIconPath);

        var loaded = service.Load(character);
        var loadedFirstSkill = loaded.FirstSkill.Single();
        AssertEqual(currentIconPath, loadedFirstSkill.IconPath);
        AssertEqual(new Uri(currentIconPath).AbsoluteUri, loadedFirstSkill.IconUri);

        var persisted = new CharacterToolboxDataService().Load(character);
        AssertEqual(currentIconPath, persisted.Skills?.FirstSkill.Single().IconPath ?? string.Empty);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SequenceFramePersistsDurationAndVoice()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frame = service.ImportFrames(character, action, [sourcePaths[0]])[0];

        service.SetFrameDuration(character, action, frame, 3);
        service.SetFrameVoice(character, action, frame, voicePath);

        var reloaded = LoadSequenceSection(service, character, action).Frames.Single();
        AssertEqual(3, reloaded.DurationFrames);
        AssertEqual(voicePath, reloaded.VoiceFilePath);
        AssertEqual(Path.GetFileName(voicePath), reloaded.VoiceFileName);
    });
}

static void SequenceFrameVoicesFilterByActionCategory()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var clickSource = Path.Combine(root, "click.wav");
        var deathSource = Path.Combine(root, "death.wav");
        var pendingSource = Path.Combine(root, "pending.wav");
        WriteWaveFile(clickSource, 1);
        WriteWaveFile(deathSource, 2);
        WriteWaveFile(pendingSource, 3);
        var voiceService = new VoiceMaterialService();
        var clickVoice = voiceService.Import(character, VoiceMaterialKind.Click, clickSource);
        var deathVoice = voiceService.Import(character, VoiceMaterialKind.Death, deathSource);
        var pendingVoice = voiceService.Import(character, VoiceMaterialKind.Other, pendingSource);

        var frameSource = Path.Combine(root, "frame.png");
        WriteSolidImage(frameSource, Color.Red, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        var frameService = new SequenceFrameService();
        var actions = SequenceFrameService.BuildActions(new CharacterSkillsData());
        var clickAction = actions.Single(action => action.Code == "Click");
        var counterAction = actions.Single(action => action.Code == "DefAtk");
        var clickFrame = frameService.ImportFrames(character, clickAction, [frameSource]).Single();
        frameService.ImportFrames(character, counterAction, [frameSource]);
        frameService.SetFrameVoice(character, clickAction, clickFrame, deathVoice.FilePath);

        var viewModel = new SequenceFramesViewModel(frameService, new CharacterSkillsService());
        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        var sections = viewModel.BaseSectionGroups.SelectMany(group => group.Sections).ToArray();

        viewModel.SelectSectionForManagement(sections.Single(section => section.Action.Code == "Click"));
        var clickOptions = viewModel.AvailableVoices.Select(option => option.FilePath).ToArray();
        AssertEqual(true, clickOptions.Contains(string.Empty));
        AssertEqual(true, clickOptions.Contains(clickVoice.FilePath));
        AssertEqual(true, clickOptions.Contains(deathVoice.FilePath));
        AssertEqual(false, clickOptions.Contains(pendingVoice.FilePath));

        viewModel.SelectSectionForManagement(sections.Single(section => section.Action.Code == "DefAtk"));
        var counterOptions = viewModel.AvailableVoices.Select(option => option.FilePath).ToArray();
        AssertEqual(true, counterOptions.Contains(clickVoice.FilePath));
        AssertEqual(true, counterOptions.Contains(deathVoice.FilePath));
        AssertEqual(true, counterOptions.Contains(pendingVoice.FilePath));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SequenceFrameVoiceSelectionRefreshesAfterOptions()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        service.ImportFrames(character, action, [sourcePaths[0]]);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        var section = viewModel.BaseSectionGroups.SelectMany(group => group.Sections)
            .Single(item => item.Action.Code == action.Code);
        viewModel.SelectSection(section);
        viewModel.SelectSectionForManagement(section);

        var selectedVoiceProperty = typeof(SequenceFramesViewModel).GetProperty("SelectedEditorVoicePath");
        var selectedVoiceOptionProperty = typeof(SequenceFramesViewModel).GetProperty("SelectedEditorVoiceOption");
        AssertEqual(true, selectedVoiceProperty is not null);
        AssertEqual(true, selectedVoiceOptionProperty is not null);
        if (selectedVoiceProperty is null || selectedVoiceOptionProperty is null)
        {
            return;
        }

        var changeOrder = 0;
        var matchingOptionAddedOrder = 0;
        var selectedPathNotifiedOrder = 0;
        viewModel.AvailableVoices.CollectionChanged += (_, args) =>
        {
            changeOrder++;
            if (args.NewItems?.OfType<SequenceFrameVoiceOption>().Any(option =>
                    string.Equals(option.FilePath, voicePath, StringComparison.OrdinalIgnoreCase)) == true)
            {
                matchingOptionAddedOrder = changeOrder;
            }
        };
        viewModel.PropertyChanged += (_, args) =>
        {
            changeOrder++;
            if (args.PropertyName == "SelectedEditorVoicePath")
            {
                selectedPathNotifiedOrder = changeOrder;
            }
        };

        viewModel.SetFrameVoiceAsync(character, section, viewModel.SelectedEditorFrame!, voicePath)
            .GetAwaiter().GetResult();

        AssertEqual(voicePath, Convert.ToString(selectedVoiceProperty.GetValue(viewModel)) ?? string.Empty);
        var selectedOption = selectedVoiceOptionProperty.GetValue(viewModel) as SequenceFrameVoiceOption;
        AssertEqual(voicePath, selectedOption?.FilePath ?? string.Empty);
        AssertEqual(true, viewModel.AvailableVoices.Any(option => ReferenceEquals(option, selectedOption)));
        AssertEqual(true, matchingOptionAddedOrder > 0);
        AssertEqual(true, selectedPathNotifiedOrder > matchingOptionAddedOrder);

        var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
        AssertEqual(true, xaml.Contains("Text=\"当前帧语音\"", StringComparison.Ordinal));
        AssertEqual(
            true,
            xaml.Contains(
                "SelectedItem=\"{Binding SequenceFrames.SelectedEditorVoiceOption, Mode=OneWay}\"",
                StringComparison.Ordinal));
        AssertEqual(false, xaml.Contains("Text=\"进入此帧时播放\"", StringComparison.Ordinal));

        var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
        AssertEqual(true, source.Contains("private void SynchronizeSequenceFrameVoiceSelection()", StringComparison.Ordinal));
        AssertEqual(true, source.Contains("SequenceFrameVoiceComboBox.SelectedItem = selectedOption;", StringComparison.Ordinal));
        AssertEqual(true, source.Contains("_isSynchronizingSequenceFrameVoiceSelection", StringComparison.Ordinal));
    });
}

static void ReplacingSequenceFramePreservesMetadata()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frame = service.ImportFrames(character, action, [sourcePaths[0]])[0];
        service.SetFrameDuration(character, action, frame, 4);
        service.SetFrameVoice(character, action, frame, voicePath);
        frame = LoadSequenceSection(service, character, action).Frames.Single();
        var replacementPath = Path.Combine(character.FolderPath, "replacement.png");
        WriteSolidImage(replacementPath, Color.Blue, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);

        service.ReplaceFrame(character, action, frame, replacementPath);

        var reloaded = LoadSequenceSection(service, character, action).Frames.Single();
        AssertEqual(4, reloaded.DurationFrames);
        AssertEqual(voicePath, reloaded.VoiceFilePath);
        using var image = new Bitmap(reloaded.FilePath);
        AssertEqual(Color.Blue.ToArgb(), image.GetPixel(0, 0).ToArgb());
    });
}

static void ReplacingSequenceFrameWithMultipleSourcesPreservesTargetMetadata()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frames = service.InsertBlankFrame(character, action, null, SequenceFrameInsertPosition.After);
        service.SetFrameDuration(character, action, frames[0], 4);
        service.SetFrameVoice(character, action, frames[0], voicePath);
        var target = LoadSequenceSection(service, character, action).Frames.Single();
        var thirdPath = Path.Combine(character.FolderPath, "third.png");
        WriteSolidImage(
            thirdPath,
            Color.Blue,
            SequenceFrameService.RequiredWidth,
            SequenceFrameService.RequiredHeight);

        frames = service.ReplaceFrameWithSources(character, action, target, [sourcePaths[0], sourcePaths[1], thirdPath]);

        AssertEqual(3, frames.Count);
        AssertSequence([false, false, false], frames.Select(frame => frame.IsBlank).ToArray());
        AssertSequence([4, 1, 1], frames.Select(frame => frame.DurationFrames).ToArray());
        AssertSequence([voicePath, string.Empty, string.Empty], frames.Select(frame => frame.VoiceFilePath).ToArray());
    });
}

static void SelectingCollectionFrameAcrossActionsReusesExistingFile()
{
    WithSequenceFrameWorkspace((service, character, sourceAction, sourcePaths, voicePath) =>
    {
        var actions = SequenceFrameService.BuildActions(new CharacterSkillsData());
        var targetAction = actions.First(action => action.Code != sourceAction.Code);
        var sourceFrame = service.ImportFrames(character, sourceAction, [sourcePaths[0]]).Single();
        var targetFrame = service.InsertBlankFrame(character, targetAction, null).Single();

        var replaced = service.ReplaceFrame(character, targetAction, targetFrame, sourceFrame.FilePath).Single();

        AssertEqual(Path.GetFullPath(sourceFrame.FilePath), Path.GetFullPath(replaced.FilePath));
    });
}

static void SequenceCollectionAggregatesSharedResourceUsages()
{
    WithSequenceFrameWorkspace((service, character, sourceAction, sourcePaths, voicePath) =>
    {
        var actions = SequenceFrameService.BuildActions(new CharacterSkillsData());
        var targetAction = actions.First(action => action.Code != sourceAction.Code);
        var sourceFrame = service.ImportFrames(character, sourceAction, [sourcePaths[0]]).Single();
        var targetFrame = service.InsertBlankFrame(character, targetAction, null).Single();
        service.ReplaceFrame(character, targetAction, targetFrame, sourceFrame.FilePath);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());

        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        viewModel.RefreshCollectionAsync().GetAwaiter().GetResult();

        var collectionItem = viewModel.CollectionItems.Single();
        var usageCountProperty = collectionItem.GetType().GetProperty("UsageCount");
        var hasReuseProperty = collectionItem.GetType().GetProperty("HasReuse");
        AssertEqual(true, usageCountProperty is not null);
        AssertEqual(true, hasReuseProperty is not null);
        AssertEqual(2, Convert.ToInt32(usageCountProperty?.GetValue(collectionItem)));
        AssertEqual(true, Convert.ToBoolean(hasReuseProperty?.GetValue(collectionItem)));
        AssertEqual(
            $"{sourceAction.DisplayName} #1 / {targetAction.DisplayName} #1",
            collectionItem.UsageText);
        AssertEqual(false, collectionItem.HasDuplicate);
    });
}

static void DuplicatingSequenceFrameCopiesMetadata()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frame = service.ImportFrames(character, action, [sourcePaths[0]])[0];
        service.SetFrameDuration(character, action, frame, 5);
        service.SetFrameVoice(character, action, frame, voicePath);
        frame = LoadSequenceSection(service, character, action).Frames.Single();

        var frames = service.DuplicateFrame(character, action, frame);

        AssertEqual(2, frames.Count);
        AssertSequence([5, 5], frames.Select(item => item.DurationFrames).ToArray());
        AssertSequence([voicePath, voicePath], frames.Select(item => item.VoiceFilePath).ToArray());
    });
}

static void ReusedSequenceFramesExposeTimelineRelationship()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frames = service.ImportFrames(character, action, sourcePaths);
        frames = service.DuplicateFrame(character, action, frames[0]);
        frames = service.DuplicateFrame(character, action, frames[2]);
        frames = service.ReorderFrames(character, action, [frames[0], frames[2], frames[1], frames[3]]);

        AssertSequence([2, 2, 2, 2], frames.Select(frame => frame.ReuseCount).ToArray());
        AssertSequence([1, 1, 2, 2], frames.Select(frame => frame.ReuseOccurrence).ToArray());
        AssertSequence([1, 2, 1, 2], frames.Select(frame => frame.ReuseSourceIndex).ToArray());
        AssertSequence(
            ["共 2 次", "共 2 次", "第 2 次", "第 2 次"],
            frames.Select(frame => frame.ReuseBadgeText).ToArray());
        AssertSequence(["1、3", "2、4", "1、3", "2、4"], frames.Select(frame => frame.ReusePositionsText).ToArray());
        AssertEqual(frames[0].ReuseColorIndex, frames[2].ReuseColorIndex);
        AssertEqual(frames[1].ReuseColorIndex, frames[3].ReuseColorIndex);
        AssertEqual(false, frames[0].ReuseColorIndex == frames[1].ReuseColorIndex);
    });
}

static void BlankSequenceFrameCanInsertBeforeOrAfterCurrentFrame()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frames = service.ImportFrames(character, action, sourcePaths);
        service.SetFrameDuration(character, action, frames[0], 2);
        service.SetFrameDuration(character, action, frames[1], 6);
        frames = LoadSequenceSection(service, character, action).Frames;

        frames = service.InsertBlankFrame(character, action, frames[1], SequenceFrameInsertPosition.Before);
        AssertSequence([2, 1, 6], frames.Select(item => item.DurationFrames).ToArray());
        AssertSequence([false, true, false], frames.Select(item => item.IsBlank).ToArray());

        frames = service.InsertBlankFrame(character, action, frames[0], SequenceFrameInsertPosition.After);
        AssertSequence([2, 1, 1, 6], frames.Select(item => item.DurationFrames).ToArray());
        AssertSequence([false, true, true, false], frames.Select(item => item.IsBlank).ToArray());
    });
}

static void DeletingMultipleSequenceFramesKeepsUnselectedFrames()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frames = service.ImportFrames(character, action, sourcePaths);
        frames = service.InsertBlankFrame(character, action, frames[1]);
        service.SetFrameDuration(character, action, frames[0], 2);
        service.SetFrameDuration(character, action, frames[1], 4);
        service.SetFrameDuration(character, action, frames[2], 6);
        frames = LoadSequenceSection(service, character, action).Frames;

        var remaining = service.DeleteFrames(character, action, [frames[0], frames[2]]);

        AssertEqual(1, remaining.Count);
        AssertEqual(4, remaining[0].DurationFrames);
    });
}

static void DuplicatingMultipleSequenceFramesPreservesOrder()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frames = service.ImportFrames(character, action, sourcePaths);
        frames = service.InsertBlankFrame(character, action, frames[1]);
        service.SetFrameDuration(character, action, frames[0], 2);
        service.SetFrameDuration(character, action, frames[1], 4);
        service.SetFrameDuration(character, action, frames[2], 6);
        frames = LoadSequenceSection(service, character, action).Frames;

        var duplicated = service.DuplicateFrames(
            character,
            action,
            [frames[2], frames[0]],
            frames[1]);

        AssertSequence([2, 4, 2, 6, 6], duplicated.Select(item => item.DurationFrames).ToArray());
    });
}

static void ReorderingSequenceFramesPreservesMetadata()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frames = service.ImportFrames(character, action, sourcePaths);
        service.SetFrameDuration(character, action, frames[0], 2);
        service.SetFrameDuration(character, action, frames[1], 6);
        service.SetFrameVoice(character, action, frames[1], voicePath);
        frames = LoadSequenceSection(service, character, action).Frames;

        var reordered = service.ReorderFrames(character, action, [frames[1], frames[0]]);

        AssertSequence([6, 2], reordered.Select(item => item.DurationFrames).ToArray());
        AssertEqual(voicePath, reordered[0].VoiceFilePath);
        AssertEqual(string.Empty, reordered[1].VoiceFilePath);
    });
}

static void SequenceEditorProvidesCompleteTimelineControls()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    AssertEqual(true, xaml.Contains("Content=\"编辑帧序列\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceFrameTimelineListView\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceFrameDurationStepper\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceFrameVoiceComboBox\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceEditorPreviewCanvas\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceEditorPreviewImageTransform\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("PointerWheelChanged=\"SequenceEditorPreviewCanvas_PointerWheelChanged\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("PointerPressed=\"SequenceEditorPreviewCanvas_PointerPressed\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("PointerMoved=\"SequenceEditorPreviewCanvas_PointerMoved\"", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("ResetSequenceEditorPreviewTransform", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("ReplaceSequenceEditorFrameButton_Click", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("SelectSequenceEditorFrameFromCollectionButton_Click", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("CopySequenceEditorFrameButton_Click", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Content=\"左插入\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Content=\"右插入\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Text=\"左插入\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Text=\"右插入\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("DeleteSequenceEditorFrameButton_Click", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("_sequenceFrameCollectionSelectionTarget", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("PlayCurrentSequenceFrameVoice", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("DurationFrames", StringComparison.Ordinal));

    var fpsStepper = document
        .Descendants()
        .Single(element => string.Equals(
            element.Attribute(x + "Name")?.Value,
            "SequencePreviewFpsStepper",
            StringComparison.Ordinal));
    var outerFpsStepper = document
        .Descendants()
        .Single(element => string.Equals(
            element.Attribute(x + "Name")?.Value,
            "SequenceOuterPreviewFpsStepper",
            StringComparison.Ordinal));
    var durationStepper = document
        .Descendants()
        .Single(element => string.Equals(
            element.Attribute(x + "Name")?.Value,
            "SequenceFrameDurationStepper",
            StringComparison.Ordinal));

    AssertEqual(true, fpsStepper.Name.LocalName == "NumberBox");
    AssertEqual(true, outerFpsStepper.Name.LocalName == "NumberBox");
    AssertEqual(true, durationStepper.Name.LocalName == "NumberBox");
    AssertEqual("Inline", fpsStepper.Attribute("SpinButtonPlacementMode")?.Value ?? string.Empty);
    AssertEqual("Inline", outerFpsStepper.Attribute("SpinButtonPlacementMode")?.Value ?? string.Empty);
    AssertEqual("Inline", durationStepper.Attribute("SpinButtonPlacementMode")?.Value ?? string.Empty);
    AssertEqual(true, durationStepper.Attribute("Value")?.Value.Contains("EditorFrameDurationInput", StringComparison.Ordinal) == true);
    AssertEqual(true, source.Contains("SequenceFrameDurationStepper_ValueChanged", StringComparison.Ordinal));
}

static void SequenceTimelineUsesCompactUniformHeightCards()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var frameCard = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceTimelineFrameCard",
        StringComparison.Ordinal));
    var thumbnail = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceTimelineFrameThumbnail",
        StringComparison.Ordinal));

    AssertEqual("{Binding TimelineWidth, Mode=OneWay}", frameCard.Attribute("Width")?.Value ?? string.Empty);
    AssertEqual("150", frameCard.Attribute("Height")?.Value ?? string.Empty);
    AssertEqual("96", thumbnail.Attribute("Height")?.Value ?? string.Empty);
    AssertEqual(
        false,
        frameCard.Descendants().Any(element =>
            element.Attribute("Text")?.Value.Contains("PlainFileName", StringComparison.Ordinal) == true));
    AssertEqual(
        true,
        thumbnail.Descendants().Any(element => string.Equals(
            element.Attribute(x + "Name")?.Value,
            "SequenceTimelineVoiceMarker",
            StringComparison.Ordinal)));
    AssertEqual(
        true,
        thumbnail.Descendants().Any(element => string.Equals(
            element.Attribute(x + "Name")?.Value,
            "SequenceTimelineSyncMarker",
            StringComparison.Ordinal)));
    AssertEqual(false, xaml.Contains("播放头进入带语音标记的帧时会同步试听 WAV。", StringComparison.Ordinal));
    AssertEqual(false, xaml.Contains("所有编辑都会直接写入当前动作的 sequence.json；Esc 关闭编辑器。", StringComparison.Ordinal));

    var oneGridFrame = CreateSequenceFrameForVoiceSync(1, 1, string.Empty);
    var fourGridFrame = CreateSequenceFrameForVoiceSync(2, 4, string.Empty);
    AssertEqual(128d, oneGridFrame.TimelineWidth);
    AssertEqual(200d, fourGridFrame.TimelineWidth);
}

static void SequenceEditorHeaderUsesStableFramePositionSummary()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, _) =>
    {
        service.ImportFrames(character, action, sourcePaths);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        var section = viewModel.BaseSectionGroups.SelectMany(group => group.Sections)
            .Single(item => item.Action.Code == action.Code);
        viewModel.SelectSection(section);

        var property = typeof(SequenceFramesViewModel).GetProperty("EditorPlaybackPositionText");
        AssertEqual(true, property is not null);
        if (property is null)
        {
            return;
        }

        AssertEqual($"1 / {sourcePaths.Count}", Convert.ToString(property.GetValue(viewModel)) ?? string.Empty);
        viewModel.StepPreviewFrame(1);
        AssertEqual($"2 / {sourcePaths.Count}", Convert.ToString(property.GetValue(viewModel)) ?? string.Empty);

        var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
        AssertEqual(
            true,
            xaml.Contains(
                "Text=\"{Binding SequenceFrames.EditorPlaybackPositionText, Mode=OneWay}\"",
                StringComparison.Ordinal));
    });
}

static void SequenceEditorCreatesFirstFrameAndCollectionSupportsModifierMultiSelect()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var sequenceSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var shortcutSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.Logging.cs"));

    AssertEqual(true, xaml.Contains("Content=\"新建帧\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Click=\"NewSequenceEditorFrameButton_Click\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"ConfirmSequenceFrameCollectionSelectionButton\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"CancelSequenceFrameCollectionSelectionButton\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("SelectionChanged=\"SequenceFramesCollectionGridView_SelectionChanged\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("DoubleTapped=\"SequenceFrameCollectionItem_DoubleTapped\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceFrameInspectorScrollViewer\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Text=\"{Binding ReuseBadgeText, Mode=OneWay}\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Visibility=\"{Binding ReuseVisibility, Mode=OneWay}\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Visibility=\"{Binding SequenceFrames.SelectedEditorFrame.ReuseVisibility, Mode=OneWay}\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("VirtualKey.Control", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("VirtualKey.Shift", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("VirtualKey.A", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("VirtualKey.D", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("VirtualKey.Left", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("VirtualKey.Right", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("NavigateSequenceEditorFrame", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("SelectSequenceReuseGroupButton_Click", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("FocusManager.GetFocusedElement", StringComparison.Ordinal));
    AssertEqual(false, sequenceSource.Contains("SelectedItems.Add(item)", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("ReplaceFrameWithSourcesAsync", StringComparison.Ordinal));
    AssertEqual(true, sequenceSource.Contains("SelectionMode != ListViewSelectionMode.None", StringComparison.Ordinal));
    AssertEqual(true, shortcutSource.Contains("SequenceFramesManagerHost.Visibility == Visibility.Visible", StringComparison.Ordinal));
    AssertEqual(true, shortcutSource.Contains("CancelSequenceFrameCollectionMultiSelection", StringComparison.Ordinal));

    var itemClickStart = sequenceSource.IndexOf("SequenceFramesCollectionGridView_ItemClick", StringComparison.Ordinal);
    var doubleTapStart = sequenceSource.IndexOf("SequenceFrameCollectionItem_DoubleTapped", StringComparison.Ordinal);
    AssertEqual(true, itemClickStart >= 0 && doubleTapStart > itemClickStart);
    var itemClickSource = sequenceSource[itemClickStart..doubleTapStart];
    AssertEqual(false, itemClickSource.Contains("ReplaceSequenceEditorFrameAsync", StringComparison.Ordinal));
}

static void SequenceCollectionDetectsDuplicatesOnlyWhenRequested()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var duplicatePath = Path.Combine(character.FolderPath, "duplicate.bmp");
        WriteSolidImage(
            duplicatePath,
            Color.Red,
            SequenceFrameService.RequiredWidth,
            SequenceFrameService.RequiredHeight,
            ImageFormat.Bmp);
        service.ImportFrames(character, action, [sourcePaths[0], duplicatePath]);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.LoadAsync(character).GetAwaiter().GetResult();

        viewModel.RefreshCollectionAsync().GetAwaiter().GetResult();

        AssertEqual(2, viewModel.CollectionItems.Count);
        AssertEqual(true, viewModel.CollectionItems.All(item => string.IsNullOrWhiteSpace(item.ContentHash)));
        AssertEqual(true, viewModel.CollectionItems.All(item => !item.HasDuplicate));
        AssertEqual(true, viewModel.CollectionSummaryText.Contains("尚未检测重复", StringComparison.Ordinal));

        viewModel.DetectCollectionDuplicatesAsync().GetAwaiter().GetResult();

        AssertEqual(true, viewModel.CollectionItems.All(item => !string.IsNullOrWhiteSpace(item.ContentHash)));
        AssertEqual(true, viewModel.CollectionItems.All(item => item.HasDuplicate));
        AssertEqual(true, viewModel.CollectionSummaryText.Contains("2 张存在内容重复", StringComparison.Ordinal));
    });
}

static void SequenceCollectionPersistsDuplicateResultsUntilMaterialCountChanges()
{
    WithSequenceFrameWorkspace((service, character, firstAction, sourcePaths, voicePath) =>
    {
        var actions = SequenceFrameService.BuildActions(new CharacterSkillsData());
        var secondAction = actions.First(action => action.Code != firstAction.Code);
        var thirdAction = actions.First(action => action.Code != firstAction.Code && action.Code != secondAction.Code);
        service.ImportFrames(character, firstAction, [sourcePaths[0]]);
        service.ImportFrames(character, secondAction, [sourcePaths[0]]);
        var initialViewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        initialViewModel.LoadAsync(character).GetAwaiter().GetResult();
        initialViewModel.DetectCollectionDuplicatesAsync().GetAwaiter().GetResult();
        AssertEqual(2, initialViewModel.CollectionItems.Count(item => item.HasDuplicate));

        var reopenedViewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        reopenedViewModel.LoadAsync(character).GetAwaiter().GetResult();
        reopenedViewModel.RefreshCollectionAsync().GetAwaiter().GetResult();
        AssertEqual(2, reopenedViewModel.CollectionItems.Count(item => item.HasDuplicate));

        var firstFrame = LoadSequenceSection(service, character, firstAction).Frames.Single();
        service.SetFrameDuration(character, firstAction, firstFrame, 4);
        var metadataChangedViewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        metadataChangedViewModel.LoadAsync(character).GetAwaiter().GetResult();
        metadataChangedViewModel.RefreshCollectionAsync().GetAwaiter().GetResult();
        AssertEqual(2, metadataChangedViewModel.CollectionItems.Count(item => item.HasDuplicate));

        service.ImportFrames(character, thirdAction, [sourcePaths[1]]);
        var materialCountChangedViewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        materialCountChangedViewModel.LoadAsync(character).GetAwaiter().GetResult();
        materialCountChangedViewModel.RefreshCollectionAsync().GetAwaiter().GetResult();
        AssertEqual(true, materialCountChangedViewModel.CollectionSummaryText.Contains("尚未检测重复", StringComparison.Ordinal));
    });
}

static void SequenceCollectionResolvesAllDuplicatesKeepingMostUsedResource()
{
    WithSequenceFrameWorkspace((service, character, firstAction, sourcePaths, voicePath) =>
    {
        var actions = SequenceFrameService.BuildActions(new CharacterSkillsData());
        var secondAction = actions.First(action => action.Code != firstAction.Code);
        var thirdAction = actions.First(action => action.Code != firstAction.Code && action.Code != secondAction.Code);
        var firstFrame = service.ImportFrames(character, firstAction, [sourcePaths[0]]).Single();
        var secondFrame = service.ImportFrames(character, secondAction, [sourcePaths[0]]).Single();
        service.DuplicateFrame(character, secondAction, secondFrame);
        var thirdFrame = service.ImportFrames(character, thirdAction, [sourcePaths[0]]).Single();
        var duplicatePaths = new[] { firstFrame.FilePath, secondFrame.FilePath, thirdFrame.FilePath };
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        viewModel.DetectCollectionDuplicatesAsync().GetAwaiter().GetResult();
        var preferred = viewModel.CollectionItems.Single(item => item.UsageCount == 2);
        var method = typeof(SequenceFramesViewModel).GetMethod("ResolveAllDuplicateFramesAsync");
        AssertEqual(true, method is not null);
        if (method is null)
        {
            return;
        }

        var task = (Task<int>)method.Invoke(
            viewModel,
            [character, null, System.Threading.CancellationToken.None])!;
        var redirectedReferenceCount = task.GetAwaiter().GetResult();

        AssertEqual(2, redirectedReferenceCount);
        var result = viewModel.CollectionItems.Single();
        AssertEqual(Path.GetFullPath(preferred.FilePath), Path.GetFullPath(result.FilePath));
        AssertEqual(4, result.UsageCount);
        AssertEqual(true, result.HasReuse);
        AssertEqual(false, result.HasDuplicate);
        AssertEqual(1, duplicatePaths.Count(File.Exists));
    });

    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    AssertEqual(true, xaml.Contains("Content=\"一键处理\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Click=\"ResolveAllSequenceFrameDuplicatesButton_Click\"", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("ResolveAllSequenceFrameDuplicatesButton_Click", StringComparison.Ordinal));
}

static void OuterSequencePreviewProvidesFrameCollectionEntry()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    AssertEqual(true, xaml.Contains("Content=\"帧素材合集\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains(
        "Click=\"OpenSequenceCollectionButton_Click\" Content=\"帧素材合集\"",
        StringComparison.Ordinal));
}

static void SequenceCollectionOrdersByActionThenFrameIndex()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var actions = SequenceFrameService.BuildActions(new CharacterSkillsData());
        var nextAction = actions[1];
        var nextActionPath = Path.Combine(character.FolderPath, "next-action.png");
        WriteSolidImage(
            nextActionPath,
            Color.Blue,
            SequenceFrameService.RequiredWidth,
            SequenceFrameService.RequiredHeight);
        service.ImportFrames(character, action, sourcePaths);
        service.ImportFrames(character, nextAction, [nextActionPath]);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.LoadAsync(character).GetAwaiter().GetResult();

        viewModel.RefreshCollectionAsync().GetAwaiter().GetResult();

        AssertSequence(
            [$"{action.DisplayName} #1", $"{action.DisplayName} #2", $"{nextAction.DisplayName} #1"],
            viewModel.CollectionItems.Select(item => item.UsageText).ToArray());
    });
}

static void SequenceCollectionUsesCompactUsageFirstCards()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));

    AssertEqual(true, xaml.Contains("x:Name=\"DetectSequenceFrameDuplicatesButton\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Click=\"DetectSequenceFrameDuplicatesButton_Click\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceFramesCollectionGridView\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("ItemWidth=\"206\"", StringComparison.Ordinal));
    var collectionTemplate = xaml[xaml.IndexOf("x:Name=\"SequenceFramesCollectionGridView\"", StringComparison.Ordinal)..];
    AssertEqual(true, collectionTemplate.IndexOf("Text=\"{Binding UsageText", StringComparison.Ordinal) <
        collectionTemplate.IndexOf("Text=\"{Binding FileName", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("DetectCollectionDuplicatesAsync", StringComparison.Ordinal));
}

static void SequenceFrameImportPreservesOuterScrollPosition()
{
    var sequenceFramesSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var navigationSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.Navigation.cs"));

    AssertEqual(
        true,
        sequenceFramesSource.Contains(
            "RunWithPageScrollPositionPreservedAsync(SequenceFramesPage",
            StringComparison.Ordinal));
    AssertEqual(
        true,
        navigationSource.Contains(
            "private async Task RunWithPageScrollPositionPreservedAsync",
            StringComparison.Ordinal));
    AssertEqual(
        true,
        sequenceFramesSource.Contains(
            "TrySelectSection(section.Action.Code)",
            StringComparison.Ordinal));
    AssertEqual(
        true,
        sequenceFramesSource.Contains(
            "await StartSequencePreviewAsync();",
            StringComparison.Ordinal));
}

static void SequencePreviewsShareDoubleBufferedPresenter()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var presenterXamlPath = Path.Combine(Directory.GetCurrentDirectory(), "Controls", "BufferedImagePresenter.xaml");
    var presenterSourcePath = Path.Combine(Directory.GetCurrentDirectory(), "Controls", "BufferedImagePresenter.xaml.cs");

    AssertEqual(true, xaml.Contains("x:Name=\"SequencePreviewPresenter\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceEditorPreviewPresenter\"", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("SequencePreviewPresenter.Show", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("SequenceEditorPreviewPresenter.Show", StringComparison.Ordinal));
    AssertEqual(false, source.Contains("SequencePreviewImage.Source =", StringComparison.Ordinal));
    AssertEqual(true, File.Exists(presenterXamlPath));
    AssertEqual(true, File.Exists(presenterSourcePath));
}

static void SequenceEditorDurationInputFollowsSelectedFrame()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        service.ImportFrames(character, action, sourcePaths);
        var section = LoadSequenceSection(service, character, action);
        service.SetFrameDuration(character, action, section.Frames[0], 2);
        section = LoadSequenceSection(service, character, action);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        var inputProperty = typeof(SequenceFramesViewModel).GetProperty("EditorFrameDurationInput");

        AssertEqual(true, inputProperty is not null);
        if (inputProperty is null)
        {
            return;
        }

        viewModel.SelectSection(section);
        viewModel.SelectSectionForManagement(section);
        AssertEqual(2d, Convert.ToDouble(inputProperty.GetValue(viewModel)));

        viewModel.SelectEditorFrame(viewModel.SelectedSectionFrames[1]);
        AssertEqual(1d, Convert.ToDouble(inputProperty.GetValue(viewModel)));
    });
}

static void SequenceTimelineSelectionFollowsPlayback()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        service.ImportFrames(character, action, sourcePaths);
        var section = LoadSequenceSection(service, character, action);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.SelectSection(section);
        viewModel.SelectSectionForManagement(section);
        viewModel.StartPreview();

        viewModel.AdvancePreviewFrame();

        AssertEqual(true, viewModel.IsPreviewing);
        AssertEqual(2, viewModel.SelectedEditorFrame?.Index);
    });
}

static void SequenceEditorPlaybackModesControlAdvancement()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        service.ImportFrames(character, action, sourcePaths);
        var section = LoadSequenceSection(service, character, action);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.SelectSection(section);
        viewModel.SelectSectionForManagement(section);

        var modeProperty = typeof(SequenceFramesViewModel).GetProperty("EditorPlaybackMode");
        var startMethod = typeof(SequenceFramesViewModel).GetMethod("StartEditorPreview");
        var advanceMethod = typeof(SequenceFramesViewModel).GetMethod("AdvanceEditorPreviewFrame");
        AssertEqual(true, modeProperty is not null && startMethod is not null && advanceMethod is not null);
        if (modeProperty is null || startMethod is null || advanceMethod is null)
        {
            return;
        }

        var modeType = modeProperty.PropertyType;
        modeProperty.SetValue(viewModel, Enum.Parse(modeType, "Once"));
        startMethod.Invoke(viewModel, null);
        AssertEqual(1, viewModel.CurrentPreviewFrame?.Index);
        AssertEqual(true, viewModel.IsPreviewing);

        AssertEqual(true, Convert.ToBoolean(advanceMethod.Invoke(viewModel, null)));
        AssertEqual(2, viewModel.CurrentPreviewFrame?.Index);
        AssertEqual(true, viewModel.IsPreviewing);

        AssertEqual(false, Convert.ToBoolean(advanceMethod.Invoke(viewModel, null)));
        AssertEqual(2, viewModel.CurrentPreviewFrame?.Index);
        AssertEqual(false, viewModel.IsPreviewing);

        startMethod.Invoke(viewModel, null);
        AssertEqual(1, viewModel.CurrentPreviewFrame?.Index);
        AssertEqual(true, viewModel.IsPreviewing);

        modeProperty.SetValue(viewModel, Enum.Parse(modeType, "Loop"));
        viewModel.AdvancePreviewFrame();
        viewModel.AdvancePreviewFrame();
        AssertEqual(1, viewModel.CurrentPreviewFrame?.Index);
        AssertEqual(true, viewModel.IsPreviewing);
    });
}

static void NewSequenceFrameSynchronizesTimelineSelection()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    const string startMarker = "private async Task InsertBlankSequenceFrameAsync";
    const string endMarker = "private async void DeleteSequenceFrameMenuItem_Click";
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);

    AssertEqual(true, start >= 0 && end > start);
    var insertHandler = source[start..end];
    AssertEqual(true, insertHandler.Contains(
        "SynchronizeSequenceTimelineSelectionToCurrentFrame();",
        StringComparison.Ordinal));
}

static void SequenceFrameMetadataEditDoesNotResetTimeline()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        service.ImportFrames(character, action, sourcePaths);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        var section = viewModel.BaseSectionGroups.SelectMany(group => group.Sections)
            .Single(item => item.Action.Code == action.Code);
        viewModel.SelectSection(section);
        viewModel.SelectSectionForManagement(section);
        viewModel.SelectEditorFrame(viewModel.SelectedSectionFrames[1]);
        var resetCount = 0;
        var minimumCount = viewModel.SelectedSectionFrames.Count;
        viewModel.SelectedSectionFrames.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }

            minimumCount = Math.Min(minimumCount, viewModel.SelectedSectionFrames.Count);
        };

        viewModel.SetFrameDurationAsync(character, section, viewModel.SelectedEditorFrame!, 4)
            .GetAwaiter().GetResult();

        AssertEqual(0, resetCount);
        AssertEqual(2, minimumCount);
        AssertEqual(2, viewModel.SelectedEditorFrame?.Index);
        AssertEqual(4, viewModel.SelectedEditorFrame?.DurationFrames);
    });
}

static void SequenceFrameSnapshotPreservesMetadata()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        var frame = service.ImportFrames(character, action, [sourcePaths[0]])[0];
        service.SetFrameDuration(character, action, frame, 7);
        service.SetFrameVoice(character, action, frame, voicePath);
        frame = LoadSequenceSection(service, character, action).Frames.Single();
        var snapshot = service.CreateActionSnapshot(character, action);

        service.DeleteFrame(character, action, frame);
        service.RestoreActionFrames(character, action, snapshot);

        var restored = LoadSequenceSection(service, character, action).Frames.Single();
        AssertEqual(7, restored.DurationFrames);
        AssertEqual(voicePath, restored.VoiceFilePath);
    });
}

static void SequenceFrameNumbersUseDynamicWidth()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ToolFolderPath);
        var sources = Enumerable.Range(1, 10)
            .Select(index => Path.Combine(root, $"frame-{index}.png"))
            .ToArray();
        for (var index = 0; index < sources.Length; index++)
        {
            WriteSolidImage(
                sources[index],
                Color.FromArgb(255, index + 1, 0, 0),
                SequenceFrameService.RequiredWidth,
                SequenceFrameService.RequiredHeight);
        }

        var service = new SequenceFrameService();
        var action = SequenceFrameService.BuildActions(new CharacterSkillsData()).First();
        var frames = service.ImportFrames(character, action, sources);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        var section = LoadSequenceSection(service, character, action);
        viewModel.SelectSection(section);
        AssertEqual(true, viewModel.CurrentFrameText.StartsWith("01/10", StringComparison.Ordinal));

        var snapshotNames = service.CreateActionSnapshot(character, action)
            .Where(path => !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToArray();
        AssertSequence(
            Enumerable.Range(1, frames.Count).Select(index => $"{index:00}.png").ToArray(),
            snapshotNames!);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void NumberedSequenceFrameImportsUseNumericSuffixOrder()
{
    var paths = new[]
    {
        @"D:\frames\Origin_Misaka_Sk2_1.PNG",
        @"D:\frames\Origin_Misaka_Sk2_10.PNG",
        @"D:\frames\Origin_Misaka_Sk2_11.PNG",
        @"D:\frames\Origin_Misaka_Sk2_2.PNG",
        @"D:\frames\Origin_Misaka_Sk2_3.PNG"
    };

    AssertSequence(
        [paths[0], paths[3], paths[4], paths[1], paths[2]],
        SequenceFrameService.OrderImportSourcePaths(paths).ToArray());
}

static void UnnumberedSequenceFrameImportsPreservePickerOrder()
{
    var paths = new[]
    {
        @"D:\frames\idle_end.PNG",
        @"D:\frames\attack.PNG",
        @"D:\frames\idle_start.PNG"
    };

    AssertSequence(paths, SequenceFrameService.OrderImportSourcePaths(paths).ToArray());
}

static void SequenceTimelineDeleteKeyUsesSelectedFrame()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var timeline = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceFrameTimelineListView",
        StringComparison.Ordinal));

    AssertEqual("SequenceFrameTimelineListView_KeyDown", timeline.Attribute("KeyDown")?.Value ?? string.Empty);
    AssertEqual(true, source.Contains("Windows.System.VirtualKey.Delete", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("DeleteSequenceFrameAsync(frame, section)", StringComparison.Ordinal));

    var deleteMethodStart = source.IndexOf(
        "private async Task DeleteSequenceFrameAsync",
        StringComparison.Ordinal);
    var deleteMethodEnd = source.IndexOf(
        "private async void ReplaceSequenceFrameMenuItem_Click",
        deleteMethodStart,
        StringComparison.Ordinal);
    AssertEqual(true, deleteMethodStart >= 0 && deleteMethodEnd > deleteMethodStart);
    var deleteMethod = source[deleteMethodStart..deleteMethodEnd];
    AssertEqual(false, deleteMethod.Contains("ClearSequencePreviewCache();", StringComparison.Ordinal));
    AssertEqual(true, deleteMethod.Contains("UpdateSequencePreviewImageSource();", StringComparison.Ordinal));
    AssertEqual(true, deleteMethod.Contains("HideSequenceFrameManager();", StringComparison.Ordinal));
}

static void SequenceCollectionMultiSelectionTracksClickOrder()
{
    var assembly = typeof(SequenceFrameCollectionItem).Assembly;
    var trackerType = assembly.GetType("CrossingVoidZDTool.SequenceFrameCollectionSelectionOrder");
    AssertEqual(true, trackerType is not null);
    if (trackerType is null)
    {
        return;
    }

    var tracker = Activator.CreateInstance(trackerType, nonPublic: true);
    var apply = trackerType.GetMethod("ApplySelectionChange");
    var orderedItemsProperty = trackerType.GetProperty("OrderedItems");
    AssertEqual(true, tracker is not null && apply is not null && orderedItemsProperty is not null);
    if (tracker is null || apply is null || orderedItemsProperty is null)
    {
        return;
    }

    var items = Enumerable.Range(1, 4)
        .Select(index => new SequenceFrameCollectionItem(
            $"Frame{index}",
            $"Frame{index}",
            $"C:\\Frame{index}.png",
            $"file:///C:/Frame{index}.png",
            $"Frame{index}.png",
            string.Empty,
            "928x640",
            $"动作 #{index}",
            1,
            false,
            string.Empty))
        .ToList();

    apply.Invoke(tracker, [new[] { items[2] }, Array.Empty<SequenceFrameCollectionItem>(), new[] { items[2] }, items]);
    apply.Invoke(tracker, [new[] { items[0] }, Array.Empty<SequenceFrameCollectionItem>(), new[] { items[0], items[2] }, items]);
    apply.Invoke(tracker, [new[] { items[3], items[1] }, Array.Empty<SequenceFrameCollectionItem>(), items, items]);
    apply.Invoke(tracker, [Array.Empty<SequenceFrameCollectionItem>(), new[] { items[0] }, new[] { items[1], items[2], items[3] }, items]);

    var orderedItems = ((IEnumerable<SequenceFrameCollectionItem>)orderedItemsProperty.GetValue(tracker)!).ToList();
    AssertSequence([items[2], items[1], items[3]], orderedItems.ToArray());
    var selectionOrderProperty = typeof(SequenceFrameCollectionItem).GetProperty("SelectionOrder");
    AssertEqual(true, selectionOrderProperty is not null);
    AssertSequence(
        [0, 2, 1, 3],
        items.Select(item => Convert.ToInt32(selectionOrderProperty?.GetValue(item))).ToArray());

    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    AssertEqual(true, xaml.Contains("Text=\"{Binding SelectionOrderText, Mode=OneWay}\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Visibility=\"{Binding SelectionOrderVisibility, Mode=OneWay}\"", StringComparison.Ordinal));
}

static void SequenceEditorSupportsExtendedMultiSelection()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var timeline = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceFrameTimelineListView",
        StringComparison.Ordinal));

    AssertEqual("Extended", timeline.Attribute("SelectionMode")?.Value ?? string.Empty);
    AssertEqual("SequenceFrameTimelineListView_SelectionChanged", timeline.Attribute("SelectionChanged")?.Value ?? string.Empty);
    AssertEqual(null, timeline.Attribute("SelectedItem"));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceSingleFramePanel\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceMultiFramePanel\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceEditorSingleFrameText\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("x:Name=\"SequenceEditorMultiSelectionText\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("SequenceFrames.CurrentPreviewFrame.BlankVisibility", StringComparison.Ordinal));
    AssertEqual(false, xaml.Contains("SequenceFrames.SelectedEditorFrame.BlankVisibility", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("DeleteSelectedSequenceFramesAsync", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("DuplicateSelectedSequenceFramesAsync", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("UpdateSequenceFrameSelectionPresentation", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("已选择 {selectionCount} 帧｜当前查看第", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("总持续帧格", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("选择复制位置", StringComparison.Ordinal));
}

static void SequenceBatchCopyUsesTimelineTargetSelection()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var timeline = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceFrameTimelineListView",
        StringComparison.Ordinal));

    AssertEqual("True", timeline.Attribute("IsItemClickEnabled")?.Value ?? string.Empty);
    AssertEqual("SequenceFrameTimelineListView_ItemClick", timeline.Attribute("ItemClick")?.Value ?? string.Empty);
    AssertEqual(true, source.Contains("StartSequenceFrameCopyTargetSelection", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("DuplicatePendingSequenceFramesAfterAsync", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("CancelSequenceFrameCopyTargetSelection", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("_isSelectingSequenceFrameCopyTarget", StringComparison.Ordinal));

    var methodStart = source.IndexOf(
        "private async Task DuplicateSelectedSequenceFramesAsync",
        StringComparison.Ordinal);
    var methodEnd = source.IndexOf(
        "private async Task DeleteSequenceFrameAsync",
        methodStart,
        StringComparison.Ordinal);
    AssertEqual(true, methodStart >= 0 && methodEnd > methodStart);
    var method = source[methodStart..methodEnd];
    AssertEqual(false, method.Contains("ShowContentAsync", StringComparison.Ordinal));
    AssertEqual(false, method.Contains("ContentDialogRequest", StringComparison.Ordinal));
}

static void SequenceFrameReorderRefreshesPreview()
{
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var methodStart = source.IndexOf(
        "private async void SequenceFrameManagerGridView_DragItemsCompleted",
        StringComparison.Ordinal);
    var methodEnd = source.IndexOf(
        "private async void CopySequenceFrameMenuItem_Click",
        methodStart,
        StringComparison.Ordinal);
    AssertEqual(true, methodStart >= 0 && methodEnd > methodStart);
    var method = source[methodStart..methodEnd];

    AssertEqual(false, method.Contains("ClearSequencePreviewCache();", StringComparison.Ordinal));
    AssertEqual(true, method.Contains("UpdateSequencePreviewImageSource();", StringComparison.Ordinal));
    AssertEqual(true, method.Contains("UpdateSequencePreviewInterval();", StringComparison.Ordinal));
}

static void SequencePreviewPrimesNextFrame()
{
    var presenter = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(),
        "Controls",
        "BufferedImagePresenter.xaml.cs"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));

    AssertEqual(true, presenter.Contains("public void Prepare(ImageSource? source)", StringComparison.Ordinal));
    AssertEqual(true, presenter.Contains("ShowPrepared", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("PrepareNextSequencePreviewSource", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("SequencePreviewPresenter.Prepare", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("SequenceEditorPreviewPresenter.Prepare", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("(currentIndex + 1) % frames.Count", StringComparison.Ordinal));
}

static void SequencePreviewInvalidatesDecodedWriteableBitmaps()
{
    var source = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(),
        "Services",
        "SequencePreviewBitmapCache.cs"));
    var invalidateIndex = source.IndexOf("writeableBitmap.Invalidate();", StringComparison.Ordinal);
    var returnIndex = source.IndexOf("return writeableBitmap;", StringComparison.Ordinal);

    AssertEqual(true, invalidateIndex >= 0);
    AssertEqual(true, returnIndex > invalidateIndex);
}

static void SequenceEditorProvidesPlaybackModeAndSpaceShortcut()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var playbackModeToggle = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceEditorPlaybackModeToggle",
        StringComparison.Ordinal));

    AssertEqual("ToggleSwitch", playbackModeToggle.Name.LocalName);
    AssertEqual("播放一次", playbackModeToggle.Attribute("OffContent")?.Value ?? string.Empty);
    AssertEqual("循环播放", playbackModeToggle.Attribute("OnContent")?.Value ?? string.Empty);
    AssertEqual(
        "{Binding SequenceFrames.IsEditorPlaybackLoop, Mode=TwoWay}",
        playbackModeToggle.Attribute("IsOn")?.Value ?? string.Empty);
    AssertEqual(false, xaml.Contains("GroupName=\"SequenceEditorPlaybackMode\"", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("VirtualKey.Space", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("ToggleSequenceEditorPreviewAsync", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("StartSequencePreviewAsync(isEditorPlayback: true)", StringComparison.Ordinal));
}

static void SequenceEditorCanPauseWhenEffectiveVoiceEnds()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var viewModelSource = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(),
        "ViewModels",
        "SequenceFramesViewModel.cs"));
    var voiceSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.VoiceMaterials.cs"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var pauseToggle = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceEditorPauseWhenVoiceEndsToggle",
        StringComparison.Ordinal));

    AssertEqual("ToggleSwitch", pauseToggle.Name.LocalName);
    AssertEqual("语音结束继续", pauseToggle.Attribute("OffContent")?.Value ?? string.Empty);
    AssertEqual("语音结束暂停", pauseToggle.Attribute("OnContent")?.Value ?? string.Empty);
    AssertEqual(
        "{Binding SequenceFrames.PauseEditorPreviewWhenVoiceEnds, Mode=TwoWay}",
        pauseToggle.Attribute("IsOn")?.Value ?? string.Empty);
    AssertEqual(true, viewModelSource.Contains("private bool _pauseEditorPreviewWhenVoiceEnds;", StringComparison.Ordinal));
    AssertEqual(true, viewModelSource.Contains("public bool PauseEditorPreviewWhenVoiceEnds", StringComparison.Ordinal));
    AssertEqual(true, voiceSource.Contains("_isSequenceEditorPreviewPlayback", StringComparison.Ordinal));
    AssertEqual(true, voiceSource.Contains("SequenceFrames.PauseEditorPreviewWhenVoiceEnds", StringComparison.Ordinal));
}

static void SequencePreviewVoiceUsesPersistentSingleChannel()
{
    var sequenceSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    var voiceSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.VoiceMaterials.cs"));

    var stopPreviewStart = sequenceSource.IndexOf("private void StopSequencePreview()", StringComparison.Ordinal);
    var stopPreviewEnd = sequenceSource.IndexOf("private void UpdateSequencePreviewInterval()", stopPreviewStart, StringComparison.Ordinal);
    AssertEqual(true, stopPreviewStart >= 0 && stopPreviewEnd > stopPreviewStart);
    var stopPreviewMethod = sequenceSource[stopPreviewStart..stopPreviewEnd];
    AssertEqual(false, stopPreviewMethod.Contains("StopVoicePlayback();", StringComparison.Ordinal));

    var playFrameVoiceStart = sequenceSource.IndexOf("private void PlayCurrentSequenceFrameVoice()", StringComparison.Ordinal);
    var playFrameVoiceEnd = sequenceSource.IndexOf("private async Task PreloadSequencePreviewBitmapsAsync", playFrameVoiceStart, StringComparison.Ordinal);
    AssertEqual(true, playFrameVoiceStart >= 0 && playFrameVoiceEnd > playFrameVoiceStart);
    var playFrameVoiceMethod = sequenceSource[playFrameVoiceStart..playFrameVoiceEnd];
    AssertEqual(true, playFrameVoiceMethod.Contains("if (!string.IsNullOrWhiteSpace(voiceFilePath)", StringComparison.Ordinal));
    AssertEqual(false, playFrameVoiceMethod.Contains("else", StringComparison.Ordinal));

    var playVoiceStart = voiceSource.IndexOf("private void PlayVoiceFile", StringComparison.Ordinal);
    var playVoiceEnd = voiceSource.IndexOf("private void StopVoicePlayback", playVoiceStart, StringComparison.Ordinal);
    AssertEqual(true, playVoiceStart >= 0 && playVoiceEnd > playVoiceStart);
    var playVoiceMethod = voiceSource[playVoiceStart..playVoiceEnd];
    AssertEqual(true, playVoiceMethod.IndexOf("StopVoicePlayback();", StringComparison.Ordinal) <
        playVoiceMethod.IndexOf("mediaPlayer.Play();", StringComparison.Ordinal));
    AssertEqual(true, playVoiceMethod.Contains("new MediaPlayer()", StringComparison.Ordinal));
    AssertEqual(true, playVoiceMethod.Contains("_voiceDurationReader.GetEffectiveDuration(filePath)", StringComparison.Ordinal));
    AssertEqual(true, playVoiceMethod.Contains("ScheduleVoicePlaybackStop(mediaPlayer", StringComparison.Ordinal));
    AssertEqual(true, voiceSource.Contains("ReferenceEquals(sender, _voiceMediaPlayer)", StringComparison.Ordinal));
    AssertEqual(true, voiceSource.Contains("IsRepeating = false", StringComparison.Ordinal));
    AssertEqual(true, voiceSource.Contains("ReferenceEquals(mediaPlayer, _voiceMediaPlayer)", StringComparison.Ordinal));
    AssertEqual(true, voiceSource.Contains("_applicationViewModel.SequenceFrames.IsPreviewing", StringComparison.Ordinal));
    AssertEqual(true, voiceSource.Contains("StopSequencePreview();", StringComparison.Ordinal));
    AssertEqual(false, voiceSource.Contains("private readonly MediaPlayer _voiceMediaPlayer", StringComparison.Ordinal));
}

static void WaveDurationReaderHandlesStandardAndExtraChunks()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var standardPath = Path.Combine(root, "standard.wav");
        var extraChunkPath = Path.Combine(root, "extra.wav");
        WriteWaveFile(standardPath, 1, sampleCount: 10000);
        WriteWaveFileWithJunkMetadata(extraChunkPath, 2, sampleCount: 6000);

        var readerType = typeof(SequenceFrameService).Assembly.GetType(
            "CrossingVoidZDTool.Services.WaveAudioDurationReader");
        AssertEqual(true, readerType is not null);
        if (readerType is null)
        {
            return;
        }

        var reader = Activator.CreateInstance(readerType);
        var method = readerType.GetMethod("GetDuration");
        AssertEqual(true, reader is not null && method is not null);
        if (reader is null || method is null)
        {
            return;
        }

        AssertEqual(TimeSpan.FromSeconds(1.25), (TimeSpan?)method.Invoke(reader, [standardPath]));
        AssertEqual(TimeSpan.FromSeconds(0.75), (TimeSpan?)method.Invoke(reader, [extraChunkPath]));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void WaveEffectiveDurationKeepsLeadingAndTrimsTrailingSilence()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var voicePath = Path.Combine(root, "leading-voice-trailing.wav");
        WriteWaveFileWithSilenceSegments(
            voicePath,
            leadingSilentSampleCount: 1600,
            audibleSampleCount: 3200,
            trailingSilentSampleCount: 4800);
        var extensibleVoicePath = Path.Combine(root, "extensible-six-channel.wav");
        WriteExtensibleWaveFileWithSilenceSegments(
            extensibleVoicePath,
            leadingSilentFrameCount: 800,
            audibleFrameCount: 1600,
            trailingSilentFrameCount: 2400);
        var readerType = typeof(SequenceFrameService).Assembly.GetType(
            "CrossingVoidZDTool.Services.WaveAudioDurationReader");
        var reader = readerType is null ? null : Activator.CreateInstance(readerType);
        var effectiveMethod = readerType?.GetMethod("GetEffectiveDuration");
        AssertEqual(true, reader is not null && effectiveMethod is not null);
        if (reader is null || effectiveMethod is null)
        {
            return;
        }

        AssertEqual(TimeSpan.FromSeconds(0.65), (TimeSpan?)effectiveMethod.Invoke(reader, [voicePath]));
        AssertEqual(TimeSpan.FromSeconds(0.35), (TimeSpan?)effectiveMethod.Invoke(reader, [extensibleVoicePath]));

        var analyzerType = typeof(SequenceFrameService).Assembly.GetType(
            "CrossingVoidZDTool.Services.SequenceVoiceSyncAnalyzer");
        var analyzer = analyzerType is null ? null : Activator.CreateInstance(analyzerType);
        var analyze = analyzerType?.GetMethod("Analyze");
        AssertEqual(true, analyzer is not null && analyze is not null);
        if (analyzer is null || analyze is null)
        {
            return;
        }

        var frame = CreateSequenceFrameForVoiceSync(1, 7, voicePath);
        var result = ((System.Collections.IEnumerable)analyze.Invoke(analyzer, [new[] { frame }, 10])!)
            .Cast<object>()
            .Single();
        AssertEqual(7, ReadIntProperty(result, "RequiredFrames"));
        AssertEqual(
            "有效语音：0.65 秒 / 7 格",
            Convert.ToString(ReadProperty(result, "DurationText")) ?? string.Empty);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SequenceVoiceSyncAnalysisCalculatesFrameDifferences()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var interruptedVoicePath = Path.Combine(root, "interrupted.wav");
        var endingVoicePath = Path.Combine(root, "ending.wav");
        WriteWaveFile(interruptedVoicePath, 1, sampleCount: 8000);
        WriteWaveFile(endingVoicePath, 2, sampleCount: 4800);
        var frames = new[]
        {
            CreateSequenceFrameForVoiceSync(1, 2, interruptedVoicePath),
            CreateSequenceFrameForVoiceSync(2, 3, string.Empty),
            CreateSequenceFrameForVoiceSync(3, 2, endingVoicePath),
            CreateSequenceFrameForVoiceSync(4, 2, string.Empty)
        };

        var analyzerType = typeof(SequenceFrameService).Assembly.GetType(
            "CrossingVoidZDTool.Services.SequenceVoiceSyncAnalyzer");
        AssertEqual(true, analyzerType is not null);
        if (analyzerType is null)
        {
            return;
        }

        var analyzer = Activator.CreateInstance(analyzerType);
        var analyze = analyzerType.GetMethod("Analyze");
        AssertEqual(true, analyzer is not null && analyze is not null);
        if (analyzer is null || analyze is null)
        {
            return;
        }

        var results = ((System.Collections.IEnumerable)analyze.Invoke(analyzer, [frames, 10])!)
            .Cast<object>()
            .ToArray();
        AssertEqual(2, results.Length);

        var interrupted = results.Single(result => ReadIntProperty(result, "FrameIndex") == 1);
        AssertEqual(10, ReadIntProperty(interrupted, "RequiredFrames"));
        AssertEqual(5, ReadIntProperty(interrupted, "AvailableFrames"));
        AssertEqual(5, ReadIntProperty(interrupted, "DifferenceFrames"));
        AssertEqual("Interrupted", ReadProperty(interrupted, "StatusKind")?.ToString() ?? string.Empty);
        AssertEqual(3, ReadIntProperty(interrupted, "BoundaryFrameIndex"));
        AssertEqual(
            "同步结果：被第 3 帧语音顶替，还差 5 格 / 0.5 秒",
            Convert.ToString(ReadProperty(interrupted, "StatusText")) ?? string.Empty);

        var ending = results.Single(result => ReadIntProperty(result, "FrameIndex") == 3);
        AssertEqual(6, ReadIntProperty(ending, "RequiredFrames"));
        AssertEqual(4, ReadIntProperty(ending, "AvailableFrames"));
        AssertEqual(2, ReadIntProperty(ending, "DifferenceFrames"));
        AssertEqual("AnimationShorter", ReadProperty(ending, "StatusKind")?.ToString() ?? string.Empty);
        AssertEqual(4, ReadIntProperty(ending, "SuggestedFrameIndex"));
        AssertEqual(4, ReadIntProperty(ending, "SuggestedDurationFrames"));
        AssertEqual(
            "可用长度：4 格 / 0.4 秒（到序列结束）",
            Convert.ToString(ReadProperty(ending, "AvailableText")) ?? string.Empty);
        AssertEqual(
            "同步结果：动画少 2 格 / 0.2 秒",
            Convert.ToString(ReadProperty(ending, "StatusText")) ?? string.Empty);
        AssertEqual(
            "少 2 格",
            Convert.ToString(ReadProperty(ending, "BadgeText")) ?? string.Empty);
        AssertEqual(
            "建议：第 4 帧由 2 格调整为 4 格",
            Convert.ToString(ReadProperty(ending, "SuggestionText")) ?? string.Empty);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SequenceVoiceSyncAnalysisRefreshesWhenFpsChanges()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ToolFolderPath);
        var imagePath = Path.Combine(root, "frame.png");
        WriteSolidImage(imagePath, Color.Red, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        var voiceSourcePath = Path.Combine(root, "voice.wav");
        WriteWaveFile(voiceSourcePath, 1, sampleCount: 8000);
        var voice = new VoiceMaterialService().Import(character, VoiceMaterialKind.Click, voiceSourcePath);
        var service = new SequenceFrameService();
        var action = SequenceFrameService.BuildActions(new CharacterSkillsData()).Single(item => item.Code == "Click");
        var frame = service.ImportFrames(character, action, [imagePath]).Single();
        service.SetFrameVoice(character, action, frame, voice.FilePath);

        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService());
        viewModel.LoadAsync(character).GetAwaiter().GetResult();
        var section = viewModel.BaseSectionGroups.SelectMany(group => group.Sections)
            .Single(item => item.Action.Code == action.Code);
        viewModel.SelectSection(section);
        viewModel.SelectSectionForManagement(section);

        AssertEqual(12, ReadSelectedVoiceSyncRequiredFrames(viewModel));
        viewModel.PreviewFps = 20;
        AssertEqual(20, ReadSelectedVoiceSyncRequiredFrames(viewModel));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SequenceVoiceSyncResultsAppearInInspectorAndTimeline()
{
    var xaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var document = XDocument.Parse(xaml);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var inspectorScrollViewer = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceFrameInspectorScrollViewer",
        StringComparison.Ordinal));
    var inspectorBorder = inspectorScrollViewer.Parent;
    AssertEqual("Border", inspectorBorder?.Name.LocalName ?? string.Empty);
    if (inspectorBorder is null)
    {
        return;
    }
    var timeline = document.Descendants().Single(element => string.Equals(
        element.Attribute(x + "Name")?.Value,
        "SequenceFrameTimelineListView",
        StringComparison.Ordinal));
    var timelineHost = document.Descendants().Single(element =>
        element.Name.LocalName == "Grid" &&
        string.Equals(element.Attribute("Grid.Row")?.Value, "2", StringComparison.Ordinal) &&
        element.Descendants().Contains(timeline));

    AssertEqual(true, xaml.Contains("SequenceVoiceSyncInspector", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("VoiceSyncDurationText", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("VoiceSyncAvailableText", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("VoiceSyncStatusText", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("VoiceSyncSuggestionText", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("VoiceSyncBadgeText", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("VoiceSyncVisibility", StringComparison.Ordinal));
    AssertEqual("1", inspectorBorder.Attribute("Grid.Row")?.Value ?? string.Empty);
    AssertEqual("2", inspectorBorder.Attribute("Grid.RowSpan")?.Value ?? string.Empty);
    AssertEqual("1", inspectorBorder.Attribute("Grid.Column")?.Value ?? string.Empty);
    AssertEqual("0", timelineHost.Attribute("Grid.Column")?.Value ?? string.Empty);
}

static SequenceFrameItem CreateSequenceFrameForVoiceSync(int index, int durationFrames, string voiceFilePath)
{
    return new SequenceFrameItem(
        string.Empty,
        string.Empty,
        $"frame-{index}.png",
        $"frame-{index}",
        index,
        SequenceFrameService.RequiredWidth,
        SequenceFrameService.RequiredHeight,
        true,
        DateTime.MinValue,
        DurationFrames: durationFrames,
        VoiceFilePath: voiceFilePath,
        VoiceFileName: Path.GetFileName(voiceFilePath));
}

static object? ReadProperty(object instance, string propertyName)
{
    return instance.GetType().GetProperty(propertyName)?.GetValue(instance);
}

static int ReadIntProperty(object instance, string propertyName)
{
    return Convert.ToInt32(ReadProperty(instance, propertyName));
}

static int ReadSelectedVoiceSyncRequiredFrames(SequenceFramesViewModel viewModel)
{
    var result = viewModel.SelectedEditorFrame?.GetType().GetProperty("VoiceSyncResult")
        ?.GetValue(viewModel.SelectedEditorFrame);
    AssertEqual(true, result is not null);
    return result is null ? 0 : ReadIntProperty(result, "RequiredFrames");
}

static SequenceFrameSection LoadSequenceSection(
    SequenceFrameService service,
    CharacterCard character,
    SequenceFrameAction action)
{
    return service.LoadSections(character, new CharacterSkillsData())
        .Single(section => section.Action.Code == action.Code);
}

static void WithSequenceFrameWorkspace(
    Action<SequenceFrameService, CharacterCard, SequenceFrameAction, IReadOnlyList<string>, string> test)
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ToolFolderPath);
        var firstPath = Path.Combine(root, "first.png");
        var secondPath = Path.Combine(root, "second.png");
        WriteSolidImage(firstPath, Color.Red, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        WriteSolidImage(secondPath, Color.Green, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        var voiceSourcePath = Path.Combine(root, "voice.wav");
        WriteWaveFile(voiceSourcePath, 1);
        var voice = new VoiceMaterialService().Import(character, VoiceMaterialKind.Skill1, voiceSourcePath);
        var action = SequenceFrameService.BuildActions(new CharacterSkillsData()).First();

        test(new SequenceFrameService(), character, action, [firstPath, secondPath], voice.FilePath);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SetFirstTag(
    UnrealSyncViewModel viewModel,
    CharacterKeywordTagCategoryKind kind,
    string value)
{
    viewModel.KeywordTagCategories.Single(category => category.Kind == kind).Entries[0].Value = value;
}

static void WithCharacterWorkspace(Action<BaseMaterialService, CharacterCard, string> test)
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "character_test", "测试角色");
        test(new BaseMaterialService(), character, root);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static CharacterCard CreateCharacter(string root, string code, string name)
{
    return new CharacterCard(
        code,
        name,
        root,
        Path.Combine(root, "Tool"),
        Path.Combine(root, "References"),
        DateTime.UtcNow,
        false,
        name,
        string.Empty);
}

static string CreateWorkspaceCharacterFolder(
    string parentPath,
    string code,
    string name,
    bool isCompleted)
{
    var characterPath = Path.Combine(parentPath, code);
    var toolPath = Path.Combine(characterPath, "tool");
    Directory.CreateDirectory(toolPath);
    var metadata = new CharacterMetadata
    {
        Code = code,
        Name = name,
        IsCompleted = isCompleted,
        LastEditedAt = DateTime.UtcNow
    };
    File.WriteAllText(
        Path.Combine(toolPath, "character.json"),
        JsonSerializer.Serialize(metadata, AppJsonSerializerContext.Default.CharacterMetadata));
    return characterPath;
}

static void UnrealBridgeChangesSelectSafeUpdatesByDefault()
{
    var toolbox = CreateUnrealBridgeSnapshot(
        ("character:info", UnrealBridgeModule.CharacterInfo, "角色信息", "info-new"),
        ("material:portrait:main", UnrealBridgeModule.BaseMaterials, "主立绘", "portrait-new"));
    var unreal = CreateUnrealBridgeSnapshot(
        ("character:info", UnrealBridgeModule.CharacterInfo, "角色信息", "info-old"));
    var state = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["character:info"] = new UnrealBridgeSyncStateEntry("info-old", "info-old")
        }
    };

    var changes = new UnrealBridgeDiffService().Compare(
        toolbox,
        unreal,
        UnrealBridgeDirection.PublishToUnreal,
        state);

    AssertEqual(UnrealBridgeChangeKind.Updated, changes.Single(item => item.StableId == "character:info").Kind);
    AssertEqual(true, changes.Single(item => item.StableId == "character:info").IsSelected);
    AssertEqual(UnrealBridgeChangeKind.Added, changes.Single(item => item.StableId == "material:portrait:main").Kind);
    AssertEqual(true, changes.Single(item => item.StableId == "material:portrait:main").IsSelected);
}

static void UnrealSyncCharacterSelectorUsesSummaryAndFlyoutList()
{
    var document = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var codeText = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.UnrealProjectSync.cs"));
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var summary = document.Descendants().Single(element =>
        string.Equals(
            element.Attribute(x + "Name")?.Value,
            "UnrealCharacterSummaryPanel",
            StringComparison.Ordinal));
    var selectorButton = document.Descendants().Single(element =>
        string.Equals(
            element.Attribute(x + "Name")?.Value,
            "UnrealCharacterSelectorButton",
            StringComparison.Ordinal));
    var refreshButton = document.Descendants().Single(element =>
        string.Equals(
            element.Attribute(x + "Name")?.Value,
            "RefreshUnrealCharactersButton",
            StringComparison.Ordinal));
    var flyout = selectorButton.Descendants().Single(element => element.Name.LocalName == "Flyout");
    var search = document.Descendants().Single(element =>
        string.Equals(
            element.Attribute(x + "Name")?.Value,
            "UnrealSyncSourceSearchBox",
            StringComparison.Ordinal));
    var list = document.Descendants().Single(element =>
        string.Equals(
            element.Attribute(x + "Name")?.Value,
            "UnrealSyncCharacterSourceListView",
            StringComparison.Ordinal));
    var itemTemplate = list.Descendants().Single(element => element.Name.LocalName == "DataTemplate");
    var rowGrid = itemTemplate.Elements().Single(element => element.Name.LocalName == "Grid");
    var displayName = itemTemplate.Descendants().Single(element =>
        string.Equals(element.Attribute("Text")?.Value, "{Binding DisplayName}", StringComparison.Ordinal));
    var code = itemTemplate.Descendants().Single(element =>
        string.Equals(element.Attribute("Text")?.Value, "{Binding SecondaryText}", StringComparison.Ordinal));

    AssertEqual(true, summary.Descendants().Any(element =>
        string.Equals(
            element.Attribute("Text")?.Value,
            "{Binding UnrealProjectSync.SelectedSource.DisplayName, Mode=OneWay}",
            StringComparison.Ordinal)));
    AssertEqual(true, summary.Descendants().Any(element =>
        string.Equals(
            element.Attribute("Text")?.Value,
            "{Binding UnrealProjectSync.SelectedSource.SecondaryText, Mode=OneWay}",
            StringComparison.Ordinal)));
    AssertEqual("搜索中文名、英文代号或素材类别", search.Attribute("PlaceholderText")?.Value ?? string.Empty);
    AssertEqual(true, list.Ancestors().Any(element => ReferenceEquals(element, flyout)));
    AssertEqual("Auto", list.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value ?? string.Empty);
    AssertEqual("Disabled", list.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value ?? string.Empty);
    AssertEqual("460", list.Attribute("MaxHeight")?.Value ?? string.Empty);
    AssertEqual("True", list.Attribute("IsItemClickEnabled")?.Value ?? string.Empty);
    AssertEqual("UnrealSyncSourceListView_ItemClick", list.Attribute("ItemClick")?.Value ?? string.Empty);
    AssertEqual("52", rowGrid.Attribute("Height")?.Value ?? string.Empty);
    AssertEqual("Stretch", rowGrid.Attribute("HorizontalAlignment")?.Value ?? string.Empty);
    AssertEqual(false, itemTemplate.Descendants().Any(element => element.Name.LocalName == "Button"));
    AssertEqual(true, list.Descendants().Any(element =>
        element.Name.LocalName == "Setter" &&
        string.Equals(element.Attribute("Property")?.Value, "HorizontalAlignment", StringComparison.Ordinal) &&
        string.Equals(element.Attribute("Value")?.Value, "Stretch", StringComparison.Ordinal)));
    AssertEqual("NoWrap", displayName.Attribute("TextWrapping")?.Value ?? string.Empty);
    AssertEqual("CharacterEllipsis", displayName.Attribute("TextTrimming")?.Value ?? string.Empty);
    AssertEqual("NoWrap", code.Attribute("TextWrapping")?.Value ?? string.Empty);
    AssertEqual("CharacterEllipsis", code.Attribute("TextTrimming")?.Value ?? string.Empty);
    AssertEqual("GetUnrealProjectCharactersButton_Click", refreshButton.Attribute("Click")?.Value ?? string.Empty);
    AssertEqual(true, refreshButton.Descendants().Any(element =>
        string.Equals(element.Attribute("Text")?.Value, "刷新来源列表", StringComparison.Ordinal)));
    AssertEqual(true, codeText.Contains("UnrealSyncSourceListView_ItemClick", StringComparison.Ordinal));
    AssertEqual(true, codeText.Contains("e.ClickedItem is not UnrealSyncSourceItem source", StringComparison.Ordinal));
    AssertEqual(true, codeText.Contains("UnrealCharacterSelectorButton.Flyout.Hide();", StringComparison.Ordinal));
}

static void UnrealProjectCharacterSummaryUsesChineseNameWithoutFreshDetails()
{
    var root = Path.Combine(Path.GetTempPath(), $"CrossingVoidZDTool-UnrealSummary-{Guid.NewGuid():N}");
    var projectPath = Path.Combine(root, "CrossingVoid.uproject");
    var basePath = Path.Combine(root, "Content", "AssetMaterial", "ImageS", "CharaterS", "Origin_Ako");
    var zdPath = Path.Combine(root, "Content", "GameActor2D", "Origin_Ako", "Material");
    var manifestPath = Path.Combine(root, "Intermediate", UnrealProjectSyncService.ExportFolderName, UnrealProjectSyncService.ExportManifestFileName);
    try
    {
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(zdPath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(projectPath, "{}");
        File.WriteAllBytes(Path.Combine(basePath, "Origin_Ako_Icon.uasset"), [0]);
        File.WriteAllBytes(Path.Combine(zdPath, "Origin_Ako_Idle.uasset"), [0]);
        File.WriteAllText(
            manifestPath,
            """
            {
              "schemaVersion": 1,
              "generatedAt": "",
              "projectPath": "",
              "targets": [],
              "assets": [],
              "characterItems": [],
              "characterActors": [],
              "characterSequences": [],
              "characterBuffs": [],
              "characterSummaries": [
                { "code": "Origin_Ako", "displayName": "亚子" }
              ]
            }
            """);

        var candidate = new UnrealProjectSyncService()
            .Check(string.Empty, projectPath)
            .CharacterCandidates
            .Single(item => item.Code == "Origin_Ako");

        AssertEqual("亚子", candidate.DisplayName);
        AssertEqual("Origin_Ako", candidate.Code);
        AssertEqual(false, candidate.HasLatestData);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void UnrealProjectCharacterReadsChineseNameFromItemAsset()
{
    var root = Path.Combine(Path.GetTempPath(), $"CrossingVoidZDTool-UnrealItemName-{Guid.NewGuid():N}");
    var projectPath = Path.Combine(root, "CrossingVoid.uproject");
    var basePath = Path.Combine(root, "Content", "AssetMaterial", "ImageS", "CharaterS", "Origin_Ako");
    var zdPath = Path.Combine(root, "Content", "GameActor2D", "Origin_Ako", "Material");
    var itemPath = Path.Combine(root, "Content", "ITems", "CharItemS", "Item_Origin_Ako.uasset");
    try
    {
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(zdPath);
        Directory.CreateDirectory(Path.GetDirectoryName(itemPath)!);
        File.WriteAllText(projectPath, "{}");
        File.WriteAllBytes(Path.Combine(basePath, "Origin_Ako_Icon.uasset"), [0]);
        File.WriteAllBytes(Path.Combine(zdPath, "Origin_Ako_Idle.uasset"), [0]);
        using (var stream = File.Create(itemPath))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(new byte[24]);
            writer.Write(-5);
            writer.Write(Encoding.Unicode.GetBytes("玉置亚子\0"));
            writer.Write(new byte[24]);
        }

        var candidate = new UnrealProjectSyncService()
            .Check(string.Empty, projectPath)
            .CharacterCandidates
            .Single(item => item.Code == "Origin_Ako");

        AssertEqual("玉置亚子", candidate.DisplayName);
        AssertEqual(false, candidate.HasLatestData);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void UnrealProjectCharacterRefreshUsesOfflineItemScan()
{
    var service = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Services", "UnrealProjectSyncService.cs"));
    var window = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.UnrealProjectSync.cs"));

    AssertEqual(true, service.Contains("ReadCharacterItemDisplayNames", StringComparison.Ordinal));
    AssertEqual(true, window.Contains("_applicationViewModel.UnrealProjectSync.Detect();", StringComparison.Ordinal));
    var handlerStart = window.IndexOf("GetUnrealProjectCharactersButton_Click", StringComparison.Ordinal);
    var handlerEnd = window.IndexOf("ImportSelectedUnrealCharacterToDraftButton_Click", handlerStart, StringComparison.Ordinal);
    var handler = window[handlerStart..handlerEnd];
    AssertEqual(false, handler.Contains("RefreshProjectCharacterSummariesAsync", StringComparison.Ordinal));
    AssertEqual(false, handler.Contains("ExportProjectCharactersAsync", StringComparison.Ordinal));
}

static void UnrealProjectDetailExportRejectsFailedProcess()
{
    var service = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Services", "UnrealProjectSyncService.cs"));

    AssertEqual(true, service.Contains("if (process.ExitCode != 0)", StringComparison.Ordinal));
    AssertEqual(true, service.Contains("Unreal 角色数据导出失败", StringComparison.Ordinal));
}

static void UnrealProjectDetailExportRejectsMismatchedModuleBuildIds()
{
    var root = Path.Combine(Path.GetTempPath(), $"CrossingVoidZDTool-UnrealModules-{Guid.NewGuid():N}");
    var engineRoot = Path.Combine(root, "EngineRoot");
    var editorPath = Path.Combine(engineRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
    var projectPath = Path.Combine(root, "CrossingVoid", "CrossingVoid.uproject");
    var projectModulesPath = Path.Combine(root, "CrossingVoid", "Binaries", "Win64", "UnrealEditor.modules");
    var pluginModulesPath = Path.Combine(root, "CrossingVoid", "Plugins", "PostProcessWidget", "Binaries", "Win64", "UnrealEditor.modules");
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(projectModulesPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(pluginModulesPath)!);
        File.WriteAllBytes(editorPath, [0]);
        File.WriteAllText(projectPath, "{}");
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(editorPath)!, "UnrealEditor.modules"),
            """
            { "BuildId": "current-build", "Modules": { "Engine": "UnrealEditor-Engine.dll" } }
            """);
        File.WriteAllText(
            projectModulesPath,
            """
            { "BuildId": "old-build", "Modules": { "CrossingVoid": "UnrealEditor-CrossingVoid.dll" } }
            """);
        File.WriteAllText(
            pluginModulesPath,
            """
            { "BuildId": "old-build", "Modules": { "PostProcessWidget": "UnrealEditor-PostProcessWidget.dll" } }
            """);

        try
        {
            _ = new UnrealProjectSyncService().BuildExportProcessStartInfo(editorPath, projectPath);
            throw new InvalidOperationException("模块 BuildId 不匹配时应当拒绝启动 Unreal。");
        }
        catch (InvalidOperationException exception)
        {
            AssertEqual(true, exception.Message.Contains("需要重新编译", StringComparison.Ordinal));
            AssertEqual(true, exception.Message.Contains("current-build", StringComparison.Ordinal));
            AssertEqual(true, exception.Message.Contains("old-build", StringComparison.Ordinal));
            AssertEqual(true, exception.Message.Contains("CrossingVoid", StringComparison.Ordinal));
            AssertEqual(true, exception.Message.Contains("PostProcessWidget", StringComparison.Ordinal));
            AssertEqual(true, exception.Message.Contains("Build.bat", StringComparison.Ordinal));
            AssertEqual(true, exception.Message.Contains("CrossingVoidEditor Win64 Development", StringComparison.Ordinal));
        }
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void UnrealBridgeChangesProtectConflictsAndDeletes()
{
    var toolbox = CreateUnrealBridgeSnapshot(
        ("character:info", UnrealBridgeModule.CharacterInfo, "角色信息", "tool-new"));
    var unreal = CreateUnrealBridgeSnapshot(
        ("character:info", UnrealBridgeModule.CharacterInfo, "角色信息", "unreal-new"),
        ("buff:legacy", UnrealBridgeModule.Buffs, "旧 BUFF", "legacy"));
    var state = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["character:info"] = new UnrealBridgeSyncStateEntry("shared-old", "shared-old")
        }
    };

    var changes = new UnrealBridgeDiffService().Compare(
        toolbox,
        unreal,
        UnrealBridgeDirection.PublishToUnreal,
        state);

    AssertEqual(UnrealBridgeChangeKind.Conflict, changes.Single(item => item.StableId == "character:info").Kind);
    AssertEqual(false, changes.Single(item => item.StableId == "character:info").IsSelected);
    AssertEqual(UnrealBridgeChangeKind.DeleteCandidate, changes.Single(item => item.StableId == "buff:legacy").Kind);
    AssertEqual(false, changes.Single(item => item.StableId == "buff:legacy").IsSelected);
}

static void UnrealBridgeChangesRespectSyncDirection()
{
    var toolbox = CreateUnrealBridgeSnapshot(
        ("character:info", UnrealBridgeModule.CharacterInfo, "角色信息", "shared-old"));
    var unreal = CreateUnrealBridgeSnapshot(
        ("character:info", UnrealBridgeModule.CharacterInfo, "角色信息", "unreal-new"));
    var state = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["character:info"] = new UnrealBridgeSyncStateEntry("shared-old", "shared-old")
        }
    };
    var service = new UnrealBridgeDiffService();

    var publish = service.Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, state).Single();
    var import = service.Compare(toolbox, unreal, UnrealBridgeDirection.ImportFromUnreal, state).Single();

    AssertEqual(UnrealBridgeChangeKind.Conflict, publish.Kind);
    AssertEqual(false, publish.IsSelected);
    AssertEqual(UnrealBridgeChangeKind.Updated, import.Kind);
    AssertEqual(true, import.IsSelected);
}

static void UnrealSyncWorkspaceSwitchesWholeDirection()
{
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());

    viewModel.IsEngineToToolbox = true;
    AssertEqual(Visibility.Visible, viewModel.ImportWorkspaceVisibility);
    AssertEqual(Visibility.Collapsed, viewModel.PublishWorkspaceVisibility);
    AssertEqual("导入所选到草稿", viewModel.ExecuteActionText);

    viewModel.IsEngineToToolbox = false;
    AssertEqual(Visibility.Collapsed, viewModel.ImportWorkspaceVisibility);
    AssertEqual(Visibility.Visible, viewModel.PublishWorkspaceVisibility);
    AssertEqual("同步所选到虚幻", viewModel.ExecuteActionText);

    var xaml = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var importPanel = xaml.Descendants().Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "UnrealImportWorkspacePanel");
    var publishPanel = xaml.Descendants().Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "UnrealPublishWorkspacePanel");
    AssertEqual("{Binding UnrealProjectSync.ImportWorkspaceVisibility, Mode=OneWay}", importPanel.Attribute("Visibility")?.Value ?? string.Empty);
    AssertEqual("{Binding UnrealProjectSync.PublishWorkspaceVisibility, Mode=OneWay}", publishPanel.Attribute("Visibility")?.Value ?? string.Empty);
    AssertEqual(true, importPanel.Descendants().Any(element => element.Attribute("Content")?.Value == "{Binding UnrealProjectSync.ImportPrimaryActionText, Mode=OneWay}"));
    AssertEqual(false, importPanel.Descendants().Any(element => (element.Attribute("Content")?.Value ?? string.Empty).Contains("虚幻", StringComparison.Ordinal)));
    AssertEqual(true, publishPanel.Descendants().Any(element => element.Attribute("Content")?.Value == "{Binding UnrealProjectSync.ImportPrimaryActionText, Mode=OneWay}"));
}

static void UnrealSyncSourcePickerIncludesSearchAndSharedMaterials()
{
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    viewModel.RefreshDraftSources([]);

    AssertEqual(5, viewModel.SharedMaterialSources.Count);
    AssertEqual(true, viewModel.SharedMaterialSources.All(item => item.IsSharedMaterial));
    AssertEqual(true, viewModel.SharedMaterialSources.Any(item => item.DisplayName == "通用音效"));
    AssertEqual(true, viewModel.SharedMaterialSources.Any(item => item.DisplayName == "战斗 BGM"));

    var xaml = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    AssertEqual(true, xaml.Descendants().Any(element =>
        element.Name.LocalName == "AutoSuggestBox" &&
        element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "UnrealSyncSourceSearchBox"));
    AssertEqual(true, xaml.Descendants().Any(element =>
        element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "项目通用素材（待支持）"));
    AssertEqual(true, xaml.Descendants().Any(element =>
        element.Name.LocalName == "TextBlock" &&
        element.Attribute("Text")?.Value == "{Binding UnrealProjectSync.SourceGroupTitle, Mode=OneWay}"));
}

static void UnrealSyncSelectionTreeProtectsUnsafeChanges()
{
    var safe = CreateUnrealBridgeChange(UnrealBridgeChangeKind.Updated, isSelected: true);
    var conflict = CreateUnrealBridgeChange(UnrealBridgeChangeKind.Conflict, isSelected: false) with
    {
        StableId = "material:conflict",
        DisplayName = "冲突头像"
    };
    var roots = UnrealSyncSelectionTreeBuilder.FromChanges([safe, conflict]);
    var leaves = roots.SelectMany(root => root.Children).ToArray();

    AssertEqual(true, leaves.Single(item => item.StableId == safe.StableId).IsChecked);
    AssertEqual(false, leaves.Single(item => item.StableId == conflict.StableId).IsChecked);
    AssertEqual(true, leaves.Single(item => item.StableId == conflict.StableId).RequiresAttention);
    AssertEqual(false, leaves.Single(item => item.StableId == conflict.StableId).IsSelectable);
    AssertEqual("暂不支持", leaves.Single(item => item.StableId == conflict.StableId).StatusText);
}

static void UnrealSyncSelectionTreeSupportsTriStateSelection()
{
    var snapshot = CreateUnrealBridgeSnapshot(
        ("material:first", UnrealBridgeModule.BaseMaterials, "素材一", "first"),
        ("material:second", UnrealBridgeModule.BaseMaterials, "素材二", "second"));
    var root = UnrealSyncSelectionTreeBuilder.FromSnapshot(snapshot).Single();

    AssertEqual<bool?>(true, root.IsChecked);
    AssertEqual(true, root.Children.All(child => child.IsSelectable));

    root.Children[0].IsChecked = false;
    AssertEqual<bool?>(null, root.IsChecked);
    AssertSequence(["material:second"], UnrealSyncSelectionTreeBuilder.SelectedStableIds([root]).ToArray());

    root.ToggleGroupSelection();
    AssertEqual(true, root.Children.All(child => child.IsChecked == true));
    AssertEqual<bool?>(true, root.IsChecked);

    root.ToggleGroupSelection();
    AssertEqual(true, root.Children.All(child => child.IsChecked == false));
    AssertEqual<bool?>(false, root.IsChecked);

    var xaml = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var parentCheckBox = xaml.Descendants().Single(element =>
        element.Name.LocalName == "CheckBox" &&
        element.Attribute("IsThreeState")?.Value == "True");
    AssertEqual("{Binding IsChecked, Mode=TwoWay}", parentCheckBox.Attribute("IsChecked")?.Value ?? string.Empty);
    AssertEqual("{Binding IsSelectable}", parentCheckBox.Attribute("IsEnabled")?.Value ?? string.Empty);
    var indeterminateIndicator = xaml.Descendants().Single(element =>
        element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "UnrealSyncIndeterminateIndicator");
    AssertEqual(true, (indeterminateIndicator.Attribute("Visibility")?.Value ?? string.Empty).Contains("IsIndeterminate", StringComparison.Ordinal));
    var checkedIndicator = xaml.Descendants().Single(element =>
        element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "UnrealSyncCheckedIndicator");
    AssertEqual(true, (checkedIndicator.Attribute("Visibility")?.Value ?? string.Empty).Contains("IsGroupChecked", StringComparison.Ordinal));
    var uncheckedIndicator = xaml.Descendants().Single(element =>
        element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "UnrealSyncUncheckedIndicator");
    AssertEqual(true, (uncheckedIndicator.Attribute("Visibility")?.Value ?? string.Empty).Contains("IsGroupUnchecked", StringComparison.Ordinal));
}

static void UnrealImportOperationSummaryTracksLeafSelection()
{
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    var snapshot = CreateUnrealBridgeSnapshot(
        ("material:existing", UnrealBridgeModule.BaseMaterials, "已有图标", "A"),
        ("material:new", UnrealBridgeModule.BaseMaterials, "新增图标", "B"),
        ("voice:new", UnrealBridgeModule.Voices, "新增语音", "C"));
    var roots = UnrealSyncSelectionTreeBuilder.FromSnapshot(snapshot);

    viewModel.SetImportSelectionTree(roots, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "material:existing"
    });

    AssertEqual("已选择 3 / 3 项", viewModel.ImportSelectionCountText);
    AssertEqual("新增 2 项", viewModel.ImportAddedCountText);
    AssertEqual("更新 1 项", viewModel.ImportUpdatedCountText);
    AssertEqual("未选 0 项", viewModel.ImportSkippedCountText);
    AssertEqual("导入所选 3 项", viewModel.ImportPrimaryActionText);
    roots.Single(root => root.Module == UnrealBridgeModule.Voices).Children.Single().IsChecked = false;
    AssertEqual("已选择 2 / 3 项", viewModel.ImportSelectionCountText);
    AssertEqual("新增 1 项", viewModel.ImportAddedCountText);
    AssertEqual("未选 1 项", viewModel.ImportSkippedCountText);
    viewModel.CompleteImportOperation(@"D:\Draft\ALO_Yuki", removedDuplicateCount: 2);
    AssertEqual(Visibility.Visible, viewModel.ImportResultVisibility);
    AssertEqual(true, viewModel.ImportResultMessage.Contains("已清理旧重复素材 2 个", StringComparison.Ordinal));
    AssertEqual(true, viewModel.ImportResultMessage.Contains(@"D:\Draft\ALO_Yuki", StringComparison.Ordinal));
}

static void UnrealImportOperationPanelShowsGuidanceAndPersistentResult()
{
    var xaml = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var panel = xaml.Descendants().Single(element =>
        element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "UnrealImportWorkspacePanel");
    var bindings = string.Join("\n", panel.DescendantsAndSelf().Attributes().Select(attribute => attribute.Value));
    AssertEqual(true, bindings.Contains("ImportOperationTitle", StringComparison.Ordinal));
    AssertEqual(true, bindings.Contains("ImportSelectionCountText", StringComparison.Ordinal));
    AssertEqual(true, bindings.Contains("ImportAddedCountText", StringComparison.Ordinal));
    AssertEqual(true, bindings.Contains("ImportUpdatedCountText", StringComparison.Ordinal));
    AssertEqual(true, bindings.Contains("ImportSkippedCountText", StringComparison.Ordinal));
    AssertEqual(true, bindings.Contains("ImportPrimaryActionText", StringComparison.Ordinal));
    AssertEqual(true, bindings.Contains("ImportResultMessage", StringComparison.Ordinal));
    AssertEqual(true, bindings.Contains("DetectionResultTitle", StringComparison.Ordinal));
    AssertEqual(true, bindings.Contains("DetectionResultSummaryText", StringComparison.Ordinal));
}

static void UnrealBridgeStateIsScopedToCharacterAndProject()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var first = CreateCharacter(root, "Misaka", "御坂美琴");
        var second = CreateCharacter(root, "Kirito", "桐人");
        var service = new UnrealBridgeStateService();
        var firstProject = Path.Combine(root, "First", "First.uproject");
        var secondProject = Path.Combine(root, "Second", "Second.uproject");
        var state = new UnrealBridgeSyncState
        {
            CharacterCode = first.Code,
            UnrealProjectPath = firstProject,
            TemplateCharacterCode = "Origin_Misaka",
            Entries =
            {
                ["character:info"] = new UnrealBridgeSyncStateEntry("tool", "unreal")
            }
        };

        service.Save(first, firstProject, state);

        AssertEqual("Origin_Misaka", service.Load(first, firstProject)?.TemplateCharacterCode);
        AssertEqual<UnrealBridgeSyncState?>(null, service.Load(first, secondProject));
        AssertEqual<UnrealBridgeSyncState?>(null, service.Load(second, firstProject));

        File.WriteAllText(
            service.GetStatePath(first, firstProject),
            JsonSerializer.Serialize(new
            {
                ProtocolVersion = 2,
                CharacterCode = first.Code,
                UnrealProjectPath = Path.GetFullPath(firstProject),
                Entries = new Dictionary<string, object>()
            }));
        AssertEqual<UnrealBridgeSyncState?>(null, service.Load(first, firstProject));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgePartialImportUpdatesOnlySelectedBaselineEntries()
{
    var toolbox = CreateUnrealBridgeSnapshot(
        ("material:selected", UnrealBridgeModule.BaseMaterials, "已选素材", "tool-selected-new"),
        ("material:untouched", UnrealBridgeModule.BaseMaterials, "未选素材", "tool-untouched-old"));
    var unreal = CreateUnrealBridgeSnapshot(
        ("material:selected", UnrealBridgeModule.BaseMaterials, "已选素材", "unreal-selected-new"),
        ("material:untouched", UnrealBridgeModule.BaseMaterials, "未选素材", "unreal-untouched-new"));
    var previous = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["material:untouched"] = new UnrealBridgeSyncStateEntry("tool-untouched-old", "unreal-untouched-old")
        }
    };

    var state = new UnrealBridgeBaselineService().BuildSelected(
        "Misaka",
        @"D:\Project\CrossingVoid.uproject",
        toolbox,
        unreal,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "material:selected" },
        previous);

    AssertEqual("tool-selected-new", state.Entries["material:selected"].ToolboxHash);
    AssertEqual("unreal-selected-new", state.Entries["material:selected"].UnrealHash);
    AssertEqual("tool-untouched-old", state.Entries["material:untouched"].ToolboxHash);
    AssertEqual("unreal-untouched-old", state.Entries["material:untouched"].UnrealHash);
}

static void UnrealBridgeBackupPolicyProtectsRiskyChanges()
{
    var added = CreateUnrealBridgeChange(UnrealBridgeChangeKind.Added, isSelected: true);
    var updated = CreateUnrealBridgeChange(UnrealBridgeChangeKind.Updated, isSelected: true);
    var deleted = CreateUnrealBridgeChange(UnrealBridgeChangeKind.DeleteCandidate, isSelected: true);
    var unselectedConflict = CreateUnrealBridgeChange(UnrealBridgeChangeKind.Conflict, isSelected: false);

    AssertEqual(false, UnrealBridgeBackupPolicy.ShouldBackupByDefault([added]));
    AssertEqual(true, UnrealBridgeBackupPolicy.ShouldBackupByDefault([added, updated]));
    AssertEqual(true, UnrealBridgeBackupPolicy.ShouldBackupByDefault([deleted]));
    AssertEqual(false, UnrealBridgeBackupPolicy.ShouldBackupByDefault([unselectedConflict]));
}

static void UnrealBridgeBackupUsesEngineAutomationTool()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var editorPath = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        var runUatPath = Path.Combine(root, "Engine", "Build", "BatchFiles", "RunUAT.bat");
        var projectPath = Path.Combine(root, "Project", "CrossingVoid.uproject");
        var backupPath = Path.Combine(root, "Backups", "CrossingVoid.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(runUatPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(editorPath, string.Empty);
        File.WriteAllText(runUatPath, string.Empty);
        File.WriteAllText(projectPath, "{}");

        var startInfo = new UnrealBridgeBackupService().BuildZipProjectStartInfo(editorPath, projectPath, backupPath);

        AssertEqual(runUatPath, startInfo.FileName);
        AssertSequence(
            ["ZipProjectUp", $"-project={projectPath}", $"-install={backupPath}"],
            startInfo.ArgumentList.ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeToolboxSnapshotRejectsDraftCharacter()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var exception = default(InvalidOperationException);
        try
        {
            _ = new UnrealBridgeToolboxSnapshotService().Build(character);
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        AssertEqual(true, exception?.Message.Contains("已完成", StringComparison.Ordinal) == true);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeToolboxSnapshotCoversAllModules()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴") with { IsCompleted = true };
        new CharacterInfoService().Save(character, new CharacterInfoData
        {
            Code = character.Code,
            Name = character.Name,
            Description = "测试角色"
        });

        var imagePath = Path.Combine(root, "source.png");
        WriteSolidImage(imagePath, Color.Red, 125, 125);
        new BaseMaterialService().ReplaceWithImages(character, BaseMaterialKind.Icon, [imagePath]);

        var skills = new CharacterSkillsData();
        var skill = CharacterSkillsService.CreateEntry();
        skill.PositionName = "前排攻击";
        skill.TrueName = "电击";
        skills.FirstSkill.Add(skill);
        new CharacterSkillsService().Save(character, skills);

        var sequenceService = new SequenceFrameService();
        var clickAction = SequenceFrameService.BuildActions(skills).Single(action => action.Code == "Click");
        sequenceService.ImportFrames(character, clickAction, [imagePath]);

        var buffService = new BuffService();
        var buffData = new BuffData();
        var buff = BuffService.CreateBuff();
        buff.UserCode = "Electric";
        buff.Name = "电击";
        buffData.Buffs.Add(buff);
        buffService.Save(character, buffData);

        var wavePath = Path.Combine(root, "source.wav");
        WriteWaveFile(wavePath, 144, 32);
        new VoiceMaterialService().Import(character, VoiceMaterialKind.Formation, wavePath);

        var snapshot = new UnrealBridgeToolboxSnapshotService().Build(character);
        var modules = snapshot.Items.Select(item => item.Module).Distinct().OrderBy(value => value).ToArray();

        AssertSequence(
            Enum.GetValues<UnrealBridgeModule>().OrderBy(value => value).ToArray(),
            modules);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeToolboxSnapshotHashesAreItemScoped()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴") with { IsCompleted = true };
        new CharacterInfoService().Save(character, new CharacterInfoData
        {
            Code = character.Code,
            Name = character.Name
        });
        var sourcePath = Path.Combine(root, "source.png");
        WriteSolidImage(sourcePath, Color.Red, 125, 125);
        var materialService = new BaseMaterialService();
        materialService.ReplaceWithImages(character, BaseMaterialKind.Icon, [sourcePath]);
        var snapshotService = new UnrealBridgeToolboxSnapshotService();
        var first = snapshotService.Build(character);
        var targetPath = materialService.LoadSections(character)
            .Single(section => section.Spec.Kind == BaseMaterialKind.Icon)
            .Items.Single().FilePath;

        WriteSolidImage(targetPath, Color.Blue, 125, 125);
        var second = snapshotService.Build(character);
        var firstById = first.Items.ToDictionary(item => item.StableId, StringComparer.OrdinalIgnoreCase);
        var unexpectedIds = second.Items
            .Where(item => !firstById.ContainsKey(item.StableId))
            .Select(item => item.StableId)
            .ToArray();
        AssertEqual(string.Empty, string.Join(",", unexpectedIds));
        var changedIds = second.Items
            .Where(item => firstById[item.StableId].ContentHash != item.ContentHash)
            .Select(item => item.StableId)
            .ToArray();

        AssertEqual(1, changedIds.Length);
        AssertEqual(true, changedIds[0].StartsWith("material:", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CharacterAntiChangesSynchronizationSnapshot()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "ALO_Yuki", "优纪 ALO");
        var infoService = new CharacterInfoService();
        var data = infoService.Load(character);
        data.Anti = false;
        infoService.Save(character, data);
        var snapshotService = new UnrealBridgeToolboxSnapshotService();
        var physical = snapshotService.BuildForSynchronization(character)
            .Items.Single(item => item.StableId == "character:info");

        data.Anti = true;
        infoService.Save(character, data);
        var energy = snapshotService.BuildForSynchronization(character)
            .Items.Single(item => item.StableId == "character:info");

        AssertEqual(false, string.Equals(physical.ContentHash, energy.ContentHash, StringComparison.Ordinal));
        AssertEqual(true, energy.PayloadJson.Contains("\"anti\":\"true\"", StringComparison.Ordinal));

        var physicalPreview = new UnrealProjectSyncCharacterInfoPreview(
            "Item_ALO_Yuki",
            "/Game/ITems/CharItemS/Item_ALO_Yuki.Item_ALO_Yuki",
            true,
            string.Empty,
            "优纪 ALO",
            string.Empty,
            [],
            [],
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false);
        UnrealProjectSyncCharacterCandidate Candidate(UnrealProjectSyncCharacterInfoPreview info) => new(
            "ALO_Yuki",
            "优纪 ALO",
            string.Empty,
            string.Empty,
            0,
            0,
            info,
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [],
            hasLatestData: true);
        var semanticService = new UnrealBridgeSemanticSnapshotService();
        var unrealPhysical = semanticService.Build(Candidate(physicalPreview)).Items.Single(item => item.StableId == "character:info");
        var unrealEnergy = semanticService.Build(Candidate(physicalPreview with { Anti = true })).Items.Single(item => item.StableId == "character:info");
        AssertEqual(false, string.Equals(unrealPhysical.ContentHash, unrealEnergy.ContentHash, StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SequenceFrameSyncIdentitySurvivesReorderAndChangesOnCopy()
{
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, _) =>
    {
        var imported = service.ImportFrames(character, action, sourcePaths);
        AssertEqual(2, imported.Count);
        AssertEqual(false, string.IsNullOrWhiteSpace(imported[0].SyncId));
        AssertEqual(false, string.IsNullOrWhiteSpace(imported[1].SyncId));
        AssertEqual(false, string.Equals(imported[0].SyncId, imported[1].SyncId, StringComparison.OrdinalIgnoreCase));

        var firstId = imported[0].SyncId;
        var secondId = imported[1].SyncId;
        var reordered = service.ReorderFrames(character, action, [imported[1], imported[0]]);
        AssertSequence([secondId, firstId], reordered.Select(frame => frame.SyncId).ToArray());

        var duplicated = service.DuplicateFrame(character, action, reordered[0]);
        AssertEqual(secondId, duplicated[0].SyncId);
        AssertEqual(false, string.IsNullOrWhiteSpace(duplicated[1].SyncId));
        AssertEqual(false, string.Equals(secondId, duplicated[1].SyncId, StringComparison.OrdinalIgnoreCase));
    });
}

static void BuffSyncIdentitySurvivesRenameAndReorder()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var service = new BuffService();
        var first = BuffService.CreateBuff();
        first.Name = "电击";
        var second = BuffService.CreateBuff();
        second.Name = "护盾";
        var data = new BuffData();
        data.Buffs.Add(first);
        data.Buffs.Add(second);
        service.Save(character, data);

        var loaded = service.Load(character);
        var electricId = loaded.Buffs.Single(buff => buff.Name == "电击").SyncId;
        var shieldId = loaded.Buffs.Single(buff => buff.Name == "护盾").SyncId;
        AssertEqual(false, string.IsNullOrWhiteSpace(electricId));
        AssertEqual(false, string.IsNullOrWhiteSpace(shieldId));

        var electric = loaded.Buffs.Single(buff => buff.Name == "电击");
        var shield = loaded.Buffs.Single(buff => buff.Name == "护盾");
        electric.UserCode = "ElectricNormalized";
        loaded.Buffs.Clear();
        loaded.Buffs.Add(shield);
        loaded.Buffs.Add(electric);
        service.Save(character, loaded);

        var reloaded = service.Load(character);
        AssertEqual(shieldId, reloaded.Buffs.Single(buff => buff.Name == "护盾").SyncId);
        AssertEqual(electricId, reloaded.Buffs.Single(buff => buff.Name == "电击").SyncId);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SkillSyncIdentitySurvivesSaveRoundTrip()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var service = new CharacterSkillsService();
        var skills = service.Load(character);
        var skill = skills.FirstSkill[0];
        AssertEqual(false, string.IsNullOrWhiteSpace(skill.SyncId));
        skill.TrueName = "电击";
        service.Save(character, skills);

        var reloaded = service.Load(character);
        AssertEqual(skill.SyncId, reloaded.FirstSkill[0].SyncId);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeStatePreservesOriginIdentity()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var projectPath = Path.Combine(root, "CrossingVoid.uproject");
        File.WriteAllText(projectPath, "{}");
        var state = new UnrealBridgeSyncState
        {
            Entries =
            {
                ["material-sync-id"] = new UnrealBridgeSyncStateEntry(
                    "toolbox-hash",
                    "unreal-hash",
                    UnrealObjectPath: "/Game/GameActor2D/Origin_Misaka/Material/T_Misaka",
                    UnrealIdentity: "package-guid-123",
                    ToolboxRelativePath: "Icon/Misaka-Icon-1.png",
                    NormalizedName: "Misaka-Icon-1")
            }
        };

        var service = new UnrealBridgeStateService();
        service.Save(character, projectPath, state);
        var loaded = service.Load(character, projectPath)
            ?? throw new InvalidOperationException("同步状态没有成功读取。");
        var entry = loaded.Entries["material-sync-id"];

        AssertEqual("/Game/GameActor2D/Origin_Misaka/Material/T_Misaka", entry.UnrealObjectPath);
        AssertEqual("package-guid-123", entry.UnrealIdentity);
        AssertEqual("Icon/Misaka-Icon-1.png", entry.ToolboxRelativePath);
        AssertEqual("Misaka-Icon-1", entry.NormalizedName);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeFileIdentitySurvivesMoveAndRename()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var sourceFolder = Path.Combine(root, "OtherImage");
        var targetFolder = Path.Combine(root, "Icon");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(targetFolder);
        var sourcePath = Path.Combine(sourceFolder, "Misaka-OtherImage-1-old.png");
        var targetPath = Path.Combine(targetFolder, "Misaka-Icon-1.png");
        WriteSolidImage(sourcePath, Color.Red, 125, 125);
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sourcePath)));
        var service = new UnrealBridgeToolboxIdentityService();
        var first = service.Reconcile(
            character,
            [new UnrealBridgeToolboxFileCandidate(UnrealBridgeModule.BaseMaterials, sourcePath, contentHash)]);
        var syncId = first[sourcePath];

        File.Move(sourcePath, targetPath);
        service.RemapPath(character, sourcePath, targetPath);
        var second = service.Reconcile(
            character,
            [new UnrealBridgeToolboxFileCandidate(UnrealBridgeModule.BaseMaterials, targetPath, contentHash)]);

        AssertEqual(syncId, second[targetPath]);
        AssertEqual(false, string.IsNullOrWhiteSpace(syncId));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeSnapshotUsesFileIdentityAcrossReclassification()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴") with { IsCompleted = true };
        var sourcePath = Path.Combine(root, "source.png");
        WriteSolidImage(sourcePath, Color.Red, 200, 209);
        var materialService = new BaseMaterialService();
        var pending = materialService.ImportAndCrop(character, BaseMaterialKind.OtherImage, sourcePath);
        var snapshotService = new UnrealBridgeToolboxSnapshotService();
        var before = snapshotService.Build(character).Items.Single(item =>
            item.Module == UnrealBridgeModule.BaseMaterials &&
            item.DisplayName.StartsWith("其他图片", StringComparison.Ordinal));

        var iconFolder = Path.Combine(root, "AssetMaterial", "Icon");
        Directory.CreateDirectory(iconFolder);
        var iconPath = Path.Combine(iconFolder, "Misaka-Icon-1.png");
        File.Move(pending.FilePath, iconPath);
        new UnrealBridgeToolboxIdentityService().RemapPath(character, pending.FilePath, iconPath);

        var after = snapshotService.Build(character).Items.Single(item =>
            item.Module == UnrealBridgeModule.BaseMaterials &&
            item.DisplayName.StartsWith("头像", StringComparison.Ordinal));

        AssertEqual(before.StableId, after.StableId);
        AssertEqual(false, string.Equals(before.DisplayName, after.DisplayName, StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void DuplicateVoicesKeepSyncIdentityAfterClassification()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴") with { IsCompleted = true };
        var firstSource = Path.Combine(root, "first.wav");
        var secondSource = Path.Combine(root, "second.wav");
        WriteWaveFile(firstSource, 1);
        File.Copy(firstSource, secondSource);
        var voiceService = new VoiceMaterialService();
        voiceService.Import(character, VoiceMaterialKind.Other, firstSource);
        voiceService.Import(character, VoiceMaterialKind.Other, secondSource);
        var snapshotService = new UnrealBridgeToolboxSnapshotService();
        var beforeIds = snapshotService.Build(character).Items
            .Where(item => item.Module == UnrealBridgeModule.Voices)
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = voiceService.LoadSections(character)
            .Single(section => section.Spec.Kind == VoiceMaterialKind.Other)
            .Items[0];

        voiceService.MovePendingVoices(character, [pending], VoiceMaterialKind.Skill1);
        var afterIds = snapshotService.Build(character).Items
            .Where(item => item.Module == UnrealBridgeModule.Voices)
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AssertEqual(2, beforeIds.Count);
        AssertEqual(true, beforeIds.SetEquals(afterIds));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeScanRestoresSyncIdFromOriginIdentity()
{
    var manifest = new UnrealBridgeScanManifest
    {
        CharacterCode = "Misaka",
        Items =
        {
            new UnrealBridgeScanItem
            {
                Module = UnrealBridgeModule.BaseMaterials,
                DisplayName = "旧头像",
                ContentHash = "unreal-hash",
                ObjectPath = "/Game/GameActor2D/Origin_Misaka/Material/T_Old.T_Old",
                OriginIdentity = "package-guid-123",
                AssetClass = "Texture2D",
                NormalizedName = "T_Old"
            }
        }
    };
    var state = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["material-sync-id"] = new UnrealBridgeSyncStateEntry(
                "toolbox-hash",
                "previous-unreal-hash",
                UnrealObjectPath: "/Game/GameActor2D/Origin_Misaka/Material/T_Old.T_Old",
                UnrealIdentity: "package-guid-123")
        }
    };

    var snapshot = new UnrealBridgeUnrealSnapshotService().BuildSnapshot(manifest, state);
    var item = snapshot.Items.Single();

    AssertEqual("material-sync-id", item.StableId);
    AssertEqual("package-guid-123", item.OriginIdentity);
    AssertEqual("/Game/GameActor2D/Origin_Misaka/Material/T_Old.T_Old", item.SourceObjectPath);
}

static void UnrealBridgeScanUsesVersionedScriptAndCommandProcess()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var editorPath = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        var commandPath = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe");
        var projectPath = Path.Combine(root, "CrossingVoid.uproject");
        var outputPath = Path.Combine(root, "scan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
        File.WriteAllText(editorPath, string.Empty);
        File.WriteAllText(commandPath, string.Empty);
        File.WriteAllText(projectPath, "{}");

        var service = new UnrealBridgeUnrealSnapshotService();
        var startInfo = service.BuildScanProcessStartInfo(
            editorPath,
            projectPath,
            "Misaka",
            outputPath);

        AssertEqual(commandPath, startInfo.FileName);
        AssertEqual(true, startInfo.Arguments.Contains("-run=pythonscript", StringComparison.OrdinalIgnoreCase));
        AssertEqual(true, startInfo.Arguments.Contains("scan_unreal_character.py", StringComparison.OrdinalIgnoreCase));
        AssertEqual("Misaka", startInfo.Environment["ZD_BRIDGE_CHARACTER_CODE"]);
        AssertEqual(outputPath, startInfo.Environment["ZD_BRIDGE_SCAN_OUTPUT"]);
        AssertEqual(true, File.Exists(service.GetScanScriptPath()));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeScannerUsesAssetRegistryReferences()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "scan_unreal_character.py");
    var source = File.ReadAllText(path);

    AssertEqual(true, source.Contains("get_asset_registry", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("get_dependencies", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("get_referencers", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("originIdentity", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("protocolVersion", StringComparison.Ordinal));
}

static void UnrealBridgeDiffRecognizesNormalizationRename()
{
    var toolbox = new UnrealBridgeSnapshot(
        "Misaka",
        [
            new UnrealBridgeSnapshotItem(
                "material-sync-id",
                "module:BaseMaterials",
                UnrealBridgeModule.BaseMaterials,
                "头像 #1",
                "new-toolbox-hash",
                "{}",
                @"D:\Project\Misaka\AssetMaterial\Icon\Misaka-Icon-1.png",
                ToolboxRelativePath: "AssetMaterial/Icon/Misaka-Icon-1.png",
                NormalizedName: "Misaka-Icon-1")
        ]);
    var unreal = new UnrealBridgeSnapshot(
        "Misaka",
        [
            new UnrealBridgeSnapshotItem(
                "material-sync-id",
                "module:BaseMaterials",
                UnrealBridgeModule.BaseMaterials,
                "旧图片",
                "unreal-hash",
                "{}",
                string.Empty,
                SourceObjectPath: "/Game/GameActor2D/Origin_Misaka/Material/T_Old.T_Old",
                OriginIdentity: "package-guid-123",
                NormalizedName: "T_Old")
        ]);
    var baseline = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["material-sync-id"] = new UnrealBridgeSyncStateEntry(
                "old-toolbox-hash",
                "unreal-hash",
                UnrealObjectPath: "/Game/GameActor2D/Origin_Misaka/Material/T_Old.T_Old",
                UnrealIdentity: "package-guid-123",
                ToolboxRelativePath: "AssetMaterial/OtherImage/Misaka-OtherImage-1-old.png",
                NormalizedName: "T_Old")
        }
    };

    var change = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, baseline)
        .Single();

    AssertEqual(UnrealBridgeChangeKind.Renamed, change.Kind);
    AssertEqual(true, change.IsSelected);
}

static void UnrealBridgeDiffUsesEndpointBaselinesForUnchangedItems()
{
    var toolbox = CreateUnrealBridgeSnapshot(
        ("asset-1", UnrealBridgeModule.BaseMaterials, "头像", "png-hash"));
    var unreal = CreateUnrealBridgeSnapshot(
        ("asset-1", UnrealBridgeModule.BaseMaterials, "头像", "uasset-hash"));
    var baseline = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["asset-1"] = new UnrealBridgeSyncStateEntry("png-hash", "uasset-hash")
        }
    };

    var change = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, baseline)
        .Single();

    AssertEqual(UnrealBridgeChangeKind.Unchanged, change.Kind);
    AssertEqual(false, change.IsSelected);
}

static void UnrealBridgeDiffMigratesMatchedItemsWithoutBaseline()
{
    var toolbox = new UnrealBridgeSnapshot("Misaka", [new UnrealBridgeSnapshotItem(
        "material:1", "module:BaseMaterials", UnrealBridgeModule.BaseMaterials, "头像", "toolbox-source-hash", "{}",
        @"D:\Project\Misaka\AssetMaterial\Icon\Misaka-Icon-1.png",
        ToolboxRelativePath: "AssetMaterial/Icon/Misaka-Icon-1.png", NormalizedName: "Misaka-Icon-1")]);
    var unreal = new UnrealBridgeSnapshot("Misaka", [new UnrealBridgeSnapshotItem(
        "material:1", "module:BaseMaterials", UnrealBridgeModule.BaseMaterials, "头像", "unreal-package-hash", "{}", string.Empty,
        SourceObjectPath: "/Game/AssetMaterial/ImageS/CharaterS/Misaka/Misaka-Icon-1.Misaka-Icon-1",
        NormalizedName: "Misaka-Icon-1")]);

    var change = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, baseline: null)
        .Single();

    AssertEqual(UnrealBridgeChangeKind.Unchanged, change.Kind);
    AssertEqual(false, change.IsSelected);

    var state = new UnrealBridgeBaselineService().BuildFromChanges(
        "Misaka",
        @"D:\Project\Game.uproject",
        [change]);
    AssertEqual("toolbox-source-hash", state.Entries["material:1"].ToolboxHash);
    AssertEqual("unreal-package-hash", state.Entries["material:1"].UnrealHash);
}

static void UnrealBridgeDiffPairsSanitizedVoiceNames()
{
    var toolboxItem = new UnrealBridgeSnapshotItem(
        "voice:toolbox-id",
        "module:Voices",
        UnrealBridgeModule.Voices,
        "编队语音 #1",
        "source-wave-hash",
        "{\"kind\":\"Formation\"}",
        @"D:\Project\Misaka\Sound\Formation\Misaka-编队 (1).wav",
        ToolboxRelativePath: "Sound/Formation/Misaka-编队 (1).wav",
        NormalizedName: "Misaka-编队 (1)");
    var unrealItem = new UnrealBridgeSnapshotItem(
        "voice:unreal-object-id",
        "module:Voices",
        UnrealBridgeModule.Voices,
        "Misaka-编队__1",
        "unreal-preview-hash",
        "{}",
        string.Empty,
        SourceObjectPath: "/Game/GameActor2D/Misaka/Sound/Formation/Misaka-编队__1.Misaka-编队__1",
        OriginIdentity: "package-guid",
        NormalizedName: "Misaka-编队__1");

    var changes = new UnrealBridgeDiffService().Compare(
        new UnrealBridgeSnapshot("Misaka", [toolboxItem]),
        new UnrealBridgeSnapshot("Misaka", [unrealItem]),
        UnrealBridgeDirection.PublishToUnreal,
        baseline: null);

    var change = changes.Single();
    AssertEqual("voice:toolbox-id", change.StableId);
    AssertEqual(UnrealBridgeChangeKind.Unchanged, change.Kind);
    AssertEqual("package-guid", change.UnrealItem?.OriginIdentity);
}

static void UnrealBridgeDiffKeepsCanonicalVoiceNameUnchanged()
{
    var toolboxItem = new UnrealBridgeSnapshotItem(
        "voice:canonical",
        "module:Voices",
        UnrealBridgeModule.Voices,
        "一技能语音 #1",
        "source-wave-hash",
        "{\"kind\":\"Skill1\"}",
        @"D:\Project\Misaka\Sound\Skill1\Misaka-Skill1-1.wav",
        ToolboxRelativePath: "Sound/Skill1/Misaka-Skill1-1.wav",
        NormalizedName: "Misaka-Skill1-1");
    var unrealItem = new UnrealBridgeSnapshotItem(
        "voice:canonical",
        "module:Voices",
        UnrealBridgeModule.Voices,
        "Misaka-Skill1-1",
        "target-wave-hash",
        "{}",
        string.Empty,
        SourceObjectPath: "/Game/GameActor2D/Misaka/Sound/Skill1/Misaka-Skill1-1.Misaka-Skill1-1",
        OriginIdentity: "package-guid",
        NormalizedName: "Misaka-Skill1-1");
    var baseline = new UnrealBridgeSyncState
    {
        HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
        Entries =
        {
            [toolboxItem.StableId] = new UnrealBridgeSyncStateEntry(
                toolboxItem.ContentHash,
                unrealItem.ContentHash,
                UnrealObjectPath: unrealItem.SourceObjectPath,
                UnrealIdentity: unrealItem.OriginIdentity,
                ToolboxRelativePath: toolboxItem.ToolboxRelativePath,
                NormalizedName: toolboxItem.NormalizedName)
        }
    };

    var change = new UnrealBridgeDiffService()
        .Compare(
            new UnrealBridgeSnapshot("Misaka", [toolboxItem]),
            new UnrealBridgeSnapshot("Misaka", [unrealItem]),
            UnrealBridgeDirection.PublishToUnreal,
            baseline)
        .Single();

    AssertEqual(UnrealBridgeChangeKind.Unchanged, change.Kind);
    AssertEqual(false, change.IsSelected);
}

static void UnrealBridgeDiffDetectsSourceFileChangeAfterMigration()
{
    var toolbox = CreateUnrealBridgeSnapshot(
        ("asset-1", UnrealBridgeModule.BaseMaterials, "头像", "source-new"));
    var unreal = CreateUnrealBridgeSnapshot(
        ("asset-1", UnrealBridgeModule.BaseMaterials, "头像", "target-same"));
    var baseline = new UnrealBridgeSyncState
    {
        HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
        Entries =
        {
            ["asset-1"] = new UnrealBridgeSyncStateEntry("source-old", "target-same")
        }
    };

    var change = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, baseline)
        .Single();

    AssertEqual(UnrealBridgeChangeKind.Updated, change.Kind);
    AssertEqual(true, change.IsSelected);
}

static void UnrealBridgeImportCreatesDraftAndWritesReadableModules()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "Misaka",
            "御坂美琴",
            string.Empty,
            string.Empty,
            0,
            0,
            new UnrealProjectSyncCharacterInfoPreview(
                "MisakaItem",
                "/Game/ITems/CharItemS/MisakaItem.MisakaItem",
                true,
                string.Empty,
                "御坂美琴",
                "电击使",
                ["Misaka", "御坂美琴"],
                ["电磁感应"],
                2,
                3,
                720,
                1000,
                500,
                200,
                180,
                15,
                150,
                100,
                true),
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [],
            hasLatestData: true);

        var result = new UnrealBridgeDraftImportService().Import(root, candidate);

        AssertEqual(true, result.CreatedNew);
        AssertEqual(false, result.Character.IsCompleted);
        AssertEqual(true, new CharacterInfoService().Load(result.Character).Anti);
        AssertEqual(Path.Combine(root, "Draft", "Misaka"), result.Character.FolderPath);
        var info = new CharacterInfoService().Load(result.Character);
        AssertEqual("御坂美琴", info.Name);
        AssertEqual("电击使", info.Description);
        AssertEqual(720, info.Speed);
        AssertEqual(1, result.ImportedModules.Count(module => module == UnrealBridgeModule.CharacterInfo));
        var draftSnapshot = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(result.Character);
        AssertEqual("Misaka", draftSnapshot.CharacterCode);
        AssertEqual(true, draftSnapshot.Items.Any(item => item.StableId == "character:info"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeImportUsesSelectedLeafItems()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var firstPath = Path.Combine(root, "first.png");
        var secondPath = Path.Combine(root, "second.png");
        WriteImage(firstPath);
        WriteImage(secondPath);
        var first = new UnrealProjectSyncExportAssetView(
            "Selective-OtherImage-1-first", "Texture2D", "/Game/Selective/first", "/Game/Selective/first.first", "/Game/Selective", firstPath);
        var second = new UnrealProjectSyncExportAssetView(
            "Selective-OtherImage-2-second", "Texture2D", "/Game/Selective/second", "/Game/Selective/second.second", "/Game/Selective", secondPath);
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "Selective",
            "选择性导入",
            string.Empty,
            string.Empty,
            2,
            0,
            new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [new UnrealProjectSyncMaterialBucket("其他图片", "OtherImage", 2, [first, second])],
            hasLatestData: true);
        var secondStableId = $"material:{UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(second.ObjectPath)}";

        var result = new UnrealBridgeDraftImportService().Import(root, candidate, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            secondStableId
        });

        var otherImages = new BaseMaterialService().LoadSections(result.Character)
            .Single(section => section.Spec.Kind == BaseMaterialKind.OtherImage)
            .Items;
        AssertEqual(1, otherImages.Count);
        var toolboxSnapshot = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(result.Character);
        AssertEqual(true, toolboxSnapshot.Items.Any(item => item.StableId == secondStableId));
        AssertEqual(false, toolboxSnapshot.Items.Any(item => item.StableId ==
            $"material:{UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(first.ObjectPath)}"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeFailedImportRemovesNewDraft()
{
    var root = CreateTemporaryTestFolder();
    var sourcePath = Path.Combine(root, "locked-source.png");
    WriteImage(sourcePath);
    try
    {
        using var sourceLock = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var candidate = CreateUnrealImportFailureCandidate("ImportFailure", "导入失败测试", sourcePath, includeCharacterInfo: false);
        var threw = false;
        try
        {
            _ = new UnrealBridgeDraftImportService().Import(root, candidate);
        }
        catch (IOException)
        {
            threw = true;
        }

        AssertEqual(true, threw);
        AssertEqual(false, Directory.Exists(Path.Combine(root, "Draft", "ImportFailure")));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeFailedImportRestoresExistingDraft()
{
    var root = CreateTemporaryTestFolder();
    var sourcePath = Path.Combine(root, "locked-source.png");
    WriteImage(sourcePath);
    try
    {
        var workspace = new CharacterWorkspaceService();
        var character = workspace.EnsureCharacterByCode(root, "ExistingDraft", "旧角色名").Character;
        new CharacterInfoService().Save(character, new CharacterInfoData
        {
            Code = character.Code,
            Name = "旧角色名",
            Description = "导入前内容"
        });
        using var sourceLock = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var candidate = CreateUnrealImportFailureCandidate("ExistingDraft", "新角色名", sourcePath, includeCharacterInfo: true);
        var threw = false;
        try
        {
            _ = new UnrealBridgeDraftImportService().Import(root, candidate);
        }
        catch (IOException)
        {
            threw = true;
        }

        AssertEqual(true, threw);
        var restored = workspace.EnsureCharacterByCode(root, "ExistingDraft", "旧角色名").Character;
        var info = new CharacterInfoService().Load(restored);
        AssertEqual("旧角色名", info.Name);
        AssertEqual("导入前内容", info.Description);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static UnrealProjectSyncCharacterCandidate CreateUnrealImportFailureCandidate(
    string code,
    string displayName,
    string sourcePath,
    bool includeCharacterInfo)
{
    var asset = new UnrealProjectSyncExportAssetView(
        $"{code}-OtherImage-1",
        "Texture2D",
        $"/Game/AssetMaterial/ImageS/CharaterS/{code}/{code}-OtherImage-1",
        $"/Game/AssetMaterial/ImageS/CharaterS/{code}/{code}-OtherImage-1.{code}-OtherImage-1",
        "/Game/AssetMaterial/ImageS/CharaterS",
        sourcePath);
    var characterInfo = includeCharacterInfo
        ? new UnrealProjectSyncCharacterInfoPreview(
            $"Item_{code}",
            $"/Game/ITems/CharItemS/Item_{code}.Item_{code}",
            true,
            string.Empty,
            displayName,
            "导入后的内容",
            [],
            [],
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0)
        : new UnrealProjectSyncCharacterInfoPreview(
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            [],
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
    return new UnrealProjectSyncCharacterCandidate(
        code,
        displayName,
        string.Empty,
        string.Empty,
        1,
        0,
        characterInfo,
        CreateEmptyUnrealSkillsPreview(),
        new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
        new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
        [new UnrealProjectSyncMaterialBucket("其他图片", "OtherImage", 1, [asset])],
        hasLatestData: true);
}

static void UnrealBridgeRepeatedImportReplacesVoiceCategory()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var workspace = new CharacterWorkspaceService();
        var character = workspace.EnsureCharacterByCode(root, "VoiceImport", "语音导入测试").Character;
        var oldVoicePath = Path.Combine(root, "old.wav");
        var unrealVoicePath = Path.Combine(root, "unreal.wav");
        WriteWaveFile(oldVoicePath, 2);
        WriteWaveFile(unrealVoicePath, 6);
        _ = new VoiceMaterialService().ImportMany(character, VoiceMaterialKind.Skill1, [oldVoicePath]);
        var asset = new UnrealProjectSyncExportAssetView(
            "VoiceImport_SK1_01",
            "SoundWave",
            "/Game/GameActor2D/VoiceImport/Sound/SK1/VoiceImport_SK1_01",
            "/Game/GameActor2D/VoiceImport/Sound/SK1/VoiceImport_SK1_01.VoiceImport_SK1_01",
            "/Game/GameActor2D",
            unrealVoicePath);
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "VoiceImport",
            "语音导入测试",
            string.Empty,
            string.Empty,
            0,
            0,
            new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [],
            hasLatestData: true,
            voiceBuckets: [new UnrealProjectSyncVoiceBucket("一技能语音", "Skill1", 1, [asset])]);

        _ = new UnrealBridgeDraftImportService().Import(root, candidate);

        var section = new VoiceMaterialService().LoadSections(character)
            .Single(item => item.Spec.Kind == VoiceMaterialKind.Skill1);
        AssertEqual(1, section.Items.Count);
        AssertEqual(new FileInfo(unrealVoicePath).Length, new FileInfo(section.Items[0].FilePath).Length);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeRepeatedImportUpdatesMaterialInPlace()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var sourcePath = Path.Combine(root, "skill-icon.png");
        WriteColoredImage(sourcePath, Color.Red);
        var candidate = CreateMaterialImportCandidate("RepeatImage", "重复图片导入", "SkillIcon", sourcePath);
        var importer = new UnrealBridgeDraftImportService();

        var first = importer.Import(root, candidate);
        WriteColoredImage(sourcePath, Color.Blue);
        var second = importer.Import(root, candidate);

        var items = new BaseMaterialService().LoadSections(second.Character)
            .Single(section => section.Spec.Kind == BaseMaterialKind.SkillIcon)
            .Items;
        AssertEqual(1, items.Count);
        using var imported = new Bitmap(items[0].FilePath);
        AssertEqual(Color.Blue.ToArgb(), imported.GetPixel(imported.Width / 2, imported.Height / 2).ToArgb());
        AssertEqual(first.Character.FolderPath, second.Character.FolderPath);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeImportCleansLegacyMaterialDuplicates()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var sourcePath = Path.Combine(root, "skill-icon.png");
        WriteColoredImage(sourcePath, Color.Gold);
        var candidate = CreateMaterialImportCandidate("LegacyRepeat", "旧重复图片", "SkillIcon", sourcePath);
        var importer = new UnrealBridgeDraftImportService();
        var first = importer.Import(root, candidate);
        var materialService = new BaseMaterialService();
        var identityService = new UnrealBridgeToolboxIdentityService();
        var stableId = UnrealBridgeSemanticSnapshotService.CreateOriginIdentity(
            candidate.MaterialBuckets.Single().Assets.Single().ObjectPath);
        for (var index = 0; index < 2; index++)
        {
            var duplicate = materialService.ImportAndCrop(first.Character, BaseMaterialKind.SkillIcon, sourcePath);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(duplicate.FilePath)));
            identityService.Assign(
                first.Character,
                new UnrealBridgeToolboxFileCandidate(UnrealBridgeModule.BaseMaterials, duplicate.FilePath, hash),
                stableId);
        }

        var result = importer.Import(root, candidate);
        var items = materialService.LoadSections(result.Character)
            .Single(section => section.Spec.Kind == BaseMaterialKind.SkillIcon)
            .Items;
        AssertEqual(1, items.Count);
        var snapshot = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(result.Character);
        AssertEqual(
            $"material:{stableId}",
            snapshot.Items.Single(item => item.Module == UnrealBridgeModule.BaseMaterials).StableId);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static UnrealProjectSyncCharacterCandidate CreateMaterialImportCandidate(
    string code,
    string displayName,
    string kind,
    string sourcePath)
{
    var asset = new UnrealProjectSyncExportAssetView(
        $"{code}-{kind}-1",
        "Texture2D",
        $"/Game/AssetMaterial/ImageS/CharaterS/{code}/{code}-{kind}-1",
        $"/Game/AssetMaterial/ImageS/CharaterS/{code}/{code}-{kind}-1.{code}-{kind}-1",
        "/Game/AssetMaterial/ImageS/CharaterS",
        sourcePath);
    return new UnrealProjectSyncCharacterCandidate(
        code,
        displayName,
        string.Empty,
        string.Empty,
        1,
        0,
        new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        CreateEmptyUnrealSkillsPreview(),
        new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
        new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
        [new UnrealProjectSyncMaterialBucket(kind, kind, 1, [asset])],
        hasLatestData: true);
}

static void UnrealBridgeFileIdentityAdoptsImportedStableId()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴");
        var filePath = Path.Combine(character.FolderPath, "AssetMaterial", "OtherImage", "Misaka-OtherImage-1-old.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        WriteImage(filePath);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(filePath)));
        var service = new UnrealBridgeToolboxIdentityService();

        service.Assign(
            character,
            new UnrealBridgeToolboxFileCandidate(UnrealBridgeModule.BaseMaterials, filePath, hash),
            "unreal-material-identity");
        var identities = service.Reconcile(
            character,
            [new UnrealBridgeToolboxFileCandidate(UnrealBridgeModule.BaseMaterials, filePath, hash)]);

        AssertEqual("unreal-material-identity", identities[filePath]);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeImportedMaterialKeepsSemanticStableId()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var exportedPath = Path.Combine(root, "exported.png");
        WriteImage(exportedPath);
        var asset = new UnrealProjectSyncExportAssetView(
            "Misaka-OtherImage-old",
            "Texture2D",
            "/Game/AssetMaterial/ImageS/CharaterS/Misaka/Misaka-OtherImage-old",
            "/Game/AssetMaterial/ImageS/CharaterS/Misaka/Misaka-OtherImage-old.Misaka-OtherImage-old",
            "/Game/AssetMaterial/ImageS/CharaterS",
            exportedPath);
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "Misaka",
            "御坂美琴",
            string.Empty,
            string.Empty,
            1,
            0,
            new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [new UnrealProjectSyncMaterialBucket("其他图片", "OtherImage", 1, [asset])],
            hasLatestData: true);
        var semanticSnapshot = new UnrealBridgeSemanticSnapshotService().Build(candidate);

        var importResult = new UnrealBridgeDraftImportService().Import(root, candidate);
        var toolboxSnapshot = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(importResult.Character);
        var semanticMaterial = semanticSnapshot.Items.Single(item => item.Module == UnrealBridgeModule.BaseMaterials);
        var toolboxMaterial = toolboxSnapshot.Items.Single(item => item.Module == UnrealBridgeModule.BaseMaterials);

        AssertEqual(semanticMaterial.StableId, toolboxMaterial.StableId);
        AssertEqual(asset.ObjectPath, semanticMaterial.SourceObjectPath);
        var baseline = new UnrealBridgeBaselineService().Build(
            "Misaka",
            Path.Combine(root, "CrossingVoid.uproject"),
            toolboxSnapshot,
            semanticSnapshot);
        AssertEqual(asset.ObjectPath, baseline.Entries[semanticMaterial.StableId].UnrealObjectPath);
        AssertEqual(toolboxMaterial.ContentHash, baseline.Entries[semanticMaterial.StableId].ToolboxHash);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeImportedSemanticItemsKeepStableIds()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var iconPath = Path.Combine(root, "icon.png");
        WriteImage(iconPath);
        var stage = new UnrealProjectSyncSkillStagePreview(
            1, "一技能", "掌心雷", "释放电击", "3", "1", "10", "常态", "空", string.Empty,
            "/Game/Icons/Misaka_SK1.Misaka_SK1", "Misaka_SK1", iconPath,
            [new UnrealProjectSyncSkillMultiplierPreview(1, "100", "0")]);
        var firstSlot = new UnrealProjectSyncSkillSlotPreview(
            "SkillSlot1", "一技能", false, true, string.Empty, [stage]);
        var supportSlot = new UnrealProjectSyncSkillSlotPreview(
            "SkillSlot4", "护援技", true, false, string.Empty, []);
        var skills = new UnrealProjectSyncSkillsPreview(
            true, "/Game/GameActor2D/Misaka/Misaka.Misaka", string.Empty, string.Empty,
            [firstSlot], supportSlot, []);
        var buff = new UnrealProjectSyncBuffPreview(
            "Misaka_BUFF1",
            "/Game/GameActor2D/Misaka/BUFF/Misaka_BUFF1.Misaka_BUFF1",
            true,
            string.Empty,
            "感电",
            "感电",
            "受到额外伤害",
            "接收方-属性",
            "削弱",
            "正常",
            1,
            3,
            10,
            30,
            string.Empty,
            string.Empty,
            "/Game/Icons/Misaka_BUFF1.Misaka_BUFF1",
            "Misaka_BUFF1",
            iconPath);
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "Misaka", "御坂美琴", string.Empty, string.Empty, 0, 0,
            new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            skills,
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
            new UnrealProjectSyncBuffsPreview(true, string.Empty, [buff]),
            [],
            hasLatestData: true);

        var semantic = new UnrealBridgeSemanticSnapshotService().Build(candidate);
        var imported = new UnrealBridgeDraftImportService().Import(root, candidate);
        var toolbox = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(imported.Character);

        AssertEqual(
            semantic.Items.Single(item => item.Module == UnrealBridgeModule.Skills && item.NormalizedName == "掌心雷").StableId,
            toolbox.Items.Single(item => item.Module == UnrealBridgeModule.Skills && item.NormalizedName == "掌心雷").StableId);
        AssertEqual(
            semantic.Items.Single(item => item.Module == UnrealBridgeModule.Buffs).StableId,
            toolbox.Items.Single(item => item.Module == UnrealBridgeModule.Buffs).StableId);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeImportedSequenceFramesKeepStableIds()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var framePath = Path.Combine(root, "idle.png");
        WriteSolidImage(framePath, Color.Red, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        var frame = new UnrealProjectSyncExportAssetView(
            "Misaka_Idle_1", "Texture2D", "/Game/GameActor2D/Misaka/Material/Misaka_Idle_1",
            "/Game/GameActor2D/Misaka/Material/Misaka_Idle_1.Misaka_Idle_1",
            "/Game/GameActor2D", framePath);
        var action = new UnrealProjectSyncSequenceActionPreview(
            "Idle", "待机", "Idle", "base", string.Empty, true, [1], 1, 1, 1, 1, 1, 12,
            [frame], [frame]);
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "Misaka", "御坂美琴", string.Empty, string.Empty, 0, 0,
            new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(
                true, true, "/Game/GameActor2D/Misaka/Misaka_AnimMaps.Misaka_AnimMaps", string.Empty,
                [action], [], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [],
            hasLatestData: true);

        var semantic = new UnrealBridgeSemanticSnapshotService().Build(candidate);
        var imported = new UnrealBridgeDraftImportService().Import(root, candidate);
        var toolbox = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(imported.Character);

        AssertEqual(
            semantic.Items.Single(item => item.StableId.StartsWith("sequence-frame:", StringComparison.Ordinal)).StableId,
            toolbox.Items.Single(item => item.StableId.StartsWith("sequence-frame:", StringComparison.Ordinal)).StableId);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeVoicesClassifyAndKeepStableIds()
{
    AssertEqual(
        VoiceMaterialKind.Skill1,
        UnrealBridgeVoiceClassification.Classify("/Game/GameActor2D/Misaka/Sound/SK1/Misaka_SK1_01", "Misaka_SK1_01"));
    AssertEqual(
        VoiceMaterialKind.Other,
        UnrealBridgeVoiceClassification.Classify("/Game/GameActor2D/Misaka/Sound/Counter/Misaka_Counter_01", "Misaka_Counter_01"));

    var root = CreateTemporaryTestFolder();
    try
    {
        var wavePath = Path.Combine(root, "skill.wav");
        WriteWaveFile(wavePath, 4);
        var asset = new UnrealProjectSyncExportAssetView(
            "Misaka_SK1_01", "SoundWave", "/Game/GameActor2D/Misaka/Sound/SK1/Misaka_SK1_01",
            "/Game/GameActor2D/Misaka/Sound/SK1/Misaka_SK1_01.Misaka_SK1_01",
            "/Game/GameActor2D", wavePath);
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "Misaka", "御坂美琴", string.Empty, string.Empty, 0, 0,
            new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [],
            hasLatestData: true,
            voiceBuckets: [new UnrealProjectSyncVoiceBucket("一技能语音", "Skill1", 1, [asset])]);

        var semantic = new UnrealBridgeSemanticSnapshotService().Build(candidate);
        var imported = new UnrealBridgeDraftImportService().Import(root, candidate);
        var toolbox = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(imported.Character);

        AssertEqual(
            semantic.Items.Single(item => item.Module == UnrealBridgeModule.Voices).StableId,
            toolbox.Items.Single(item => item.Module == UnrealBridgeModule.Voices).StableId);
        AssertEqual(true, imported.ImportedModules.Contains(UnrealBridgeModule.Voices));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeExporterProducesVoiceBuckets()
{
    var script = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"));
    var service = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Services", "UnrealProjectSyncService.cs"));

    AssertEqual(true, script.Contains("def _export_sound_wave", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("output_path = os.path.join(folder, \"{}.wav\"", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("_export_sound_wave(asset, export_root)", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("\"schemaVersion\": 2", StringComparison.Ordinal));
    AssertEqual(true, service.Contains("BuildVoiceBuckets(zdAssets, sequencePreview)", StringComparison.Ordinal));
}

static void UnrealBridgeSequenceVoiceNotificationsClassifyAndBindFrames()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var frame1 = Path.Combine(root, "frame-1.png");
        var frame2 = Path.Combine(root, "frame-2.png");
        var voice = Path.Combine(root, "Vo_Tone1.wav");
        WriteImage(frame1);
        WriteImage(frame2);
        WriteWaveFile(voice, 1);

        const string voiceObjectPath = "/Game/GameActor2D/ALO_Yuki/Sound/Vo_Tone1.Vo_Tone1";
        var voiceAsset = new UnrealProjectSyncExportAssetView(
            "Vo_Tone1",
            "SoundWave",
            "/Game/GameActor2D/ALO_Yuki/Sound/Vo_Tone1",
            voiceObjectPath,
            "/Game/GameActor2D",
            voice);
        var frameAssets = new[]
        {
            new UnrealProjectSyncExportAssetView("Frame1", "Texture2D", "/Game/Test/Frame1", "/Game/Test/Frame1.Frame1", "/Game/Test", frame1),
            new UnrealProjectSyncExportAssetView("Frame2", "Texture2D", "/Game/Test/Frame2", "/Game/Test/Frame2.Frame2", "/Game/Test", frame2)
        };
        var skillAction = new UnrealProjectSyncSequenceActionPreview(
            "Sk1", "一技能", "SK1Seq", "skill", string.Empty, true, [1], 1, 1,
            2, 2, 1, 12, frameAssets, frameAssets,
            [new UnrealProjectSyncSequenceSoundNotifyPreview(
                1, 1.0 / 12.0, 0, voiceObjectPath, "Vo_Tone1", "SoundWave", voice, true)]);
        var skill2Action = new UnrealProjectSyncSequenceActionPreview(
            "Sk2", "二技能", "SK2Seq", "skill", string.Empty, true, [1], 1, 1,
            2, 2, 1, 12, frameAssets, frameAssets,
            [new UnrealProjectSyncSequenceSoundNotifyPreview(
                0, 0, 0, voiceObjectPath, "Vo_Tone1", "SoundWave", voice, true)]);
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "ALO_Yuki", "优纪 ALO", string.Empty, string.Empty, 0, 0,
            new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(true, true, "/Game/Test/AnimMaps", "已读取", [], [skillAction, skill2Action], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [],
            hasLatestData: true,
            voiceBuckets: [new UnrealProjectSyncVoiceBucket("待分配语音", "Other", 1, [voiceAsset])]);

        var imported = new UnrealBridgeDraftImportService().Import(root, candidate);

        var skillVoices = new VoiceMaterialService().LoadSections(imported.Character)
            .Single(section => section.Spec.Kind == VoiceMaterialKind.Skill1)
            .Items;
        AssertEqual(1, skillVoices.Count);
        AssertEqual(0, new VoiceMaterialService().LoadSections(imported.Character)
            .Single(section => section.Spec.Kind == VoiceMaterialKind.Other)
            .Items.Count);
        var sequenceSection = new SequenceFrameService()
            .LoadSections(imported.Character, new CharacterSkillsService().Load(imported.Character))
            .Single(section => section.Action.Code == "Sk1");
        AssertEqual(2, sequenceSection.Frames.Count);
        AssertEqual(skillVoices[0].FilePath, sequenceSection.Frames[1].VoiceFilePath);
        var secondSequenceSection = new SequenceFrameService()
            .LoadSections(imported.Character, new CharacterSkillsService().Load(imported.Character))
            .Single(section => section.Action.Code == "Sk2");
        AssertEqual(skillVoices[0].FilePath, secondSequenceSection.Frames[0].VoiceFilePath);
        var usages = new SequenceFrameService().GetVoiceUsages(imported.Character);
        AssertEqual(2, usages[Path.GetFullPath(skillVoices[0].FilePath)].Count);
        AssertEqual("一技能 #2", usages[Path.GetFullPath(skillVoices[0].FilePath)][0].DisplayText);
        AssertEqual("二技能 #1", usages[Path.GetFullPath(skillVoices[0].FilePath)][1].DisplayText);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeExporterReadsSequenceSoundNotifies()
{
    var script = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"));

    AssertEqual(true, script.Contains("def _sequence_sound_notifies", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("\"soundNotifies\":", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("_get_editor_property(sequence, \"AnimNotifies\"", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("_get_editor_property(notify, \"Sound\"", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("\"frameIndex\":", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("\"timeSeconds\":", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("\"trackIndex\":", StringComparison.Ordinal));
}

static void ProjectSharedMaterialLibraryDeduplicatesAndTracksUsages()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var source = Path.Combine(root, "SharpHit.wav");
        WriteWaveFile(source, 7);
        var service = new ProjectSharedMaterialService();

        var first = service.Import(
            root,
            ProjectSharedMaterialCategory.BattleEffects,
            source,
            "/Game/AssetMaterial/Sound/Pvp_Effect/SharpHit.SharpHit",
            new ProjectSharedMaterialUsage("ALO_Yuki", "Sk1", 3));
        var second = service.Import(
            root,
            ProjectSharedMaterialCategory.BattleEffects,
            source,
            "/Game/AssetMaterial/Sound/Pvp_Effect/SharpHit_Copy.SharpHit_Copy",
            new ProjectSharedMaterialUsage("ALO_Yuki", "Sk2", 5));

        var index = service.Load(root);
        AssertEqual(first.Id, second.Id);
        AssertEqual(1, index.Items.Count);
        AssertEqual(2, index.Items[0].SourceObjectPaths.Count);
        AssertEqual(2, index.Items[0].Usages.Count);
        AssertEqual(true, File.Exists(index.Items[0].FilePath));
        AssertEqual(true, index.Items[0].FilePath.Contains(Path.Combine("Shared", "Audio", "BattleEffects"), StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeImportCopiesSharedSequenceSounds()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var frame = Path.Combine(root, "frame.png");
        var effect = Path.Combine(root, "SharpAir.wav");
        WriteImage(frame);
        WriteWaveFile(effect, 8);
        var frameAsset = new UnrealProjectSyncExportAssetView(
            "Frame", "Texture2D", "/Game/Test/Frame", "/Game/Test/Frame.Frame", "/Game/Test", frame);
        var action = new UnrealProjectSyncSequenceActionPreview(
            "Sk1", "一技能", "SK1Seq", "skill", string.Empty, true, [1], 1, 1,
            1, 1, 1, 12, [frameAsset], [frameAsset],
            [new UnrealProjectSyncSequenceSoundNotifyPreview(
                0, 0, 1,
                "/Game/AssetMaterial/Sound/Pvp_Effect/SharpAir.SharpAir",
                "SharpAir", "SoundWave", effect, false),
             new UnrealProjectSyncSequenceSoundNotifyPreview(
                0, 0, 2,
                "/Game/AssetMaterial/Sound/Pvp_Effect/SharpAir3/META_SharpAir3.META_SharpAir3",
                "META_SharpAir3", "MetaSoundSource", string.Empty, false)]);
        var candidate = new UnrealProjectSyncCharacterCandidate(
            "ALO_Yuki", "优纪 ALO", string.Empty, string.Empty, 0, 0,
            new UnrealProjectSyncCharacterInfoPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, [], [], 1, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CreateEmptyUnrealSkillsPreview(),
            new UnrealProjectSyncSequenceFramesPreview(true, true, "/Game/Test/AnimMaps", "已读取", [], [action], [], []),
            new UnrealProjectSyncBuffsPreview(false, string.Empty, []),
            [],
            hasLatestData: true);

        _ = new UnrealBridgeDraftImportService().Import(root, candidate);

        var shared = new ProjectSharedMaterialService().Load(root);
        AssertEqual(2, shared.Items.Count);
        var copied = shared.Items.Single(item => !item.IsReferenceOnly);
        AssertEqual(ProjectSharedMaterialCategory.BattleEffects, copied.Category);
        AssertEqual("ALO_Yuki", copied.Usages[0].CharacterCode);
        AssertEqual("Sk1", copied.Usages[0].ActionCode);
        AssertEqual(0, copied.Usages[0].FrameIndex);
        var metaSound = shared.Items.Single(item => item.IsReferenceOnly);
        AssertEqual("META_SharpAir3", metaSound.DisplayName);
        AssertEqual("MetaSoundSource", metaSound.AssetClass);
        AssertEqual(1, metaSound.SourceObjectPaths.Count);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeExporterReadsCharacterAnti()
{
    var script = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"));

    AssertEqual(true, script.Contains("def _bool_value(value):", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("\"anti\": _bool_value(_get_editor_property(char_data, \"Anti\"))", StringComparison.Ordinal));
}

static void UnrealBridgeExporterIncludesSharedBuffIcons()
{
    AssertEqual(
        "/Game/AssetMaterial/ImageS/BUFF",
        UnrealProjectSyncService.TargetSharedBuffIconContentPath);

    var script = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"));
    var service = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Services", "UnrealProjectSyncService.cs"));

    AssertEqual(true, script.Contains("SHARED_BUFF_ICON_ROOT = \"/Game/AssetMaterial/ImageS/BUFF\"", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("SHARED_BUFF_ICON_ROOT,", StringComparison.Ordinal));
    AssertEqual(true, script.Contains("if selected_codes and target_path not in (SHARED_BUFF_ICON_ROOT, SHARED_BATTLE_EFFECT_ROOT):", StringComparison.Ordinal));
    AssertEqual(true, service.Contains("TargetSharedBuffIconContentPath,", StringComparison.Ordinal));
}

static UnrealProjectSyncSkillsPreview CreateEmptyUnrealSkillsPreview()
{
    var support = new UnrealProjectSyncSkillSlotPreview(
        "SkillSlot4",
        "护援技",
        true,
        false,
        "未读取",
        []);
    return new UnrealProjectSyncSkillsPreview(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        [],
        support,
        []);
}

static void UnrealBridgeExecutionPlanUsesSelectedChangesAndDeletesLast()
{
    var sourceItem = new UnrealBridgeSnapshotItem(
        "asset-add",
        "module:BaseMaterials",
        UnrealBridgeModule.BaseMaterials,
        "头像",
        "toolbox-add",
        "{}",
        @"D:\Project\Misaka\AssetMaterial\Icon\Misaka-Icon-1.png",
        ToolboxRelativePath: "AssetMaterial/Icon/Misaka-Icon-1.png",
        NormalizedName: "Misaka-Icon-1");
    var deleteItem = new UnrealBridgeSnapshotItem(
        "asset-delete",
        "module:Voices",
        UnrealBridgeModule.Voices,
        "旧语音",
        "unreal-delete",
        "{}",
        string.Empty,
        SourceObjectPath: "/Game/GameActor2D/Origin_Misaka/Sound/Old.Old",
        OriginIdentity: "voice-guid",
        NormalizedName: "Old");
    var changes = new[]
    {
        new UnrealBridgeChange(
            sourceItem.StableId,
            sourceItem.Module,
            sourceItem.DisplayName,
            UnrealBridgeChangeKind.Added,
            sourceItem,
            null,
            true),
        new UnrealBridgeChange(
            "not-selected",
            UnrealBridgeModule.CharacterInfo,
            "未选中",
            UnrealBridgeChangeKind.Updated,
            sourceItem with { StableId = "not-selected" },
            null,
            false),
        new UnrealBridgeChange(
            deleteItem.StableId,
            deleteItem.Module,
            deleteItem.DisplayName,
            UnrealBridgeChangeKind.DeleteCandidate,
            null,
            deleteItem,
            true)
    };

    var plan = new UnrealBridgeExecutionPlanService().Build(
        UnrealBridgeDirection.PublishToUnreal,
        "Misaka",
        @"D:\UnrealMap\CrossingVoid\CrossingVoid.uproject",
        changes,
        deletionsConfirmed: true,
        isFirstPublish: true,
        templateCharacterCode: "ALO_Yuki");

    AssertSequence(
        [UnrealBridgeOperationKind.Add, UnrealBridgeOperationKind.Delete],
        plan.Operations.Select(operation => operation.Kind).ToArray());
    AssertSequence(
        ["asset-add", "asset-delete"],
        plan.Operations.Select(operation => operation.StableId).ToArray());
    AssertEqual(true, plan.BackupRequired);
    AssertEqual("ALO_Yuki", plan.TemplateCharacterCode);
}

static void UnrealBridgeLegacyVoiceNamesClassifySafely()
{
    AssertEqual(
        VoiceMaterialKind.Formation,
        UnrealBridgeVoiceClassification.Classify("/Game/GameActor2D/ALO_Yuki/Sound", "Vo_Select"));
    AssertEqual(
        VoiceMaterialKind.Hurt,
        UnrealBridgeVoiceClassification.Classify("/Game/GameActor2D/ALO_Yuki/Sound", "Vo_Odnm4"));
    AssertEqual(
        VoiceMaterialKind.Other,
        UnrealBridgeVoiceClassification.Classify("/Game/GameActor2D/ALO_Yuki/Sound", "Vo_Tone12"));
}

static void UnrealBridgeVoicePublishCreatesCategoryFolder()
{
    var toolboxItem = new UnrealBridgeSnapshotItem(
        "voice:formation-1",
        "module:Voices",
        UnrealBridgeModule.Voices,
        "编队语音 #1",
        "toolbox-hash",
        "{\"kind\":\"Formation\",\"index\":\"1\"}",
        @"D:\Project\ALO_Yuki\Sound\Formation\ALO_Yuki-Formation-1.wav",
        ToolboxRelativePath: "Sound/Formation/ALO_Yuki-Formation-1.wav",
        NormalizedName: "ALO_Yuki-Formation-1");
    var unrealItem = new UnrealBridgeSnapshotItem(
        "voice:formation-1",
        "module:Voices",
        UnrealBridgeModule.Voices,
        "Vo_Select",
        "unreal-hash",
        "{\"kind\":\"Formation\"}",
        string.Empty,
        SourceObjectPath: "/Game/GameActor2D/ALO_Yuki/Sound/Vo_Select.Vo_Select",
        OriginIdentity: "voice-guid",
        NormalizedName: "Vo_Select");
    var baseline = new UnrealBridgeSyncState
    {
        Entries =
        {
            [toolboxItem.StableId] = new UnrealBridgeSyncStateEntry(
                toolboxItem.ContentHash,
                unrealItem.ContentHash,
                UnrealObjectPath: unrealItem.SourceObjectPath,
                UnrealIdentity: unrealItem.OriginIdentity,
                ToolboxRelativePath: toolboxItem.ToolboxRelativePath,
                NormalizedName: unrealItem.NormalizedName)
        }
    };
    var change = new UnrealBridgeDiffService().Compare(
        new UnrealBridgeSnapshot("ALO_Yuki", [toolboxItem]),
        new UnrealBridgeSnapshot("ALO_Yuki", [unrealItem]),
        UnrealBridgeDirection.PublishToUnreal,
        baseline).Single();

    AssertEqual(UnrealBridgeChangeKind.Renamed, change.Kind);
    AssertEqual(true, change.IsSelected);

    var operation = new UnrealBridgeExecutionPlanService().Build(
        UnrealBridgeDirection.PublishToUnreal,
        "ALO_Yuki",
        @"D:\UnrealMap\CrossingVoid\CrossingVoid.uproject",
        [change],
        deletionsConfirmed: false,
        isFirstPublish: false,
        templateCharacterCode: string.Empty).Operations.Single();

    AssertEqual(
        "/Game/GameActor2D/ALO_Yuki/Sound/Formation/ALO_Yuki_Formation_1.ALO_Yuki_Formation_1",
        operation.TargetObjectPath);
}

static void UnrealBridgePublishPolicySelectsOnlyMappedFileAssets()
{
    var fileItem = new UnrealBridgeSnapshotItem(
        "material:1", "module:BaseMaterials", UnrealBridgeModule.BaseMaterials, "头像", "hash", "{}",
        Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"),
        SourceObjectPath: "/Game/Images/Misaka_Icon.Misaka_Icon");
    var semanticItem = new UnrealBridgeSnapshotItem(
        "character:info", string.Empty, UnrealBridgeModule.CharacterInfo, "角色信息", "hash", "{}", string.Empty,
        SourceObjectPath: "/Game/Items/Misaka.Misaka");

    AssertEqual(true, UnrealBridgePublishSupportPolicy.CanExecute(new UnrealBridgeChange(
        fileItem.StableId, fileItem.Module, fileItem.DisplayName, UnrealBridgeChangeKind.Updated,
        fileItem, fileItem, true)));
    AssertEqual(true, UnrealBridgePublishSupportPolicy.CanExecute(new UnrealBridgeChange(
        fileItem.StableId, fileItem.Module, fileItem.DisplayName, UnrealBridgeChangeKind.Renamed,
        fileItem, fileItem, true)));
    AssertEqual(false, UnrealBridgePublishSupportPolicy.CanExecute(new UnrealBridgeChange(
        semanticItem.StableId, semanticItem.Module, semanticItem.DisplayName, UnrealBridgeChangeKind.Updated,
        semanticItem, semanticItem, true)));
    AssertEqual(false, UnrealBridgePublishSupportPolicy.CanExecute(new UnrealBridgeChange(
        fileItem.StableId, fileItem.Module, fileItem.DisplayName, UnrealBridgeChangeKind.Added,
        fileItem, null, true)));
}

static void UnrealBridgePostExecutionRestoresRenamedIdentity()
{
    var rescanned = CreateUnrealBridgeSnapshot(
        ("temporary-id", UnrealBridgeModule.BaseMaterials, "新头像", "new-hash"));
    rescanned = new UnrealBridgeSnapshot(
        "Misaka",
        [rescanned.Items[0] with { SourceObjectPath = "/Game/Images/Misaka_Icon.Misaka_Icon" }]);
    var result = new UnrealBridgeExecutionResult
    {
        Succeeded = true,
        Items =
        {
            new UnrealBridgeExecutionItemResult
            {
                StableId = "material:original-id",
                Succeeded = true,
                ObjectPath = "/Game/Images/Misaka_Icon.Misaka_Icon"
            }
        }
    };

    var restored = new UnrealBridgePostExecutionIdentityService().Restore(rescanned, result);

    AssertEqual("material:original-id", restored.Items.Single().StableId);
}

static void UnrealBridgeExecutionPlanRequiresDeleteConfirmationAndTemplate()
{
    var delete = CreateUnrealBridgeChange(UnrealBridgeChangeKind.DeleteCandidate, isSelected: true);
    var add = CreateUnrealBridgeChange(UnrealBridgeChangeKind.Added, isSelected: true);
    var service = new UnrealBridgeExecutionPlanService();
    var deleteRejected = false;
    var templateRejected = false;
    try
    {
        _ = service.Build(
            UnrealBridgeDirection.PublishToUnreal,
            "Misaka",
            @"D:\Project\Game.uproject",
            [delete],
            deletionsConfirmed: false,
            isFirstPublish: false,
            templateCharacterCode: string.Empty);
    }
    catch (InvalidOperationException)
    {
        deleteRejected = true;
    }

    try
    {
        _ = service.Build(
            UnrealBridgeDirection.PublishToUnreal,
            "Misaka",
            @"D:\Project\Game.uproject",
            [add],
            deletionsConfirmed: true,
            isFirstPublish: true,
            templateCharacterCode: string.Empty);
    }
    catch (InvalidOperationException)
    {
        templateRejected = true;
    }

    AssertEqual(true, deleteRejected);
    AssertEqual(true, templateRejected);
}

static void UnrealBridgeExecutorUsesFixedScriptAndResultProtocol()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var editorPath = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        var commandPath = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe");
        var projectPath = Path.Combine(root, "Game.uproject");
        var planPath = Path.Combine(root, "plan.json");
        var progressPath = Path.Combine(root, "progress.json");
        var resultPath = Path.Combine(root, "result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
        File.WriteAllText(editorPath, string.Empty);
        File.WriteAllText(commandPath, string.Empty);
        File.WriteAllText(projectPath, "{}");
        File.WriteAllText(planPath, "{}");
        var service = new UnrealBridgeExecutorService();

        var startInfo = service.BuildProcessStartInfo(
            editorPath,
            projectPath,
            planPath,
            progressPath,
            resultPath);

        AssertEqual(commandPath, startInfo.FileName);
        AssertEqual(true, startInfo.Arguments.Contains("execute_unreal_bridge.py", StringComparison.OrdinalIgnoreCase));
        AssertEqual(planPath, startInfo.Environment["ZD_BRIDGE_PLAN_PATH"]);
        AssertEqual(progressPath, startInfo.Environment["ZD_BRIDGE_PROGRESS_PATH"]);
        AssertEqual(resultPath, startInfo.Environment["ZD_BRIDGE_RESULT_PATH"]);
        AssertEqual(true, File.Exists(service.GetExecuteScriptPath()));
        AssertEqual(
            true,
            File.ReadAllText(service.GetExecuteScriptPath()).Contains("make_directory", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void UnrealBridgeVerificationRequiresCompleteSuccess()
{
    var plan = new UnrealBridgeExecutionPlan
    {
        CharacterCode = "Misaka",
        UnrealProjectPath = @"D:\Project\Game.uproject",
        TemplateCharacterCode = "ALO_Yuki",
        Operations =
        {
            new UnrealBridgeOperation
            {
                StableId = "asset-1",
                Module = UnrealBridgeModule.BaseMaterials,
                Kind = UnrealBridgeOperationKind.Update
            }
        }
    };
    var toolboxItem = new UnrealBridgeSnapshotItem(
        "asset-1",
        "module:BaseMaterials",
        UnrealBridgeModule.BaseMaterials,
        "头像",
        "toolbox-hash",
        "{}",
        @"D:\Project\Misaka\AssetMaterial\Icon\Misaka-Icon-1.png",
        ToolboxRelativePath: "AssetMaterial/Icon/Misaka-Icon-1.png",
        NormalizedName: "Misaka-Icon-1");
    var unrealItem = new UnrealBridgeSnapshotItem(
        "asset-1",
        "module:BaseMaterials",
        UnrealBridgeModule.BaseMaterials,
        "头像",
        "unreal-hash",
        "{}",
        string.Empty,
        SourceObjectPath: "/Game/GameActor2D/Origin_Misaka/Material/Misaka_Icon_1.Misaka_Icon_1",
        OriginIdentity: "package-guid",
        NormalizedName: "Misaka_Icon_1");
    var toolbox = new UnrealBridgeSnapshot("Misaka", [toolboxItem]);
    var unreal = new UnrealBridgeSnapshot("Misaka", [unrealItem]);
    var success = new UnrealBridgeExecutionResult
    {
        Succeeded = true,
        Items =
        {
            new UnrealBridgeExecutionItemResult { StableId = "asset-1", Succeeded = true }
        }
    };
    var service = new UnrealBridgeVerificationService();

    var state = service.BuildVerifiedState(plan, success, toolbox, unreal);
    AssertEqual("toolbox-hash", state.Entries["asset-1"].ToolboxHash);
    AssertEqual("unreal-hash", state.Entries["asset-1"].UnrealHash);
    AssertEqual("package-guid", state.Entries["asset-1"].UnrealIdentity);

    var rejected = false;
    success.Items[0].Succeeded = false;
    try
    {
        _ = service.BuildVerifiedState(plan, success, toolbox, unreal);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    AssertEqual(true, rejected);
}

static void UnrealBridgeVerificationPreservesUnselectedBaselineEntries()
{
    var plan = new UnrealBridgeExecutionPlan
    {
        CharacterCode = "Misaka",
        UnrealProjectPath = @"D:\Project\Game.uproject",
        Operations =
        {
            new UnrealBridgeOperation
            {
                StableId = "selected",
                Module = UnrealBridgeModule.BaseMaterials,
                Kind = UnrealBridgeOperationKind.Update
            }
        }
    };
    var result = new UnrealBridgeExecutionResult
    {
        Succeeded = true,
        Items =
        {
            new UnrealBridgeExecutionItemResult { StableId = "selected", Succeeded = true }
        }
    };
    var toolbox = CreateUnrealBridgeSnapshot(
        ("selected", UnrealBridgeModule.BaseMaterials, "新头像", "selected-source-new"));
    var unreal = CreateUnrealBridgeSnapshot(
        ("selected", UnrealBridgeModule.BaseMaterials, "新头像", "selected-target-new"));
    var previous = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["selected"] = new UnrealBridgeSyncStateEntry("selected-source-old", "selected-target-old"),
            ["unselected"] = new UnrealBridgeSyncStateEntry("kept-source", "kept-target")
        }
    };

    var state = new UnrealBridgeVerificationService().BuildVerifiedState(
        plan,
        result,
        toolbox,
        unreal,
        previous);

    AssertEqual("selected-source-new", state.Entries["selected"].ToolboxHash);
    AssertEqual("selected-target-new", state.Entries["selected"].UnrealHash);
    AssertEqual("kept-source", state.Entries["unselected"].ToolboxHash);
    AssertEqual("kept-target", state.Entries["unselected"].UnrealHash);
}

static UnrealBridgeChange CreateUnrealBridgeChange(UnrealBridgeChangeKind kind, bool isSelected)
{
    var item = new UnrealBridgeSnapshotItem(
        "test:item",
        string.Empty,
        UnrealBridgeModule.CharacterInfo,
        "测试项",
        "hash",
        "{}",
        string.Empty);
    return new UnrealBridgeChange(
        item.StableId,
        item.Module,
        item.DisplayName,
        kind,
        item,
        null,
        isSelected);
}

static UnrealBridgeSnapshot CreateUnrealBridgeSnapshot(
    params (string StableId, UnrealBridgeModule Module, string Name, string Hash)[] items)
{
    return new UnrealBridgeSnapshot(
        "Misaka",
        items.Select(item => new UnrealBridgeSnapshotItem(
            item.StableId,
            ParentStableId: string.Empty,
            item.Module,
            item.Name,
            item.Hash,
            PayloadJson: "{}",
            AssetPath: string.Empty)).ToArray());
}

static string CreateTemporaryTestFolder()
{
    var root = Path.Combine(Path.GetTempPath(), "CrossingVoidZDTool.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}

static void WriteImage(string path)
{
    using var image = new Bitmap(566, 325);
    image.Save(path, ImageFormat.Png);
}

static void WriteColoredImage(string path, Color color)
{
    using var image = new Bitmap(566, 325);
    using var graphics = Graphics.FromImage(image);
    graphics.Clear(color);
    image.Save(path, ImageFormat.Png);
}

static void WriteWaveFile(string path, byte sample, int sampleCount = 1)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + sampleCount);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
    writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(8000);
    writer.Write(8000);
    writer.Write((short)1);
    writer.Write((short)8);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    writer.Write(sampleCount);
    writer.Write(Enumerable.Repeat(sample, sampleCount).ToArray());
}

static void WriteWaveFileWithJunkMetadata(string path, byte sample, int sampleCount = 1)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(48 + sampleCount);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
    writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(8000);
    writer.Write(8000);
    writer.Write((short)1);
    writer.Write((short)8);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("JUNK"));
    writer.Write(3);
    writer.Write(new byte[] { 1, 2, 3, 0 });
    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    writer.Write(sampleCount);
    writer.Write(Enumerable.Repeat(sample, sampleCount).ToArray());
}

static void WriteWaveFileWithSilenceSegments(
    string path,
    int leadingSilentSampleCount,
    int audibleSampleCount,
    int trailingSilentSampleCount)
{
    const int sampleRate = 8000;
    var samples = Enumerable.Repeat((byte)128, leadingSilentSampleCount)
        .Concat(Enumerable.Range(0, audibleSampleCount).Select(index => index % 2 == 0 ? (byte)208 : (byte)48))
        .Concat(Enumerable.Repeat((byte)128, trailingSilentSampleCount))
        .ToArray();
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + samples.Length);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
    writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(sampleRate);
    writer.Write(sampleRate);
    writer.Write((short)1);
    writer.Write((short)8);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    writer.Write(samples.Length);
    writer.Write(samples);
}

static void WriteExtensibleWaveFileWithSilenceSegments(
    string path,
    int leadingSilentFrameCount,
    int audibleFrameCount,
    int trailingSilentFrameCount)
{
    const int sampleRate = 8000;
    const short channels = 6;
    const short bitsPerSample = 16;
    const short blockAlign = channels * (bitsPerSample / 8);
    var frameCount = leadingSilentFrameCount + audibleFrameCount + trailingSilentFrameCount;
    var dataSize = frameCount * blockAlign;
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(60 + dataSize);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
    writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
    writer.Write(40);
    writer.Write(unchecked((short)0xFFFE));
    writer.Write(channels);
    writer.Write(sampleRate);
    writer.Write(sampleRate * blockAlign);
    writer.Write(blockAlign);
    writer.Write(bitsPerSample);
    writer.Write((short)22);
    writer.Write(bitsPerSample);
    writer.Write(0x3F);
    writer.Write(1);
    writer.Write((short)0);
    writer.Write((short)0x0010);
    writer.Write(new byte[] { 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    writer.Write(dataSize);
    for (var frame = 0; frame < frameCount; frame++)
    {
        var isAudible = frame >= leadingSilentFrameCount &&
            frame < leadingSilentFrameCount + audibleFrameCount;
        var sample = isAudible ? (short)(frame % 2 == 0 ? 12000 : -12000) : (short)0;
        for (var channel = 0; channel < channels; channel++)
        {
            writer.Write(sample);
        }
    }
}

static void WriteSolidImage(
    string path,
    Color color,
    int width,
    int height,
    ImageFormat? imageFormat = null)
{
    using var image = new Bitmap(width, height);
    using var graphics = Graphics.FromImage(image);
    graphics.Clear(color);
    image.Save(path, imageFormat ?? ImageFormat.Png);
}

static int CountOccurrences(string source, string value)
{
    var count = 0;
    var startIndex = 0;
    while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
    {
        count++;
        startIndex += value.Length;
    }

    return count;
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!Equals(expected, actual))
    {
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}。");
    }
}

static void AssertSequence<T>(T[] expected, T[] actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException($"期望 [{string.Join(", ", expected)}]，实际 [{string.Join(", ", actual)}]。");
    }
}
