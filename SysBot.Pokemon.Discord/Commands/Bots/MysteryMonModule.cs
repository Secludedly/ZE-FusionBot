using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class MysteryMonModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;
        private static readonly Random rng = new();

        // Banned species entirely
        private static readonly HashSet<ushort> BannedSpecies = new()
        {
            643, 644, 646, 716, 718, 791, 792, 800, 801, 888, 889, 898
        };

        // -------------------------------
        // ENTRY COMMAND
        // -------------------------------
        [Command("mysterymon")]
        [Alias("mm", "mystery", "surprise")]
        public async Task MysteryMonAsync()
        {
            var userID = Context.User.Id;
            if (Info.IsUserInQueue(userID))
            {
                await ReplyAsync("You already have an existing trade in the queue. Wait for it to finish.");
                return;
            }

            var code = Info.GetRandomTradeCode(userID);
            await Task.Run(async () => await MysteryMonAsync(code));
        }

        // -------------------------------
        // MAIN ENTRY WITH CODE
        // -------------------------------
        [Command("mysterymon")]
        [Alias("mm", "mystery", "surprise")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task MysteryMonAsync(int code)
        {
            var userID = Context.User.Id;

            if (Info.IsUserInQueue(userID))
            {
                await ReplyAsync("You already have an existing trade in the queue. Wait for it to finish.");
                return;
            }

            try
            {
                using var cancel = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));
                var pkm = GenerateMysteryMon(cancel.Token);

                if (pkm is not null)
                {
                    var sig = Context.User.GetFavor();

                    await AddTradeToQueueAsync(code, Context.User.Username, pkm, sig, Context.User);

                    if (Context.Message is IUserMessage m)
                        _ = DeleteMessageAfterDelay(m, 2000);
                }
                else
                {
                    await ReplyAsync("Luck was not in your favor. Please try to find your MysteryMon again!\nWhatever it is, it's still waiting for you to find it, so give it another try!");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(MysteryMonModule<T>));
                await ReplyAsync("Luck was not in your favor. Please try to find your MysteryMon again!\nWhatever it is, it's still waiting for you to find it, so give it another try!");
            }
        }

        private static async Task DeleteMessageAfterDelay(IUserMessage msg, int delay)
        {
            await Task.Delay(delay);
            await msg.DeleteAsync();
        }

        // --------------------------------------------------------
        // RANDOMIZATION + LEGALITY PIPELINE
        // --------------------------------------------------------
        private static T? GenerateMysteryMon(System.Threading.CancellationToken token)
        {
            var game = GetGameVersion();
            var speciesList = GetRandomSpecies(game);

            if (speciesList.Count == 0)
                return default;

            // Outer loop: try different species to avoid unlucky draws
            for (int outer = 0; outer < 15; outer++)
            {
                if (token.IsCancellationRequested)
                    return default;

                ushort species = speciesList[rng.Next(speciesList.Count)];
                string speciesName = GameInfo.GetStrings("en").specieslist[species];

                var showdown = new ShowdownSet(speciesName);
                var template = AutoLegalityWrapper.GetTemplate(showdown);
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();

                var legal = sav.GetLegal(template, out _);
                if (legal == null) continue;

                var working = (T)EntityConverter.ConvertToType(legal, typeof(T), out _)!;
                if (working == null) continue;

                // Apply all steps with pre-checks and smarter retries
                bool success = true;

                success &= SafeApply(working, SetIVs, "IVs", token, 1000);
                success &= SafeApply(working, SetAlpha, "Alpha", token, 1000, () => working is IAlpha);
                success &= SafeApply(working, SetEVs, "EVs", token, 1000);
                success &= SafeApply(working, SetLevel, "Level", token, 1000);
                success &= SafeApply(working, SetTeraType, "TeraType", token, 1000, () => working is PK9);
                success &= SafeApply(working, SetNature, "Nature", token, 1000, () => GetGameVersion() != GameVersion.ZA);
                success &= SafeApply(working, SetShinyStatus, "Shiny", token, 1000);
                success &= SafeApply(working, SetHeldItem, "HeldItem", token, 1000);

                var finalCheck = new LegalityAnalysis(working);
                if (finalCheck.Valid)
                    return working;

                LogUtil.LogInfo($"[MysteryMon] Species {speciesName} failed final legality check, retrying outer loop", "MysteryMon");
            }

            LogUtil.LogError("[MysteryMon] All species attempts failed, returning null", "MysteryMon");
            return default;
        }

        // Smarter retry helper with optional pre-check
        private static bool SafeApply(T pk, Action<T> action, string name, System.Threading.CancellationToken token, int maxAttempts, Func<bool>? preCheck = null)
        {
            if (preCheck != null && !preCheck()) return true; // skip if condition not met

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (token.IsCancellationRequested)
                    return false;

                var backup = (T)pk.Clone();
                try
                {
                    action(pk);
                    if (new LegalityAnalysis(pk).Valid)
                    {
                        LogUtil.LogInfo($"[MysteryMon] Step '{name}' → OK on attempt {attempt + 1}", "MysteryMon");
                        return true;
                    }
                    pk = backup; // restore
                }
                catch
                {
                    pk = backup; // restore on exception
                }
            }

            LogUtil.LogError($"[MysteryMon] Step '{name}' → FAILED after {maxAttempts} attempts", "MysteryMon");
            return false;
        }

        // --------------------------------------------------------
        // STEP: IVs (with Alpha bonus)
        // --------------------------------------------------------
        private static void SetIVs(T pk)
        {
            // Start with fully random IVs
            pk.IV_HP = (byte)rng.Next(32);
            pk.IV_ATK = (byte)rng.Next(32);
            pk.IV_DEF = (byte)rng.Next(32);
            pk.IV_SPA = (byte)rng.Next(32);
            pk.IV_SPD = (byte)rng.Next(32);
            pk.IV_SPE = (byte)rng.Next(32);

            // If this mon is Alpha, force 3 random IVs to be perfect (31)
            if (pk is IAlpha alpha && alpha.IsAlpha)
            {
                // pick 3 unique stats to max
                var indices = Enumerable.Range(0, 6).OrderBy(_ => rng.Next()).Take(3).ToArray();

                foreach (int idx in indices)
                {
                    switch (idx)
                    {
                        case 0: pk.IV_HP = 31; break;
                        case 1: pk.IV_ATK = 31; break;
                        case 2: pk.IV_DEF = 31; break;
                        case 3: pk.IV_SPA = 31; break;
                        case 4: pk.IV_SPD = 31; break;
                        case 5: pk.IV_SPE = 31; break;
                    }
                }
            }

            // 22% chance for full perfect IVs across all stats
            if (rng.Next(100) < 22)
            {
                pk.IV_HP = pk.IV_ATK = pk.IV_DEF = pk.IV_SPA = pk.IV_SPD = pk.IV_SPE = 31;
            }
        }

        // --------------------------------------------------------
        // STEP: Alpha (SV / ZA only)
        // --------------------------------------------------------
        private static void SetAlpha(T pk)
        {
            // Only PK9 (SV) or PA9 (ZA) can be Alpha
            if (pk is not IAlpha alpha)
                return;

            bool makeAlpha = rng.Next(100) < 15; // 15% chance to be Alpha
            alpha.IsAlpha = makeAlpha;
        }

        // --------------------------------------------------------
        // STEP: EVs
        // --------------------------------------------------------
        private static void SetEVs(T pk)
        {
            int total = 0;
            byte[] evs = new byte[6];

            for (int i = 0; i < 6; i++)
            {
                int ev = rng.Next(0, 253);
                if (total + ev > 510) ev = 510 - total;
                if (ev > 252) ev = 252;
                evs[i] = (byte)ev;
                total += ev;
            }

            pk.EV_HP = evs[0];
            pk.EV_ATK = evs[1];
            pk.EV_DEF = evs[2];
            pk.EV_SPA = evs[3];
            pk.EV_SPD = evs[4];
            pk.EV_SPE = evs[5];
        }

        // --------------------------------------------------------
        // STEP: Level
        // --------------------------------------------------------
        private static void SetLevel(T pk)
        {
            var game = GetGameVersion();
            bool isAlpha = pk is IAlpha alpha && alpha.IsAlpha;

            int level;

            if (isAlpha && (game == GameVersion.SV || game == GameVersion.ZA))
            {
                // Alpha Pokémon: force level 69–100
                level = rng.Next(69, 101);
            }
            else
            {
                if (rng.Next(100) < 79) // 79% chance
                {
                    // High level: 66–100
                    level = rng.Next(66, 101);
                }
                else
                {
                    // Low level: 35–60
                    level = rng.Next(35, 61);
                }
            }

            pk.CurrentLevel = (byte)level;
        }

        // --------------------------------------------------------
        // STEP: Tera Type (SV Only)
        // --------------------------------------------------------
        private static void SetTeraType(T pk)
        {
            if (pk is not PK9 pk9)
                return;

            var personal = pk9.PersonalInfo;
            int t1 = personal.Type1;
            int t2 = personal.Type2;

            var pool = Enumerable.Range(0, 18).Where(t => t != t1 && t != t2).ToList();
            int newType = pool[rng.Next(pool.Count)];

            pk9.SetTeraType((MoveType)newType);
        }

        // --------------------------------------------------------
        // STEP: Nature
        // --------------------------------------------------------
        private static void SetNature(T pk)
        {
            var game = GetGameVersion();
            // PLZA: skip setting random Nature entirely so that Alpha can legalize without problems.
            if (game == GameVersion.ZA)
                return;

            pk.Nature = (Nature)rng.Next(25);
        }

        // --------------------------------------------------------
        // STEP: Shiny
        // --------------------------------------------------------
        private static void SetShinyStatus(T pk)
        {
            if (rng.Next(100) < 30) // 30% chance to be shiny
                pk.SetShiny();
        }

        // --------------------------------------------------------
        // STEP: Held Item
        // --------------------------------------------------------
        private static void SetHeldItem(T pk)
        {
            var game = GetGameVersion();
            var pool = GetHeldItemPool(game);

            pk.HeldItem = pool.Count == 0 ? 0 : pool[rng.Next(pool.Count)];
        }

        // --------------------------------------------------------
        // SUPPORT METHODS
        // --------------------------------------------------------
        private static List<ushort> GetRandomSpecies(GameVersion game)
        {
            var strings = GameInfo.GetStrings("en");
            return strings.specieslist
                .Select((n, i) => (n, i))
                .Where(p => !string.IsNullOrWhiteSpace(p.n) && !BannedSpecies.Contains((ushort)p.i))
                .Select(p => (ushort)p.i)
                .ToList();
        }

        private static bool SafeApplyWithRetries(T pk, Action<T> action, string name, int maxAttempts)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var backup = (T)pk.Clone();
                try
                {
                    action(pk);
                    var la = new LegalityAnalysis(pk);
                    if (la.Valid)
                    {
                        LogUtil.LogInfo($"[MysteryMon] Step '{name}' → OK on attempt {attempt + 1}", "MysteryMon");
                        return true;
                    }
                    pk = backup; // restore for next attempt
                }
                catch
                {
                    pk = backup; // restore on exception
                }
            }

            LogUtil.LogError($"[MysteryMon] Step '{name}' → FAILED after {maxAttempts} attempts", "MysteryMon");
            return false;
        }

        private async Task AddTradeToQueueAsync(int code, string trainer, T pk, RequestSignificance sig, SocketUser usr)
        {
            var la = new LegalityAnalysis(pk);
            if (!la.Valid)
                return;

            await QueueHelper<T>.AddToQueueAsync(
                Context,
                code,
                trainer,
                sig,
                pk,
                PokeRoutineType.LinkTrade,
                PokeTradeType.Specific,
                usr
            );
        }

        private static List<int> GetHeldItemPool(GameVersion game) =>
            game switch
            {
                GameVersion.ZA => ZA_HeldItems,
                GameVersion.PLA => new List<int>(),
                _ => DefaultHeldItems()
            };

        private static List<int> DefaultHeldItems() => new()
        {
            1, 236, 244, 1120, 286, 217, 328, 221, 248, 255, 228, 229, 230,
            275, 233, 281, 234, 265, 269, 245, 538, 645, 223, 287, 297, 220,
            270, 290, 294, 241, 268, 50, 55, 47, 48, 49, 51, 54, 158, 210,
            155, 157, 619, 620, 82, 84, 85, 81, 80, 83, 107, 108, 109
        };

        private static readonly List<int> ZA_HeldItems = new()
        {
            23, 24, 27, 29, 33, 1, 150, 152, 155, 158, 184, 185, 45, 46, 47,
            48, 49, 50, 51, 52, 80, 81, 82, 83, 84, 85, 107, 108, 109, 214,
            217, 218, 221, 222, 230, 231, 232, 233, 234, 236, 237, 238, 241,
            242, 248, 249, 245, 253, 266, 267, 268, 270, 540, 565, 566, 567,
            568, 569, 570, 639, 640, 646, 647, 849, 1128, 1231, 1232, 1233,
            1234, 1235, 1236, 1237, 1238, 1239, 1240, 1241, 1242, 1243, 1244,
            1245, 1246, 1247, 1248, 1249, 1250, 1251, 1582, 2558, 581
        };

        private static GameVersion GetGameVersion() =>
            typeof(T) switch
            {
                var t when t == typeof(PK8) => GameVersion.SWSH,
                var t when t == typeof(PB8) => GameVersion.BDSP,
                var t when t == typeof(PA8) => GameVersion.PLA,
                var t when t == typeof(PK9) => GameVersion.SV,
                var t when t == typeof(PA9) => GameVersion.ZA,
                _ => throw new ArgumentException("Unsupported game version.")
            };
    }
}
