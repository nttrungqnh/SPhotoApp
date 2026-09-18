using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SphotoApp.Web.Models;
namespace SphotoApp.Web.Data;
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider s)
    {
        var db=s.GetRequiredService<AppDbContext>(); await db.Database.MigrateAsync(); await EnsureEppColumnsAsync(db);
        var roles=s.GetRequiredService<RoleManager<IdentityRole>>(); foreach(var r in new[]{"Admin","Manager","Employee"}) if(!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));
        var users=s.GetRequiredService<UserManager<ApplicationUser>>(); if(await users.FindByNameAsync("admin") is null){var u=new ApplicationUser{UserName="admin",Email="admin@sphoto.local",FullName="System Administrator",EmailConfirmed=true}; await users.CreateAsync(u,"Admin@123"); await users.AddToRoleAsync(u,"Admin");}
    }
    static async Task EnsureEppColumnsAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID('dbo.Customers','U') IS NULL CREATE TABLE dbo.Customers (Id int IDENTITY(1,1) NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL, Requirement nvarchar(max) NULL, Note nvarchar(max) NULL, UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()); ELSE IF COL_LENGTH('dbo.Customers','Note') IS NULL ALTER TABLE dbo.Customers ADD Note nvarchar(max) NULL;");
        foreach(var sql in new[]{"IF COL_LENGTH('dbo.EppConfigs','GmailEmail') IS NULL ALTER TABLE dbo.EppConfigs ADD GmailEmail nvarchar(max) NULL;","IF COL_LENGTH('dbo.EppConfigs','GmailImapHost') IS NULL ALTER TABLE dbo.EppConfigs ADD GmailImapHost nvarchar(max) NULL;","IF COL_LENGTH('dbo.EppConfigs','GmailImapPort') IS NULL ALTER TABLE dbo.EppConfigs ADD GmailImapPort int NOT NULL CONSTRAINT DF_EppConfigs_GmailImapPort DEFAULT 993;","IF COL_LENGTH('dbo.EppConfigs','GmailUseSsl') IS NULL ALTER TABLE dbo.EppConfigs ADD GmailUseSsl bit NOT NULL CONSTRAINT DF_EppConfigs_GmailUseSsl DEFAULT 1;","IF COL_LENGTH('dbo.EppConfigs','EncryptedGmailAppPassword') IS NULL ALTER TABLE dbo.EppConfigs ADD EncryptedGmailAppPassword nvarchar(max) NULL;","UPDATE dbo.EppConfigs SET GmailImapHost='imap.gmail.com' WHERE GmailImapHost IS NULL;","UPDATE dbo.EppConfigs SET GmailImapPort=993 WHERE GmailImapPort IS NULL;","UPDATE dbo.EppConfigs SET GmailUseSsl=1 WHERE GmailUseSsl IS NULL;"}) await db.Database.ExecuteSqlRawAsync(sql);
    }
}
