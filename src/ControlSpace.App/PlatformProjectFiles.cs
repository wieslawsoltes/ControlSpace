using ControlSpace.Storage;
using ControlSpace.Workbench.Uno;
using Windows.Storage;
using Windows.Storage.Pickers;
namespace ControlSpace.App;

internal sealed class PlatformProjectFiles : IProjectFiles
{
    public async Task<string?> OpenAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        var properties = await file.GetBasicPropertiesAsync(); if (properties.Size > ProjectStorage.MaximumBytes) throw new InvalidDataException("Project exceeds the 8 MiB limit.");
        return await FileIO.ReadTextAsync(file);
    }
    public async Task<bool> SaveAsync(string contents, string name, string extension = ".json")
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = name };
        picker.FileTypeChoices.Add(extension == ".csv" ? "CSV tag table" : "ControlSpace project", new List<string> { extension });
        var file = await picker.PickSaveFileAsync(); if (file is null) return false;
        await FileIO.WriteTextAsync(file, contents); return true;
    }
    public async Task<string?> ReadRecoveryAsync()
    {
        var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync("recovery.controlspace.json");
        return item is StorageFile file ? await FileIO.ReadTextAsync(file) : null;
    }
    public async Task WriteRecoveryAsync(string contents)
    {
        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("recovery.controlspace.json", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, contents);
    }
}
