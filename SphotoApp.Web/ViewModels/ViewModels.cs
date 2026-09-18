using System.ComponentModel.DataAnnotations; using SphotoApp.Web.Models;
namespace SphotoApp.Web.ViewModels;
public class LoginVm { [Required] public string Username {get;set;}=""; [Required,DataType(DataType.Password)] public string Password {get;set;}=""; public bool RememberMe {get;set;} }
public class DashboardVm { public DownloadHistory? Latest {get;set;} public int TodayCount {get;set;} public int TotalCount {get;set;} public int CustomerCount {get;set;} public List<DownloadHistory> Recent {get;set;}=new(); }
public class TemplateUploadVm { [Required] public string Name {get;set;}=""; [Required] public IFormFile? File {get;set;} public string? TargetSheetName {get;set;} public int TargetStartRow {get;set;}=2; }
public class MappingPageVm { public int? TemplateId {get;set;} public int? SourceHistoryId {get;set;} public List<ExcelTemplate> Templates {get;set;}=new(); public List<DownloadHistory> SourceFiles {get;set;}=new(); public List<string> SourceHeaders {get;set;}=new(); public List<ColumnOptionVm> TargetColumns {get;set;}=new(); public List<ExcelMapping> Mappings {get;set;}=new(); }
public class ColumnOptionVm { public string Column {get;set;}=""; public string DisplayName {get;set;}=""; }
