using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace dnspod
{
    public partial class Form1 : Form
    {
        private TabControl providers; private ComboBox domains; private ComboBox types; private DataGridView records;
        private CloudProvider current;
        private TabControl accounts; private bool changingAccounts, openingAccount;
        private readonly Dictionary<CloudProvider, string> selectedAccounts = new Dictionary<CloudProvider, string>();
        private CloudCredential activeCredential; private IList<DnsDomain> loadedRecordsDomains; private IList<DnsRecord> loadedRecords;
        private const int PageSize = 100; private int pageIndex; private Label pageInfo; private Button previousPage, nextPage;
        public Form1() { InitializeComponent(); Build(); }
        private void Build()
        {
            Text = "云 DNS 管理"; Width = 800; Height = 620; MinimumSize = new Size(760, 480); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            providers = new TabControl { Dock = DockStyle.Top, Height = 38, Padding = new Point(14, 5) }; providers.TabPages.Add("阿里云"); providers.TabPages.Add("腾讯云"); providers.SelectedIndexChanged += delegate { current = (CloudProvider)providers.SelectedIndex; ShowProvider(); };
            accounts = new TabControl { Dock = DockStyle.Top, Height = 34, Padding = new Point(24, 3), ShowToolTips = true, DrawMode = TabDrawMode.OwnerDrawFixed };
            accounts.SelectedIndexChanged += AccountChanged;
            accounts.DrawItem += DrawAccountTab;
            accounts.Selecting += AccountTabSelecting;
            accounts.MouseClick += AccountTabClick;
            accounts.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && accounts.SelectedTab != null && !(accounts.SelectedTab.Tag is CloudCredential))
                {
                    e.Handled = true; e.SuppressKeyPress = true; ShowAccountLogin(null);
                }
            };
            GroupBox operation = new GroupBox { Text = "DNS 解析管理", Dock = DockStyle.Top, Height = 116, Padding = new Padding(12, 20, 12, 8) };
            domains = new ComboBox { Left = 12, Top = 25, Width = 285, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList }; domains.SelectedIndexChanged += DomainChanged;
            types = new ComboBox { Left = 313, Top = 25, Width = 150, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList }; types.Items.Add("全部类型"); types.SelectedIndex = 0; types.SelectedIndexChanged += delegate { pageIndex = 0; RenderRecords(); };
            Button add = new Button { Left = 12, Top = 65, Width = 112, Height = 34, Text = "＋ 添加解析", UseVisualStyleBackColor = true }; add.Click += AddClick;
            Button del = new Button { Left = 136, Top = 65, Width = 112, Height = 34, Text = "× 删除勾选", UseVisualStyleBackColor = true }; del.Click += DeleteClick;
            operation.Controls.AddRange(new Control[] { domains, types, add, del });
            records = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false, EnableHeadersVisualStyles = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = true, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, GridColor = Color.FromArgb(232, 237, 244), ColumnHeadersHeight = 42, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };
            records.DefaultCellStyle = new DataGridViewCellStyle { Font = Font, ForeColor = Color.FromArgb(45, 55, 72), BackColor = Color.White, SelectionBackColor = Color.FromArgb(225, 238, 255), SelectionForeColor = Color.FromArgb(30, 64, 120), Padding = new Padding(10, 0, 10, 0) };
            records.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font(Font, FontStyle.Bold), BackColor = Color.FromArgb(238, 243, 250), ForeColor = Color.FromArgb(66, 82, 110), SelectionBackColor = Color.FromArgb(238, 243, 250), Padding = new Padding(10, 0, 10, 0) };
            records.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            records.RowTemplate.Height = 40;
            records.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Checked", HeaderText = "勾选", Width = 64, Resizable = DataGridViewTriState.False, FlatStyle = FlatStyle.Flat });
            string[] names = { "Host", "Type", "Value", "Line", "Ttl", "Remark" };
            string[] labels = { "主机记录", "记录类型", "记录值", "线路", "TTL（秒）", "备注" };
            int[] widths = { 120, 80, 200, 80, 90, 160 };
            for (int i = 0; i < names.Length; i++) records.Columns.Add(new DataGridViewTextBoxColumn { Name = names[i], HeaderText = labels[i], Width = widths[i], ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic });
            records.Columns["Value"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            records.Columns["Value"].MinimumWidth = 120;
            records.Columns.Add(new DataGridViewButtonColumn { Name = "Edit", HeaderText = "操作", Text = "编辑", UseColumnTextForButtonValue = true, Width = 62, ReadOnly = true });
            records.Columns.Add(new DataGridViewButtonColumn { Name = "Delete", HeaderText = "", Text = "删除", UseColumnTextForButtonValue = true, Width = 62, ReadOnly = true });
            records.CellContentClick += RecordActionClick;
            records.CurrentCellDirtyStateChanged += delegate { if (records.IsCurrentCellDirty) records.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            records.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.ColumnIndex >= 0 && !(records.Columns[e.ColumnIndex] is DataGridViewButtonColumn)) EditRecord(sender, e); };
            ContextMenuStrip copyMenu = new ContextMenuStrip();
            copyMenu.Items.Add("复制选中内容（Ctrl+C）", null, delegate { CopyRecords(); });
            records.ContextMenuStrip = copyMenu;
            records.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0 || records[e.ColumnIndex, e.RowIndex].Selected) return;
                records.ClearSelection(); records.CurrentCell = records[e.ColumnIndex, e.RowIndex]; records.CurrentCell.Selected = true;
            };
            Panel pagination = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            previousPage = new Button { Left = 12, Top = 7, Width = 86, Height = 30, Text = "上一页" };
            nextPage = new Button { Left = 108, Top = 7, Width = 86, Height = 30, Text = "下一页" };
            pageInfo = new Label { Left = 210, Top = 13, AutoSize = true };
            previousPage.Click += delegate { if (pageIndex > 0) { pageIndex--; RenderRecords(); } };
            nextPage.Click += delegate { pageIndex++; RenderRecords(); };
            pagination.Controls.AddRange(new Control[] { previousPage, nextPage, pageInfo });
            Controls.Add(records); Controls.Add(pagination); Controls.Add(operation); Controls.Add(accounts); Controls.Add(providers); current = CloudProvider.Aliyun; ShowProvider();
        }
        private void CopyRecords()
        {
            if (records.GetCellCount(DataGridViewElementStates.Selected) == 0) return;
            try { DataObject content = records.GetClipboardContent(); if (content != null) Clipboard.SetDataObject(content); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "剪贴板暂时无法访问，请重试。", "复制失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private Rectangle AccountCloseBounds(int index)
        {
            Rectangle tab = accounts.GetTabRect(index);
            int size = Math.Min(SystemInformation.SmallCaptionButtonSize.Width, Math.Max(1, tab.Height - 6));
            return new Rectangle(tab.Right - size - 5, tab.Top + (tab.Height - size) / 2, size, size);
        }
        private void DrawAccountTab(object sender, DrawItemEventArgs e)
        {
            TabPage tab = accounts.TabPages[e.Index];
            e.DrawBackground();
            Rectangle text = e.Bounds; text.Inflate(-8, 0);
            if (tab.Tag is CloudCredential)
            {
                Rectangle close = AccountCloseBounds(e.Index);
                text.Width = Math.Max(0, close.Left - text.Left - 4);
                ControlPaint.DrawCaptionButton(e.Graphics, close, CaptionButton.Close, ButtonState.Flat);
            }
            TextRenderer.DrawText(e.Graphics, tab.Text, accounts.Font, text, SystemColors.ControlText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }
        private void AccountTabSelecting(object sender, TabControlCancelEventArgs e)
        {
            if (changingAccounts) return;
            if (!(e.TabPage.Tag is CloudCredential))
            {
                e.Cancel = true;
                if (Control.MouseButtons == MouseButtons.None) BeginInvoke((MethodInvoker)delegate { ShowAccountLogin(null); });
            }
            else if (Control.MouseButtons == MouseButtons.Left && AccountCloseBounds(e.TabPageIndex).Contains(accounts.PointToClient(Control.MousePosition))) e.Cancel = true;
        }
        private void AccountTabClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            for (int i = 0; i < accounts.TabPages.Count; i++)
            {
                if (!accounts.GetTabRect(i).Contains(e.Location)) continue;
                TabPage tab = accounts.TabPages[i];
                if (!(tab.Tag is CloudCredential)) ShowAccountLogin(null);
                else if (AccountCloseBounds(i).Contains(e.Location)) CloseAccount(tab);
                return;
            }
        }
        private void ShowProvider()
        {
            changingAccounts = true;
            try
            {
                accounts.TabPages.Clear();
                string selected; selectedAccounts.TryGetValue(current, out selected);
                foreach (CloudCredential credential in CredentialStore.ReadAll(current))
                {
                    TabPage tab = new TabPage((accounts.TabPages.Count + 1) + ". " + CredentialStore.Mask(credential.Id));
                    tab.Tag = credential; tab.ToolTipText = CredentialStore.Mask(credential.Id); accounts.TabPages.Add(tab);
                    if (credential.Id == selected) accounts.SelectedTab = tab;
                }
                accounts.TabPages.Add(new TabPage("＋ 添加账号"));
                if (accounts.SelectedIndex < 0) accounts.SelectedIndex = 0;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "读取账号失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { changingAccounts = false; }
            AccountChanged(null, EventArgs.Empty);
        }
        private void AccountChanged(object sender, EventArgs e)
        {
            if (changingAccounts) return;
            CloudCredential credential = accounts.SelectedTab == null ? null : accounts.SelectedTab.Tag as CloudCredential;
            activeCredential = null; loadedRecordsDomains = null; loadedRecords = null; pageIndex = 0;
            domains.Items.Clear(); types.Items.Clear(); types.Items.Add("全部类型"); types.SelectedIndex = 0;
            RenderRecords();
            if (credential == null) return;
            selectedAccounts[current] = credential.Id;
            try { LoadDomains(credential); }
            catch (Exception ex)
            {
                activeCredential = null; loadedRecordsDomains = null; domains.Items.Clear();
                MessageBox.Show(this, "该账号无法读取域名，其他账号不受影响。\n\n" + ex.Message, "加载域名失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowAccountLogin(credential);
            }
        }
        private void ShowAccountLogin(CloudCredential existingCredential)
        {
            if (openingAccount) return;
            openingAccount = true;
            try
            {
                using (AccountLoginForm dialog = new AccountLoginForm(current, existingCredential))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    CloudCredential credential = dialog.Credential;
                    changingAccounts = true;
                    try
                    {
                        TabPage tab = null;
                        foreach (TabPage candidate in accounts.TabPages)
                        {
                            CloudCredential existing = candidate.Tag as CloudCredential;
                            if (existing != null && existing.Id == credential.Id) { tab = candidate; break; }
                        }
                        if (tab == null)
                        {
                            tab = new TabPage((accounts.TabPages.Count) + ". " + CredentialStore.Mask(credential.Id));
                            accounts.TabPages.Insert(Math.Max(0, accounts.TabPages.Count - 1), tab);
                        }
                        tab.Tag = credential; tab.ToolTipText = CredentialStore.Mask(credential.Id); accounts.SelectedTab = tab;
                    }
                    finally { changingAccounts = false; }
                    selectedAccounts[current] = credential.Id;
                    loadedRecords = null; pageIndex = 0; RenderRecords();
                    ShowDomains(credential, dialog.Domains);
                }
            }
            finally { openingAccount = false; }
        }
        private void LoadDomains(CloudCredential c) { ShowDomains(c, DnsProviders.Domains(c)); }
        private void ShowDomains(CloudCredential c, IList<DnsDomain> items) { loadedRecordsDomains = items; activeCredential = c; domains.Items.Clear(); foreach (DnsDomain d in loadedRecordsDomains) domains.Items.Add(d); if (domains.Items.Count > 0) domains.SelectedIndex = 0; else MessageBox.Show("账号验证成功，但没有读取到可管理的域名。请检查账号权限和域名归属。", "域名为空", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void DomainChanged(object sender, EventArgs e) { loadedRecords = null; pageIndex = 0; RenderRecords(); if (domains.SelectedItem == null) return; try { DnsDomain domain = (DnsDomain)domains.SelectedItem; loadedRecords = DnsProviders.Records(activeCredential, domain); types.Items.Clear(); types.Items.Add("全部类型"); foreach (DnsRecord r in loadedRecords) if (!types.Items.Contains(r.Type)) types.Items.Add(r.Type); types.SelectedIndex = 0; RenderRecords(); } catch (Exception ex) { MessageBox.Show("读取域名解析记录失败。\n\n" + ex.Message, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
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
                int index = records.Rows.Add(false, r.Host, r.Type, r.Value, r.Line, r.Ttl, r.Remark);
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
                    try { DnsProviders.Update(activeCredential, (DnsDomain)domains.SelectedItem, record, f.Host, f.Type, f.Value, f.Remark); }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "修改失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    finally { DomainChanged(null, EventArgs.Empty); }
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
        private void CloseAccount(TabPage tab)
        {
            CloudCredential credential = tab.Tag as CloudCredential;
            if (credential == null || MessageBox.Show(this, "确认关闭账号 " + tab.Text + " 并退出登录？\n将移除此账号保存的凭据，其他账号保持登录。", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                CredentialStore.Delete(credential);
                bool selected = accounts.SelectedTab == tab;
                int index = accounts.TabPages.IndexOf(tab);
                changingAccounts = true;
                try
                {
                    accounts.TabPages.Remove(tab); tab.Dispose();
                    for (int i = 0; i < accounts.TabPages.Count; i++)
                    {
                        CloudCredential remaining = accounts.TabPages[i].Tag as CloudCredential;
                        if (remaining != null) accounts.TabPages[i].Text = (i + 1) + ". " + CredentialStore.Mask(remaining.Id);
                    }
                    if (selected && accounts.TabPages.Count > 1) accounts.SelectedIndex = Math.Min(index, accounts.TabPages.Count - 2);
                }
                finally { changingAccounts = false; }
                accounts.Invalidate();
                if (selected) { selectedAccounts.Remove(current); AccountChanged(null, EventArgs.Empty); }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "退出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    internal sealed class AccountLoginForm : Form
    {
        private readonly CloudProvider provider;
        private readonly TextBox id, key;
        private readonly Button login;
        private readonly Label status;
        public CloudCredential Credential { get; private set; }
        public IList<DnsDomain> Domains { get; private set; }
        public AccountLoginForm(CloudProvider provider, CloudCredential existingCredential)
        {
            this.provider = provider;
            Text = existingCredential == null ? "添加" + (provider == CloudProvider.Aliyun ? "阿里云" : "腾讯云") + "账号" : "重新验证账号";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font; AutoScaleDimensions = CurrentAutoScaleDimensions;
            ClientSize = new Size(620, 186); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            id = new TextBox { Left = 86, Top = 24, Width = 510, Text = existingCredential == null ? "" : existingCredential.Id };
            key = new TextBox { Left = 86, Top = 64, Width = 510, UseSystemPasswordChar = true };
            status = new Label { Left = 86, Top = 102, AutoSize = true, ForeColor = Color.DimGray };
            login = new Button { Left = 370, Top = 138, Width = 130, Height = 32, Text = "登录并验证", UseVisualStyleBackColor = true };
            Button cancel = new Button { Left = 510, Top = 138, Width = 86, Height = 32, Text = "取消", DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true };
            login.Click += LoginClick;
            Controls.AddRange(new Control[] { new Label { Left = 20, Top = 28, AutoSize = true, Text = "ID" }, id, new Label { Left = 20, Top = 68, AutoSize = true, Text = "KEY" }, key, status, login, cancel });
            AcceptButton = login; CancelButton = cancel;
            ActiveControl = existingCredential == null ? id : key;
        }
        private void LoginClick(object sender, EventArgs e)
        {
            if (!login.Enabled) return;
            try
            {
                if (String.IsNullOrEmpty(id.Text.Trim()) || String.IsNullOrEmpty(key.Text)) throw new InvalidOperationException("请输入 ID 和 KEY。");
                login.Enabled = false; status.Text = "正在验证账号...";
                CloudCredential credential = new CloudCredential { Provider = provider, Id = id.Text.Trim(), Secret = key.Text };
                IList<DnsDomain> domains = DnsProviders.Domains(credential);
                CredentialStore.Write(credential);
                Credential = credential; Domains = domains;
                id.Clear(); key.Clear(); DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { status.Text = "登录失败"; MessageBox.Show(this, "云 API 调用失败。\n\n" + ex.Message, "登录失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { login.Enabled = true; }
        }
    }

    internal sealed class AddRecordForm : Form
    {
        public string Host { get { return host.Text.Trim(); } } public string Type { get { return Convert.ToString(type.SelectedItem); } } public string Value { get { return value.Text.Trim(); } }
        public string Remark { get { return remark.Text; } }
        private TextBox remark = new TextBox();
        private TextBox host = new TextBox(), value = new TextBox(); private ComboBox type = new ComboBox();
        public AddRecordForm(DnsRecord record) : this()
        {
            Text = "编辑解析"; host.Text = record.Host; value.Text = record.Value;
            if (!type.Items.Contains(record.Type)) type.Items.Add(record.Type);
            type.SelectedItem = record.Type;
            ClientSize = new Size(620, 332);
            remark.Text = record.Remark; remark.SetBounds(104, 224, 492, 26);
            foreach (Control control in Controls) if (control is Button) control.Top += 48;
            Controls.Add(new Label { Text = "备注", AutoSize = true, Left = 20, Top = 227 }); Controls.Add(remark);
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
