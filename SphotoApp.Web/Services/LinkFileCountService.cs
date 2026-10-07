using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using SphotoApp.Web.Options;

namespace SphotoApp.Web.Services;

public interface ILinkFileCountService
{
    Task<int?> CountAsync(string url, string? driveApiKey, CancellationToken ct = default);
}

public sealed class LinkFileCountService(HttpClient http, IOptions<GoogleDriveOptions> driveOptions,
    IOptions<EppAutomationOptions> browserOptions) : ILinkFileCountService, IDisposable
{
    private readonly ConcurrentDictionary<string, Task<int?>> cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim browserGate = new(1, 1);
    private IPlaywright? playwright;
    private IBrowser? browser;

    public Task<int?> CountAsync(string url, string? driveApiKey, CancellationToken ct = default) =>
        cache.GetOrAdd(url, _ => CountCoreAsync(url, driveApiKey, ct));

    private async Task<int?> CountCoreAsync(string url, string? driveApiKey, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            if (IsHost(uri, "drive.google.com")) return await CountDriveAsync(uri, driveApiKey ?? driveOptions.Value.ApiKey, timeout.Token);
            if (!new[] { "dropbox.com", "we.tl", "wetransfer.com", "fromsmash.com", "transfernow.net" }.Any(host => IsHost(uri, host))) return null;
            var engine = await GetBrowserAsync(timeout.Token);
            await using var context = await engine.NewContextAsync(new() { Locale = "en-US", AcceptDownloads = false });
            await context.RouteAsync("**/*", route => route.Request.ResourceType is "image" or "media" or "font" ? route.AbortAsync() : route.ContinueAsync());
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(15000);
            using var cancellation = timeout.Token.Register(() => { _ = context.CloseAsync(); });
            if (IsHost(uri, "dropbox.com")) return await CountDropboxAsync(page, url, timeout.Token);
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
            // Transfer providers sometimes expose an explicit total even without a public listing API.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(1000, timeout.Token);
                var text = await page.Locator("body").InnerTextAsync();
                if (Regex.IsMatch(text, @"expired|not found|no longer available|access denied|password required", RegexOptions.IgnoreCase)) return null;
                var totals = Regex.Matches(text, @"(?im)^\s*(?:Download\s+|Total[: ]+)?(\d+)\s+files?\s*(?:[•·(].*)?$", RegexOptions.IgnoreCase)
                    .Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToArray();
                if (totals.Length == 1) return totals[0];
            }
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { ct.ThrowIfCancellationRequested(); return null; }
    }

    private async Task<int?> CountDriveAsync(Uri uri, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var match = Regex.Match(uri.AbsolutePath, @"/folders/([a-zA-Z0-9_-]+)");
        if (!match.Success) return null;
        var folders = new Queue<string>(); folders.Enqueue(match.Groups[1].Value);
        var visited = new HashSet<string>(StringComparer.Ordinal); var files = new HashSet<string>(StringComparer.Ordinal);
        while (folders.TryDequeue(out var folder))
        {
            if (!visited.Add(folder)) continue;
            string? token = null;
            do
            {
                var query = $"'{folder}' in parents and trashed = false";
                var url = $"https://www.googleapis.com/drive/v3/files?key={Uri.EscapeDataString(key)}&q={Uri.EscapeDataString(query)}&fields=nextPageToken,incompleteSearch,files(id,mimeType)&pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true";
                if (token is not null) url += "&pageToken=" + Uri.EscapeDataString(token);
                using var data = await http.GetFromJsonAsync<JsonDocument>(url, ct);
                if (data is null || !data.RootElement.TryGetProperty("files", out var entries)) return null;
                if (data.RootElement.TryGetProperty("incompleteSearch", out var incomplete) && incomplete.GetBoolean()) return null;
                foreach (var entry in entries.EnumerateArray())
                {
                    var id = entry.GetProperty("id").GetString()!;
                    if (entry.GetProperty("mimeType").GetString() == "application/vnd.google-apps.folder") folders.Enqueue(id);
                    else files.Add(id);
                }
                token = data.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            } while (!string.IsNullOrEmpty(token));
        }
        return files.Count;
    }

    private async Task<int?> CountDropboxAsync(IPage page, string url, CancellationToken ct)
    {
        var folders = new Queue<string>(); folders.Enqueue(url);
        var visited = new HashSet<string>(StringComparer.Ordinal); var files = new HashSet<string>(StringComparer.Ordinal);
        while (folders.TryDequeue(out var folder))
        {
            if (!visited.Add(folder)) continue;
            if (visited.Count > 100) return null;
            var response = await page.GotoAsync(folder, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
            if (response is null || !response.Ok) return null;
            await page.Locator("[data-testid='sl-grid-body']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15000 });
            var items = new Dictionary<string, DropboxItem>(StringComparer.Ordinal);
            var stable = 0; var complete = false;
            for (var step = 0; step < 180; step++)
            {
                ct.ThrowIfCancellationRequested();
                var snapshot = await page.EvaluateAsync<DropboxSnapshot>("""
                    () => {
                        const list = document.querySelector('[data-testid="sl-grid-body"]');
                        const links = [...list.querySelectorAll('[data-testid="grid-link"]')];
                        const items = links.map(a => {
                            const card = a.closest('li'); const text = card?.innerText || '';
                            const folder = !!card?.querySelector('[data-testid*="Folder"]');
                            const file = /\d[\d.,]*\s*(?:B|KB|MB|GB|TB)\b/i.test(text);
                            return { Url: a.href, Folder: folder, Known: folder || file };
                        });
                        let scroller = list.parentElement;
                        while(scroller && !(scroller.scrollHeight > scroller.clientHeight + 2 && /auto|scroll/.test(getComputedStyle(scroller).overflowY))) scroller = scroller.parentElement;
                        scroller ||= document.scrollingElement;
                        const bottom = scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 3;
                        const loading = !!document.querySelector('[role="progressbar"], [aria-busy="true"]');
                        scroller.scrollTop += Math.max(200, scroller.clientHeight * .8);
                        return { Items: items, Bottom: bottom, Loading: loading };
                    }
                    """);
                var before = items.Count;
                foreach (var item in snapshot.Items) items[item.Url] = item;
                stable = snapshot.Bottom && !snapshot.Loading && before == items.Count ? stable + 1 : 0;
                if (stable >= 3) { complete = true; break; }
                await Task.Delay(600, ct);
            }
            if (!complete || items.Values.Any(item => !item.Known)) return null;
            if (items.Count == 0)
            {
                if (!(await page.Locator("body").InnerTextAsync()).Contains("empty", StringComparison.OrdinalIgnoreCase)) return null;
            }
            foreach (var item in items.Values)
            {
                if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var child) || !IsHost(child, "dropbox.com")) return null;
                if (item.Folder) folders.Enqueue(item.Url); else files.Add(item.Url);
            }
        }
        return files.Count;
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        await browserGate.WaitAsync(ct);
        try
        {
            if (browser is not null) return browser;
            playwright = await Playwright.CreateAsync();
            var configured = browserOptions.Value.BrowserExecutablePath;
            var bundledRoot = Path.Combine(AppContext.BaseDirectory, ".playwright");
            var bundled = Directory.Exists(bundledRoot) ? Directory.EnumerateDirectories(bundledRoot, "chromium-*").OrderByDescending(path => path)
                .Select(path => Path.Combine(path, "chrome-win", "chrome.exe")).FirstOrDefault(File.Exists) : null;
            var candidates = new[] { configured, bundled,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe") };
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) });
            return browser;
        }
        finally { browserGate.Release(); }
    }
    private static bool IsHost(Uri uri, string host) => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    private sealed class DropboxSnapshot { public DropboxItem[] Items { get; set; } = []; public bool Bottom { get; set; } public bool Loading { get; set; } }
    private sealed class DropboxItem { public string Url { get; set; } = ""; public bool Folder { get; set; } public bool Known { get; set; } }
    public void Dispose()
    {
        try { browser?.CloseAsync().GetAwaiter().GetResult(); } finally { playwright?.Dispose(); browserGate.Dispose(); }
    }
}
