﻿using System.Diagnostics;
using GIMI_ModManager.Core.GamesService.JsonModels;
using GIMI_ModManager.Core.Helpers;
using Serilog;

namespace GIMI_ModManager.Core.GamesService.Models;

[DebuggerDisplay("{" + nameof(DisplayName) + "}")]
public class Character : ICharacter
{
    public int Id { get; internal set; }
    public string? Category { get; internal set; }
    public string InternalName { get; set; } = null!;
    public string ModFilesName { get; internal set; } = null!;
    public string DisplayName { get; set; } = null!;
    public int Rarity { get; internal set; }
    public Uri? ImageUri { get; set; }
    public IGameClass Class { get; internal set; } = null!;
    public IGameElement Element { get; internal set; } = null!;
    public DateTime ReleaseDate { get; internal set; }
    public ICollection<IRegion> Regions { get; internal set; } = null!;
    public ICollection<ICharacterSkin> AdditionalSkins { get; internal set; } = new List<ICharacterSkin>();


    internal static CharacterBuilder FromJson(JsonCharacter jsonCharacter)
    {
        var internalName = jsonCharacter.InternalName ??
                           throw new InvalidJsonConfigException("InternalName can never be missing or null");

        var character = new Character
        {
            Id = jsonCharacter.Id,
            InternalName = internalName,
            ModFilesName = "",
            DisplayName = jsonCharacter.DisplayName ?? internalName,
            Rarity = jsonCharacter.Rarity is >= 0 and <= 5 ? jsonCharacter.Rarity.Value : -1,
            ReleaseDate = DateTime.TryParse(jsonCharacter.ReleaseDate, out var date) ? date : DateTime.MaxValue,
            AdditionalSkins = new List<ICharacterSkin>()
        };

        foreach (var jsonCharacterInGameSkin in jsonCharacter.InGameSkins)
        {
            var skin = CharacterSkin.FromJson(character, jsonCharacterInGameSkin);
            character.AdditionalSkins.Add(skin);
        }

        return new CharacterBuilder(character, jsonCharacter);
    }


    public class InvalidJsonConfigException : Exception
    {
        public InvalidJsonConfigException(string message) : base(message)
        {
        }
    }
}

internal sealed class CharacterBuilder
{
    private readonly Character _character;
    private readonly JsonCharacter _jsonCharacter;

    private bool _regionSet;
    private bool _classSet;
    private bool _elementSet;
    private bool _skinsSet;

    public CharacterBuilder(Character character, JsonCharacter jsonCharacter)
    {
        _character = character;
        _jsonCharacter = jsonCharacter;
    }

    public CharacterBuilder SetRegion(IReadOnlyCollection<IRegion> regions)
    {
        if (_regionSet)
            throw new InvalidOperationException("Region already set");

        var connectedRegions = new List<IRegion>();

        foreach (var internalRegionName in _jsonCharacter.Region ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(internalRegionName))
                throw new InvalidOperationException("Region cannot be null or empty");

            var region = regions.FirstOrDefault(region =>
                region.InternalName.Equals(internalRegionName, StringComparison.OrdinalIgnoreCase));

            if (region is not null)
            {
                connectedRegions.Add(region);
            }
            else
                Log.Warning("Region {Region} not found in regions list", internalRegionName);
        }

        _character.Regions = connectedRegions;

        if (connectedRegions.Count == 0)
            Log.Warning("No regions found for character {Character}", _character.DisplayName);

        _regionSet = true;
        return this;
    }

    public CharacterBuilder SetClass(IReadOnlyCollection<IGameClass> gameClasses)
    {
        if (_classSet)
            throw new InvalidOperationException("Class already set");

        _character.Class = gameClasses.FirstOrDefault(gameClass =>
            gameClass.InternalName.Equals(_jsonCharacter.Class,
                StringComparison.OrdinalIgnoreCase))!;
        if (_character.Class is null)
            throw new InvalidOperationException("Class not found");

        _classSet = true;
        return this;
    }

    public CharacterBuilder SetElement(IReadOnlyCollection<IGameElement> elements)
    {
        if (_elementSet)
            throw new InvalidOperationException("Element already set");

        _character.Element = elements.FirstOrDefault(element =>
            element.InternalName.Equals(_jsonCharacter.Element,
                StringComparison.OrdinalIgnoreCase))!;

        if (_character.Element is null)
            throw new InvalidOperationException("Element not found");

        _elementSet = true;
        return this;
    }

    public CharacterBuilder SetCharacterOverride(JsonCharacter? character)
    {
        if (character is null)
            return this;

        if (!character.DisplayName.IsNullOrEmpty())
            _character.DisplayName = character.DisplayName;

        if (!character.Image.IsNullOrEmpty())
            _character.ImageUri = Uri.TryCreate(character.Image, UriKind.Absolute, out var uri) ? uri : null;


        return this;
    }

    public CharacterBuilder AddSkin(ICharacterSkin skin)
    {
        _character.AdditionalSkins.Add(skin);
        _skinsSet = true;
        return this;
    }


    private void SetImage(string imageFolder, string? image)
    {
        if (image.IsNullOrEmpty()) return;

        var imagePath = Path.Combine(imageFolder, image);
        if (File.Exists(imagePath))
            _character.ImageUri = new Uri(imagePath);
        else
            Log.Debug("Image {Image} for {Name} {CharacterName} is invalid", image, _character.DisplayName,
                _character.InternalName);
    }

    public Character CreateCharacter(string imageFolder)
    {
        if (!_regionSet)
            throw new InvalidOperationException("Region not set");

        if (!_classSet)
            throw new InvalidOperationException("Class not set");

        if (!_elementSet)
            throw new InvalidOperationException("Element not set");

        if (_jsonCharacter.Image.IsNullOrEmpty()) return _character;


        SetImage(imageFolder, _jsonCharacter.Image);

        foreach (var characterSkin in _character.AdditionalSkins)
        {
            var jsonCharacterSkin = _jsonCharacter.InGameSkins
                .FirstOrDefault(skin => skin.InternalName is not null &&
                                        skin.InternalName.Equals(characterSkin.InternalName,
                                            StringComparison.OrdinalIgnoreCase));

            if (jsonCharacterSkin is null)
                continue;

            SetImage(imageFolder, jsonCharacterSkin.Image);
        }


        return _character;
    }
}