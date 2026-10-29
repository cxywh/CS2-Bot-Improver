using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BotLauncher;

internal enum LaunchMode
{
    Unknown,
    Online,
    WithBots
}

internal enum Difficulty
{
    Unknown,
    Low,
    Medium,
    High
}

internal sealed record DropKnifeState(string Key, List<int> Knives, List<string> Files, bool CheatEnabled);

// Everything that touches the game install lives here. Read paths are cheap; every write keeps a
// sibling copy of the touched file so a bad click is always reversible.
internal sealed class Cs2Env
{
    internal const string PluginFolderName = "BotRandomizer";

    private static readonly int[] KnownKnives =
    [
        500, 503, 505, 506, 507, 508, 509, 512, 514, 515,
        516, 517, 518, 519, 520, 521, 522, 523, 525, 526
    ];

    internal static readonly (int Id, string Name)[] KnifeNames =
    [
        // Names are the exact strings the game ships in csgo_schinese.txt
        // (SFUI_WPNHUD_Knife*/knife_*), not community translations.
        (500, "刺刀"), (503, "海豹短刀"), (505, "折叠刀"), (506, "穿肠刀"),
        (507, "爪子刀"), (508, "M9 刺刀"), (509, "猎杀者匕首"), (512, "弯刀"),
        (514, "鲍伊猎刀"), (515, "蝴蝶刀"), (516, "暗影双匕"), (517, "系绳匕首"),
        (518, "求生匕首"), (519, "熊刀"), (520, "折刀"), (521, "流浪者匕首"),
        (522, "短剑"), (523, "锯齿爪刀"), (525, "骷髅匕首"), (526, "廓尔喀刀")
    ];

    private static readonly string[] CfgCandidates =
    [
        "my_bot_normal_config.cfg",
        "my_bot_normal_config_rules_unchanged.cfg",
        "my_bot_rush_config.cfg",
        "my_bot_rush_config_rules_unchanged.cfg",
        "my_bot_ffa_config.cfg",
        "my_bot_ffa_config_rules_unchanged.cfg"
    ];

    private Cs2Env(string csgoDir)
    {
        CsgoDir = csgoDir;
    }

    internal string CsgoDir { get; }

    // game\csgo -> game ; cs2.exe sits in game\bin\win64
    internal string GameRoot => Directory.GetParent(CsgoDir)!.FullName;

    internal string CfgDir => Path.Combine(CsgoDir, "cfg");

    internal string OverridesDir => Path.Combine(CsgoDir, "overrides");

    internal string BackupDir => Path.Combine(CsgoDir, "backup");

    internal string PluginDir => Path.Combine(CsgoDir, "addons", "counterstrikesharp", "plugins", PluginFolderName);

    internal string WeightPath => Path.Combine(PluginDir, "weights.json");

    internal string SelfIdsPath => Path.Combine(PluginDir, "self_steamids.txt");

    internal string CoreJsonPath => Path.Combine(CsgoDir, "addons", "counterstrikesharp", "configs", "core.json");

    // ---- self whitelist -------------------------------------------------

    /// <summary>Steam install root, used for loginusers.vdf and localconfig.vdf.</summary>
    internal string? SteamInstallRoot
    {
        get
        {
            var dir = new DirectoryInfo(CsgoDir);
            for (var i = 0; i < 5 && dir is not null; i++)
                dir = dir.Parent;

            if (dir is not null && File.Exists(Path.Combine(dir.FullName, "config", "loginusers.vdf")))
                return dir.FullName;

            foreach (var key in new[] { @"Software\Valve\Steam", @"Software\WOW6432Node\Valve\Steam" })
            {
                var value = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                    return value.Replace('/', '\\');
            }

            return SteamRoot();
        }
    }

    /// <summary>Plugin files shipped next to the exe, used by the one-click installer.</summary>
    internal static string PayloadDir => Path.Combine(AppContext.BaseDirectory, "payload");

    internal static readonly string[] PayloadFiles =
    [
        "BotRandomizer.dll",
        "BotRandomizer.deps.json",
        "cosmetic_catalog.json",
        "charm_placements.json"
    ];

    /// <summary>User-owned files: copied only when the target does not have them yet.</summary>
    internal static readonly string[] PayloadUserFiles =
    [
        "weights.json",
        "self_steamids.txt"
    ];

    internal sealed record InstallResult(bool Ok, string Message, int Copied, int Kept);

    internal InstallResult InstallPlugin(string sourceDir)
    {
        if (!Directory.Exists(sourceDir))
            return new InstallResult(false, $"找不到插件文件目录 {sourceDir}", 0, 0);

        var missing = PayloadFiles.Where(f => !File.Exists(Path.Combine(sourceDir, f))).ToArray();
        if (missing.Length > 0)
            return new InstallResult(false, $"插件目录缺少 {string.Join("、", missing)}", 0, 0);

        Directory.CreateDirectory(PluginDir);

        // anything we are about to overwrite gets a timestamped backup first
        var replaced = PayloadFiles.Concat(PayloadUserFiles)
            .Select(f => Path.Combine(PluginDir, f))
            .Where(File.Exists)
            .ToArray();

        if (replaced.Length > 0)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backup = Path.Combine(BackupDir, "plugin-" + stamp);
            Directory.CreateDirectory(backup);
            foreach (var file in replaced)
                File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), overwrite: true);
        }

        var copied = 0;
        var kept = 0;
        foreach (var name in PayloadFiles)
        {
            File.Copy(Path.Combine(sourceDir, name), Path.Combine(PluginDir, name), overwrite: true);
            copied++;
        }

        foreach (var name in PayloadUserFiles)
        {
            var target = Path.Combine(PluginDir, name);
            if (File.Exists(target))
            {
                kept++;
                continue;
            }

            var source = Path.Combine(sourceDir, name);
            if (File.Exists(source))
            {
                File.Copy(source, target);
                copied++;
            }
        }

        return new InstallResult(true,
            $"插件已安装：写入 {copied} 个文件" + (kept > 0 ? $"，保留 {kept} 个已有配置" : string.Empty) +
            "（重启服务器或换图生效）", copied, kept);
    }

    internal List<SteamAccount> ReadSteamAccounts()
    {
        var result = new List<SteamAccount>();
        var root = SteamInstallRoot;
        if (root is null)
            return result;

        var vdf = Path.Combine(root, "config", "loginusers.vdf");
        if (!File.Exists(vdf))
            return result;

        string? currentId = null;
        var depth = 0;
        string account = string.Empty, persona = string.Empty;
        var mostRecent = false;

        foreach (var raw in File.ReadAllLines(vdf))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (line == "{")
            {
                depth++;
                continue;
            }

            if (line == "}")
            {
                if (currentId is not null && depth == 2)
                {
                    result.Add(new SteamAccount(currentId, persona.Length > 0 ? persona : account, account, mostRecent));
                    currentId = null;
                }

                depth--;
                continue;
            }

            var parts = SplitQuoted(line);
            if (parts.Count == 1 && depth == 1 && parts[0].Length >= 17 && parts[0].All(char.IsDigit))
            {
                currentId = parts[0];
                account = string.Empty;
                persona = string.Empty;
                mostRecent = false;
                continue;
            }

            if (currentId is null || parts.Count != 2)
                continue;

            switch (parts[0])
            {
                case "AccountName":
                    account = parts[1];
                    break;
                case "PersonaName":
                    persona = parts[1];
                    break;
                case "MostRecent":
                    mostRecent = parts[1] == "1";
                    break;
            }
        }

        return result;
    }

    private static List<string> SplitQuoted(string line)
    {
        var parts = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] != '"')
            {
                i++;
                continue;
            }

            var end = line.IndexOf('"', i + 1);
            if (end < 0)
                break;

            parts.Add(line[(i + 1)..end]);
            i = end + 1;
        }

        return parts;
    }

    internal List<(string Id, string Note)> ReadSelfIds()
    {
        var entries = new List<(string Id, string Note)>();
        if (!File.Exists(SelfIdsPath))
            return entries;

        foreach (var raw in File.ReadAllLines(SelfIdsPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or '/')
                continue;

            var note = string.Empty;
            var slash = line.IndexOf("//", StringComparison.Ordinal);
            if (slash >= 0)
            {
                note = line[(slash + 2)..].Trim();
                line = line[..slash].Trim();
            }

            // only real SteamID64 values; anything else is treated as a comment line
            if (line.Length is < 17 or > 18 || !line.All(char.IsDigit))
                continue;

            entries.Add((line, note));
        }

        return entries;
    }

    internal void SaveSelfIds(IEnumerable<(string Id, string Note)> entries)
    {
        var lines = new List<string>
        {
            "# 只有列在这里的真人 SteamID 会和 bot 一样穿上随机饰品",
            "# 一行一个 SteamID64（17 位），// 后面是注释；由 BotLauncher 生成"
        };

        foreach (var (id, note) in entries)
            lines.Add(note.Length > 0 ? $"{id} // {note}" : id);

        Directory.CreateDirectory(Path.GetDirectoryName(SelfIdsPath)!);
        File.WriteAllLines(SelfIdsPath, lines, new System.Text.UTF8Encoding(false));
    }


    internal string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CS2BotLauncher", "settings.json");

    // ---- discovery ----

    internal static Cs2Env? Locate(string? overridePath = null)
    {
        foreach (var candidate in EnumerateCandidates(overridePath))
        {
            if (LooksLikeCsgo(candidate))
                return new Cs2Env(Path.GetFullPath(candidate));
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            yield return overridePath!;

        foreach (var saved in ReadSavedPaths())
            yield return saved;

        yield return @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo";
        yield return @"C:\Program Files\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo";

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                continue;

            foreach (var root in new[] { "Steam", "SteamLibrary", "Games", "Program Files (x86)\\Steam" })
            {
                yield return Path.Combine(drive.Name, root, "steamapps", "common",
                    "Counter-Strike Global Offensive", "game", "csgo");
            }

            foreach (var library in ReadLibraryFolders(Path.Combine(drive.Name, "Steam", "steamapps", "libraryfolders.vdf")))
                yield return Path.Combine(library, "steamapps", "common", "Counter-Strike Global Offensive", "game", "csgo");
        }
    }

    private static IEnumerable<string> ReadLibraryFolders(string vdf)
    {
        if (!File.Exists(vdf))
            yield break;

        string text;
        try
        {
            text = File.ReadAllText(vdf);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            yield return match.Groups[1].Value.Replace("\\\\", "\\");
    }

    internal static bool LooksLikeCsgo(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return false;

        try
        {
            return File.Exists(Path.Combine(dir, "gameinfo.gi")) ||
                   Directory.Exists(Path.Combine(dir, "addons")) ||
                   Directory.Exists(Path.Combine(dir, "cfg"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ReadSavedPaths()
    {
        var store = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CS2BotLauncher", "settings.json");

        if (!File.Exists(store))
            yield break;

        string? saved = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(store));
            if (document.RootElement.TryGetProperty("csgo", out var value))
                saved = value.GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            saved = null;
        }

        if (!string.IsNullOrWhiteSpace(saved))
            yield return saved!;
    }

    internal void SavePath()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(new { csgo = CsgoDir }, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---- mode ----

    internal LaunchMode CurrentMode()
    {
        var current = Path.Combine(CsgoDir, "gameinfo.gi");
        if (!File.Exists(current))
            return LaunchMode.Unknown;

        var hash = Hash(current);
        if (File.Exists(Path.Combine(BackupDir, "Online", "gameinfo.gi")) &&
            Hash(Path.Combine(BackupDir, "Online", "gameinfo.gi")) == hash)
            return LaunchMode.Online;

        if (File.Exists(Path.Combine(BackupDir, "WithBots", "gameinfo.gi")) &&
            Hash(Path.Combine(BackupDir, "WithBots", "gameinfo.gi")) == hash)
            return LaunchMode.WithBots;

        return LaunchMode.Unknown;
    }

    internal (bool Ok, string Message) SwitchMode(LaunchMode mode)
    {
        if (mode == LaunchMode.Unknown)
            return (false, "未知模式");

        var source = Path.Combine(BackupDir, mode == LaunchMode.Online ? "Online" : "WithBots", "gameinfo.gi");
        if (!File.Exists(source))
            return (false, $"缺少备份 {source}");

        var target = Path.Combine(CsgoDir, "gameinfo.gi");
        CopyWithBackup(source, target, "gameinfo");
        return (true, mode == LaunchMode.Online ? "已切到官方匹配模式，重启游戏生效" : "已切到人机模式，重启游戏生效");
    }

    // ---- difficulty ----

    internal Difficulty CurrentDifficulty()
    {
        var target = Path.Combine(OverridesDir, "botprofile.vpk");
        if (!File.Exists(target))
            return Difficulty.Unknown;

        var hash = Hash(target);
        foreach (var level in new[] { Difficulty.Low, Difficulty.Medium, Difficulty.High })
        {
            var source = Path.Combine(OverridesDir, level.ToString(), "botprofile.vpk");
            if (File.Exists(source) && Hash(source) == hash)
                return level;
        }

        return Difficulty.Unknown;
    }

    internal (bool Ok, string Message) SetDifficulty(Difficulty level)
    {
        if (level == Difficulty.Unknown)
            return (false, "未知难度");

        var source = Path.Combine(OverridesDir, level.ToString(), "botprofile.vpk");
        if (!File.Exists(source))
            return (false, $"缺少 {source}");

        CopyWithBackup(source, Path.Combine(OverridesDir, "botprofile.vpk"), "botprofile");
        return (true, $"已应用 {level} 难度，下一局人机生效");
    }

    // ---- drop knives ----

    internal List<string> CfgFiles()
    {
        var list = new List<string>();
        foreach (var name in CfgCandidates)
        {
            var path = Path.Combine(CfgDir, name);
            if (File.Exists(path))
                list.Add(path);
        }

        return list;
    }

    internal DropKnifeState ReadDropKnives()
    {
        var files = CfgFiles();
        var key = "\\";
        var knives = new List<int>();
        var cheats = false;

        foreach (var file in files)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("sv_cheats", StringComparison.OrdinalIgnoreCase) &&
                    trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() == "1")
                    cheats = true;

                if (!TryParseBind(trimmed, out var parsedKey, out var parsedIds))
                    continue;

                key = parsedKey;
                knives = parsedIds;
            }
        }

        return new DropKnifeState(key, knives, files, cheats);
    }

    private static readonly Regex BindPattern = new(
        "^bind\\s+(?<key>\"[^\"]+\"|\\S+)\\s+\"(?<body>[^\"]*subclass_create[^\"]*)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool TryParseBind(string line, out string key, out List<int> knives)
    {
        key = string.Empty;
        knives = [];

        var match = BindPattern.Match(line);
        if (!match.Success)
            return false;

        key = match.Groups["key"].Value.Trim('"');
        foreach (Match id in Regex.Matches(match.Groups["body"].Value, "subclass_create\\s+(\\d+)"))
        {
            if (int.TryParse(id.Groups[1].Value, out var parsed) && !knives.Contains(parsed))
                knives.Add(parsed);
        }

        return true;
    }

    internal (bool Ok, string Message) WriteDropKnives(string key, IReadOnlyList<int> knives)
    {
        var files = CfgFiles();
        if (files.Count == 0)
            return (false, "cfg 目录里找不到 my_bot_*.cfg");

        if (string.IsNullOrWhiteSpace(key))
            return (false, "按键为空");

        var selected = knives.Where(id => KnownKnives.Contains(id)).Distinct().ToList();

        var written = 0;
        foreach (var file in files)
        {
            if (RewriteCfgFile(file, key, selected))
                written++;
        }

        return written == 0
            ? (false, "没有找到可下的 bind subclass_create 行")
            : (true, $"已写入 {written} 个 cfg：{selected.Count} 把刀，绑定键 {DescribeKey(key)}");
    }

    // Rewrites one cfg in place: the existing "bind <key> subnet..." line keeps its position,
    // every other line is byte-for-byte preserved. Returns false when the file has no such line.
    internal static bool RewriteCfgFile(string file, string key, IReadOnlyList<int> knives)
    {
        var lines = File.ReadAllLines(file).ToList();
        var bindIndex = lines.FindIndex(l => BindPattern.IsMatch(l.TrimStart()));
        if (bindIndex < 0)
            return false;

        var body = knives.Count == 0
            ? string.Empty
            : string.Join(";", knives.Select(id => $"subclass_create {id}"));

        lines[bindIndex] = knives.Count == 0 ? $"bind {key} \"\"" : $"bind {key} \"{body}\"";

        var backup = file + ".editbak";
        if (!File.Exists(backup))
            File.Copy(file, backup);

        File.WriteAllLines(file, lines);
        return true;
    }

    // ---- launch ----

    internal static string? ReadLaunchOptions()
    {
        var steamRoot = SteamRoot();
        if (steamRoot is null)
            return null;

        var userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata))
            return null;

        foreach (var userDir in Directory.GetDirectories(userdata))
        {
            var config = Path.Combine(userDir, "config", "localconfig.vdf");
            if (!File.Exists(config))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(config);
            }
            catch (IOException)
            {
                continue;
            }

            var found = LaunchOptionsFromVdf(text);
            if (found is not null)
                return found;
        }

        return null;
    }

    // localconfig.vdf holds several "730" blocks; only the one that actually contains
    // LaunchOptions matters, so walk the braces instead of regexing the whole file.
    internal static string? LaunchOptionsFromVdf(string text)
    {
        const string needle = "\"730\"";
        var index = text.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            var open = text.IndexOf('{', index);
            if (open < 0)
                return null;

            var depth = 0;
            var close = -1;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '{')
                    depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
            }

            if (close < 0)
                return null;

            var block = text.Substring(open, close - open);
            var match = Regex.Match(block, "\"LaunchOptions\"\\s+\"([^\"]*)\"");
            if (match.Success)
                return match.Groups[1].Value;

            index = text.IndexOf(needle, close, StringComparison.Ordinal);
        }

        return null;
    }

    internal static string? SteamRoot()
    {
        foreach (var candidate in new[]
                 {
                     @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam"
                 })
        {
            if (File.Exists(Path.Combine(candidate, "steam.exe")))
                return candidate;
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                continue;

            var candidate = Path.Combine(drive.Name, "Steam");
            if (File.Exists(Path.Combine(candidate, "steam.exe")))
                return candidate;
        }

        return null;
    }

    // cs2.exe lives in game\bin\win64, one level above game\csgo
    internal string GameExe => Path.Combine(GameRoot, "bin", "win64", "cs2.exe");

    // Metamod (which CounterStrikeSharp needs) refuses to load unless the command line carries
    // -insecure or -dedicated. Launching through Steam Play without that flag aborts the game with
    // "CAppSystemDict: Unable to create interface Source2ServerConfig001", so the exe is started
    // here with the flag attached instead of going through steam://rungameid/730.
    internal (bool Ok, string Message) LaunchGame()
    {
        if (GameRunning())
            return (false, "CS2 已经在运行了");

        try
        {
            if (File.Exists(GameExe))
            {
                Process.Start(new ProcessStartInfo(GameExe)
                {
                    Arguments = "-insecure",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(GameExe)!
                });
                return (true, "已启动 CS2（-insecure，Metamod / 插件才会加载）");
            }

            Process.Start(new ProcessStartInfo("steam://rungameid/730") { UseShellExecute = true });
            return (false, "没找到 cs2.exe，改走 Steam 启动；Steam 启动项里必须自己加 -insecure，否则会报 Source2ServerConfig001");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return (false, $"启动失败：{ex.Message}");
        }
    }

    internal static bool GameRunning()
    {
        try
        {
            return Process.GetProcessesByName("cs2").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // ---- plugin checks ----

    internal (bool Ok, string Detail) CheckPlugin()
    {
        var dll = Path.Combine(PluginDir, "BotRandomizer.dll");
        if (!File.Exists(dll))
            return (false, $"未找到 {dll}");

        var catalog = Path.Combine(PluginDir, "cosmetic_catalog.json");
        if (!File.Exists(catalog))
            return (false, "缺少 cosmetic_catalog.json");

        return (true, $"BotRandomizer.dll {new FileInfo(dll).Length / 1024} KB");
    }

    internal (bool Ok, string Detail) CheckCoreJson()
    {
        if (!File.Exists(CoreJsonPath))
            return (false, "未找到 core.json");

        var text = File.ReadAllText(CoreJsonPath);
        var match = Regex.Match(text, "\"FollowCS2ServerGuidelines\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return (false, "core.json 未包含该字段");

        return match.Groups[1].Value.Equals("false", StringComparison.OrdinalIgnoreCase)
            ? (true, "FollowCS2ServerGuidelines = false")
            : (false, "FollowCS2ServerGuidelines = true（插件会被限制）");
    }

    internal (bool Ok, string Detail) CheckSelfIds()
    {
        if (!File.Exists(SelfIdsPath))
            return (false, "未找到 self_steamids.txt");

        var ids = File.ReadAllLines(SelfIdsPath)
            .Select(line => line.Split("//")[0].Trim())
            .Count(line => line.Length > 0);

        return ids > 0
            ? (true, $"{ids} 个 SteamID 白名单")
            : (false, "白名单为空，只有 bot 会换装");
    }

    internal (bool Ok, string Detail) CheckWeights()
    {
        if (!File.Exists(WeightPath))
            return (false, "未找到 weights.json（插件将用内置权重）");

        var size = new FileInfo(WeightPath).Length;
        return (true, $"weights.json {size} B");
    }

    // ---- helpers ----

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(MD5.HashData(stream));
    }

    private static void CopyWithBackup(string source, string target, string suffix)
    {
        if (File.Exists(target))
        {
            var backup = $"{target}.{suffix}.bak";
            if (!File.Exists(backup))
                File.Copy(target, backup, overwrite: false);
        }

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.Copy(source, target, overwrite: true);
    }

    internal static string KeyToken(Keys key)
    {
        return key switch
        {
            Keys.Oem5 or Keys.OemBackslash => "\\",
            Keys.Oemtilde => "`",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.Oem6 => "]",
            Keys.Oem1 => ";",
            Keys.Oem7 => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Space => "space",
            Keys.Tab => "tab",
            Keys.Enter => "enter",
            Keys.Back => "backspace",
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => $"kp_{(key - Keys.NumPad0)}",
            >= Keys.A and <= Keys.Z => ((char)('a' + (key - Keys.A))).ToString(),
            >= Keys.F1 and <= Keys.F12 => $"f{key - Keys.F1 + 1}",
            Keys.LShiftKey or Keys.ShiftKey => "shift",
            Keys.LControlKey or Keys.ControlKey => "ctrl",
            Keys.LMenu or Keys.Menu => "alt",
            _ => key.ToString().ToLowerInvariant()
        };
    }

    internal static string DescribeKey(string token)
    {
        return token switch
        {
            "\\" => "\\",
            "space" => "空格",
            "tab" => "Tab",
            "shift" => "Shift",
            "ctrl" => "Ctrl",
            "alt" => "Alt",
            _ => token.ToUpperInvariant()
        };
    }

    internal static string ModeLabel(LaunchMode mode) => mode switch
    {
        LaunchMode.Online => "官方匹配 (Online)",
        LaunchMode.WithBots => "本地人机 (WithBots)",
        _ => "未识别（gameinfo.gi 与备份都不匹配）"
    };

    internal static string DifficultyLabel(Difficulty level) => level switch
    {
        Difficulty.Low => "Low · 原版强度",
        Difficulty.Medium => "Medium · 加强",
        Difficulty.High => "High · 最强",
        _ => "未识别"
    };
}

internal sealed record SteamAccount(string SteamId, string Persona, string Account, bool MostRecent);
