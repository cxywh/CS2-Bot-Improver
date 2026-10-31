using System.Text;

namespace BotLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run();

        if (args.Any(a => a.Equals("--measure", StringComparison.OrdinalIgnoreCase)))
            return MeasureText();

        if (args.Any(a => a.Equals("--audit", StringComparison.OrdinalIgnoreCase)))
            return Audit();

        if (args.Any(a => a.Equals("--launch", StringComparison.OrdinalIgnoreCase)))
        {
            var target = Cs2Env.Locate(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)));
            if (target is null)
            {
                Console.Out.WriteLine("launch: 未定位 CS2");
                return 2;
            }

            var launched = target.LaunchGame();
            Console.Out.WriteLine($"launch: {(launched.Ok ? "OK" : "FAIL")} {launched.Message}");
            Console.Out.WriteLine($"exe: {target.GameExe} (exists={File.Exists(target.GameExe)})");
            return launched.Ok ? 0 : 1;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var pageArg = args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        var page = pageArg is not null && int.TryParse(pageArg[7..], out var parsedPage) ? parsedPage : 0;
        Application.Run(new MainForm(path, page));
        return 0;
    }

    // Walks every control on every page and reports any label or button whose bounds are
    // smaller than the text it draws.
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    private static int Audit()
    {
        // Run at the display DPI so the audit measures the same metrics as the window.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new MainForm(null, 0);
        form.Show();
        Application.DoEvents();

        var problems = 0;
        for (var page = 0; page < form.PageCount; page++)
        {
            form.ShowPage(page);
            Application.DoEvents();
            form.PerformLayout();
            Application.DoEvents();
            Console.Out.WriteLine($"--- page {page} ---");
            problems += AuditChildren(form, "  ", ref problems);
        }

        Console.Out.WriteLine(problems == 0 ? "AUDIT OK" : $"AUDIT FAILED ({problems})");
        return problems == 0 ? 0 : 1;
    }

    private static int AuditChildren(Control parent, string indent, ref int total)
    {
        var local = 0;
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible)
                continue;

            switch (child)
            {
                case Label label when !string.IsNullOrWhiteSpace(label.Text):
                {
                    var single = label.Text.IndexOf((char)10) < 0 && label.Text.IndexOf((char)13) < 0;

                    var width = single ? Theme.TextWidth(label.Font, label.Text) : 0;
                    var height = single
                        ? Theme.SingleLine(label.Font, label.Text)
                        : Theme.Wrapped(label.Font, label.Text, Math.Max(40, label.Width)) ;
                    if (single && label.Width + 1 < width || label.Height + 1 < height)
                    {
                        Console.Out.WriteLine($"{indent}CLIP label need={width}x{height} have={label.Width}x{label.Height} << {Flatten(label.Text)} >>");
                        local++;
                    }

                    break;
                }
                case NavItem nav when !string.IsNullOrWhiteSpace(nav.Text):
                {
                    var width = Theme.TextWidth(nav.Font, nav.Text);
                    if (nav.Width < width + 34)
                    {
                        Console.Out.WriteLine($"{indent}CLIP nav need={width + 34} have={nav.Width} << {nav.Text} >>");
                        local++;
                    }

                    break;
                }
                case SliderRow row:
                {
                    var width = Theme.TextWidth(Theme.Body, row.Label);
                    if (row.NameWidth < width + 10)
                    {
                        Console.Out.WriteLine($"{indent}CLIP slider-name need={width + 10} have={row.NameWidth} << {row.Label} >>");
                        local++;
                    }

                    break;
                }
                case PillButton button:
                {
                    var width = Theme.TextWidth(button.Font, button.Text);
                    if (button.Width < width + 16)
                    {
                        Console.Out.WriteLine($"{indent}CLIP button need={width + 16} have={button.Width} << {button.Text} >>");
                        local++;
                    }

                    break;
                }
            }

            local += AuditChildren(child, indent + "  ", ref total);
        }

        total += local;
        return local;
    }

    private static string Flatten(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    // Renders a string with the exact font/flags the UI uses and reports the real ink
    // extent, so a label width is never guessed from MeasureText alone.
    private static int MeasureText()
    {
        var samples = new (string Text, Font Font)[]
        {
            ("启动器 · 模式 / 难度 / 掉落刀具 / 随机权重", Theme.Small),
            ("切换 gameinfo.gi 与启动游戏；两者都只是本机文件与进程操作", Theme.Small),
            ("启动 CS2（-insecure）", Theme.Body),
            ("打开 cfg 目录", Theme.Body),
            ("按一下绑定键就在脚下刷出选中的刀；写入的是 cfg 里的 bind 行", Theme.Small),
            ("猎杀者匕首 Huntsman", Theme.Body)
        };

        foreach (var (text, font) in samples)
        {
            using var bitmap = new Bitmap(1600, 60);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Black);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                TextRenderer.DrawText(g, text, font, new Rectangle(0, 0, 1600, 60), Color.White,
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }

            var inkRight = 0;
            for (var x = 0; x < bitmap.Width; x++)
                for (var y = 0; y < bitmap.Height; y++)
                    if (bitmap.GetPixel(x, y).R > 40)
                    {
                        inkRight = Math.Max(inkRight, x + 1);
                        break;
                    }

            var measured = Theme.TextWidth(font, text);
            Console.Out.WriteLine($"{(inkRight > measured ? "UNDER" : "ok")}  measure={measured,4} ink={inkRight,4}  delta={inkRight - measured,3}  {text}");
        }

        return 0;
    }
}

// Headless checks: environment discovery against the real install, plus the two write paths
// (cfg bind rewrite, weights round trip) exercised on throwaway copies.
internal static class SelfTest
{
    internal static int Run()
    {
        var log = new StringBuilder();
        var failures = 0;

        void Check(string name, bool ok, string detail)
        {
            if (!ok)
                failures++;
            log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
        }

        var env = Cs2Env.Locate();
        Check("locate-csgo", env is not null, env?.CsgoDir ?? "not found");

        if (env is not null)
        {
            var mode = env.CurrentMode();
            Check("mode", mode != LaunchMode.Unknown, Cs2Env.ModeLabel(mode));

            var difficulty = env.CurrentDifficulty();
            Check("difficulty", difficulty != Difficulty.Unknown, Cs2Env.DifficultyLabel(difficulty));

            var knives = env.ReadDropKnives();
            Check("drop-knives-read", knives.Files.Count > 0,
                $"key={Cs2Env.DescribeKey(knives.Key)} knives=[{string.Join(",", knives.Knives)}] files={knives.Files.Count} sv_cheats={knives.CheatEnabled}");

            var plugin = env.CheckPlugin();
            Check("plugin", plugin.Ok, plugin.Detail);

            var core = env.CheckCoreJson();
            Check("core-json", core.Ok, core.Detail);

            var selfIds = env.CheckSelfIds();
            Check("self-ids", selfIds.Ok, selfIds.Detail);

            var weights = env.CheckWeights();
            Check("weights-present", weights.Ok, weights.Detail);

            Check("cs2-exe", File.Exists(env.GameExe), env.GameExe);

            var launchOptions = Cs2Env.ReadLaunchOptions();
            Check("launch-options", true, launchOptions is null ? "读不到 localconfig.vdf" : $"\"{launchOptions}\"");
        }

        // cfg rewrite on a copy
        var temp = Path.Combine(Path.GetTempPath(), "botlauncher-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var sample = Path.Combine(temp, "my_bot_normal_config.cfg");
            File.WriteAllLines(sample, new[]
            {
                "sv_cheats 1",
                "mp_drop_knife_enable 1",
                "bind \\ \"subclass_create 515\"",
                "alias bot_aim_head \"bot_aim head\"",
                "bot_quota 10"
            });

            var ok = Cs2Env.RewriteCfgFile(sample, "\\", new[] { 515, 507, 500 });
            var lines = File.ReadAllLines(sample);
            Check("cfg-rewrite", ok &&
                                lines[2] == "bind \\ \"subclass_create 515;subclass_create 507;subclass_create 500\"" &&
                                lines[0] == "sv_cheats 1" && lines[3] == "alias bot_aim_head \"bot_aim head\"" && lines[4] == "bot_quota 10",
                lines[2]);
            Check("cfg-backup", File.Exists(sample + ".editbak"), "editbak created");

            var reparsed = Cs2Env.TryParseBind(File.ReadAllLines(sample)[2], out var key, out var ids);
            Check("cfg-reparse", reparsed && key == "\\" && ids.SequenceEqual(new[] { 515, 507, 500 }),
                $"key={key} ids=[{string.Join(",", ids)}]");

            // full write paths against a fake CS2 layout, so the real install is untouched
            var fake = Path.Combine(temp, "csgo");
            Directory.CreateDirectory(Path.Combine(fake, "backup", "Online"));
            Directory.CreateDirectory(Path.Combine(fake, "backup", "WithBots"));
            Directory.CreateDirectory(Path.Combine(fake, "cfg"));
            Directory.CreateDirectory(Path.Combine(fake, "overrides", "Low"));
            Directory.CreateDirectory(Path.Combine(fake, "overrides", "Medium"));
            Directory.CreateDirectory(Path.Combine(fake, "overrides", "High"));

            File.WriteAllText(Path.Combine(fake, "backup", "Online", "gameinfo.gi"), "online-build");
            File.WriteAllText(Path.Combine(fake, "backup", "WithBots", "gameinfo.gi"), "bots-build");
            File.WriteAllText(Path.Combine(fake, "gameinfo.gi"), "online-build");
            File.WriteAllText(Path.Combine(fake, "overrides", "Low", "botprofile.vpk"), "low");
            File.WriteAllText(Path.Combine(fake, "overrides", "Medium", "botprofile.vpk"), "medium");
            File.WriteAllText(Path.Combine(fake, "overrides", "High", "botprofile.vpk"), "high");
            File.WriteAllText(Path.Combine(fake, "overrides", "botprofile.vpk"), "low");
            File.WriteAllLines(Path.Combine(fake, "cfg", "my_bot_normal_config.cfg"), new[]
            {
                "sv_cheats 1",
                "mp_drop_knife_enable 1",
                "bind \\ \"subclass_create 515\""
            });

            var fakeEnv = Cs2Env.Locate(fake);
            Check("fake-locate", fakeEnv is not null, fake);
            if (fakeEnv is not null)
            {
                Check("fake-mode-detect", fakeEnv.CurrentMode() == LaunchMode.Online, fakeEnv.CurrentMode().ToString());
                Check("fake-mode-switch", fakeEnv.SwitchMode(LaunchMode.WithBots).Ok &&
                                         fakeEnv.CurrentMode() == LaunchMode.WithBots &&
                                         File.ReadAllText(Path.Combine(fake, "gameinfo.gi")) == "bots-build",
                    "gameinfo.gi == WithBots 备份");

                Check("fake-difficulty-detect", fakeEnv.CurrentDifficulty() == Difficulty.Low,
                    fakeEnv.CurrentDifficulty().ToString());
                Check("fake-difficulty-switch", fakeEnv.SetDifficulty(Difficulty.High).Ok &&
                                                fakeEnv.CurrentDifficulty() == Difficulty.High,
                    "overrides\\botprofile.vpk == high");

                var write = fakeEnv.WriteDropKnives("k", new[] { 500, 507 });
                var cfg = File.ReadAllLines(Path.Combine(fake, "cfg", "my_bot_normal_config.cfg"));
                Check("fake-drop-knives", write.Ok &&
                                          cfg[2] == "bind k \"subclass_create 500;subclass_create 507\"" &&
                                          cfg[0] == "sv_cheats 1" && cfg[1] == "mp_drop_knife_enable 1",
                    cfg.Length > 2 ? cfg[2] : "missing");

                var roundTrip = fakeEnv.ReadDropKnives();
                Check("fake-drop-knives-read", roundTrip.Key == "k" && roundTrip.Knives.SequenceEqual(new[] { 500, 507 }),
                    $"key={roundTrip.Key} knives=[{string.Join(",", roundTrip.Knives)}]");

                // the whitelist page writes through SaveSelfIds; prove the round trip and that
                // hand-written comments survive a rewrite
                Directory.CreateDirectory(fakeEnv.PluginDir);
                File.WriteAllLines(fakeEnv.SelfIdsPath,
                    new[] { "# 说明行", "// 另一种注释", "76561197960265729 // old" });
                fakeEnv.SaveSelfIds(new[] { ("76561198000000001", "me"), ("76561198000000002", "") });
                // one-click installer: new files land, existing user config is kept
                var payload = Path.Combine(fake, "payload");
                Directory.CreateDirectory(payload);
                foreach (var name in Cs2Env.PayloadFiles)
                    File.WriteAllText(Path.Combine(payload, name), "new-" + name);
                File.WriteAllText(Path.Combine(payload, "weights.json"), "new-weights");
                File.WriteAllText(Path.Combine(payload, "self_steamids.txt"), "new-ids");

                Directory.CreateDirectory(fakeEnv.PluginDir);
                File.WriteAllText(Path.Combine(fakeEnv.PluginDir, "weights.json"), "user-weights");
                Directory.CreateDirectory(fakeEnv.CsgoDir); // keep the fake tree alive

                var install = fakeEnv.InstallPlugin(payload);
                Check("plugin-install",
                    install.Ok &&
                    File.ReadAllText(Path.Combine(fakeEnv.PluginDir, "BotRandomizer.dll")) == "new-BotRandomizer.dll" &&
                    File.ReadAllText(Path.Combine(fakeEnv.PluginDir, "weights.json")) == "user-weights" &&
                    // self_steamids.txt already existed from the SaveSelfIds check above, so it must survive
                    File.ReadAllText(Path.Combine(fakeEnv.PluginDir, "self_steamids.txt")).Contains("76561197960265729") == false &&
                    File.ReadAllText(Path.Combine(fakeEnv.PluginDir, "self_steamids.txt")).Contains("76561198000000001"),
                    $"copied={install.Copied} kept={install.Kept}");

                var whitelist = fakeEnv.ReadSelfIds();
                Check("self-ids-write",
                    whitelist.Count == 2 && whitelist[0].Id == "76561198000000001" && whitelist[0].Note == "me" &&
                    whitelist[1].Note.Length == 0,
                    $"ids=[{string.Join(", ", whitelist.Select(i => i.Id + ":" + i.Note))}]");
            }

            // vdf parsing must pick the 730 block that actually carries LaunchOptions
            var vdf = string.Join(Environment.NewLine, new[]
            {
                "\"apps\"",
                "{",
                "  \"730\"",
                "  {",
                "    \"LastPlayed\"  \"1\"",
                "  }",
                "  \"730\"",
                "  {",
                "    \"LaunchOptions\"  \"-insecure -console\"",
                "  }",
                "}"
            });
            Check("vdf-launch-options", Cs2Env.LaunchOptionsFromVdf(vdf) == "-insecure -console",
                Cs2Env.LaunchOptionsFromVdf(vdf) ?? "null");

            // one save must commit every group at once
            var groupFile = Path.Combine(temp, "weights-all.json");
            var groupSeed = string.Join(Environment.NewLine, new[]
            {
                "{",
                "  \"knives\": { \"515\": 25 },",
                "  \"gloves\": { \"5030\": 4 },",
                "  \"weaponRarity\": { \"covert\": 40, \"milSpec\": 10 }",
                "}"
            });
            File.WriteAllText(groupFile, groupSeed);

            var groupTarget = WeightFile.Load(groupFile);
            var groupEdits = new Dictionary<string, Dictionary<string, int>>
            {
                ["knives"] = new() { ["515"] = 55, ["507"] = 45 },
                ["gloves"] = new() { ["5030"] = 9 },
                ["keychain"] = new() { ["chanceNumerator"] = 3, ["chanceDenominator"] = 5 }
            };
            var groupDefs = new (string Id, (string Key, string Label, int Value)[] Items, bool IsKeychain)[]
            {
                ("knives", WeightFile.KnifeDefaults, false),
                ("gloves", WeightFile.GloveDefaults, false),
                ("weaponRarity", WeightFile.RarityDefaults, false),
                ("keychain", Array.Empty<(string, string, int)>(), true)
            };

            var written = WeightsPage.CommitAll(groupTarget, groupEdits, groupDefs);
            groupTarget.Save();

            var reloadedGroups = WeightFile.Load(groupFile);
            Check("weights-save-all",
                reloadedGroups.Get("knives", "515", -1) == 55 &&
                reloadedGroups.Get("knives", "507", -1) == 45 &&
                reloadedGroups.Get("gloves", "5030", -1) == 9 &&
                reloadedGroups.Get("keychain", "chanceNumerator", -1) == 3 &&
                reloadedGroups.Get("keychain", "chanceDenominator", -1) == 5 &&
                reloadedGroups.Get("weaponRarity", "covert", -1) == 40 &&
                reloadedGroups.Get("weaponRarity", "milSpec", -1) == 10,
                written + " 项写入：刀型/手套/挂件已改，稀有度保持原值");

            // the mouse wheel must not move a slider; only a drag may
            var wheelRow = new SliderRow("wheel", "滚轮测试", 50, 1, 100);
            var onWheel = typeof(Control).GetMethod("OnMouseWheel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            onWheel?.Invoke(wheelRow, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 10, 120) });
            Check("slider-wheel-ignored", wheelRow.Value == 50, "滚轮后仍是 " + wheelRow.Value);

            // weights round trip, unknown keys preserved
            var weightPath = Path.Combine(temp, "weights.json");
            File.WriteAllText(weightPath,
                "{\n  // 注释必须保留\n  \"knives\": { \"515\": 25, \"999\": 1 },\n  \"futureSection\": { \"x\": 3 }\n}\n");

            var file = WeightFile.Load(weightPath);
            file.Set("knives", "515", 60);
            file.Set("knives", "507", 40);
            file.Save();

            var reloaded = WeightFile.Load(weightPath);
            var text = File.ReadAllText(weightPath);
            Check("weights-roundtrip",
                reloaded.Get("knives", "515", -1) == 60 &&
                reloaded.Get("knives", "507", -1) == 40 &&
                reloaded.Get("knives", "999", -1) == 1 &&
                text.Contains("futureSection"),
                "515=60 507=40 unknown key+sections kept, comments stripped");
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }

        log.AppendLine(failures == 0 ? "SELFTEST OK" : $"SELFTEST FAILED ({failures})");
        Console.Out.Write(log.ToString());
        return failures == 0 ? 0 : 1;
    }

}
