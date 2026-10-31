using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace BotLauncher;

internal sealed class OverviewPage : Page
{
    private readonly CheckCard _checks = new();
    private readonly Label _paths = new();
    private readonly PillButton _browse = new("指定 CS2 目录", width: 128);
    private readonly PillButton _openPlugin = new("打开插件目录", width: 116);
    private readonly PillButton _openCfg = new("打开 cfg 目录", width: 108);
    private readonly PillButton _install = new("安装 / 更新插件", accent: true, width: 152);

    internal OverviewPage() : base("概览", "插件与目录的当前状态，红点表示这一项会让插件不生效")
    {
        AutoScroll = true;
        Controls.Add(Label(Title, Theme.H1, Theme.Text, 24, 20));
        Controls.Add(Label(Subtitle, Theme.Small, Theme.Muted, 26, 52));

        _checks.Location = new Point(24, 86);
        Controls.Add(_checks);

        _paths.AutoSize = false;
        _paths.Font = Theme.Small;
        _paths.ForeColor = Theme.Dim;
        _paths.Location = new Point(26, 0);
        _paths.Size = new Size(600, 64);
        Controls.Add(_paths);

        _browse.Click += (_, _) => Browse();
        _openPlugin.Click += (_, _) => Open(Env?.PluginDir);
        _openCfg.Click += (_, _) => Open(Env?.CfgDir);
        _install.Click += (_, _) => Install();
        Controls.Add(_browse);
        Controls.Add(_openPlugin);
        Controls.Add(_openCfg);
        Controls.Add(_install);

        Resize += (_, _) => LayoutChildren();
    }

    protected override void OnRefresh()
    {
        var rows = new List<CheckRow>();

        if (Env is null)
        {
            rows.Add(new CheckRow("CS2 安装目录", "未找到，点右上「重新检测」或下面「指定 CS2 目录」", CheckState.Bad));
        }
        else
        {
            var plugin = Env.CheckPlugin();
            var core = Env.CheckCoreJson();
            var ids = Env.CheckSelfIds();
            var weights = Env.CheckWeights();
            var mode = Env.CurrentMode();
            var difficulty = Env.CurrentDifficulty();
            var knives = Env.ReadDropKnives();

            rows.Add(new CheckRow("CS2 目录", Env.CsgoDir, CheckState.Good));
            rows.Add(new CheckRow("插件本体", plugin.Detail, plugin.Ok ? CheckState.Good : CheckState.Bad));

            var launchOptions = Cs2Env.ReadLaunchOptions();
            var hasInsecure = launchOptions?.Contains("-insecure", StringComparison.OrdinalIgnoreCase) == true;
            rows.Add(new CheckRow("启动方式",
                hasInsecure
                    ? "Steam 启动项含 -insecure，Steam 直接 Play 也能加载插件"
                    : "Steam 启动项没有 -insecure：请用本启动器「启动 CS2」，直接 Steam Play 会报 Source2ServerConfig001",
                hasInsecure ? CheckState.Good : CheckState.Warn));

            rows.Add(new CheckRow("cs2.exe",
                File.Exists(Env.GameExe) ? Env.GameExe : "未找到 cs2.exe，无法带 -insecure 直启",
                File.Exists(Env.GameExe) ? CheckState.Good : CheckState.Bad));
            rows.Add(new CheckRow("core.json 服务器准则", core.Detail, core.Ok ? CheckState.Good : CheckState.Bad));
            rows.Add(new CheckRow("真人白名单", ids.Detail, ids.Ok ? CheckState.Good : CheckState.Warn));
            rows.Add(new CheckRow("随机权重文件", weights.Detail, weights.Ok ? CheckState.Good : CheckState.Warn));
            rows.Add(new CheckRow("当前模式", Cs2Env.ModeLabel(mode),
                mode == LaunchMode.WithBots ? CheckState.Good : CheckState.Warn));
            rows.Add(new CheckRow("当前人机难度", Cs2Env.DifficultyLabel(difficulty),
                difficulty == Difficulty.Unknown ? CheckState.Warn : CheckState.Good));
            rows.Add(new CheckRow("掉落刀具绑定",
                knives.Files.Count == 0
                    ? "cfg 目录里没有 my_bot_*.cfg"
                    : $"{Cs2Env.DescribeKey(knives.Key)} 键 · {knives.Knives.Count} 把刀 · {knives.Files.Count} 个 cfg · sv_cheats {(knives.CheatEnabled ? "1" : "未开")}",
                knives.Files.Count == 0 ? CheckState.Bad : knives.CheatEnabled ? CheckState.Good : CheckState.Warn));

            _paths.Text = $"插件目录  {Env.PluginDir}\r\n配置目录  {Env.CfgDir}\r\n备份目录  {Env.BackupDir}";
        }

        _checks.Rows = rows;
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        var width = Math.Max(560, Width - 48);
        _checks.Top = 86;
        _checks.Fit(width);

        var buttonsY = _checks.Bottom + 12;
        _browse.Location = new Point(24, buttonsY);
        _openPlugin.Location = new Point(_browse.Right + 10, buttonsY);
        _openCfg.Location = new Point(_openPlugin.Right + 10, buttonsY);
        _install.Location = new Point(_openCfg.Right + 10, buttonsY);
        _install.Visible = Directory.Exists(Cs2Env.PayloadDir);

        _paths.Location = new Point(26, buttonsY + 42);
        _paths.Size = new Size(width, Theme.Wrapped(Theme.Small, _paths.Text, width - 4));
        AutoScrollMinSize = Size.Empty;
        Invalidate();
    }

    private void Install()
    {
        if (Env is null)
        {
            SetStatus("未定位 CS2 目录，先点「指定 CS2 目录」", Theme.Bad);
            return;
        }

        var confirm = MessageBox.Show(FindForm(),
            $"把插件写入：\r\n{Env.PluginDir}\r\n\r\n已有的 dll 与资源会先备份到 csgo\\backup\\plugin-<时间>，weights.json 和白名单不会被覆盖。",
            "安装 / 更新插件", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK)
            return;

        var result = Env.InstallPlugin(Cs2Env.PayloadDir);
        SetStatus(result.Message, result.Ok ? Theme.Ok : Theme.Bad);
        if (result.Ok)
            OnRefresh();
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 ...\\Counter-Strike Global Offensive\\game\\csgo 目录",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        var picked = dialog.SelectedPath;
        if (!Cs2Env.LooksLikeCsgo(picked))
        {
            SetStatus($"该目录不像 CS2 的 game\\csgo：{picked}", Theme.Bad);
            return;
        }

        RequestReload(picked);
    }

    private void Open(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            SetStatus("目录不存在", Theme.Bad);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}

internal enum CheckState
{
    Good,
    Warn,
    Bad
}

internal sealed record CheckRow(string Label, string Detail, CheckState State);

internal sealed class CheckCard : Control
{
    private List<CheckRow> _rows = [];
    private int[] _rowHeights = [];

    internal CheckCard()
    {
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    internal List<CheckRow> Rows
    {
        get => _rows;
        set
        {
            _rows = value;
            Measure();
            Invalidate();
        }
    }

    // Row height is derived from the wrapped detail text.
    private const int LabelColumn = 196;

    private void Measure()
    {
        _rowHeights = new int[_rows.Count];
        for (var i = 0; i < _rows.Count; i++)
        {
            var labelHeight = Theme.SingleLine(Theme.Body, _rows[i].Label);
            var detailHeight = Theme.Wrapped(Theme.Small, _rows[i].Detail, Math.Max(160, Width - 84 - LabelColumn));
            _rowHeights[i] = Math.Max(34, Math.Max(labelHeight, detailHeight) + 12);
        }
    }

    internal void Fit(int width)
    {
        Width = width;
        Measure();
        Height = _rowHeights.Sum() + 16;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var card = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.DrawCard(g, card);

        var y = 8;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var rowHeight = i < _rowHeights.Length ? _rowHeights[i] : 38;
            var color = row.State switch
            {
                CheckState.Good => Theme.Ok,
                CheckState.Warn => Theme.Warn,
                _ => Theme.Bad
            };

            var labelHeight = Theme.SingleLine(Theme.Body, row.Label);
            Theme.DrawDot(g, new Rectangle(22, y + 6 + labelHeight / 2 - 4, 8, 8), color);

            // Label and value share one line so every check fits without scrolling.
            TextRenderer.DrawText(g, row.Label, Theme.Body, new Rectangle(44, y + 5, LabelColumn, labelHeight), Theme.Text,
                TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, row.Detail, Theme.Small,
                new Rectangle(44 + LabelColumn, y + 6, Width - 84 - LabelColumn, rowHeight - 10), Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);

            if (i < _rows.Count - 1)
            {
                using var line = new Pen(Color.FromArgb(28, 33, 42));
                g.DrawLine(line, 44, y + rowHeight - 2, Width - 24, y + rowHeight - 2);
            }

            y += rowHeight;
        }
    }
}
