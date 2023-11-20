﻿using System.Diagnostics.CodeAnalysis;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Entities.Mods.SkinMod;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.ModHandling;
// Start with installing (and unzipping) the mod to the temp folder, before this process starts

// If a single folder
// Check if the mod already exists in the mods folder
// If yes -> Ask if the user wants to overwrite the mod or rename the mod or this mod

// If multiple folders / files
// show a dedicated window for the user to decide what to do with the files and folders, set mod name and so on

// Then move the mod to the mods folder
public class ModFolderManager
{
    private readonly ILogger _logger;
    private readonly ModDragAndDropService _modDragAndDropService;

    private DirectoryInfo _workFolder;

    public ModFolderManager(ILogger logger, ModDragAndDropService modDragAndDropService)
    {
        _logger = logger;
        _modDragAndDropService = modDragAndDropService;
    }

    public Task StartModInstallationAsync(DirectoryInfo modFolder)
    {
        throw new NotImplementedException();
    }
}

public sealed class ModInstallation : IDisposable
{
    private readonly ModCrawlerService _modCrawlerService = App.GetService<ModCrawlerService>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly ICharacterModList _destinationModList;
    private readonly DirectoryInfo _originalModFolder;
    private readonly List<FileStream> _lockedFiles = new();

    private FileInfo? _jasmConfigFile;
    public DirectoryInfo ModFolder { get; private set; }
    private DirectoryInfo? _shaderFixesFolder;
    private List<FileInfo> _shaderFixesFiles = new();


    private ModInstallation(DirectoryInfo originalModFolder, ICharacterModList destinationModList)
    {
        _originalModFolder = originalModFolder;
        _destinationModList = destinationModList;
        SetRootModFolder(originalModFolder);
        LockFiles();
    }

    private void LockFiles()
    {
        foreach (var fileInfo in _originalModFolder.GetFiles("*", SearchOption.AllDirectories))
        {
            var fileStream = fileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            _lockedFiles.Add(fileStream);
        }
    }

    // Lock mod folder
    public static ModInstallation Start(DirectoryInfo modFolder, ICharacterModList destinationModList)
    {
        return new ModInstallation(modFolder, destinationModList);
    }

    [MemberNotNull(nameof(ModFolder))]
    public void SetRootModFolder(DirectoryInfo newRootFolder)
    {
        if (newRootFolder.FullName == _shaderFixesFolder?.FullName)
            throw new ArgumentException("The new root folder is the same as the current shader fixes folder");

        if (!newRootFolder.Exists)
            throw new DirectoryNotFoundException($"The folder {newRootFolder.FullName} does not exist");

        ModFolder = new DirectoryInfo(newRootFolder.FullName);
        _jasmConfigFile = _modCrawlerService.GetFirstJasmConfigFileAsync(ModFolder, false);
    }

    public void SetShaderFixesFolder(DirectoryInfo shaderFixesFolder)
    {
        if (shaderFixesFolder.FullName == ModFolder.FullName)
            throw new ArgumentException("The new shader fixes folder is the same as the current root folder");

        if (!shaderFixesFolder.Exists)
            throw new DirectoryNotFoundException($"The folder {shaderFixesFolder.FullName} does not exist");

        _shaderFixesFiles.Clear();
        _shaderFixesFiles.AddRange(shaderFixesFolder.GetFiles("*.txt", SearchOption.TopDirectoryOnly));
        _shaderFixesFolder = shaderFixesFolder;
    }


    public DirectoryInfo? AutoSetModRootFolder()
    {
        var jasmConfigFile = _modCrawlerService.GetFirstJasmConfigFileAsync(_originalModFolder);
        DirectoryInfo? modRootFolder = null;
        if (jasmConfigFile is not null)
        {
            modRootFolder = new DirectoryInfo(jasmConfigFile.DirectoryName!);
        }
        else
        {
            var mergedIniFile = _modCrawlerService.GetMergedIniFile(_originalModFolder);
            if (mergedIniFile is not null)
                modRootFolder = new DirectoryInfo(mergedIniFile.DirectoryName!);
        }

        if (modRootFolder is null)
            return null;

        SetRootModFolder(modRootFolder);
        return modRootFolder;
    }

    public DirectoryInfo? AutoSetShaderFixesFolder()
    {
        var shaderFixesFolder = _modCrawlerService.GetShaderFixesFolder(_originalModFolder);
        if (shaderFixesFolder is null)
            return null;

        SetShaderFixesFolder(shaderFixesFolder);
        return shaderFixesFolder;
    }


    public ISkinMod? AnyDuplicateName()
    {
        var skinEntries = _destinationModList.Mods;


        foreach (var skinEntry in skinEntries)
        {
            if (ModFolderHelpers.FolderNameEquals(skinEntry.Mod.Name, ModFolder.Name))
                return skinEntry.Mod;
        }

        return null;
    }

    public async Task RenameAndAddAsync(AddModOptions options, ISkinMod dupeMod,
        string dupeModNewFolderName, string? dupeModNewCustomName = null)
    {
        if (dupeModNewFolderName.IsNullOrEmpty() && options.NewModFolderName.IsNullOrEmpty())
            throw new ArgumentException("The new mod folder name and old folder name cannot be null or empty");

        if (dupeModNewFolderName == options.NewModFolderName)
            throw new ArgumentException("The new mod folder name and old folder name cannot be the same");


        var skinMod = await CreateSkinModWithOptionsAsync(options);
        var newModRenamed = false;
        if (!options.NewModFolderName.IsNullOrEmpty() && skinMod.Name != options.NewModFolderName)
        {
            var tmpFolder = App.GetUniqueTmpFolder();
            skinMod = await SkinMod.CreateModAsync(skinMod.CopyTo(tmpFolder.FullName).FullPath).ConfigureAwait(false);
            skinMod.Rename(options.NewModFolderName);
            newModRenamed = true;
        }

        if (!dupeModNewFolderName.IsNullOrEmpty() && dupeMod.Name != dupeModNewFolderName)
        {
            dupeMod.Rename(dupeModNewFolderName);
        }

        // Set new custom name for dupe mod
        if (!dupeModNewCustomName.IsNullOrEmpty())
        {
            var dupeModSettings = await dupeMod.Settings.ReadSettingsAsync().ConfigureAwait(false);

            await dupeMod.Settings
                .SaveSettingsAsync(dupeModSettings.DeepCopyWithProperties(customName: dupeModNewCustomName))
                .ConfigureAwait(false);
        }

        _skinManagerService.AddMod(skinMod, _destinationModList, newModRenamed);
    }


    public async Task AddAndReplaceAsync(ISkinMod dupeMod, AddModOptions? options = null)
    {
        var skinMod = await CreateSkinModWithOptionsAsync(options).ConfigureAwait(false);
        try
        {
            _destinationModList.DeleteModBySkinEntryId(dupeMod.Id);
        }
        catch (DirectoryNotFoundException)
        {
        }

        _skinManagerService.AddMod(skinMod, _destinationModList);
    }

    public async Task AddModAsync(AddModOptions? options = null)
    {
        if (AnyDuplicateName() is not null)
            throw new InvalidOperationException("There is already a mod with the same name");

        var skinMod = await CreateSkinModWithOptionsAsync(options);

        _skinManagerService.AddMod(skinMod, _destinationModList);
    }

    private async Task<ISkinMod> CreateSkinModWithOptionsAsync(AddModOptions? options = null)
    {
        await RemoveJasmConfigFileLockAsync().ConfigureAwait(false);

        var skinMod = await SkinMod.CreateModAsync(ModFolder, true).ConfigureAwait(false);

        if (options is null)
            return skinMod;

        var settings = new ModSettings(
            id: skinMod.Id,
            customName: options.ModName,
            imagePath: options.ModImage,
            author: options.Author,
            modUrl: Uri.TryCreate(options.ModUrl, UriKind.Absolute, out var modUrl) ? modUrl : null,
            description: options.Description,
            dateAdded: DateTime.Now
        );
        await skinMod.Settings.SaveSettingsAsync(settings).ConfigureAwait(false);
        return skinMod;
    }

    private async Task RemoveJasmConfigFileLockAsync()
    {
        if (_jasmConfigFile is not null)
        {
            _jasmConfigFile.Refresh();
            var jasmFs = _lockedFiles.FirstOrDefault(file =>
                file.Name.Equals(_jasmConfigFile.FullName, StringComparison.OrdinalIgnoreCase));

            if (jasmFs is not null)
            {
                await jasmFs.DisposeAsync().ConfigureAwait(false);
                _lockedFiles.Remove(jasmFs);
            }
        }
    }

    private void ReleaseLockedFiles()
    {
        foreach (var fileStream in _lockedFiles.ToArray())
        {
            fileStream.Dispose();
            _lockedFiles.Remove(fileStream);
        }
    }

    public void Dispose()
    {
        ReleaseLockedFiles();
    }
}

public record AddModOptions
{
    public string? NewModFolderName { get; set; }
    public string? ModName { get; set; }
    public Uri? ModImage { get; set; }
    public string? ModUrl { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
}