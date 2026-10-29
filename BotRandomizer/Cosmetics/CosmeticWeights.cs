using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BotRandomizer;

// Optional tuning file (weights.json) that overrides the compiled-in roll weights.
// Every section and every key is optional: anything missing falls back to the built-in
// values, and any value that is not a positive integer is ignored for that key only.
internal sealed class CosmeticWeights
{
    internal static readonly CosmeticWeights Default = new();

    private IReadOnlyDictionary<ushort, int> _knives = new Dictionary<ushort, int>();
    private IReadOnlyDictionary<ushort, int> _gloves = new Dictionary<ushort, int>();
    private IReadOnlyDictionary<CosmeticRarity, int> _rarity = new Dictionary<CosmeticRarity, int>();
    private IReadOnlyDictionary<StickerFinish, int> _stickerFinish = new Dictionary<StickerFinish, int>();
    private IReadOnlyDictionary<string, int> _knifeFinish =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    internal int? GloveDefault { get; private init; }
    internal int CleanCraft { get; private init; } = 35;
    internal int SingleCraft { get; private init; } = 47;
    internal int PairCraft { get; private init; } = 55;
    internal int ThreeCraft { get; private init; } = 63;
    internal int FourCraft { get; private init; } = 88;
    internal int KeychainNumerator { get; private init; } = 7;
    internal int KeychainDenominator { get; private init; } = 10;

    internal int KnifeWeight(KnifeDefinition knife)
        => _knives.TryGetValue(knife.DefIndex, out var weight) ? weight : knife.Weight;

    internal int GloveWeight(ushort defIndex)
        => _gloves.TryGetValue(defIndex, out var weight)
            ? weight
            : GloveDefault ?? RandomizerAssets.GetGloveVariantWeight(defIndex);

    internal int WeaponRarityWeight(CosmeticRarity rarity)
        => _rarity.TryGetValue(rarity, out var weight)
            ? weight
            : RandomizerAssets.GetWeaponRarityWeight(rarity);

    internal int StickerFinishWeight(StickerFinish finish)
        => _stickerFinish.TryGetValue(finish, out var weight)
            ? weight
            : RandomizerAssets.GetStickerFinishWeight(finish);

    internal int KnifeFinishWeight(string finish, int catalogWeight)
        => _knifeFinish.TryGetValue(finish, out var weight) ? weight : catalogWeight;

    internal static CosmeticWeights Load(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            logger.LogInformation(
                "[BotRandomizer] weights.json not found; using built-in roll weights");
            return Default;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("weights.json must contain a JSON object.");

            var knives = new Dictionary<ushort, int>();
            foreach (var (key, value) in ReadNumbers(root, "knives"))
            {
                if (ushort.TryParse(key, out var defIndex))
                    knives[defIndex] = value;
            }

            var gloves = new Dictionary<ushort, int>();
            int? gloveDefault = null;
            foreach (var (key, value) in ReadNumbers(root, "gloves"))
            {
                if (key.Equals("default", StringComparison.OrdinalIgnoreCase))
                    gloveDefault = value;
                else if (ushort.TryParse(key, out var defIndex))
                    gloves[defIndex] = value;
            }

            var rarity = new Dictionary<CosmeticRarity, int>();
            foreach (var (key, value) in ReadNumbers(root, "weaponRarity"))
            {
                if (Enum.TryParse<CosmeticRarity>(key, ignoreCase: true, out var parsed))
                    rarity[parsed] = value;
            }

            var stickerFinish = new Dictionary<StickerFinish, int>();
            foreach (var (key, value) in ReadNumbers(root, "stickerFinish"))
            {
                if (Enum.TryParse<StickerFinish>(key, ignoreCase: true, out var parsed))
                    stickerFinish[parsed] = value;
            }

            var knifeFinish = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in ReadNumbers(root, "knifeFinish"))
                knifeFinish[key] = value;

            var craft = ReadNumbers(root, "stickerCraft").ToDictionary(pair => pair.Key, pair => pair.Value);
            var keychain = ReadNumbers(root, "keychain").ToDictionary(pair => pair.Key, pair => pair.Value);

            var weights = new CosmeticWeights
            {
                _knives = knives,
                _gloves = gloves,
                GloveDefault = gloveDefault,
                _rarity = rarity,
                _stickerFinish = stickerFinish,
                _knifeFinish = knifeFinish,
                CleanCraft = CraftValue(craft, "clean", 35),
                SingleCraft = CraftValue(craft, "single", 47),
                PairCraft = CraftValue(craft, "pair", 55),
                ThreeCraft = CraftValue(craft, "three", 63),
                FourCraft = CraftValue(craft, "four", 88),
                KeychainNumerator = CraftValue(keychain, "chanceNumerator", 7),
                KeychainDenominator = CraftValue(keychain, "chanceDenominator", 10)
            };

            if (weights.KeychainNumerator > weights.KeychainDenominator)
                throw new InvalidDataException(
                    "keychain.chanceNumerator must not exceed keychain.chanceDenominator.");

            if (!(weights.CleanCraft <= weights.SingleCraft
                  && weights.SingleCraft <= weights.PairCraft
                  && weights.PairCraft <= weights.ThreeCraft
                  && weights.ThreeCraft <= weights.FourCraft))
            {
                throw new InvalidDataException(
                    "stickerCraft thresholds must be non-decreasing (clean <= single <= pair <= three <= four).");
            }

            logger.LogInformation(
                "[BotRandomizer] weights.json loaded: {Knives} knife, {Gloves} glove, {Rarity} rarity, {Finishes} sticker finish, {KnifeFinishes} knife finish overrides",
                knives.Count,
                gloves.Count + (gloveDefault is null ? 0 : 1),
                rarity.Count,
                stickerFinish.Count,
                knifeFinish.Count);

            return weights;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "[BotRandomizer] weights.json is invalid; using built-in roll weights");
            return Default;
        }
    }

    private static IEnumerable<(string Key, int Value)> ReadNumbers(JsonElement root, string section)
    {
        if (!root.TryGetProperty(section, out var element) || element.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetInt32(out var value)
                || value <= 0)
            {
                continue;
            }

            yield return (property.Name, value);
        }
    }

    private static int CraftValue(IReadOnlyDictionary<string, int> values, string key, int fallback)
        => values.TryGetValue(key, out var value) ? value : fallback;
}
