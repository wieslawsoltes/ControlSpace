using System.Security.Cryptography;
using System.Text;
using ControlSpace.Core;

namespace ControlSpace.Storage;

/// <summary>Atomic replacement with compare-and-swap tokens and an interprocess lock. Desktop only.</summary>
public sealed class FileProjectStore(string path) : IProjectStore
{
    private readonly string _path = Path.GetFullPath(path);
    private static string Token(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    public async Task<StoredProject?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        if (new FileInfo(_path).Length > DocumentLimits.MaxBytes) throw new InvalidDataException("Stored project exceeds 16 MiB.");
        var json = await File.ReadAllTextAsync(_path, cancellationToken); return new(DocumentCodec.Deserialize(json), Token(json));
    }
    public async Task<string> SaveAsync(ProjectDocument project, string? expectedToken, CancellationToken cancellationToken = default)
    {
        var json = DocumentCodec.Serialize(project);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var gate = await LockAsync(cancellationToken);
        var current = await LoadAsync(cancellationToken);
        if (!string.Equals(current?.Token, expectedToken, StringComparison.Ordinal)) throw new StorageConflictException();
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken);
                await stream.FlushAsync(cancellationToken); stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, _path, true); return Token(json);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        for (var retry = 0; ; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (retry < 100) { await Task.Delay(50, cancellationToken); }
        }
    }
}
