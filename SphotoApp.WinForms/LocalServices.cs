using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using SphotoApp.Web.Models;
using SphotoApp.Web.Services;
using System.Text.Json;

namespace SphotoApp.WinForms;

internal sealed class DesktopEnvironment : IWebHostEnvironment
{
    public DesktopEnvironment(string root)
    {
        ContentRootPath = root;
        WebRootPath = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(WebRootPath);
        ContentRootFileProvider = new PhysicalFileProvider(root);
        WebRootFileProvider = new PhysicalFileProvider(WebRootPath);
    }

    public string ApplicationName { get; set; } = "SphotoApp.Desktop";
    public string EnvironmentName { get; set; } = "Production";
    public string WebRootPath { get; set; }
    public IFileProvider WebRootFileProvider { get; set; }
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
}

internal static class LocalDataPaths
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Storage", "Data");
    public static string Config => Path.Combine(Root, "connection.json");
    public static string ConnectionString => Path.Combine(Root, "database-connection.json");
    public static string Customers => Path.Combine(Root, "customers.json");
    public static string History => Path.Combine(Root, "history.json");
}

internal static class LocalJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static async Task<T> ReadAsync<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options) ?? fallback;
        }
        catch { return fallback; }
    }

    public static async Task WriteAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, value, Options);
        File.Move(temp, path, true);
    }
}

internal sealed class DesktopConfigService(IDataProtectionProvider provider) : IEppConfigService
{
    private readonly IDataProtector eppProtector = provider.CreateProtector("SphotoApp.Desktop.Epp.v1");
    private readonly IDataProtector driveProtector = provider.CreateProtector("SphotoApp.Desktop.Drive.v1");
    private readonly IDataProtector gmailProtector = provider.CreateProtector("SphotoApp.Desktop.Gmail.v1");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<EppConfig?> GetActiveAsync()
    {
        var config = await LocalJson.ReadAsync<EppConfig?>(LocalDataPaths.Config, null);
        return config is { IsActive: true } ? config : null;
    }

    public async Task SaveAsync(EppConfig config, string? password, string? googleDriveApiKey, string? gmailAppPassword)
    {
        await gate.WaitAsync();
        try
        {
            var current = await LocalJson.ReadAsync<EppConfig?>(LocalDataPaths.Config, null);
            config.Id = 1;
            config.IsActive = true;
            config.CreatedAt = current?.CreatedAt ?? DateTime.UtcNow;
            config.UpdatedAt = DateTime.UtcNow;
            config.EncryptedPassword = !string.IsNullOrWhiteSpace(password) ? eppProtector.Protect(password) : current?.EncryptedPassword ?? "";
            config.EncryptedGoogleDriveApiKey = !string.IsNullOrWhiteSpace(googleDriveApiKey) ? driveProtector.Protect(googleDriveApiKey) : current?.EncryptedGoogleDriveApiKey;
            config.EncryptedGmailAppPassword = !string.IsNullOrWhiteSpace(gmailAppPassword) ? gmailProtector.Protect(gmailAppPassword) : current?.EncryptedGmailAppPassword;
            await LocalJson.WriteAsync(LocalDataPaths.Config, config);
        }
        finally { gate.Release(); }
    }

    public string Decrypt(EppConfig config) => eppProtector.Unprotect(config.EncryptedPassword);
    public string? DecryptGoogleDriveApiKey(EppConfig config) => Unprotect(driveProtector, config.EncryptedGoogleDriveApiKey);
    public string? DecryptGmailAppPassword(EppConfig config) => Unprotect(gmailProtector, config.EncryptedGmailAppPassword);

    private static string? Unprotect(IDataProtector protector, string? value)
    {
        try { return string.IsNullOrWhiteSpace(value) ? null : protector.Unprotect(value); }
        catch { return null; }
    }
}

internal sealed class DesktopConnectionService(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("SphotoApp.Desktop.ConnectionString.v1");

    public async Task<string?> GetAsync()
    {
        var value = await LocalJson.ReadAsync<string?>(LocalDataPaths.ConnectionString, null);
        try { return string.IsNullOrWhiteSpace(value) ? null : protector.Unprotect(value); }
        catch { return null; }
    }

    public Task SaveAsync(string value) => LocalJson.WriteAsync(LocalDataPaths.ConnectionString, protector.Protect(value));
}

internal sealed class DesktopCustomerService : ICustomerService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<List<Customer>> ListAsync() =>
        (await LocalJson.ReadAsync(LocalDataPaths.Customers, new List<Customer>())).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task SaveAsync(Customer customer)
    {
        await gate.WaitAsync();
        try
        {
            var items = await LocalJson.ReadAsync(LocalDataPaths.Customers, new List<Customer>());
            if (items.Any(x => x.Id != customer.Id && string.Equals(x.Name.Trim(), customer.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Tên khách hàng đã tồn tại.");
            var current = items.FirstOrDefault(x => x.Id == customer.Id);
            if (current is null)
            {
                customer.Id = items.Count == 0 ? 1 : items.Max(x => x.Id) + 1;
                customer.UpdatedAt = DateTime.UtcNow;
                items.Add(customer);
            }
            else
            {
                current.Name = customer.Name.Trim();
                current.Requirement = customer.Requirement;
                current.Note = customer.Note;
                current.UpdatedAt = DateTime.UtcNow;
            }
            await LocalJson.WriteAsync(LocalDataPaths.Customers, items);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(int id)
    {
        await gate.WaitAsync();
        try
        {
            var items = await LocalJson.ReadAsync(LocalDataPaths.Customers, new List<Customer>());
            items.RemoveAll(x => x.Id == id);
            await LocalJson.WriteAsync(LocalDataPaths.Customers, items);
        }
        finally { gate.Release(); }
    }

    public async Task<string?> GetRequirementAsync(string name) =>
        (await ListAsync()).FirstOrDefault(x => string.Equals(x.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))?.Requirement;
}

internal sealed class DesktopTemplateService(IWebHostEnvironment env) : ITemplateService
{
    private string TemplateFolder => Path.Combine(env.ContentRootPath, "Storage", "Templates");

    public Task<ExcelTemplate?> GetActiveAsync()
    {
        var path = Directory.Exists(TemplateFolder)
            ? Directory.EnumerateFiles(TemplateFolder, "*.xlsx").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        return Task.FromResult(path is null ? null : new ExcelTemplate
        {
            Id = 1, Name = Path.GetFileNameWithoutExtension(path), FileName = Path.GetFileName(path),
            StoredFileName = Path.GetFileName(path), FilePath = path, TargetStartRow = 2, IsActive = true
        });
    }

    public async Task<ExcelTemplate> UploadAsync(string name, IFormFile file, string? sheet, int row)
    {
        Directory.CreateDirectory(TemplateFolder);
        var path = Path.Combine(TemplateFolder, Path.GetFileName(file.FileName));
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream);
        return new ExcelTemplate { Id = 1, Name = name, FileName = file.FileName, StoredFileName = file.FileName, FilePath = path, TargetSheetName = sheet, TargetStartRow = Math.Max(1, row), IsActive = true };
    }

    public Task SetActiveAsync(int id) => Task.CompletedTask;
    public Task DeleteAsync(int id) => Task.CompletedTask;
}

internal sealed class LocalHistoryService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<List<DownloadHistory>> ListAsync() =>
        (await LocalJson.ReadAsync(LocalDataPaths.History, new List<DownloadHistory>())).OrderByDescending(x => x.StartedAt).ToList();

    public async Task<DownloadHistory> AddAsync()
    {
        await gate.WaitAsync();
        try
        {
            var items = await LocalJson.ReadAsync(LocalDataPaths.History, new List<DownloadHistory>());
            var item = new DownloadHistory { Id = items.Count == 0 ? 1 : items.Max(x => x.Id) + 1 };
            items.Add(item);
            await LocalJson.WriteAsync(LocalDataPaths.History, items);
            return item;
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(DownloadHistory item)
    {
        await gate.WaitAsync();
        try
        {
            var items = await LocalJson.ReadAsync(LocalDataPaths.History, new List<DownloadHistory>());
            var index = items.FindIndex(x => x.Id == item.Id);
            if (index >= 0) items[index] = item; else items.Add(item);
            await LocalJson.WriteAsync(LocalDataPaths.History, items);
        }
        finally { gate.Release(); }
    }

    public Task ClearAsync() => LocalJson.WriteAsync(LocalDataPaths.History, new List<DownloadHistory>());
    public async Task<DownloadHistory?> FindAsync(int id) => (await ListAsync()).FirstOrDefault(x => x.Id == id);
}
