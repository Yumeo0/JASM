﻿using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Contracts.ViewModels;
using GIMI_ModManager.WinUI.Models;
using GIMI_ModManager.WinUI.Models.Options;
using GIMI_ModManager.WinUI.Services;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels;

public partial class CharactersViewModel : ObservableRecipient, INavigationAware
{
    private readonly IGenshinService _genshinService;
    private readonly ILogger _logger;
    private readonly INavigationService _navigationService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly GenshinProcessManager _genshinProcessManager;
    private readonly ThreeDMigtoProcessManager _threeDMigtoProcessManager;
    public NotificationManager NotificationManager { get; }
    public ElevatorService ElevatorService { get; }


    private GenshinCharacter[] _characters = Array.Empty<GenshinCharacter>();
    public ObservableCollection<CharacterGridItemModel> Characters { get; } = new();

    public ObservableCollection<CharacterGridItemModel> SuggestionsBox { get; } = new();

    public ObservableCollection<CharacterGridItemModel> PinnedCharacters { get; } = new();

    public ObservableCollection<CharacterGridItemModel> HiddenCharacters { get; } = new();


    private string _searchText = string.Empty;

    public CharactersViewModel(IGenshinService genshinService, ILogger logger, INavigationService navigationService,
        ISkinManagerService skinManagerService, ILocalSettingsService localSettingsService,
        NotificationManager notificationManager, ElevatorService elevatorService,
        GenshinProcessManager genshinProcessManager, ThreeDMigtoProcessManager threeDMigtoProcessManager)
    {
        _genshinService = genshinService;
        _logger = logger.ForContext<CharactersViewModel>();
        _navigationService = navigationService;
        _skinManagerService = skinManagerService;
        _localSettingsService = localSettingsService;
        NotificationManager = notificationManager;
        ElevatorService = elevatorService;
        _genshinProcessManager = genshinProcessManager;
        _threeDMigtoProcessManager = threeDMigtoProcessManager;

        ElevatorService.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(ElevatorService.ElevatorStatus))
                RefreshModsInGameCommand.NotifyCanExecuteChanged();
        };
    }

    public int? AutoSuggestBox_TextChanged(string text)
    {
        _searchText = text;
        SuggestionsBox.Clear();

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            SuggestionsBox.Clear();
            ResetContent();
            return null;
        }

        var suitableItems = new List<CharacterGridItemModel>();
        var splitText = _searchText.Split(" ");
        foreach (var character in _characters.Select(ch => new CharacterGridItemModel(ch)))
        {
            var found = splitText.Any((key) =>
            {
                return character.Character.Keys.Any(characterKeys =>
                    characterKeys.Contains(key, StringComparison.CurrentCultureIgnoreCase));
            });
            if (found)
            {
                suitableItems.Add(character);
            }
        }

        if (!suitableItems.Any())
        {
            ResetContent();
            return 0;
        }

        suitableItems.ForEach(suggestion => SuggestionsBox.Add(suggestion));

        ShowOnlyCharacters(suitableItems);
        return suitableItems.Count;
    }


    public void SuggestionBox_Chosen(CharacterGridItemModel character)
    {
        _navigationService.SetListDataItemForNextConnectedAnimation(character);
        _navigationService.NavigateTo(typeof(CharacterDetailsViewModel).FullName!, character);
    }

    //private void ResetContent()
    //{
    //    var neitherPinnedNorHiddenCharacters = _characters.Where(x =>
    //        !PinnedCharacters.Contains(x) && !HiddenCharacters.Contains(x)).ToArray();

    //    var listLocationIndex = 0;
    //    for (var i = 0; i < PinnedCharacters.Count; i++)
    //    {
    //        var character = PinnedCharacters.ElementAtOrDefault(i);

    //        if (character is null)
    //        {
    //            Characters.Add(_characters[i]);
    //            listLocationIndex = i + 1;
    //            continue;
    //        }

    //        if (character.Id != _characters[i].Id)
    //        {
    //            Characters.Insert(i, _characters[i]);
    //        }

    //        listLocationIndex = i + 1;
    //    }

    //    var nextListLocationIndex = listLocationIndex;

    //    for (var i = listLocationIndex; i < neitherPinnedNorHiddenCharacters.Length + listLocationIndex; i++)
    //    {
    //        var index = i - listLocationIndex;
    //        var character = Characters.ElementAtOrDefault(i);

    //        if (character is null)
    //        {
    //            Characters.Add(_characters[index]);
    //            nextListLocationIndex = i + 1;
    //            continue;
    //        }

    //        if (character.Id != _characters[index].Id)
    //        {
    //            Characters.Insert(i, _characters[index]);
    //        }

    //        nextListLocationIndex = i + 1;
    //    }

    //    for (var i = nextListLocationIndex; i < HiddenCharacters.Count + nextListLocationIndex; i++)
    //    {
    //        var index = i - nextListLocationIndex;
    //        var character = HiddenCharacters.ElementAtOrDefault(i);

    //        if (character is null)
    //        {
    //            if (HiddenCharacters.Contains(_characters[index]))
    //            {
    //                continue;
    //            }

    //            Characters.Add(_characters[index]);
    //            continue;
    //        }

    //        if (character.Id != _characters[index].Id)
    //        {
    //            if (HiddenCharacters.Contains(_characters[index]))
    //            {
    //                continue;
    //            }

    //            Characters.Insert(i, _characters[index]);
    //        }
    //    }
    //}

    private void ResetContent()
    {
        var neitherPinnedNorHiddenCharacters = _characters.Where(x =>
            !PinnedCharacters.Select(pch => pch.Character).Contains(x) &&
            !HiddenCharacters.Select(pch => pch.Character).Contains(x));


        var gridIndex = 0;
        foreach (var genshinCharacter in PinnedCharacters)
        {
            InsertCharacterIntoView(genshinCharacter, gridIndex);
            gridIndex++;
        }

        foreach (var genshinCharacter in neitherPinnedNorHiddenCharacters.Select(ch => new CharacterGridItemModel(ch)))
        {
            InsertCharacterIntoView(genshinCharacter, gridIndex);
            gridIndex++;
        }

        foreach (var genshinCharacter in HiddenCharacters)
        {
            InsertCharacterIntoView(genshinCharacter, gridIndex);
            gridIndex++;
        }

        for (int i = Characters.Count; i > gridIndex; i--)
        {
            Characters.RemoveAt(i - 1);
        }


        Debug.Assert(Characters.Distinct().Count() == Characters.Count,
            $"Characters.Distinct().Count(): {Characters.Distinct().Count()} != Characters.Count: {Characters.Count}\n\t" +
            $"Duplicate characters found in character overview");
    }


    private void InsertCharacterIntoView(CharacterGridItemModel character, int gridIndex)
    {
        var characterAtGridIndex = Characters.ElementAtOrDefault(gridIndex);

        if (characterAtGridIndex?.Character.Id == character.Character.Id)
        {
            return;
        }

        if (characterAtGridIndex is null)
        {
            Characters.Add(character);
            return;
        }

        if (character.Character.Id != characterAtGridIndex.Character.Id)
        {
            Characters.Insert(gridIndex, character);
        }
    }

    private void ShowOnlyCharacters(IEnumerable<CharacterGridItemModel> charactersToShow, bool hardClear = false)
    {
        var tmpList = new List<CharacterGridItemModel>(Characters);

        if (hardClear)
            tmpList = new List<CharacterGridItemModel>(_characters.Select(ch => new CharacterGridItemModel(ch)));

        var characters = tmpList.Where(charactersToShow.Contains).ToArray();

        var pinnedCharacters = characters.Intersect(PinnedCharacters).ToArray();
        characters = characters.Except(pinnedCharacters).ToArray();
        Characters.Clear();

        foreach (var genshinCharacter in pinnedCharacters)
        {
            Characters.Add(genshinCharacter);
        }

        foreach (var genshinCharacter in characters)
        {
            Characters.Add(genshinCharacter);
        }

        Debug.Assert(Characters.Distinct().Count() == Characters.Count,
            $"Characters.Distinct().Count(): {Characters.Distinct().Count()} != Characters.Count: {Characters.Count}\n\t" +
            $"Duplicate characters found in character overview");
    }

    public async void OnNavigatedTo(object parameter)
    {
        var characters = _genshinService.GetCharacters().OrderBy(g => g.DisplayName).ToList();
        var others = characters.FirstOrDefault(ch => ch.Id == _genshinService.OtherCharacterId);
        if (others is not null) // Add to front
        {
            characters.Remove(others);
            characters.Insert(0, others);
        }

        var gliders = characters.FirstOrDefault(ch => ch.Id == _genshinService.GlidersCharacterId);
        if (gliders is not null) // Add to end
        {
            characters.Remove(gliders);
            characters.Add(gliders);
        }

        _characters = characters.ToArray();

        var pinnedCharactersOptions = await ReadCharacterSettings();

        foreach (var pinedCharacterId in pinnedCharactersOptions.PinedCharacters)
        {
            var character = _characters.FirstOrDefault(x => x.Id == pinedCharacterId);
            if (character is not null)
            {
                PinnedCharacters.Add(new CharacterGridItemModel(character) { IsPinned = true });
            }
        }

        foreach (var hiddenCharacterId in pinnedCharactersOptions.HiddenCharacters)
        {
            var character = _characters.FirstOrDefault(x => x.Id == hiddenCharacterId);
            if (character is not null)
            {
                HiddenCharacters.Add(new CharacterGridItemModel(character) { IsHidden = true });
            }
        }

        ResetContent();

        // Character Ids where more than 1 skin is enabled
        var charactersWithMultipleActiveSkins = _skinManagerService.CharacterModLists
            .Where(x => x.Mods.Count(mod => mod.IsEnabled) > 1).Select(x => x.Character.Id);

        foreach (var characterGridItemModel in Characters.Where(x =>
                     charactersWithMultipleActiveSkins.Contains(x.Character.Id)))
        {
            if (characterGridItemModel.Character.Id == _genshinService.OtherCharacterId ||
                characterGridItemModel.Character.Id == _genshinService.GlidersCharacterId)
                continue;

            characterGridItemModel.Warning = true;
        }

        if (!pinnedCharactersOptions.ShowOnlyCharactersWithMods) return;

        ShowOnlyCharactersWithMods = true;
        var characterIdsWithMods =
            _skinManagerService.CharacterModLists.Where(x => x.Mods.Any()).Select(x => x.Character.Id);

        var charactersWithMods = Characters.Where(x => characterIdsWithMods.Contains(x.Character.Id));

        ShowOnlyCharacters(charactersWithMods);
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void CharacterClicked(CharacterGridItemModel characterModel)
    {
        _navigationService.SetListDataItemForNextConnectedAnimation(characterModel);
        _navigationService.NavigateTo(typeof(CharacterDetailsViewModel).FullName!, characterModel);
    }

    [ObservableProperty] private bool _showOnlyCharactersWithMods = false;

    [RelayCommand]
    private async Task ShowCharactersWithModsAsync()
    {
        if (ShowOnlyCharactersWithMods)
        {
            ShowOnlyCharactersWithMods = false;
            ResetContent();
            var settingss = await ReadCharacterSettings();


            settingss.ShowOnlyCharactersWithMods = ShowOnlyCharactersWithMods;

            await SaveCharacterSettings(settingss);

            return;
        }

        var charactersWithMods =
            _skinManagerService.CharacterModLists.Where(x => x.Mods.Any())
                .Select(x => new CharacterGridItemModel(x.Character));

        ShowOnlyCharacters(charactersWithMods);

        ShowOnlyCharactersWithMods = true;

        var settings = await ReadCharacterSettings();

        settings.ShowOnlyCharactersWithMods = ShowOnlyCharactersWithMods;

        await SaveCharacterSettings(settings);
    }


    [ObservableProperty] private string _pinText = DefaultPinText;

    [ObservableProperty] private string _pinGlyph = DefaultPinGlyph;

    const string DefaultPinGlyph = "\uE718";
    const string DefaultPinText = "Pin To Top";
    const string DefaultUnpinGlyph = "\uE77A";
    const string DefaultUnpinText = "Unpin Character";

    public void OnRightClickContext(CharacterGridItemModel clickedCharacter)
    {
        if (PinnedCharacters.Contains(clickedCharacter))
        {
            PinText = DefaultUnpinText;
            PinGlyph = DefaultUnpinGlyph;
        }
        else
        {
            PinText = DefaultPinText;
            PinGlyph = DefaultPinGlyph;
        }
    }

    [RelayCommand]
    private async Task PinCharacterAsync(CharacterGridItemModel character)
    {
        if (PinnedCharacters.Contains(character))
        {
            character.IsPinned = false;
            PinnedCharacters.Remove(character);

            if (!ShowOnlyCharactersWithMods)
                ResetContent();
            else
            {
                var charactersWithModss =
                    _skinManagerService.CharacterModLists.Where(x => x.Mods.Any())
                        .Select(x => new CharacterGridItemModel(x.Character));
                ShowOnlyCharacters(charactersWithModss, true);
            }


            var settingss = await ReadCharacterSettings();

            character.IsPinned = false;
            var pinedCharacterss = PinnedCharacters.Select(ch => ch.Character.Id).ToArray();
            settingss.PinedCharacters = pinedCharacterss;

            await SaveCharacterSettings(settingss);
            return;
        }

        character.IsPinned = true;
        PinnedCharacters.Add(character);

        if (!ShowOnlyCharactersWithMods)
            ResetContent();

        else
        {
            var charactersWithMods =
                _skinManagerService.CharacterModLists.Where(x => x.Mods.Any())
                    .Select(x => new CharacterGridItemModel(x.Character));
            ShowOnlyCharacters(charactersWithMods);
        }


        var settings = await ReadCharacterSettings();

        var pinedCharacters = PinnedCharacters.Select(ch => ch.Character.Id)
            .Union(settings.PinedCharacters.ToList()).ToArray();
        settings.PinedCharacters = pinedCharacters;

        await SaveCharacterSettings(settings);
    }


    [RelayCommand]
    private void HideCharacter(GenshinCharacter character)
        => NotImplemented.Show("Hiding characters is not implemented yet");

    private async Task<CharacterOverviewOptions> ReadCharacterSettings() =>
        await _localSettingsService.ReadSettingAsync<CharacterOverviewOptions>(CharacterOverviewOptions.Key) ??
        new CharacterOverviewOptions();

    private async Task SaveCharacterSettings(CharacterOverviewOptions settings) =>
        await _localSettingsService.SaveSettingAsync(CharacterOverviewOptions.Key, settings);


    [RelayCommand]
    private async Task Start3DmigotoAsync()
    {
        _logger.Debug("Starting 3Dmigoto");
        _threeDMigtoProcessManager.CheckStatus();

        if (_threeDMigtoProcessManager.ProcessStatus == ProcessStatus.NotInitialized)
        {
            var processPath = await _threeDMigtoProcessManager.PickProcessPathAsync(App.MainWindow);
            if (processPath is null) return;
            await _threeDMigtoProcessManager.SetPath(Path.GetFileName(processPath), processPath);
        }

        if (_threeDMigtoProcessManager.ProcessStatus == ProcessStatus.NotRunning)
            _threeDMigtoProcessManager.StartProcess();
    }

    private bool CanRefreshModsInGame() => ElevatorService.ElevatorStatus == ElevatorStatus.Running;

    [RelayCommand(CanExecute = nameof(CanRefreshModsInGame))]
    private async Task RefreshModsInGameAsync()
    {
        _logger.Debug("Refreshing Mods In Game");
        await ElevatorService.RefreshGenshinMods();
    }

    [RelayCommand]
    private async Task StartGenshinAsync()
    {
        _logger.Debug("Starting Genshin Impact");
        _genshinProcessManager.CheckStatus();
        if (_genshinProcessManager.ProcessStatus == ProcessStatus.NotInitialized)
        {
            var processPath = await _genshinProcessManager.PickProcessPathAsync(App.MainWindow);
            if (processPath is null) return;
            await _genshinProcessManager.SetPath(Path.GetFileName(processPath), processPath);
        }

        if (_genshinProcessManager.ProcessStatus == ProcessStatus.NotRunning)
            _genshinProcessManager.StartProcess();
    }
}