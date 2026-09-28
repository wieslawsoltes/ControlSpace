namespace ControlSpace.Workbench.Uno;

/// <summary>Optional host adapter for user-specific window layout; never stored in project files.</summary>
public interface IWorkbenchPreferences
{
    Task<string?> ReadLayoutAsync();
    Task WriteLayoutAsync(string contents);
}
