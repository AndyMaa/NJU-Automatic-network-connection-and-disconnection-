using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        }
        catch { }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show("程序启动失败：" + ex.Message, "NJU-WLAN 网络助手",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show(
            "程序发生错误：" + e.Exception.Message + "\r\n\r\n" +
            "类型：" + e.Exception.GetType().FullName + "\r\n\r\n" +
            "详细信息已写入 error.log。",
            "NJU-WLAN 网络助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try { LogError(e.ExceptionObject as Exception); }
        catch { }
    }

    static void LogError(Exception ex)
    {
        if (ex == null) return;
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            string text = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\r\n" +
                ex.GetType().FullName + ": " + ex.Message + "\r\n" +
                ex.StackTrace + "\r\n\r\n";
            File.AppendAllText(path, text, Encoding.UTF8);
        }
        catch { }
    }
}

class Config
{
    public string Username = "";
    public string Password = "";
    public int IdleTimeoutSeconds = 300;
    public int PollSeconds = 5;
    public string InfoUrl = "https://p.nju.edu.cn/api/portal/v1/getinfo";
    public string LoginUrl = "https://p.nju.edu.cn/api/portal/v1/login";
    public string LogoutUrl = "https://p.nju.edu.cn/api/portal/v1/logout";
}

class Status
{
    public bool online;
    public string fullname = "";
    public string username = "";
    public string balance = "";
    public string service = "";
    public string error = "";
}

class ConnectResult
{
    public bool Success = false;
    public string Message = "";
}

class HandleTab : Form
{
    public HandleTab(Color color)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = color;
        Size = new Size(12, 48);
        MinimumSize = new Size(12, 48);
        MaximumSize = new Size(12, 48);
        Cursor = Cursors.Hand;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            int r = 6;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(Width - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(Width - r * 2, Height - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, Height - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }
    }
}

class MainForm : Form
{
    // 主题主色（南京大学紫）
    static readonly Color PRIMARY = Color.FromArgb(0x6A, 0x00, 0x5F);
    static readonly Color PRIMARY_LIGHT = Color.FromArgb(0xF3, 0xE5, 0xF1);
    static readonly Color BG = Color.FromArgb(0xF5, 0xF7, 0xFA);
    static readonly Color CARD = Color.White;
    static readonly Color TEXT = Color.FromArgb(0x32, 0x37, 0x3C);
    static readonly Color MUTED = Color.FromArgb(0x5A, 0x66, 0x72);
    static readonly Color SUCCESS = Color.FromArgb(0x2E, 0x7D, 0x32);
    static readonly Color DANGER = Color.FromArgb(0xC0, 0x39, 0x2B);
    static readonly Color WARN = Color.FromArgb(0xE6, 0x7E, 0x22);

    Font _fontUI = new Font("Microsoft YaHei UI", 9f);
    Font _fontBold = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
    Font _fontTitle = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
    Font _fontBig = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
    Font _fontMono = new Font("Consolas", 9f);
    Font _fontSmall = new Font("Microsoft YaHei UI", 8.5f);

    string _configPath;
    Config _cfg;

    List<Button> _buttons = new List<Button>();
    Button _btnStartAuto;
    Button _btnStopAuto;
    Button _btnDock;

    TextBox _txtUsername;
    TextBox _txtPassword;
    TextBox _txtTimeout;
    Label _statusLabel;
    Label _statusDot;
    Label _accountLabel;
    Label _idleLabel;
    TextBox _logBox;

    bool _busy = false;
    bool _autoRunning = false;
    Thread _autoThread;

    bool _dockMode = false;
    bool _docked = false;
    Rectangle _normalBounds;
    Rectangle _workArea;
    HandleTab _tab;
    System.Windows.Forms.Timer _hoverTimer;
    System.Windows.Forms.Timer _idleTimer;
    System.Windows.Forms.Timer _statusTimer;

    public MainForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "NJU-WLAN 网络助手";
        ClientSize = new Size(720, 700);
        MinimumSize = new Size(700, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BG;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { }

        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NJU-WLAN-Helper");
        try { Directory.CreateDirectory(appDataDir); } catch { }
        _configPath = Path.Combine(appDataDir, "config.json");
        _cfg = LoadConfig(_configPath);

        BuildUi();
        UpdateAutoButtons();

        _hoverTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _hoverTimer.Tick += HoverTick;
        _hoverTimer.Start();

        _idleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _idleTimer.Tick += delegate { if (!_autoRunning) UpdateIdleUi(GetIdleSeconds()); };
        _idleTimer.Start();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 20000 };
        _statusTimer.Tick += delegate { if (!_autoRunning && !_busy) RefreshStatus(); };
        _statusTimer.Start();

        Log("程序已启动。请先填写并保存账号密码，然后使用一键联网或自动模式。");
        RefreshStatus();
    }

    void BuildUi()
    {
        Panel root = new Panel { Dock = DockStyle.Fill, BackColor = BG };
        Controls.Add(root);

        // 顶部标题栏
        Panel header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = PRIMARY };
        root.Controls.Add(header);

        Label title = new Label
        {
            Text = "NJU-WLAN 网络助手",
            ForeColor = Color.White,
            BackColor = PRIMARY,
            Font = _fontBig,
            AutoSize = true,
            Location = new Point(20, 15)
        };
        header.Controls.Add(title);

        Label subtitle = new Label
        {
            Text = "空闲自动断网 · 操作自动联网",
            ForeColor = PRIMARY_LIGHT,
            BackColor = PRIMARY,
            Font = _fontSmall,
            AutoSize = true,
            Location = new Point(20, 44)
        };
        header.Controls.Add(subtitle);

        // 状态卡片
        Panel statusCard = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = CARD };
        statusCard.Paint += CardBorder;
        root.Controls.Add(statusCard);

        Label statusTitle = new Label
        {
            Text = "当前状态",
            Font = _fontUI,
            ForeColor = MUTED,
            BackColor = CARD,
            AutoSize = true,
            Location = new Point(20, 16)
        };
        statusCard.Controls.Add(statusTitle);

        _statusDot = new Label
        {
            Text = "●",
            Font = new Font("Microsoft YaHei UI", 11f),
            ForeColor = MUTED,
            BackColor = CARD,
            AutoSize = true,
            Location = new Point(96, 15)
        };
        statusCard.Controls.Add(_statusDot);

        _statusLabel = new Label
        {
            Text = "检测中...",
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = MUTED,
            BackColor = CARD,
            AutoSize = true,
            Location = new Point(116, 13)
        };
        statusCard.Controls.Add(_statusLabel);

        _accountLabel = new Label
        {
            Text = "账号：",
            Font = _fontUI,
            ForeColor = TEXT,
            BackColor = CARD,
            AutoSize = false,
            Size = new Size(660, 22),
            Location = new Point(20, 44)
        };
        statusCard.Controls.Add(_accountLabel);

        _idleLabel = new Label
        {
            Text = "空闲时间：0 秒",
            Font = _fontUI,
            ForeColor = TEXT,
            BackColor = CARD,
            AutoSize = false,
            Size = new Size(660, 22),
            Location = new Point(20, 72)
        };
        statusCard.Controls.Add(_idleLabel);

        // 设置卡片
        Panel settingsCard = new Panel { Dock = DockStyle.Top, Height = 158, BackColor = CARD };
        settingsCard.Paint += CardBorder;
        root.Controls.Add(settingsCard);

        Label settingsTitle = new Label
        {
            Text = "账号与超时设置",
            Font = _fontTitle,
            ForeColor = TEXT,
            BackColor = CARD,
            AutoSize = true,
            Location = new Point(20, 12)
        };
        settingsCard.Controls.Add(settingsTitle);

        AddField(settingsCard, "账号", 40, 104, 400, out _txtUsername, false);
        AddField(settingsCard, "密码", 76, 104, 400, out _txtPassword, true);

        Label timeoutLabel = new Label
        {
            Text = "超时时间",
            Font = _fontUI,
            ForeColor = TEXT,
            BackColor = CARD,
            AutoSize = true,
            Location = new Point(20, 116)
        };
        settingsCard.Controls.Add(timeoutLabel);

        _txtTimeout = new TextBox
        {
            Font = _fontUI,
            Location = new Point(104, 112),
            Width = 64,
            Text = Math.Max(1, _cfg.IdleTimeoutSeconds / 60).ToString()
        };
        settingsCard.Controls.Add(_txtTimeout);

        Label minLabel = new Label
        {
            Text = "分钟",
            Font = _fontUI,
            ForeColor = TEXT,
            BackColor = CARD,
            AutoSize = true,
            Location = new Point(176, 116)
        };
        settingsCard.Controls.Add(minLabel);

        Button btnSave = MakeButton("保存设置", PRIMARY, Color.White, SaveSettings);
        btnSave.Dock = DockStyle.None;
        btnSave.Location = new Point(550, 44);
        btnSave.Size = new Size(150, 36);
        settingsCard.Controls.Add(btnSave);

        Button btnSaveLogin = MakeButton("保存并登录", SUCCESS, Color.White, SaveAndLogin);
        btnSaveLogin.Dock = DockStyle.None;
        btnSaveLogin.Location = new Point(550, 90);
        btnSaveLogin.Size = new Size(150, 36);
        settingsCard.Controls.Add(btnSaveLogin);

        // 操作按钮卡片
        Panel actionCard = new Panel { Dock = DockStyle.Top, Height = 144, BackColor = BG };
        root.Controls.Add(actionCard);

        TableLayoutPanel grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = BG,
            Padding = new Padding(20, 8, 20, 8)
        };
        for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        for (int i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        actionCard.Controls.Add(grid);

        Button btnConnect = MakeButton("立即联网", PRIMARY, Color.White, ConnectStart);
        Button btnDisconnect = MakeButton("立即断网", DANGER, Color.White, DisconnectStart);
        _btnStartAuto = MakeButton("启动自动模式", PRIMARY, Color.White, StartAuto);
        _btnStopAuto = MakeButton("停止自动模式", DANGER, Color.White, StopAuto);
        Button btnStatus = MakeButton("查看状态", PRIMARY, Color.White, StatusStart);
        _btnDock = MakeButton("贴边隐藏", PRIMARY, Color.White, ToggleDock);
        Button btnExit = MakeButton("退出程序", Color.FromArgb(0x5A, 0x66, 0x72), Color.White, delegate { Close(); });

        grid.Controls.Add(btnConnect, 0, 0);
        grid.Controls.Add(btnDisconnect, 1, 0);
        grid.Controls.Add(_btnStartAuto, 2, 0);
        grid.Controls.Add(_btnStopAuto, 3, 0);
        grid.Controls.Add(btnStatus, 0, 1);
        grid.Controls.Add(_btnDock, 1, 1);
        grid.Controls.Add(btnExit, 2, 1);

        // 日志卡片
        Panel logCard = new Panel { Dock = DockStyle.Fill, BackColor = CARD, Padding = new Padding(20, 8, 20, 14) };
        root.Controls.Add(logCard);

        Label logTitle = new Label
        {
            Text = "运行日志",
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = TEXT,
            BackColor = CARD,
            Dock = DockStyle.Top,
            Height = 28
        };
        logCard.Controls.Add(logTitle);

        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = CARD,
            ForeColor = TEXT,
            Font = _fontMono,
            Dock = DockStyle.Fill
        };
        logCard.Controls.Add(_logBox);

        // 修正停靠顺序：让 Fill 的日志卡在最底层，顶部卡片按 header→status→settings→action 顺序停靠
        root.Controls.SetChildIndex(logCard, 0);
        root.Controls.SetChildIndex(actionCard, 1);
        root.Controls.SetChildIndex(settingsCard, 2);
        root.Controls.SetChildIndex(statusCard, 3);
        root.Controls.SetChildIndex(header, 4);
    }

    void AddField(Panel card, string labelText, int y, int x, int width, out TextBox box, bool isPassword)
    {
        Label label = new Label
        {
            Text = labelText,
            Font = _fontUI,
            ForeColor = TEXT,
            BackColor = CARD,
            AutoSize = true,
            Location = new Point(20, y + 4)
        };
        card.Controls.Add(label);

        box = new TextBox
        {
            Font = _fontUI,
            Location = new Point(x, y),
            Width = width,
            UseSystemPasswordChar = isPassword
        };
        if (isPassword) box.Text = _cfg.Password;
        else box.Text = _cfg.Username;
        card.Controls.Add(box);
    }

    void CardBorder(object sender, PaintEventArgs e)
    {
        Control c = (Control)sender;
        using (Pen p = new Pen(Color.FromArgb(0xE3, 0xE8, 0xEE)))
        {
            e.Graphics.DrawRectangle(p, 0, 0, c.Width - 1, c.Height - 1);
        }
    }

    Button MakeButton(string text, Color bg, Color fg, EventHandler onClick)
    {
        Button b = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = _fontBold,
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            Margin = new Padding(6)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.12f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.08f);
        b.Click += onClick;
        _buttons.Add(b);
        return b;
    }

    void Log(string message)
    {
        string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
        _logBox.AppendText(line + "\r\n");
    }

    void Ui(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(a); }
            catch { }
        }
        else
        {
            a();
        }
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (Button b in _buttons) b.Enabled = !busy;
        if (!busy) UpdateAutoButtons();
    }

    void UpdateAutoButtons()
    {
        if (_busy) return;
        if (_btnStartAuto != null) _btnStartAuto.Enabled = !_autoRunning;
        if (_btnStopAuto != null) _btnStopAuto.Enabled = _autoRunning;
    }

    void UpdateStatusUi(Status st)
    {
        Color statusColor;
        if (st.online)
        {
            _statusLabel.Text = "在线";
            statusColor = SUCCESS;
        }
        else if (st.error != "")
        {
            _statusLabel.Text = "未知";
            statusColor = WARN;
        }
        else
        {
            _statusLabel.Text = "离线";
            statusColor = DANGER;
        }
        _statusLabel.ForeColor = statusColor;
        if (_statusDot != null) _statusDot.ForeColor = statusColor;

        string txt;
        if (st.online)
        {
            txt = "当前在线账号：" + (st.username != "" ? st.username : "未知");
            if (st.fullname != "") txt += "    姓名：" + st.fullname;
            if (st.service != "") txt += "    套餐：" + st.service;
            if (st.balance != "") txt += "    余额：" + st.balance;
        }
        else
        {
            txt = "账号：" + _cfg.Username;
            if (st.error != "") txt += "    " + st.error;
        }
        _accountLabel.Text = txt;
    }

    void UpdateIdleUi(int sec)
    {
        string suffix = _autoRunning ? "  （自动模式运行中）" : "";
        _idleLabel.Text = "空闲时间：" + sec + " 秒" + suffix;
    }

    void RefreshStatus()
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            Status st = GetStatus(_cfg);
            Ui(delegate { UpdateStatusUi(st); UpdateIdleUi(GetIdleSeconds()); });
        });
    }

    void RunAction(string startLog, Func<bool> action, string okLog, string failLog)
    {
        if (_busy) return;
        SetBusy(true);
        Log(startLog);
        ThreadPool.QueueUserWorkItem(delegate
        {
            bool ok = false;
            try { ok = action(); }
            catch (Exception ex) { Ui(delegate { Log("出错：" + ex.Message); }); }
            Ui(delegate
            {
                Log(ok ? okLog : failLog);
                RefreshStatus();
                SetBusy(false);
            });
        });
    }

    void ConnectStart(object sender, EventArgs e)
    {
        DoConnect("正在执行一键联网...", false);
    }

    void DisconnectStart(object sender, EventArgs e)
    {
        RunAction("正在执行一键断网...", delegate { return DisconnectCore(_cfg); }, "断网成功。", "断网失败，请稍后重试。");
    }

    void StatusStart(object sender, EventArgs e)
    {
        if (_busy) return;
        Log("正在查询状态...");
        RefreshStatus();
    }

    void SaveSettings(object sender, EventArgs e)
    {
        if (!ValidateSettings()) return;
        if (!SaveSettingsFromUi())
        {
            ShowSaveError();
            return;
        }
        Log("设置已保存。");
        RefreshStatus();
    }

    void SaveAndLogin(object sender, EventArgs e)
    {
        if (!ValidateSettings()) return;
        if (!SaveSettingsFromUi())
        {
            ShowSaveError();
            return;
        }
        DoConnect("正在使用新账号登录...", true);
    }

    void ShowSaveError()
    {
        MessageBox.Show(
            "保存失败：无法写入配置文件。\n\n" +
            "请检查系统是否开启了“受控文件夹访问”（Windows 安全中心），并放行本程序。",
            "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    void DoConnect(string startLog, bool forceRelogin)
    {
        if (_busy) return;
        SetBusy(true);
        Log(startLog);
        ThreadPool.QueueUserWorkItem(delegate
        {
            ConnectResult r = new ConnectResult();
            try { r = ConnectWithResult(_cfg, forceRelogin); }
            catch (Exception ex) { r.Message = ex.Message; }
            Ui(delegate
            {
                if (r.Success)
                {
                    Log("联网成功。");
                }
                else
                {
                    Log("联网失败。");
                    if (r.Message != "")
                    {
                        MessageBox.Show("联网失败：" + r.Message, "NJU-WLAN 网络助手",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                RefreshStatus();
                SetBusy(false);
            });
        });
    }

    bool ValidateSettings()
    {
        if (_txtUsername.Text.Trim() == "" || _txtPassword.Text == "")
        {
            MessageBox.Show("请输入账号和密码。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    bool SaveSettingsFromUi()
    {
        int minutes;
        if (!int.TryParse(_txtTimeout.Text.Trim(), out minutes)) minutes = 5;
        minutes = Math.Max(1, Math.Min(600, minutes));
        _txtTimeout.Text = minutes.ToString();

        _cfg.Username = _txtUsername.Text.Trim();
        _cfg.Password = _txtPassword.Text;
        _cfg.IdleTimeoutSeconds = minutes * 60;
        return SaveConfig(_configPath, _cfg);
    }

    void StartAuto(object sender, EventArgs e)
    {
        if (_autoRunning) return;
        if (_cfg.Username == "" || _cfg.Password == "")
        {
            MessageBox.Show("请先填写并保存账号密码。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _autoRunning = true;
        UpdateAutoButtons();
        Log("自动模式已启动。");

        _autoThread = new Thread(AutoLoop);
        _autoThread.IsBackground = true;
        _autoThread.Start();
    }

    void StopAuto(object sender, EventArgs e)
    {
        if (!_autoRunning) return;
        _autoRunning = false;
        UpdateAutoButtons();
        Log("正在停止自动模式...");
    }

    void AutoLoop()
    {
        long nextConnectAt = 0;
        long nextDisconnectAt = 0;

        while (_autoRunning)
        {
            int idle = GetIdleSeconds();
            Status st = GetStatus(_cfg);
            int timeout = Math.Max(30, _cfg.IdleTimeoutSeconds);
            bool shouldOnline = idle < timeout;

            if (shouldOnline && !st.online)
            {
                long now = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
                if (now >= nextConnectAt)
                {
                    Ui(delegate { Log("检测到操作（空闲 " + idle + " 秒），正在联网..."); });
                    ConnectResult cr = ConnectWithResult(_cfg, false);
                    if (cr.Success)
                    {
                        nextConnectAt = 0;
                        Ui(delegate { Log("自动联网成功。"); });
                    }
                    else if (cr.Message != "")
                    {
                        nextConnectAt = long.MaxValue;
                        Ui(delegate { Log("自动联网失败：" + cr.Message + "（已停止重试，请检查账号密码）。"); });
                    }
                    else
                    {
                        nextConnectAt = now + 20;
                        Ui(delegate { Log("自动联网失败，稍后重试。"); });
                    }
                }
            }
            else if (!shouldOnline && st.online)
            {
                long now = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
                if (now >= nextDisconnectAt)
                {
                    Ui(delegate { Log("已空闲 " + idle + " 秒，正在断网..."); });
                    bool ok = DisconnectCore(_cfg);
                    if (ok) { nextDisconnectAt = 0; Ui(delegate { Log("自动断网成功。"); }); }
                    else { nextDisconnectAt = now + 30; Ui(delegate { Log("自动断网失败，稍后重试。"); }); }
                }
            }

            Status final = st;
            int finalIdle = idle;
            Ui(delegate { UpdateStatusUi(final); UpdateIdleUi(finalIdle); });

            int poll = Math.Max(1, Math.Min(_cfg.PollSeconds, 5));
            for (int i = 0; i < poll && _autoRunning; i++) Thread.Sleep(1000);
        }

        Ui(delegate
        {
            Log("自动模式已停止。");
            UpdateAutoButtons();
        });
    }

    void ToggleDock(object sender, EventArgs e)
    {
        _dockMode = !_dockMode;
        if (_dockMode)
        {
            _btnDock.Text = "取消贴边";
            _normalBounds = Bounds;
            _workArea = Screen.FromControl(this).WorkingArea;
            DockToEdge();
            Log("已贴边隐藏：把鼠标移到屏幕右边缘的小标签即可弹出。");
        }
        else
        {
            _btnDock.Text = "贴边隐藏";
            RestoreNormal();
            Log("已退出贴边隐藏。");
        }
    }

    void EnsureTab()
    {
        if (_tab == null) _tab = new HandleTab(PRIMARY);
    }

    void DockToEdge()
    {
        if (_docked) return;
        EnsureTab();
        _tab.Location = new Point(_workArea.Right - _tab.Width,
            _workArea.Y + (_workArea.Height - _tab.Height) / 2);
        Hide();
        _tab.Show();
        _docked = true;
    }

    void SlideOut()
    {
        if (!_docked) return;
        if (_tab != null) _tab.Hide();
        int y = _workArea.Y + Math.Max(0, (_workArea.Height - Height) / 2);
        Location = new Point(_workArea.Right - Width, y);
        Show();
        _docked = false;
        BringToFront();
        Activate();
    }

    void RestoreNormal()
    {
        _docked = false;
        if (_tab != null) _tab.Hide();
        if (_normalBounds.Width > 0 && _normalBounds.Height > 0)
        {
            Location = _normalBounds.Location;
            Size = _normalBounds.Size;
        }
        Show();
        BringToFront();
        Activate();
    }

    void HoverTick(object sender, EventArgs e)
    {
        if (!_dockMode) return;
        Point p = Cursor.Position;
        if (_docked)
        {
            if (_tab != null && _tab.Visible && _tab.Bounds.Contains(p)) SlideOut();
        }
        else
        {
            if (!Bounds.Contains(p)) DockToEdge();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _autoRunning = false;
        if (_tab != null)
        {
            _tab.Dispose();
            _tab = null;
        }
        base.OnFormClosing(e);
    }

    // ===== 网络与系统底层 =====

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    static int GetIdleSeconds()
    {
        LASTINPUTINFO lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        if (!GetLastInputInfo(ref lii)) return 0;
        uint ms = (uint)Environment.TickCount - lii.dwTime;
        return (int)(ms / 1000);
    }

    static string Http(string method, string url, string body, int timeoutSec, out string error)
    {
        error = null;
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = timeoutSec * 1000;
            req.Accept = "application/json";
            req.ContentType = "application/json";
            req.Referer = "https://p.nju.edu.cn/";
            req.UserAgent = "Mozilla/5.0 (NJU-Network-Helper)";
            if (method == "POST" && body != null)
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            }
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    static string HttpPostRaw(string url, string body, int timeoutSec, out string error)
    {
        error = null;
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.Timeout = timeoutSec * 1000;
            req.Accept = "application/json";
            req.ContentType = "application/json";
            req.Referer = "https://p.nju.edu.cn/";
            req.UserAgent = "Mozilla/5.0 (NJU-Network-Helper)";
            byte[] data = Encoding.UTF8.GetBytes(body);
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }
        catch (WebException ex)
        {
            if (ex.Response != null)
            {
                try
                {
                    using (StreamReader sr = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
                catch { }
            }
            error = ex.Message;
            return null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    static int GetReplyCode(string json)
    {
        if (json == null) return -1;
        try
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            Dictionary<string, object> d = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (d != null && d.ContainsKey("reply_code")) return IntOf(d["reply_code"]);
        }
        catch { }
        return -1;
    }

    static string GetReplyMsg(string json)
    {
        if (json == null) return "";
        try
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            Dictionary<string, object> d = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (d != null)
            {
                if (d.ContainsKey("reply_msg")) return ToStr(d["reply_msg"]);
                if (d.ContainsKey("message")) return ToStr(d["message"]);
                if (d.ContainsKey("msg")) return ToStr(d["msg"]);
            }
        }
        catch { }
        return "";
    }

    static Status GetStatus(Config c)
    {
        Status st = new Status();
        string err;
        string text = Http("GET", c.InfoUrl, null, 8, out err);
        if (err != null) { st.error = err; return st; }

        try
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            object rootObj = ser.DeserializeObject(text);
            Dictionary<string, object> root = rootObj as Dictionary<string, object>;
            if (root == null) { st.error = "返回数据格式异常"; return st; }

            int reply = IntOf(Get(root, "reply_code"));
            int total = 0;
            object[] rows = null;
            Dictionary<string, object> results = Get(root, "results") as Dictionary<string, object>;
            if (results != null)
            {
                total = IntOf(Get(results, "total"));
                rows = Get(results, "rows") as object[];
            }

            if (reply == 0 && total > 0 && rows != null && rows.Length > 0)
            {
                Dictionary<string, object> row = rows[0] as Dictionary<string, object>;
                if (row != null)
                {
                    st.online = true;
                    st.fullname = ToStr(Get(row, "fullname"));
                    st.username = ToStr(Get(row, "username"));
                    st.balance = ToStr(Get(row, "balance"));
                    st.service = ToStr(Get(row, "service_name"));
                }
            }
        }
        catch (Exception ex)
        {
            st.error = ex.Message;
        }
        return st;
    }

    static ConnectResult ConnectWithResult(Config c, bool forceRelogin)
    {
        ConnectResult r = new ConnectResult();
        Status st = GetStatus(c);
        if (st.online && !forceRelogin) { r.Success = true; return r; }

        if (st.online && forceRelogin)
        {
            string lerr;
            Http("POST", c.LogoutUrl, "{}", 10, out lerr);
            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(1000);
                if (!GetStatus(c).online) break;
            }
        }

        string body = "{\"username\":" + JsStr(c.Username) + ",\"password\":" + JsStr(c.Password) + "}";
        string err;
        string resp = HttpPostRaw(c.LoginUrl, body, 10, out err);

        int code = GetReplyCode(resp);
        if (code > 0)
        {
            r.Message = GetReplyMsg(resp);
            if (r.Message == "") r.Message = "账号或密码错误，请检查后重试。";
            return r;
        }

        for (int i = 0; i < 8; i++)
        {
            Thread.Sleep(2000);
            if (GetStatus(c).online) { r.Success = true; return r; }
        }
        return r;
    }

    static bool DisconnectCore(Config c)
    {
        Status st = GetStatus(c);
        if (!st.online) return true;

        string err;
        Http("POST", c.LogoutUrl, "{}", 10, out err);
        if (err != null) return false;

        for (int i = 0; i < 8; i++)
        {
            Thread.Sleep(2000);
            if (!GetStatus(c).online) return true;
        }
        return false;
    }

    static object Get(Dictionary<string, object> d, string key)
    {
        object o;
        return d.TryGetValue(key, out o) ? o : null;
    }

    static int IntOf(object o)
    {
        try { return Convert.ToInt32(o); }
        catch { return 0; }
    }

    static string ToStr(object o)
    {
        if (o == null) return "";
        return Convert.ToString(o);
    }

    static string JsStr(string s)
    {
        if (s == null) return "null";
        StringBuilder sb = new StringBuilder();
        sb.Append('"');
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u" + ((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    static Config LoadConfig(string path)
    {
        Config c = new Config();
        if (!File.Exists(path)) return c;
        try
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            Dictionary<string, object> d = ser.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (d == null) return c;
            if (d.ContainsKey("username")) c.Username = ToStr(d["username"]);
            if (d.ContainsKey("password")) c.Password = ToStr(d["password"]);
            if (d.ContainsKey("infoUrl")) c.InfoUrl = ToStr(d["infoUrl"]);
            if (d.ContainsKey("loginUrl")) c.LoginUrl = ToStr(d["loginUrl"]);
            if (d.ContainsKey("logoutUrl")) c.LogoutUrl = ToStr(d["logoutUrl"]);
            int v;
            if (d.ContainsKey("idleTimeoutSeconds") && int.TryParse(ToStr(d["idleTimeoutSeconds"]), out v) && v > 0) c.IdleTimeoutSeconds = v;
            if (d.ContainsKey("pollSeconds") && int.TryParse(ToStr(d["pollSeconds"]), out v) && v > 0) c.PollSeconds = v;
        }
        catch { }
        return c;
    }

    static bool SaveConfig(string path, Config c)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"username\": " + JsStr(c.Username) + ",");
            sb.AppendLine("  \"password\": " + JsStr(c.Password) + ",");
            sb.AppendLine("  \"idleTimeoutSeconds\": " + c.IdleTimeoutSeconds + ",");
            sb.AppendLine("  \"pollSeconds\": " + c.PollSeconds + ",");
            sb.AppendLine("  \"infoUrl\": " + JsStr(c.InfoUrl) + ",");
            sb.AppendLine("  \"loginUrl\": " + JsStr(c.LoginUrl) + ",");
            sb.AppendLine("  \"logoutUrl\": " + JsStr(c.LogoutUrl));
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
