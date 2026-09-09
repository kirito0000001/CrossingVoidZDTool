using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CrossingVoidZDTool;
using CrossingVoidZDTool.Services;
using CrossingVoidZDTool.ViewModels;
using Microsoft.UI.Xaml;

if (args.Length > 0 && string.Equals(args[0], "smoke", StringComparison.OrdinalIgnoreCase))
{
    return RunUnrealSyncSmoke(args.Skip(1).ToArray());
}

if (args.Length > 0 && string.Equals(args[0], "migrate-paths", StringComparison.OrdinalIgnoreCase))
{
    return RunPortablePathMigration(args.Skip(1).ToArray());
}

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
    ("虚幻双向差异与选择状态分离", UnrealBridgeChangesDoNotSelectByDefault),
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
    ("同步前备份开关对每一步都生效", BackupSettingGovernsEveryStep),
    ("虚幻项目备份使用当前引擎 ZipProjectUp", UnrealBridgeBackupUsesEngineAutomationTool),
    ("虚幻发布快照拒绝草稿角色", UnrealBridgeToolboxSnapshotRejectsDraftCharacter),
    ("虚幻工具箱快照覆盖角色六类模块", UnrealBridgeToolboxSnapshotCoversAllModules),
    ("蓝图置入目标值取自工具箱数据", BlueprintSetupRequestComesFromToolboxData),
    ("蓝图置入序列绑定与代号表一致", BlueprintSetupSequenceBindingsMatchCatalog),
    ("蓝图置入把中文选项映射成虚幻枚举名", BlueprintSetupMapsChineseOptionsToUnrealEnums),
    ("蓝图置入按行写数据表而不是整表回灌", BlueprintSetupWritesRowsInsteadOfRefillingTable),
    ("蓝图置入中栏按组显示且隐藏无变化项", BlueprintSetupGroupsItemsAndHidesUnchanged),
    ("同步流程步号不会被夹回最后一步之前", WorkflowStepIsNotClampedBelowLastStep),
    ("同步进度按角色和步骤存进角色目录", WorkflowStepCacheLivesInCharacterFolder),
    ("已加载的步骤不再重复触发虚幻检测", WorkflowStepSkipsDetectionWhenAlreadyLoaded),
    ("切换角色后各自的步骤与结果互不串台", WorkflowStateIsIsolatedPerCharacter),
    ("角色目录里的路径落盘时不带盘符", CharacterOwnedPathsArePortableOnDisk),
    ("角色目录搬家后图标路径依然指得到", PortablePathsSurviveCharacterFolderMove),
    ("旧机器留下的图标路径会被修回来", StaleIconPathIsRepairedOnRead),
    ("同步缓存只收编工具箱侧路径", SyncCacheKeepsUnrealSidePathsAbsolute),
    ("中栏任何状态都有东西显示", WorkspaceNeverShowsBlankPanel),
    ("中栏分组与条目始终一致", WorkspaceGroupsStayConsistentWithItems),
    ("直接改列表中栏也会跟着刷新", WorkspaceReactsToRawCollectionChanges),
    ("逐项勾选会刷新右栏的已选择计数", SingleItemSelectionRefreshesStepSelectionText),
    ("重置导入操作会刷新中栏与流程状态", ResetImportOperationRefreshesWorkspaceAndWorkflow),
    ("改过分类的语音不会被基线当成已同步", ReclassifiedVoiceSurvivesBaselineFilter),
    ("元数据损坏时角色不会从角色台消失", CorruptMetadataKeepsCharacterVisible),
    ("角色数据写到一半崩溃不会丢原文件", CharacterDataWriteIsAtomic),
    ("语音名字识别不被角色代号误伤", VoiceClassificationIgnoresCharacterCodeNoise),
    ("语音分类表与桥接脚本标签一致", VoiceSpecsMatchBridgeScriptLabels),
    ("查看模式能看序列但改不了", ReadOnlySequenceCanBeViewedButNotEdited),
    ("语音规范路径按分类落到对应目录", VoicePathPolicyBuildsCanonicalFolder),
    ("清单读不出来时不清理帧文件", UnreadableManifestSkipsFramePruning),
    ("设置文件损坏时留档并说出来", CorruptSettingsAreQuarantinedAndReported),
    ("原子写不残留临时文件", AtomicWriteLeavesNoTemporaryFile),
    ("取消时真的杀掉 Unreal 进程", CancellingUnrealRunKillsTheProcess),
    ("子进程输出一律固定 UTF-8", RedirectedProcessOutputAlwaysFixesEncoding),
    ("进程编排不再各写一份", ProcessOrchestrationIsNotDuplicated),
    ("技术债只许降不许升", TechnicalDebtRatchetOnlyGoesDown),
    ("整份设置换掉后派生属性会刷新", ReplacingSettingsNotifiesDerivedProperties),
    ("诊断条目不算成功的动作", DiagnosticItemsDoNotCountAsSucceededActions),
    ("技能数值解析不了要报错不要归零", UnparsableSkillNumbersAreReportedNotZeroed),
    ("技能数值在逗号小数点区域仍能往返", SkillNumbersSurviveCommaDecimalCulture),
    ("蓝图置入的引用比较与纠偏自检", BlueprintSetupSelfCheckPasses),
    ("依次检测卡在第一个待处理步骤", DetectAllStepsStopsAtFirstBlockedStep),
    ("某一步检测失败就不再往下跑", DetectAllStepsStopsOnStepFailure),
    ("进入某一步先落步再检测", EnteringStepNavigatesBeforeDetecting),
    ("每一步的进度都按阶段分段", WorkflowProgressIsPhasedForEveryStep),
    ("在线执行不可用时退回离线", RemoteExecutionFallsBackToOffline),
    ("依次检测只检测不写入", DetectAllStepsNeverWrites),
    ("蓝图置入写入按钮在检测结束后可用", BlueprintSetupApplyButtonEnablesAfterScan),
    ("蓝图置入支持一键全选和全取消", BlueprintSetupSupportsSelectAllToggle),
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
    ("换工程路径后已同步素材不会变成冲突", UnrealBridgeDiffKeepsSyncedItemsAfterProjectMove),
    ("双端格式哈希不同但各自未变时保持未修改", UnrealBridgeDiffUsesEndpointBaselinesForUnchangedItems),
    ("无旧基线时已有配对素材先迁移为未修改", UnrealBridgeDiffMigratesMatchedItemsWithoutBaseline),
    ("特殊字符清洗后的语音仍按规范路径配对", UnrealBridgeDiffPairsSanitizedVoiceNames),
    ("规范连字符语音不会误报改名", UnrealBridgeDiffKeepsCanonicalVoiceNameUnchanged),
    ("虚幻基础配置使用独立第四步工作区", UnrealLightConfigurationUsesDedicatedFourthStep),
    ("虚幻基础配置脚本遵守确认字段白名单", UnrealLightConfigurationScriptUsesConfirmedWhitelist),
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
    ("主项目资源排除测试构建输出", MainProjectExcludesTestBuildOutputsFromResources),
    ("序列动作目录与导出脚本代号表一致", SequenceActionCatalogMatchesExporterSpecs),
    ("序列动作目录识别历史命名和形态后缀", SequenceActionCatalogResolvesLegacyNamesAndForms),
    ("序列动作目录产出统一的规范资产命名", SequenceActionCatalogProducesCanonicalAssetNames),
    ("工具箱动作代号全部能解析到规范动作", ToolboxSequenceActionCodesResolveThroughCatalog),
    ("清理令牌不会命中角色名里的同名片段", SequenceActionLegacyTokensDoNotMatchCharacterName),
    ("同步计划字段全部被桥接脚本读取", SequenceSyncPlanFieldsAreReadByBridgeScript),
    ("序列快照在历史拼写下仍然两侧对齐", SequenceSnapshotsAlignAcrossLegacySpellings),
    ("已发布序列帧判为无差异并可建立基线", SequenceFramesAlreadyPublishedCountAsUnchanged),
    ("序列帧数量变化产生新增和删除", SequenceFrameCountChangesProduceAddAndDelete),
    ("序列帧区分工具箱更新和虚幻侧冲突", SequenceFrameEditsSeparateUpdateFromConflict),
    ("序列基线只提交本次执行的动作", SequenceBaselineCommitsOnlyExecutedActions),
    ("动作目录里的多余素材列为待删除", StaleSequenceAssetsBecomeDeleteCandidates),
    ("缓存恢复后序列分组标题仍是动作名", SequenceGroupTitleSurvivesCacheRestore),
    ("同步前比较忽略两侧口径差异不误报变化", PublishChangeComparisonIgnoresUnchangedSubset),
    ("建立迁移基线不会翻转序列差异类型", MigrationBaselineDoesNotFlipSequenceKinds),
    ("同步计划携带勾选的待删资产路径", SequencePlanCarriesSelectedStaleAssetPaths),
    ("零帧动作被跳过而不是整批失败", SequencePlanSkipsActionsWithoutFrames),
    ("基线只刷新执行成功的动作", SequenceBaselineSkipsFailedActions),
    ("在线执行只认当前选中的项目", OnlineExecutionOnlyMatchesSelectedProject),
    ("会话缓存落盘不会卡死 UI 线程", SessionCacheFlushDoesNotDeadlockUiThread),
    ("批量恢复勾选只重算一次", BulkSelectionRestoreRecomputesOnce),
    ("资产类名不会把内存地址带进哈希", AssetClassNeverCarriesPointerIntoHash),
    ("导出帧列表一个关键帧一项", ExportedFrameListIsOneEntryPerKeyframe),
    ("同步进度分段覆盖每个阶段", SyncProgressBandsCoverEveryPhase),
    ("导出跳过未变化的 PNG", ExportSkipsUnchangedPngFiles),
    ("复扫导出与同步共用一个编辑器会话", PostSyncExportRunsInTheSameEditorSession),
    ("空白帧的删除项可以执行", BlankFrameDeletionIsExecutable),
    ("两侧一致就不是冲突", MatchingSidesAreNotConflicts),
    ("非规范序列只解绑不删资产", OrphanSequencesAreDetachedNotDeleted),
    ("同步完成后仍有可见反馈", SyncCompletionLeavesVisibleFeedback)
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
    AssertEqual(13, VoiceMaterialService.Specs.Count);
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
            VoiceMaterialKind.Other,
            // 音效追加在最末尾：有三处按枚举序数排序，插进中间会静默改掉行为
            VoiceMaterialKind.SoundEffect
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

        AssertEqual(13, viewModel.VoiceSections.Count);
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
    var xaml = ReadAllProjectXaml();
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

        var xaml = ReadAllProjectXaml();
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

        var xaml = ReadAllProjectXaml();
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
        var xaml = ReadAllProjectXaml();

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
    var xaml = ReadAllProjectXaml();
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

        var xaml = ReadAllProjectXaml();
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

        var xaml = ReadAllProjectXaml();
        AssertEqual(
            true,
            xaml.Contains(
                "Text=\"{Binding SequenceFrames.EditorPlaybackPositionText, Mode=OneWay}\"",
                StringComparison.Ordinal));
    });
}

static void SequenceEditorCreatesFirstFrameAndCollectionSupportsModifierMultiSelect()
{
    var xaml = ReadAllProjectXaml();
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

    var xaml = ReadAllProjectXaml();
    var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.SequenceFrames.cs"));
    AssertEqual(true, xaml.Contains("Content=\"一键处理\"", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("Click=\"ResolveAllSequenceFrameDuplicatesButton_Click\"", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("ResolveAllSequenceFrameDuplicatesButton_Click", StringComparison.Ordinal));
}

static void OuterSequencePreviewProvidesFrameCollectionEntry()
{
    var xaml = ReadAllProjectXaml();
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
    var xaml = ReadAllProjectXaml();
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
    var xaml = ReadAllProjectXaml();
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

    var xaml = ReadAllProjectXaml();
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

static void UnrealBridgeChangesDoNotSelectByDefault()
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
    AssertEqual(false, changes.Single(item => item.StableId == "character:info").IsSelected);
    AssertEqual(UnrealBridgeChangeKind.Added, changes.Single(item => item.StableId == "material:portrait:main").Kind);
    AssertEqual(false, changes.Single(item => item.StableId == "material:portrait:main").IsSelected);
}

static void UnrealSyncCharacterSelectorUsesSummaryAndFlyoutList()
{
    // 这条用例原来钉的是一套已经不存在的界面：摘要面板 + 弹出式选择器，
    // 外加一堆写死的像素（行高 52、列表最大高 460）。左栏后来改成了
    // 「搜索框 + 常驻来源列表」，那些断言就成了描述旧设计的化石。
    // 现在钉的是还成立的行为：来源能搜、能点、点了有人接、条目认得出是谁。
    var document = XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml"));
    var codeText = ReadUnrealSyncWindowSource();
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

    XElement Named(string name) => document.Descendants().Single(element =>
        string.Equals(element.Attribute(x + "Name")?.Value, name, StringComparison.Ordinal));

    var search = Named("UnrealSyncSourceSearchBox");
    var list = Named("UnrealSyncCharacterSourceListView");
    var refreshButton = Named("RefreshUnrealCharactersButton");

    // 搜索框要真的绑到搜索文本上，否则输入了也筛不动。
    AssertEqual(
        "{Binding UnrealProjectSync.SourceSearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
        search.Attribute("Text")?.Value ?? string.Empty);
    AssertEqual(false, string.IsNullOrWhiteSpace(search.Attribute("PlaceholderText")?.Value));

    // 列表要绑到来源集合，并且点击能选中角色。
    AssertEqual(
        "{Binding UnrealProjectSync.CharacterSources, Mode=OneWay}",
        list.Attribute("ItemsSource")?.Value ?? string.Empty);
    AssertEqual("True", list.Attribute("IsItemClickEnabled")?.Value ?? string.Empty);
    AssertEqual("UnrealSyncSourceListView_ItemClick", list.Attribute("ItemClick")?.Value ?? string.Empty);

    // 条目上要能看出是哪个角色：中文名 + 代号。
    var itemTemplate = list.Descendants().Single(element => element.Name.LocalName == "DataTemplate");
    AssertEqual(true, itemTemplate.Descendants().Any(element =>
        string.Equals(element.Attribute("Text")?.Value, "{Binding DisplayName}", StringComparison.Ordinal)));
    AssertEqual(true, itemTemplate.Descendants().Any(element =>
        string.Equals(element.Attribute("Text")?.Value, "{Binding SecondaryText}", StringComparison.Ordinal)));

    // 刷新按钮接到拉取角色的处理器。
    AssertEqual("GetUnrealProjectCharactersButton_Click", refreshButton.Attribute("Click")?.Value ?? string.Empty);

    // 点击处理器要真的认这个条目类型，否则点了没反应。
    AssertEqual(true, codeText.Contains("UnrealSyncSourceListView_ItemClick", StringComparison.Ordinal));
    AssertEqual(true, codeText.Contains("e.ClickedItem is not UnrealSyncSourceItem source", StringComparison.Ordinal));
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
    var window = ReadUnrealSyncWindowSource();

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
    // 导出成没成功以产物为准，不以退出码为准：commandlet 只要编辑器在别处报过错
    // （实测是 Misaka_AnimBP 有个 Play Sequence 指向已不存在的序列）就返回非 0，
    // 而日志里明写着 Python script executed successfully、清单也照常写出来了。
    // 按退出码判，每次检测都会被判成导出失败。
    //
    // 这条用例以前是断言 UnrealProjectSyncService.cs 里存在某个字面量——查的是变量名，
    // 改个命名就假报警，也拦不住逻辑写错。进程编排抽成 UnrealProcessRunner 之后，
    // 同一份行为可以直接跑起来验。
    var root = CreateTemporaryTestFolder();
    try
    {
        var outputPath = Path.Combine(root, "manifest.json");

        // 上一轮留下的残留：写在本次开跑之前
        File.WriteAllText(outputPath, "{}");
        var staleStart = DateTime.UtcNow.AddSeconds(1);
        AssertEqual(false, UnrealProcessRunner.IsFreshOutput(outputPath, staleStart));

        // 本轮新写的产物
        AssertEqual(true, UnrealProcessRunner.IsFreshOutput(outputPath, DateTime.UtcNow.AddSeconds(-5)));

        // 文件根本不存在时不能算新鲜
        AssertEqual(false, UnrealProcessRunner.IsFreshOutput(
            Path.Combine(root, "missing.json"), DateTime.UtcNow.AddSeconds(-5)));

        // 退出码非 0，但 verdict 说产物有效 —— 不许判失败
        var okRun = UnrealProcessRunner.RunAsync(
            NonZeroExitProcess(),
            TimeSpan.FromMinutes(1),
            "起不来",
            "超时",
            verdict: _ => null).GetAwaiter().GetResult();
        AssertEqual(true, okRun.ExitCode != 0);

        // 退出码为 0，但 verdict 说产物无效 —— 必须判失败，且用 verdict 给的理由
        var threw = false;
        try
        {
            UnrealProcessRunner.RunAsync(
                ZeroExitProcess(),
                TimeSpan.FromMinutes(1),
                "起不来",
                "超时",
                verdict: _ => "没有生成有效清单").GetAwaiter().GetResult();
        }
        catch (InvalidOperationException error)
        {
            threw = true;
            AssertEqual(true, error.Message.Contains("没有生成有效清单", StringComparison.Ordinal));
        }

        AssertEqual(true, threw);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    static System.Diagnostics.ProcessStartInfo NonZeroExitProcess() => new()
    {
        FileName = "cmd.exe",
        Arguments = "/c exit 3",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    static System.Diagnostics.ProcessStartInfo ZeroExitProcess() => new()
    {
        FileName = "cmd.exe",
        Arguments = "/c exit 0",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
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
    // 勾选由选择树决定，差异服务一律产出未勾选的变更（见上面 publish 那条）。
    AssertEqual(false, import.IsSelected);
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
    // 两栏的主操作文案早就分开了：导入用 ImportPrimaryActionText（「导入所选 N 项」），
    // 发布用 PublishActionText（「同步素材到虚幻」/「同步序列到虚幻」）。
    AssertEqual(true, publishPanel.Descendants().Any(element => element.Attribute("Content")?.Value == "{Binding UnrealProjectSync.PublishActionText, Mode=OneWay}"));
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
    var roots = UnrealSyncSelectionTreeBuilder.FromChanges([safe, conflict], selectPendingByDefault: true);
    var leaves = roots.SelectMany(root => root.Children).ToArray();

    AssertEqual(true, leaves.Single(item => item.StableId == safe.StableId).IsChecked);
    AssertEqual(false, leaves.Single(item => item.StableId == conflict.StableId).IsChecked);
    AssertEqual(true, leaves.Single(item => item.StableId == conflict.StableId).RequiresAttention);
    AssertEqual(false, leaves.Single(item => item.StableId == conflict.StableId).IsSelectable);
    // 状态文案后来从「暂不支持」改成了「需要检查」（另有「重定向」一类），
    // 意思一样：这条要人先看一眼，不能自动执行。上面几条断言钉的是这个语义。
    AssertEqual("需要检查", leaves.Single(item => item.StableId == conflict.StableId).StatusText);
}

static void UnrealSyncSelectionTreeSupportsTriStateSelection()
{
    // 这里测的是父子三态本身，所以用不要求导出文件的模块。
    // 图片和语音要「导出文件存在」才可勾选（没有文件就没东西可导入），
    // 用它们会让本用例的构造数据永远不可勾选，跟三态无关。
    var snapshot = CreateUnrealBridgeSnapshot(
        ("skill:first", UnrealBridgeModule.Skills, "技能一", "first"),
        ("skill:second", UnrealBridgeModule.Skills, "技能二", "second"));
    var root = UnrealSyncSelectionTreeBuilder.FromSnapshot(snapshot).Single();

    AssertEqual<bool?>(true, root.IsChecked);
    AssertEqual(true, root.Children.All(child => child.IsSelectable));

    root.Children[0].IsChecked = false;
    AssertEqual<bool?>(null, root.IsChecked);
    AssertSequence(["skill:second"], UnrealSyncSelectionTreeBuilder.SelectedStableIds([root]).ToArray());

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
    var snapshot = CreateImportableUnrealBridgeSnapshot(
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
    // 检测结果的标题和摘要原来挂在右栏的导入面板上，现在归中栏的统一占位面板管，
    // 所以断言范围要放到整份 XAML，不能再限定在 UnrealImportWorkspacePanel 里。
    var allBindings = string.Join(Environment.NewLine, xaml.Descendants().Attributes().Select(attribute => attribute.Value));
    AssertEqual(true, allBindings.Contains("WorkspacePlaceholderTitle", StringComparison.Ordinal));
    AssertEqual(true, allBindings.Contains("WorkspacePlaceholderDescription", StringComparison.Ordinal));
    // 摘要文本本身还在用：占位面板的「无差异」说明就取自它。
    AssertEqual(true, ReadUnrealSyncViewModelSource().Contains("DetectionResultSummaryText", StringComparison.Ordinal));
}

/// <summary>读同步台视图模型的全部分部文件。</summary>
static string ReadUnrealSyncViewModelSource()
{
    var files = Directory.GetFiles(
        Path.Combine(Directory.GetCurrentDirectory(), "ViewModels"),
        "UnrealProjectSyncViewModel*.cs");
    return string.Join(Environment.NewLine, files.Select(path => File.ReadAllText(path, Encoding.UTF8)));
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

static void BackupSettingGovernsEveryStep()
{
    // 整体设置是唯一开关。以前只要计划里含更新/改名/删除就会绕过设置强制备份，
    // 而第五步必然带删除项 —— 等于这个开关对第五步完全无效：
    // 关着开关点同步，照样先压一份几个 G 的工程出来。
    AssertEqual(
        UnrealBridgeBackupDecision.Skip,
        UnrealBridgeBackupPolicy.Decide(backupEnabledInSettings: false, planTouchesExistingAssets: false));

    // 第五步的典型情形：开关关着，但这一批要删历史资产 —— 依然不备份，只记一条警告。
    AssertEqual(
        UnrealBridgeBackupDecision.SkipWithRiskWarning,
        UnrealBridgeBackupPolicy.Decide(backupEnabledInSettings: false, planTouchesExistingAssets: true));

    // 开关打开就一定备份，跟这批改了什么无关。
    AssertEqual(
        UnrealBridgeBackupDecision.Backup,
        UnrealBridgeBackupPolicy.Decide(backupEnabledInSettings: true, planTouchesExistingAssets: false));
    AssertEqual(
        UnrealBridgeBackupDecision.Backup,
        UnrealBridgeBackupPolicy.Decide(backupEnabledInSettings: true, planTouchesExistingAssets: true));

    // 第五步整批删除项走进来时，计划侧仍然应判定「会动既有资产」，
    // 这样关着开关时才会留下那条警告。
    var sequenceDeletes = new[]
    {
        CreateUnrealBridgeChange(UnrealBridgeChangeKind.DeleteCandidate, isSelected: true)
    };
    AssertEqual(
        UnrealBridgeBackupDecision.SkipWithRiskWarning,
        UnrealBridgeBackupPolicy.Decide(false, UnrealBridgeBackupPolicy.ShouldBackupByDefault(sequenceDeletes)));
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

static UnrealProjectSyncViewModel CreateBlueprintSetupViewModel(int pendingCount)
{
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    viewModel.IsEngineToToolbox = false;
    viewModel.ReturnToWorkflowStep(UnrealSyncWorkflow.MaxStep);
    var items = new List<UnrealBlueprintSetupResultItem>
    {
        new()
        {
            StableId = "bp.icon1p", GroupKey = "onset", GroupName = "对局设置",
            DisplayName = "1P 主战头像", Status = UnrealBlueprintSetupStatus.Unchanged
        }
    };
    for (var index = 0; index < pendingCount; index++)
    {
        items.Add(new UnrealBlueprintSetupResultItem
        {
            StableId = $"bp.seq.Field{index}", GroupKey = "sequence", GroupName = "动作序列",
            DisplayName = $"字段{index}", Status = UnrealBlueprintSetupStatus.Pending,
            CurrentValues = [""], TargetValues = ["x"]
        });
    }

    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items = items
    });
    return viewModel;
}

static void BlueprintSetupApplyButtonEnablesAfterScan()
{
    var viewModel = CreateBlueprintSetupViewModel(3);

    // 检测结果是在「流程操作进行中」的状态下写进来的，那一刻算出来的可用性必然是假。
    // 操作收尾时必须重算一次，否则按钮会一直停在灰色——25 项全勾着也按不下去。
    viewModel.SetWorkflowOperationRunning(true);
    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items =
        [
            new UnrealBlueprintSetupResultItem
            {
                StableId = "bp.anti", GroupKey = "onset", GroupName = "对局设置",
                DisplayName = "异能角色", Status = UnrealBlueprintSetupStatus.Pending,
                CurrentValues = ["false"], TargetValues = ["true"]
            }
        ]
    });
    AssertEqual(false, viewModel.CanApplyBlueprintSetup);

    viewModel.SetWorkflowOperationRunning(false);
    AssertEqual(1, viewModel.BlueprintSetupSelectedCount);
    AssertEqual(true, viewModel.CanApplyBlueprintSetup);
}

static void BlueprintSetupSupportsSelectAllToggle()
{
    // 全选/全不选/反选是六步通用的一组，不再由第六步自己实现一遍。
    var viewModel = CreateBlueprintSetupViewModel(3);
    AssertEqual(3, viewModel.SelectedStepItemCount);
    AssertEqual(3, viewModel.SelectableStepItemCount);
    AssertEqual(true, viewModel.AreAllStepItemsSelected);
    AssertEqual("已选择 3 / 3 项", viewModel.StepSelectionText);

    viewModel.SetStepSelection(false);
    AssertEqual(0, viewModel.SelectedStepItemCount);
    AssertEqual(false, viewModel.AreAllStepItemsSelected);
    AssertEqual(false, viewModel.CanApplyBlueprintSetup);

    viewModel.SetStepSelection(true);
    AssertEqual(3, viewModel.SelectedStepItemCount);
    AssertEqual(true, viewModel.CanApplyBlueprintSetup);

    // 反选：全勾时反选应当变成一个都不勾。
    viewModel.InvertStepSelection();
    AssertEqual(0, viewModel.SelectedStepItemCount);
    viewModel.BlueprintSetupItems[0].IsSelected = true;
    viewModel.InvertStepSelection();
    AssertEqual(2, viewModel.SelectedStepItemCount);

    // 无差异的条目不可勾选，全选不能把它算进来。
    viewModel.SetStepSelection(true);
    AssertEqual(3, viewModel.BlueprintSetupItems.Count);
    AssertSequence(
        ["bp.seq.Field0", "bp.seq.Field1", "bp.seq.Field2"],
        viewModel.GetSelectedBlueprintSetupIds().OrderBy(item => item, StringComparer.Ordinal).ToArray());

    // 第一步没有可勾选的东西，整组按钮该藏起来。
    viewModel.ReturnToWorkflowStep(1);
    AssertEqual(false, viewModel.HasStepSelection);
    AssertEqual(Visibility.Collapsed, viewModel.StepSelectionVisibility);
}

static void RemoteExecutionFallsBackToOffline()
{
    // 编辑器开着时在线执行快约十倍，但它可能正忙着跑别的远程任务、
    // 或者没开远程执行插件。以前这种情况整步直接报错，用户只能自己去
    // 关编辑器再重来；现在自动退回离线。

    // 只有「连不上编辑器」才退回。脚本自己失败退回去也是一样的错。
    AssertEqual(true, UnrealPythonTaskExecutionService.IsRemoteUnavailable(
        new InvalidOperationException(
            $"{UnrealPythonTaskExecutionService.RemoteUnavailableMarker} No running Unreal Editor...")));
    AssertEqual(false, UnrealPythonTaskExecutionService.IsRemoteUnavailable(
        new InvalidOperationException("row struct has no property named Name")));
    AssertEqual(false, UnrealPythonTaskExecutionService.IsRemoteUnavailable(null));
    // 包在里层也要认出来
    AssertEqual(true, UnrealPythonTaskExecutionService.IsRemoteUnavailable(
        new InvalidOperationException("外层", new IOException(
            UnrealPythonTaskExecutionService.RemoteUnavailableMarker))));

    // 远程脚本必须真的带上这个标记，否则上面的判断永远不成立
    var runner = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "run_remote_unreal_job.py"), Encoding.UTF8);
    AssertEqual(true, runner.Contains(UnrealPythonTaskExecutionService.RemoteUnavailableMarker));

    // 走过回退的步骤必须把离线的 StartInfo 一起传进去，否则没得退
    var window = ReadUnrealSyncWindowSource();
    AssertEqual(1, CountOccurrences(window, "private async Task<T> RunUnrealTaskWithOfflineFallbackAsync<T>("));
    AssertEqual(2, CountOccurrences(window, "await RunUnrealTaskWithOfflineFallbackAsync("));
}

static (UnrealProjectSyncViewModel Sync, UnrealSyncWorkflowController Controller, FakeWorkflowHost Host, string Root)
    CreateWorkflowController(int step = 1)
{
    var root = CreateTemporaryTestFolder();
    var projectPath = Path.Combine(root, "CrossingVoid.uproject");
    File.WriteAllText(projectPath, "{}");
    var character = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴") with { IsCompleted = true };
    Directory.CreateDirectory(character.ToolFolderPath);

    var sync = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    sync.Load(Path.Combine(root, "UnrealEditor.exe"), projectPath);
    sync.IsEngineToToolbox = false;
    sync.RefreshDraftSources([character]);
    sync.SelectSource(sync.CharacterSources.Single());
    // 选中角色会顺手跑一遍第一步的本地检测（那一步不碰虚幻，很便宜）。
    // 用例要的是「这一步还没检测过」的起点，先清干净。
    sync.FoundationChecks.Clear();
    sync.ReturnToWorkflowStep(step);

    var host = new FakeWorkflowHost();
    return (sync, new UnrealSyncWorkflowController(sync, host), host, root);
}

static void DetectAllStepsNeverWrites()
{
    // 依次检测只跑检测。第三、五步的同步和第四、六步的写入都会改动 Unreal 工程，
    // 那是要人确认的事，不该被一个按钮顺手做掉。
    //
    // 以前这条只能靠在 MainWindow 源码里搜「有没有出现写入方法的名字」来保证——
    // 改个命名就假报警，真把写入塞进去也未必搜得到。编排搬进控制器之后可以直接
    // 说死：控制器能对界面做的事只有这三件，里面根本没有写入的口子。
    var members = typeof(IUnrealSyncWorkflowHost)
        .GetMethods()
        .Select(method => method.Name)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
    AssertSequence(["Log", "Notify", "RunStepDetectionAsync"], members);

    // 检测必须是可等待的，否则循环会在检测还没跑完时就往下走。
    AssertEqual(typeof(Task), typeof(IUnrealSyncWorkflowHost).GetMethod("RunStepDetectionAsync")!.ReturnType);

    var (_, controller, host, root) = CreateWorkflowController(4);
    try
    {
        var finished = false;
        host.OnDetect = async _ =>
        {
            await Task.Yield();
            finished = true;
        };

        controller.EnterStepAsync(4).GetAwaiter().GetResult();
        // 控制器等到了检测真的跑完才返回
        AssertEqual(true, finished);
        AssertSequence([4], host.DetectedSteps.ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void DetectAllStepsStopsAtFirstBlockedStep()
{
    // 依次检测的价值是把六步的等待一次排完，不是替人做决定：
    // 走到一个还有事要做的步骤就得停下来说清楚卡在哪。
    var (sync, controller, host, root) = CreateWorkflowController(4);
    try
    {
        // 第四步检测完还留着没处理的项 -> 不满足离开条件
        host.OnDetect = _ =>
        {
            sync.SetLightConfigurationResult(new UnrealLightConfigurationResult
            {
                Succeeded = true,
                CharacterCode = "Misaka",
                Items =
                [
                    new UnrealLightConfigurationResultItem
                    {
                        StableId = "item.icon", GroupName = "Item", DisplayName = "道具图标",
                        Status = UnrealLightConfigurationStatus.Pending
                    }
                ],
            });
            return Task.CompletedTask;
        };

        controller.DetectAllStepsAsync().GetAwaiter().GetResult();

        // 只检测了第四步就停住，绝不能顺手把第五、六步也跑掉
        AssertSequence([4], host.DetectedSteps.ToArray());
        AssertEqual(4, sync.WorkflowStep);
        var notice = host.Notices.Single();
        AssertEqual("第四步尚未完成", notice.Title);
        AssertEqual(UnrealSyncNoticeSeverity.Informational, notice.Severity);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void DetectAllStepsStopsOnStepFailure()
{
    // 某一步检测失败还继续往下跑，后面几步都是在错误状态上白等。
    var (sync, controller, host, root) = CreateWorkflowController(4);
    try
    {
        host.OnDetect = _ =>
        {
            sync.SetWorkspaceFailure("导出失败：找不到 Item 资产");
            return Task.CompletedTask;
        };

        controller.DetectAllStepsAsync().GetAwaiter().GetResult();

        AssertSequence([4], host.DetectedSteps.ToArray());
        var notice = host.Notices.Single();
        AssertEqual(UnrealSyncNoticeSeverity.Error, notice.Severity);
        AssertEqual("第 4 步检测失败", notice.Title);
        // 失败原因要带出来，光说「失败了」等于没说
        AssertEqual(true, notice.Message.Contains("找不到 Item 资产", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void EnteringStepNavigatesBeforeDetecting()
{
    // 「进入某一步」的本职是落步，检测只是顺带。两件事绑死的话，
    // 检测失败或被别的操作占用就会把人卡在上一步，界面停在原地却已经跑起了虚幻。
    var (sync, controller, host, root) = CreateWorkflowController();
    try
    {
        var stepWhenDetecting = 0;
        host.OnDetect = _ =>
        {
            stepWhenDetecting = sync.WorkflowStep;
            throw new InvalidOperationException("检测炸了");
        };

        var threw = false;
        try
        {
            controller.EnterStepAsync(4).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        AssertEqual(true, threw);
        // 检测开始时步号已经落到第四步了，不是等检测成功才落
        AssertEqual(4, stepWhenDetecting);
        AssertEqual(4, sync.WorkflowStep);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void WorkflowProgressIsPhasedForEveryStep()
{
    // 以前只有第五步用了分段权重，其余步骤全是写死的百分比（15、45、90…），
    // 而且第四、六步调虚幻的那十几秒进度条完全不动，看着像卡死。

    // 扫描只有一个阶段，不该显示成「阶段 1/1」那种废话。
    var scan = WorkflowProgressPlan.ForStepScan();
    AssertEqual(1, scan.PhaseCount);
    AssertEqual("正在读取", scan.Caption(WorkflowProgressPlan.Scan, "正在读取"));

    // 写入分「写入 + 复查」两段；开了备份就是三段，且备份占大头。
    var apply = WorkflowProgressPlan.ForStepApply(includesBackup: false);
    AssertEqual(2, apply.PhaseCount);
    AssertEqual("阶段 1/2 · 正在写入", apply.Caption(WorkflowProgressPlan.Apply, "正在写入"));

    var backedUp = WorkflowProgressPlan.ForStepApply(includesBackup: true);
    AssertEqual(3, backedUp.PhaseCount);
    AssertEqual(1, backedUp.PhaseNumber(WorkflowProgressPlan.Backup));
    AssertEqual(2, backedUp.PhaseNumber(WorkflowProgressPlan.Apply));
    AssertEqual(3, backedUp.PhaseNumber(WorkflowProgressPlan.Verify));
    // 压一份几个 G 的工程实测约 66 秒，是这条流程里最长的一段，
    // 它就该占掉进度条的大部分，不该被抹平成三等分。
    var backupBand = backedUp[WorkflowProgressPlan.Backup];
    AssertEqual(true, backupBand.End - backupBand.Start > 50);

    // 阶段区间必须首尾相接、不重叠，否则进度条会跳。
    var previousEnd = 0d;
    foreach (var phase in new[] { WorkflowProgressPlan.Backup, WorkflowProgressPlan.Apply, WorkflowProgressPlan.Verify })
    {
        var band = backedUp[phase];
        AssertEqual(true, Math.Abs(band.Start - previousEnd) < 0.001);
        AssertEqual(true, band.End > band.Start);
        previousEnd = band.End;
    }
    // 收尾留了一小段尾巴，不该顶到 100。
    AssertEqual(true, previousEnd is > 90 and < 100);

    // 子进度映射到区间内
    var band2 = backedUp[WorkflowProgressPlan.Apply];
    AssertEqual(true, Math.Abs(band2.At(0) - band2.Start) < 0.001);
    AssertEqual(true, Math.Abs(band2.At(100) - band2.End) < 0.001);
    // 越界的子进度要夹住，不能把进度条推出这一段
    AssertEqual(true, Math.Abs(band2.At(500) - band2.End) < 0.001);

    // 第四、六步的桥接脚本都要回报进度，否则那十几秒还是静止的。
    foreach (var script in new[] { "apply_blueprint_setup.py", "configure_unreal_light_settings.py" })
    {
        var text = File.ReadAllText(Path.Combine(
            Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", script), Encoding.UTF8);
        AssertEqual(true, text.Contains("def _progress("));
        // 光有函数没用，得真的在流程里调
        AssertEqual(true, CountOccurrences(text, "_progress(") >= 4);
    }

    // 界面这一侧要把进度文件转成分段推进
    var window = ReadUnrealSyncWindowSource();
    AssertEqual(2, CountOccurrences(window, "new Progress<UnrealExportProgressState>("));
}

static void BlueprintSetupSelfCheckPasses()
{
    // 第六步的比较逻辑住在 Python 里（要在虚幻进程内跑），C# 这边够不着，
    // 所以带着它自己的自检脚本一起跑。脚本把 unreal 用桩顶掉，不需要引擎。
    var script = Path.Combine("Tools", "UnrealBridge", "tests", "check_blueprint_setup.py");
    AssertEqual(true, File.Exists(script));

    var output = new StringBuilder();
    var exitCode = -1;
    var ran = false;
    foreach (var exe in new[] { "python", "python3", "py" })
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = "\"" + script + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            });
            if (process is null) continue;
            output.Append(process.StandardOutput.ReadToEnd());
            output.Append(process.StandardError.ReadToEnd());
            process.WaitForExit(60_000);
            exitCode = process.ExitCode;
            ran = true;
            break;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 这个名字没装，换下一个
        }
    }

    if (ran)
    {
        if (exitCode != 0)
        {
            throw new InvalidOperationException("蓝图置入自检未通过：\n" + output);
        }

        return;
    }

    // 机器上没有 Python 时不能就这么放过去，退而守住源码层面的不变量，
    // 免得改回按字符串比大小写、或者又把写入失败吞掉。
    var source = File.ReadAllText(Path.Combine("Tools", "UnrealBridge", "apply_blueprint_setup.py"), Encoding.UTF8);
    AssertEqual(true, source.Contains("def _object_values_match", StringComparison.Ordinal));
    AssertEqual(true, source.Contains(".casefold()", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("def _missing_object_targets", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("def _reconcile_applied", StringComparison.Ordinal));
}

static void ReadOnlySequenceCanBeViewedButNotEdited()
{
    // 已完成角色进「查看」模式后看不了序列：把序列送进右侧预览器的那个播放按钮
    // 被整块左栏的 IsHitTestVisible=false 一起挡住了，而右侧预览器自己没有
    // 任何自动选中逻辑，于是没有任何可达路径能看序列。
    // 限制应该只落在「改」上，不该把「看」一起禁掉。
    WithSequenceFrameWorkspace((service, character, action, sourcePaths, voicePath) =>
    {
        service.ImportFrames(character, action, [sourcePaths[0], sourcePaths[1]]);
        var viewModel = new SequenceFramesViewModel(service, new CharacterSkillsService())
        {
            IsReadOnly = true,
        };
        viewModel.LoadAsync(character).GetAwaiter().GetResult();

        var section = viewModel.BaseSectionGroups.SelectMany(group => group.Sections)
            .Single(item => item.Action.Code == action.Code);

        // 看：选中动作后右侧必须真的有帧可放
        viewModel.SelectSection(section);
        AssertEqual(true, viewModel.PreviewFrames.Count > 0);
        AssertEqual(section.Action.Code, viewModel.PreviewSection?.Action.Code);

        // 改：一律拒绝，且要给出可见的理由而不是静默不动
        var before = section.Frames.Count;
        viewModel.DeleteFrameAsync(character, section, section.Frames[0]).GetAwaiter().GetResult();
        AssertEqual(before, section.Frames.Count);
        AssertEqual(true, viewModel.StatusText.Contains("查看模式", StringComparison.Ordinal));

        viewModel.ImportAsync(character, section, [sourcePaths[0]]).GetAwaiter().GetResult();
        AssertEqual(before, section.Frames.Count);
        AssertEqual(0, viewModel.ResolveAllDuplicateFramesAsync(character).GetAwaiter().GetResult());

        // 磁盘上的帧文件一个都不能少
        var frameFiles = Directory.GetFiles(
            Path.Combine(character.FolderPath, "ZDMaterial", action.Code, "Frames"), "*.png");
        AssertEqual(before, frameFiles.Length);
    });
}

static void UnreadableManifestSkipsFramePruning()
{
    // 清理冗余帧时会先收集「所有动作里被清单引用到的帧」。以前某个动作的
    // sequence.json 读不出来（杀软刚扫完、文件被占用、上游非原子写留下的坏文件）
    // 会被当作「这个动作零引用」，于是它名下的帧图全都不在引用集里，
    // 紧接着被删光——用户的原始帧就这么没了，界面上没有任何提示。
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ToolFolderPath);
        var service = new SequenceFrameService();
        var actions = SequenceFrameService.BuildActions(new CharacterSkillsData());
        var first = actions[0];
        var second = actions[1];

        var sourceA = Path.Combine(root, "a.png");
        var sourceB = Path.Combine(root, "b.png");
        var sourceC = Path.Combine(root, "c.png");
        foreach (var path in new[] { sourceA, sourceB, sourceC })
        {
            WriteSolidImage(path, Color.Red, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        }

        service.ImportFrames(character, first, [sourceA, sourceB]);
        service.ImportFrames(character, second, [sourceC]);

        var secondFramesFolder = Path.Combine(
            character.FolderPath, "ZDMaterial", second.Code, "Frames");
        AssertEqual(1, Directory.GetFiles(secondFramesFolder, "*.png").Length);

        // 先把第一个动作读出来（LoadSections 会读全部清单，坏在这一步就命中不了要验的路径）
        var firstSection = LoadSequenceSection(service, character, first);

        // 在第一个动作的帧目录里放一张「清单没引用」的孤儿图。
        // 正常情况下它就是清理的目标，会被删掉——下面用它来观察清理到底跑没跑。
        var firstFramesFolder = Path.Combine(character.FolderPath, "ZDMaterial", first.Code, "Frames");
        var orphanPath = Path.Combine(firstFramesFolder, "orphan.png");
        WriteSolidImage(orphanPath, Color.Blue, SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);

        // 把另一个动作的清单写坏，模拟「读的时候好好的，清理时读不到了」
        var brokenManifest = Path.Combine(character.FolderPath, "ZDMaterial", second.Code, "sequence.json");
        AssertEqual(true, File.Exists(brokenManifest));
        File.WriteAllText(brokenManifest, "{\"Frames\":[");

        // 触发一次会带清理的操作
        service.DeleteFrame(character, first, firstSection.Frames[0]);

        // 引用集不完整时必须整个放弃清理：孤儿图还在，说明没有照着残缺的引用集去删。
        // 修复前这里会把孤儿图删掉——而真实场景里被删的是用户的原始帧。
        AssertEqual(true, File.Exists(orphanPath));
        // 另一个动作的帧当然也一张不少
        AssertEqual(1, Directory.GetFiles(secondFramesFolder, "*.png").Length);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CorruptSettingsAreQuarantinedAndReported()
{
    // settings.json 损坏以前是个闭环：静默返回默认设置 -> 工作区路径回落到默认值
    // -> 界面显示「目录已就绪」，用户的真实工程连同全部角色看起来空了
    // -> 下一次改任何设置调用 Save()，损坏文件被默认值覆盖，丢失变成永久。
    var messages = new List<string>();
    ToolboxLog.SetSink(new CollectingLogSink(messages));
    try
    {
        var service = new AppSettingsService();
        var settingsPath = service.SettingsFilePath;
        var backupPath = settingsPath + ".regression-backup";
        var hadOriginal = File.Exists(settingsPath);
        if (hadOriginal)
        {
            File.Copy(settingsPath, backupPath, overwrite: true);
        }

        try
        {
            Directory.CreateDirectory(service.SettingsDirectoryPath);
            File.WriteAllText(settingsPath, "{ this is not json");

            var loaded = service.Load();
            // 读不出来时用默认设置是可以的，但必须留下痕迹
            AssertEqual(true, loaded is not null);
            AssertEqual(true, messages.Any(text => text.Contains("设置文件读取失败", StringComparison.Ordinal)));
            // 坏文件要被改名留档，否则下一次保存就把证据盖掉了
            AssertEqual(false, File.Exists(settingsPath));
            var quarantined = Directory.GetFiles(service.SettingsDirectoryPath, "settings.json.corrupt-*");
            AssertEqual(true, quarantined.Length >= 1);
            foreach (var path in quarantined)
            {
                File.Delete(path);
            }
        }
        finally
        {
            if (hadOriginal)
            {
                File.Copy(backupPath, settingsPath, overwrite: true);
                File.Delete(backupPath);
            }
            else if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }
    finally
    {
        ToolboxLog.SetSink(null);
    }
}

static void CancellingUnrealRunKillsTheProcess()
{
    // 这是整次进程编排重构的核心行为。以前四处都把
    //     if (ct.IsCancellationRequested) KillProcessTree(...)
    // 写在轮询循环顶部，可取消几乎总是落在 await Task.Delay(..., ct) 里直接抛出，
    // 那一句永远轮不到；而 using var process 的 Dispose 并不会结束进程。
    // 结果就是 UnrealEditor-Cmd.exe 变成孤儿，继续占着工程锁，下一次同步起不来。
    var root = CreateTemporaryTestFolder();
    try
    {
        // 子进程先等几秒再写这个文件。取消之后它要是还活着，文件迟早会出现——
        // 用「文件有没有出现」判，比观察某个文件还在不在长要干脆得多。
        var survivedPath = Path.Combine(root, "survived.txt");
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c ping -n 5 127.0.0.1 > nul & echo survived > \"{survivedPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var cancellation = new CancellationTokenSource();
        var run = UnrealProcessRunner.RunAsync(
            startInfo,
            TimeSpan.FromMinutes(2),
            "起不来",
            "超时",
            cancellationToken: cancellation.Token);

        Thread.Sleep(600);
        // 还没到写文件的时候，此刻取消
        AssertEqual(false, File.Exists(survivedPath));
        cancellation.Cancel();

        var cancelled = false;
        try
        {
            run.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        AssertEqual(true, cancelled);

        // 关键断言：等过子进程本来会写文件的时刻，它必须已经被杀掉了。
        // 没杀掉的话这个文件会出现——那就是「孤儿进程还在跑」。
        Thread.Sleep(6000);
        AssertEqual(false, File.Exists(survivedPath));
    }
    finally
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // 子进程真没杀掉的话这里会删不动——上面的断言已经报出来了
        }
    }
}

static void RedirectedProcessOutputAlwaysFixesEncoding()
{
    // 中文 Windows 的控制台代码页是 936，而这些子进程写的是 UTF-8
    // （UnrealPythonTaskExecutionService 还显式设了 PYTHONUTF8=1），
    // 不固定 StandardOutputEncoding 就必然乱码——偏偏这些输出只在出故障时才会被人翻出来看。
    foreach (var path in Directory.EnumerateFiles("Services", "*.cs", SearchOption.AllDirectories))
    {
        var source = File.ReadAllText(path, Encoding.UTF8);
        if (!source.Contains("RedirectStandardOutput", StringComparison.Ordinal))
        {
            continue;
        }

        if (!source.Contains("StandardOutputEncoding", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{path} 重定向了子进程输出但没有固定编码，中文诊断信息会乱码。");
        }
    }
}

static void TechnicalDebtRatchetOnlyGoesDown()
{
    // 棘轮护栏。这几个数字是「当前有多糟」的快照，用例只保证它们不再变糟。
    //
    // 为什么不是「一刀切禁止」：裸 catch 有 22 处、MainWindow 最大分部 2335 行，
    // 要求一次清零只会让人把用例删掉。棘轮能真正落地——每次顺手改好一处，
    // 就把上限往下调一格，退步则立刻红。
    //
    // 改好之后请把这里的数字调低，别只是让它继续过。
    var violations = new List<string>();

    void Ratchet(string what, int actual, int ceiling)
    {
        if (actual > ceiling)
        {
            violations.Add($"{what}：{actual} 超过上限 {ceiling}");
        }
    }

    // 1) Services 层的裸 catch（没有异常类型、也没有 when 过滤）。
    //    每一处都是「悄悄吞掉，出问题只能靠猜」的候选。
    var bareCatches = 0;
    foreach (var path in Directory.EnumerateFiles("Services", "*.cs", SearchOption.AllDirectories))
    {
        bareCatches += File.ReadAllLines(path, Encoding.UTF8)
            .Count(line => line.Trim() == "catch");
    }

    Ratchet("Services 层裸 catch", bareCatches, 20);

    // 2) MainWindow 分部的体量。规约明写着「别把项目养成一个超大的 MainWindow」，
    //    但没有任何机制拦住它长大。
    var largestPartial = Directory.EnumerateFiles(".", "MainWindow*.cs")
        .Select(path => File.ReadAllLines(path, Encoding.UTF8).Length)
        .DefaultIfEmpty(0)
        .Max();
    Ratchet("MainWindow 最大分部行数", largestPartial, 2335);

    // 3) 单文件 XAML。九个页面加十二个自建遮罩层全挤在这一个文件里。
    Ratchet("MainWindow.xaml 行数", File.ReadAllLines("MainWindow.xaml", Encoding.UTF8).Length, 5522);

    // 4) Services 最大单文件。
    var largestService = Directory.EnumerateFiles("Services", "*.cs", SearchOption.AllDirectories)
        .Select(path => File.ReadAllLines(path, Encoding.UTF8).Length)
        .DefaultIfEmpty(0)
        .Max();
    // UnrealProjectSyncService 一个类干十件事，这个数字贴着上限。
    // 下一步是把它里面 1130 行零 IO 的纯函数抽出去（预览投影 + 素材分类），
    // 那两段没有任何外部调用方，抽出即降到约 2050——方案见 Plan/02-重构方案.md 的 P2。
    Ratchet("Services 最大单文件行数", largestService, 3126);

    // 5) 断言源码文本的用例数。这类断言查的是变量名和换行位置，
    //    改个命名就假报警，却拦不住逻辑写错——而且它们把反模式固化住了
    //    （有一条直接断言 Click="XxxButton_Click"，等于规定不许改成 Command 绑定）。
    //    只许往下走。
    var ownSource = File.ReadAllText(
        Path.Combine("Tests", "CrossingVoidZDTool.RegressionTests", "Program.cs"), Encoding.UTF8);
    // 搜索串拆开拼，免得这一行把自己也算进去
    var marker = "File.ReadAllText(Path.Combine(Directory." + "GetCurrentDirectory()";
    var sourceTextAssertions = ownSource.Split(marker).Length - 1;
    Ratchet("断言源码文本的用例", sourceTextAssertions, 50);

    if (violations.Count > 0)
    {
        throw new InvalidOperationException(
            "技术债棘轮退步了：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}

static (CharacterCard Character, CharacterInfoData Info, CharacterSkillsData Skills) BuildBlueprintSetupFixture(string root)
{
    var character = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴");
    Directory.CreateDirectory(character.ToolFolderPath);
    var info = new CharacterInfoData { Code = "Misaka", Name = "御坂美琴", FormLimit = 1 };
    var skills = new CharacterSkillsData();
    skills.FirstSkill.Add(new CharacterSkillEntry { TrueName = "超电磁炮" });
    return (character, info, skills);
}

static void UnparsableSkillNumbersAreReportedNotZeroed()
{
    // 第六步以前对解析不了的数值一律 `: 0d` —— 技能倍率、护援值被静默写成 0，
    // 扫描时看不出任何异常。又一例「显示成功但实际没做成」。
    var root = CreateTemporaryTestFolder();
    try
    {
        var (character, info, skills) = BuildBlueprintSetupFixture(root);
        var service = new UnrealBlueprintSetupService();

        // 空文本是常态（形态没填满），必须仍然当 0，不能报错
        skills.FirstSkill[0].GuardValue = string.Empty;
        service.BuildRequest(character, info, skills, []);

        // 非空但解析不出来，必须抛，且要点名是哪个字段、原文是什么
        skills.FirstSkill[0].GuardValue = "1,5";
        var threw = false;
        try
        {
            service.BuildRequest(character, info, skills, []);
        }
        catch (Exception error)
        {
            threw = true;
            AssertEqual(true, error.Message.Contains("1,5", StringComparison.Ordinal));
            AssertEqual(true, error.Message.Contains("Misaka", StringComparison.Ordinal));
        }

        AssertEqual(true, threw);

        // 带单位的文本同理（技能编辑框不做数字校验，用户确实填得进去）
        skills.FirstSkill[0].GuardValue = "1.5倍";
        var threwAgain = false;
        try
        {
            service.BuildRequest(character, info, skills, []);
        }
        catch (Exception)
        {
            threwAgain = true;
        }

        AssertEqual(true, threwAgain);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SkillNumbersSurviveCommaDecimalCulture()
{
    // 格式化用当前区域、解析用不变区域，这个不对称在小数点是逗号的区域下
    // 必然坏掉：格式化写出 "1,5"，解析读不了，旧代码静默归 0。
    // 这条用例一次钉死整族——不去逐个断言某处有没有写 InvariantCulture，
    // 而是直接换个区域跑一遍往返。
    var original = System.Globalization.CultureInfo.CurrentCulture;
    var root = CreateTemporaryTestFolder();
    try
    {
        System.Globalization.CultureInfo.CurrentCulture =
            System.Globalization.CultureInfo.GetCultureInfo("de-DE");

        var (character, info, skills) = BuildBlueprintSetupFixture(root);

        // 走完整往返：Unreal 侧的数值 -> 回填成文本 -> 存进角色数据 -> 第六步再解析回去。
        // 格式化用当前区域、解析用不变区域这个不对称，正是在这里断掉的。
        var formatted = UnrealProjectSyncService.FormatDouble(1.5);
        AssertEqual("1.5", formatted);
        skills.FirstSkill[0].GuardValue = formatted;

        // 在德语区域下拼请求：数值必须原样是 1.5，而不是被吞成 0
        var request = new UnrealBlueprintSetupService().BuildRequest(character, info, skills, []);
        var payload = JsonSerializer.Serialize(
            request, UnrealBlueprintSetupJsonContext.Default.UnrealBlueprintSetupRequest);

        // 落到载荷里的必须是英文句点写法，Python 侧才读得回来
        AssertEqual(true, payload.Contains("1.5", StringComparison.Ordinal));
        AssertEqual(false, payload.Contains("1,5", StringComparison.Ordinal));
    }
    finally
    {
        System.Globalization.CultureInfo.CurrentCulture = original;
        Directory.Delete(root, recursive: true);
    }
}

static void DiagnosticItemsDoNotCountAsSucceededActions()
{
    // 序列同步的结果里混着一条 orphan-sequences 的诊断条目（孤儿序列解绑），
    // 它不对应任何动作。以前它被算进「已成功的动作」，于是所有真实动作都失败时，
    // 「一个都没成功就抛」的判断失效——界面报「已成功 1 个动作并写入基线」，
    // 那个假 ID 还会被拿去生成基线条目。
    var allFailed = new[]
    {
        new UnrealBridgeExecutionItemResult
        {
            StableId = "orphan-sequences", Succeeded = true, ItemKind = "diagnostic",
        },
        new UnrealBridgeExecutionItemResult
        {
            StableId = SequenceFrameIdentity.BuildActionStableId("Click"),
            Succeeded = false, ItemKind = "action",
        },
    };
    AssertEqual(0, UnrealBridgeExecutionItemResult.SelectSucceededActionStableIds(allFailed).Length);

    // 真实动作成功了当然要算
    var mixed = new[]
    {
        new UnrealBridgeExecutionItemResult
        {
            StableId = "orphan-sequences", Succeeded = true, ItemKind = "diagnostic",
        },
        new UnrealBridgeExecutionItemResult
        {
            StableId = "action:click", Succeeded = true, ItemKind = "action",
        },
    };
    AssertSequence(["action:click"], UnrealBridgeExecutionItemResult.SelectSucceededActionStableIds(mixed));

    // 别的脚本写出的结果不带 itemKind，缺省必须当成动作，否则会被整批滤掉
    var legacy = new[]
    {
        new UnrealBridgeExecutionItemResult { StableId = "material:1", Succeeded = true },
    };
    AssertEqual("action", legacy[0].ItemKind);
    AssertSequence(["material:1"], UnrealBridgeExecutionItemResult.SelectSucceededActionStableIds(legacy));

    // 空 StableId 的条目也不算
    var blank = new[]
    {
        new UnrealBridgeExecutionItemResult { StableId = string.Empty, Succeeded = true },
    };
    AssertEqual(0, UnrealBridgeExecutionItemResult.SelectSucceededActionStableIds(blank).Length);
    AssertEqual(0, UnrealBridgeExecutionItemResult.SelectSucceededActionStableIds(null).Length);
}

static void ReplacingSettingsNotifiesDerivedProperties()
{
    // SettingsViewModel 里逐项修改的 setter 都记得通知，唯独「整份 _settings 被换掉」
    // 这条路径没有——于是重新加载设置后，界面上的当前角色、上次编辑位置、
    // 引擎与工程路径会停在旧值。
    var viewModel = new SettingsViewModel(new AppSettingsService(), new ProjectRootMigrationService());
    var changed = new List<string>();
    viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

    viewModel.LoadAndEnsureProjectRoot();

    foreach (var name in new[]
             {
                 nameof(SettingsViewModel.CurrentCharacterCode),
                 nameof(SettingsViewModel.LastEditedCharacterCode),
                 nameof(SettingsViewModel.LastEditedModuleTag),
                 nameof(SettingsViewModel.UnrealEnginePath),
                 nameof(SettingsViewModel.UnrealProjectPath),
             })
    {
        if (!changed.Contains(name))
        {
            throw new InvalidOperationException($"重新加载设置后没有通知 {name}");
        }
    }
}

static void ProcessOrchestrationIsNotDuplicated()
{
    // 起进程、轮询、杀进程树这套编排原本在四个服务里各抄了一份，
    // 除了轮询间隔各不相同之外还共享同样的两个坑（取消杀不掉、读到上一轮结果）。
    // 收敛到 UnrealProcessRunner 之后，这条守卫挡住「下次又各自抄一份」。
    string[] shouldNotOrchestrate =
    [
        Path.Combine("Services", "UnrealProjectSyncService.cs"),
        Path.Combine("Services", "UnrealBlueprintSetupService.cs"),
        Path.Combine("Services", "UnrealBridgeExecutorService.cs"),
        Path.Combine("Services", "UnrealLightConfigurationService.cs"),
        Path.Combine("Services", "UnrealAssetBrowseService.cs"),
    ];

    foreach (var path in shouldNotOrchestrate)
    {
        var source = File.ReadAllText(path, Encoding.UTF8);
        foreach (var forbidden in new[] { "KillProcessTree", "entireProcessTree", "Process.Start" })
        {
            if (source.Contains(forbidden, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{path} 又自己编排进程了（出现 {forbidden}），应该走 UnrealProcessRunner。");
            }
        }
    }
}

static void AtomicWriteLeavesNoTemporaryFile()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var path = Path.Combine(root, "data.json");
        AtomicFileWriter.WriteAllText(path, "{\"a\":1}");
        AssertEqual("{\"a\":1}", File.ReadAllText(path));

        // 覆盖写：目标始终存在，且不留临时文件
        AtomicFileWriter.WriteAllText(path, "{\"a\":2}");
        AssertEqual("{\"a\":2}", File.ReadAllText(path));
        AssertEqual(0, Directory.GetFiles(root, "*.tmp").Length);

        // 临时名带 GUID，两次写入不会撞同一个临时文件
        AssertEqual(1, Directory.GetFiles(root).Length);

        // 写不进去时要如实抛出，并且不留半截临时文件
        var directoryAsTarget = Path.Combine(root, "occupied");
        Directory.CreateDirectory(directoryAsTarget);
        var threw = false;
        try
        {
            AtomicFileWriter.WriteAllText(directoryAsTarget, "x");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            threw = true;
        }

        AssertEqual(true, threw);
        AssertEqual(0, Directory.GetFiles(root, "*.tmp").Length);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void VoicePathPolicyBuildsCanonicalFolder()
{
    // UnrealBridgeVoicePathPolicy 是纯策略、零 IO，却一直没有任何测试覆盖，
    // 而第三步语音改名的目标路径全靠它算。
    const string current = "/Game/GameActor2D/Misaka/Sound/Other/Misaka-Defeat-1.Misaka-Defeat-1";

    AssertEqual(true, UnrealBridgeVoicePathPolicy.TryBuildCanonicalObjectPath(
        current, "{\"kind\":\"Defeat\"}", "Misaka-Defeat-1", out var defeatPath));
    AssertEqual(
        "/Game/GameActor2D/Misaka/Sound/Defeat/Misaka-Defeat-1.Misaka-Defeat-1",
        defeatPath);

    // 新加的音效分类要能落到自己的目录
    AssertEqual(true, UnrealBridgeVoicePathPolicy.TryBuildCanonicalObjectPath(
        current, "{\"kind\":\"SoundEffect\"}", "Misaka-SE-1", out var sePath));
    AssertEqual(
        "/Game/GameActor2D/Misaka/Sound/SoundEffect/Misaka-SE-1.Misaka-SE-1",
        sePath);

    // 连字符是合法的 Unreal 资产名字符，规范路径里必须原样保留
    // （CONTEXT.md 的「语音规范名称」明确要求，不能改写成下划线）
    AssertEqual(true, defeatPath.Contains("Misaka-Defeat-1", StringComparison.Ordinal));

    // 认不出分类就不给目标路径，让调用方走原有的兜底
    AssertEqual(false, UnrealBridgeVoicePathPolicy.TryBuildCanonicalObjectPath(
        current, "{}", "Misaka-Defeat-1", out _));
    AssertEqual(false, UnrealBridgeVoicePathPolicy.TryBuildCanonicalObjectPath(
        current, "{\"kind\":\"Defeat\"}", string.Empty, out _));

    // 路径里没有 /Sound/ 段时同样不给结论
    AssertEqual(false, UnrealBridgeVoicePathPolicy.TryBuildCanonicalObjectPath(
        "/Game/GameActor2D/Misaka/AssetMaterial/X.X", "{\"kind\":\"Defeat\"}", "X", out _));
}

static void VoiceClassificationIgnoresCharacterCodeNoise()
{
    const string sound = "/Game/GameActor2D/Misaka/Sound/Other";

    // 失败语音的常见拼法都要认得。以前 token 只有 defeat/lose/failure，
    // Vo_Fail1 这种识别不出来，会掉进待分配。
    foreach (var assetName in new[] { "Vo_Defeat1", "Vo_Defeated1", "Vo_Lose1", "Vo_Fail1", "Vo_Lost1" })
    {
        AssertEqual(
            VoiceMaterialKind.Defeat,
            UnrealBridgeVoiceClassification.Classify(sound, assetName));
    }

    // ko / sub 这类两三个字母的 token 排在失败语音前面，
    // 以前做子串匹配会被角色代号整批误伤：代号里带 ko 的角色（Kokona、Nakoruru）
    // 所有语音都会被判成终结技语音。
    foreach (var code in new[] { "Kokona", "Nakoruru", "Subaru", "Linkle", "Steam" })
    {
        AssertEqual(
            VoiceMaterialKind.Defeat,
            UnrealBridgeVoiceClassification.Classify($"/Game/GameActor2D/{code}/Sound/Other", "Vo_Defeat1"));
    }

    // 真正该命中的短 token 仍要命中
    AssertEqual(VoiceMaterialKind.Ultimate, UnrealBridgeVoiceClassification.Classify(sound, "Vo_KO_1"));
    AssertEqual(VoiceMaterialKind.Support, UnrealBridgeVoiceClassification.Classify(sound, "Vo-Sub-1"));
    AssertEqual(VoiceMaterialKind.SoundEffect, UnrealBridgeVoiceClassification.Classify(sound, "Vo_SE_1"));
    AssertEqual(VoiceMaterialKind.SoundEffect, UnrealBridgeVoiceClassification.Classify(sound, "Misaka_SoundEffect_1"));

    // 认不出来的仍然进待分配
    AssertEqual(VoiceMaterialKind.Other, UnrealBridgeVoiceClassification.Classify(sound, "Vo_Unknown_1"));
}

static void VoiceSpecsMatchBridgeScriptLabels()
{
    // 语音分类在 C# 和 Python 两边各有一份表，而且一条漂移校验都没有——
    // 实际已经漂了（入队语音/编队语音、受击语音/受伤语音）。这里钉住它。
    var scriptPath = Path.Combine("Tools", "UnrealBridge", "configure_unreal_light_settings.py");
    var script = File.ReadAllText(scriptPath, Encoding.UTF8);
    var start = script.IndexOf("VOICE_CATEGORY_LABELS = {", StringComparison.Ordinal);
    AssertEqual(true, start >= 0);
    var end = script.IndexOf("}", start, StringComparison.Ordinal);
    AssertEqual(true, end > start);
    var body = script[start..end];

    foreach (var spec in VoiceMaterialService.Specs)
    {
        var expected = $"\"{spec.Kind}\": \"{spec.DisplayName}\"";
        if (!body.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"桥接脚本的语音标签与 VoiceMaterialService.Specs 不一致，缺少或写错：{expected}");
        }
    }
}

static void CorruptMetadataKeepsCharacterVisible()
{
    // character.json 坏掉时（写一半崩溃、被外部工具改坏），以前扫描直接跳过，
    // 这个角色就从角色台上凭空消失——用户会以为素材全丢了，
    // 而磁盘上的图片语音其实一个没少。
    var root = CreateTemporaryTestFolder();
    try
    {
        var service = new CharacterWorkspaceService();
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Draft"), "Misaka", "御坂美琴", isCompleted: false);
        CreateWorkspaceCharacterFolder(Path.Combine(root, "Draft"), "ALO_Yuki", "结衣", isCompleted: false);
        AssertEqual(2, service.LoadCharacters(root).Count);

        // 把其中一个的元数据写成半截 JSON
        var broken = Path.Combine(root, "Draft", "Misaka", "tool", "character.json");
        File.WriteAllText(broken, "{\"Code\":\"Misa");

        var cards = service.LoadCharacters(root);
        // 角色必须还在，不能凭空消失
        AssertEqual(2, cards.Count);
        var recovered = cards.Single(card =>
            string.Equals(card.Code, "Misaka", StringComparison.OrdinalIgnoreCase));
        // 兜底卡片用目录名当代号，完成状态取自它所在的目录
        AssertEqual(false, recovered.IsCompleted);
        AssertEqual(true, Directory.Exists(recovered.FolderPath));

        // 完全没有元数据文件的目录仍然不算角色
        Directory.CreateDirectory(Path.Combine(root, "Draft", "随手建的空目录"));
        AssertEqual(2, service.LoadCharacters(root).Count);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CharacterDataWriteIsAtomic()
{
    // 这个函数名叫 WriteAllTextAtomic，但以前是「写临时文件 -> 删掉目标 -> 移过去」，
    // 删和移之间崩溃就等于角色的技能、BUFF、信息整份消失。
    // 没法真的在中途杀进程，退而验证两件可观察的事：
    // 目标文件在整个过程中从不消失，且不会留下固定名的临时文件让并发写互相踩。
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ToolFolderPath);
        var service = new CharacterToolboxDataService();

        service.Update(character, data => data.Skills = new CharacterSkillsData());
        var dataPath = Path.Combine(character.ToolFolderPath, "ZDToolboxData.json");
        AssertEqual(true, File.Exists(dataPath));

        var workspace = new CharacterWorkspaceService();
        for (var round = 0; round < 5; round++)
        {
            workspace.SaveDraft(character, "草稿第 " + round + " 轮");
            // 每一轮之后目标文件都必须在，且内容完整可读
            AssertEqual(true, File.Exists(dataPath));
            AssertEqual("草稿第 " + round + " 轮", workspace.LoadDraft(character));
        }

        // 不许残留临时文件
        var leftovers = Directory.GetFiles(character.ToolFolderPath, "*.tmp");
        AssertEqual(0, leftovers.Length);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void ReclassifiedVoiceSurvivesBaselineFilter()
{
    // 用户报的：在工具箱里把一条语音从「待分配」归到「失败语音」，
    // 第三步却怎么都同步不上去，Unreal 里那条一直躺在 Sound/Other。
    //
    // 差异服务其实判对了（Renamed，目标 Sound/Defeat），丢失发生在它之上：
    // 恢复分步缓存时会拿基线把「已经同步过的」剔掉，而那个判定只比内容哈希。
    // 改分类时文件字节没变、Unreal 那侧也没被动过，两个哈希都和基线一致，
    // 于是这条 Renamed 被整条抹掉——界面还会因此报「全部素材无差异」。
    var root = CreateTemporaryTestFolder();
    try
    {
        var projectPath = Path.Combine(root, "CrossingVoid.uproject");
        File.WriteAllText(projectPath, "{}");
        var enginePath = Path.Combine(root, "UnrealEditor.exe");
        File.WriteAllText(enginePath, "x");
        var character = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴") with { IsCompleted = true };
        Directory.CreateDirectory(character.ToolFolderPath);

        const string stableId = "voice:shared-sync-id";
        const string oldRelative = "Sound/Other/Misaka-Defeat-1.wav";
        const string newRelative = "Sound/Defeat/Misaka-Defeat-1.wav";
        const string unrealPath = "/Game/GameActor2D/Misaka/Sound/Other/Misaka-Defeat-1.Misaka-Defeat-1";

        // 工具箱侧已经归到 Defeat；Unreal 侧还在 Other。字节没变，所以两边哈希都和基线一致。
        var toolboxItem = new UnrealBridgeSnapshotItem(
            stableId, "module:Voices", UnrealBridgeModule.Voices, "失败语音 #1",
            "wave-bytes-hash", "{\"kind\":\"Defeat\"}",
            Path.Combine(character.FolderPath, "Sound", "Defeat", "Misaka-Defeat-1.wav"),
            ToolboxRelativePath: newRelative,
            NormalizedName: "Misaka-Defeat-1");
        var unrealItem = new UnrealBridgeSnapshotItem(
            stableId, "module:Voices", UnrealBridgeModule.Voices, "Misaka-Defeat-1",
            "unreal-preview-hash", "{}", string.Empty,
            SourceObjectPath: unrealPath,
            OriginIdentity: "package-guid",
            NormalizedName: "Misaka-Defeat-1");
        var change = new UnrealBridgeChange(
            stableId, UnrealBridgeModule.Voices, "失败语音 #1",
            UnrealBridgeChangeKind.Renamed, toolboxItem, unrealItem, true);

        // 基线记的是改分类之前的状态：路径还是 Other，哈希和现在一模一样
        new UnrealBridgeStateService().Save(character, projectPath, new UnrealBridgeSyncState
        {
            CharacterCode = character.Code,
            UnrealProjectPath = projectPath,
            Entries =
            {
                [stableId] = new UnrealBridgeSyncStateEntry(
                    "wave-bytes-hash",
                    "unreal-preview-hash",
                    unrealPath,
                    "package-guid",
                    oldRelative,
                    "Misaka-Defeat-1"),
            },
        });

        // 先把这条差异写进第三步的分步缓存
        var writer = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
        writer.Load(enginePath, projectPath);
        writer.IsEngineToToolbox = false;
        writer.RefreshDraftSources([character]);
        writer.SelectSource(writer.CharacterSources.Single());
        writer.ReturnToWorkflowStep(3);
        writer.SetPublishSelectionTree(
            UnrealSyncSelectionTreeBuilder.FromChanges(
                [change], UnrealBridgePublishSupportPolicy.CanExecute, selectPendingByDefault: true),
            [change]);
        writer.FlushSessionCache();

        // 换一个视图模型从缓存恢复——这正是切角色/切步骤时走的那条路
        var reader = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
        reader.Load(enginePath, projectPath);
        reader.IsEngineToToolbox = false;
        reader.RefreshDraftSources([character]);
        reader.SelectSource(reader.CharacterSources.Single());

        var restored = reader.SelectionTreeRoots
            .SelectMany(item => item.Children)
            .Select(item => item.StableId)
            .ToArray();
        AssertSequence([stableId], restored);
        // 只剩这一条差异时，绝不能对外宣称「全部素材无差异」
        AssertEqual(false, reader.HasNoPublishChanges);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SingleItemSelectionRefreshesStepSelectionText()
{
    // 右栏那句「已选择 N / M 项」是按步骤算的，和导入摘要不是一回事。
    // 第四、六步的单项勾选都记得通知它，唯独第三、五步这条路径漏了：
    // 逐个勾选素材时计数一直卡在旧值，要点全选或切步骤才跳回来。
    const string actionCode = "Sk1";
    var changes = new List<UnrealBridgeChange>
    {
        // 树是「动作根 + 帧叶子」两层，少了根节点就建不出子项
        new(
            SequenceFrameIdentity.BuildActionStableId(actionCode),
            UnrealBridgeModule.SequenceFrames,
            actionCode,
            UnrealBridgeChangeKind.Unchanged,
            null,
            null,
            false,
            SequenceFrameIdentity.BuildActionStableId(actionCode)),
    };
    for (var frame = 0; frame < 3; frame++)
    {
        changes.Add(CreateSequenceDeleteChange(
            actionCode,
            "/Game/GameActor2D/Misaka/Material/" + actionCode + "/F" + frame + ".F" + frame));
    }

    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    // 计数是按当前步骤算的，默认停在第一步会去数底层检测项
    viewModel.ReturnToWorkflowStep(5);
    var roots = UnrealSyncSelectionTreeBuilder.FromSequenceChanges(
        changes, UnrealBridgePublishSupportPolicy.CanExecute, selectPendingByDefault: false);
    viewModel.SetPublishSelectionTree(roots, changes);
    AssertEqual(true, viewModel.IsWorkflowStepLoaded(5));

    var leaf = viewModel.SelectionTreeRoots.SelectMany(root => root.Children).First();
    AssertEqual(false, leaf.IsChecked);
    var before = viewModel.SelectedStepItemCount;

    var changed = new List<string>();
    viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
    leaf.IsChecked = true;

    // 计数本身要变
    AssertEqual(before + 1, viewModel.SelectedStepItemCount);
    // 而且必须真的通知出去——否则界面上那行字不会动
    AssertEqual(true, changed.Contains(nameof(UnrealProjectSyncViewModel.StepSelectionText)));
    AssertEqual(true, changed.Contains(nameof(UnrealProjectSyncViewModel.SelectedStepItemCount)));
    AssertEqual(true, changed.Contains(nameof(UnrealProjectSyncViewModel.AreAllStepItemsSelected)));
}

static void ResetImportOperationRefreshesWorkspaceAndWorkflow()
{
    // 重置导入操作会清掉 _hasImportDetection 和 _loadedPublishStep，
    // 中栏状态、能否进下一步、发布勾选是否就绪全都跟着变。
    // 但它不动任何 ObservableCollection，所以中栏的集合监听在这条路径上
    // 不会触发——不显式广播的话，中栏会停在上一刻的可见性上。
    var changes = new List<UnrealBridgeChange>
    {
        new(
            SequenceFrameIdentity.BuildActionStableId("Click"),
            UnrealBridgeModule.SequenceFrames,
            "Click",
            UnrealBridgeChangeKind.Unchanged,
            null,
            null,
            false,
            SequenceFrameIdentity.BuildActionStableId("Click")),
        CreateSequenceDeleteChange("Click", "/Game/GameActor2D/Misaka/Material/Click/F0.F0"),
    };

    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    viewModel.IsEngineToToolbox = true;
    var roots = UnrealSyncSelectionTreeBuilder.FromSequenceChanges(
        changes, UnrealBridgePublishSupportPolicy.CanExecute, selectPendingByDefault: true);
    viewModel.SetPublishSelectionTree(roots, changes);
    viewModel.SetLoadedPublishStep(3);
    AssertEqual(true, viewModel.HasContentDetection);

    var changed = new List<string>();
    viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

    // 导入方向 + 空树 -> 走到 ResetImportOperation
    viewModel.SetPublishSelectionTree([], []);
    viewModel.FailImportDetection("检测失败");

    AssertEqual(false, viewModel.HasContentDetection);
    AssertEqual(false, viewModel.IsWorkflowStepLoaded(3));
    // 中栏必须被通知到，否则占位和内容可能双双隐藏
    AssertEqual(true, changed.Contains(nameof(UnrealProjectSyncViewModel.WorkspaceState)));
    AssertEqual(true, changed.Contains(nameof(UnrealProjectSyncViewModel.WorkspacePlaceholderVisibility)));
    // 流程可用性也要被通知到
    AssertEqual(true, changed.Contains(nameof(UnrealProjectSyncViewModel.CanAdvanceWorkflow)));
    AssertEqual(true, changed.Contains(nameof(UnrealProjectSyncViewModel.WorkflowNextButtonEnabled)));
}

static void WorkspaceReactsToRawCollectionChanges()
{
    // 中栏空白反复出现的根因是「谁改了数据、谁负责通知」这条约定守不住：
    // 六步各有若干条清空/填充路径，漏一条面板就停在上一刻的可见性上。
    // 这里绕开所有 SetXxxResult，直接动集合——中栏必须自己反应过来。
    var root = CreateTemporaryTestFolder();
    try
    {
        var projectPath = Path.Combine(root, "CrossingVoid.uproject");
        File.WriteAllText(projectPath, "{}");
        var character = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴") with { IsCompleted = true };
        Directory.CreateDirectory(character.ToolFolderPath);

        var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
        viewModel.Load(Path.Combine(root, "UnrealEditor.exe"), projectPath);
        viewModel.IsEngineToToolbox = false;
        viewModel.RefreshDraftSources([character]);
        viewModel.SelectSource(viewModel.CharacterSources.Single());

        // 每一步：先声明这一步已加载（空 -> 无差异），再直接往集合里塞一条。
        var steps = new (int Step, Action MarkLoaded, Action Add, Func<Visibility> Content)[]
        {
            (4,
                () => viewModel.SetLightConfigurationResult(new UnrealLightConfigurationResult
                {
                    Succeeded = true, CharacterCode = character.Code, Items = []
                }),
                () => viewModel.LightConfigurationItems.Add(new UnrealLightConfigurationViewItem(
                    new UnrealLightConfigurationResultItem
                    {
                        StableId = "item.icon", GroupName = "Item", DisplayName = "道具图标",
                        Status = UnrealLightConfigurationStatus.Pending
                    }, true)),
                () => viewModel.LightConfigurationWorkspaceVisibility),
            (6,
                () => viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
                {
                    Succeeded = true, CharacterCode = character.Code, Items = []
                }),
                () => viewModel.BlueprintSetupItems.Add(new UnrealBlueprintSetupViewItem(
                    new UnrealBlueprintSetupResultItem
                    {
                        StableId = "bp.anti", GroupKey = "onset", GroupName = "对局设置",
                        DisplayName = "异能角色", Status = UnrealBlueprintSetupStatus.Pending
                    }, true)),
                () => viewModel.BlueprintSetupWorkspaceVisibility),
        };

        foreach (var (step, markLoaded, add, content) in steps)
        {
            viewModel.ReturnToWorkflowStep(step);
            markLoaded();

            // 空列表：占位可见、内容收起——但绝不能两个都收起
            AssertEqual(UnrealSyncWorkspaceState.NoChanges, viewModel.WorkspaceState);
            AssertEqual(Visibility.Visible, viewModel.WorkspacePlaceholderVisibility);

            add();

            // 没有任何人显式通知，中栏也得从占位切到内容
            AssertEqual(UnrealSyncWorkspaceState.HasContent, viewModel.WorkspaceState);
            AssertEqual(Visibility.Visible, content());
            AssertEqual(Visibility.Collapsed, viewModel.WorkspacePlaceholderVisibility);
        }
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void WorkspaceGroupsStayConsistentWithItems()
{
    // 中栏渲染的是分组集合，不是条目集合。只要两者失配——有条目但没分组——
    // 内容面板就会显示成一片空白，而右栏的计数看着一切正常。
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService())
    {
        IsEngineToToolbox = false,
    };
    viewModel.ReturnToWorkflowStep(UnrealSyncWorkflow.MaxStep);

    // 复刻实际遇到的构成：59 项里 58 项无差异、1 项待写入。
    var items = new List<UnrealBlueprintSetupResultItem>();
    for (var index = 0; index < 58; index++)
    {
        items.Add(new UnrealBlueprintSetupResultItem
        {
            StableId = $"bp.same.{index}",
            GroupKey = index % 2 == 0 ? "onset" : "sequence",
            GroupName = index % 2 == 0 ? "对局设置" : "动作序列",
            DisplayName = $"字段{index}",
            Status = UnrealBlueprintSetupStatus.Unchanged
        });
    }
    items.Add(new UnrealBlueprintSetupResultItem
    {
        StableId = "table.SubSkill.Name",
        GroupKey = "SubSkill",
        GroupName = "护援技",
        DisplayName = "技能名字",
        Status = UnrealBlueprintSetupStatus.Pending,
        CurrentValues = ["群体削弱"],
        TargetValues = [""]
    });

    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items = items
    });

    // 统计按全部算，中栏只放需要处理的
    AssertEqual(58, viewModel.BlueprintSetupUnchangedCount);
    AssertEqual(1, viewModel.BlueprintSetupPendingCount);
    AssertEqual(1, viewModel.BlueprintSetupItems.Count);

    // 关键：有条目就必须有分组，且分组里的条目数对得上
    AssertEqual(UnrealSyncWorkspaceState.HasContent, viewModel.WorkspaceState);
    AssertEqual(Visibility.Visible, viewModel.BlueprintSetupWorkspaceVisibility);
    AssertEqual(Visibility.Collapsed, viewModel.WorkspacePlaceholderVisibility);
    AssertEqual(1, viewModel.BlueprintSetupGroups.Count);
    AssertEqual(
        viewModel.BlueprintSetupItems.Count,
        viewModel.BlueprintSetupGroups.Sum(group => group.Items.Count));
    AssertEqual("护援技", viewModel.BlueprintSetupGroups[0].GroupName);

    // 内容态时，第三、五步那棵树的面板必须让位，否则它会盖在上面显示成空白
    AssertEqual(Visibility.Collapsed, viewModel.SelectionContentVisibility);
    AssertEqual(Visibility.Collapsed, viewModel.FoundationWorkspaceVisibility);
    AssertEqual(Visibility.Collapsed, viewModel.LightConfigurationWorkspaceVisibility);

    // 复刻真实的收尾顺序：先写入结果（操作还没结束），再结束操作。
    // 中间那一刻是忙碌态，内容面板会收起；结束操作后必须重新亮出来，
    // 否则占位和内容双双隐藏，中栏一片空白，而右栏计数看着一切正常。
    var changed = new List<string>();
    viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
    viewModel.SetWorkflowOperationRunning(true);
    AssertEqual(UnrealSyncWorkspaceState.Busy, viewModel.WorkspaceState);
    AssertEqual(Visibility.Collapsed, viewModel.BlueprintSetupWorkspaceVisibility);
    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items = items
    });
    changed.Clear();
    viewModel.SetWorkflowOperationRunning(false);
    AssertEqual(UnrealSyncWorkspaceState.HasContent, viewModel.WorkspaceState);
    AssertEqual(Visibility.Visible, viewModel.BlueprintSetupWorkspaceVisibility);
    // 光是「算出来对」不够，界面靠通知才会重新读——必须真的通知到。
    AssertEqual(true, changed.Contains(nameof(viewModel.BlueprintSetupWorkspaceVisibility)));
    AssertEqual(true, changed.Contains(nameof(viewModel.WorkspacePlaceholderVisibility)));

    // 再来一次（模拟「进这一步先恢复缓存、检测完再刷一次」），分组不能掉队
    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items = items
    });
    AssertEqual(1, viewModel.BlueprintSetupGroups.Count);
    AssertEqual(
        viewModel.BlueprintSetupItems.Count,
        viewModel.BlueprintSetupGroups.Sum(group => group.Items.Count));
}

static void WorkspaceNeverShowsBlankPanel()
{
    // 中栏以前由九个各自独立的可见性绑定拼出来，「已加载」和「有内容」
    // 两个条件之间漏掉的那块没人认领，就是一片空白——第四步和第六步都撞过：
    // 进去一片白，点一次重新加载才显示「没有差异」。
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService())
    {
        IsEngineToToolbox = false,
    };
    viewModel.ReturnToWorkflowStep(UnrealSyncWorkflow.MaxStep);

    void AssertPlaceholderIsReadable(UnrealSyncWorkspaceState expected)
    {
        AssertEqual(expected, viewModel.WorkspaceState);
        AssertEqual(Visibility.Visible, viewModel.WorkspacePlaceholderVisibility);
        AssertEqual(Visibility.Collapsed, viewModel.WorkspaceContentVisibility);
        // 占位面板必须真的有字，否则和空白没区别。
        AssertEqual(false, string.IsNullOrWhiteSpace(viewModel.WorkspacePlaceholderTitle));
        AssertEqual(false, string.IsNullOrWhiteSpace(viewModel.WorkspacePlaceholderDescription));
        AssertEqual(false, string.IsNullOrWhiteSpace(viewModel.WorkspacePlaceholderGlyph));
    }

    // 没选角色
    AssertPlaceholderIsReadable(UnrealSyncWorkspaceState.NoCharacter);

    // 检测中：这一步的旧数据可能已经清掉了，必须明确显示在跑，不能空着
    viewModel.SetWorkflowOperationRunning(true);
    AssertPlaceholderIsReadable(UnrealSyncWorkspaceState.Busy);
    AssertEqual(Visibility.Visible, viewModel.WorkspaceBusyVisibility);
    viewModel.SetWorkflowOperationRunning(false);

    // 检测完、没有差异 —— 就是原先那块空白
    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items =
        [
            new UnrealBlueprintSetupResultItem
            {
                StableId = "bp.icon1p", GroupKey = "onset", GroupName = "对局设置",
                DisplayName = "1P 主战头像", Status = UnrealBlueprintSetupStatus.Unchanged
            }
        ]
    });
    AssertPlaceholderIsReadable(UnrealSyncWorkspaceState.NoChanges);

    // 有差异时才显示这一步自己的列表
    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items =
        [
            new UnrealBlueprintSetupResultItem
            {
                StableId = "bp.anti", GroupKey = "onset", GroupName = "对局设置",
                DisplayName = "异能角色", Status = UnrealBlueprintSetupStatus.Pending,
                CurrentValues = ["false"], TargetValues = ["true"]
            }
        ]
    });
    AssertEqual(UnrealSyncWorkspaceState.HasContent, viewModel.WorkspaceState);
    AssertEqual(Visibility.Visible, viewModel.WorkspaceContentVisibility);
    AssertEqual(Visibility.Collapsed, viewModel.WorkspacePlaceholderVisibility);
    AssertEqual(Visibility.Visible, viewModel.BlueprintSetupWorkspaceVisibility);

    // 失败态要看得出是失败，并且带上原因
    viewModel.FailBlueprintSetup("Unreal 没有生成结果文件。");
    AssertPlaceholderIsReadable(UnrealSyncWorkspaceState.Failed);
    AssertEqual(true, viewModel.IsWorkspacePlaceholderError);
    AssertEqual(true, viewModel.WorkspacePlaceholderDescription.Contains("结果文件", StringComparison.Ordinal));

    // 重新检测成功后失败态要消失
    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items = []
    });
    AssertEqual(false, viewModel.IsWorkspacePlaceholderError);

    // 六步都要能给出一个可读的占位，不能有哪一步落进空白
    for (var step = UnrealSyncWorkflow.MinStep; step <= UnrealSyncWorkflow.MaxStep; step++)
    {
        viewModel.ReturnToWorkflowStep(step);
        AssertEqual(false, string.IsNullOrWhiteSpace(viewModel.WorkflowStepName));
        if (viewModel.WorkspaceState != UnrealSyncWorkspaceState.HasContent)
        {
            AssertEqual(false, string.IsNullOrWhiteSpace(viewModel.WorkspacePlaceholderTitle));
        }
    }
}

static string SeedIconFile(CharacterCard character, params string[] segments)
{
    var path = Path.Combine([character.FolderPath, .. segments]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
    return path;
}

static void CharacterOwnedPathsArePortableOnDisk()
{
    // 之前 ZDToolboxData.json 里存的是 G:\WinUI\...\Misaka\... 这种整机绝对路径，
    // 换台机器、挪一次工作区就全指丢了。落盘必须是相对角色目录的写法，
    // 但内存里仍要是绝对路径，界面加载图片才不用到处拼路径。
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ToolFolderPath);
        var iconPath = SeedIconFile(character, "AssetMaterial", "SkillIcon", "Misaka-1.png");

        var service = new CharacterToolboxDataService();
        service.Update(character, data =>
        {
            data.Skills = new CharacterSkillsData();
            data.Buffs = new BuffData();
            data.Skills.FirstSkill.Add(new CharacterSkillEntry { TrueName = "超电磁炮", IconPath = iconPath });
            data.Buffs.Buffs.Add(new BuffEntry { Name = "带电", IconPath = iconPath });
        });

        var text = File.ReadAllText(Path.Combine(character.ToolFolderPath, "ZDToolboxData.json"));
        AssertEqual(true, text.Contains("$char/AssetMaterial/SkillIcon/Misaka-1.png", StringComparison.Ordinal));
        // 盘符一个都不许留下
        AssertEqual(false, text.Contains(root, StringComparison.OrdinalIgnoreCase));

        // 读回来必须还原成能直接喂给图片控件的绝对路径
        var reloaded = service.Load(character);
        AssertEqual(iconPath, reloaded.Skills.FirstSkill[0].IconPath);
        AssertEqual(iconPath, reloaded.Buffs.Buffs[0].IconPath);
        AssertEqual(true, File.Exists(reloaded.Buffs.Buffs[0].IconPath));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void PortablePathsSurviveCharacterFolderMove()
{
    // 相对基准取的是角色文件夹本身而不是工作区根，所以角色从 Draft 挪到
    // Completed（或者整个工作区换个盘）之后不用改写任何一条路径。
    var root = CreateTemporaryTestFolder();
    try
    {
        var draftFolder = Path.Combine(root, "Draft", "Misaka");
        var character = CreateCharacter(draftFolder, "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ToolFolderPath);
        var iconPath = SeedIconFile(character, "AssetMaterial", "SkillIcon", "Misaka-1.png");

        var service = new CharacterToolboxDataService();
        service.Update(character, data =>
        {
            data.Skills = new CharacterSkillsData();
            data.Skills.FirstSkill.Add(new CharacterSkillEntry { TrueName = "超电磁炮", IconPath = iconPath });
        });

        var completedFolder = Path.Combine(root, "Completed", "Misaka");
        Directory.CreateDirectory(Path.GetDirectoryName(completedFolder)!);
        Directory.Move(draftFolder, completedFolder);

        var moved = CreateCharacter(completedFolder, "Misaka", "御坂美琴") with { IsCompleted = true };
        var reloaded = service.Load(moved);
        AssertEqual(
            Path.Combine(completedFolder, "AssetMaterial", "SkillIcon", "Misaka-1.png"),
            reloaded.Skills.FirstSkill[0].IconPath);
        AssertEqual(true, File.Exists(reloaded.Skills.FirstSkill[0].IconPath));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void StaleIconPathIsRepairedOnRead()
{
    // 真实数据里遗留的坏账：图标路径还写着别的机器上的位置。
    // 文件其实就在角色目录里，按角色名之后的那一段拼回去就能找到。
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(Path.Combine(root, "SAO_kirito"), "SAO_kirito", "桐人[SAO]");
        Directory.CreateDirectory(character.ToolFolderPath);
        var iconPath = SeedIconFile(character, "BUFF", "SAO_Kirito_BUFF-1", "Icon.png");

        var stale = @"D:\NewData\CrossingVoidZDProject\SAO_kirito\BUFF\SAO_Kirito_BUFF-1\Icon.png";
        var missing = @"D:\NewData\CrossingVoidZDProject\SAO_kirito\BUFF\没了\没了.png";
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["IconPath"] = stale,
            ["ItemIconPath"] = missing,
            // 图标以外的字段不做这种猜测，虚幻那边的路径本来就该指到工程里去
            ["AssetPath"] = stale,
        });

        var restored = ToolboxPortablePathService.ToAbsoluteJson(json, character.FolderPath);
        using var document = JsonDocument.Parse(restored);
        AssertEqual(iconPath, document.RootElement.GetProperty("IconPath").GetString());
        // 真找不到的就老实保留原值，不许凭空编一个出来
        AssertEqual(missing, document.RootElement.GetProperty("ItemIconPath").GetString());
        AssertEqual(stale, document.RootElement.GetProperty("AssetPath").GetString());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SyncCacheKeepsUnrealSidePathsAbsolute()
{
    // 分步缓存里两侧路径同名都叫 AssetPath：工具箱那侧要收编成相对，
    // 虚幻那侧（引擎、工程、导出中间目录）在工作区外面，必须保持绝对。
    var root = CreateTemporaryTestFolder();
    try
    {
        var projectPath = Path.Combine(root, "Unreal", "CrossingVoid.uproject");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "{}");
        var character = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴");
        Directory.CreateDirectory(character.ToolFolderPath);
        var toolboxAsset = SeedIconFile(character, "ZDMaterial", "Click", "Frame0.png");
        var unrealAsset = Path.Combine(root, "Unreal", "Intermediate", "ZDToolboxExport", "Frame0.png");

        var service = new UnrealSyncSessionCacheService();
        var cache = new UnrealSyncSessionCache
        {
            ProtocolVersion = 3,
            ProjectPath = projectPath,
            EnginePath = Path.Combine(root, "Engine", "UnrealEditor.exe"),
            SelectedCharacterCode = character.Code,
            WorkflowStep = 3,
            PublishChanges =
            [
                new UnrealBridgeChange(
                    "click.0",
                    UnrealBridgeModule.SequenceFrames,
                    "Click 第 0 帧",
                    UnrealBridgeChangeKind.Added,
                    new UnrealBridgeSnapshotItem("click.0", "click", UnrealBridgeModule.SequenceFrames, "Click 第 0 帧", "hash", "{}", toolboxAsset),
                    new UnrealBridgeSnapshotItem("click.0", "click", UnrealBridgeModule.SequenceFrames, "Click 第 0 帧", "hash", "{}", unrealAsset),
                    true)
            ],
        };
        AssertEqual(true, service.Write(character, projectPath, cache));

        var cachePath = Directory.GetFiles(
            UnrealSyncSessionCacheService.GetCacheFolderPath(character), "sync-*-step3.json").Single();
        var text = File.ReadAllText(cachePath);
        AssertEqual(true, text.Contains("$char/ZDMaterial/Click/Frame0.png", StringComparison.Ordinal));
        AssertEqual(true, text.Contains("Intermediate", StringComparison.Ordinal));
        AssertEqual(false, text.Contains(character.FolderPath.Replace(@"\", @"\\"), StringComparison.OrdinalIgnoreCase));

        var loaded = service.LoadStep(character, projectPath, character.Code, 3);
        AssertEqual(UnrealSyncSessionCacheLoadStatus.Loaded, loaded.Status);
        AssertEqual(toolboxAsset, loaded.Cache!.PublishChanges[0].ToolboxItem!.AssetPath);
        AssertEqual(unrealAsset, loaded.Cache.PublishChanges[0].UnrealItem!.AssetPath);
        AssertEqual(projectPath, loaded.Cache.ProjectPath);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void WorkflowStateIsIsolatedPerCharacter()
{
    // 同步台一次只服务一个角色，但用户会来回切。切换时如果状态没清干净、
    // 或者缓存读到了别人的那一份，就会出现「切过去还显示上一个角色的差异」
    // 这种最难发现的错——界面看着正常，同步下去写错角色。
    var root = CreateTemporaryTestFolder();
    try
    {
        var projectPath = Path.Combine(root, "CrossingVoid.uproject");
        File.WriteAllText(projectPath, "{}");
        var misaka = CreateCharacter(Path.Combine(root, "Misaka"), "Misaka", "御坂美琴") with { IsCompleted = true };
        var kirito = CreateCharacter(Path.Combine(root, "SAO_Kirito"), "SAO_Kirito", "桐人[SAO]") with { IsCompleted = true };
        Directory.CreateDirectory(misaka.ToolFolderPath);
        Directory.CreateDirectory(kirito.ToolFolderPath);

        var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
        viewModel.Load(Path.Combine(root, "UnrealEditor.exe"), projectPath);
        viewModel.IsEngineToToolbox = false;
        viewModel.RefreshDraftSources([misaka, kirito]);

        UnrealSyncSourceItem SourceOf(CharacterCard character) =>
            viewModel.CharacterSources.Single(item =>
                string.Equals(item.DraftCharacter?.Code, character.Code, StringComparison.OrdinalIgnoreCase));

        // 御坂走到第六步并留下一份检测结果
        viewModel.SelectSource(SourceOf(misaka));
        viewModel.ReturnToWorkflowStep(UnrealSyncWorkflow.MaxStep);
        viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
        {
            Succeeded = true,
            CharacterCode = misaka.Code,
            Items =
            [
                new UnrealBlueprintSetupResultItem
                {
                    StableId = "bp.anti", GroupKey = "onset", GroupName = "对局设置",
                    DisplayName = "异能角色", Status = UnrealBlueprintSetupStatus.Pending,
                    CurrentValues = ["false"], TargetValues = ["true"]
                }
            ]
        });
        viewModel.FlushSessionCache();
        AssertEqual(UnrealSyncWorkflow.MaxStep, viewModel.WorkflowStep);
        AssertEqual(1, viewModel.BlueprintSetupItems.Count);

        // 切到桐人：不能带着御坂的结果过去
        viewModel.SelectSource(SourceOf(kirito));
        AssertEqual(0, viewModel.BlueprintSetupItems.Count);
        AssertEqual(false, viewModel.IsBlueprintSetupLoaded);
        AssertEqual(false, viewModel.IsWorkflowStepLoaded(UnrealSyncWorkflow.MaxStep));
        // 也不能带着御坂的差异树过去
        AssertEqual(0, viewModel.SelectionTreeRoots.Count);

        // 桐人自己走到第四步，留下自己的结果
        viewModel.ReturnToWorkflowStep(4);
        viewModel.SetLightConfigurationResult(new UnrealLightConfigurationResult
        {
            Succeeded = true,
            CharacterCode = kirito.Code,
            Items =
            [
                new UnrealLightConfigurationResultItem
                {
                    StableId = "item.icon", GroupName = "Item", DisplayName = "道具图标",
                    Status = UnrealLightConfigurationStatus.Pending
                }
            ]
        });
        viewModel.FlushSessionCache();

        // 两份缓存各自落在自己的角色目录下，互不覆盖
        var misakaCache = UnrealSyncSessionCacheService.GetCacheFolderPath(misaka);
        var kiritoCache = UnrealSyncSessionCacheService.GetCacheFolderPath(kirito);
        AssertEqual(true, Directory.Exists(misakaCache));
        AssertEqual(true, Directory.Exists(kiritoCache));
        AssertEqual(true, Directory.GetFiles(misakaCache, "*step6*.json").Length == 1);
        AssertEqual(true, Directory.GetFiles(kiritoCache, "*step4*.json").Length == 1);
        // 御坂的目录里不该出现桐人那一步的文件，反之亦然
        AssertEqual(0, Directory.GetFiles(misakaCache, "*step4*.json").Length);
        AssertEqual(0, Directory.GetFiles(kiritoCache, "*step6*.json").Length);

        // 切回御坂：第六步的结果要能从它自己的缓存恢复回来
        viewModel.SelectSource(SourceOf(misaka));
        var restored = new UnrealSyncSessionCacheService()
            .LoadStep(misaka, projectPath, misaka.Code, UnrealSyncWorkflow.MaxStep);
        AssertEqual(UnrealSyncSessionCacheLoadStatus.Loaded, restored.Status);
        AssertEqual(UnrealSyncWorkflow.MaxStep, restored.Cache!.WorkflowStep);
        AssertEqual(true, restored.Cache.IsBlueprintSetupLoaded);
        AssertSequence(["bp.anti"], restored.Cache.BlueprintSetupItems.Select(item => item.StableId).ToArray());

        // 桐人的第四步缓存同样完好，没有被御坂的写入覆盖
        var kiritoRestored = new UnrealSyncSessionCacheService()
            .LoadStep(kirito, projectPath, kirito.Code, 4);
        AssertEqual(UnrealSyncSessionCacheLoadStatus.Loaded, kiritoRestored.Status);
        AssertEqual(true, kiritoRestored.Cache!.IsLightConfigurationLoaded);
        AssertSequence(["item.icon"], kiritoRestored.Cache.LightConfigurationItems.Select(item => item.StableId).ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void WorkflowStepIsNotClampedBelowLastStep()
{
    // 步号上限以前散落着写死成 5：接上第六步之后点「下一步」会被静默夹回第五步，
    // 界面停在原地却已经跑起了虚幻检测，看着就像按钮直接执行了操作。
    // 加新步骤时只该改 UnrealSyncWorkflow.MaxStep 一处。
    AssertEqual(6, UnrealSyncWorkflow.MaxStep);

    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    // 流程步骤只存在于「工具箱 -> 虚幻」方向，默认方向是反过来的。
    viewModel.IsEngineToToolbox = false;
    viewModel.ReturnToWorkflowStep(UnrealSyncWorkflow.MaxStep);
    AssertEqual(UnrealSyncWorkflow.MaxStep, viewModel.WorkflowStep);
    AssertEqual(true, viewModel.IsBlueprintSetupWorkspace);
    // 越界的步号才应该被夹住。
    viewModel.ReturnToWorkflowStep(UnrealSyncWorkflow.MaxStep + 1);
    AssertEqual(UnrealSyncWorkflow.MaxStep, viewModel.WorkflowStep);

    // 「上一步」只导航，绝不触发检测。往回走是「我要看看上一步」，
    // 不是「重新查一遍上一步」——那一步没缓存时中栏会显示未检测占位，
    // 要不要真查由用户点「重新加载」决定。
    var (sync, controller, host, root) = CreateWorkflowController(UnrealSyncWorkflow.MaxStep);
    try
    {
        controller.GoToPreviousStep();
        AssertEqual(UnrealSyncWorkflow.MaxStep - 1, sync.WorkflowStep);
        AssertEqual(0, host.DetectedSteps.Count);

        // 一直往回退也不会退过第一步，更不会一路触发检测
        for (var i = 0; i < UnrealSyncWorkflow.MaxStep + 2; i++)
        {
            controller.GoToPreviousStep();
        }

        AssertEqual(UnrealSyncWorkflow.MinStep, sync.WorkflowStep);
        AssertEqual(0, host.DetectedSteps.Count);

        // 已经有数据的步骤不重复检测：进第六步只该记一条复用日志
        controller.EnterStepAsync(UnrealSyncWorkflow.MaxStep).GetAwaiter().GetResult();
        AssertSequence([UnrealSyncWorkflow.MaxStep], host.DetectedSteps.ToArray());
        sync.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
        {
            Succeeded = true, CharacterCode = "Misaka", Items = []
        });
        controller.EnterStepAsync(UnrealSyncWorkflow.MaxStep).GetAwaiter().GetResult();
        AssertSequence([UnrealSyncWorkflow.MaxStep], host.DetectedSteps.ToArray());
        AssertEqual(1, host.Logs.Count(log => log.Contains("复用本步缓存", StringComparison.Ordinal)));

        // 「重新加载」是明确要求重查，有缓存也得真跑
        controller.ReloadCurrentStepAsync().GetAwaiter().GetResult();
        AssertSequence(
            [UnrealSyncWorkflow.MaxStep, UnrealSyncWorkflow.MaxStep],
            host.DetectedSteps.ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void WorkflowStepCacheLivesInCharacterFolder()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴") with { IsCompleted = true };
        const string projectPath = @"C:\Unreal\CrossingVoid.uproject";
        var service = new UnrealSyncSessionCacheService();

        // 没写过就是 Missing，不能报成损坏——第一次进这一步是正常情况。
        AssertEqual(
            UnrealSyncSessionCacheLoadStatus.Missing,
            service.LoadStep(character, projectPath, character.Code, UnrealSyncWorkflow.MaxStep).Status);

        // 每一步各自一个文件，互不覆盖。
        for (var step = UnrealSyncWorkflow.MinStep; step <= UnrealSyncWorkflow.MaxStep; step++)
        {
            AssertEqual(true, service.Write(character, projectPath, new UnrealSyncSessionCache
            {
                ProjectPath = projectPath,
                SelectedCharacterCode = character.Code,
                WorkflowStep = step,
                IsPublishDetection = true
            }));
        }

        // 缓存要落在角色自己的目录里：角色之间不会撞车，导出角色时进度一起带走。
        var cacheFolder = UnrealSyncSessionCacheService.GetCacheFolderPath(character);
        AssertEqual(true, cacheFolder.StartsWith(character.ToolFolderPath, StringComparison.OrdinalIgnoreCase));
        AssertEqual(
            UnrealSyncWorkflow.MaxStep - UnrealSyncWorkflow.MinStep + 1,
            Directory.GetFiles(cacheFolder, "*.json").Length);

        for (var step = UnrealSyncWorkflow.MinStep; step <= UnrealSyncWorkflow.MaxStep; step++)
        {
            var loaded = service.LoadStep(character, projectPath, character.Code, step);
            AssertEqual(UnrealSyncSessionCacheLoadStatus.Loaded, loaded.Status);
            AssertEqual(step, loaded.Cache!.WorkflowStep);
        }

        // 恢复现场时取最近写过的那一步。
        AssertEqual(
            UnrealSyncWorkflow.MaxStep,
            service.LoadLatest(character, projectPath, character.Code).Cache!.WorkflowStep);

        // 同一个角色对接另一个 Unreal 项目时不能读到这一份。
        AssertEqual(
            UnrealSyncSessionCacheLoadStatus.Missing,
            service.LoadStep(character, @"D:\Other\Other.uproject", character.Code, UnrealSyncWorkflow.MaxStep).Status);

        // 落盘被强杀打断会留下 0 字节文件，那是「没有缓存」，不是「缓存损坏」，
        // 不该弹错误提示。
        var emptyPath = Path.Combine(cacheFolder, Path.GetFileName(
            Directory.GetFiles(cacheFolder, "*.json").OrderBy(path => path).First()));
        File.WriteAllText(emptyPath, string.Empty);
        AssertEqual(
            UnrealSyncSessionCacheLoadStatus.Missing,
            service.LoadStep(character, projectPath, character.Code, UnrealSyncWorkflow.MinStep).Status);

        // 某一步的文件坏了，只该让那一步失效，不该拖垮整次恢复。
        var brokenPath = Directory.GetFiles(cacheFolder, "*.json")
            .OrderByDescending(path => path)
            .First();
        File.WriteAllText(brokenPath, "{ 这不是 JSON");
        var latest = service.LoadLatest(character, projectPath, character.Code);
        AssertEqual(UnrealSyncSessionCacheLoadStatus.Loaded, latest.Status);
        AssertEqual(UnrealSyncWorkflow.MaxStep - 1, latest.Cache!.WorkflowStep);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void WorkflowStepSkipsDetectionWhenAlreadyLoaded()
{
    var viewModel = CreateBlueprintSetupViewModel(2);

    // 第六步已经有数据，再进这一步就不该重跑虚幻检测。
    AssertEqual(true, viewModel.IsWorkflowStepLoaded(6));
    // 其余步骤各看各的状态，不能跟着第六步一起被认为已加载。
    AssertEqual(false, viewModel.IsWorkflowStepLoaded(2));
    AssertEqual(false, viewModel.IsWorkflowStepLoaded(4));

    // 第三步和第五步共用同一棵差异树，必须靠归属区分，
    // 否则从第五步回第三步会误以为已经检测过。
    AssertEqual(false, viewModel.IsWorkflowStepLoaded(3));
    AssertEqual(false, viewModel.IsWorkflowStepLoaded(5));
}

static void BlueprintSetupRequestComesFromToolboxData()
{
    var root = CreateTemporaryTestFolder();
    try
    {
        var character = CreateCharacter(root, "Misaka", "御坂美琴") with { IsCompleted = true };
        var info = new CharacterInfoData
        {
            Code = character.Code,
            Name = "御坂美琴",
            Anti = true,
            FormLimit = 2
        };
        new CharacterInfoService().Save(character, info);

        // 对局内头像是固定四槽：1=1P 主战、2=1P 护援、3=2P 主战、4=2P 护援。
        // 主战两张进角色蓝图，护援两张进 SupImage 表，接错就会把头像装到别人身上。
        var materialService = new BaseMaterialService();
        var images = new List<string>();
        for (var index = 1; index <= 4; index++)
        {
            var path = Path.Combine(root, $"avatar{index}.png");
            WriteSolidImage(path, Color.Red, 566, 325);
            images.Add(path);
        }
        materialService.ReplaceWithImages(character, BaseMaterialKind.BattleAvatar, images);

        var skills = new CharacterSkillsData();
        var first = CharacterSkillsService.CreateEntry();
        first.PositionName = "群体攻击";
        first.TrueName = "雷击枪";
        first.PtCost = "3";
        first.SkillState = "常态";
        first.GuardState = "反击";
        first.GuardValue = "0.5";
        skills.FirstSkill.Add(first);
        var combo = CharacterSkillsService.CreateEntry();
        combo.TrueName = "电光石火";
        combo.ComboCharacterName = "桐人[SAO]";
        skills.ComboSkills.Add(combo);
        new CharacterSkillsService().Save(character, skills);

        var sections = materialService.LoadSections(character);
        var request = new UnrealBlueprintSetupService().BuildRequest(
            character, info, skills, sections);

        AssertEqual("御坂美琴", request.CharacterName);
        AssertEqual(2, request.FormCount);
        AssertEqual(true, request.Anti);

        var avatarSlots = sections
            .Single(section => section.Spec.Kind == BaseMaterialKind.BattleAvatar)
            .Items.ToDictionary(item => item.Index, item => item.FilePath);
        AssertEqual(
            UnrealBlueprintSetupService.BuildImageObjectPath("Misaka", avatarSlots[1]),
            request.Icon1PObjectPath);
        AssertEqual(
            UnrealBlueprintSetupService.BuildImageObjectPath("Misaka", avatarSlots[3]),
            request.Icon2PObjectPath);
        AssertEqual(
            UnrealBlueprintSetupService.BuildImageObjectPath("Misaka", avatarSlots[2]),
            request.Support1PObjectPath);
        AssertEqual(
            UnrealBlueprintSetupService.BuildImageObjectPath("Misaka", avatarSlots[4]),
            request.Support2PObjectPath);

        AssertEqual(
            "/Game/GameActor2D/Misaka/Misaka_AnimBP.Misaka_AnimBP_C",
            request.AnimInstanceClassObjectPath);
        // 站街 Flipbook 的规范名不带角色前缀，序列同步就是这么产的。
        AssertEqual(
            "/Game/GameActor2D/Misaka/Material/Idle/Idle_Flipbook.Idle_Flipbook",
            request.IdleFlipbookObjectPath);

        // 技能按形态并列，保存时工具箱会补齐到 FormLimit 条，
        // 这里 FormLimit=2，所以第二形态是一条空白占位。
        var slot1 = request.Skills.Single(item => item.SlotKey == "SkillSlot1");
        AssertSequence(["群体攻击", ""], slot1.Names.ToArray());
        AssertSequence(["雷击枪", ""], slot1.SkillNames.ToArray());
        AssertSequence([3, 0], slot1.PointCosts.ToArray());
        AssertSequence(["Normal", "Air"], slot1.SkillStates.ToArray());
        AssertSequence(["Attack", "Air"], slot1.PreformTypes.ToArray());
        AssertSequence([0.5, 0d], slot1.PreSkillValues.ToArray());

        // 连携技按搭档分条，行键就是搭档的中文名。
        var comboPayload = request.Skills.Single(item => item.SlotKey == "Combo");
        AssertEqual("桐人[SAO]", comboPayload.PartnerName);
        AssertEqual("电光石火", comboPayload.SkillNames[0]);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void BlueprintSetupSequenceBindingsMatchCatalog()
{
    var bindings = UnrealBlueprintSetupService.BuildSequenceBindings("Misaka", 2);

    // 只有代号表里标了蓝图属性的动作才归第六步管，其余靠 AnimMaps 绑定。
    var expected = SequenceActionCatalog.Definitions
        .Where(item => !string.IsNullOrEmpty(item.BlueprintSequenceArrayProperty))
        .Select(item => item.BlueprintSequenceArrayProperty)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();
    AssertSequence(
        expected,
        bindings.Select(item => item.PropertyName).OrderBy(item => item, StringComparer.Ordinal).ToArray());

    // 形态 N 存在下标 N-1，第二形态的资产名要带 _Shape2。
    var click = bindings.Single(item => item.PropertyName == "ClickSeq");
    AssertSequence(
        [
            "/Game/GameActor2D/Misaka/AnimSequences/Click.Click",
            "/Game/GameActor2D/Misaka/AnimSequences/Click_Shape2.Click_Shape2"
        ],
        click.ObjectPaths.ToArray());
}

static void BlueprintSetupMapsChineseOptionsToUnrealEnums()
{
    // 工具箱存的是中文选项，Unreal 的 E2DSkillType/EPreformType 是英文枚举名，
    // 映射错了不会报错，只会静悄悄写进一个错的状态。
    AssertEqual("Normal", UnrealBlueprintSetupService.MapSkillStateToUnreal("常态"));
    AssertEqual("Disable", UnrealBlueprintSetupService.MapSkillStateToUnreal("禁用"));
    AssertEqual("Abandon", UnrealBlueprintSetupService.MapSkillStateToUnreal("舍弃"));
    AssertEqual("Air", UnrealBlueprintSetupService.MapSkillStateToUnreal("空"));
    AssertEqual("Air", UnrealBlueprintSetupService.MapSkillStateToUnreal(string.Empty));

    // 守备的「反击」对应的是 EPreformType.Attack，不是字面意义上的攻击。
    AssertEqual("Defense", UnrealBlueprintSetupService.MapGuardStateToUnreal("防御"));
    AssertEqual("Attack", UnrealBlueprintSetupService.MapGuardStateToUnreal("反击"));
    AssertEqual("Dodge", UnrealBlueprintSetupService.MapGuardStateToUnreal("闪避"));
    AssertEqual("Air", UnrealBlueprintSetupService.MapGuardStateToUnreal("空"));
}

static void BlueprintSetupWritesRowsInsteadOfRefillingTable()
{
    var script = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "apply_blueprint_setup.py"), Encoding.UTF8);

    // FSkillData2D 自带 Name 属性，和数据表的行名字段同名。整表回灌会把每行的
    // 行名覆盖成技能名字数组，2DSubSkill 那 27 行会一次全废，所以绝不能出现。
    AssertEqual(false, script.Contains(".fill_from_json_string("));
    AssertEqual(false, script.Contains(".fill_data_table_from_json_string("));
    AssertEqual(true, script.Contains("upsert_data_table_row("));

    // 读取侧同样不能走整表导出：UDataTable 的 JSON 导出器把行名字段（默认叫 "Name"）
    // 当作 FieldToSkip 传给 WriteStruct，FSkillData2D 恰好也有个 Name 属性，
    // 于是每行的技能名字导出来永远是空数组——比对时被永远判成待写入，
    // 写进去了也看不出变化。必须按行读。
    AssertEqual(false, script.Contains(".export_to_json_string()"));
    AssertEqual(true, script.Contains("read_data_table_row("));

    // 蓝图技能槽里的 FText 也走桥接插件，否则本地化键会被降级成文化无关文本。
    AssertEqual(true, script.Contains("apply_json_to_struct_property"));

    // 扫描和写入必须共用同一份载荷，只差一个 Mode。分成两套算法的话，
    // 界面报「无差异」而写入却改了东西这种事迟早会发生。
    var window = ReadUnrealSyncWindowSource();
    AssertEqual(true, window.Contains("request.Mode = apply ? \"Apply\" : \"Scan\";"));
    // 扫描和写入都只经这一个执行方法，载荷自然是同一份。
    AssertEqual(1, CountOccurrences(window, "private async Task<UnrealBlueprintSetupResult> ExecuteUnrealBlueprintSetupAsync("));
    AssertEqual(2, CountOccurrences(window, "await ExecuteUnrealBlueprintSetupAsync("));
}

static void BlueprintSetupGroupsItemsAndHidesUnchanged()
{
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    viewModel.IsEngineToToolbox = false;
    viewModel.ReturnToWorkflowStep(6);
    viewModel.SetBlueprintSetupResult(new UnrealBlueprintSetupResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items =
        [
            new UnrealBlueprintSetupResultItem
            {
                StableId = "bp.icon1p", GroupKey = "onset", GroupName = "对局设置",
                DisplayName = "1P 主战头像", Status = UnrealBlueprintSetupStatus.Unchanged
            },
            new UnrealBlueprintSetupResultItem
            {
                StableId = "bp.anti", GroupKey = "onset", GroupName = "对局设置",
                DisplayName = "异能角色", Status = UnrealBlueprintSetupStatus.Pending,
                CurrentValues = ["false"], TargetValues = ["true"]
            },
            new UnrealBlueprintSetupResultItem
            {
                StableId = "bp.skill.SkillSlot1.SkillName", GroupKey = "SkillSlot1", GroupName = "一技能",
                DisplayName = "技能真名", Status = UnrealBlueprintSetupStatus.Pending,
                CurrentValues = ["掌心雷"], TargetValues = ["雷击枪"]
            }
        ]
    });

    // 统计按全部条目算，但无变化的不进中栏——一个角色六十多条，
    // 全铺出来会把真正待处理的几条埋掉。
    AssertEqual(1, viewModel.BlueprintSetupUnchangedCount);
    AssertEqual(2, viewModel.BlueprintSetupPendingCount);
    AssertEqual(2, viewModel.BlueprintSetupItems.Count);
    AssertSequence(
        ["对局设置", "一技能"],
        viewModel.BlueprintSetupGroups.Select(group => group.GroupName).ToArray());
    AssertSequence(
        ["bp.anti", "bp.skill.SkillSlot1.SkillName"],
        viewModel.GetSelectedBlueprintSetupIds().OrderBy(item => item, StringComparer.Ordinal).ToArray());

    // 取消勾选后不能再进入写入集合。
    viewModel.BlueprintSetupItems[0].IsSelected = false;
    AssertSequence(
        ["bp.skill.SkillSlot1.SkillName"],
        viewModel.GetSelectedBlueprintSetupIds().ToArray());
    AssertEqual(true, viewModel.CanApplyBlueprintSetup);

    // 一项都没勾时写入按钮必须灰掉，否则会发一次空写入。
    viewModel.BlueprintSetupItems[1].IsSelected = false;
    AssertEqual(0, viewModel.GetSelectedBlueprintSetupIds().Count);
    AssertEqual(false, viewModel.CanApplyBlueprintSetup);
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
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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

static void UnrealBridgeDiffKeepsSyncedItemsAfterProjectMove()
{
    // 基线按 Unreal 工程路径散列分文件存。把工程换个盘符之后，旧基线留在旧文件里，
    // 新路径的基线可能只记了一部分模块（例如只同步过序列帧）。这时素材项查不到
    // 记录，如果还要求「整个基线文件为空」才认作已同步，它们会全部变成冲突——
    // 而冲突不能自动执行，第三步就此卡死，列表再也归不了零。实测一个角色
    // 147 项素材里有 144 项被这样误判。
    var iconPath = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml");
    var toolbox = new UnrealBridgeSnapshot(
        "Misaka",
        [
            new UnrealBridgeSnapshotItem(
                "material:icon-1",
                "module:BaseMaterials",
                UnrealBridgeModule.BaseMaterials,
                "头像 #1",
                "toolbox-source-hash",
                "{}",
                iconPath,
                ToolboxRelativePath: "AssetMaterial/Icon/Misaka-Icon-1.png",
                NormalizedName: "Misaka-Icon-1")
        ]);
    var unreal = new UnrealBridgeSnapshot(
        "Misaka",
        [
            new UnrealBridgeSnapshotItem(
                "material:icon-1",
                "module:BaseMaterials",
                UnrealBridgeModule.BaseMaterials,
                "头像 #1",
                // 两侧哈希本来就不可比：工具箱是 PNG 源文件，Unreal 是导出的资产。
                "unreal-asset-hash",
                "{}",
                string.Empty,
                SourceObjectPath: "/Game/AssetMaterial/ImageS/CharaterS/Misaka/Misaka-Icon-1.Misaka-Icon-1",
                NormalizedName: "Misaka-Icon-1")
        ]);

    // 新路径的基线只记了序列帧，素材项在里面查不到。
    var baselineFromOtherModule = new UnrealBridgeSyncState
    {
        Entries =
        {
            ["sequence-frame:click:0"] = new UnrealBridgeSyncStateEntry("a", "b")
        }
    };

    var change = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, baselineFromOtherModule)
        .Single();

    // 资产已经在规范目录、用的就是规范名，就是「已同步、只是没记过」，不是冲突。
    AssertEqual(UnrealBridgeChangeKind.Unchanged, change.Kind);

    // 完全没有基线时同样如此（原来只覆盖得了这一种情况）。
    AssertEqual(
        UnrealBridgeChangeKind.Unchanged,
        new UnrealBridgeDiffService()
            .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null)
            .Single().Kind);

    // 但资产不在规范位置就不能这么放过——那才是真的要人看一眼。
    var strayUnreal = new UnrealBridgeSnapshot(
        "Misaka",
        [
            unreal.Items[0] with
            {
                SourceObjectPath = "/Game/GameActor2D/Origin_Misaka/Material/T_Old.T_Old",
                NormalizedName = "T_Old"
            }
        ]);
    AssertEqual(
        UnrealBridgeChangeKind.Conflict,
        new UnrealBridgeDiffService()
            .Compare(toolbox, strayUnreal, UnrealBridgeDirection.PublishToUnreal, baselineFromOtherModule)
            .Single().Kind);
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
    // 默认勾选的决定早已从差异服务移到选择树：Compare 一律产出未勾选的变更，
    // 由 UnrealSyncSelectionTreeBuilder 按 selectPendingByDefault 决定勾不勾。
    // 这条断言留着是为了钉住这个分工——差异服务不该再自己决定勾选。
    AssertEqual(false, change.IsSelected);
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

static void UnrealLightConfigurationUsesDedicatedFourthStep()
{
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService())
    {
        IsEngineToToolbox = false
    };
    viewModel.ReturnToWorkflowStep(4);
    viewModel.SetLightConfigurationResult(new UnrealLightConfigurationResult
    {
        Succeeded = true,
        CharacterCode = "Misaka",
        Items =
        {
            new UnrealLightConfigurationResultItem
            {
                StableId = "item.speed",
                GroupName = "Item 配置",
                DisplayName = "速度",
                TargetPath = "/Game/ITems/CharItemS/Item_Misaka.Item_Misaka",
                TargetField = "ItemData.CharData.Speed",
                SourceSummary = "CharacterInfo.Speed",
                CurrentSummary = "550",
                TargetSummary = "720",
                Status = UnrealLightConfigurationStatus.Pending
            },
            new UnrealLightConfigurationResultItem
            {
                StableId = "item.health",
                GroupName = "Item 配置",
                DisplayName = "生命值",
                Status = UnrealLightConfigurationStatus.Unchanged
            }
        }
    });

    AssertEqual("基础配置", viewModel.WorkspaceTitle);
    AssertEqual(1, viewModel.LightConfigurationItems.Count);
    AssertEqual(1, viewModel.LightConfigurationSelectedCount);
    AssertEqual(1, viewModel.LightConfigurationPendingCount);
    AssertEqual(1, viewModel.LightConfigurationUnchangedCount);
    AssertEqual(true, viewModel.CanApplyLightConfiguration);
}

static void UnrealLightConfigurationScriptUsesConfirmedWhitelist()
{
    var scriptPath = Path.Combine(
        Directory.GetCurrentDirectory(),
        "Tools",
        "UnrealBridge",
        "configure_unreal_light_settings.py");
    var source = File.ReadAllText(scriptPath);

    AssertEqual(true, source.Contains("team.voice", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("item.passive", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("meta.waves", StringComparison.Ordinal));
    AssertEqual(true, source.Contains("voice.talk-concurrency", StringComparison.Ordinal));
    AssertEqual(false, source.Contains("CharShapeNow", StringComparison.Ordinal));
    AssertEqual(false, source.Contains("SkillNow", StringComparison.Ordinal));
    AssertEqual(false, source.Contains("SkillHave", StringComparison.Ordinal));
    AssertEqual(false, source.Contains("SkillLevel", StringComparison.Ordinal));
    AssertEqual(false, source.Contains("Synchronize", StringComparison.Ordinal));

    var exportSource = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(),
        "Tools",
        "Unreal",
        "export_zd_assets.py"));
    AssertEqual(true, exportSource.Contains("UI_TeamSelect", StringComparison.Ordinal));
    AssertEqual(true, exportSource.Contains("CharVoice", StringComparison.Ordinal));

    var xaml = ReadAllProjectXaml();
    AssertEqual(true, xaml.Contains("LightConfigurationItems", StringComparison.Ordinal));
    AssertEqual(true, xaml.Contains("ApplyUnrealLightConfigurationButton_Click", StringComparison.Ordinal));
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
    // 默认勾选的决定早已从差异服务移到选择树：Compare 一律产出未勾选的变更，
    // 由 UnrealSyncSelectionTreeBuilder 按 selectPendingByDefault 决定勾不勾。
    // 这条断言留着是为了钉住这个分工——差异服务不该再自己决定勾选。
    AssertEqual(false, change.IsSelected);
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
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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
        new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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
        new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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
                [action], [], [], [], []),
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
            new UnrealProjectSyncSequenceFramesPreview(false, false, string.Empty, string.Empty, [], [], [], [], []),
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
            new UnrealProjectSyncSequenceFramesPreview(true, true, "/Game/Test/AnimMaps", "已读取", [], [skillAction, skill2Action], [], [], []),
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
            new UnrealProjectSyncSequenceFramesPreview(true, true, "/Game/Test/AnimMaps", "已读取", [], [action], [], [], []),
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
    // 共享目录不参与「按选中角色过滤」——BUFF 图标是全角色公用的，
    // 被过滤掉就会在差异里表现成「Unreal 里没有这些图标」。
    // 这里断言的是意图，不是那一行的字面写法：豁免名单后来加过 TEAM_SELECT_ROOT，
    // 原先按整行比对的断言就是这么红掉的。
    var exemption = System.Text.RegularExpressions.Regex.Match(
        script,
        @"elif selected_codes and target_path not in \(([^)]*)\):");
    AssertEqual(true, exemption.Success);
    AssertEqual(true, exemption.Groups[1].Value.Contains("SHARED_BUFF_ICON_ROOT", StringComparison.Ordinal));
    AssertEqual(true, exemption.Groups[1].Value.Contains("SHARED_BATTLE_EFFECT_ROOT", StringComparison.Ordinal));
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
    // 默认勾选的决定早已从差异服务移到选择树：Compare 一律产出未勾选的变更，
    // 由 UnrealSyncSelectionTreeBuilder 按 selectPendingByDefault 决定勾不勾。
    // 这条断言留着是为了钉住这个分工——差异服务不该再自己决定勾选。
    AssertEqual(false, change.IsSelected);

    // 执行计划只收被勾选的变更；勾选由选择树负责，这里直接模拟用户勾上。
    var operation = new UnrealBridgeExecutionPlanService().Build(
        UnrealBridgeDirection.PublishToUnreal,
        "ALO_Yuki",
        @"D:\UnrealMap\CrossingVoid\CrossingVoid.uproject",
        [change with { IsSelected = true }],
        deletionsConfirmed: false,
        isFirstPublish: false,
        templateCharacterCode: string.Empty).Operations.Single();

    // 语音的规范名用连字符，和图片素材一致（项目里就是 Misaka-Formation-1 这种）。
    // 这条断言原来写的是下划线，是命名规范改成连字符之前留下的。
    AssertEqual(
        "/Game/GameActor2D/ALO_Yuki/Sound/Formation/ALO_Yuki-Formation-1.ALO_Yuki-Formation-1",
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
    // 新增的文件素材也能自动执行——第三步要靠这条才能把新图片同步进去。
    // 这条断言原来写的是 false，那是「新增必须先在第二步选好重定向目标」
    // 时期留下的；现在有源文件就直接建，没有源文件（例如空白帧）另有分支。
    AssertEqual(true, UnrealBridgePublishSupportPolicy.CanExecute(new UnrealBridgeChange(
        fileItem.StableId, fileItem.Module, fileItem.DisplayName, UnrealBridgeChangeKind.Added,
        fileItem, null, true)));

    // 但源文件不存在的新增仍然不能自动执行：没东西可导入。
    var missingFileItem = fileItem with { AssetPath = Path.Combine(Directory.GetCurrentDirectory(), "不存在的素材.png") };
    AssertEqual(false, UnrealBridgePublishSupportPolicy.CanExecute(new UnrealBridgeChange(
        missingFileItem.StableId, missingFileItem.Module, missingFileItem.DisplayName,
        UnrealBridgeChangeKind.Added, missingFileItem, null, true)));

    // 语义模块（角色信息这种）任何情况下都不走文件同步。
    AssertEqual(false, UnrealBridgePublishSupportPolicy.CanExecute(new UnrealBridgeChange(
        semanticItem.StableId, semanticItem.Module, semanticItem.DisplayName,
        UnrealBridgeChangeKind.Added, semanticItem, null, true)));
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

/// <summary>
/// 构造一份「可导入」的快照：图片和语音必须有真实存在的导出文件才允许勾选
/// （没有文件就没东西可导入），所以这些用例不能用 AssetPath 为空的默认构造。
/// </summary>
static UnrealBridgeSnapshot CreateImportableUnrealBridgeSnapshot(
    params (string StableId, UnrealBridgeModule Module, string Name, string Hash)[] items)
{
    var existingFile = Path.Combine(Directory.GetCurrentDirectory(), "MainWindow.xaml");
    return new UnrealBridgeSnapshot(
        "Misaka",
        items.Select(item => new UnrealBridgeSnapshotItem(
            item.StableId,
            ParentStableId: string.Empty,
            item.Module,
            item.Name,
            item.Hash,
            PayloadJson: "{}",
            AssetPath: existingFile)).ToArray());
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

/// <summary>
/// 读同步台在 MainWindow 上的全部分部文件。
///
/// 按文件名读单个文件的写法太脆：这套代码按步骤拆过一次，
/// 一拆所有源码断言就一起红。这里按前缀全收，之后再拆也不受影响。
/// </summary>
static string ReadUnrealSyncWindowSource()
{
    var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "MainWindow.UnrealSync*.cs")
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();
    if (files.Length == 0)
    {
        throw new FileNotFoundException("未找到同步台的 MainWindow 分部文件。");
    }

    return string.Join(Environment.NewLine, files.Select(path => File.ReadAllText(path, Encoding.UTF8)));
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

static void SequenceActionCatalogMatchesExporterSpecs()
{
    var script = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"))
        .ReplaceLineEndings("\n");
    var start = script.IndexOf("SEQUENCE_ACTION_SPECS = [", StringComparison.Ordinal);
    AssertEqual(true, start >= 0);
    var end = script.IndexOf("]\n", start, StringComparison.Ordinal);
    AssertEqual(true, end > start);
    var block = script[start..end];
    var specs = System.Text.RegularExpressions.Regex
        .Matches(block, """\("([^"]*)", "([^"]+)", "([^"]+)", "([^"]+)", \[([^\]]*)\]\)""")
        .Select(match => (
            SourceProperty: match.Groups[1].Value,
            Code: match.Groups[2].Value,
            DisplayName: match.Groups[3].Value,
            Category: match.Groups[4].Value,
            Aliases: System.Text.RegularExpressions.Regex.Matches(match.Groups[5].Value, @"""([^""]+)""")
                .Select(alias => alias.Groups[1].Value)
                .ToArray()))
        .ToArray();

    AssertEqual(SequenceActionCatalog.Definitions.Count, specs.Length);
    AssertSequence(
        specs.Select(spec => spec.Code).ToArray(),
        SequenceActionCatalog.Definitions.Select(definition => definition.Code).ToArray());
    for (var index = 0; index < specs.Length; index++)
    {
        var spec = specs[index];
        var definition = SequenceActionCatalog.Definitions[index];
        AssertEqual(spec.DisplayName, definition.DisplayName);
        AssertEqual(
            spec.Category.Equals("skill", StringComparison.Ordinal)
                ? SequenceActionCategory.Skill
                : SequenceActionCategory.Base,
            definition.Category);
        AssertSequence(spec.Aliases, definition.Aliases.ToArray());
        // 技能槽 SkillSlot1..4 是 FSkillData2D 数值结构，不是序列数组，所以目录里必须留空。
        AssertEqual(
            true,
            definition.BlueprintSequenceArrayProperty.Length == 0 ||
            definition.BlueprintSequenceArrayProperty.Equals(spec.SourceProperty, StringComparison.Ordinal));
    }
}

static void SequenceActionCatalogResolvesLegacyNamesAndForms()
{
    var cases = new (string Raw, string Code, int Form)[]
    {
        ("Dead", "Death", 1),
        ("Death", "Death", 1),
        ("Defense", "Defence", 1),
        ("Defatk", "DefAtk", 1),
        ("Ondm", "OnDamage", 1),
        ("OnDM", "OnDamage", 1),
        ("Standup", "StandUP", 1),
        ("Ko", "KO", 1),
        ("SK2", "Sk2", 1),
        ("Sk1", "Sk1", 1),
        ("Fly", "Flying", 1),
        ("Flydown", "FlyDown", 1),
        ("Victor", "Victory", 1),
        ("Defeated", "Defeat", 1),
        ("Idle02", "Idle", 2),
        ("Sk102", "Sk1", 2),
        ("Click2", "Click", 2),
        ("Sk1_Shape2", "Sk1", 2),
        ("Sk2_Shape2", "Sk2", 2)
    };
    foreach (var (raw, code, form) in cases)
    {
        AssertEqual(true, SequenceActionCatalog.TryResolve(raw, out var definition, out var formIndex));
        AssertEqual(code, definition!.Code);
        AssertEqual(form, formIndex);
    }

    AssertEqual(false, SequenceActionCatalog.TryResolve("Link", out _, out _));
    AssertEqual(false, SequenceActionCatalog.TryResolve("12Sao_Asuna", out _, out _));
    AssertEqual(false, SequenceActionCatalog.TryResolve(string.Empty, out _, out _));

    // 历史写法与规范写法必须落到同一个分组键，否则同一个动作会被拆成两行。
    AssertEqual(SequenceActionCatalog.NormalizeActionKey("OnDamage"), SequenceActionCatalog.NormalizeActionKey("Ondm"));
    AssertEqual(SequenceActionCatalog.NormalizeActionKey("Defence"), SequenceActionCatalog.NormalizeActionKey("Defense"));
    AssertEqual(SequenceActionCatalog.NormalizeActionKey("Death"), SequenceActionCatalog.NormalizeActionKey("Dead"));
    AssertEqual(SequenceActionCatalog.NormalizeActionKey("KO"), SequenceActionCatalog.NormalizeActionKey("Ko"));
    AssertEqual(SequenceActionCatalog.NormalizeActionKey("Idle02"), SequenceActionCatalog.NormalizeActionKey("Idle_Shape2"));
    AssertEqual(false, SequenceActionCatalog.NormalizeActionKey("Idle") == SequenceActionCatalog.NormalizeActionKey("Idle02"));
}

static void SequenceActionCatalogProducesCanonicalAssetNames()
{
    var onDamage = SequenceActionCatalog.Resolve("Ondm", out var onDamageForm);
    AssertEqual("OnDamage", SequenceActionCatalog.GetAnimSequenceName(onDamage, onDamageForm));
    AssertEqual("OnDamage", SequenceActionCatalog.GetMaterialFolderName(onDamage, onDamageForm));
    AssertEqual("OnDamage_Flipbook", SequenceActionCatalog.GetFlipbookName(onDamage, onDamageForm));

    // 虚幻侧帧号从 0 开始，位宽跟随总帧数：个位数不补零，十位数补到两位，百位数补到三位。
    AssertEqual("OnDamage_Frame0", SequenceActionCatalog.GetFrameTextureName(onDamage, onDamageForm, 0, 6));
    AssertEqual("OnDamage_Frame5", SequenceActionCatalog.GetFrameTextureName(onDamage, onDamageForm, 5, 6));
    AssertEqual("OnDamage_Frame00", SequenceActionCatalog.GetFrameTextureName(onDamage, onDamageForm, 0, 13));
    AssertEqual("OnDamage_Frame12", SequenceActionCatalog.GetFrameTextureName(onDamage, onDamageForm, 12, 13));
    AssertEqual("OnDamage_Frame000", SequenceActionCatalog.GetFrameTextureName(onDamage, onDamageForm, 0, 100));
    AssertEqual("OnDamage_Frame099", SequenceActionCatalog.GetFrameTextureName(onDamage, onDamageForm, 99, 100));
    AssertEqual("OnDamage_Frame09_Sprite", SequenceActionCatalog.GetFrameSpriteName(onDamage, onDamageForm, 9, 10));

    // 边界：10 张素材是两位数，所以第一张是 00 而不是 0。
    AssertEqual("00", SequenceActionCatalog.FormatFrameOrdinal(0, 10));
    AssertEqual("8", SequenceActionCatalog.FormatFrameOrdinal(8, 9));
    AssertEqual("099", SequenceActionCatalog.FormatFrameOrdinal(99, 100));

    var outOfRange = false;
    try
    {
        SequenceActionCatalog.FormatFrameOrdinal(6, 6);
    }
    catch (ArgumentOutOfRangeException)
    {
        outOfRange = true;
    }

    AssertEqual(true, outOfRange);

    var idleShape2 = SequenceActionCatalog.Resolve("Idle02", out var idleForm);
    AssertEqual("Idle_Shape2", SequenceActionCatalog.GetAnimSequenceName(idleShape2, idleForm));
    AssertEqual("Idle_Shape2_Frame02", SequenceActionCatalog.GetFrameTextureName(idleShape2, idleForm, 2, 20));
    AssertEqual("Idle_Shape2_Flipbook", SequenceActionCatalog.GetFlipbookName(idleShape2, idleForm));

    // 生成的名字必须能被自己重新解析，否则复扫时会认不出刚写进去的资产。
    foreach (var definition in SequenceActionCatalog.Definitions)
    {
        foreach (var form in new[] { 1, 2 })
        {
            AssertEqual(
                true,
                SequenceActionCatalog.TryResolve(
                    SequenceActionCatalog.GetAnimSequenceName(definition, form),
                    out var roundTrip,
                    out var roundTripForm));
            AssertEqual(definition.Code, roundTrip!.Code);
            AssertEqual(form, roundTripForm);
        }
    }
}

static void ToolboxSequenceActionCodesResolveThroughCatalog()
{
    var skills = new CharacterSkillsData();
    foreach (var action in SequenceFrameService.BuildActions(skills, formLimit: 3))
    {
        if (action.IsCombo)
        {
            // 连携技尚未纳入规范动作目录，第五步也还不发布它。
            AssertEqual(false, SequenceActionCatalog.TryResolve(action.Code, out _, out _));
            continue;
        }

        AssertEqual(true, SequenceActionCatalog.TryResolve(action.Code, out var definition, out var formIndex));
        AssertEqual(action.FormIndex, formIndex);
        // 工具箱代号写作 Idle / Idle02，规范代号写作 Idle / Idle_Shape2；两者必须指向同一个动作。
        var expectedToolboxCode = formIndex > 1
            ? definition!.Code + formIndex.ToString("00")
            : definition!.Code;
        AssertEqual(
            SequenceActionCatalog.NormalizeToken(expectedToolboxCode),
            SequenceActionCatalog.NormalizeToken(action.Code));
    }
}

static void SequenceSyncPlanFieldsAreReadByBridgeScript()
{
    var script = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "sync_character_sequences.py"));
    // 计划里新增的字段如果没有加进 _load 的归一化列表，Python 只会看到 PascalCase 键，
    // action.get('camelCase') 会静默取到默认值。
    foreach (var type in new[] { typeof(UnrealBridgeSequenceSyncAction), typeof(UnrealBridgeSequenceSyncFrame) })
    {
        foreach (var property in type.GetProperties())
        {
            var camelCase = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            if (!script.Contains("'" + camelCase + "'", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"同步脚本没有读取计划字段 {camelCase}。");
            }
        }
    }
}

static void SequenceActionLegacyTokensDoNotMatchCharacterName()
{
    var ko = SequenceActionCatalog.Resolve("KO", out var koForm);
    AssertSequence(["ko"], SequenceActionCatalog.GetLegacyNameTokens(ko, koForm).ToArray());

    var onDamage = SequenceActionCatalog.Resolve("OnDamage", out var onDamageForm);
    AssertSequence(["ondamage", "ondm"], SequenceActionCatalog.GetLegacyNameTokens(onDamage, onDamageForm).ToArray());

    // fly 只用于识别历史 Material/Fly 文件夹；拿去删除会连 FlyStart、FlyDown 的资产一起删掉。
    var flying = SequenceActionCatalog.Resolve("Flying", out var flyingForm);
    AssertSequence(["flying"], SequenceActionCatalog.GetLegacyNameTokens(flying, flyingForm).ToArray());
    AssertEqual(true, flying.Aliases.Contains("fly"));
    AssertSequence(
        ["flystart"],
        SequenceActionCatalog.GetLegacyNameTokens(
            SequenceActionCatalog.Resolve("FlyStart", out var flyStartForm), flyStartForm).ToArray());

    // 清理必须按资产名分段比较；用整条路径做子串匹配时，
    // 角色 Origin_Ako 同步 KO 会命中该角色的每一个资产。
    var script = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "sync_character_sequences.py"));
    AssertEqual(true, script.Contains("legacy_tokens & _name_tokens(_asset_name_of(path))", StringComparison.Ordinal));
    AssertEqual(false, script.Contains("alias in lowered", StringComparison.Ordinal));
}

static UnrealBridgeSnapshot BuildToolboxSequenceSnapshot(
    string characterCode,
    string actionCode,
    int frameCount)
{
    var actionId = SequenceFrameIdentity.BuildActionStableId(actionCode);
    var items = new List<UnrealBridgeSnapshotItem>
    {
        new(actionId,
            $"module:{UnrealBridgeModule.SequenceFrames}",
            UnrealBridgeModule.SequenceFrames, actionCode,
            SequenceFrameIdentity.BuildActionPayload(actionCode, 12),
            SequenceFrameIdentity.BuildActionPayload(actionCode, 12), string.Empty)
    };
    for (var ordinal = 0; ordinal < frameCount; ordinal++)
    {
        var payload = "{\"actionCode\":\"" + actionCode + "\",\"durationFrames\":\"1\",\"index\":\"" +
            (ordinal + 1) + "\",\"isBlank\":\"false\",\"voiceFileName\":\"\"}";
        items.Add(new UnrealBridgeSnapshotItem(
            SequenceFrameIdentity.BuildFrameStableId(actionCode, ordinal),
            actionId,
            UnrealBridgeModule.SequenceFrames,
            $"{actionCode} 第 {ordinal + 1} 帧",
            $"TOOLBOX-{ordinal}",
            payload,
            string.Empty));
    }

    return new UnrealBridgeSnapshot(characterCode, items);
}

static UnrealBridgeSnapshot BuildUnrealSequenceSnapshot(
    string characterCode,
    string unrealActionCode,
    string[] frameObjectPaths)
{
    var separator = ((char)0x1F).ToString();
    var actionId = SequenceFrameIdentity.BuildActionStableId(unrealActionCode);
    var items = new List<UnrealBridgeSnapshotItem>
    {
        new(actionId, $"module:{UnrealBridgeModule.SequenceFrames}", UnrealBridgeModule.SequenceFrames,
            unrealActionCode,
            SequenceFrameIdentity.BuildActionPayload(unrealActionCode, 12),
            SequenceFrameIdentity.BuildActionPayload(unrealActionCode, 12), string.Empty,
            $"/Game/GameActor2D/{characterCode}/{characterCode}_AnimMaps.{characterCode}_AnimMaps")
    };
    for (var ordinal = 0; ordinal < frameObjectPaths.Length; ordinal++)
    {
        items.Add(new UnrealBridgeSnapshotItem(
            SequenceFrameIdentity.BuildFrameStableId(unrealActionCode, ordinal),
            actionId,
            UnrealBridgeModule.SequenceFrames,
            $"{unrealActionCode} 第 {ordinal + 1} 帧",
            $"UNREAL-{ordinal}",
            string.Join(separator, unrealActionCode, "1", (ordinal + 1).ToString(), "False"),
            string.Empty,
            frameObjectPaths[ordinal]));
    }

    return new UnrealBridgeSnapshot(characterCode, items);
}

static void SequenceSnapshotsAlignAcrossLegacySpellings()
{
    // 工具箱代号 OnDamage，Unreal 目录是 Ondm；两侧必须落到同一批稳定 ID。
    var toolbox = BuildToolboxSequenceSnapshot("Misaka", "OnDamage", 4);
    var unreal = BuildUnrealSequenceSnapshot("Misaka", "Ondm",
    [
        "/Game/GameActor2D/Misaka/Material/OnDM/Origin_Misaka_Ondm_1.Origin_Misaka_Ondm_1",
        "/Game/GameActor2D/Misaka/Material/OnDM/Origin_Misaka_Ondm_2.Origin_Misaka_Ondm_2",
        "/Game/GameActor2D/Misaka/Material/OnDM/Origin_Misaka_Ondm_3.Origin_Misaka_Ondm_3",
        "/Game/GameActor2D/Misaka/Material/OnDM/Origin_Misaka_Ondm_4.Origin_Misaka_Ondm_4"
    ]);

    AssertSequence(
        toolbox.Items.Select(item => item.StableId).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        unreal.Items.Select(item => item.StableId).OrderBy(value => value, StringComparer.Ordinal).ToArray());

    var changes = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null)
        .ToArray();

    // 历史命名的帧要判成改名并拆成旧/新两行，不能判成冲突——冲突在第五步不可执行。
    var frameRows = changes
        .Where(change => change.SequenceGroupKey == SequenceFrameIdentity.BuildActionStableId("OnDamage"))
        .ToArray();
    frameRows = frameRows.Where(change => SequenceFrameIdentity.IsFrameStableId(change.StableId)).ToArray();
    AssertEqual(8, frameRows.Length);
    AssertEqual(4, frameRows.Count(change => change.Kind == UnrealBridgeChangeKind.DeleteCandidate));
    AssertEqual(4, frameRows.Count(change => change.Kind == UnrealBridgeChangeKind.Added));
    AssertEqual(0, changes.Count(change => change.Kind == UnrealBridgeChangeKind.Conflict));
}

static void SequenceFramesAlreadyPublishedCountAsUnchanged()
{
    var toolbox = BuildToolboxSequenceSnapshot("Misaka", "Sk1", 13);
    // 13 帧是两位数，规范贴图名从 Sk1_Frame00 开始。
    var unreal = BuildUnrealSequenceSnapshot("Misaka", "Sk1",
        Enumerable.Range(0, 13)
            .Select(ordinal => $"/Game/GameActor2D/Misaka/Material/Sk1/Sk1_Frame{ordinal:00}.Sk1_Frame{ordinal:00}")
            .ToArray());

    var changes = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null)
        .ToArray();

    AssertEqual(14, changes.Length);
    AssertEqual(14, changes.Count(change => change.Kind == UnrealBridgeChangeKind.Unchanged));

    // 已同步的帧两侧都在，可以直接迁移成基线；这是第五步能记录同步状态的前提。
    var baseline = new UnrealBridgeBaselineService().BuildFromChanges(
        "Misaka",
        @"C:\Unreal\CrossingVoid.uproject",
        changes.Where(change => change.ToolboxItem is not null && change.UnrealItem is not null).ToArray());
    AssertEqual(14, baseline.Entries.Count);
}

static void SequenceFrameCountChangesProduceAddAndDelete()
{
    var toolbox = BuildToolboxSequenceSnapshot("Misaka", "Idle", 8);
    var unreal = BuildUnrealSequenceSnapshot("Misaka", "Idle",
        Enumerable.Range(0, 6)
            .Select(ordinal => $"/Game/GameActor2D/Misaka/Material/Idle/Idle_Frame{ordinal}.Idle_Frame{ordinal}")
            .ToArray());

    var changes = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null)
        .Where(change => SequenceFrameIdentity.IsFrameStableId(change.StableId))
        .ToArray();

    // 两侧都是个位数宽度，所以前 6 帧命名一致、判为无差异；多出来的两帧是新增。
    AssertEqual(6, changes.Count(change => change.Kind == UnrealBridgeChangeKind.Unchanged));
    AssertEqual(2, changes.Count(change => change.Kind == UnrealBridgeChangeKind.Added));
    AssertEqual(0, changes.Count(change => change.Kind == UnrealBridgeChangeKind.DeleteCandidate));
}

static void SequenceFrameEditsSeparateUpdateFromConflict()
{
    var toolbox = BuildToolboxSequenceSnapshot("Misaka", "Click", 4);
    var unreal = BuildUnrealSequenceSnapshot("Misaka", "Click",
        Enumerable.Range(0, 4)
            .Select(ordinal => $"/Game/GameActor2D/Misaka/Material/Click/Click_Frame{ordinal}.Click_Frame{ordinal}")
            .ToArray());
    var baseline = new UnrealBridgeBaselineService().BuildFromChanges(
        "Misaka",
        @"C:\Unreal\CrossingVoid.uproject",
        new UnrealBridgeDiffService()
            .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null)
            .Where(change => change.ToolboxItem is not null && change.UnrealItem is not null)
            .ToArray());

    var editedFrameId = SequenceFrameIdentity.BuildFrameStableId("Click", 2);

    // 工具箱侧改了第 3 帧：应该是可执行的更新，拆成旧/新两行。
    var toolboxEdited = toolbox with
    {
        Items = toolbox.Items
            .Select(item => item.StableId == editedFrameId ? item with { ContentHash = "TOOLBOX-EDITED" } : item)
            .ToArray()
    };
    var updated = new UnrealBridgeDiffService()
        .Compare(toolboxEdited, unreal, UnrealBridgeDirection.PublishToUnreal, baseline)
        .ToArray();
    AssertEqual(2, updated.Count(change => change.StableId.StartsWith(editedFrameId, StringComparison.Ordinal)));
    AssertEqual(0, updated.Count(change => change.Kind == UnrealBridgeChangeKind.Conflict));
    AssertEqual(
        true,
        updated.Any(change =>
            change.StableId == editedFrameId + ":add" &&
            UnrealBridgePublishSupportPolicy.CanExecute(change with { ToolboxItem = null }) == false));

    // Unreal 侧被改动：必须判成冲突，交给人处理，不能自动覆盖。
    var unrealEdited = unreal with
    {
        Items = unreal.Items
            .Select(item => item.StableId == editedFrameId ? item with { ContentHash = "UNREAL-EDITED" } : item)
            .ToArray()
    };
    var conflicted = new UnrealBridgeDiffService()
        .Compare(toolbox, unrealEdited, UnrealBridgeDirection.PublishToUnreal, baseline)
        .ToArray();
    AssertEqual(
        1,
        conflicted.Count(change =>
            change.StableId == editedFrameId + ":delete" &&
            change.Kind == UnrealBridgeChangeKind.DeleteCandidate));
    AssertEqual(4, conflicted.Count(change => change.Kind == UnrealBridgeChangeKind.Unchanged));
}

static void SequenceBaselineCommitsOnlyExecutedActions()
{
    var project = @"C:\Unreal\CrossingVoid.uproject";
    var clickToolbox = BuildToolboxSequenceSnapshot("Misaka", "Click", 4);
    var clickUnreal = BuildUnrealSequenceSnapshot("Misaka", "Click",
        Enumerable.Range(0, 4)
            .Select(ordinal => $"/Game/GameActor2D/Misaka/Material/Click/Click_Frame{ordinal}.Click_Frame{ordinal}")
            .ToArray());
    var clickChanges = new UnrealBridgeDiffService()
        .Compare(clickToolbox, clickUnreal, UnrealBridgeDirection.PublishToUnreal, null)
        .ToArray();

    var service = new UnrealBridgeBaselineService();
    var previous = service.BuildFromChanges("Misaka", project, clickChanges);
    AssertEqual(5, previous.Entries.Count);

    // 这一轮只同步 Idle：Click 的既有基线必须原样保留。
    var idleToolbox = BuildToolboxSequenceSnapshot("Misaka", "Idle", 8);
    var idleUnreal = BuildUnrealSequenceSnapshot("Misaka", "Idle",
        Enumerable.Range(0, 8)
            .Select(ordinal => $"/Game/GameActor2D/Misaka/Material/Idle/Idle_Frame{ordinal}.Idle_Frame{ordinal}")
            .ToArray());
    var idleChanges = new UnrealBridgeDiffService()
        .Compare(idleToolbox, idleUnreal, UnrealBridgeDirection.PublishToUnreal, previous)
        .ToArray();

    var merged = service.MergeVerifiedSequenceState(
        previous,
        "Misaka",
        project,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SequenceFrameIdentity.BuildActionStableId("Idle") },
        idleChanges);

    AssertEqual(true, merged.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Click", 3)));
    AssertEqual(true, merged.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Idle", 7)));
    AssertEqual(14, merged.Entries.Count);

    // 再同步一次 Idle，但这次只剩 5 帧：多出来的旧基线条目必须被清掉。
    var shorterToolbox = BuildToolboxSequenceSnapshot("Misaka", "Idle", 5);
    var shorterUnreal = BuildUnrealSequenceSnapshot("Misaka", "Idle",
        Enumerable.Range(0, 5)
            .Select(ordinal => $"/Game/GameActor2D/Misaka/Material/Idle/Idle_Frame{ordinal}.Idle_Frame{ordinal}")
            .ToArray());
    var shrunk = service.MergeVerifiedSequenceState(
        merged,
        "Misaka",
        project,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SequenceFrameIdentity.BuildActionStableId("Idle") },
        new UnrealBridgeDiffService()
            .Compare(shorterToolbox, shorterUnreal, UnrealBridgeDirection.PublishToUnreal, merged)
            .ToArray());

    AssertEqual(false, shrunk.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Idle", 7)));
    AssertEqual(true, shrunk.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Idle", 4)));
    AssertEqual(true, shrunk.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Click", 3)));
    AssertEqual(UnrealBridgeSyncState.SourceFileHashScheme, shrunk.HashScheme);
}

static UnrealBridgeSnapshot AppendOwnedSequenceAssets(
    UnrealBridgeSnapshot snapshot,
    string unrealActionCode,
    string[] ownedObjectPaths)
{
    var separator = ((char)0x1F).ToString();
    var actionId = SequenceFrameIdentity.BuildActionStableId(unrealActionCode);
    var items = snapshot.Items.ToList();
    foreach (var objectPath in ownedObjectPaths)
    {
        var package = objectPath.Split('.', 2)[0];
        var assetName = package[(package.LastIndexOf('/') + 1)..];
        items.Add(new UnrealBridgeSnapshotItem(
            SequenceFrameIdentity.BuildOwnedAssetStableId(unrealActionCode, objectPath),
            actionId,
            UnrealBridgeModule.SequenceFrames,
            $"{unrealActionCode} · {assetName}",
            $"OWNED-{assetName}",
            string.Join(separator, unrealActionCode, "1", assetName, "Texture2D"),
            string.Empty,
            objectPath,
            string.Empty,
            string.Empty,
            assetName));
    }

    return snapshot with { Items = items.ToArray() };
}

static void StaleSequenceAssetsBecomeDeleteCandidates()
{
    // 复刻 Misaka/Material/Sk1 的真实现场：13 帧规范资产 + 早期同步留下的旧命名 + 断了引用的旧 Sprite。
    var toolbox = BuildToolboxSequenceSnapshot("Misaka", "Sk1", 13);
    const string material = "/Game/GameActor2D/Misaka/Material/Sk1";
    var canonical = Enumerable.Range(0, 13)
        .SelectMany(ordinal => new[]
        {
            $"{material}/Sk1_Frame{ordinal:00}.Sk1_Frame{ordinal:00}",
            $"{material}/Sk1_Frame{ordinal:00}_Sprite.Sk1_Frame{ordinal:00}_Sprite"
        })
        .Append($"{material}/Sk1_Flipbook.Sk1_Flipbook")
        .Append("/Game/GameActor2D/Misaka/AnimSequences/Sk1.Sk1")
        .ToArray();
    var stale = Enumerable.Range(6, 8)
        .Select(index => $"{material}/Origin_Misaka_Sk1_{index}_Sprite.Origin_Misaka_Sk1_{index}_Sprite")
        // 早期实验用的一位起始三位编号，以及那时放在 Material 目录里、与序列同名的旧 Flipbook。
        .Append($"{material}/Sk1_Frame001.Sk1_Frame001")
        .Append($"{material}/Sk1.Sk1")
        .ToArray();

    var unreal = AppendOwnedSequenceAssets(
        BuildUnrealSequenceSnapshot("Misaka", "Sk1",
            Enumerable.Range(0, 13)
                .Select(ordinal => $"{material}/Sk1_Frame{ordinal:00}.Sk1_Frame{ordinal:00}")
                .ToArray()),
        "Sk1",
        canonical.Concat(stale).ToArray());

    var changes = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null)
        .ToArray();

    var deletes = changes
        .Where(change => SequenceFrameIdentity.IsOwnedAssetStableId(change.StableId))
        .ToArray();
    AssertEqual(stale.Length, deletes.Length);
    AssertEqual(stale.Length, deletes.Count(change => change.Kind == UnrealBridgeChangeKind.DeleteCandidate));
    AssertEqual(true, deletes.All(change => UnrealBridgePublishSupportPolicy.CanExecute(change)));

    // Material 目录里那个和 AnimSequences 序列同名的旧 Flipbook 必须被当成待删除，
    // 只按资产名比较会把它误判成规范序列。
    AssertEqual(
        true,
        deletes.Any(change => string.Equals(
            change.UnrealItem!.SourceObjectPath,
            $"{material}/Sk1.Sk1",
            StringComparison.Ordinal)));

    // 全部归到同一个动作分组，选择树里才会挂在「一技能」下面。
    var group = SequenceFrameIdentity.BuildActionStableId("Sk1");
    AssertEqual(true, deletes.All(change => change.SequenceGroupKey == group));

    var roots = UnrealSyncSelectionTreeBuilder.FromSequenceChanges(
        changes,
        UnrealBridgePublishSupportPolicy.CanExecute);
    var sk1 = roots.Single(item => item.StableId == group);
    AssertEqual(stale.Length, sk1.DeleteCount);
    AssertEqual(0, sk1.AddCount);
}




static void SequenceGroupTitleSurvivesCacheRestore()
{
    var toolbox = BuildToolboxSequenceSnapshot("Misaka", "Sk1", 13);
    const string material = "/Game/GameActor2D/Misaka/Material/Sk1";
    var unreal = AppendOwnedSequenceAssets(
        BuildUnrealSequenceSnapshot("Misaka", "Sk1",
            Enumerable.Range(0, 13)
                .Select(ordinal => $"{material}/Sk1_Frame{ordinal:00}.Sk1_Frame{ordinal:00}")
                .ToArray()),
        "Sk1",
        [$"{material}/Origin_Misaka_Sk1_6_Sprite.Origin_Misaka_Sk1_6_Sprite"]);
    var changes = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null)
        // 复现 FilterCachedPublishChanges：恢复缓存时 Unchanged 的动作节点会被过滤掉。
        .Where(change => change.Kind != UnrealBridgeChangeKind.Unchanged)
        .ToArray();
    AssertEqual(false, changes.Any(change => SequenceFrameIdentity.IsActionStableId(change.StableId)));

    var roots = UnrealSyncSelectionTreeBuilder.FromSequenceChanges(changes, UnrealBridgePublishSupportPolicy.CanExecute);
    var sk1 = roots.Single(item => item.StableId == SequenceFrameIdentity.BuildActionStableId("Sk1"));
    AssertEqual("一技能", sk1.DisplayName);
    AssertEqual(1, sk1.DeleteCount);
}

static void PublishChangeComparisonIgnoresUnchangedSubset()
{
    // 复现同步前那次比较：检测时 _lastPublishChanges 是完整差异集（含 Unchanged），
    // 但 ReturnToWorkflowStep 会用会话缓存把它换成"去掉 Unchanged"的子集。
    // 两侧口径不一致时，比较会因为元素个数不同恒判"内容已变化"，同步永远走不下去。
    var toolbox = BuildToolboxSequenceSnapshot("Misaka", "Sk1", 4);
    var unreal = BuildUnrealSequenceSnapshot("Misaka", "Sk1",
        Enumerable.Range(0, 4)
            .Select(ordinal => $"/Game/GameActor2D/Misaka/Material/Sk1/Sk1_Frame{ordinal}.Sk1_Frame{ordinal}")
            .ToArray());
    var full = new UnrealBridgeDiffService()
        .Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null)
        .ToArray();
    AssertEqual(true, full.Any(change => change.Kind == UnrealBridgeChangeKind.Unchanged));

    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    // WorkflowStep 只读；FilterPublishChanges 同样认 ZdAnimationTracks 这个阶段。
    viewModel.SelectedPublishStage = viewModel.PublishStages
        .First(stage => stage.Stage == UnrealBridgePublishStage.ZdAnimationTracks);

    // 缓存恢复路径写入的是过滤后的子集。
    var cachedSubset = full.Where(change => change.Kind != UnrealBridgeChangeKind.Unchanged).ToArray();
    AssertEqual(false, cachedSubset.Length == full.Length);
    viewModel.SetPublishSelectionTree(
        UnrealSyncSelectionTreeBuilder.FromSequenceChanges(cachedSubset, UnrealBridgePublishSupportPolicy.CanExecute),
        cachedSubset);

    // 同步前重新检测得到的是完整集合；内容其实没变，必须判为一致。
    AssertEqual(true, viewModel.MatchesCurrentPublishChanges(full));

    // 内容真的变了仍然要判为不一致。
    var editedFrameId = SequenceFrameIdentity.BuildFrameStableId("Sk1", 2);
    var edited = full
        .Select(change => change.StableId == editedFrameId
            ? change with { Kind = UnrealBridgeChangeKind.Added, UnrealItem = null }
            : change)
        .ToArray();
    AssertEqual(false, viewModel.MatchesCurrentPublishChanges(edited));
}

static void MigrationBaselineDoesNotFlipSequenceKinds()
{
    // 检测时用 baseline=null 算差异，算完才写迁移基线；同步时会加载这份基线再算一次。
    // 如果两次的 Kind 不同，同步前的一致性校验就会误判"内容已变化"。
    var toolbox = BuildToolboxSequenceSnapshot("Misaka", "Sk1", 6);
    const string material = "/Game/GameActor2D/Misaka/Material/Sk1";
    var unreal = AppendOwnedSequenceAssets(
        BuildUnrealSequenceSnapshot("Misaka", "Sk1",
        [
            // 前四帧已是规范命名，后两帧还是历史命名（会判成改名）。
            $"{material}/Sk1_Frame0.Sk1_Frame0",
            $"{material}/Sk1_Frame1.Sk1_Frame1",
            $"{material}/Sk1_Frame2.Sk1_Frame2",
            $"{material}/Sk1_Frame3.Sk1_Frame3",
            $"{material}/Origin_Misaka_Sk1_5.Origin_Misaka_Sk1_5",
            $"{material}/Origin_Misaka_Sk1_6.Origin_Misaka_Sk1_6"
        ]),
        "Sk1",
        [$"{material}/Origin_Misaka_Sk1_6_Sprite.Origin_Misaka_Sk1_6_Sprite"]);

    var service = new UnrealBridgeDiffService();
    var first = service.Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, null).ToArray();
    var migrated = new UnrealBridgeBaselineService().BuildFromChanges(
        "Misaka",
        @"C:\Unreal\CrossingVoid.uproject",
        first.Where(change => change.ToolboxItem is not null && change.UnrealItem is not null).ToArray());
    AssertEqual(true, migrated.Entries.Count > 0);

    var second = service.Compare(toolbox, unreal, UnrealBridgeDirection.PublishToUnreal, migrated).ToArray();
    AssertSequence(
        first.Select(change => $"{change.StableId}={change.Kind}")
            .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        second.Select(change => $"{change.StableId}={change.Kind}")
            .OrderBy(value => value, StringComparer.Ordinal).ToArray());
}

static (CharacterCard Character, string Root) CreateSequenceCharacterWithFrames(string actionCode, int frameCount)
{
    var root = CreateTemporaryTestFolder();
    var character = CreateCharacter(root, "Misaka", "御坂美琴");
    Directory.CreateDirectory(character.ToolFolderPath);
    var service = new SequenceFrameService();
    var action = SequenceFrameService.BuildActions(new CharacterSkillsData())
        .First(item => string.Equals(item.Code, actionCode, StringComparison.OrdinalIgnoreCase));
    var sources = new List<string>();
    for (var index = 0; index < frameCount; index++)
    {
        var path = Path.Combine(root, $"frame{index}.png");
        WriteSolidImage(path, Color.FromArgb(10 + index * 5, 20, 30),
            SequenceFrameService.RequiredWidth, SequenceFrameService.RequiredHeight);
        sources.Add(path);
    }

    if (sources.Count > 0)
    {
        service.ImportFrames(character, action, sources);
    }

    return (character, root);
}

static UnrealBridgeChange CreateSequenceDeleteChange(string actionCode, string objectPath)
{
    var separator = ((char)0x1F).ToString();
    var assetName = objectPath.Split('.', 2)[0][(objectPath.LastIndexOf('/') + 1)..];
    var unrealItem = new UnrealBridgeSnapshotItem(
        SequenceFrameIdentity.BuildOwnedAssetStableId(actionCode, objectPath),
        SequenceFrameIdentity.BuildActionStableId(actionCode),
        UnrealBridgeModule.SequenceFrames,
        $"{actionCode} · {assetName}",
        $"HASH-{assetName}",
        string.Join(separator, actionCode, "1", assetName, "Texture2D"),
        string.Empty,
        objectPath,
        string.Empty,
        string.Empty,
        assetName);
    return new UnrealBridgeChange(
        unrealItem.StableId,
        UnrealBridgeModule.SequenceFrames,
        unrealItem.DisplayName,
        UnrealBridgeChangeKind.DeleteCandidate,
        null,
        unrealItem,
        true,
        SequenceFrameIdentity.BuildActionStableId(actionCode));
}

static void SequencePlanCarriesSelectedStaleAssetPaths()
{
    var (character, root) = CreateSequenceCharacterWithFrames("Click", 3);
    try
    {
        // 用户勾选的待删资产必须原样进入计划：Python 端按资产名 token 猜测，
        // 既漏删（历史命名不含动作 token）也误删（规范目录里没勾的资产）。
        const string material = "/Game/GameActor2D/Misaka/Material/Click";
        var selected = new[]
        {
            CreateSequenceDeleteChange("Click", $"{material}/Origin_Misaka_Click_1.Origin_Misaka_Click_1"),
            CreateSequenceDeleteChange("Click", $"{material}/Frame_01.Frame_01")
        };

        var plan = new UnrealBridgeSequencePublishService()
            .BuildSequenceSyncPlan(character, @"C:\Unreal\CrossingVoid.uproject", selected);
        var action = plan.Actions.Single();
        AssertEqual(true, action.HasStaleAssetSelection);
        AssertSequence(
            selected.Select(change => change.UnrealItem!.SourceObjectPath).OrderBy(v => v, StringComparer.Ordinal).ToArray(),
            action.StaleAssetObjectPaths.OrderBy(v => v, StringComparer.Ordinal).ToArray());

        // Frame_01 的资产名里不含 "click"，token 猜测永远删不掉它。
        AssertEqual(false, SequenceActionCatalog
            .GetLegacyNameTokens(SequenceActionCatalog.Resolve("Click", out var form), form)
            .Any(token => "frame01".Contains(token, StringComparison.Ordinal)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SequencePlanSkipsActionsWithoutFrames()
{
    // Click 有帧、Death 没有帧：以前 Death 会让整批同步抛异常失败。
    var (character, root) = CreateSequenceCharacterWithFrames("Click", 2);
    try
    {
        var selected = new[]
        {
            CreateSequenceDeleteChange("Click", "/Game/GameActor2D/Misaka/Material/Click/Old.Old"),
            CreateSequenceDeleteChange("Death", "/Game/GameActor2D/Misaka/Material/Death/Old.Old")
        };

        var service = new UnrealBridgeSequencePublishService();
        var plan = service.BuildSequenceSyncPlan(character, @"C:\Unreal\CrossingVoid.uproject", selected);
        AssertEqual("Click", plan.Actions.Single().ActionCode);
        AssertSequence(["Death"], service.SkippedActionCodes.ToArray());

        // 全部动作都没有帧时才应该报错。
        var onlyEmpty = new[]
        {
            CreateSequenceDeleteChange("Death", "/Game/GameActor2D/Misaka/Material/Death/Old.Old")
        };
        var threw = false;
        try
        {
            new UnrealBridgeSequencePublishService()
                .BuildSequenceSyncPlan(character, @"C:\Unreal\CrossingVoid.uproject", onlyEmpty);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        AssertEqual(true, threw);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SequenceBaselineSkipsFailedActions()
{
    // 中途失败时，只有成功的动作可以刷新基线；失败动作保留执行前的条目。
    var project = @"C:\Unreal\CrossingVoid.uproject";
    var service = new UnrealBridgeBaselineService();
    var previous = new UnrealBridgeSyncState
    {
        HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
        CharacterCode = "Misaka",
        UnrealProjectPath = project
    };
    previous.Entries[SequenceFrameIdentity.BuildFrameStableId("Sk1", 0)] =
        new UnrealBridgeSyncStateEntry("OLD-TOOLBOX", "OLD-UNREAL", "/Game/old.old", string.Empty, string.Empty, string.Empty);
    previous.Entries[SequenceFrameIdentity.BuildFrameStableId("Sk2", 0)] =
        new UnrealBridgeSyncStateEntry("KEEP-TOOLBOX", "KEEP-UNREAL", "/Game/keep.keep", string.Empty, string.Empty, string.Empty);

    // 只有 Sk1 执行成功。
    var executed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SequenceFrameIdentity.BuildActionStableId("Sk1")
    };
    var merged = service.MergeVerifiedSequenceState(previous, "Misaka", project, executed, []);
    AssertEqual(false, merged.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Sk1", 0)));
    AssertEqual(true, merged.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Sk2", 0)));

    // 复扫比较用的基线要剔除已执行动作，否则刚同步好的帧会被判成冲突。
    var rescanBaseline = service.WithoutActions(previous, executed)!;
    AssertEqual(false, rescanBaseline.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Sk1", 0)));
    AssertEqual(true, rescanBaseline.Entries.ContainsKey(SequenceFrameIdentity.BuildFrameStableId("Sk2", 0)));
    AssertEqual(2, previous.Entries.Count);
}

static void OnlineExecutionOnlyMatchesSelectedProject()
{
    var engine = @"F:\UnrealEngine\UE_Moon\Engine\Binaries\Win64\UnrealEditor.exe";
    var service = new UnrealPythonTaskExecutionService();

    // 没有任何编辑器打开这个项目时必须走离线，而不是选了在线再连接失败。
    var missingProject = Path.Combine(Path.GetTempPath(), "ZDNoSuchProject", "ZDNoSuchProject.uproject");
    AssertEqual(false, service.ShouldUseRunningEditor(engine, missingProject));

    // 路径为空时同样退回离线，不能因为「有 UnrealEditor 进程」就当作可用。
    AssertEqual(false, service.ShouldUseRunningEditor(engine, string.Empty));
    AssertEqual(false, service.ShouldUseRunningEditor(null, null));

    // 走离线时 BuildLaunch 必须原样返回离线启动信息。
    var root = CreateTemporaryTestFolder();
    try
    {
        var scriptPath = Path.Combine(root, "job.py");
        File.WriteAllText(scriptPath, "# noop");
        var offline = new System.Diagnostics.ProcessStartInfo { FileName = "UnrealEditor-Cmd.exe" };
        var launch = service.BuildLaunch(
            engine,
            missingProject,
            scriptPath,
            Path.Combine(root, "job.remote.json"),
            offline);
        AssertEqual(false, launch.UsesRunningEditor);
        AssertEqual(true, ReferenceEquals(offline, launch.StartInfo));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SessionCacheFlushDoesNotDeadlockUiThread()
{
    // 复现真实卡死：防抖保存的续体要回到 UI 线程，而 UI 线程正同步等同一个信号量。
    // 用单线程同步上下文模拟 UI 线程；死锁时这里会超时失败而不是永久挂起。
    var context = new SingleThreadTestSynchronizationContext();
    var previous = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(context);
    try
    {
        var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
        viewModel.SelectedPublishStage = viewModel.PublishStages
            .First(stage => stage.Stage == UnrealBridgePublishStage.ZdAnimationTracks);

        var completed = new ManualResetEventSlim(false);
        // 反复触发「保存 → 立刻同步落盘」，正是检测流程里的真实节奏。
        var worker = new Thread(() =>
        {
            context.Send(_ =>
            {
                for (var round = 0; round < 20; round++)
                {
                    viewModel.SaveSelectionStateToSessionCache();
                }
            }, null);
            completed.Set();
        });
        worker.IsBackground = true;
        worker.Start();

        // 同时把排队到「UI 线程」的续体抽干，模拟消息循环。
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!completed.IsSet && DateTime.UtcNow < deadline)
        {
            context.DrainPending();
            Thread.Sleep(10);
        }

        AssertEqual(true, completed.IsSet);
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previous);
        context.Dispose();
    }
}

static void BulkSelectionRestoreRecomputesOnce()
{
    // 恢复勾选时每个叶子都会 OnPropertyChanged(IsChecked)。以前这会逐个触发
    // 「全量汇总 + 全量过滤 + 克隆全部变更去建缓存」，上千个叶子就是 O(n^2) 次克隆：
    // 实测把进程顶到 5GB、UI 线程 100% 空转十几分钟，看起来就像死锁。
    const int actionCount = 12;
    const int framesPerAction = 60;
    var changes = new List<UnrealBridgeChange>();
    for (var actionIndex = 0; actionIndex < actionCount; actionIndex++)
    {
        var actionCode = SequenceActionCatalog.Definitions[actionIndex].Code;
        changes.Add(new UnrealBridgeChange(
            SequenceFrameIdentity.BuildActionStableId(actionCode),
            UnrealBridgeModule.SequenceFrames,
            actionCode,
            UnrealBridgeChangeKind.Unchanged,
            null,
            null,
            false,
            SequenceFrameIdentity.BuildActionStableId(actionCode)));
        for (var frame = 0; frame < framesPerAction; frame++)
        {
            changes.Add(CreateSequenceDeleteChange(
                actionCode,
                "/Game/GameActor2D/Misaka/Material/" + actionCode + "/F" + frame + ".F" + frame));
        }
    }

    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    var roots = UnrealSyncSelectionTreeBuilder.FromSequenceChanges(
        changes,
        UnrealBridgePublishSupportPolicy.CanExecute,
        selectPendingByDefault: false);
    viewModel.SetPublishSelectionTree(roots, changes);

    var leafIds = viewModel.SelectionTreeRoots
        .SelectMany(root => root.Children)
        .Select(child => child.StableId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    AssertEqual(actionCount * framesPerAction, leafIds.Count);

    // ApplyPublishFilter 每次都给 VisibleChildren 赋一个新数组，
    // 所以这个计数恰好等于「重算跑了几趟」。
    var recomputeCounter = new int[1];
    foreach (var root in viewModel.SelectionTreeRoots)
    {
        root.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UnrealSyncSelectionTreeItem.VisibleChildren))
            {
                recomputeCounter[0]++;
            }
        };
    }

    viewModel.RestoreSelectionState(leafIds);

    // 收尾统一重算一次，每个根被赋一次 VisibleChildren。
    // 修复前是 720 个叶子各触发一轮全量过滤 = 720 x 12 = 8640。
    AssertEqual(viewModel.SelectionTreeRoots.Count, recomputeCounter[0]);

    // 压制通知不能把勾选本身也压没了。
    AssertEqual(leafIds.Count, viewModel.GetSelectedStableIds().Count);
}


static void AssetClassNeverCarriesPointerIntoHash()
{
    // Unreal 的 asset_class_path 是 TopLevelAssetPath 结构体，str() 出来带对象内存地址，
    // 每次导出都不一样。它会进 ownedAssets 的 payload 参与内容哈希，于是同一个资产
    // 每次检测都被判成「有变化」：同步前的最终比对永远通不过，点了同步什么也不会发生，
    // 差异列表还原封不动留在那。实测一次导出里有 873 个这样的值。
    const string first =
        "<Struct 'TopLevelAssetPath' (0x000001C0DE94853C) {package_name: \"/Script/Paper2D\", asset_name: \"PaperSprite\"}>";
    const string second =
        "<Struct 'TopLevelAssetPath' (0x000002FA11223344) {package_name: \"/Script/Paper2D\", asset_name: \"PaperSprite\"}>";

    // 同一个资产、两次导出，归一后必须完全一致。
    AssertEqual("PaperSprite", SequenceFrameIdentity.NormalizeAssetClass(first));
    AssertEqual("PaperSprite", SequenceFrameIdentity.NormalizeAssetClass(second));

    // 干净的类名原样保留，空值不炸。
    AssertEqual("Texture2D", SequenceFrameIdentity.NormalizeAssetClass("Texture2D"));
    AssertEqual(string.Empty, SequenceFrameIdentity.NormalizeAssetClass(null));
    AssertEqual(string.Empty, SequenceFrameIdentity.NormalizeAssetClass("   "));

    // 认不出 asset_name 时至少要把地址剥掉，绝不能让指针进哈希。
    AssertEqual(false, SequenceFrameIdentity.NormalizeAssetClass("<Struct 'X' (0xDEADBEEF) {}>").Contains("0x"));

    // 导出脚本本身也不能再直接 str() 那个结构体。
    var exportScript = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"), Encoding.UTF8);
    AssertEqual(true, exportScript.Contains("_stable_class_text"));
    AssertEqual(true, exportScript.Contains("asset_name"));
}

static void ExportedFrameListIsOneEntryPerKeyframe()
{
    // 一个关键帧就是一帧素材，frame_run 只表示它停留多久。
    // 以前导出侧按 frame_run 把关键帧复制成多份：Misaka 的一技能 13 帧里
    // 有一帧时长 2、一帧时长 3，导出就变成 16 项，而工具箱侧一帧一项永远是 13。
    // 两边条数对不上，多出来的 3 项每次检测都是处理不掉的差异。
    var script = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"), Encoding.UTF8);

    // 不能再按 frame_run 复制关键帧。
    AssertEqual(false, script.Contains("for _ in range(frame_run)"));
    // 时长要作为字段带出来，而不是靠重复条数表达。
    AssertEqual(true, script.Contains("durationFrames"));
    // 播放总长另算：声音通知定位仍然要展开后的帧数。
    AssertEqual(true, script.Contains("playback_frame_count"));

    // 桥接脚本删除后必须以「资产真的不在了」为准，不能只信返回值。
    // 删除整体交给 ZDBridge.PurgeAssets：脚本这一侧做不可靠。
    // delete_asset 只要有引用方就拒删；ForceDeleteObjects 只置空硬引用，
    // 而图集是用 TSoftObjectPtr 挂着 Sprite 的，强删够不着；
    // Python 更问不出「引用是硬是软」——find_package_referencers 不返回依赖种类。
    var syncScript = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "sync_character_sequences.py"), Encoding.UTF8);
    AssertEqual(true, syncScript.Contains("purge_assets"));
    // 插件缺席时退回普通删除并如实汇报，不能整批失败。
    AssertEqual(true, syncScript.Contains("PurgeAssets unavailable"));
    // 删不掉时要报出还拽着它的引用方，并区分硬/软。
    AssertEqual(true, syncScript.Contains("hard="));
    AssertEqual(true, syncScript.Contains("soft="));

    // 这些实现已经搬进 C++，脚本里不该再留自己那一套。
    foreach (var removed in new[]
             {
                 "_try_delete_asset", "_clear_references_to", "_detach_reference",
                 "_zd_force_delete", "_deletion_rank", "collect_garbage",
             })
    {
        AssertEqual(false, syncScript.Contains(removed));
    }

    // C++ 侧必须真正做到这几件脚本做不到的事。
    var bridge = File.ReadAllText(
        @"I:\UnrealProject_Moon\SRC_REPO\CrossingVoid\Plugins\ZDBridge\Source\ZDBridge\Private\ZDBridgeLibrary.cpp",
        Encoding.UTF8);
    AssertEqual(true, bridge.Contains("UZDBridgeLibrary::PurgeAssets"));
    // 区分硬/软引用，这是 Python 问不出来的。
    AssertEqual(true, bridge.Contains("EDependencyQuery::Hard"));
    AssertEqual(true, bridge.Contains("EDependencyQuery::Soft"));
    // 复核靠 FindObject + DoesPackageExist，不信任何返回值。
    AssertEqual(true, bridge.Contains("PurgeAssetIsGone"));
    // ForceDeleteObjects 一遍删不干净：实测 8 个只删掉 7 个，重试一次才成功。
    AssertEqual(true, bridge.Contains("MaxPasses"));
    // AST_GameActor2D 是目录级的 PrimaryAssetLabel，不是 Sprite Atlas，
    // 没有槽位可摘；那段基于错误假设的代码不该再回来。
    AssertEqual(false, bridge.Contains("PaperSpriteAtlas"));
}

static void SyncProgressBandsCoverEveryPhase()
{
    // 以前进度是写死的：导出完直接跳 40，桥接 58，复扫 86，
    // 而两次 Unreal 导出根本没接进度回调 —— 一次同步里有两段各十几秒的纯静默。
    var plan = WorkflowProgressPlan.ForSequenceSync(includesBackup: false);
    var preflight = plan[WorkflowProgressPlan.PreflightExport];
    var bridge = plan[WorkflowProgressPlan.BridgeExecute];
    var rescan = plan[WorkflowProgressPlan.RescanExport];

    // 从 0 开始、首尾相接、不留空洞。
    AssertEqual(0d, preflight.Start);
    AssertEqual(true, Math.Abs(preflight.End - bridge.Start) < 0.001);
    AssertEqual(true, Math.Abs(bridge.End - rescan.Start) < 0.001);
    // 收尾留 2%，复扫结束时不该已经顶到 100。
    AssertEqual(true, rescan.End > 95d && rescan.End < 100d);

    // 权重按实测耗时（15s / 13s / 15s）分配：导出两段应当比桥接长。
    AssertEqual(true, preflight.End - preflight.Start > bridge.End - bridge.Start);
    AssertEqual(true, Math.Abs((preflight.End - preflight.Start) - (rescan.End - rescan.Start)) < 0.001);

    // 子进度映射到区间内部，且被夹住。
    AssertEqual(preflight.Start, preflight.At(0));
    AssertEqual(preflight.End, preflight.At(100));
    AssertEqual(preflight.End, preflight.At(140));
    AssertEqual(preflight.Start, preflight.At(-20));

    // 开了备份时它会占掉大半条 —— 实测约 66 秒，确实是最长的一段。
    var withBackup = WorkflowProgressPlan.ForSequenceSync(includesBackup: true);
    var backup = withBackup[WorkflowProgressPlan.Backup];
    AssertEqual(true, backup.End - backup.Start > 50d);
    AssertEqual(true, Math.Abs(withBackup[WorkflowProgressPlan.PreflightExport].End - backup.Start) < 0.001);
    AssertEqual(true, Math.Abs(backup.End - withBackup[WorkflowProgressPlan.BridgeExecute].Start) < 0.001);

    // 未知阶段名不能抛异常，退化成一整条即可。
    AssertEqual(0d, plan["nope"].Start);
}

static void ExportSkipsUnchangedPngFiles()
{
    // 一次同步要跑三趟 Unreal 导出，每趟都把同一批贴图重写一遍 PNG，
    // 而且 AssetExportTask.replace_identical 还是 True（内容相同也照写）。
    var script = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"), Encoding.UTF8);
    AssertEqual(true, script.Contains("_png_is_up_to_date"));
    AssertEqual(true, script.Contains("_package_file_path"));
    // 必须在建导出任务之前就短路掉，否则省不下时间。
    var guardIndex = script.IndexOf("if _png_is_up_to_date(output_path", StringComparison.Ordinal);
    var taskIndex = script.IndexOf("task = unreal.AssetExportTask()", StringComparison.Ordinal);
    AssertEqual(true, guardIndex > 0 && taskIndex > guardIndex);
}

static void PostSyncExportRunsInTheSameEditorSession()
{
    // 一次第五步同步原本要开三次编辑器：同步前导出、桥接同步、复扫导出。
    // 实测每次会话 13-15 秒，其中约 9 秒是纯启动开销。复扫要读的就是那个
    // 刚被自己改过、已经加载好的编辑器，没有理由再开一次。
    // 合并后实测 16 秒完成「同步 + 导出」，对比原先 13 + 15 = 28 秒。
    var syncScript = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "sync_character_sequences.py"), Encoding.UTF8);
    AssertEqual(true, syncScript.Contains("_run_post_sync_export"));
    AssertEqual(true, syncScript.Contains("ZD_POST_SYNC_EXPORT_SCRIPT"));
    // 导出脚本是顶层执行的，必须给独立命名空间，否则两边同名函数会互相覆盖。
    AssertEqual(true, syncScript.Contains("__zd_post_sync_export__"));

    // 结果必须先落盘再导出：导出失败只该让工具箱退回独立导出，不能吃掉同步结果。
    var mainCall = syncScript.LastIndexOf("    main()", StringComparison.Ordinal);
    var exportCall = syncScript.LastIndexOf("    _run_post_sync_export()", StringComparison.Ordinal);
    AssertEqual(true, mainCall > 0 && exportCall > mainCall);

    // 两侧必须用同一套导出环境变量，否则复扫会写到别的清单上。
    var service = new UnrealProjectSyncService();
    const string project = @"C:\Unreal\CrossingVoid.uproject";
    var env = service.BuildExportEnvironment(
        project, UnrealProjectSyncExportScope.CharacterSequences, ["Misaka"]);
    AssertEqual(service.GetExportManifestPath(project, UnrealProjectSyncExportScope.CharacterSequences),
        env["ZD_TOOLBOX_EXPORT_MANIFEST"]);
    AssertEqual("CharacterSequences", env["ZD_TOOLBOX_EXPORT_SCOPE"]);
    AssertEqual(true, env["ZD_TOOLBOX_SELECTED_CHARACTERS"].Contains("Misaka"));
    // 不同范围写不同清单，别名不能撞。
    AssertEqual(false, string.Equals(
        service.GetExportManifestPath(project, UnrealProjectSyncExportScope.CharacterSequences),
        service.GetExportManifestPath(project, UnrealProjectSyncExportScope.CharacterMaterials),
        StringComparison.OrdinalIgnoreCase));

    // 同会话导出失败时脚本只记日志不抛异常，所以必须靠清单写入时间判断能否跳过。
    var host = ReadUnrealSyncWindowSource();
    AssertEqual(true, host.Contains("TrySkipRescanExport"));
    AssertEqual(true, host.Contains("LastWriteTimeUtc"));
}

static void BlankFrameDeletionIsExecutable()
{
    // 护援技的 Flipbook 里有 5 个空白关键帧（sprite 为 null）。它们会被列为删除候选，
    // 但空白帧在 Unreal 里根本没有对应资产，所以 SourceObjectPath 是空的。
    // 以前删除项一律要求对象路径非空，于是全选序列后这 5 条被判成"不可执行"，
    // 整批同步被"包含尚未完成重定向的同步项"拦下 —— 而且当时日志里一条线索都没有。
    var separator = SequenceFrameIdentity.SemanticPayloadSeparator.ToString();
    var blankPayload = string.Join(separator, "Sub", "1", "1", "True");

    var blankUnrealItem = new UnrealBridgeSnapshotItem(
        SequenceFrameIdentity.BuildFrameStableId("Sub", 0) + ":delete",
        SequenceFrameIdentity.BuildActionStableId("Sub"),
        UnrealBridgeModule.SequenceFrames,
        "护援技 第 1 帧（旧）",
        "HASH-BLANK",
        blankPayload,
        string.Empty,
        string.Empty,   // 空白帧没有对象路径
        string.Empty,
        "空白帧");
    var blankDelete = new UnrealBridgeChange(
        blankUnrealItem.StableId,
        UnrealBridgeModule.SequenceFrames,
        blankUnrealItem.DisplayName,
        UnrealBridgeChangeKind.DeleteCandidate,
        null,
        blankUnrealItem,
        true,
        SequenceFrameIdentity.BuildActionStableId("Sub"));

    // 空白帧的"删除"靠同步时重建 Flipbook 完成，必须算可执行。
    AssertEqual(true, UnrealBridgePublishSupportPolicy.CanExecute(blankDelete));

    // 非空白帧仍然要求有对象路径，否则删无可删。
    var brokenPayload = string.Join(separator, "Sub", "1", "2", "False");
    var brokenItem = blankUnrealItem with { PayloadJson = brokenPayload, SourceObjectPath = string.Empty };
    AssertEqual(false, UnrealBridgePublishSupportPolicy.CanExecute(blankDelete with { UnrealItem = brokenItem }));

    // 有路径的普通删除项照常可执行。
    var normalItem = brokenItem with { SourceObjectPath = "/Game/GameActor2D/Misaka/Material/Sub/Old.Old" };
    AssertEqual(true, UnrealBridgePublishSupportPolicy.CanExecute(blankDelete with { UnrealItem = normalItem }));

    // 判定两种载荷格式都要认：Unreal 侧是 \u001f 拼接，工具箱侧是 JSON。
    AssertEqual(true, SequenceFrameIdentity.IsBlankFramePayload(blankPayload));
    AssertEqual(false, SequenceFrameIdentity.IsBlankFramePayload(brokenPayload));
    AssertEqual(true, SequenceFrameIdentity.IsBlankFramePayload("{\"isBlank\":\"true\"}"));
    AssertEqual(false, SequenceFrameIdentity.IsBlankFramePayload("{\"isBlank\":\"false\"}"));
    AssertEqual(false, SequenceFrameIdentity.IsBlankFramePayload(null));
    AssertEqual(false, SequenceFrameIdentity.IsBlankFramePayload("{ 坏 JSON"));

    // 导出同理：commandlet 只要编辑器在别处报过错就返回非 0。
    // 实测工程里 Misaka_AnimBP 有个 Play Sequence 节点指向已不存在的序列，
    // 于是每次检测都被判成「导出失败」，而日志里明写着 Python script executed successfully。
    // 导出成没成功，以清单为准。
    var syncService = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Services", "UnrealProjectSyncService.cs"), Encoding.UTF8);
    AssertEqual(true, syncService.Contains("manifestIsFresh"));
    AssertEqual(true, syncService.Contains("TryGetLastWriteUtc"));
    // 清单缺失或不是这一轮写的，才是真失败。
    AssertEqual(true, syncService.Contains("manifest is null || !manifestIsFresh"));

    // 合并会话之后，同步结果先落盘、复扫导出后跑，编辑器自己还会做资产校验。
    // 这些后续动作报错会把进程退出码带成非 0，但同步本身已经完成 ——
    // 实测就出现过 16/16 全成功却被判成"结果文件错误地标记为成功"而整批失败。
    var executor = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Services", "UnrealBridgeExecutorService.cs"), Encoding.UTF8);
    AssertEqual(false, executor.Contains("但结果文件错误地标记为成功"));
    AssertEqual(true, executor.Contains("ProcessExitWarning"));
    // 告警必须带上进程输出，否则等于什么线索都没留。
    AssertEqual(true, executor.Contains("进程输出"));

    // 第五步不碰角色蓝图：绑定序列槽位是下一步的职责。
    // 而且 UBlueprint 的 generated_class 在这个引擎版本上并非可脚本化属性，
    // 硬写会让第一个带蓝图属性的动作（DefAtk）直接失败，拖垮整批同步。
    var syncScript2 = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "sync_character_sequences.py"), Encoding.UTF8);
    AssertEqual(false, syncScript2.Contains("_write_blueprint_sequence_slot"));
    AssertEqual(false, syncScript2.Contains("_blueprint_default_object"));
    AssertEqual(false, syncScript2.Contains("generated_class"));
    // 但计划字段要留着，下一步还要用。
    AssertEqual(true, syncScript2.Contains("'blueprintProperty'"));
    AssertEqual(true, syncScript2.Contains("'blueprintFormSlotIndex'"));

    // 中止同步时必须在日志里留下原因，否则事后完全查不出为什么没跑。
    var host = ReadUnrealSyncWindowSource();
    foreach (var reason in new[]
             {
                 "reason=no-detection", "reason=selection-lost", "reason=unsupported",
                 "reason=nothing-executable", "reason=changes-drifted",
             })
    {
        AssertEqual(true, host.Contains(reason));
    }
}

static void MatchingSidesAreNotConflicts()
{
    // 有基线条目时，判定只看「相对基线变没变」，从不看两侧当前是否已经一致。
    // Misaka 有 6 个动作的 fps 曾经两边不同（12 vs 15），基线因此记下两个不同的哈希；
    // 同步把两边弄一致之后，这 6 个被永久判成冲突，差异列表再也归不了零。
    var payload = SequenceFrameIdentity.BuildActionPayload("Death", 12);
    var toolbox = new UnrealBridgeSnapshotItem(
        SequenceFrameIdentity.BuildActionStableId("Death"),
        $"module:{UnrealBridgeModule.SequenceFrames}",
        UnrealBridgeModule.SequenceFrames,
        "死亡", "SAME-HASH", payload, string.Empty, string.Empty, string.Empty, "Death");
    var unrealItem = toolbox with { };

    // 基线停在两侧还不一致的年代。
    var baseline = new UnrealBridgeSyncState
    {
        HashScheme = UnrealBridgeSyncState.SourceFileHashScheme,
        CharacterCode = "Misaka",
        UnrealProjectPath = @"C:\Unreal\CrossingVoid.uproject",
    };
    baseline.Entries[toolbox.StableId] = new UnrealBridgeSyncStateEntry(
        "OLD-TOOLBOX", "OLD-UNREAL", string.Empty, string.Empty, string.Empty, string.Empty);

    var changes = new UnrealBridgeDiffService().Compare(
        new UnrealBridgeSnapshot("Misaka", [toolbox]),
        new UnrealBridgeSnapshot("Misaka", [unrealItem]),
        UnrealBridgeDirection.PublishToUnreal,
        baseline);
    // 两侧逐字节相同：没有东西可同步，也谈不上冲突。
    AssertEqual(
        UnrealBridgeChangeKind.Unchanged,
        changes.Single(item => item.StableId == toolbox.StableId).Kind);

    // 两侧真不一致时，冲突判定照旧。
    var drifted = unrealItem with { ContentHash = "DIFFERENT-HASH" };
    var conflictChanges = new UnrealBridgeDiffService().Compare(
        new UnrealBridgeSnapshot("Misaka", [toolbox]),
        new UnrealBridgeSnapshot("Misaka", [drifted]),
        UnrealBridgeDirection.PublishToUnreal,
        baseline);
    AssertEqual(
        UnrealBridgeChangeKind.Conflict,
        conflictChanges.Single(item => item.StableId == toolbox.StableId).Kind);
}

static void OrphanSequencesAreDetachedNotDeleted()
{
    // Misaka 的动画源上挂着一条 /Game/ZDBridgeTest/Test_Sequence——它在角色目录之外，
    // 按目录扫描永远看不到。PaperZD 的动画源没有列表属性，"注册"就是序列自身的
    // AnimSource 指针，所以必须从动画源反查，也只能靠清空那个指针来移除。
    const string orphanPath = "/Game/ZDBridgeTest/Test_Sequence.Test_Sequence";
    var orphanId = SequenceFrameIdentity.BuildOrphanSequenceStableId(orphanPath);
    AssertEqual(true, SequenceFrameIdentity.IsOrphanSequenceStableId(orphanId));
    // 不能和帧、占用资产的身份混淆。
    AssertEqual(false, SequenceFrameIdentity.IsFrameStableId(orphanId));
    AssertEqual(false, SequenceFrameIdentity.IsOwnedAssetStableId(orphanId));

    var unrealItem = new UnrealBridgeSnapshotItem(
        orphanId,
        SequenceFrameIdentity.OrphanGroupStableId,
        UnrealBridgeModule.SequenceFrames,
        "非规范序列 · Test_Sequence",
        "HASH-ORPHAN",
        "Test_Sequence",
        string.Empty,
        orphanPath,
        string.Empty,
        "Test_Sequence");
    var change = new UnrealBridgeChange(
        orphanId,
        UnrealBridgeModule.SequenceFrames,
        unrealItem.DisplayName,
        UnrealBridgeChangeKind.DeleteCandidate,
        null,
        unrealItem,
        true,
        SequenceFrameIdentity.OrphanGroupStableId);

    // 有对象路径的删除候选：可执行。
    AssertEqual(true, UnrealBridgePublishSupportPolicy.CanExecute(change));

    // 计划只带解绑路径，不产生任何动作——只勾非规范序列也是合法的一批。
    var (character, root) = CreateSequenceCharacterWithFrames("Click", 1);
    try
    {
        var plan = new UnrealBridgeSequencePublishService()
            .BuildSequenceSyncPlan(character, @"C:\Unreal\CrossingVoid.uproject", [change]);
        AssertEqual(0, plan.Actions.Count);
        AssertSequence([orphanPath], plan.DetachSequenceObjectPaths.ToArray());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    // 大小写对齐要连着改两次名，中间那个 *__ZDCaseMigration 一旦被引用方记进包里、
    // 而重定向器随后被清理，引用就永久悬空——实测打断了 Misaka_AnimBP 的 Play Sequence 节点，
    // 此后每次导出编辑器都报 "references an unknown sequence" 并让 commandlet 返回非 0。
    var caseScript = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "sync_character_sequences.py"), Encoding.UTF8);
    AssertEqual(true, caseScript.Contains("_fixup_redirectors"));
    AssertEqual(true, caseScript.Contains("fixup_referencers"));
    // 修复必须发生在两次改名之后，否则中间名仍会被记下来。
    var secondRename = caseScript.IndexOf("_rename_asset(temporary_package, target_package", StringComparison.Ordinal);
    var fixupCall = caseScript.IndexOf("_fixup_redirectors([package, temporary_package]", StringComparison.Ordinal);
    AssertEqual(true, secondRename > 0 && fixupCall > secondRename);

    // 非规范序列必须能进选择树，否则它只体现在计数里、列表却是空的。
    var workspace = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Models", "UnrealSyncWorkspaceModels.cs"), Encoding.UTF8);
    AssertEqual(true, workspace.Contains("IsOrphanSequenceStableId(change.StableId)"));
    // 分组表头不能作为快照条目产出，否则它自己会被判成一条待删除。
    var snapshot = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Services", "UnrealBridgeSemanticSnapshotService.cs"), Encoding.UTF8);
    AssertEqual(false, snapshot.Contains("Add(items, SequenceFrameIdentity.OrphanGroupStableId"));

    // 脚本侧只解绑，绝不删资产：串错位置的序列往往仍是有用素材。
    var syncScript = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "UnrealBridge", "sync_character_sequences.py"), Encoding.UTF8);
    AssertEqual(true, syncScript.Contains("detach_sequences_from_animation_source"));
    AssertEqual(true, syncScript.Contains("detachSequenceObjectPaths"));

    // 导出侧必须从动画源反查，而不是靠目录扫描。
    var exportScript = File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "Tools", "Unreal", "export_zd_assets.py"), Encoding.UTF8);
    AssertEqual(true, exportScript.Contains("_orphan_animation_sequences"));
    AssertEqual(true, exportScript.Contains("scan_animation_source"));
    AssertEqual(true, exportScript.Contains("orphanSequences"));

    // C++ 侧解绑不能顺手删资产。
    var bridge = File.ReadAllText(
        @"I:\UnrealProject_Moon\SRC_REPO\CrossingVoid\Plugins\ZDBridge\Source\ZDBridge\Private\ZDBridgeLibrary.cpp",
        Encoding.UTF8);
    var detachStart = bridge.IndexOf("UZDBridgeLibrary::DetachSequencesFromAnimationSource", StringComparison.Ordinal);
    AssertEqual(true, detachStart > 0);
    var detachBody = bridge[detachStart..];
    AssertEqual(true, detachBody.Contains("SetAnimSource(nullptr)"));
    AssertEqual(false, detachBody.Contains("ForceDeleteObjects"));
    AssertEqual(false, detachBody.Contains("DeleteAsset"));
}

static void SyncCompletionLeavesVisibleFeedback()
{
    // 同步刚结束时中栏一片空白：CompletePublishOperation 把 _hasImportDetection 置假，
    // 结果面板的显示条件要求它为真，于是整块直接折叠——
    // 刚跑完一次成功的同步，界面却像什么都没发生过。刷新一次才又有内容。
    // 现在中栏由 WorkspaceState 统一决定，「没有差异」是一个明确的状态，
    // 不再是「所有面板的条件都不满足」这种没人认领的组合。
    var viewModel = new UnrealProjectSyncViewModel(new UnrealProjectSyncService())
    {
        IsEngineToToolbox = false,
    };
    viewModel.SelectedPublishStage = viewModel.PublishStages
        .First(stage => stage.Stage == UnrealBridgePublishStage.ZdAnimationTracks);
    viewModel.ReturnToWorkflowStep(5);

    var payload = SequenceFrameIdentity.BuildActionPayload("Click", 12);
    var item = new UnrealBridgeSnapshotItem(
        SequenceFrameIdentity.BuildActionStableId("Click"),
        $"module:{UnrealBridgeModule.SequenceFrames}",
        UnrealBridgeModule.SequenceFrames,
        "点击", "SAME", payload, string.Empty, string.Empty, string.Empty, "Click");
    var unchanged = new UnrealBridgeChange(
        item.StableId,
        UnrealBridgeModule.SequenceFrames,
        item.DisplayName,
        UnrealBridgeChangeKind.Unchanged,
        item,
        item,
        false,
        item.StableId);

    // 复扫结果：检查过内容、没有剩余差异。
    viewModel.SetPublishSelectionTree([], [unchanged]);
    AssertEqual(true, viewModel.HasContentDetection);

    viewModel.CompletePublishOperation(executedCount: 7, deferredCount: 0);

    // 结果面板必须还在，否则中栏什么都不显示。
    AssertEqual(true, viewModel.HasContentDetection);
    AssertEqual(UnrealSyncWorkspaceState.NoChanges, viewModel.WorkspaceState);
    AssertEqual(Visibility.Visible, viewModel.WorkspacePlaceholderVisibility);
    AssertEqual(false, viewModel.IsWorkspacePlaceholderError);
    // 复扫的统计要保留，不能归零成"共检查 0 项"。
    AssertEqual(true, viewModel.DetectionResultSummaryText.Contains("共检查 1 项", StringComparison.Ordinal));
    // 执行结果要看得见。
    AssertEqual(Visibility.Visible, viewModel.ImportResultVisibility);
    AssertEqual(true, viewModel.ImportResultMessage.Contains("7", StringComparison.Ordinal));
    // 但同步按钮不能因此被误启用：树已清空，没有可执行的选择。
    AssertEqual(false, viewModel.HasPublishSelection);
    AssertEqual(false, viewModel.CanStartPublish);
}

// ---------------------------------------------------------------------------
// 同步台冒烟：用工具箱自己的服务，对真实工作区和真实 Unreal 工程跑一遍检测。
//
// 回归用例跑的是构造数据，验证不了「真项目里到底能不能用」。这个入口把
// MainWindow 检测流程里的那几步原样搬过来（导出 -> 建双端快照 -> 比对），
// 不经过界面，所以能在命令行里反复跑。
//
//     dotnet run -- smoke [工作区] [引擎exe] [uproject]
// ---------------------------------------------------------------------------
static int RunUnrealSyncSmoke(string[] args)
{
    var settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CrossingVoidZDTool", "settings.json");
    string? Setting(string name)
    {
        if (!File.Exists(settingsPath)) return null;
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath, Encoding.UTF8));
        return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    // 开关和位置参数分开，免得 --promote 被当成工作区路径。
    var positional = args.Where(item => !item.StartsWith("--", StringComparison.Ordinal)).ToArray();
    var workspace = positional.ElementAtOrDefault(0) ?? Setting("ProjectRootPath") ?? string.Empty;
    var enginePath = positional.ElementAtOrDefault(1) ?? Setting("UnrealEnginePath") ?? string.Empty;
    var projectPath = positional.ElementAtOrDefault(2) ?? Setting("UnrealProjectPath") ?? string.Empty;
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine($"工作区 : {workspace}");
    Console.WriteLine($"引擎   : {enginePath}");
    Console.WriteLine($"工程   : {projectPath}");
    if (!Directory.Exists(workspace) || !File.Exists(projectPath))
    {
        Console.WriteLine("路径无效，无法冒烟。");
        return 2;
    }

    var workspaceService = new CharacterWorkspaceService();

    // 需要至少两个已完成角色才能验证「切换角色后同步台还能不能用」。
    // 只有一个时，把一个草稿转正——这是界面上本来就有的操作，不是测试专用后门。
    if (args.Contains("--promote", StringComparer.OrdinalIgnoreCase))
    {
        var draft = workspaceService.LoadCharacters(workspace)
            .FirstOrDefault(item => !item.IsCompleted);
        if (draft is not null)
        {
            var promoted = workspaceService.SetCompleted(draft, true);
            Console.WriteLine($"已把草稿 {promoted.Code} 转为已完成：{promoted.FolderPath}");
        }
    }

    var characters = workspaceService.LoadCharacters(workspace)
        .Where(character => character.IsCompleted)
        .OrderBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    Console.WriteLine($"已完成角色：{characters.Length} 个 —— {string.Join("、", characters.Select(item => $"{item.Code}/{item.Name}"))}");
    if (characters.Length == 0)
    {
        Console.WriteLine("没有已完成角色，无法冒烟。");
        return 2;
    }

    var syncService = new UnrealProjectSyncService();
    var cacheService = new UnrealSyncSessionCacheService();
    var failures = 0;

    foreach (var character in characters)
    {
        Console.WriteLine();
        Console.WriteLine($"===== {character.Code} / {character.Name}");
        try
        {
            // 按真实流程走：每一步用自己的导出范围，导完立刻比对。
            // 先把两个范围都导出再统一比对是错的——素材比对会读到序列范围的清单，
            // 于是所有素材都被算成「Unreal 里没有」，凭空多出上百条新增。
            var baseline = new UnrealBridgeStateService().Load(character, projectPath);
            var toolboxSnapshot = new UnrealBridgeToolboxSnapshotService().BuildForSynchronization(character);
            foreach (var (label, scope, modules) in new (string, UnrealProjectSyncExportScope, UnrealBridgeModule[])[]
                     {
                         ("第三步 素材", UnrealProjectSyncExportScope.CharacterMaterials,
                             [UnrealBridgeModule.BaseMaterials, UnrealBridgeModule.Voices]),
                         ("第五步 序列", UnrealProjectSyncExportScope.CharacterSequences,
                             [UnrealBridgeModule.SequenceFrames]),
                     })
            {
                var run = syncService.ExportProjectCharactersAsync(
                    enginePath, projectPath, [character.Code], null, CancellationToken.None, scope)
                    .GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(run.Warning))
                {
                    Console.WriteLine($"  导出提醒：{run.Warning}");
                }

                var check = syncService.Check(enginePath, projectPath);
                var candidates = check.CharacterCandidates;
                // 多角色的关键风险：给这个角色导出会不会把别人从清单里挤掉。
                foreach (var other in characters)
                {
                    var present = candidates.Any(item =>
                        string.Equals(item.Code, other.Code, StringComparison.OrdinalIgnoreCase));
                    if (!present)
                    {
                        Console.WriteLine($"  !! {label} 导出后，清单里找不到 {other.Code} 了");
                        failures++;
                    }
                }

                var candidate = candidates.FirstOrDefault(item =>
                    string.Equals(item.Code, character.Code, StringComparison.OrdinalIgnoreCase));
                if (candidate is null)
                {
                    Console.WriteLine($"  !! {label}：Unreal 侧找不到这个角色的 Item 资产");
                    failures++;
                    continue;
                }

                var unrealSnapshot = new UnrealBridgeSemanticSnapshotService().Build(candidate);
                var left = toolboxSnapshot with { Items = toolboxSnapshot.Items.Where(item => modules.Contains(item.Module)).ToArray() };
                var right = unrealSnapshot with { Items = unrealSnapshot.Items.Where(item => modules.Contains(item.Module)).ToArray() };
                var changes = new UnrealBridgeDiffService()
                    .Compare(left, right, UnrealBridgeDirection.PublishToUnreal, baseline)
                    .ToArray();
                Console.WriteLine(
                    $"  {label}（工具箱 {left.Items.Count} / Unreal {right.Items.Count}）：" +
                    $"共 {changes.Length}，新增 {changes.Count(c => c.Kind == UnrealBridgeChangeKind.Added)}，" +
                    $"更新 {changes.Count(c => c.Kind is UnrealBridgeChangeKind.Updated or UnrealBridgeChangeKind.Renamed)}，" +
                    $"待删 {changes.Count(c => c.Kind == UnrealBridgeChangeKind.DeleteCandidate)}，" +
                    $"冲突 {changes.Count(c => c.Kind == UnrealBridgeChangeKind.Conflict)}，" +
                    $"无差异 {changes.Count(c => c.Kind == UnrealBridgeChangeKind.Unchanged)}");
                foreach (var change in changes.Where(c => c.Kind != UnrealBridgeChangeKind.Unchanged).Take(8))
                {
                    Console.WriteLine($"      [{change.Kind}] {change.DisplayName}");
                }
            }

            // 缓存必须落在这个角色自己的目录里
            var cacheFolder = UnrealSyncSessionCacheService.GetCacheFolderPath(character);
            var stepFiles = Directory.Exists(cacheFolder)
                ? Directory.GetFiles(cacheFolder, "sync-*.json").Length
                : 0;
            Console.WriteLine($"  分步缓存：{cacheFolder}（{stepFiles} 个）");
            for (var step = UnrealSyncWorkflow.MinStep; step <= UnrealSyncWorkflow.MaxStep; step++)
            {
                var result = cacheService.LoadStep(character, projectPath, character.Code, step);
                if (result.Status == UnrealSyncSessionCacheLoadStatus.Invalid)
                {
                    Console.WriteLine($"    !! 第 {step} 步缓存损坏：{result.ErrorMessage}");
                    failures++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  !! 异常：{ex.Message}");
            failures++;
        }
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0 ? "冒烟通过。" : $"冒烟发现 {failures} 处问题。");
    return failures == 0 ? 0 : 1;
}

// ---------------------------------------------------------------------------
// 一次性迁移：把已有角色数据里的整机绝对路径改写成 $char/ 可移植写法。
//
// 新代码读写时会自动做这件事，但已完成的角色得主动过一遍——顺便把
// SAO_kirito 那种指向旧机器（D:\NewData\...）的死图标路径修回来。
// 走的是工具箱自己的读写服务，所以落盘格式和平时完全一致，也会照常留备份。
//
//     dotnet run -- migrate-paths [工作区]
// ---------------------------------------------------------------------------
static int RunPortablePathMigration(string[] args)
{
    Console.OutputEncoding = Encoding.UTF8;
    var settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CrossingVoidZDTool", "settings.json");
    string? Setting(string name)
    {
        if (!File.Exists(settingsPath)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath, Encoding.UTF8));
        return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    var workspace = args.ElementAtOrDefault(0) ?? Setting("ProjectRootPath") ?? string.Empty;
    Console.WriteLine($"工作区：{workspace}");
    if (!Directory.Exists(workspace))
    {
        Console.WriteLine("工作区路径无效。");
        return 2;
    }

    var characters = new CharacterWorkspaceService().LoadCharacters(workspace)
        .OrderBy(item => item.IsCompleted ? 0 : 1)
        .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var dataService = new CharacterToolboxDataService();
    var rewritten = 0;
    var repaired = 0;

    foreach (var character in characters)
    {
        var files = new List<string>();
        var toolboxDataPath = Path.Combine(character.ToolFolderPath, "ZDToolboxData.json");
        if (File.Exists(toolboxDataPath))
        {
            files.Add(toolboxDataPath);
        }

        var cacheFolder = UnrealSyncSessionCacheService.GetCacheFolderPath(character);
        if (Directory.Exists(cacheFolder))
        {
            files.AddRange(Directory.GetFiles(cacheFolder, "sync-*.json"));
        }

        var before = files.ToDictionary(path => path, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

        // 角色数据：读进来（相对->绝对 + 修死路径）再原样写回去（绝对->相对）。
        if (File.Exists(toolboxDataPath))
        {
            dataService.Update(character, _ => { });
        }

        // 分步缓存：同样过一遍读写。缓存本身是可再生的，读坏了就跳过。
        var cacheService = new UnrealSyncSessionCacheService();
        foreach (var cachePath in files.Where(path => !string.Equals(path, toolboxDataPath, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var raw = File.ReadAllText(cachePath, Encoding.UTF8);
                var folder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(cachePath))));
                var portable = ToolboxPortablePathService.ToPortableJson(
                    ToolboxPortablePathService.ToAbsoluteJson(raw, folder), folder);
                if (!string.Equals(raw, portable, StringComparison.Ordinal))
                {
                    File.WriteAllText(cachePath, portable, new UTF8Encoding(false));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  跳过 {Path.GetFileName(cachePath)}：{ex.Message}");
            }
        }

        var touched = new List<string>();
        foreach (var (path, text) in before)
        {
            if (!File.Exists(path)) continue;
            var after = File.ReadAllText(path);
            if (string.Equals(text, after, StringComparison.Ordinal)) continue;

            var wasAbsolute = CountAbsolutePaths(text);
            var nowAbsolute = CountAbsolutePaths(after);
            touched.Add($"{Path.GetFileName(path)}（绝对路径 {wasAbsolute} -> {nowAbsolute}）");
            rewritten++;
            repaired += Math.Max(0, wasAbsolute - nowAbsolute);
        }

        if (touched.Count > 0)
        {
            Console.WriteLine($"{(character.IsCompleted ? "已完成" : "草稿  ")} {character.Code}");
            foreach (var line in touched)
            {
                Console.WriteLine($"  {line}");
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine($"共改写 {rewritten} 个文件，收编 {repaired} 条绝对路径。");
    return 0;

    static int CountAbsolutePaths(string json)
    {
        var count = 0;
        for (var i = 0; i + 2 < json.Length; i++)
        {
            // JSON 里的 "C:\..." 转义后是 C:\\ 两个反斜杠；网络路径同理。
            if (char.IsLetter(json[i]) && json[i + 1] == ':' && json[i + 2] == '\\')
            {
                count++;
            }
        }

        return count;
    }
}



/// <summary>
/// 假的界面侧。记下控制器要求检测了哪几步、说了什么话，
/// 这样六步编排可以整段跑起来断言，而不用去匹配 MainWindow 的源码文本。
/// </summary>
static string ReadAllProjectXaml()
{
    // 界面结构要按「整个界面」来断言，不能只盯 MainWindow.xaml 一个文件。
    //
    // 这批断言以前逐字读 MainWindow.xaml，等于规定所有界面都必须写在那一个
    // 5522 行的文件里：把十二个自建遮罩层抽成 Controls/*.xaml 会让它们集体失败，
    // 而功能一点没坏，只是字符串搬了家。测试不该把反模式钉死。
    //
    // 断言「不存在」的那几条也一并受益——它们本来就该保证整个界面里都没有。
    var files = Directory
        .EnumerateFiles(Directory.GetCurrentDirectory(), "*.xaml", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (files.Length == 0)
    {
        throw new InvalidOperationException("没有找到任何 XAML 文件，工作目录可能不对（请在仓库根目录运行）。");
    }

    return string.Join(Environment.NewLine, files.Select(path => File.ReadAllText(path, Encoding.UTF8)));
}

sealed class FakeWorkflowHost : IUnrealSyncWorkflowHost
{
    public List<int> DetectedSteps { get; } = [];

    public List<UnrealSyncNotice> Notices { get; } = [];

    public List<string> Logs { get; } = [];

    /// <summary>让用例决定这一步检测「跑出了什么结果」。</summary>
    public Func<int, Task>? OnDetect { get; set; }

    public Task RunStepDetectionAsync(int step)
    {
        DetectedSteps.Add(step);
        return OnDetect?.Invoke(step) ?? Task.CompletedTask;
    }

    public void Notify(UnrealSyncNotice notice) => Notices.Add(notice);

    public void Log(string message) => Logs.Add(message);
}

sealed class CollectingLogSink(List<string> messages) : IToolboxLogSink
{
    public void Write(ToolboxLogLevel level, string message, Exception? error) => messages.Add(message);
}

sealed class SingleThreadTestSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.Add((d, state));
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    /// <summary>把已排队的续体全部执行掉，模拟 UI 消息循环。</summary>
    public void DrainPending()
    {
        while (_queue.TryTake(out var item))
        {
            item.Callback(item.State);
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _queue.Dispose();
    }
}

