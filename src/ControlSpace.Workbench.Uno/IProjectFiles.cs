namespace ControlSpace.Workbench.Uno;

/// <summary>Host adapter. File formats and engineering logic are not coupled to pickers.</summary>
public interface IProjectFiles
{
    Task<string?> OpenAsync();
    Task<bool> SaveAsync(string contents, string name, string extension = ".json");
    Task<string?> ReadRecoveryAsync();
    Task WriteRecoveryAsync(string contents);
}
