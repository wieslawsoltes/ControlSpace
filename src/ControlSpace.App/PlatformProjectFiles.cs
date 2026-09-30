using System.Text;
using ControlSpace.Storage;
using ControlSpace.Workbench.Uno;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
namespace ControlSpace.App;

internal sealed class PlatformProjectFiles : IProjectFiles, IWorkbenchPreferences
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
        // The browser DownloadUpload picker returns a temporary file. Completing
        // updates is what dispatches the actual download, not WriteTextAsync alone.
        // On other Uno hosts this follows the same cross-platform save contract.
        CachedFileManager.DeferUpdates(file);
        // Write JSON as explicit UTF-8 bytes. FileIO.WriteTextAsync adds a BOM on
        // some hosts, which strict JSON readers reject. Keep CSV text behavior.
        if (extension == ".csv") await FileIO.WriteTextAsync(file, contents);
        else await FileIO.WriteBytesAsync(file, Encoding.UTF8.GetBytes(contents));
        var status = await CachedFileManager.CompleteUpdatesAsync(file);
        if (status is not (FileUpdateStatus.Complete or FileUpdateStatus.CompleteAndRenamed))
            throw new IOException("The selected file could not be finalized: " + status);
        return true;
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
    private const string LayoutKey = "ControlSpace.Workbench.Layout.v1";
    public async Task<string?> ReadLayoutAsync()
    {
#if __WASM__
        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(LayoutKey, out var stored) && stored is string saved)
            return saved;
#endif
        // Retain the file reader for desktop and migration from earlier browser previews.
        var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync("workbench-layout.json");
        return item is StorageFile file ? await FileIO.ReadTextAsync(file) : null;
    }
    public Task WriteLayoutAsync(string contents)
    {
#if __WASM__
        // Small preferences must survive immediate reload, without waiting for the
        // browser virtual filesystem's periodic IndexedDB synchronization.
        ApplicationData.Current.LocalSettings.Values[LayoutKey] = contents;
        return Task.CompletedTask;
#else
        return WriteLayoutFileAsync(contents);
#endif
    }
    private static async Task WriteLayoutFileAsync(string contents)
    {
        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("workbench-layout.json", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, contents);
    }
}
