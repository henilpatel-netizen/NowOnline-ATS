namespace Ats.Application.Abstractions;

public sealed record FileDownload(Stream Content, string ContentType, string DownloadName);

public sealed record StoredFileInfo(long Length, string ContentType, string FileName);

public interface IFileStore
{
    // Stores the content under a generated opaque key (preserving the extension) and returns the key.
    Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default);

    // Opens a previously stored file by key, or null if the key is invalid or missing.
    Task<FileDownload?> OpenAsync(string key, CancellationToken ct = default);

    // Metadata for a stored file, or null if the key is invalid or missing. Does not open a stream.
    Task<StoredFileInfo?> StatAsync(string key, CancellationToken ct = default);

    // Deletes a stored file by key. No-op if the key is invalid or the file is missing.
    Task DeleteAsync(string key, CancellationToken ct = default);
}
