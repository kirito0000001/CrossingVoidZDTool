using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool;

internal sealed class BuffData
{
    public ObservableCollection<BuffEntry> Buffs { get; set; } = [];

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

internal sealed class BuffFileData
{
    public BuffEntry Buff { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

internal sealed class BuffEntry : CrossingVoidZDTool.ViewModels.ObservableObject
{
    public static event EventHandler? AnyBuffEdited;
    [ThreadStatic]
    private static int _editNotificationSuppressions;

    private int _index;
    private string _userCode = string.Empty;
    private string _generatedCode = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _damageType = string.Empty;
    private string _iconPath = string.Empty;
    private string _iconUri = string.Empty;
    private string _gainType = "增益";
    private string _stacks = string.Empty;
    private string _completeStacks = string.Empty;
    private string _strength = string.Empty;
    private string _completeStrength = string.Empty;
    private string _ownerText = string.Empty;
    private string _taskPriority = "正常";
    private string _triggerTiming = string.Empty;
    private string _conditionSummary = string.Empty;
    private string _readStatus = string.Empty;
    private string _sourceAssetPath = string.Empty;
    private string _draft = string.Empty;

    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, value);
    }

    public string UserCode
    {
        get => _userCode;
        set => SetBuffProperty(ref _userCode, value);
    }

    public string GeneratedCode
    {
        get => _generatedCode;
        set
        {
            if (SetProperty(ref _generatedCode, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Name
    {
        get => _name;
        set => SetBuffProperty(ref _name, value, notifyDisplayProperties: true);
    }

    public string Description
    {
        get => _description;
        set => SetBuffProperty(ref _description, value, notifyDisplayProperties: true);
    }

    public string DamageType
    {
        get => _damageType;
        set => SetBuffProperty(ref _damageType, value);
    }

    public string IconPath
    {
        get => _iconPath;
        set
        {
            if (SetProperty(ref _iconPath, value))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(HasNoIcon));
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

    public string GainType
    {
        get => _gainType;
        set => SetBuffProperty(ref _gainType, value, notifyDisplayProperties: true);
    }

    public string Stacks
    {
        get => _stacks;
        set => SetBuffProperty(ref _stacks, value, notifyDisplayProperties: true);
    }

    public string CompleteStacks
    {
        get => _completeStacks;
        set => SetBuffProperty(ref _completeStacks, value, notifyDisplayProperties: true);
    }

    public string Strength
    {
        get => _strength;
        set => SetBuffProperty(ref _strength, value, notifyDisplayProperties: true);
    }

    public string CompleteStrength
    {
        get => _completeStrength;
        set => SetBuffProperty(ref _completeStrength, value, notifyDisplayProperties: true);
    }

    public string OwnerText
    {
        get => _ownerText;
        set => SetBuffProperty(ref _ownerText, value, notifyDisplayProperties: true);
    }

    public void SetOwnerTextFromToolbox(string value)
    {
        using var notifications = SuppressEditNotifications();
        if (SetProperty(ref _ownerText, value, nameof(OwnerText)))
        {
            OnPropertyChanged(nameof(OwnerDisplayText));
            OnPropertyChanged(nameof(TypeSummaryText));
        }
    }

    public string TaskPriority
    {
        get => _taskPriority;
        set => SetBuffProperty(ref _taskPriority, value, notifyDisplayProperties: true);
    }

    public string TriggerTiming
    {
        get => _triggerTiming;
        set => SetBuffProperty(ref _triggerTiming, value);
    }

    public string ConditionSummary
    {
        get => _conditionSummary;
        set => SetBuffProperty(ref _conditionSummary, value);
    }

    public string ReadStatus
    {
        get => _readStatus;
        set => SetBuffProperty(ref _readStatus, value, notifyDisplayProperties: true);
    }

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(Name)
        ? Name.Trim()
        : !string.IsNullOrWhiteSpace(GeneratedCode)
            ? GeneratedCode.Trim()
            : "未命名 BUFF";

    [JsonIgnore]
    public string OwnerDisplayText => string.IsNullOrWhiteSpace(OwnerText)
        ? "归属未填写"
        : $"归属：{OwnerText.Trim()}";

    [JsonIgnore]
    public string ValueSummaryText => $"层数 {TextOrUnset(Stacks)}~{TextOrUnset(CompleteStacks)} / 强度 {TextOrUnset(Strength)}~{TextOrUnset(CompleteStrength)}";

    [JsonIgnore]
    public string TypeSummaryText => $"{TextOrUnset(GainType)} · {TextOrUnset(DamageType)} · {TextOrUnset(TaskPriority)}";

    [JsonIgnore]
    public string ShortDescription => string.IsNullOrWhiteSpace(Description)
        ? "说明未填写"
        : Description.Trim();

    [JsonIgnore]
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUri);

    [JsonIgnore]
    public bool HasNoIcon => !HasIcon;

    public string SourceAssetPath
    {
        get => _sourceAssetPath;
        set => SetBuffProperty(ref _sourceAssetPath, value);
    }

    public string Draft
    {
        get => _draft;
        set => SetBuffProperty(ref _draft, value);
    }

    private void SetBuffProperty(
        ref string field,
        string value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null,
        bool notifyDisplayProperties = false)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            if (notifyDisplayProperties)
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(OwnerDisplayText));
                OnPropertyChanged(nameof(ValueSummaryText));
                OnPropertyChanged(nameof(TypeSummaryText));
                OnPropertyChanged(nameof(ShortDescription));
            }

            RaiseEdited();
        }
    }

    private static string TextOrUnset(string value) => string.IsNullOrWhiteSpace(value) ? "未填" : value.Trim();

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

        AnyBuffEdited?.Invoke(null, EventArgs.Empty);
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
