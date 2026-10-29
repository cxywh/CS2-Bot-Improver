namespace BotLauncher;

// Lets someone install the plugin without hand-editing self_steamids.txt: pick a Steam
// account from loginusers.vdf, or paste an ID, then save.
internal sealed class WhitelistPage : Page
{
    private readonly ListBox _list = new();
    private readonly Label _hint = new();
    private readonly PillButton _addCurrent = new("添加当前账号", accent: true, width: 132);
    private readonly PillButton _addAll = new("添加全部账号", width: 120);
    private readonly PillButton _addManual = new("手动输入 ID", width: 112);
    private readonly PillButton _remove = new("删除选中", width: 104);
    private readonly PillButton _save = new("保存白名单", accent: true, width: 116);
    private readonly List<(string Id, string Note)> _entries = [];
    private List<SteamAccount> _accounts = [];

    internal WhitelistPage() : base("真人白名单", "只有列在这里的 SteamID 会像人机一样随机换装；填自己或队友，不用把整队写进来")
    {
        Controls.Add(Label(Title, Theme.H1, Theme.Text, 24, 20));
        Controls.Add(Label(Subtitle, Theme.Small, Theme.Muted, 26, 52));

        _list.BackColor = Theme.Card;
        _list.ForeColor = Theme.Text;
        _list.BorderStyle = BorderStyle.None;
        _list.Font = Theme.Body;
        _list.IntegralHeight = false;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 34;
        _list.DrawItem += DrawRow;
        Controls.Add(_list);

        _hint.Font = Theme.Small;
        _hint.ForeColor = Theme.Dim;
        _hint.AutoSize = false;
        Controls.Add(_hint);

        _addCurrent.Click += (_, _) => AddCurrent();
        _addAll.Click += (_, _) => AddAll();
        _addManual.Click += (_, _) => AddManual();
        _remove.Click += (_, _) => RemoveSelected();
        _save.Click += (_, _) => Save();

        Controls.Add(_addCurrent);
        Controls.Add(_addAll);
        Controls.Add(_addManual);
        Controls.Add(_remove);
        Controls.Add(_save);

        Resize += (_, _) => LayoutChildren();
    }

    protected override void OnRefresh()
    {
        _accounts = Env?.ReadSteamAccounts() ?? [];

        _entries.Clear();
        if (Env is not null)
            _entries.AddRange(Env.ReadSelfIds());

        Refill();

        _hint.Text = Env is null
            ? "未定位 CS2 目录，先到「概览」指定 game\\csgo"
            : _accounts.Count == 0
                ? "没读到 Steam 账号（Steam 没登录过？），可以点「手动输入 ID」"
                : $"读到 {_accounts.Count} 个 Steam 账号：{string.Join(" / ", _accounts.Select(a => a.Persona))}";

        LayoutChildren();
    }

    private void LayoutChildren()
    {
        var width = Math.Max(560, Width - 48);
        _list.Location = new Point(24, 92);
        _list.Size = new Size(width, Math.Max(180, Height - 268));

        _hint.Location = new Point(26, _list.Bottom + 10);
        _hint.Size = new Size(width, Theme.Wrapped(Theme.Small, _hint.Text, width - 6));

        var y = _hint.Bottom + 12;
        _addCurrent.Location = new Point(24, y);
        _addAll.Location = new Point(_addCurrent.Right + 10, y);
        _addManual.Location = new Point(_addAll.Right + 10, y);

        y += 44;
        _remove.Location = new Point(24, y);
        _save.Location = new Point(_remove.Right + 10, y);
    }

    private void Refill()
    {
        var selected = _list.SelectedIndex;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var (id, note) in _entries)
            _list.Items.Add(note.Length > 0 ? $"{id}    {note}" : id);
        _list.EndUpdate();

        if (_list.Items.Count > 0)
            _list.SelectedIndex = Math.Clamp(selected < 0 ? 0 : selected, 0, _list.Items.Count - 1);
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
            return;

        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var back = new SolidBrush(isSelected ? Color.FromArgb(30, 40, 62) : (e.Index % 2 == 0 ? Theme.Card : Color.FromArgb(19, 23, 29))))
            e.Graphics.FillRectangle(back, e.Bounds);

        var text = _list.Items[e.Index]?.ToString() ?? string.Empty;
        TextRenderer.DrawText(e.Graphics, text, Theme.Body,
            new Rectangle(e.Bounds.Left + 14, e.Bounds.Top, e.Bounds.Width - 20, e.Bounds.Height),
            isSelected ? Theme.Text : Color.FromArgb(206, 214, 226),
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void AddCurrent()
    {
        var current = _accounts.FirstOrDefault(a => a.MostRecent) ?? _accounts.FirstOrDefault();
        if (current is null)
        {
            SetStatus("没读到 Steam 账号，用「手动输入 ID」粘贴 SteamID64", Theme.Warn);
            return;
        }

        AddEntry(current.SteamId, current.Persona);
    }

    private void AddAll()
    {
        if (_accounts.Count == 0)
        {
            SetStatus("没读到 Steam 账号", Theme.Warn);
            return;
        }

        foreach (var account in _accounts)
            AddEntry(account.SteamId, account.Persona, quiet: true);

        SetStatus($"已加入 {_accounts.Count} 个账号，记得点「保存白名单」", Theme.Ok);
    }

    private void AddEntry(string id, string note, bool quiet = false)
    {
        if (_entries.Any(e => e.Id == id))
        {
            SetStatus($"{id} 已在白名单里", Theme.Muted);
            return;
        }

        _entries.Add((id, note));
        Refill();
        _list.SelectedIndex = _list.Items.Count - 1;
        if (!quiet)
            SetStatus($"已加入 {id}，点「保存白名单」写入", Theme.Ok);
    }

    private void AddManual()
    {
        using var prompt = new PromptForm("手动添加 SteamID", "17 位 SteamID64，例如 76561198000000001", string.Empty, textMode: true);
        if (prompt.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        var input = prompt.TextResult;
        if (input.Length is < 17 or > 18 || !input.All(char.IsDigit))
        {
            SetStatus("SteamID64 是 17 位数字，可以在 Steam 个人资料链接里看到", Theme.Bad);
            return;
        }

        var known = _accounts.FirstOrDefault(a => a.SteamId == input);
        AddEntry(input, known?.Persona ?? string.Empty);
    }

    private void RemoveSelected()
    {
        var index = _list.SelectedIndex;
        if (index < 0 || index >= _entries.Count)
        {
            SetStatus("先在列表里选一行", Theme.Muted);
            return;
        }

        var removed = _entries[index];
        _entries.RemoveAt(index);
        Refill();
        SetStatus($"已移除 {removed.Id}，点「保存白名单」写入", Theme.Warn);
    }

    private void Save()
    {
        if (Env is null)
        {
            SetStatus("未定位 CS2 目录", Theme.Bad);
            return;
        }

        try
        {
            Env.SaveSelfIds(_entries);
            SetStatus($"已保存 {_entries.Count} 个 SteamID 到 self_steamids.txt（重启服务器或换图生效）", Theme.Ok);
            OnRefresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"写入失败：{ex.Message}", Theme.Bad);
        }
    }
}
