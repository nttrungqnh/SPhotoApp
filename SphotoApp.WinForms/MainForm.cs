using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using SphotoApp.Web.Models;
using SphotoApp.Web.Services;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SphotoApp.WinForms;

internal sealed class MainForm : Form
{
    private static readonly Color Primary = Color.FromArgb(55, 96, 235);
    private readonly IServiceProvider services;
    private readonly DataGridView previewGrid = CreateGrid();
    private readonly DataGridView customerGrid = CreateGrid();
    private readonly DataGridView historyGrid = CreateGrid();
    private readonly ListBox log = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9), IntegralHeight = false };
    private readonly Button runButton = PrimaryButton("TẢI DỮ LIỆU EPP");
    private readonly Button exportButton = PrimaryButton("LƯU FILE KẾT QUẢ");
    private readonly Dictionary<string, TextBox> configInputs = new();
    private readonly CheckBox sslInput = new() { Text = "Dùng SSL", Checked = true, AutoSize = true };
    private string? currentOutputPath;

    public MainForm(IServiceProvider services)
    {
        this.services = services;
        Text = "SPhotoApp Desktop"; Width = 1500; Height = 900; MinimumSize = new Size(1100, 700); StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(244, 247, 252);
        Controls.Add(BuildUi());
        Shown += async (_, _) => { await LoadConfigAsync(); await LoadCustomersAsync(); await LoadHistoryAsync(); };
    }

    private Control BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(20, 8), Font = new Font("Segoe UI Semibold", 10) };
        tabs.TabPages.Add(BuildPlanningTab());
        tabs.TabPages.Add(BuildHistoryTab());
        tabs.TabPages.Add(BuildConfigTab());
        tabs.TabPages.Add(BuildCustomersTab());
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 84, BackColor = Color.White, Padding = new Padding(24, 10, 24, 8), RowCount = 2, ColumnCount = 1 };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        header.Controls.Add(new Label { Text = "SPhotoApp Desktop", Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 17), ForeColor = Color.FromArgb(35, 45, 70) });
        header.Controls.Add(new Label { Text = "Quản lý dữ liệu Planning", Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray });
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) }; root.Controls.Add(tabs); root.Controls.Add(header); return root;
    }

    private TabPage BuildPlanningTab()
    {
        var page = NewPage("Planning");
        runButton.Width = 190; runButton.Height = 44; exportButton.Width = 190; exportButton.Height = 44; exportButton.Enabled = false;
        runButton.Click += async (_, _) => await RunPlanningAsync(); exportButton.Click += (_, _) => ExportResult();
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 125, ColumnCount = 2, Padding = new Padding(18), BackColor = Color.White };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        var titlePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        titlePanel.Controls.Add(new Label { Text = "Tải dữ liệu Planning", Font = new Font("Segoe UI Semibold", 20), AutoSize = true });
        titlePanel.Controls.Add(new Label { Text = "Đăng nhập EPP, tải Excel, lấy link Drive/Email và tạo file kết quả.", AutoSize = true, ForeColor = Color.DimGray });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 25, 0, 0) }; buttons.Controls.Add(exportButton); buttons.Controls.Add(runButton);
        top.Controls.Add(titlePanel); top.Controls.Add(buttons);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12), RowCount = 2, ColumnCount = 1 };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(log, 0, 0); content.Controls.Add(previewGrid, 0, 1);
        var container = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) }; container.Controls.Add(content); container.Controls.Add(top); page.Controls.Add(container); return page;
    }

    private TabPage BuildHistoryTab()
    {
        var page = NewPage("Lịch sử tải");
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(8), BackColor = Color.White };
        var refresh = SecondaryButton("Làm mới"); var open = SecondaryButton("Mở file kết quả"); var clear = DangerButton("Xóa lịch sử");
        refresh.Click += async (_, _) => await LoadHistoryAsync(); open.Click += (_, _) => OpenSelectedHistory(); clear.Click += async (_, _) => await ClearHistoryAsync();
        toolbar.Controls.Add(refresh); toolbar.Controls.Add(open); toolbar.Controls.Add(clear); page.Controls.Add(historyGrid); page.Controls.Add(toolbar); return page;
    }

    private TabPage BuildConfigTab()
    {
        var page = NewPage("Cấu hình kết nối");
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(24), ColumnCount = 2, BackColor = Color.White };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddField(panel, "Tên cấu hình", "Name"); AddField(panel, "Login URL", "LoginUrl"); AddField(panel, "Username / Email", "Username"); AddField(panel, "Password EPP", "Password", true);
        AddField(panel, "Connection string (tùy chọn)", "ConnectionString");
        AddField(panel, "Google Drive folder URL", "DriveFolder"); AddField(panel, "Google Drive API key", "DriveKey", true);
        AddField(panel, "Email Gmail", "GmailEmail"); AddField(panel, "Gmail App Password", "GmailPassword", true);
        AddField(panel, "IMAP host", "ImapHost"); AddField(panel, "IMAP port", "ImapPort");
        panel.Controls.Add(sslInput); panel.SetColumnSpan(sslInput, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        var save = PrimaryButton("Lưu cấu hình"); var testEpp = SecondaryButton("Kiểm tra EPP"); var testGmail = SecondaryButton("Kiểm tra Gmail");
        save.Click += async (_, _) => await SaveConfigAsync(); testEpp.Click += async (_, _) => await TestEppAsync(); testGmail.Click += async (_, _) => await TestGmailAsync();
        actions.Controls.Add(save); actions.Controls.Add(testEpp); actions.Controls.Add(testGmail); panel.Controls.Add(actions); panel.SetColumnSpan(actions, 2);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) }; scroll.Controls.Add(panel); page.Controls.Add(scroll); return page;
    }

    private TabPage BuildCustomersTab()
    {
        var page = NewPage("Khách hàng");
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(8), BackColor = Color.White };
        var add = PrimaryButton("+ Thêm khách hàng"); var edit = SecondaryButton("Sửa"); var view = SecondaryButton("Xem"); var delete = DangerButton("Xóa"); var search = new TextBox { Width = 280, PlaceholderText = "Tìm theo tên khách hàng...", Margin = new Padding(18, 6, 6, 6) };
        add.Click += async (_, _) => await EditCustomerAsync(null); edit.Click += async (_, _) => await EditSelectedCustomerAsync(false); view.Click += async (_, _) => await EditSelectedCustomerAsync(true); delete.Click += async (_, _) => await DeleteSelectedCustomerAsync(); search.TextChanged += (_, _) => FilterCustomers(search.Text);
        toolbar.Controls.Add(add); toolbar.Controls.Add(view); toolbar.Controls.Add(edit); toolbar.Controls.Add(delete); toolbar.Controls.Add(search); page.Controls.Add(customerGrid); page.Controls.Add(toolbar); return page;
    }

    private async Task RunPlanningAsync()
    {
        runButton.Enabled = false; exportButton.Enabled = false; previewGrid.Rows.Clear(); previewGrid.Columns.Clear(); log.Items.Clear(); currentOutputPath = null;
        using var scope = services.CreateScope(); var histories = scope.ServiceProvider.GetRequiredService<LocalHistoryService>(); var history = await histories.AddAsync();
        try
        {
            AddLog("Đang đọc cấu hình..."); var configService = scope.ServiceProvider.GetRequiredService<IEppConfigService>(); var cfg = await configService.GetActiveAsync() ?? throw new InvalidOperationException("Chưa có cấu hình EPP.");
            var template = await scope.ServiceProvider.GetRequiredService<ITemplateService>().GetActiveAsync() ?? throw new InvalidOperationException("Không tìm thấy Excel template trong Storage/Templates.");
            AddLog("Đang đăng nhập EPP và tải Planning..."); var download = await scope.ServiceProvider.GetRequiredService<IEppAutomationService>().DownloadPlanningAsync(cfg); if (!download.Success) throw new InvalidOperationException(download.ErrorMessage);
            history.SourceFileName = download.FileName; history.SourceFilePath = download.FilePath;
            AddLog("Đang tìm link Google Drive và Email..."); var output = await scope.ServiceProvider.GetRequiredService<IExcelProcessingService>().ProcessAsync(download.FilePath!, template, cfg.GoogleDriveFolderUrl, configService.DecryptGoogleDriveApiKey(cfg));
            history.OutputFileName = output.FileName; history.OutputFilePath = output.FilePath; history.RecordCount = output.Records; history.Status = "Hoàn thành"; history.CompletedAt = DateTime.UtcNow; await histories.SaveAsync(history);
            currentOutputPath = output.FilePath; LoadPreview(output.FilePath, download.FilePath!); exportButton.Enabled = true;
            AddLog($"Hoàn thành: {output.Records} job; Drive {output.DriveLinksFound}/{output.DriveLookups}; Email {output.EmailLinksFound}."); if (!string.IsNullOrWhiteSpace(output.DriveError)) AddLog("Google Drive: " + output.DriveError);
        }
        catch (Exception ex) { history.Status = "Lỗi"; history.ErrorMessage = ex.Message; history.CompletedAt = DateTime.UtcNow; await histories.SaveAsync(history); AddLog("LỖI: " + ex.Message); MessageBox.Show(ex.Message, "Không thể tải dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { runButton.Enabled = true; await LoadHistoryAsync(); }
    }

    private void LoadPreview(string outputPath, string sourcePath)
    {
        using var output = new XLWorkbook(outputPath); var sheet = output.Worksheets.First(); var times = new List<string>();
        if (File.Exists(sourcePath)) { using var source = new XLWorkbook(sourcePath); times = source.Worksheets.First().RowsUsed().Skip(1).Where(r => !r.CellsUsed().All(c => c.IsEmpty())).OrderBy(r => r.Cell(4).GetString(), StringComparer.OrdinalIgnoreCase).ThenBy(r => DateTime.TryParse(r.Cell(2).GetString(), out var d) ? d : DateTime.MaxValue).Select(r => r.Cell(2).GetString()).ToList(); }
        var headers = new[] { "STT", "Thời gian", "Tên job", "Client", "Link Download", "Upload", "Nguồn link", "Link Upload", "Yêu cầu khách hàng", "Yêu cầu từ Email" }; foreach (var h in headers) previewGrid.Columns.Add(h, h);
        var rows = sheet.RowsUsed().Where(r => r.RowNumber() >= 2).ToList(); for (var i = 0; i < rows.Count; i++) { var r = rows[i]; var link = r.Cell(3).GetString(); var source = string.IsNullOrWhiteSpace(link) ? "" : Regex.IsMatch(link, "drive\\.google\\.com", RegexOptions.IgnoreCase) ? "Google Drive" : Regex.IsMatch(link, "dropbox\\.com|we\\.tl|wetransfer\\.com", RegexOptions.IgnoreCase) ? "Email" : "Có sẵn"; previewGrid.Rows.Add(i + 1, i < times.Count ? times[i] : "", r.Cell(1).GetString(), r.Cell(2).GetString(), r.Cell(3).GetString(), r.Cell(4).GetString(), source, "", r.Cell(12).GetString(), r.Cell(13).GetString()); }
        previewGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells); previewGrid.Columns[2].Width = 360; previewGrid.Columns[8].Width = 300; previewGrid.Columns[9].Width = 300;
    }

    private async Task LoadConfigAsync()
    {
        using var scope = services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IEppConfigService>(); Set("ConnectionString", await scope.ServiceProvider.GetRequiredService<DesktopConnectionService>().GetAsync()); var c = await service.GetActiveAsync(); if (c is null) return;
        Set("Name", c.Name); Set("LoginUrl", c.LoginUrl); Set("Username", c.Username); Set("DriveFolder", c.GoogleDriveFolderUrl); Set("GmailEmail", c.GmailEmail); Set("ImapHost", c.GmailImapHost); Set("ImapPort", c.GmailImapPort.ToString()); sslInput.Checked = c.GmailUseSsl;
        try { Set("Password", service.Decrypt(c)); } catch { Set("Password", ""); } Set("DriveKey", service.DecryptGoogleDriveApiKey(c)); Set("GmailPassword", service.DecryptGmailAppPassword(c));
    }

    private async Task SaveConfigAsync()
    {
        using var scope = services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IEppConfigService>(); var old = await service.GetActiveAsync();
        var c = new EppConfig { Id = old?.Id ?? 0, Name = Get("Name"), LoginUrl = Get("LoginUrl"), Username = Get("Username"), GoogleDriveFolderUrl = Get("DriveFolder"), GmailEmail = Get("GmailEmail"), GmailImapHost = string.IsNullOrWhiteSpace(Get("ImapHost")) ? "imap.gmail.com" : Get("ImapHost"), GmailImapPort = int.TryParse(Get("ImapPort"), out var port) ? port : 993, GmailUseSsl = sslInput.Checked, IsActive = true };
        await service.SaveAsync(c, Get("Password"), Get("DriveKey"), Get("GmailPassword")); if (!string.IsNullOrWhiteSpace(Get("ConnectionString"))) await scope.ServiceProvider.GetRequiredService<DesktopConnectionService>().SaveAsync(Get("ConnectionString")); MessageBox.Show("Đã lưu cấu hình.", "SPhotoApp", MessageBoxButtons.OK, MessageBoxIcon.Information); await LoadConfigAsync();
    }

    private async Task TestEppAsync() { using var scope = services.CreateScope(); var cfg = await scope.ServiceProvider.GetRequiredService<IEppConfigService>().GetActiveAsync(); if (cfg is null) { MessageBox.Show("Chưa có cấu hình EPP."); return; } Cursor = Cursors.WaitCursor; var r = await scope.ServiceProvider.GetRequiredService<IEppAutomationService>().TestLoginAsync(cfg); Cursor = Cursors.Default; MessageBox.Show(r.Message, r.Success ? "Thành công" : "Lỗi", MessageBoxButtons.OK, r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error); }
    private async Task TestGmailAsync() { using var scope = services.CreateScope(); var cfg = await scope.ServiceProvider.GetRequiredService<IEppConfigService>().GetActiveAsync(); if (cfg is null) { MessageBox.Show("Chưa có cấu hình Gmail."); return; } var r = await scope.ServiceProvider.GetRequiredService<IGmailConnectionService>().TestAsync(cfg); MessageBox.Show(r.Message, r.Success ? "Thành công" : "Lỗi", MessageBoxButtons.OK, r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error); }

    private async Task LoadCustomersAsync()
    {
        using var scope = services.CreateScope(); var data = await scope.ServiceProvider.GetRequiredService<ICustomerService>().ListAsync(); customerGrid.Tag = data; customerGrid.Columns.Clear(); customerGrid.Rows.Clear();
        customerGrid.Columns.Add("Id", "Id"); customerGrid.Columns[0].Visible = false; customerGrid.Columns.Add("Stt", "STT"); customerGrid.Columns.Add("Name", "Tên khách hàng"); customerGrid.Columns.Add("Requirement", "Yêu cầu"); customerGrid.Columns.Add("Note", "Ghi chú"); var i = 1; foreach (var c in data) customerGrid.Rows.Add(c.Id, i++, c.Name, c.Requirement, c.Note); customerGrid.Columns[2].Width = 240; customerGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; customerGrid.Columns[4].Width = 300;
    }

    private void FilterCustomers(string text) { foreach (DataGridViewRow row in customerGrid.Rows) row.Visible = string.IsNullOrWhiteSpace(text) || Convert.ToString(row.Cells[2].Value)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true; }
    private int? SelectedCustomerId() => customerGrid.CurrentRow is null ? null : Convert.ToInt32(customerGrid.CurrentRow.Cells[0].Value);
    private async Task EditSelectedCustomerAsync(bool readOnly) { var id = SelectedCustomerId(); if (id is null) return; using var scope = services.CreateScope(); var c = (await scope.ServiceProvider.GetRequiredService<ICustomerService>().ListAsync()).FirstOrDefault(x => x.Id == id.Value); if (c is not null) await EditCustomerAsync(c, readOnly); }
    private async Task EditCustomerAsync(Customer? source, bool readOnly = false) { using var dialog = new CustomerDialog(source, readOnly); if (!readOnly && dialog.ShowDialog(this) == DialogResult.OK) { using var scope = services.CreateScope(); try { await scope.ServiceProvider.GetRequiredService<ICustomerService>().SaveAsync(dialog.Customer); await LoadCustomersAsync(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Không thể lưu", MessageBoxButtons.OK, MessageBoxIcon.Warning); } } else if (readOnly) dialog.ShowDialog(this); }
    private async Task DeleteSelectedCustomerAsync() { var id = SelectedCustomerId(); if (id is null || MessageBox.Show("Xóa khách hàng đã chọn?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return; using var scope = services.CreateScope(); await scope.ServiceProvider.GetRequiredService<ICustomerService>().DeleteAsync(id.Value); await LoadCustomersAsync(); }

    private async Task LoadHistoryAsync() { using var scope = services.CreateScope(); var list = (await scope.ServiceProvider.GetRequiredService<LocalHistoryService>().ListAsync()).Take(200).ToList(); historyGrid.Columns.Clear(); historyGrid.Rows.Clear(); historyGrid.Columns.Add("Id", "Id"); historyGrid.Columns[0].Visible = false; historyGrid.Columns.Add("Time", "Thời gian"); historyGrid.Columns.Add("Source", "File nguồn"); historyGrid.Columns.Add("Output", "File kết quả"); historyGrid.Columns.Add("Records", "Số dòng"); historyGrid.Columns.Add("Status", "Trạng thái"); historyGrid.Columns.Add("Error", "Lỗi"); foreach (var h in list) historyGrid.Rows.Add(h.Id, h.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), h.SourceFileName, h.OutputFileName, h.RecordCount, h.Status, h.ErrorMessage); historyGrid.Columns[6].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; }
    private async void OpenSelectedHistory() { if (historyGrid.CurrentRow is null) return; var id = Convert.ToInt32(historyGrid.CurrentRow.Cells[0].Value); using var scope = services.CreateScope(); var item = await scope.ServiceProvider.GetRequiredService<LocalHistoryService>().FindAsync(id); if (!string.IsNullOrWhiteSpace(item?.OutputFilePath) && File.Exists(item.OutputFilePath)) Process.Start(new ProcessStartInfo(item.OutputFilePath) { UseShellExecute = true }); else MessageBox.Show("File kết quả không còn tồn tại trên máy này."); }
    private async Task ClearHistoryAsync() { if (MessageBox.Show("Xóa toàn bộ lịch sử tải?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; using var scope = services.CreateScope(); await scope.ServiceProvider.GetRequiredService<LocalHistoryService>().ClearAsync(); await LoadHistoryAsync(); }
    private void ExportResult() { if (string.IsNullOrWhiteSpace(currentOutputPath) || !File.Exists(currentOutputPath)) return; using var dialog = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = Path.GetFileName(currentOutputPath) }; if (dialog.ShowDialog(this) == DialogResult.OK) File.Copy(currentOutputPath, dialog.FileName, true); }
    private void AddLog(string value) { log.Items.Add($"{DateTime.Now:HH:mm:ss}  {value}"); log.TopIndex = log.Items.Count - 1; }

    private void AddField(TableLayoutPanel panel, string label, string key, bool password = false) { var wrapper = new Panel { Height = 78, Dock = DockStyle.Top, Padding = new Padding(6) }; var caption = new Label { Text = label, Dock = DockStyle.Top, Height = 25 }; var input = new TextBox { Dock = DockStyle.Top, Height = 34, UseSystemPasswordChar = password }; wrapper.Controls.Add(input); wrapper.Controls.Add(caption); configInputs[key] = input; panel.Controls.Add(wrapper); }
    private string Get(string key) => configInputs[key].Text.Trim(); private void Set(string key, string? value) => configInputs[key].Text = value ?? "";
    private static TabPage NewPage(string text) => new(text) { BackColor = Color.FromArgb(244, 247, 252), Padding = new Padding(8) };
    private static DataGridView CreateGrid() => new() { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing, ColumnHeadersHeight = 42, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single, RowTemplate = { MinimumHeight = 36 }, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True, Padding = new Padding(6, 5, 6, 5), SelectionBackColor = Color.FromArgb(225, 233, 255), SelectionForeColor = Color.Black }, ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Segoe UI Semibold", 9.5F), BackColor = Color.FromArgb(245, 247, 252), Padding = new Padding(6, 5, 6, 5), WrapMode = DataGridViewTriState.False, Alignment = DataGridViewContentAlignment.MiddleLeft }, EnableHeadersVisualStyles = false };
    private static Button PrimaryButton(string text) => new() { Text = text, AutoSize = true, Height = 36, Padding = new Padding(12, 0, 12, 0), BackColor = Primary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(6) };
    private static Button SecondaryButton(string text) => new() { Text = text, AutoSize = true, Height = 36, Padding = new Padding(10, 0, 10, 0), BackColor = Color.White, ForeColor = Color.FromArgb(40, 55, 85), FlatStyle = FlatStyle.Flat, Margin = new Padding(6) };
    private static Button DangerButton(string text) { var b = SecondaryButton(text); b.ForeColor = Color.Firebrick; return b; }
}

internal sealed class CustomerDialog : Form
{
    private readonly TextBox name = new() { Dock = DockStyle.Top };
    private readonly TextBox requirement = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox note = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly int id;
    public Customer Customer => new() { Id = id, Name = name.Text.Trim(), Requirement = requirement.Text.Trim(), Note = note.Text.Trim() };
    public CustomerDialog(Customer? customer, bool readOnly)
    {
        id = customer?.Id ?? 0; Text = readOnly ? "Xem khách hàng" : customer is null ? "Thêm khách hàng" : "Sửa khách hàng"; Width = 720; Height = 550; StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
        name.Text = customer?.Name ?? ""; requirement.Text = customer?.Requirement ?? ""; note.Text = customer?.Note ?? ""; name.ReadOnly = requirement.ReadOnly = note.ReadOnly = readOnly;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 6 }; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(new Label { Text = "Tên khách hàng", AutoSize = true }); layout.Controls.Add(name); layout.Controls.Add(new Label { Text = "Yêu cầu", AutoSize = true }); layout.Controls.Add(requirement); layout.Controls.Add(new Label { Text = "Ghi chú", AutoSize = true }); layout.Controls.Add(note);
        if (!readOnly) { var save = new Button { Text = "Lưu", Dock = DockStyle.Bottom, Height = 42, BackColor = Color.FromArgb(55, 96, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show("Vui lòng nhập tên khách hàng."); return; } DialogResult = DialogResult.OK; Close(); }; Controls.Add(save); }
        Controls.Add(layout);
    }
}
