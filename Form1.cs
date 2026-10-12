using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace dnspod
{
    public partial class Form1 : Form
    {
        private TabControl providers; private ComboBox domains; private ComboBox types; private DataGridView records;
        private TextBox id; private TextBox secret; private Button login; private Label status; private CloudProvider current;
        private GroupBox account; private Label loggedAccount; private Button logout;
        private CloudCredential activeCredential; private IList<DnsDomain> loadedRecordsDomains; private IList<DnsRecord> loadedRecords;
        private const int PageSize = 100; private int pageIndex; private Label pageInfo; private Button previousPage, nextPage;
        public Form1() { InitializeComponent(); Build(); }
        private void Build()
        {
            Text = "云 DNS 管理"; Width = 800; Height = 620; MinimumSize = new Size(760, 480); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            providers = new TabControl { Dock = DockStyle.Top, Height = 38, Padding = new Point(14, 5) }; providers.TabPages.Add("阿里云"); providers.TabPages.Add("腾讯云"); providers.SelectedIndexChanged += delegate { current = (CloudProvider)providers.SelectedIndex; ShowProvider(); };
            account = new GroupBox { Text = "账号登录与验证", Dock = DockStyle.Top, Height = 138, Padding = new Padding(12, 20, 12, 8) };
            id = new TextBox { Left = 86, Top = 26, Width = 500, Height = 28 }; secret = new TextBox { Left = 86, Top = 64, Width = 500, Height = 28, PasswordChar = '*' };
            login = new Button { Left = 608, Top = 26, Width = 128, Height = 34, Text = "登录并验证", UseVisualStyleBackColor = true }; login.Click += LoginClick;
            status = new Label { Left = 86, Top = 103, AutoSize = true, ForeColor = Color.DimGray };
            account.Controls.AddRange(new Control[] { new Label { Left = 16, Top = 30, AutoSize = true, Text = "ID" }, id, new Label { Left = 16, Top = 68, AutoSize = true, Text = "Secret" }, secret, login, status });
            GroupBox operation = new GroupBox { Text = "DNS 解析管理", Dock = DockStyle.Top, Height = 116, Padding = new Padding(12, 20, 12, 8) };
            domains = new ComboBox { Left = 12, Top = 25, Width = 285, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList }; domains.SelectedIndexChanged += DomainChanged;
            types = new ComboBox { Left = 313, Top = 25, Width = 150, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList }; types.Items.Add("全部类型"); types.SelectedIndex = 0; types.SelectedIndexChanged += delegate { pageIndex = 0; RenderRecords(); };
            Button add = new Button { Left = 12, Top = 65, Width = 112, Height = 34, Text = "＋ 添加解析", UseVisualStyleBackColor = true }; add.Click += AddClick;
            Button del = new Button { Left = 136, Top = 65, Width = 112, Height = 34, Text = "× 删除勾选", UseVisualStyleBackColor = true }; del.Click += DeleteClick;
            loggedAccount = new Label { Left = 16, Top = 32, AutoSize = true, ForeColor = Color.FromArgb(45, 55, 72), Visible = false };
            logout = new Button { Top = 24, Width = 140, Height = 34, Text = "退出当前用户", UseVisualStyleBackColor = true, Visible = false }; logout.Click += LogoutClick;
            account.Controls.AddRange(new Control[] { loggedAccount, logout });
            operation.Controls.AddRange(new Control[] { domains, types, add, del });
            records = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false, EnableHeadersVisualStyles = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, GridColor = Color.FromArgb(232, 237, 244), ColumnHeadersHeight = 42, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };
            records.DefaultCellStyle = new DataGridViewCellStyle { Font = Font, ForeColor = Color.FromArgb(45, 55, 72), BackColor = Color.White, SelectionBackColor = Color.FromArgb(225, 238, 255), SelectionForeColor = Color.FromArgb(30, 64, 120), Padding = new Padding(10, 0, 10, 0) };
            records.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font(Font, FontStyle.Bold), BackColor = Color.FromArgb(238, 243, 250), ForeColor = Color.FromArgb(66, 82, 110), SelectionBackColor = Color.FromArgb(238, 243, 250), Padding = new Padding(10, 0, 10, 0) };
            records.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            records.RowTemplate.Height = 40;
            records.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Checked", HeaderText = "勾选", Width = 64, Resizable = DataGridViewTriState.False, FlatStyle = FlatStyle.Flat });
            string[] names = { "Host", "Type", "Value", "Line", "Ttl" };
            string[] labels = { "主机记录", "记录类型", "记录值", "线路", "TTL（秒）" };
            int[] widths = { 120, 80, 200, 80, 90 };
            for (int i = 0; i < names.Length; i++) records.Columns.Add(new DataGridViewTextBoxColumn { Name = names[i], HeaderText = labels[i], Width = widths[i], ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic });
            records.Columns["Value"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            records.Columns["Value"].MinimumWidth = 120;
            records.Columns.Add(new DataGridViewButtonColumn { Name = "Edit", HeaderText = "操作", Text = "编辑", UseColumnTextForButtonValue = true, Width = 62, ReadOnly = true });
            records.Columns.Add(new DataGridViewButtonColumn { Name = "Delete", HeaderText = "", Text = "删除", UseColumnTextForButtonValue = true, Width = 62, ReadOnly = true });
            records.CellContentClick += RecordActionClick;
            records.CurrentCellDirtyStateChanged += delegate { if (records.IsCurrentCellDirty) records.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            records.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.ColumnIndex >= 0 && !(records.Columns[e.ColumnIndex] is DataGridViewButtonColumn)) EditRecord(sender, e); };
            Panel pagination = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            previousPage = new Button { Left = 12, Top = 7, Width = 86, Height = 30, Text = "上一页" };
            nextPage = new Button { Left = 108, Top = 7, Width = 86, Height = 30, Text = "下一页" };
            pageInfo = new Label { Left = 210, Top = 13, AutoSize = true };
            previousPage.Click += delegate { if (pageIndex > 0) { pageIndex--; RenderRecords(); } };
            nextPage.Click += delegate { pageIndex++; RenderRecords(); };
            pagination.Controls.AddRange(new Control[] { previousPage, nextPage, pageInfo });
            Controls.Add(records); Controls.Add(pagination); Controls.Add(operation); Controls.Add(account); Controls.Add(providers); current = CloudProvider.Aliyun; ShowProvider();
        }
        private void ShowAccount(CloudCredential credential)
        {
            bool loggedIn = credential != null;
            account.SuspendLayout();
            foreach (Control control in account.Controls) control.Visible = !loggedIn;
            loggedAccount.Visible = loggedIn; logout.Visible = loggedIn;
            account.Height = loggedIn ? 76 : 138;
            if (loggedIn)
            {
                loggedAccount.Text = "已登录：" + CredentialStore.Mask(credential.Id);
                logout.Left = loggedAccount.Right + 24;
                id.Clear(); secret.Clear();
            }
            account.ResumeLayout(true);
        }
        private CloudCredential Read() { return CredentialStore.Read(current); }
        private void ShowProvider() { CloudCredential c = Read(); activeCredential = null; if (status == null || id == null || secret == null || domains == null || records == null) return; ShowAccount(null); status.Text = c == null ? "未登录" : "已登录：" + CredentialStore.Mask(c.Id); id.Text = c == null ? "" : c.Id; secret.Text = ""; domains.Items.Clear(); records.Rows.Clear(); loadedRecords = null; pageIndex = 0; RenderRecords(); if (c != null) try { LoadDomains(c); ShowAccount(c); } catch (Exception ex) { status.Text = "凭据失效"; MessageBox.Show("已保存的账号无法读取域名，请重新登录。\n\n" + ex.Message, "加载域名失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        private void LoginClick(object sender, EventArgs e) { try { if (String.IsNullOrEmpty(id.Text.Trim()) || String.IsNullOrEmpty(secret.Text)) throw new InvalidOperationException("请输入 ID 和 Secret。"); login.Enabled = false; status.Text = "正在验证账号..."; CloudCredential c = new CloudCredential { Provider = current, Id = id.Text.Trim(), Secret = secret.Text }; LoadDomains(c); CredentialStore.Write(c); ShowAccount(c); status.Text = "已登录：" + CredentialStore.Mask(c.Id); MessageBox.Show("账号验证成功，已加载域名。\n请选择域名后查看解析记录。", "登录成功", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch (Exception ex) { status.Text = "登录失败"; MessageBox.Show("云 API 调用失败。\n\n" + ex.Message, "登录失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } finally { login.Enabled = true; } }
        private void LoadDomains(CloudCredential c) { loadedRecordsDomains = DnsProviders.Domains(c); activeCredential = c; domains.Items.Clear(); foreach (DnsDomain d in loadedRecordsDomains) domains.Items.Add(d); if (domains.Items.Count > 0) { domains.SelectedIndex = 0; status.Text = "已登录：" + CredentialStore.Mask(c.Id) + "，域名数：" + domains.Items.Count; } else { status.Text = "已登录，但没有可管理的域名"; MessageBox.Show("账号验证成功，但没有读取到可管理的域名。请检查账号权限和域名归属。", "域名为空", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        private void DomainChanged(object sender, EventArgs e) { loadedRecords = null; pageIndex = 0; RenderRecords(); if (domains.SelectedItem == null) return; try { DnsDomain domain = (DnsDomain)domains.SelectedItem; status.Text = "正在加载解析：" + domain.Name; loadedRecords = DnsProviders.Records(activeCredential, domain); types.Items.Clear(); types.Items.Add("全部类型"); foreach (DnsRecord r in loadedRecords) if (!types.Items.Contains(r.Type)) types.Items.Add(r.Type); types.SelectedIndex = 0; RenderRecords(); status.Text = "域名：" + domain.Name + "，解析记录数：" + loadedRecords.Count; } catch (Exception ex) { status.Text = "解析加载失败"; MessageBox.Show("读取域名解析记录失败。\n\n" + ex.Message, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void RenderRecords()
        {
            records.Rows.Clear();
            string filter = types.SelectedItem == null ? "全部类型" : Convert.ToString(types.SelectedItem);
            List<DnsRecord> sorted = loadedRecords == null ? new List<DnsRecord>() : new List<DnsRecord>(loadedRecords);
            sorted.Sort(delegate(DnsRecord a, DnsRecord b) { return String.Compare(b.CreatedAt, a.CreatedAt, StringComparison.Ordinal); });
            if (filter != "全部类型") sorted.RemoveAll(delegate(DnsRecord r) { return r.Type != filter; });
            int pageCount = (sorted.Count + PageSize - 1) / PageSize;
            pageIndex = Math.Max(0, Math.Min(pageIndex, pageCount - 1));
            int start = pageIndex * PageSize, end = Math.Min(start + PageSize, sorted.Count);
            for (int i = start; i < end; i++)
            {
                DnsRecord r = sorted[i];
                int index = records.Rows.Add(false, r.Host, r.Type, r.Value, r.Line, r.Ttl);
                records.Rows[index].Tag = r;
            }
            pageInfo.Text = String.Format("第 {0} / {1} 页  共 {2} 条  每页 {3} 条", pageCount == 0 ? 0 : pageIndex + 1, pageCount, sorted.Count, PageSize);
            previousPage.Enabled = pageIndex > 0;
            nextPage.Enabled = pageIndex + 1 < pageCount;
        }
        private void RecordActionClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || domains.SelectedItem == null) return;
            string column = records.Columns[e.ColumnIndex].Name;
            if (column == "Edit") { EditRecord(sender, e); return; }
            if (column != "Delete") return;
            DnsRecord record = records.Rows[e.RowIndex].Tag as DnsRecord;
            if (record == null || MessageBox.Show(this, "确认删除解析记录：" + record.Host + "（" + record.Type + "）？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            records.Enabled = false;
            try { DnsProviders.Delete(activeCredential, (DnsDomain)domains.SelectedItem, record); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "删除失败，请核实记录状态", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { DomainChanged(null, EventArgs.Empty); records.Enabled = true; }
        }
        private void AddClick(object sender, EventArgs e) { if (domains.SelectedItem == null) return; using (AddRecordForm f = new AddRecordForm()) if (f.ShowDialog(this) == DialogResult.OK) try { DnsProviders.Add(activeCredential, (DnsDomain)domains.SelectedItem, f.Host, f.Type, f.Value); DomainChanged(null, EventArgs.Empty); } catch (Exception ex) { MessageBox.Show(ex.Message, "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void EditRecord(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex == 0 || domains.SelectedItem == null) return;
            DnsRecord record = records.Rows[e.RowIndex].Tag as DnsRecord;
            if (record == null) return;
            using (AddRecordForm f = new AddRecordForm(record))
                if (f.ShowDialog(this) == DialogResult.OK)
                    try { DnsProviders.Update(activeCredential, (DnsDomain)domains.SelectedItem, record, f.Host, f.Type, f.Value); DomainChanged(null, EventArgs.Empty); }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "修改失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void DeleteClick(object sender, EventArgs e)
        {
            if (domains.SelectedItem == null) return;
            records.EndEdit();
            List<DnsRecord> selected = new List<DnsRecord>();
            foreach (DataGridViewRow row in records.Rows) if (Object.Equals(row.Cells[0].Value, true)) selected.Add((DnsRecord)row.Tag);
            if (selected.Count == 0) return;
            if (MessageBox.Show("确认删除勾选的 " + selected.Count + " 条解析记录？", "批量删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int completed = 0;
            Button button = sender as Button; if (button != null) button.Enabled = false;
            try { foreach (DnsRecord record in selected) { DnsProviders.Delete(activeCredential, (DnsDomain)domains.SelectedItem, record); completed++; } }
            catch (Exception ex) { MessageBox.Show("已确认删除 " + completed + " 条，后续操作已停止。失败请求的结果请刷新核实。\n\n" + ex.Message, "批量删除未完成", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { DomainChanged(null, EventArgs.Empty); if (button != null) button.Enabled = true; }
        }
        private void LogoutClick(object sender, EventArgs e) { if (MessageBox.Show(this, "确认退出当前用户？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return; CredentialStore.Delete(current); ShowProvider(); }
    }

    internal sealed class AddRecordForm : Form
    {
        public string Host { get { return host.Text.Trim(); } } public string Type { get { return Convert.ToString(type.SelectedItem); } } public string Value { get { return value.Text.Trim(); } }
        private TextBox host = new TextBox(), value = new TextBox(); private ComboBox type = new ComboBox();
        public AddRecordForm(DnsRecord record) : this()
        {
            Text = "编辑解析"; host.Text = record.Host; value.Text = record.Value;
            if (!type.Items.Contains(record.Type)) type.Items.Add(record.Type);
            type.SelectedItem = record.Type;
        }
        public AddRecordForm()
        {
            SuspendLayout();
            Text = "添加解析"; Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font; AutoScaleDimensions = CurrentAutoScaleDimensions;
            ClientSize = new Size(620, 284); BackColor = Color.FromArgb(248, 250, 253);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            Label l1 = new Label { Text = "主机记录", AutoSize = true, Left = 20, Top = 25 };
            Label l2 = new Label { Text = "类型", AutoSize = true, Left = 20, Top = 65 };
            Label l3 = new Label { Text = "记录值", AutoSize = true, Left = 20, Top = 105 };
            host.SetBounds(104, 22, 492, 26);
            type.SetBounds(104, 62, 492, 26); type.DropDownStyle = ComboBoxStyle.DropDownList;
            type.Items.AddRange(new object[] { "A", "AAAA", "CNAME", "MX", "TXT", "NS" }); type.SelectedIndex = 0;
            value.Multiline = true; value.SetBounds(104, 102, 492, 110); value.ScrollBars = ScrollBars.Vertical;
            Button ok = new Button { Text = "保存", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(43, 108, 220), ForeColor = Color.White };
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            ok.SetBounds(408, 232, 86, 32); cancel.SetBounds(510, 232, 86, 32); ok.FlatAppearance.BorderSize = 0;
            Controls.AddRange(new Control[] { l1, host, l2, type, l3, value, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
            ResumeLayout(false); PerformLayout();
        }
    }
}
