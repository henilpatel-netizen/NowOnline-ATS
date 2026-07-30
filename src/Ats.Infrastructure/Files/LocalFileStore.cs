using Ats.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ats.Infrastructure.Files;

public sealed class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["FileStorage:LocalPath"];
        if (string.IsNullOrWhiteSpace(configured)) configured = "App_Data/uploads";
        _root = Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName);
        var key = Guid.NewGuid().ToString("N") + ext;
        var path = Path.Combine(_root, key);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task<FileDownload?> OpenAsync(string key, CancellationToken ct = default)
    {
        if (!IsBareKey(key)) return Task.FromResult<FileDownload?>(null);
        var path = Path.Combine(_root, key);
        if (!File.Exists(path)) return Task.FromResult<FileDownload?>(null);

        Stream stream = File.OpenRead(path);
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return Task.FromResult<FileDownload?>(new FileDownload(stream, ContentTypeFor(key), "resume" + ext));
    }

    public Task<StoredFileInfo?> StatAsync(string key, CancellationToken ct = default)
    {
        if (!IsBareKey(key)) return Task.FromResult<StoredFileInfo?>(null);
        var path = Path.Combine(_root, key);
        if (!File.Exists(path)) return Task.FromResult<StoredFileInfo?>(null);
        var info = new FileInfo(path);
        return Task.FromResult<StoredFileInfo?>(
            new StoredFileInfo(info.Length, ContentTypeFor(key), "resume" + Path.GetExtension(key)));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        if (!IsBareKey(key)) return Task.CompletedTask;
        var path = Path.Combine(_root, key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // A valid stored key is a bare file name: no path separators, no traversal.
    private static bool IsBareKey(string key) =>
        !string.IsNullOrWhiteSpace(key) && !key.Contains('/') && !key.Contains('\\') && !key.Contains("..");

    private static string ContentTypeFor(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };
}
