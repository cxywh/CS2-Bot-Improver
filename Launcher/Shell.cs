using System.Drawing.Drawing2D;

namespace BotLauncher;

internal abstract class Page : UserControl
{
    protected Page(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        AutoScaleMode = AutoScaleMode.None;
        Font = Theme.Body;
        DoubleBuffered = true;
    }

    internal string Title { get; }

    internal string Subtitle { get; }

    internal Cs2Env? Env { get; private set; }

    internal event Action<string, Color>? Status;

    // Raised when a page picks a different CS2 directory; the shell re-runs discovery.
    internal event Action<string>? ReloadRequested;

    internal void Refresh(Cs2Env? env)
    {
        Env = env;
        OnRefresh();
    }

    protected abstract void OnRefresh();

    protected void RequestReload(string path)
    {
        ReloadRequested?.Invoke(path);
    }

    protected void SetStatus(string message, Color color)
    {
        Status?.Invoke(message, color);
    }

    protected static Label Label(string text, Font font, Color color, int x, int y, int width = 0, int height = 0)
    {
        var label = new Label
        {
            Text = text,
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoSize = false,
            Location = new Point(x, y)
        };

        if (width > 0)
        {
            label.Size = new Size(width, height > 0 ? height : Theme.SingleLine(font, text) + 2);
        }
        else
        {
            // AutoSize relies on GDI+ preferred size and shaves the last glyph of a
            // CJK string; measure with the same TextRenderer used for painting instead.
            label.Size = new Size(Theme.TextWidth(font, text) + 12, Theme.SingleLine(font, text) + 2);
        }

        return label;
    }
}

internal sealed class MainForm : Form
{
    private static readonly (string Title, string Glyph)[] Nav =
    [
        ("概览", "◈"),
        ("模式与启动", "▷"),
        ("难度", "◆"),
        ("掉落刀具", "✦"),
        ("真人白名单", "◍"),
        ("随机权重", "≡")
    ];

    private string? _pathOverride;
    private readonly List<NavItem> _navItems = [];
    private readonly List<Page> _pages = [];
    private readonly Panel _content = new();
    private readonly Label _status = new();
    private readonly Label _path = new();
    private Panel? _header;
    private PillButton? _redetect;
    private readonly ToolTip _tip = new();
    private Cs2Env? _env;

    internal MainForm(string? pathOverride, int startPage = 0)
    {
        _pathOverride = pathOverride;
        Text = "CS2 Bot Improver Launcher";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = new Size(1000, 640);

        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Size = new Size(Math.Min(1240, work.Width - 80), Math.Min(860, work.Height - 80));
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        BuildShell();
        Reload();
        ShowPage(Math.Clamp(startPage, 0, _pages.Count - 1));


    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (!Environment.GetCommandLineArgs().Any(a => a.Equals("--dump-layout", StringComparison.OrdinalIgnoreCase)))
            return;

        var dump = new List<string> { $"form client={ClientSize.Width}x{ClientSize.Height} size={Size.Width}x{Size.Height}" };
        Dump(this, dump, 0);
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "botlauncher-layout.txt"), dump);
        Close();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var work = Screen.FromControl(this).WorkingArea;
        if (Width > work.Width - 20 || Height > work.Height - 20)
            Size = new Size(Math.Min(Width, work.Width - 20), Math.Min(Height, work.Height - 20));
        Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
    }

    private void BuildShell()
    {
        // header
        var header = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = Theme.Bg };
        header.Controls.Add(MakeLabel("CS2 BOT IMPROVER", Theme.H1, Theme.Text, 28, 20));
        header.Controls.Add(MakeLabel("启动器 · 模式 / 难度 / 掉落刀具 / 随机权重", Theme.Small, Theme.Muted, 30, 52));
        _path.Text = "正在检测 CS2 目录…";
        _path.Font = Theme.Small;
        _path.ForeColor = Theme.Dim;
        _path.AutoSize = false;
        _path.TextAlign = ContentAlignment.MiddleRight;
        _path.Location = new Point(400, 26);
        _path.Size = new Size(0, 40);
        _path.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_path);

        _redetect = new PillButton("重新检测", width: 96) { Anchor = AnchorStyles.None };
        _redetect.Click += (_, _) => Reload();
        header.Controls.Add(_redetect);
        _header = header;

        var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

        // nav
        var nav = new Panel { Dock = DockStyle.Left, Width = 212, BackColor = Theme.Bg, Padding = new Padding(14, 8, 14, 8) };
        var y = 8;
        for (var i = 0; i < Nav.Length; i++)
        {
            var index = i;
            var item = new NavItem(Nav[i].Title, Nav[i].Glyph) { Location = new Point(14, y), Width = 184 };
            item.Click += (_, _) => ShowPage(index);
            nav.Controls.Add(item);
            _navItems.Add(item);
            y += 46;
        }

        // footer
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.Bg, Padding = new Padding(28, 0, 28, 0) };
        _status.Text = "就绪";
        _status.ForeColor = Theme.Muted;
        _status.Font = Theme.Body;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        footer.Controls.Add(_status);
        var hint = new Label
        {
            Text = "改动写入前会自动备份 .bak",
            Font = Theme.Small,
            ForeColor = Theme.Dim,
            AutoSize = false,
            Dock = DockStyle.Right,
            Width = 240,
            Height = 40,
            TextAlign = ContentAlignment.MiddleRight
        };
        footer.Controls.Add(hint);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Theme.Bg;
        _content.Padding = new Padding(16, 0, 20, 0);

        Controls.Add(_content);
        Controls.Add(rule);
        Controls.Add(header);
        Controls.Add(nav);
        Controls.Add(footer);

        _pages.AddRange(new Page[]
        {
            new OverviewPage(),
            new LaunchPage(),
            new DifficultyPage(),
            new KnivesPage(),
            new WhitelistPage(),
            new WeightsPage()
        });

        foreach (var page in _pages)
        {
            page.Visible = false;
            page.Status += SetStatus;
            page.ReloadRequested += path =>
            {
                _pathOverride = path;
                Reload();
            };
            _content.Controls.Add(page);
        }

        Resize += (_, _) => PositionHeader();
        header.Resize += (_, _) => PositionHeader();
        PositionHeader();
    }

    private static string Shorten(string path)
    {
        if (path.Length <= 44)
            return path;

        var parts = path.Split(Path.DirectorySeparatorChar);
        var tail = string.Join("\\", parts.TakeLast(3));
        return "…\\" + tail;
    }

    private static Label MakeLabel(string text, Font font, Color color, int x, int y)
    {
        // Fixed measured box: AutoSize reports a preferred size short of the line box.
        return new Label
        {
            Text = text,
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoSize = false,
            Size = new Size(Theme.TextWidth(font, text) + 14, Theme.SingleLine(font, text) + 3),
            Location = new Point(x, y)
        };
    }

    private static void Dump(Control parent, List<string> lines, int depth)
    {
        foreach (Control child in parent.Controls)
        {
            var overflow = child.Bottom > parent.ClientSize.Height || child.Right > parent.ClientSize.Width;
            lines.Add($"{new string(' ', depth * 2)}{child.GetType().Name} bounds=({child.Left},{child.Top},{child.Width},{child.Height}) text=\"{child.Text}\"{(overflow ? "  <-- 溢出" : string.Empty)}");
            Dump(child, lines, depth + 1);
        }
    }

    private void PositionHeader()
    {
        if (_header is null)
            return;

        var buttonLeft = _header.Width - (_redetect?.Width ?? 96) - 28;
        _path.Location = new Point(Math.Max(300, buttonLeft - 420), 20);
        _path.Size = new Size(400, 48);

        if (_redetect is not null)
            _redetect.Location = new Point(buttonLeft, 32);
    }

    private void Reload()
    {
        _env = Cs2Env.Locate(_pathOverride);
        if (_env is not null)
        {
            try
            {
                _env.SavePath();
            }
            catch (IOException)
            {
                // 记不住路径不影响使用，下次仍会自动探测
            }

            _path.Text = Shorten(_env.CsgoDir);
            _tip.SetToolTip(_path, _env.CsgoDir);
            _path.ForeColor = Theme.Dim;
            SetStatus($"已定位 CS2：{_env.CsgoDir}", Theme.Ok);
        }
        else
        {
            _path.Text = "未找到 CS2 目录，请在概览页手动指定";
            _path.ForeColor = Theme.Bad;
            SetStatus("未找到 CS2 安装目录，点「重新检测」或在概览页浏览指定", Theme.Bad);
        }

        foreach (var page in _pages)
            page.Refresh(_env);

        PositionHeader();
    }

    internal int PageCount => _pages.Count;

    internal void ShowPage(int index)
    {
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _navItems[i].Active = i == index;
            _navItems[i].Invalidate();
        }
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }
}
