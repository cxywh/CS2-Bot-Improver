using System.Drawing.Drawing2D;

namespace BotLauncher;

internal sealed class KnivesPage : Page
{
    private readonly TextBox _keyBox = new();
    private readonly PillButton _capture = new("点这里按下新按键", width: 150);
    private readonly PillButton _all = new("全选", width: 76);
    private readonly PillButton _none = new("清空", width: 76);
    private readonly PillButton _reload = new("重新载入", width: 96);
    private readonly PillButton _save = new("保存到 cfg", accent: true, width: 120);
    private readonly FlowLayoutPanel _chips = new();
    private readonly Label _state = Label(string.Empty, Theme.Small, Theme.Muted, 24, 0, 700, 20);
    private readonly List<Chip> _knifeChips = [];
    private bool _capturing;

    internal KnivesPage() : base("掉落刀具", "按一下绑定键就在脚下刷出选中的刀；写入的是 cfg 里的 bind 行")
    {
        Controls.Add(Label(Title, Theme.H1, Theme.Text, 24, 20));
        Controls.Add(Label(Subtitle, Theme.Small, Theme.Muted, 26, 52));

        var keyLabel = Label("绑定键", Theme.Body, Theme.Muted, 24, 92);
        Controls.Add(keyLabel);

        _keyBox.Text = "\\";
        _keyBox.Font = Theme.Mono;
        _keyBox.ReadOnly = true;
        _keyBox.TextAlign = HorizontalAlignment.Center;
        _keyBox.BackColor = Color.FromArgb(18, 22, 28);
        _keyBox.ForeColor = Theme.Text;
        _keyBox.BorderStyle = BorderStyle.None;
        _keyBox.Location = new Point(90, 88);
        _keyBox.Size = new Size(64, 30);
        _keyBox.TabStop = true;
        _keyBox.KeyDown += KeyBoxOnKeyDown;
        Controls.Add(_keyBox);

        _capture.Click += (_, _) => StartCapture();
        Controls.Add(_capture);

        _state.AutoSize = true;

        _state.Padding = new Padding(0, 0, 8, 0);
        Controls.Add(_state);

        _all.Click += (_, _) => SetAll(true);
        _none.Click += (_, _) => SetAll(false);
        _reload.Click += (_, _) => OnRefresh();
        _save.Click += (_, _) => Save();
        Controls.Add(_all);
        Controls.Add(_none);
        Controls.Add(_reload);
        Controls.Add(_save);

        _chips.FlowDirection = FlowDirection.LeftToRight;
        _chips.WrapContents = true;
        _chips.AutoScroll = true;
        _chips.BackColor = Theme.Bg;
        Controls.Add(_chips);

        foreach (var (id, name) in Cs2Env.KnifeNames)
        {
            var chip = new Chip(name, selected: false, width: 150, height: 38) { Tag = id };
            chip.Toggled += _ => UpdateStateLine();
            _knifeChips.Add(chip);
            _chips.Controls.Add(chip);
        }

        Resize += (_, _) => LayoutChildren();
    }

    protected override void OnRefresh()
    {
        var state = Env?.ReadDropKnives();
        if (state is null)
        {
            _state.Text = "未定位 CS2 目录";
            SetAll(false);
            return;
        }

        _keyBox.Text = Cs2Env.DescribeKey(state.Key);
        foreach (var chip in _knifeChips)
        {
            chip.Selected = chip.Tag is int id && state.Knives.Contains(id);
            chip.Invalidate();
        }

        UpdateStateLine();
        LayoutChildren();
    }

    private void UpdateStateLine()
    {
        var state = Env?.ReadDropKnives();
        var selected = Selected().Count;
        _state.Text = state is null
            ? "未定位 CS2 目录"
            : $"cfg 里当前：{Cs2Env.DescribeKey(state.Key)} 键 · {state.Knives.Count} 把刀 · {state.Files.Count} 个文件 · sv_cheats {(state.CheatEnabled ? "1" : "未开")} ｜ 已选 {selected} 把";
        _state.ForeColor = selected == 0 ? Theme.Warn : Theme.Muted;
    }

    private void LayoutChildren()
    {
        var width = Math.Max(620, Width - 48);

        _capture.Location = new Point(166, 84);
        _state.Location = new Point(24, 128);

        var chipsY = 156;
        _chips.Location = new Point(24, chipsY);
        _chips.Size = new Size(width, Math.Max(160, Height - chipsY - 76));

        var buttonY = Height - 56;
        _all.Location = new Point(24, buttonY);
        _none.Location = new Point(24 + 76 + 8, buttonY);
        _reload.Location = new Point(24 + (76 + 8) * 2, buttonY);
        _save.Location = new Point(width - 120 + 24, buttonY);

        Invalidate();
    }

    private void StartCapture()
    {
        _capturing = true;
        _keyBox.Text = "按键…";
        _keyBox.BackColor = Color.FromArgb(34, 48, 82);
        _keyBox.Focus();
        SetStatus("按任意键作为掉落刀具的绑定键（Esc 取消）", Theme.Accent);
    }

    private void KeyBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturing)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape)
        {
            _capturing = false;
            _keyBox.BackColor = Color.FromArgb(18, 22, 28);
            OnRefresh();
            SetStatus("已取消按键捕获", Theme.Muted);
            return;
        }

        if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        _keyBox.Text = Cs2Env.DescribeKey(Cs2Env.KeyToken(e.KeyCode));
        _keyBox.BackColor = Color.FromArgb(18, 22, 28);
        _capturing = false;
        SetStatus($"已记录绑定键 {_keyBox.Text}，点「保存到 cfg」写入", Theme.Accent);
    }

    private void SetAll(bool selected)
    {
        foreach (var chip in _knifeChips)
        {
            chip.Selected = selected;
            chip.Invalidate();
        }

        UpdateStateLine();
    }

    private List<int> Selected()
    {
        return _knifeChips.Where(c => c.Selected).Select(c => (int)c.Tag!).ToList();
    }

    private string KeyToken()
    {
        var display = _keyBox.Text.Trim();
        return display switch
        {
            "空格" => "space",
            "Tab" => "tab",
            "Shift" => "shift",
            "Ctrl" => "ctrl",
            "Alt" => "alt",
            "按键…" => "\\",
            _ => display.ToLowerInvariant()
        };
    }

    private void Save()
    {
        if (Env is null)
        {
            SetStatus("未定位 CS2 目录", Theme.Bad);
            return;
        }

        if (Cs2Env.GameRunning())
        {
            SetStatus("CS2 正在运行：cfg 已加载，改完要在控制台 exec 一次或换图才生效", Theme.Warn);
        }

        var result = Env.WriteDropKnives(KeyToken(), Selected());
        SetStatus(result.Message, result.Ok ? Theme.Ok : Theme.Bad);
        if (result.Ok)
            UpdateStateLine();
    }
}

internal sealed class WeightsPage : Page
{
    private sealed record Group(string Id, string Title, string Tab, string Note, (string Key, string Label, int Value)[] Items, bool IsKeychain = false);

    private static readonly Group[] Groups =
    [
        new("knives", "刀型权重", "刀型", "每个 bot 掷刀型时的相对权重，四项热门刀种原来合计约 70%。", WeightFile.KnifeDefaults),
        new("knifeFinish", "刀皮款式权重", "刀皮", "刀皮 finish 的相对权重，多普勒 / 伽玛多普勒 / 渐变之和决定“花刀”出现率。", WeightFile.KnifeFinishDefaults),
        new("gloves", "手套权重", "手套", "按家族加权；default 兜住除运动 / 专业之外的所有手套。", WeightFile.GloveDefaults),
        new("weaponRarity", "枪皮稀有度权重", "枪皮", "枪皮按稀有度加权，隐秘 / 保密越高越常出红粉。", WeightFile.RarityDefaults),
        new("stickerFinish", "印花 finish 权重", "印花", "印花质感权重：纸质 / 全息 / 金色 / 闪箔…", WeightFile.StickerFinishDefaults),
        new("stickerCraft", "印花数量档位", "印花档位", "阈值必须递增，数字即“低于此值”的档位边界。", WeightFile.StickerCraftDefaults),
        new("keychain", "挂件概率", "挂件", "每把枪带挂件的概率，分子 / 分母。", [], IsKeychain: true)
    ];

    private readonly List<Chip> _tabs = [];
    private readonly Panel _host = new();
    private readonly FlowLayoutPanel _rows = new();
    private readonly Label _total = Label(string.Empty, Theme.Body, Theme.Muted, 24, 0);
    private readonly PillButton _save = new("保存全部权重", accent: true, width: 140);
    private readonly PillButton _defaults = new("本组恢复默认", width: 116);
    private readonly PillButton _reload = new("重新载入", width: 96);
    private readonly List<SliderRow> _sliderRows = [];
    private readonly Dictionary<string, Dictionary<string, int>> _edits = [];
    private readonly Label _dirty = new();
    private Label? _noteLabel;
    private WeightFile? _file;
    private int _active;

    internal WeightsPage() : base("随机权重", "全部用滑块调；每行右侧是它在当前分组里的占比，改完保存后重启服务器生效")
    {
        Controls.Add(Label(Title, Theme.H1, Theme.Text, 24, 20));
        Controls.Add(Label(Subtitle, Theme.Small, Theme.Muted, 26, 52));

        var tabs = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Theme.Bg,
            Location = new Point(24, 84)
        };
        Controls.Add(tabs);

        for (var i = 0; i < Groups.Length; i++)
        {
            var index = i;
            var chip = new Chip(Groups[i].Tab, selected: i == 0, height: 32, width: 96);
            chip.Toggled += _ => ShowGroup(index);
            _tabs.Add(chip);
            tabs.Controls.Add(chip);
        }

        _tabsHost = tabs;

        _host.BackColor = Theme.Bg;
        Controls.Add(_host);

        _rows.FlowDirection = FlowDirection.TopDown;
        _rows.WrapContents = false;
        _rows.AutoScroll = true;
        _rows.BackColor = Theme.Card;
        _host.Controls.Add(_rows);

        _dirty.AutoSize = true;

        _dirty.Padding = new Padding(0, 0, 8, 0);
        _dirty.Font = Theme.Small;
        _dirty.ForeColor = Theme.Warn;
        _dirty.Text = string.Empty;

        // created with empty text, so it must re-size itself when the total changes
        _total.AutoSize = true;
        _total.Padding = new Padding(0, 0, 8, 0);
        Controls.Add(_total);
        Controls.Add(_dirty);

        _save.Click += (_, _) => Save();
        _defaults.Click += (_, _) => ApplyDefaults();
        _reload.Click += (_, _) => OnRefresh();
        Controls.Add(_save);
        Controls.Add(_defaults);
        Controls.Add(_reload);

        Resize += (_, _) => LayoutChildren();
    }

    private readonly FlowLayoutPanel _tabsHost;

    protected override void OnRefresh()
    {
        if (Env is null)
        {
            _file = null;
            BuildRows();
            SetStatus("未定位 CS2 目录", Theme.Bad);
            return;
        }

        _edits.Clear();

        try
        {
            _file = WeightFile.Load(Env.WeightPath);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            _file = null;
            SetStatus($"weights.json 读不了：{ex.Message}", Theme.Bad);
        }

        BuildRows();
    }

    private void ShowGroup(int index)
    {
        _active = index;
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Selected = i == index;
            _tabs[i].Invalidate();
        }

        BuildRows();
    }

    private void BuildRows()
    {
        _rows.SuspendLayout();
        foreach (Control control in _rows.Controls)
            control.Dispose();
        _rows.Controls.Clear();
        _sliderRows.Clear();

        var group = Groups[_active];
        var width = Math.Max(560, _rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);

        var noteWidth = Math.Max(360, width - 24);
        var note = new Label
        {
            Text = group.Note,
            Font = Theme.Small,
            ForeColor = Theme.Dim,
            AutoSize = false,
            Size = new Size(noteWidth, Theme.Wrapped(Theme.Small, group.Note, noteWidth - 6)),
            Margin = new Padding(0, 0, 0, 8),
            TextAlign = ContentAlignment.TopLeft
        };
        _noteLabel = note;
        _rows.Controls.Add(note);

        if (group.IsKeychain)
        {
            var num = ValueOf(group.Id, "chanceNumerator", WeightFile.KeychainNumeratorDefault);
            var den = ValueOf(group.Id, "chanceDenominator", WeightFile.KeychainDenominatorDefault);

            var keychainName = Theme.TextWidth(Theme.Body, "带挂件概率 · 分母") + 26;
            var rowNum = new SliderRow("chanceNumerator", "带挂件概率 · 分子", num, 1, 20) { Width = width, Margin = new Padding(0), NameWidth = keychainName };
            var rowDen = new SliderRow("chanceDenominator", "带挂件概率 · 分母", den, 1, 20) { Width = width, Margin = new Padding(0), NameWidth = keychainName };
            rowNum.Changed += row => { Record(group.Id, row.Key, row.Value); RefreshKeychainSummary(); };
            rowDen.Changed += row => { Record(group.Id, row.Key, row.Value); RefreshKeychainSummary(); };
            _sliderRows.Add(rowNum);
            _sliderRows.Add(rowDen);
        }
        else
        {
            var nameWidth = Math.Clamp(group.Items.Max(i => Theme.TextWidth(Theme.Body, i.Label)) + 26, 220, 380);
            foreach (var (key, label, fallback) in group.Items)
            {
                var itemKey = key;
                var row = new SliderRow(itemKey, label, ValueOf(group.Id, itemKey, fallback), 1, 100)
                {
                    Width = width,
                    Margin = new Padding(0),
                    NameWidth = nameWidth
                };
                row.Changed += r => { Record(group.Id, itemKey, r.Value); RefreshSummary(); };
                _sliderRows.Add(row);
            }
        }

        foreach (var row in _sliderRows)
            _rows.Controls.Add(row);

        _rows.Padding = new Padding(4, 6, 4, 10);
        UpdateDirty();
        RefreshSummary();
        _rows.ResumeLayout();
        LayoutChildren();
    }

    // Edited values live here until they are written, so switching between groups never loses
    // changes and one save commits every group at once.
    private int ValueOf(string groupId, string key, int fallback)
    {
        if (_edits.TryGetValue(groupId, out var map) && map.TryGetValue(key, out var edited))
            return edited;

        return _file?.Get(groupId, key, fallback) ?? fallback;
    }

    private void Record(string groupId, string key, int value)
    {
        if (!_edits.TryGetValue(groupId, out var map))
        {
            map = [];
            _edits[groupId] = map;
        }

        map[key] = value;
        UpdateDirty();
    }

    private void UpdateDirty()
    {
        var changed = 0;

        foreach (var (groupId, map) in _edits)
        {
            var group = Groups.First(g => g.Id == groupId);
            foreach (var (key, value) in map)
            {
                var fallback = group.IsKeychain
                    ? key == "chanceNumerator" ? WeightFile.KeychainNumeratorDefault : WeightFile.KeychainDenominatorDefault
                    : group.Items.First(item => item.Key == key).Value;

                if ((_file?.Get(groupId, key, fallback) ?? fallback) != value)
                    changed++;
            }
        }

        _dirty.Text = changed == 0 ? "没有未保存的改动" : $"有 {changed} 项改动未保存";
        _dirty.ForeColor = changed == 0 ? Theme.Dim : Theme.Warn;
    }

    private void RefreshSummary()
    {
        var group = Groups[_active];
        if (group.IsKeychain)
        {
            RefreshKeychainSummary();
            return;
        }

        var total = _sliderRows.Sum(r => r.Value);
        foreach (var row in _sliderRows)
            row.SetTotal(total);

        _total.Text = total == 0 ? "合计 0（保存会被拒绝）" : $"合计 {total} · {_sliderRows.Count} 项";
        _total.ForeColor = total == 0 ? Theme.Warn : Theme.Muted;
    }

    private void RefreshKeychainSummary()
    {
        if (_sliderRows.Count < 2)
            return;

        var num = _sliderRows[0].Value;
        var den = Math.Max(1, _sliderRows[1].Value);
        var percent = num * 100.0 / den;
        _sliderRows[0].SetTotal(den);
        _sliderRows[1].SetTotal(den);
        _total.Text = $"约 {percent:0.#}% 的枪会带挂件";
        _total.ForeColor = num > den ? Theme.Bad : Theme.Muted;
    }

    private void LayoutChildren()
    {
        var width = Math.Max(620, Width - 48);
        _tabsHost.Width = width;
        _tabsHost.Height = 40;

        var top = _tabsHost.Bottom + 12;
        _host.Location = new Point(24, top);
        _host.Size = new Size(width, Math.Max(200, Height - top - 78));
        _rows.Location = new Point(0, 0);
        _rows.Size = _host.ClientSize;

        _reload.Location = new Point(width + 24 - _reload.Width, Height - 76);
        _defaults.Location = new Point(_reload.Left - _defaults.Width - 8, Height - 76);
        _save.Location = new Point(_defaults.Left - _save.Width - 8, Height - 76);

        _total.Location = new Point(28, Height - 72);
        _dirty.Location = new Point(28, Height - 50);

        var rowWidth = Math.Max(520, _rows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
        foreach (var row in _sliderRows)
            row.Width = rowWidth;

        if (_noteLabel is not null)
            _noteLabel.Width = rowWidth;

        Invalidate();
    }

    private void ApplyDefaults()
    {
        var group = Groups[_active];
        if (group.IsKeychain)
        {
            if (_sliderRows.Count >= 2)
            {
                _sliderRows[0].Value = WeightFile.KeychainNumeratorDefault;
                _sliderRows[1].Value = WeightFile.KeychainDenominatorDefault;
            }

            RefreshKeychainSummary();
            SetStatus("挂件概率已回到默认，记得保存", Theme.Accent);
            return;
        }

        for (var i = 0; i < _sliderRows.Count && i < group.Items.Length; i++)
            _sliderRows[i].Value = group.Items[i].Value;

        RefreshSummary();
        SetStatus("本组已回到内置默认值，记得保存", Theme.Accent);
    }

    private void Save()
    {
        if (Env is null)
        {
            SetStatus("未定位 CS2 目录", Theme.Bad);
            return;
        }

        if (_file is null)
        {
            SetStatus("没有可写入的 weights.json，先点「重新载入」", Theme.Bad);
            return;
        }

        try
        {
            ValidateAll();

            var written = CommitAll(_file, _edits, Groups.Select(g => (g.Id, g.Items, g.IsKeychain)).ToArray());

            _file.Save();
            _edits.Clear();
            UpdateDirty();
            RefreshSummary();
            SetStatus($"已保存全部 {Groups.Length} 组共 {written} 项 → {_file.Path}（重启服务器或换图生效）", Theme.Ok);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetStatus($"保存被拒绝：{ex.Message}", Theme.Bad);
        }
    }

    // Writes every group in one pass: edited values win, untouched keys keep whatever the file
    // already had. Returns how many keys were written.
    internal static int CommitAll(
        WeightFile file,
        IReadOnlyDictionary<string, Dictionary<string, int>> edits,
        IReadOnlyList<(string Id, (string Key, string Label, int Value)[] Items, bool IsKeychain)> groups)
    {
        int Read(string groupId, string key, int fallback)
        {
            return edits.TryGetValue(groupId, out var map) && map.TryGetValue(key, out var edited)
                ? edited
                : file.Get(groupId, key, fallback);
        }

        var written = 0;
        foreach (var group in groups)
        {
            if (group.IsKeychain)
            {
                file.Set("keychain", "chanceNumerator", Read("keychain", "chanceNumerator", WeightFile.KeychainNumeratorDefault));
                file.Set("keychain", "chanceDenominator", Read("keychain", "chanceDenominator", WeightFile.KeychainDenominatorDefault));
                written += 2;
                continue;
            }

            foreach (var (key, _, fallback) in group.Items)
            {
                file.Set(group.Id, key, Read(group.Id, key, fallback));
                written++;
            }
        }

        return written;
    }

    // One save writes every group, so every group has to be valid first.
    private void ValidateAll()
    {
        var num = ValueOf("keychain", "chanceNumerator", WeightFile.KeychainNumeratorDefault);
        var den = ValueOf("keychain", "chanceDenominator", WeightFile.KeychainDenominatorDefault);
        if (num < 1 || den < 1 || num > den)
            throw new InvalidDataException($"挂件概率不合法（分子 {num} / 分母 {den}）");

        var craft = Groups.First(g => g.Id == "stickerCraft");
        var previous = -1;
        foreach (var (key, _, fallback) in craft.Items)
        {
            var value = ValueOf("stickerCraft", key, fallback);
            if (value <= 0)
                throw new InvalidDataException($"印花档位「{key}」必须大于 0");
            if (value < previous)
                throw new InvalidDataException("印花档位必须递增（干净 < 1 张 < 2 张 < 3 张 < 4 张）");
            previous = value;
        }
    }
}
