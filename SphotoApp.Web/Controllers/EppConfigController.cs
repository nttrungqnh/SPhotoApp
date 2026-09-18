using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SphotoApp.Web.Models;
using SphotoApp.Web.Services;

namespace SphotoApp.Web.Controllers;

[Authorize(Roles = "Admin")]
public class EppConfigController(IEppConfigService service, IEppAutomationService epp, IGmailConnectionService gmail) : Controller
{
    public async Task<IActionResult> Index()
    {
        var config = await service.GetActiveAsync();
        ViewBag.CurrentPassword = string.Empty;
        ViewBag.CurrentGoogleDriveApiKey = string.Empty;
        ViewBag.CurrentGmailAppPassword = string.Empty;
        if (config is not null)
        {
            try { ViewBag.CurrentPassword = service.Decrypt(config); }
            catch { ViewBag.PasswordWarning = "Không thể hiển thị mật khẩu đã lưu. Hãy nhập lại mật khẩu khi cần cập nhật cấu hình."; }
            ViewBag.CurrentGoogleDriveApiKey = service.DecryptGoogleDriveApiKey(config) ?? string.Empty;
            ViewBag.CurrentGmailAppPassword = service.DecryptGmailAppPassword(config) ?? string.Empty;
        }
        return View(config ?? new EppConfig());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EppConfig config, string? password, string? googleDriveApiKey, string? gmailAppPassword)
    {
        // This value is stored encrypted and is intentionally not posted back by the form.
        ModelState.Remove(nameof(EppConfig.EncryptedPassword));
        ModelState.Remove(nameof(EppConfig.EncryptedGoogleDriveApiKey));
        ModelState.Remove(nameof(EppConfig.EncryptedGmailAppPassword));
        if (config.Id == 0 && string.IsNullOrWhiteSpace(password))
            ModelState.AddModelError("password", "Vui lòng nhập mật khẩu EPP.");
        if (!ModelState.IsValid)
        {
            ViewBag.CurrentPassword = password ?? string.Empty;
            ViewBag.CurrentGoogleDriveApiKey = googleDriveApiKey ?? string.Empty;
            ViewBag.CurrentGmailAppPassword = gmailAppPassword ?? string.Empty;
            return View("Index", config);
        }

        await service.SaveAsync(config, password, googleDriveApiKey, gmailAppPassword);
        TempData["Success"] = "Đã lưu cấu hình EPP.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Test()
    {
        var config = await service.GetActiveAsync();
        if (config is null) return Json(new { success = false, message = "Chưa có cấu hình EPP." });
        var result = await epp.TestLoginAsync(config);
        return Json(new { success = result.Success, message = result.Message });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestGmail()
    {
        var config = await service.GetActiveAsync();
        if (config is null) return Json(new { success = false, message = "Chưa có cấu hình EPP/Gmail." });
        var result = await gmail.TestAsync(config, HttpContext.RequestAborted);
        return Json(new { success = result.Success, message = result.Message });
    }

}
