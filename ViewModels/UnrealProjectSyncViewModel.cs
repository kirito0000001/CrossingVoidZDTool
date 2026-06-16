using System.Collections.ObjectModel;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossingVoidZDTool.Services;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class UnrealProjectSyncViewModel : ObservableObject
{
    private readonly UnrealProjectSyncService _syncService;
    private string _enginePath = string.Empty;
    private string _projectPath = string.Empty;
    private string _contentPath = string.Empty;
    private string _targetBaseMaterialContentPath = UnrealProjectSyncService.TargetBaseMaterialContentPath;
    private string _targetZdContentPath = UnrealProjectSyncService.TargetZdContentPath;
    private string _targetCharacterItemContentPath = UnrealProjectSyncService.TargetCharacterItemContentPath;
    private string _linkSkillLibraryObjectPath = UnrealProjectSyncService.LinkSkillLibraryObjectPath;
    private string _targetBaseMaterialDiskPath = string.Empty;
    private string _targetZdDiskPath = string.Empty;
    private string _targetCharacterItemDiskPath = string.Empty;
    private string _linkSkillLibraryDiskPath = string.Empty;
    private string _exportDirectoryPath = string.Empty;
    private string _exportScriptPath = string.Empty;
    private string _exportManifestPath = string.Empty;
    private int _exportedAssetCount;
    private string _exportGeneratedAtText = "尚未导出";
    private bool _hasExportManifest;
    private bool _isEngineToToolbox = true;
    private UnrealProjectSyncCharacterCandidate? _selectedCharacterCandidate;
    private string _selectedCharacterInfoText = "请选择下方角色卡查看素材分类。";
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    private string _statusTitle = "尚未检测";
    private string _statusMessage = "请选择虚幻引擎和目标项目后进行关联检测。";
    private bool _canSync;

    public UnrealProjectSyncViewModel(UnrealProjectSyncService syncService)
    {
        _syncService = syncService;
    }

    public ObservableCollection<UnrealProjectSyncCheckItem> CheckItems { get; } = [];

    public ObservableCollection<UnrealProjectSyncExportAssetView> ExportPreviewAssets { get; } = [];

    public ObservableCollection<UnrealProjectSyncCharacterCandidate> CharacterCandidates { get; } = [];

    public string EnginePath
    {
        get => _enginePath;
        set
        {
            if (SetProperty(ref _enginePath, value))
            {
                Detect();
            }
        }
    }

    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            if (SetProperty(ref _projectPath, value))
            {
                Detect();
            }
        }
    }

    public string ContentPath
    {
        get => _contentPath;
        private set => SetProperty(ref _contentPath, value);
    }

    public string TargetBaseMaterialContentPath
    {
        get => _targetBaseMaterialContentPath;
        private set => SetProperty(ref _targetBaseMaterialContentPath, value);
    }

    public string TargetZdContentPath
    {
        get => _targetZdContentPath;
        private set => SetProperty(ref _targetZdContentPath, value);
    }

    public string TargetCharacterItemContentPath
    {
        get => _targetCharacterItemContentPath;
        private set => SetProperty(ref _targetCharacterItemContentPath, value);
    }

    public string LinkSkillLibraryObjectPath
    {
        get => _linkSkillLibraryObjectPath;
        private set => SetProperty(ref _linkSkillLibraryObjectPath, value);
    }

    public string TargetBaseMaterialDiskPath
    {
        get => _targetBaseMaterialDiskPath;
        private set => SetProperty(ref _targetBaseMaterialDiskPath, value);
    }

    public string TargetZdDiskPath
    {
        get => _targetZdDiskPath;
        private set => SetProperty(ref _targetZdDiskPath, value);
    }

    public string TargetCharacterItemDiskPath
    {
        get => _targetCharacterItemDiskPath;
        private set => SetProperty(ref _targetCharacterItemDiskPath, value);
    }

    public string LinkSkillLibraryDiskPath
    {
        get => _linkSkillLibraryDiskPath;
        private set => SetProperty(ref _linkSkillLibraryDiskPath, value);
    }

    public string ExportDirectoryPath
    {
        get => _exportDirectoryPath;
        private set => SetProperty(ref _exportDirectoryPath, value);
    }

    public string ExportScriptPath
    {
        get => _exportScriptPath;
        private set => SetProperty(ref _exportScriptPath, value);
    }

    public string ExportManifestPath
    {
        get => _exportManifestPath;
        private set => SetProperty(ref _exportManifestPath, value);
    }

    public int ExportedAssetCount
    {
        get => _exportedAssetCount;
        private set => SetProperty(ref _exportedAssetCount, value);
    }

    public string ExportGeneratedAtText
    {
        get => _exportGeneratedAtText;
        private set => SetProperty(ref _exportGeneratedAtText, value);
    }

    public bool HasExportManifest
    {
        get => _hasExportManifest;
        private set => SetProperty(ref _hasExportManifest, value);
    }

    public bool IsEngineToToolbox
    {
        get => _isEngineToToolbox;
        set
        {
            if (SetProperty(ref _isEngineToToolbox, value))
            {
                OnPropertyChanged(nameof(DirectionTitle));
                OnPropertyChanged(nameof(DirectionDescription));
            }
        }
    }

    public string DirectionTitle => IsEngineToToolbox ? "工具箱 ← 虚幻引擎" : "工具箱 → 虚幻引擎";

    public string DirectionDescription => IsEngineToToolbox
        ? "当前制作引擎项目到工具箱的同步页面。"
        : "工具箱到虚幻引擎的同步页面稍后开放。";

    public UnrealProjectSyncCharacterCandidate? SelectedCharacterCandidate
    {
        get => _selectedCharacterCandidate;
        private set
        {
            if (SetProperty(ref _selectedCharacterCandidate, value))
            {
                SelectedCharacterInfoText = value is null
                    ? "请选择下方角色卡查看素材分类。"
                    : BuildSelectedCharacterInfoText(value);
            }
        }
    }

    public string SelectedCharacterInfoText
    {
        get => _selectedCharacterInfoText;
        private set => SetProperty(ref _selectedCharacterInfoText, value);
    }

    public InfoBarSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool CanSync
    {
        get => _canSync;
        private set => SetProperty(ref _canSync, value);
    }

    public void Load(string enginePath, string projectPath)
    {
        _enginePath = enginePath;
        _projectPath = projectPath;
        OnPropertyChanged(nameof(EnginePath));
        OnPropertyChanged(nameof(ProjectPath));
        Detect();
    }

    public void Detect()
    {
        var selectedCodes = CharacterCandidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedCandidateCode = SelectedCharacterCandidate?.Code;
        var result = _syncService.Check(EnginePath, ProjectPath);
        StatusSeverity = result.Severity;
        StatusTitle = result.Title;
        StatusMessage = result.Message;
        CanSync = result.CanSync;
        ContentPath = result.UnrealContentPath;
        TargetBaseMaterialContentPath = result.TargetBaseMaterialContentPath;
        TargetZdContentPath = result.TargetZdContentPath;
        TargetCharacterItemContentPath = result.TargetCharacterItemContentPath;
        LinkSkillLibraryObjectPath = result.LinkSkillLibraryObjectPath;
        TargetBaseMaterialDiskPath = result.TargetBaseMaterialDiskPath;
        TargetZdDiskPath = result.TargetZdDiskPath;
        TargetCharacterItemDiskPath = result.TargetCharacterItemDiskPath;
        LinkSkillLibraryDiskPath = result.LinkSkillLibraryDiskPath;
        ExportDirectoryPath = result.ExportDirectoryPath;
        ExportScriptPath = result.ExportScriptPath;
        ExportManifestPath = result.ExportManifestPath;
        ExportedAssetCount = result.ExportedAssetCount;
        HasExportManifest = result.ExportManifestExists;
        ExportGeneratedAtText = result.ExportGeneratedAt is DateTime generatedAt
            ? generatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            : "尚未导出";
        CheckItems.Clear();
        foreach (var item in result.Items)
        {
            CheckItems.Add(item);
        }

        ExportPreviewAssets.Clear();
        foreach (var asset in result.ExportPreviewAssets)
        {
            ExportPreviewAssets.Add(asset);
        }

        CharacterCandidates.Clear();
        foreach (var candidate in result.CharacterCandidates)
        {
            if (selectedCodes.Contains(candidate.Code))
            {
                candidate.IsSelected = true;
            }

            CharacterCandidates.Add(candidate);
        }

        if (SelectedCharacterCandidate is null)
        {
            SelectedCharacterCandidate = CharacterCandidates.FirstOrDefault();
            return;
        }

        var refreshedSelectedCandidate = CharacterCandidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, selectedCandidateCode, StringComparison.OrdinalIgnoreCase));
        SelectedCharacterCandidate = refreshedSelectedCandidate ?? CharacterCandidates.FirstOrDefault();
    }

    public string GetExportScriptPath()
    {
        var scriptPath = _syncService.GetExportScriptPath();
        Detect();
        return scriptPath;
    }

    public async Task<UnrealProjectSyncExportRunResult> ExportProjectCharactersAsync(
        IReadOnlyCollection<string>? selectedCharacterCodes = null,
        IProgress<ProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _syncService.ExportProjectCharactersAsync(EnginePath, ProjectPath, selectedCharacterCodes, progress, cancellationToken);
        Detect();
        return result;
    }

    public string[] GetSelectedCharacterCodes()
    {
        return CharacterCandidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public int SyncMaterialBucketToToolbox(CharacterCard character, UnrealProjectSyncMaterialBucket bucket)
    {
        return _syncService.SyncMaterialBucketToToolbox(character, bucket);
    }

    public int SyncAllMaterialBucketsToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        return _syncService.SyncAllMaterialBucketsToToolbox(character, candidate);
    }

    public void SyncCharacterInfoToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        _syncService.SyncCharacterInfoToToolbox(character, candidate);
    }

    public int SyncAllSkillsToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        return _syncService.SyncAllSkillsToToolbox(character, candidate);
    }

    public int SyncSkillSlotToToolbox(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncSkillSlotPreview slot)
    {
        return _syncService.SyncSkillSlotToToolbox(character, candidate, slot);
    }

    public int SyncLinkSkillToToolbox(
        CharacterCard character,
        UnrealProjectSyncCharacterCandidate candidate,
        UnrealProjectSyncLinkSkillPreview linkSkill)
    {
        return _syncService.SyncLinkSkillToToolbox(character, candidate, linkSkill);
    }

    public int SyncAllSequenceFramesToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        return _syncService.SyncAllSequenceFramesToToolbox(character, candidate);
    }

    public int SyncSequenceActionToToolbox(CharacterCard character, UnrealProjectSyncSequenceActionPreview action)
    {
        return _syncService.SyncSequenceActionToToolbox(character, action);
    }

    public int SyncAllBuffsToToolbox(CharacterCard character, UnrealProjectSyncCharacterCandidate candidate)
    {
        return _syncService.SyncAllBuffsToToolbox(character, candidate);
    }

    public int SyncBuffToToolbox(CharacterCard character, UnrealProjectSyncBuffPreview buff)
    {
        return _syncService.SyncBuffToToolbox(character, buff);
    }

    public void SelectCharacterCandidate(UnrealProjectSyncCharacterCandidate? candidate)
    {
        SelectedCharacterCandidate = candidate;
    }

    private static string BuildSelectedCharacterInfoText(UnrealProjectSyncCharacterCandidate candidate)
    {
        var info = candidate.CharacterInfo;
        if (!info.HasData)
        {
            return $"未找到角色物品数据：/Game/ITems/CharItemS/Item_{candidate.Code}。当前只显示基础素材和 ZD 素材候选。";
        }

        if (!info.HasItemData)
        {
            return $"已找到角色物品蓝图，但没有读出 ItemData.CharData：{info.ReadMessage}";
        }

        var keywordText = info.KeywordTags.Count == 0 ? "无" : string.Join("、", info.KeywordTags);
        var passiveText = info.PassiveSkills.Count == 0 ? "无" : $"{info.PassiveSkills.Count} 条";
        return
            $"物品资产：{info.AssetName}\n" +
            $"名称：{(string.IsNullOrWhiteSpace(info.Name) ? candidate.Code : info.Name)}\n" +
            $"介绍：{(string.IsNullOrWhiteSpace(info.Description) ? "无" : info.Description)}\n" +
            $"关键词：{keywordText}\n" +
            $"形态上限：{info.FormLimit}，技能详情：{info.SkillCount}，被动介绍：{passiveText}\n" +
            $"数值：速度 {info.Speed} / 生命 {info.Health} / 攻击 {info.Attack} / 物防 {info.PhysicalDefense} / 异防 {info.EnergyDefense} / 暴击 {info.CriticalRate} / 暴伤 {info.CriticalDamage} / 同步率 {info.Synchronize}";
    }
}
