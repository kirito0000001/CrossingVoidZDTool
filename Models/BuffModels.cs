using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private int _stacks;
    private int _completeStacks = 3;
    private int _strength = 1;
    private int _completeStrength = 10;
    private int _completedCount;
    private bool _endsWhenStacksReachZero;
    private string _ownerText = string.Empty;
    private string _taskPriority = "正常";
    private string _triggerTiming = string.Empty;
    private string _conditionSummary = string.Empty;
    private string _readStatus = string.Empty;
    private string _sourceAssetPath = string.Empty;
    private string _draft = string.Empty;
    private string _initializationNotes = string.Empty;
    private string _conditionUpdateNotes = string.Empty;
    private string _completionNotes = string.Empty;
    private string _removalNotes = string.Empty;
    private ObservableCollection<BuffEffectModule> _effects = [];
    private readonly HashSet<BuffEffectModule> _subscribedEffects = [];

    public BuffEntry()
    {
        AttachEffects(_effects);
    }

    public string SyncId { get; set; } = Guid.NewGuid().ToString("N");

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

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Stacks
    {
        get => _stacks;
        set => SetBuffProperty(ref _stacks, value, notifyDisplayProperties: true);
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int CompleteStacks
    {
        get => _completeStacks;
        set => SetBuffProperty(ref _completeStacks, value, notifyDisplayProperties: true);
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Strength
    {
        get => _strength;
        set => SetBuffProperty(ref _strength, value, notifyDisplayProperties: true);
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int CompleteStrength
    {
        get => _completeStrength;
        set => SetBuffProperty(ref _completeStrength, value, notifyDisplayProperties: true);
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int CompletedCount
    {
        get => _completedCount;
        set => SetBuffProperty(ref _completedCount, value, notifyDisplayProperties: true);
    }

    public bool EndsWhenStacksReachZero
    {
        get => _endsWhenStacksReachZero;
        set => SetBuffProperty(ref _endsWhenStacksReachZero, value);
    }

    public ObservableCollection<BuffEffectModule> Effects
    {
        get => _effects;
        set
        {
            var next = value ?? [];
            if (ReferenceEquals(_effects, next))
            {
                return;
            }

            DetachEffects(_effects);
            _effects = next;
            AttachEffects(_effects);
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectSummaryText));
            RaiseEdited();
        }
    }

    public string InitializationNotes
    {
        get => _initializationNotes;
        set => SetBuffProperty(ref _initializationNotes, value);
    }

    public string ConditionUpdateNotes
    {
        get => _conditionUpdateNotes;
        set => SetBuffProperty(ref _conditionUpdateNotes, value);
    }

    public string CompletionNotes
    {
        get => _completionNotes;
        set => SetBuffProperty(ref _completionNotes, value);
    }

    public string RemovalNotes
    {
        get => _removalNotes;
        set => SetBuffProperty(ref _removalNotes, value);
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
    public string ValueSummaryText => $"层数 {Stacks}~{CompleteStacks} / 强度 {Strength}~{CompleteStrength}";

    [JsonIgnore]
    public string EffectSummaryText => Effects.Count == 0 ? "未配置效果模块" : $"效果模块 {Effects.Count} 个";

    [JsonIgnore]
    public string TypeSummaryText => $"{TextOrUnset(GainType)} · {DamageTypeOrMarker()} · {TextOrUnset(TaskPriority)}";

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

    private void SetBuffProperty<T>(
        ref T field,
        T value,
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

    private string DamageTypeOrMarker() => string.IsNullOrWhiteSpace(DamageType) ? "仅标记" : DamageType.Trim();

    private void AttachEffects(ObservableCollection<BuffEffectModule> effects)
    {
        effects.CollectionChanged += Effects_CollectionChanged;
        foreach (var effect in effects)
        {
            AttachEffect(effect);
        }
    }

    private void DetachEffects(ObservableCollection<BuffEffectModule> effects)
    {
        effects.CollectionChanged -= Effects_CollectionChanged;
        foreach (var effect in _subscribedEffects)
        {
            effect.PropertyChanged -= Effect_PropertyChanged;
        }

        _subscribedEffects.Clear();
    }

    private void Effects_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var effect in _subscribedEffects)
            {
                effect.PropertyChanged -= Effect_PropertyChanged;
            }

            _subscribedEffects.Clear();
        }
        else if (e.OldItems is not null)
        {
            foreach (BuffEffectModule effect in e.OldItems)
            {
                if (_subscribedEffects.Remove(effect))
                {
                    effect.PropertyChanged -= Effect_PropertyChanged;
                }
            }
        }

        foreach (var effect in _effects)
        {
            AttachEffect(effect);
        }

        OnPropertyChanged(nameof(EffectSummaryText));
        RaiseEdited();
    }

    private void Effect_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseEdited();
    }

    private void AttachEffect(BuffEffectModule effect)
    {
        if (_subscribedEffects.Add(effect))
        {
            effect.PropertyChanged += Effect_PropertyChanged;
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

internal sealed class BuffEffectModule : CrossingVoidZDTool.ViewModels.ObservableObject
{
    private string _effectType = "属性修改";
    private string _target = "自身";
    private string _attribute = string.Empty;
    private string _operation = "加算";
    private double _value;
    private double _perStackValue;
    private string _implementationNotes = string.Empty;

    public string EffectType
    {
        get => _effectType;
        set => SetProperty(ref _effectType, value);
    }

    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    public string Attribute
    {
        get => _attribute;
        set => SetProperty(ref _attribute, value);
    }

    public string Operation
    {
        get => _operation;
        set => SetProperty(ref _operation, value);
    }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public double PerStackValue
    {
        get => _perStackValue;
        set => SetProperty(ref _perStackValue, value);
    }

    public string ImplementationNotes
    {
        get => _implementationNotes;
        set => SetProperty(ref _implementationNotes, value);
    }
}
