using System.Collections.ObjectModel;
using System;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool;

internal sealed class CharacterSkillsData
{
    public int SchemaVersion { get; set; }

    public ObservableCollection<CharacterSkillEntry> FirstSkill { get; set; } = [];

    public ObservableCollection<CharacterSkillEntry> SecondSkill { get; set; } = [];

    public ObservableCollection<CharacterSkillEntry> UltimateSkill { get; set; } = [];

    public ObservableCollection<CharacterSkillEntry> SupportSkill { get; set; } = [];

    public ObservableCollection<CharacterSkillEntry> ComboSkills { get; set; } = [];
}

internal sealed class CharacterSkillEntry : CrossingVoidZDTool.ViewModels.ObservableObject
{
    public static event EventHandler? AnyEntryEdited;
    [ThreadStatic]
    private static int _editNotificationSuppressions;
    private string _positionName = string.Empty;
    private string _description = string.Empty;
    private string _ptCost = string.Empty;
    private string _autoPriority = string.Empty;
    private string _skillState = "空";
    private string _guardState = "空";
    private string _guardValue = string.Empty;
    private string _trueName = string.Empty;
    private string _attackCapacity = string.Empty;
    private string _iconPath = string.Empty;
    private string _iconUri = string.Empty;
    private string _comboCharacterCode = string.Empty;
    private string _comboCharacterName = string.Empty;
    private bool _isExpanded;
    private bool _canDelete = true;

    public string SyncId { get; set; } = Guid.NewGuid().ToString("N");

    public ObservableCollection<SkillMultiplierLevel> LevelMultipliers { get; set; } = [];

    public string PositionName
    {
        get => _positionName;
        set => SetSkillProperty(ref _positionName, value);
    }

    public string Description
    {
        get => _description;
        set => SetSkillProperty(ref _description, value);
    }

    public string PtCost
    {
        get => _ptCost;
        set => SetSkillProperty(ref _ptCost, value);
    }

    public string AutoPriority
    {
        get => _autoPriority;
        set => SetSkillProperty(ref _autoPriority, value);
    }

    public string SkillState
    {
        get => _skillState;
        set => SetSkillProperty(ref _skillState, value);
    }

    public string GuardState
    {
        get => _guardState;
        set => SetSkillProperty(ref _guardState, value);
    }

    public string GuardValue
    {
        get => _guardValue;
        set => SetSkillProperty(ref _guardValue, value);
    }

    public string TrueName
    {
        get => _trueName;
        set => SetSkillProperty(ref _trueName, value);
    }

    public string AttackCapacity
    {
        get => _attackCapacity;
        set => SetSkillProperty(ref _attackCapacity, value);
    }

    public string IconPath
    {
        get => _iconPath;
        set => SetSkillProperty(ref _iconPath, value);
    }

    public string ComboCharacterCode
    {
        get => _comboCharacterCode;
        set
        {
            if (SetProperty(ref _comboCharacterCode, value))
            {
                OnPropertyChanged(nameof(HasComboCharacter));
                OnPropertyChanged(nameof(ComboCharacterDisplayText));
                RaiseEdited();
            }
        }
    }

    public string ComboCharacterName
    {
        get => _comboCharacterName;
        set
        {
            if (SetProperty(ref _comboCharacterName, value))
            {
                OnPropertyChanged(nameof(HasComboCharacter));
                OnPropertyChanged(nameof(ComboCharacterDisplayText));
                RaiseEdited();
            }
        }
    }

    [JsonIgnore]
    public string IconUri
    {
        get => _iconUri;
        set
        {
            if (SetProperty(ref _iconUri, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasNoIcon));
            }
        }
    }

    [JsonIgnore]
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    [JsonIgnore]
    public bool CanDelete
    {
        get => _canDelete;
        set => SetProperty(ref _canDelete, value);
    }

    [JsonIgnore]
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUri);

    [JsonIgnore]
    public bool HasNoIcon => string.IsNullOrWhiteSpace(IconUri);

    [JsonIgnore]
    public bool HasComboCharacter =>
        !string.IsNullOrWhiteSpace(ComboCharacterCode) ||
        !string.IsNullOrWhiteSpace(ComboCharacterName);

    [JsonIgnore]
    public string ComboCharacterDisplayText
    {
        get
        {
            if (!HasComboCharacter)
            {
                return string.Empty;
            }

            var name = !string.IsNullOrWhiteSpace(ComboCharacterName)
                ? ComboCharacterName
                : !string.IsNullOrWhiteSpace(ComboCharacterCode)
                    ? ComboCharacterCode
                    : "未命名角色";
            return $"连携目标：{name}";
        }
    }

    private void SetSkillProperty(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            RaiseEdited();
        }
    }

    public static IDisposable SuppressEditNotifications()
    {
        _editNotificationSuppressions++;
        return new EditNotificationScope(() => _editNotificationSuppressions--);
    }

    private static void RaiseEdited()
    {
        if (_editNotificationSuppressions > 0)
        {
            return;
        }

        AnyEntryEdited?.Invoke(null, EventArgs.Empty);
    }

    private sealed class EditNotificationScope(Action onDispose) : IDisposable
    {
        private bool _isDisposed;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            onDispose();
        }
    }
}

internal sealed class SkillMultiplierLevel : CrossingVoidZDTool.ViewModels.ObservableObject
{
    public static event EventHandler? AnyMultiplierEdited;
    [ThreadStatic]
    private static int _editNotificationSuppressions;
    private string _physicalMultiplier = string.Empty;
    private string _energyMultiplier = string.Empty;

    public int Level { get; set; }

    public string PhysicalMultiplier
    {
        get => _physicalMultiplier;
        set => SetMultiplierProperty(ref _physicalMultiplier, value);
    }

    public string EnergyMultiplier
    {
        get => _energyMultiplier;
        set => SetMultiplierProperty(ref _energyMultiplier, value);
    }

    private void SetMultiplierProperty(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            RaiseEdited();
        }
    }

    public static IDisposable SuppressEditNotifications()
    {
        _editNotificationSuppressions++;
        return new EditNotificationScope(() => _editNotificationSuppressions--);
    }

    private static void RaiseEdited()
    {
        if (_editNotificationSuppressions > 0)
        {
            return;
        }

        AnyMultiplierEdited?.Invoke(null, EventArgs.Empty);
    }

    private sealed class EditNotificationScope(Action onDispose) : IDisposable
    {
        private bool _isDisposed;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            onDispose();
        }
    }
}
