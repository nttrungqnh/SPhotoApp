using ClosedXML.Excel; using Microsoft.AspNetCore.DataProtection; using Microsoft.EntityFrameworkCore; using Microsoft.Extensions.Options; using Microsoft.Playwright; using SphotoApp.Web.Data; using SphotoApp.Web.Models; using SphotoApp.Web.Options; using System.Net.Http.Json; using System.Net.Security; using System.Net.Sockets; using System.Text; using System.Text.RegularExpressions;
namespace SphotoApp.Web.Services;
public interface ICustomerService { Task<List<Customer>> ListAsync(); Task SaveAsync(Customer customer); Task DeleteAsync(int id); Task<string?> GetRequirementAsync(string name); }
public class CustomerService(AppDbContext db):ICustomerService { public Task<List<Customer>> ListAsync()=>db.Customers.OrderBy(x=>x.Name).ToListAsync(); public async Task SaveAsync(Customer c){var duplicate=await db.Customers.AnyAsync(x=>x.Id!=c.Id&&x.Name.ToLower()==c.Name.ToLower());if(duplicate)throw new InvalidOperationException("Tên khách hàng đã tồn tại.");var old=await db.Customers.FirstOrDefaultAsync(x=>x.Id==c.Id);if(old is null)db.Customers.Add(c);else{old.Name=c.Name;old.Requirement=c.Requirement;old.Note=c.Note;old.UpdatedAt=DateTime.UtcNow;}await db.SaveChangesAsync();} public async Task DeleteAsync(int id){var c=await db.Customers.FindAsync(id);if(c is not null){db.Customers.Remove(c);await db.SaveChangesAsync();}} public Task<string?> GetRequirementAsync(string name)=>db.Customers.Where(x=>x.Name==name).Select(x=>x.Requirement).FirstOrDefaultAsync(); }
public record EppDownloadResult(bool Success,string? FileName,string? FilePath,string? ErrorMessage,DateTime DownloadedAt);
public interface IEppConfigService { Task<EppConfig?> GetActiveAsync(); Task SaveAsync(EppConfig config,string? password,string? googleDriveApiKey,string? gmailAppPassword); string Decrypt(EppConfig config); string? DecryptGoogleDriveApiKey(EppConfig config); string? DecryptGmailAppPassword(EppConfig config); }
public class EppConfigService(AppDbContext db,IDataProtectionProvider p):IEppConfigService { readonly IDataProtector protector=p.CreateProtector("SphotoApp.EppPassword.v1"); readonly IDataProtector driveKeyProtector=p.CreateProtector("SphotoApp.GoogleDriveApiKey.v1"); readonly IDataProtector gmailKeyProtector=p.CreateProtector("SphotoApp.GmailAppPassword.v1"); public Task<EppConfig?> GetActiveAsync()=>db.EppConfigs.FirstOrDefaultAsync(x=>x.IsActive); public async Task SaveAsync(EppConfig c,string? password,string? googleDriveApiKey,string? gmailAppPassword) { var current=await db.EppConfigs.FirstOrDefaultAsync(x=>x.Id==c.Id); if(current is null) { c.EncryptedPassword=protector.Protect(password??"");if(!string.IsNullOrWhiteSpace(googleDriveApiKey))c.EncryptedGoogleDriveApiKey=driveKeyProtector.Protect(googleDriveApiKey);if(!string.IsNullOrWhiteSpace(gmailAppPassword))c.EncryptedGmailAppPassword=gmailKeyProtector.Protect(gmailAppPassword); db.Add(c); } else { current.Name=c.Name;current.LoginUrl=c.LoginUrl;current.Username=c.Username;current.GoogleDriveFolderUrl=c.GoogleDriveFolderUrl;current.GmailEmail=c.GmailEmail;current.GmailImapHost=c.GmailImapHost;current.GmailImapPort=c.GmailImapPort;current.GmailUseSsl=c.GmailUseSsl;current.IsActive=c.IsActive;current.UpdatedAt=DateTime.UtcNow;if(!string.IsNullOrWhiteSpace(password))current.EncryptedPassword=protector.Protect(password);if(!string.IsNullOrWhiteSpace(googleDriveApiKey))current.EncryptedGoogleDriveApiKey=driveKeyProtector.Protect(googleDriveApiKey);if(!string.IsNullOrWhiteSpace(gmailAppPassword))current.EncryptedGmailAppPassword=gmailKeyProtector.Protect(gmailAppPassword); } await db.SaveChangesAsync(); } public string Decrypt(EppConfig c)=>protector.Unprotect(c.EncryptedPassword); public string? DecryptGoogleDriveApiKey(EppConfig c) { try { return string.IsNullOrWhiteSpace(c.EncryptedGoogleDriveApiKey)?null:driveKeyProtector.Unprotect(c.EncryptedGoogleDriveApiKey); } catch { return null; } } public string? DecryptGmailAppPassword(EppConfig c) { try { return string.IsNullOrWhiteSpace(c.EncryptedGmailAppPassword)?null:gmailKeyProtector.Unprotect(c.EncryptedGmailAppPassword); } catch { return null; } }
}
public interface ITemplateService { Task<ExcelTemplate?> GetActiveAsync(); Task<ExcelTemplate> UploadAsync(string name,IFormFile file,string? sheet,int row); Task SetActiveAsync(int id); Task DeleteAsync(int id); }
public class TemplateService(AppDbContext db,IWebHostEnvironment env,IOptions<StorageOptions> settings):ITemplateService { public Task<ExcelTemplate?> GetActiveAsync()=>db.ExcelTemplates.Include(x=>x.Mappings).FirstOrDefaultAsync(x=>x.IsActive); public async Task<ExcelTemplate> UploadAsync(string name,IFormFile f,string? sheet,int row) { if(Path.GetExtension(f.FileName).ToLowerInvariant()!=".xlsx")throw new InvalidOperationException("Chỉ cho phép file .xlsx."); foreach(var x in db.ExcelTemplates)x.IsActive=false; var stored=$"{Guid.NewGuid():N}.xlsx";var path=Path.Combine(env.ContentRootPath,settings.Value.Root,settings.Value.TemplateFolder,stored);using var s=File.Create(path);await f.CopyToAsync(s);var t=new ExcelTemplate{Name=name,FileName=Path.GetFileName(f.FileName),StoredFileName=stored,FilePath=path,TargetSheetName=sheet,TargetStartRow=Math.Max(1,row),IsActive=true};db.Add(t);await db.SaveChangesAsync();return t;} public async Task SetActiveAsync(int id){foreach(var x in db.ExcelTemplates)x.IsActive=x.Id==id;await db.SaveChangesAsync();} public async Task DeleteAsync(int id){var x=await db.ExcelTemplates.FindAsync(id);if(x is null)return;if(File.Exists(x.FilePath))File.Delete(x.FilePath);db.Remove(x);await db.SaveChangesAsync();} }
public interface IGoogleDriveService { string? LastError { get; } Task<string?> FindFolderUrlAsync(string? parentFolderUrl, string folderName, string? apiKey, CancellationToken ct = default); }
public class GoogleDriveService(HttpClient http, IOptions<GoogleDriveOptions> options, ILogger<GoogleDriveService> log) : IGoogleDriveService
{
    private readonly Dictionary<string, string?> folderLookup = new(StringComparer.Ordinal);
    private string? indexedParentId;
    public string? LastError { get; private set; }

    public async Task<string?> FindFolderUrlAsync(string? parentFolderUrl, string folderName, string? apiKey, CancellationToken ct = default)
    {
        apiKey ??= options.Value.ApiKey;
        var parentId = ExtractFolderId(parentFolderUrl);
        if (string.IsNullOrWhiteSpace(apiKey)) { LastError = "Ứng dụng chưa nạp được khóa Google Drive API."; return null; }
        if (string.IsNullOrWhiteSpace(parentId)) { LastError = "URL thư mục Google Drive không hợp lệ."; return null; }
        try
        {
            if (!string.Equals(indexedParentId, parentId, StringComparison.Ordinal))
            {
                LastError = null;
                folderLookup.Clear();
                indexedParentId = parentId;
                string? nextPageToken = null;
                do
                {
                    var query = $"'{parentId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
                    var requestUrl = $"https://www.googleapis.com/drive/v3/files?key={Uri.EscapeDataString(apiKey)}&q={Uri.EscapeDataString(query)}&fields=nextPageToken,files(id,name)&pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true";
                    if (!string.IsNullOrWhiteSpace(nextPageToken)) requestUrl += $"&pageToken={Uri.EscapeDataString(nextPageToken)}";
                    var result = await http.GetFromJsonAsync<GoogleDriveFilesResponse>(requestUrl, ct);
                    foreach (var folder in result?.Files ?? [])
                        if (!string.IsNullOrWhiteSpace(folder.Name) && !string.IsNullOrWhiteSpace(folder.Id))
                            folderLookup[folder.Name.Trim()] = $"https://drive.google.com/drive/folders/{folder.Id}";
                    nextPageToken = result?.NextPageToken;
                } while (!string.IsNullOrWhiteSpace(nextPageToken));
            }
            return folderLookup.GetValueOrDefault(folderName.Trim());
        }
        catch (Exception ex)
        {
            indexedParentId = null;
            folderLookup.Clear();
            LastError = ex is HttpRequestException ? "Không thể kết nối Google Drive API." : "Google Drive API trả về lỗi khi đọc danh sách thư mục.";
            log.LogWarning(ex, "Không thể tra cứu thư mục Google Drive cho mã {FolderName}.", folderName);
            return null;
        }
    }

    static string? ExtractFolderId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var match = Regex.Match(url, @"/folders/([^/?#]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private sealed class GoogleDriveFilesResponse { public string? NextPageToken { get; set; } public List<GoogleDriveFile>? Files { get; set; } }
    private sealed class GoogleDriveFile { public string? Id { get; set; } public string? Name { get; set; } }
}
public sealed record GmailOrderData(string? Link,string? CustomerRequest);
public interface IGmailConnectionService { Task<(bool Success,string Message)> TestAsync(EppConfig config,CancellationToken ct=default); Task<GmailOrderData?> FindOrderAsync(string orderId,CancellationToken ct=default); }
public sealed class GmailConnectionService(IEppConfigService configs,ILogger<GmailConnectionService> log):IGmailConnectionService
{
    public async Task<GmailOrderData?> FindOrderAsync(string orderId,CancellationToken ct=default)
    {
        var config=await configs.GetActiveAsync(); if(config is null) return null;
        if (string.IsNullOrWhiteSpace(config.GmailEmail)||string.IsNullOrWhiteSpace(orderId)) return null;
        var password=configs.DecryptGmailAppPassword(config); if(string.IsNullOrWhiteSpace(password)) return null;
        try
        {
            var host=string.IsNullOrWhiteSpace(config.GmailImapHost)?"imap.gmail.com":config.GmailImapHost.Trim(); var port=config.GmailImapPort<=0?993:config.GmailImapPort;
            using var tcp=new TcpClient(); await tcp.ConnectAsync(host,port,ct); await using Stream raw=tcp.GetStream(); Stream active=raw;
            if(config.GmailUseSsl||port==993){var ssl=new SslStream(raw,false);await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost=host},ct);active=ssl;}
            using var reader=new StreamReader(active,Encoding.ASCII,false,4096,leaveOpen:true); await using var writer=new StreamWriter(active,Encoding.ASCII,4096,leaveOpen:true){NewLine="\r\n",AutoFlush=true};
            var greeting=await reader.ReadLineAsync(ct); if(string.IsNullOrWhiteSpace(greeting)) return null;
            await writer.WriteLineAsync($"A001 LOGIN \"{Escape(config.GmailEmail)}\" \"{Escape(password)}\""); if(!(await ReadTaggedAsync(reader,"A001",ct)).StartsWith("A001 OK",StringComparison.OrdinalIgnoreCase)) return null;
            await writer.WriteLineAsync("A002 SELECT INBOX"); if(!(await ReadTaggedAsync(reader,"A002",ct)).StartsWith("A002 OK",StringComparison.OrdinalIgnoreCase)) return null;
            await writer.WriteLineAsync($"A003 SEARCH OR SUBJECT \"{Escape(orderId)}\" TEXT \"{Escape(orderId)}\""); var search=await ReadResponseAsync(reader,"A003",ct); var ids=search.Where(x=>x.StartsWith("* SEARCH ",StringComparison.OrdinalIgnoreCase)).SelectMany(x=>x[9..].Split(' ',StringSplitOptions.RemoveEmptyEntries)).Reverse().Take(100).ToArray();
            foreach(var id in ids)
            {
                await writer.WriteLineAsync($"A004 FETCH {id} BODY.PEEK[]");
                var body=await ReadResponseAsync(reader,"A004",ct); var text=System.Net.WebUtility.HtmlDecode(string.Join("\n",body));
                // Quoted-printable HTML can split an URL across a soft line break.
                text=Regex.Replace(text,"=\\r?\\n",string.Empty);
                text=Regex.Replace(text,"=([0-9A-Fa-f]{2})",m=>((char)Convert.ToByte(m.Groups[1].Value,16)).ToString());
                var matches=Regex.Matches(text, "https://[^\\s<>\\\"']+", RegexOptions.IgnoreCase);
                // Ignore incidental CSS/tracking links; only accept known delivery hosts.
                var match=matches.Cast<Match>().Where(x=>Regex.IsMatch(x.Value,"(?:dropbox\\.com|we\\.tl|wetransfer\\.com|drive\\.google\\.com)",RegexOptions.IgnoreCase)).OrderByDescending(x=>x.Value.Length).FirstOrDefault();
                if(match is not null){await writer.WriteLineAsync("A005 LOGOUT");return new GmailOrderData(match.Value.TrimEnd('.',',',';',')'),ExtractCustomerRequest(text));}
            }
            await writer.WriteLineAsync("A005 LOGOUT"); return null;
        }
        catch(Exception ex){log.LogWarning(ex,"Không thể tìm email cho mã đơn hàng {OrderId}",orderId);return null;}
    }
    static string? ExtractCustomerRequest(string text)
    {
        var plain=Regex.Replace(text,"<br\\s*/?>|</(?:div|p|tr|h[1-6])>","\\n",RegexOptions.IgnoreCase);
        plain=Regex.Replace(plain,"<[^>]+>"," "); plain=System.Net.WebUtility.HtmlDecode(plain);
        var deliver=Regex.Match(plain,"Deliver\\s+As\\s*:[^\\r\\n]*(?:\\r?\\n|$)",RegexOptions.IgnoreCase);
        if(!deliver.Success) return null;
        var request=plain[(deliver.Index+deliver.Length)..];
        var profile=request.IndexOf("Customer Profile",StringComparison.OrdinalIgnoreCase); if(profile>=0) request=request[..profile];
        request=request.Replace("\\n"," ",StringComparison.Ordinal).Replace("&#x20;"," ",StringComparison.OrdinalIgnoreCase);
        request=request.Replace("=20"," ",StringComparison.OrdinalIgnoreCase).Trim();
        request=Regex.Replace(request,"^(?:JPG\\s*,?\\s*DNG\\s*files?|JPG|PNG|DNG)(?:\\s|,|\\n)*","",RegexOptions.IgnoreCase).Trim();
        request=Regex.Replace(request,"\\bSee\\s+example\\s+images\\b.*$","",RegexOptions.IgnoreCase);
        request=Regex.Replace(request,"\\s+"," ").Trim();
        return string.IsNullOrWhiteSpace(request)?null:request;
    }
    public async Task<(bool Success,string Message)> TestAsync(EppConfig config,CancellationToken ct=default)
    {
        if (string.IsNullOrWhiteSpace(config.GmailEmail)) return (false,"Vui lòng nhập email Gmail.");
        var password=configs.DecryptGmailAppPassword(config);
        if (string.IsNullOrWhiteSpace(password)) return (false,"Chưa có Gmail App Password đã lưu.");
        var host=string.IsNullOrWhiteSpace(config.GmailImapHost)?"imap.gmail.com":config.GmailImapHost.Trim();
            var port=config.GmailImapPort<=0?993:config.GmailImapPort;
            // Gmail's implicit-TLS IMAP endpoint on port 993 requires SSL even
            // if an older saved configuration left the checkbox unchecked.
            var useSsl=config.GmailUseSsl || port==993;
        try
        {
            using var tcp=new TcpClient(); await tcp.ConnectAsync(host,port,ct);
            await using Stream stream=tcp.GetStream();
            Stream active=stream;
            if(useSsl)
            {
                var ssl=new SslStream(stream,false); await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, ct); active=ssl;
            }
            using var reader=new StreamReader(active,Encoding.ASCII,false,1024,leaveOpen:true);
            await using var writer=new StreamWriter(active,Encoding.ASCII,1024,leaveOpen:true){NewLine="\r\n",AutoFlush=true};
            var greeting=await reader.ReadLineAsync(ct);
            if(string.IsNullOrWhiteSpace(greeting)||(!greeting.StartsWith("* OK",StringComparison.OrdinalIgnoreCase)&&!greeting.StartsWith("* PREAUTH",StringComparison.OrdinalIgnoreCase))) return (false,"Gmail không trả về lời chào IMAP hợp lệ. Hãy bật SSL khi dùng cổng 993.");
            await writer.WriteLineAsync($"A001 LOGIN \"{Escape(config.GmailEmail)}\" \"{Escape(password)}\"");
            var login=await ReadTaggedAsync(reader,"A001",ct);
            if(!login.StartsWith("A001 OK",StringComparison.OrdinalIgnoreCase)) return (false,"Gmail từ chối đăng nhập. Hãy kiểm tra App Password và bật IMAP.");
            await writer.WriteLineAsync("A002 NOOP"); await ReadTaggedAsync(reader,"A002",ct);
            await writer.WriteLineAsync("A003 LOGOUT");
            return (true,"Kết nối Gmail IMAP thành công.");
        }
        catch(Exception ex)
        {
            log.LogWarning(ex,"Không thể kiểm tra kết nối Gmail IMAP");
            return (false,"Không thể kết nối Gmail IMAP. Kiểm tra host, port, SSL và App Password.");
        }
    }
    static async Task<string> ReadTaggedAsync(StreamReader reader,string tag,CancellationToken ct)
    {
        string? line; var last="";
        while((line=await reader.ReadLineAsync(ct)) is not null){last=line;if(line.StartsWith(tag+" ",StringComparison.OrdinalIgnoreCase))break;}
        return last;
    }
    static async Task<List<string>> ReadResponseAsync(StreamReader reader,string tag,CancellationToken ct)
    { var lines=new List<string>(); string? line; while((line=await reader.ReadLineAsync(ct)) is not null){lines.Add(line);if(line.StartsWith(tag+" ",StringComparison.OrdinalIgnoreCase))break;} return lines; }
    static string Escape(string value)=>value.Replace("\\","\\\\",StringComparison.Ordinal).Replace("\"","\\\"",StringComparison.Ordinal);
}
public interface IExcelProcessingService { Task<(string FileName,string FilePath,int Records,int DriveLookups,int DriveLinksFound,int EmailLinksFound,string? DriveError)> ProcessAsync(string source,ExcelTemplate template,string? driveFolderUrl,string? driveApiKey,CancellationToken ct=default); }
public class ExcelProcessingService(IWebHostEnvironment env,IOptions<StorageOptions> settings,IOptions<GoogleDriveOptions> driveOptions,IGoogleDriveService drive,IGmailConnectionService gmail,ICustomerService customers):IExcelProcessingService { public async Task<(string,string,int,int,int,int,string?)> ProcessAsync(string source,ExcelTemplate t,string? driveFolderUrl,string? driveApiKey,CancellationToken ct=default) { using var input=new XLWorkbook(source);using var output=new XLWorkbook(t.FilePath);var sourceSheet=input.Worksheets.First();var targetSheet=string.IsNullOrWhiteSpace(t.TargetSheetName)?output.Worksheets.First():output.Worksheet(t.TargetSheetName);var firstOutputRow=Math.Max(2,t.TargetStartRow);var count=0;var driveLookups=0;var driveLinksFound=0;var emailLinksFound=0;var parentFolderUrl=string.IsNullOrWhiteSpace(driveFolderUrl)?driveOptions.Value.ParentFolderUrl:driveFolderUrl;foreach(var sourceRow in sourceSheet.RowsUsed().Skip(1).Where(r=>!r.CellsUsed().All(cell=>cell.IsEmpty())).OrderBy(r=>r.Cell(4).GetString(),StringComparer.OrdinalIgnoreCase).ThenBy(r=>{var s=r.Cell(2).GetString();return DateTime.TryParse(s,out var d)?d:DateTime.MaxValue;})){if(sourceRow.CellsUsed().All(cell=>cell.IsEmpty()))continue;var targetRow=firstOutputRow+count;targetSheet.Cell(targetRow,1).Value=sourceRow.Cell(3).Value;targetSheet.Cell(targetRow,2).Value=sourceRow.Cell(4).Value;targetSheet.Cell(targetRow,3).Value=sourceRow.Cell(5).Value;targetSheet.Cell(targetRow,4).Value=sourceRow.Cell(6).Value;var customerRequirement=await customers.GetRequirementAsync(sourceRow.Cell(4).GetString().Trim());if(!string.IsNullOrWhiteSpace(customerRequirement))targetSheet.Cell(targetRow,12).Value=customerRequirement;var downloadValue=sourceRow.Cell(5).GetString().Trim();if(Regex.IsMatch(downloadValue,@"^\d+$")){driveLookups++;var folderUrl=await drive.FindFolderUrlAsync(parentFolderUrl,downloadValue,driveApiKey,ct);if(!string.IsNullOrWhiteSpace(folderUrl)){targetSheet.Cell(targetRow,5).Value=folderUrl;driveLinksFound++;}else{var emailData=await gmail.FindOrderAsync(downloadValue,ct);if(emailData is not null&&!string.IsNullOrWhiteSpace(emailData.Link)){targetSheet.Cell(targetRow,5).Value=emailData.Link;if(!string.IsNullOrWhiteSpace(emailData.CustomerRequest))targetSheet.Cell(targetRow,13).Value=emailData.CustomerRequest;emailLinksFound++;}}}else if(downloadValue.StartsWith("https://",StringComparison.OrdinalIgnoreCase)){targetSheet.Cell(targetRow,5).Value=downloadValue;}count++;}if(count>0){var mappedRange=targetSheet.Range(firstOutputRow,1,firstOutputRow+count-1,5);mappedRange.Style.Alignment.WrapText=true;targetSheet.Column(1).Width=55;targetSheet.Column(2).Width=22;targetSheet.Column(3).Width=45;targetSheet.Column(4).Width=18;targetSheet.Column(5).Width=55;for(var row=firstOutputRow;row<firstOutputRow+count;row++)targetSheet.Row(row).AdjustToContents();}var n=$"Planning_Result_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";var p=Path.Combine(env.ContentRootPath,settings.Value.Root,settings.Value.OutputFolder,n);output.SaveAs(p);return (n,p,count,driveLookups,driveLinksFound,emailLinksFound,drive.LastError);} }
public interface IEppAutomationService { Task<EppDownloadResult> DownloadPlanningAsync(EppConfig config,CancellationToken ct=default); Task<(bool Success,string Message)> TestLoginAsync(EppConfig config,CancellationToken ct=default); }
public static class EppSelectors
{
    public const string PlanningUrl = "https://epp-portal.com/orders/plannings";
    public static readonly string[] Email = { "input[type=email]", "input[name=email]", "input[name=username]" };
    public static readonly string[] Password = { "input[type=password]" };
    public static readonly string[] LoginButton = { "button:has-text('Sign In')", "button:has-text('Login')", "input[type=submit]" };
    // The Download control is in the Planning toolbar, not the table's Download column.
    public static readonly string[] Download = { "button:has-text('Download')", "a:has-text('Download')", "[role=button]:has-text('Download')" };
}
public class EppAutomationService(IOptions<EppAutomationOptions> options,IOptions<StorageOptions> storage,IWebHostEnvironment env,ILogger<EppAutomationService> log,IEppConfigService configs):IEppAutomationService
{
    public async Task<(bool,string)> TestLoginAsync(EppConfig c,CancellationToken ct=default) { var r=await RunAsync(c,false,ct); return (r.Success,r.Success?"Đăng nhập EPP thành công.":r.ErrorMessage!); }
    public Task<EppDownloadResult> DownloadPlanningAsync(EppConfig c,CancellationToken ct=default)=>RunAsync(c,true,ct);
    async Task<EppDownloadResult> RunAsync(EppConfig c,bool download,CancellationToken ct)
    {
        IPage? page=null; var step="Khởi tạo trình duyệt";
        try {
            using var pw=await Playwright.CreateAsync(); var browserPath=ResolveBrowserPath(); await using var browser=await pw.Chromium.LaunchAsync(new(){Headless=options.Value.Headless,ExecutablePath=browserPath}); page=await browser.NewPageAsync(); page.SetDefaultTimeout(options.Value.Timeout);
            step="Mở trang đăng nhập EPP"; await page.GotoAsync(c.LoginUrl, new(){WaitUntil=WaitUntilState.DOMContentLoaded});
            step="Tìm ô Email"; await page.Locator(string.Join(",",EppSelectors.Email)).First.FillAsync(c.Username); step="Tìm ô Password"; await page.Locator(string.Join(",",EppSelectors.Password)).First.FillAsync(configs.Decrypt(c)); step="Bấm Sign In"; await page.Locator(string.Join(",",EppSelectors.LoginButton)).First.ClickAsync();
            await page.WaitForTimeoutAsync(1500);
            if (!download)
            {
                var passwordStillVisible = await page.Locator(string.Join(",", EppSelectors.Password)).First.IsVisibleAsync();
                if (passwordStillVisible) throw new InvalidOperationException("EPP vẫn hiển thị form đăng nhập; hãy kiểm tra tài khoản hoặc mật khẩu.");
                return new(true,null,null,null,DateTime.UtcNow);
            }
            step="Mở trang Planning"; await page.GotoAsync(EppSelectors.PlanningUrl, new(){WaitUntil=WaitUntilState.DOMContentLoaded});
            step="Bấm nút Download trên thanh công cụ"; var task=page.WaitForDownloadAsync(); await page.Locator(string.Join(",",EppSelectors.Download)).First.ClickAsync(); var d=await task;
            var n=$"Planning_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"; var path=Path.Combine(env.ContentRootPath,storage.Value.Root,storage.Value.DownloadFolder,n); await d.SaveAsAsync(path); return new(true,n,path,null,DateTime.UtcNow);
        } catch(Exception ex) {
            var basePath = Path.Combine(env.ContentRootPath,storage.Value.Root,storage.Value.ErrorFolder,$"epp_error_{DateTime.Now:yyyyMMdd_HHmmss}"); Directory.CreateDirectory(Path.GetDirectoryName(basePath)!); var screenshot = basePath+".png";
            try { if(page is not null) await page.ScreenshotAsync(new(){Path=screenshot,FullPage=true}); } catch { }
            await File.WriteAllTextAsync(basePath+".txt",$"Step: {step}{Environment.NewLine}Exception: {ex.GetType().Name}{Environment.NewLine}Message: {ex.Message}{Environment.NewLine}Url: {page?.Url}");
            log.LogError(ex,"EPP automation failed. Screenshot: {Screenshot}",screenshot);
            var message = ex is InvalidOperationException ? ex.Message
                : ex.Message.Contains("Executable doesn't exist",StringComparison.OrdinalIgnoreCase) ? "Chưa cài Chromium cho Playwright. Hãy chạy install-playwright.ps1 rồi khởi động lại ứng dụng."
                : ex is TimeoutException ? "Hết thời gian chờ tại trang EPP. Kiểm tra kết nối, tài khoản hoặc selector EPP; ảnh chẩn đoán đã được lưu trong Storage/Errors."
                : ex.Message.Contains("net::",StringComparison.OrdinalIgnoreCase) ? "Không thể kết nối EPP Portal. Hãy kiểm tra Login URL hoặc kết nối mạng."
                : "Không thể hoàn tất EPP. Ảnh chẩn đoán đã được lưu trong Storage/Errors; kiểm tra log ứng dụng để biết bước gây lỗi.";
            return new(false,null,null,$"Lỗi tại bước: {step}. {message}",DateTime.UtcNow);
        }
    }
    string? ResolveBrowserPath()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.BrowserExecutablePath) && File.Exists(options.Value.BrowserExecutablePath)) return options.Value.BrowserExecutablePath;
        var systemChrome=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe");
        if (File.Exists(systemChrome)) return systemChrome;
        var cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ms-playwright");
        return Directory.Exists(cache) ? Directory.EnumerateDirectories(cache,"chromium-*").OrderByDescending(x=>x).Select(x=>Path.Combine(x,"chrome-win","chrome.exe")).FirstOrDefault(File.Exists) : null;
    }
}
