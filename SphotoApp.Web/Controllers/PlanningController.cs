using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SphotoApp.Web.Data;
using SphotoApp.Web.Models;
using SphotoApp.Web.Services;

namespace SphotoApp.Web.Controllers;

[Authorize]
public class PlanningController(AppDbContext db, IEppConfigService config, IEppAutomationService epp, ITemplateService templates, IExcelProcessingService excel) : Controller
{
    public IActionResult Index() => View();
    public async Task<IActionResult> History() => View(await db.DownloadHistories.OrderByDescending(x => x.StartedAt).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearHistory()
    {
        var histories = await db.DownloadHistories.ToListAsync();
        db.DownloadHistories.RemoveRange(histories);
        await db.SaveChangesAsync();
        TempData["Success"] = $"Đã xóa {histories.Count} bản ghi lịch sử tải.";
        return RedirectToAction(nameof(History));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Download()
    {
        var history = new DownloadHistory { CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) };
        db.Add(history); await db.SaveChangesAsync();
        try
        {
            var eppConfig = await config.GetActiveAsync() ?? throw new InvalidOperationException("Chưa có cấu hình EPP.");
            var template = await templates.GetActiveAsync() ?? throw new InvalidOperationException("Chưa có Excel Template đang sử dụng.");
            var download = await epp.DownloadPlanningAsync(eppConfig);
            if (!download.Success) throw new InvalidOperationException(download.ErrorMessage);
            history.SourceFileName = download.FileName; history.SourceFilePath = download.FilePath;
            var output = await excel.ProcessAsync(download.FilePath!, template, eppConfig.GoogleDriveFolderUrl, config.DecryptGoogleDriveApiKey(eppConfig), HttpContext.RequestAborted);
            history.OutputFileName = output.FileName; history.OutputFilePath = output.FilePath; history.RecordCount = output.Records; history.Status = "Hoàn thành"; history.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(); return Json(new { success = true, file = history.OutputFileName, records = history.RecordCount, historyId = history.Id, driveLookups = output.DriveLookups, driveLinksFound = output.DriveLinksFound, emailLinksFound = output.EmailLinksFound, driveError = output.DriveError });
        }
        catch (Exception ex)
        {
            history.Status = "Lỗi"; history.ErrorMessage = ex.Message; history.CompletedAt = DateTime.UtcNow; await db.SaveChangesAsync();
            return Json(new { success = false, message = ex.Message });
        }
    }

    public async Task<IActionResult> File(int id, string type)
    {
        var history = await db.DownloadHistories.FindAsync(id); var path = type == "source" ? history?.SourceFilePath : history?.OutputFilePath;
        if (history is null || string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Path.GetFileName(path));
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id)
    {
        var history = await db.DownloadHistories.FindAsync(id);
        if (history is null || string.IsNullOrWhiteSpace(history.OutputFilePath) || !System.IO.File.Exists(history.OutputFilePath)) return NotFound();
        using var workbook = new XLWorkbook(history.OutputFilePath);
        var sheet = workbook.Worksheets.First();
        var sourceTimes = new List<string>();
        if (!string.IsNullOrWhiteSpace(history.SourceFilePath) && System.IO.File.Exists(history.SourceFilePath))
        {
            using var sourceBook = new XLWorkbook(history.SourceFilePath);
            var sourceSheet = sourceBook.Worksheets.First();
            sourceTimes = sourceSheet.RowsUsed().Skip(1).Where(r => !r.CellsUsed().All(cell => cell.IsEmpty()))
                .OrderBy(r => r.Cell(4).GetString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => DateTime.TryParse(r.Cell(2).GetString(), out var d) ? d : DateTime.MaxValue)
                .Select(r => r.Cell(2).GetString()).ToList();
        }
        var headers = new[] { "STT", "Thời gian", sheet.Cell(1, 1).GetString(), sheet.Cell(1, 2).GetString(), sheet.Cell(1, 3).GetString(), sheet.Cell(1, 4).GetString(), "Nguồn link", sheet.Cell(1, 5).GetString(), "Yêu cầu khách hàng", "Yêu cầu từ Email" };
        var rows = sheet.RowsUsed().Where(row => row.RowNumber() >= 2).Select((row,index) =>
        {
            var link = row.Cell(5).GetString();
            var source = string.IsNullOrWhiteSpace(link) ? "" :
                (System.Text.RegularExpressions.Regex.IsMatch(link, "(?:drive\\.google\\.com)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "Google Drive" :
                 System.Text.RegularExpressions.Regex.IsMatch(link, "(?:dropbox\\.com|we\\.tl|wetransfer\\.com)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "Email" : "Có sẵn");
            return new[] { (index + 1).ToString(), index < sourceTimes.Count ? sourceTimes[index] : "", row.Cell(1).GetString(), row.Cell(2).GetString(), row.Cell(3).GetString(), row.Cell(4).GetString(), source, link, row.Cell(12).GetString(), row.Cell(13).GetString() };
        }).ToArray();
        return Json(new { headers, rows, totalRecords = history.RecordCount });
    }
}
