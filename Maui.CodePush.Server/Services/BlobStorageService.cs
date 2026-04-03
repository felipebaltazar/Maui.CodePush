using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Maui.CodePush.Server.Services;

public class BlobStorageService
{
    private readonly BlobServiceClient? _blobClient;
    private readonly string _localUploadsPath;
    private readonly bool _useBlob;

    public BlobStorageService(IConfiguration configuration)
    {
        _localUploadsPath = configuration["Uploads:Path"] ?? "uploads";
        Directory.CreateDirectory(_localUploadsPath);

        var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
            ?? configuration["Azure:StorageConnectionString"];

        if (!string.IsNullOrEmpty(connectionString))
        {
            _blobClient = new BlobServiceClient(connectionString);
            _useBlob = true;
        }
    }

    public async Task<string> UploadReleaseAsync(Guid appId, Guid releaseId, string moduleName, byte[] data)
    {
        var safeName = SanitizeName(moduleName);
        var blobName = $"{appId}/releases/{releaseId}/{safeName}.dll";

        if (_useBlob)
        {
            var container = _blobClient!.GetBlobContainerClient("releases");
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlobClient(blobName);
            await blob.UploadAsync(new BinaryData(data), overwrite: true);
            return blobName;
        }

        // Fallback: local filesystem
        var dir = Path.Combine(_localUploadsPath, appId.ToString(), "releases", releaseId.ToString());
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{safeName}.dll");
        await File.WriteAllBytesAsync(filePath, data);
        return filePath;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', '.', '.' }).ToHashSet();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        if (string.IsNullOrEmpty(sanitized) || sanitized.Contains(".."))
            throw new ArgumentException($"Invalid module name: {name}");
        return sanitized;
    }

    public async Task<string> UploadPatchAsync(Guid appId, Guid patchId, byte[] data)
    {
        var blobName = $"{appId}/patches/{patchId}.dll";

        if (_useBlob)
        {
            var container = _blobClient!.GetBlobContainerClient("patches");
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlobClient(blobName);
            await blob.UploadAsync(new BinaryData(data), overwrite: true);
            return blobName;
        }

        var dir = Path.Combine(_localUploadsPath, appId.ToString(), "patches");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{patchId}.dll");
        await File.WriteAllBytesAsync(filePath, data);
        return filePath;
    }

    public async Task<byte[]?> DownloadAsync(string container, string blobName)
    {
        if (_useBlob)
        {
            try
            {
                var blobContainer = _blobClient!.GetBlobContainerClient(container);
                var blob = blobContainer.GetBlobClient(blobName);
                var response = await blob.DownloadContentAsync();
                return response.Value.Content.ToArray();
            }
            catch
            {
                return null;
            }
        }

        // Fallback: local file
        var filePath = Path.Combine(_localUploadsPath, blobName);
        if (!File.Exists(filePath))
        {
            // Try legacy flat path
            var parts = blobName.Split('/');
            if (parts.Length >= 3)
            {
                var legacyPath = Path.Combine(_localUploadsPath, parts[0], $"{parts[^1]}");
                if (File.Exists(legacyPath))
                    return await File.ReadAllBytesAsync(legacyPath);
            }
            return null;
        }
        return await File.ReadAllBytesAsync(filePath);
    }

    public async Task DeleteAsync(string container, string blobName)
    {
        if (_useBlob)
        {
            try
            {
                var blobContainer = _blobClient!.GetBlobContainerClient(container);
                var blob = blobContainer.GetBlobClient(blobName);
                await blob.DeleteIfExistsAsync();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BlobStorage] Error: {ex.Message}"); }
            return;
        }

        var filePath = Path.Combine(_localUploadsPath, blobName);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    public async Task DeleteAllForAppAsync(Guid appId)
    {
        if (_useBlob)
        {
            foreach (var containerName in new[] { "releases", "patches" })
            {
                try
                {
                    var container = _blobClient!.GetBlobContainerClient(containerName);
                    await foreach (var blob in container.GetBlobsAsync(prefix: $"{appId}/"))
                    {
                        await container.DeleteBlobIfExistsAsync(blob.Name);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BlobStorage] Error: {ex.Message}"); }
            }
            return;
        }

        var dir = Path.Combine(_localUploadsPath, appId.ToString());
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
