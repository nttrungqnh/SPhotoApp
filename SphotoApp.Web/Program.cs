using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SphotoApp.Web.Data;
using SphotoApp.Web.Models;
using SphotoApp.Web.Options;
using SphotoApp.Web.Services;
var builder = WebApplication.CreateBuilder(args);
// The Google Drive key is deliberately kept in local User Secrets. Load it for local
// executable/IIS runs as well, not only when ASPNETCORE_ENVIRONMENT is Development.
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage")); builder.Services.Configure<EppAutomationOptions>(builder.Configuration.GetSection("EppAutomation")); builder.Services.Configure<GoogleDriveOptions>(builder.Configuration.GetSection("GoogleDrive"));
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => o.LoginPath = "/Account/Login"); builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IEppConfigService, EppConfigService>(); builder.Services.AddScoped<ICustomerService, CustomerService>(); builder.Services.AddScoped<ITemplateService, TemplateService>(); builder.Services.AddScoped<IExcelProcessingService, ExcelProcessingService>(); builder.Services.AddScoped<IEppAutomationService, EppAutomationService>(); builder.Services.AddScoped<IGmailConnectionService, GmailConnectionService>(); builder.Services.AddHttpClient<IGoogleDriveService, GoogleDriveService>();
var app = builder.Build(); var storage = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
foreach (var f in new[] { storage.DownloadFolder, storage.TemplateFolder, storage.OutputFolder, storage.ErrorFolder }) Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, storage.Root, f));
using (var scope = app.Services.CreateScope()) await DbInitializer.InitializeAsync(scope.ServiceProvider);
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Home/Error"); app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}"); app.Run();
