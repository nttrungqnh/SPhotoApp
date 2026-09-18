using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SphotoApp.Web.Models;
using SphotoApp.Web.Services;
namespace SphotoApp.Web.Controllers;
[Authorize(Roles="Admin")]
public class CustomerController(ICustomerService service):Controller
{
    public async Task<IActionResult> Index()=>View(await service.ListAsync());
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Save(Customer customer){if(!ModelState.IsValid)return RedirectToAction(nameof(Index));try{await service.SaveAsync(customer);}catch(InvalidOperationException ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index));}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Delete(int id){await service.DeleteAsync(id);return RedirectToAction(nameof(Index));}
}
