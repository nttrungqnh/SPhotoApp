using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SphotoApp.Web.Data;
using SphotoApp.Web.Models;
using SphotoApp.Web.ViewModels;

namespace SphotoApp.Web.Controllers;

[Authorize(Roles = "Admin")]
public class MappingController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(int? templateId, int? sourceHistoryId)
    {
        var templates = await db.ExcelTemplates.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync();
        var sources = await db.DownloadHistories.Where(x => x.SourceFilePath != null && x.Status == "Hoàn thành").OrderByDescending(x => x.StartedAt).ToListAsync();
        var template = templates.FirstOrDefault(x => x.Id == templateId) ?? templates.FirstOrDefault(x => x.IsActive);
        var model = new MappingPageVm { TemplateId = template?.Id, SourceHistoryId = sourceHistoryId, Templates = templates, SourceFiles = sources };
        if (template is null) return View(model);
        model.Mappings = await db.ExcelMappings.Where(x => x.TemplateId == template.Id).OrderBy(x => x.Order).ToListAsync();
        model.TargetColumns = ReadTemplateColumns(template);
        var source = sources.FirstOrDefault(x => x.Id == sourceHistoryId);
        if (source?.SourceFilePath is not null && System.IO.File.Exists(source.SourceFilePath)) model.SourceHeaders = ReadHeaders(source.SourceFilePath);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ExcelMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.SourceHeader) || string.IsNullOrWhiteSpace(mapping.TargetColumn))
        {
            TempData["Error"] = "Hãy chọn cả cột nguồn và cột Template.";
            return RedirectToAction(nameof(Index), new { templateId = mapping.TemplateId });
        }
        mapping.DataType ??= "Text"; mapping.IsActive = true;
        if (mapping.Id == 0) { mapping.Order = await db.ExcelMappings.CountAsync(x => x.TemplateId == mapping.TemplateId) + 1; db.Add(mapping); }
        else db.Update(mapping);
        await db.SaveChangesAsync();
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { success = true, mapping = new { mapping.Id, mapping.SourceHeader, mapping.TargetColumn, mapping.DataType, mapping.TemplateId } });
        return RedirectToAction(nameof(Index), new { templateId = mapping.TemplateId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int templateId)
    {
        var mapping = await db.ExcelMappings.FindAsync(id);
        if (mapping is not null) { db.Remove(mapping); await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { templateId });
    }

    static List<string> ReadHeaders(string path)
    {
        using var workbook = new XLWorkbook(path);
        return workbook.Worksheets.First().Row(1).CellsUsed().Select(x => x.GetString().Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static List<ColumnOptionVm> ReadTemplateColumns(ExcelTemplate template)
    {
        using var workbook = new XLWorkbook(template.FilePath);
        var sheet = string.IsNullOrWhiteSpace(template.TargetSheetName) ? workbook.Worksheets.First() : workbook.Worksheet(template.TargetSheetName);
        var headerRow = Math.Max(1, template.TargetStartRow - 1);
        return sheet.Row(headerRow).CellsUsed().Select(x => new ColumnOptionVm { Column = x.Address.ColumnLetter, DisplayName = $"{x.Address.ColumnLetter} — {x.GetString().Trim()}" }).ToList();
    }
}
