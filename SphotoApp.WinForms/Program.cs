using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SphotoApp.Web.Options;
using SphotoApp.Web.Services;

namespace SphotoApp.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();


        try
        {
            var services = BuildServices();
            using var provider = services.BuildServiceProvider();
            PrepareStorage(provider);
            Application.Run(new MainForm(provider));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể khởi động SPhotoApp.\n\n{ex.Message}", "Lỗi khởi động", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static ServiceCollection BuildServices()
    {
        var root = AppContext.BaseDirectory;
        var keyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SphotoApp", "Keys");
        Directory.CreateDirectory(keyFolder);
        Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", root);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<IWebHostEnvironment>(new DesktopEnvironment(root));
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyFolder)).SetApplicationName("SphotoApp.Desktop");
        services.Configure<StorageOptions>(o => { o.Root = "Storage"; o.DownloadFolder = "Downloads"; o.TemplateFolder = "Templates"; o.OutputFolder = "Outputs"; o.ErrorFolder = "Errors"; });
        services.Configure<EppAutomationOptions>(o => { o.Headless = true; o.Timeout = 60000; });
        services.Configure<GoogleDriveOptions>(_ => { });
        services.AddSingleton<IEppConfigService, DesktopConfigService>();
        services.AddSingleton<DesktopConnectionService>();
        services.AddSingleton<ICustomerService, DesktopCustomerService>();
        services.AddSingleton<ITemplateService, DesktopTemplateService>();
        services.AddSingleton<LocalHistoryService>();
        services.AddScoped<IExcelProcessingService, ExcelProcessingService>();
        services.AddScoped<IEppAutomationService, EppAutomationService>();
        services.AddScoped<IGmailConnectionService, GmailConnectionService>();
        services.AddHttpClient<IGoogleDriveService, GoogleDriveService>();
        services.AddHttpClient<ILinkFileCountService, LinkFileCountService>();
        return services;
    }

    private static void PrepareStorage(IServiceProvider provider)
    {
        var env = provider.GetRequiredService<IWebHostEnvironment>();
        foreach (var folder in new[] { "Downloads", "Templates", "Outputs", "Errors" })
            Directory.CreateDirectory(Path.Combine(env.ContentRootPath, "Storage", folder));
    }
}
