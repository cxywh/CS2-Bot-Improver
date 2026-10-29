using System.Diagnostics;

namespace BotLauncher;

internal sealed class LaunchPage : Page
{
    private readonly ChoiceCard _online = new("官方匹配 Online", "还原 gameinfo.gi 为官方匹配用的版本。自建服 / 本地人机不生效。", "gameinfo.gi");
    private readonly ChoiceCard _bots = new("本地人机 WithBots", "换成允许本地服务器与人机对局的 gameinfo.gi，插件与人机局需要这个。", "gameinfo.gi");
    private readonly PillButton _apply = new("应用模式", accent: true, width: 120);
    private readonly Label _state = Label(string.Empty, Theme.Small, Theme.Muted, 0, 0);
    private readonly PillButton _launch = new("启动 CS2（-insecure）", accent: true, width: 176);
    private readonly PillButton _openGame = new("打开游戏目录", width: 116);
    private readonly Label _options = Label(string.Empty, Theme.Body, Theme.Text, 0, 0);
    private readonly Panel _optionsCard = new();

    internal LaunchPage() : base("模式与启动", "切换 gameinfo.gi 与启动游戏；两者都只是本机文件与进程操作")
    {
        AutoScroll = true;
        Controls.Add(Label(Title, Theme.H1, Theme.Text, 24, 20));
        Controls.Add(Label(Subtitle, Theme.Small, Theme.Muted, 26, 52));

        _online.Click += (_, _) => Select(true);
        _bots.Click += (_, _) => Select(false);
        Controls.Add(_online);
        Controls.Add(_bots);
        Controls.Add(_apply);
        Controls.Add(_state);

        _apply.Click += (_, _) => Apply();
        _launch.Click += (_, _) => Launch();
        _openGame.Click += (_, _) => Open(Env?.CsgoDir);
        Controls.Add(_launch);
        Controls.Add(_openGame);

        _optionsCard.BackColor = Theme.Card;
        _optionsCard.Paint += PaintOptionsCard;
        _optionsCard.Controls.Add(_options);
        Controls.Add(_optionsCard);

        Resize += (_, _) => LayoutChildren();
    }

    protected override void OnRefresh()
    {
        var mode = Env?.CurrentMode() ?? LaunchMode.Unknown;
        _online.Selected = mode == LaunchMode.Online;
        _bots.Selected = mode == LaunchMode.WithBots;
        _online.Invalidate();
        _bots.Invalidate();

        _state.Text = Env is null ? "未定位 CS2 目录" : $"当前：{Cs2Env.ModeLabel(mode)}";
        _state.ForeColor = mode == LaunchMode.WithBots ? Theme.Ok : mode == LaunchMode.Online ? Theme.Warn : Theme.Bad;

        var options = Cs2Env.ReadLaunchOptions();
        _options.Text = options is null
            ? "读不到 Steam 的 localconfig.vdf，手动到 库 → 右键 CS2 → 属性 → 启动项 里加"
            : string.IsNullOrWhiteSpace(options)
                ? "启动项为空。要连官方匹配请保持为空；自建服务器需要 -insecure。"
                : options;
        _options.ForeColor = string.IsNullOrWhiteSpace(options) ? Theme.Muted : Theme.Text;

        LayoutChildren();
    }

    private void LayoutChildren()
    {
        var width = Math.Max(600, Width - 48);
        var y = 92;

        // Cards are stacked at full width and sized from their wrapped text.
        _online.Fit(width);
        _online.Location = new Point(24, y);
        y = _online.Bottom + 12;

        _bots.Fit(width);
        _bots.Location = new Point(24, y);
        y = _bots.Bottom + 18;

        _apply.Location = new Point(24, y);
        _state.Location = new Point(_apply.Right + 12, y + 9);
        _state.AutoSize = true;
        _state.Padding = new Padding(0, 0, 8, 0);
        y += 54;

        _launch.Location = new Point(24, y);
        _openGame.Location = new Point(_launch.Right + 10, y);
        y += 54;

        _optionsCard.Location = new Point(24, y);
        _optionsCard.Size = new Size(width, 104);
        _options.Location = new Point(20, 44);
        _options.Size = new Size(width - 40, Theme.Wrapped(_options.Font, _options.Text, width - 40) + 2);
        _options.AutoSize = false;
        AutoScrollMinSize = Size.Empty;

        Invalidate();
    }

    private void PaintOptionsCard(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.DrawCard(g, new Rectangle(0, 0, _optionsCard.Width - 1, _optionsCard.Height - 1));
        TextRenderer.DrawText(g, "STEAM 启动项（只读）· 直接 Steam Play 需要里面有 -insecure，否则 Metamod 会拒绝加载", Theme.Small, new Rectangle(20, 16, _optionsCard.Width - 40, 20), Theme.Dim,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void Select(bool online)
    {
        _online.Selected = online;
        _bots.Selected = !online;
        _online.Invalidate();
        _bots.Invalidate();
    }

    private void Apply()
    {
        if (Env is null)
        {
            SetStatus("未定位 CS2 目录", Theme.Bad);
            return;
        }

        if (Cs2Env.GameRunning())
        {
            SetStatus("CS2 正在运行，先退出游戏再切模式（退出会覆盖 gameinfo.gi）", Theme.Warn);
            return;
        }

        var target = _online.Selected ? LaunchMode.Online : LaunchMode.WithBots;
        var result = Env.SwitchMode(target);
        SetStatus(result.Message, result.Ok ? Theme.Ok : Theme.Bad);
        if (result.Ok)
            OnRefresh();
    }

    private void Launch()
    {
        if (Env is null)
        {
            SetStatus("未定位 CS2 目录，无法启动", Theme.Bad);
            return;
        }

        var result = Env.LaunchGame();
        SetStatus(result.Message, result.Ok ? Theme.Ok : Theme.Bad);
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

internal sealed class DifficultyPage : Page
{
    private readonly ChoiceCard _low = new("Low · 原版强度", "botprofile.vpk 用原版数值，反应与准度最温和。", "");
    private readonly ChoiceCard _medium = new("Medium · 加强", "中等档 botprofile，压枪与转身更稳。", "");
    private readonly ChoiceCard _high = new("High · 最强", "强档 botprofile，人机反应最快、瞄点最狠。", "");
    private readonly PillButton _apply = new("应用难度", accent: true, width: 120);
    private readonly Label _state = Label(string.Empty, Theme.Small, Theme.Muted, 0, 0);
    private readonly Label _note = Label(
        "改动的是 game\\csgo\\overrides\\botprofile.vpk 这个文件，原文件与三档样本都在 overrides 目录里，随时可切回。\r\n下一局创建人机时生效，已经生成的 bot 不会变。",
        Theme.Small, Theme.Dim, 24, 0, 700, 60);

    internal DifficultyPage() : base("难度", "三档 botprofile 覆盖，切换人机强度")
    {
        AutoScroll = true;
        Controls.Add(Label(Title, Theme.H1, Theme.Text, 24, 20));
        Controls.Add(Label(Subtitle, Theme.Small, Theme.Muted, 26, 52));

        _low.Click += (_, _) => Select(Difficulty.Low);
        _medium.Click += (_, _) => Select(Difficulty.Medium);
        _high.Click += (_, _) => Select(Difficulty.High);
        Controls.Add(_low);
        Controls.Add(_medium);
        Controls.Add(_high);

        _apply.Click += (_, _) => Apply();
        Controls.Add(_apply);
        Controls.Add(_state);
        Controls.Add(_note);

        Resize += (_, _) => LayoutChildren();
    }

    protected override void OnRefresh()
    {
        var current = Env?.CurrentDifficulty() ?? Difficulty.Unknown;
        _low.Selected = current == Difficulty.Low;
        _medium.Selected = current == Difficulty.Medium;
        _high.Selected = current == Difficulty.High;
        foreach (var card in new[] { _low, _medium, _high })
            card.Invalidate();

        _state.Text = Env is null ? "未定位 CS2 目录" : $"当前：{Cs2Env.DifficultyLabel(current)}";
        _state.ForeColor = current == Difficulty.Unknown ? Theme.Warn : Theme.Ok;

        LayoutChildren();
    }

    private void LayoutChildren()
    {
        var width = Math.Max(660, Width - 48);
        var cardWidth = (width - 32) / 3;
        const int y = 92;

        _low.Fit(cardWidth);
        _medium.Fit(cardWidth);
        _high.Fit(cardWidth);

        var cardHeight = Math.Max(_low.Height, Math.Max(_medium.Height, _high.Height));
        _low.Size = new Size(cardWidth, cardHeight);
        _medium.Size = new Size(cardWidth, cardHeight);
        _high.Size = new Size(cardWidth, cardHeight);

        _low.Location = new Point(24, y);
        _medium.Location = new Point(24 + cardWidth + 16, y);
        _high.Location = new Point(24 + (cardWidth + 16) * 2, y);

        var below = y + cardHeight + 18;
        _apply.Location = new Point(24, below);
        _state.Location = new Point(_apply.Right + 12, below + 9);
        _state.AutoSize = true;
        _state.Padding = new Padding(0, 0, 8, 0);

        _note.Location = new Point(24, below + 56);
        _note.Size = new Size(width, Theme.Wrapped(Theme.Small, _note.Text, width - 6));
        AutoScrollMinSize = Size.Empty;

        Invalidate();
    }

    private void Select(Difficulty level)
    {
        _low.Selected = level == Difficulty.Low;
        _medium.Selected = level == Difficulty.Medium;
        _high.Selected = level == Difficulty.High;
        foreach (var card in new[] { _low, _medium, _high })
            card.Invalidate();
    }

    private void Apply()
    {
        if (Env is null)
        {
            SetStatus("未定位 CS2 目录", Theme.Bad);
            return;
        }

        var level = _low.Selected ? Difficulty.Low : _medium.Selected ? Difficulty.Medium : Difficulty.High;
        var result = Env.SetDifficulty(level);
        SetStatus(result.Message, result.Ok ? Theme.Ok : Theme.Bad);
        if (result.Ok)
            OnRefresh();
    }
}
