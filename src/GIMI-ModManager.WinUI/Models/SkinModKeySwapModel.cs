﻿using CommunityToolkit.Mvvm.ComponentModel;
using GIMI_ModManager.Core.Entities;

namespace GIMI_ModManager.WinUI.Models;

public partial class SkinModKeySwapModel : ObservableObject, IEquatable<SkinModKeySwapModel>
{
    [ObservableProperty] private string _sectionKey = string.Empty;

    [ObservableProperty] private string? _condition;
    [ObservableProperty] private string? _forwardHotkey;
    [ObservableProperty] private string? _backwardHotkey;
    [ObservableProperty] private string? _type;
    [ObservableProperty] private string[]? _swapVar;
    [ObservableProperty] private string _variationsCount;

    public static SkinModKeySwapModel FromKeySwapSettings(SkinModKeySwap skinSwapSetting)
    {
        return new SkinModKeySwapModel
        {
            SectionKey = skinSwapSetting.SectionKey,
            ForwardHotkey = skinSwapSetting.ForwardHotkey,
            BackwardHotkey = skinSwapSetting.BackwardHotkey,
            Type = skinSwapSetting.Type,
            SwapVar = skinSwapSetting.SwapVar,
            VariationsCount = skinSwapSetting.SwapVar?.Length.ToString() ?? "Unknown"
        };
    }

    public static SkinModKeySwapModel[] FromKeySwapSettings(SkinModKeySwap[] skinSwapSettings)
        => skinSwapSettings.Select(FromKeySwapSettings).ToArray();

    public SkinModKeySwap ToKeySwapSettings()
    {
        return new()
        {
            SectionKey = SectionKey,
            ForwardHotkey = ForwardHotkey,
            BackwardHotkey = BackwardHotkey,
            Type = Type,
            SwapVar = SwapVar
        };
    }

    public bool Equals(SkinModKeySwapModel? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Condition == other.Condition && ForwardHotkey == other.ForwardHotkey &&
               BackwardHotkey == other.BackwardHotkey && Type == other.Type && Equals(SwapVar, other.SwapVar);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is SkinModKeySwapModel other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Condition, ForwardHotkey, BackwardHotkey, Type, SwapVar);
    }
}