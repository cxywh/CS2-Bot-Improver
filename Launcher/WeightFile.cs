using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BotLauncher;

// Reads and writes the weights.json consumed by BotRandomizer. Unknown sections and keys are
// preserved, so a newer plugin can add settings without this editor dropping them.
internal sealed class WeightFile
{
    internal const string FileName = "weights.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private JsonObject _root;

    private WeightFile(string path, JsonObject root, bool existed)
    {
        Path = path;
        _root = root;
        Existed = existed;
    }

    internal string Path { get; }

    internal bool Existed { get; }

    internal static WeightFile Load(string path)
    {
        if (!File.Exists(path))
            return new WeightFile(path, new JsonObject(), existed: false);

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return new WeightFile(path, new JsonObject(), existed: true);

        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        using var document = JsonDocument.Parse(text, options);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("weights.json 的根节点必须是 JSON 对象。");

        var root = JsonNode.Parse(text, documentOptions: options)?.AsObject() ?? new JsonObject();
        return new WeightFile(path, root, existed: true);
    }

    internal int Get(string section, string key, int fallback)
    {
        if (_root[section] is not JsonObject group)
            return fallback;
        if (group[key] is not JsonValue value)
            return fallback;
        return value.TryGetValue<int>(out var parsed) ? parsed : fallback;
    }

    internal void Set(string section, string key, int value)
    {
        if (_root[section] is not JsonObject group)
        {
            group = new JsonObject();
            _root[section] = group;
        }

        group[key] = value;
    }

    internal void Remove(string section, string key)
    {
        if (_root[section] is JsonObject group)
            group.Remove(key);
    }

    internal void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(Path, _root.ToJsonString(WriteOptions) + Environment.NewLine);
    }

    // ---- built-in defaults, mirroring RandomizerAssets / catalog defaults ----

    internal static readonly (string Key, string Label, int Value)[] KnifeDefaults =
    [
        ("500", "刺刀 Bayonet", 7),
        ("503", "经典匕首 Classic", 3),
        ("505", "折叠刀 Flip", 4),
        ("507", "爪子刀 Karambit", 21),
        ("508", "M9 刺刀", 17),
        ("509", "猎杀者匕首 Huntsman", 3),
        ("515", "蝴蝶刀 Butterfly", 25),
        ("519", "熊刀 Ursus", 2),
        ("522", "短剑 Stiletto", 6),
        ("523", "锯齿爪刀 Talon", 5),
        ("525", "骷髅匕首 Skeleton", 7)
    ];

    internal static readonly (string Key, string Label, int Value)[] GloveDefaults =
    [
        ("5030", "运动手套 Sport", 4),
        ("5034", "专业手套 Specialist", 3),
        ("default", "其他手套家族", 2)
    ];

    internal static readonly (string Key, string Label, int Value)[] RarityDefaults =
    [
        ("consumer", "消费级 白", 1),
        ("industrial", "工业级 浅蓝", 3),
        ("milSpec", "军规级 蓝", 10),
        ("restricted", "受限 紫", 22),
        ("classified", "保密 粉", 23),
        ("covert", "隐秘 红", 40),
        ("contraband", "违禁 金", 1)
    ];

    internal static readonly (string Key, string Label, int Value)[] StickerFinishDefaults =
    [
        ("paper", "纸质 Paper", 43),
        ("glitter", "闪亮 Glitter", 8),
        ("holo", "全息 Holo", 28),
        ("foil", "闪箔 Foil", 9),
        ("gold", "金色 Gold", 11),
        ("lenticular", "透镜 Lenticular", 1)
    ];

    internal static readonly (string Key, string Label, int Value)[] StickerCraftDefaults =
    [
        ("clean", "干净无印花（低于此值）", 35),
        ("single", "1 张封顶", 47),
        ("pair", "2 张封顶", 55),
        ("three", "3 张封顶", 63),
        ("four", "4 张封顶", 88)
    ];

    internal static readonly (string Key, string Label, int Value)[] KnifeFinishDefaults =
    [
        ("Autotronic", "自动化 Autotronic", 4),
        ("Black Laminate", "黑色层压板 Black Laminate", 4),
        ("Blue Steel", "蓝钢 Blue Steel", 3),
        ("Boreal Forest", "北方森林 Boreal Forest", 3),
        ("Bright Water", "澄澈之水 Bright Water", 3),
        ("Case Hardened", "表面淬火 Case Hardened", 4),
        ("Crimson Web", "深红之网 Crimson Web", 6),
        ("Damascus Steel", "大马士革钢 Damascus Steel", 3),
        ("Doppler", "多普勒 Doppler", 10),
        ("Fade", "渐变之色 Fade", 8),
        ("Forest DDPAT", "森林 DDPAT", 3),
        ("Freehand", "自由之手 Freehand", 3),
        ("Gamma Doppler", "伽玛多普勒 Gamma Doppler", 8),
        ("Lore", "传说 Lore", 5),
        ("Marble Fade", "大理石渐变 Marble Fade", 5),
        ("Night", "夜色 Night", 3),
        ("Night Stripe", "夜色条纹 Night Stripe", 2),
        ("Rust Coat", "锈蚀 Rust Coat", 3),
        ("Safari Mesh", "狩猎网格 Safari Mesh", 2),
        ("Scorched", "烧灼 Scorched", 3),
        ("Slaughter", "屠夫 Slaughter", 6),
        ("Stained", "染色 Stained", 3),
        ("Tiger Tooth", "虎牙 Tiger Tooth", 4),
        ("Ultraviolet", "致命紫罗兰 Ultraviolet", 3),
        ("Urban Masked", "都市伪装 Urban Masked", 4)
    ];

    internal const int KeychainNumeratorDefault = 7;
    internal const int KeychainDenominatorDefault = 10;
}
