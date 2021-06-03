using System;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Catch.Difficulty;
using BotServer.PPCalculator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using System.Threading.Tasks;

namespace BotServer
{
    class App
    {
        private Server server;

        public MapCache cache = new MapCache();

        private Thread console;

        public App(int port)
        {
            cache.Token = File.ReadAllText("./token.txt").TrimEnd(Environment.NewLine.ToCharArray());

            server = new Server(this, port);

            var appConsole = new AppConsole(this);

            console = new Thread(new ThreadStart(appConsole.ThreadRunner));
            console.Start();

            createEndpoints();
        }

        public void Stop()
        {
            server.Close();
            console.Abort();
        }

        private void createEndpoints()
        {
            server.AddEndpoint("/", (req, res) => {
                var result = JObject.FromObject(new
                {
                    maps = cache.Count
                });

                return Task.FromResult(JsonConvert.SerializeObject(result));
            });

            server.AddEndpoint("/getBeatmap", async (req, res) => {
                var query = Helpers.ParseQueryString(req.QueryString);

                if(!query.ContainsKey("id"))
                    return JsonConvert.SerializeObject(new { error = "No ID provided" });

                var expirable = await cache.GetBeatmap(Helpers.ParseIntOr(query["id"], 0));
                var map = expirable.map;
                var metadata = map.Metadata;

                var calculator = PPCalculatorHelpers.GetPPCalculator(map.RulesetID);

                int mode = map.RulesetID;

                var playableMap = map.GetPlayableBeatmap(calculator.Ruleset.RulesetInfo);

                if (query.ContainsKey("mode") && map.RulesetID == 0)
                {
                    mode = Helpers.ParseIntOr(query["mode"], 0);
                    // mode = int.Parse(query["mode"]);
                    Ruleset ruleset = getRuleset(mode);
                    calculator = PPCalculatorHelpers.GetPPCalculator(mode);
                    var converter = ruleset.CreateBeatmapConverter(playableMap);
                    playableMap = converter.Convert();
                    map = new PPCalculator.WorkingBeatmap(playableMap);
                }

                var mods = query.ContainsKey("mods") ? calculator.getMods(query["mods"].Split(",")) : new List<Mod>();

                var difficulty = map.GetDifficultyWithMods(mods);
                var attributes = calculator.Ruleset.CreateDifficultyCalculator(map).Calculate(mods.ToArray());

                var controlPointInfo = map.Beatmap.ControlPointInfo;
                var timingPoints = controlPointInfo.TimingPoints;
                var avgBPM = 60000 / (timingPoints
                    .GroupBy(c => c.BeatLength)
                    .OrderByDescending(grp => grp.Count())
                    .FirstOrDefault()?.FirstOrDefault() ?? new TimingControlPoint()).BeatLength;

                var result = JObject.FromObject(new
                {
                    title = metadata.Title,
                    artist = metadata.Artist,
                    creator = metadata.AuthorString,
                    version = map.BeatmapInfo.Version,
                    beatmapsetID = expirable.apiMap.SetID,
                    maxCombo = calculator.GetMaxCombo(playableMap),
                    status = expirable.apiMap.Status.ToString(),
                    mode,
                    difficulty = getDifficulty(difficulty, attributes),
                    bpm = new
                    {
                        min = Math.Round(controlPointInfo.BPMMinimum),
                        max = Math.Round(controlPointInfo.BPMMaximum),
                        avg = Math.Round(avgBPM)
                    },
                    length = map.Beatmap.HitObjects.LastOrDefault().StartTime
                });

                return JsonConvert.SerializeObject(result);
            });

            server.AddEndpoint("/getScorePP", async (req, res) => {
                // id=80&mods=HD,DT,HR,FL,SO,NF&combo=600&miss=2&n50=2&acc=98.653452&score=900000&fail=3453
                var query = Helpers.ParseQueryString(req.QueryString);

                if(!query.ContainsKey("id"))
                    return JsonConvert.SerializeObject(new { error = "No ID provided" });

                var expirable = await cache.GetBeatmap(Helpers.ParseIntOr(query["id"], 0));
                var map = expirable.map;

                var calculator = PPCalculatorHelpers.GetPPCalculator(map.RulesetID);
                var playableMap = map.GetPlayableBeatmap(calculator.Ruleset.RulesetInfo);

                if (query.ContainsKey("mode") && map.RulesetID == 0)
                {
                    int mode = Helpers.ParseIntOr(query["mode"], 0);
                    Ruleset ruleset = getRuleset(mode);
                    calculator = PPCalculatorHelpers.GetPPCalculator(mode);
                    var converter = ruleset.CreateBeatmapConverter(playableMap);
                    playableMap = converter.Convert();
                    map = new PPCalculator.WorkingBeatmap(playableMap);
                }

                var mods = query.ContainsKey("mods") ? query["mods"].Split(",") : new string[] { };

                var failed = query.ContainsKey("fail");
                var fail = failed ? calculator.GetTimeAtHits(playableMap, int.Parse(query["fail"])) : 0;

                int combo = !query.ContainsKey("combo") ? calculator.GetMaxCombo(playableMap) : Helpers.ParseIntOr(query["combo"], calculator.GetMaxCombo(playableMap));
                int miss = !query.ContainsKey("miss") ? 0 : Helpers.ParseIntOr(query["miss"], 0);
                int n50 = !query.ContainsKey("n50") ? 0 : Helpers.ParseIntOr(query["n50"], 0);
                double acc = !query.ContainsKey("acc") ? 1 : double.Parse(query["acc"], CultureInfo.InvariantCulture) / 100;
                int score = !query.ContainsKey("score") ? 1000000 : Helpers.ParseIntOr(query["score"], 1000000);

                double pp = failed
                    ? calculator.Calculate(map, fail, acc, combo, miss, n50, mods.ToArray(), score)
                    : calculator.Calculate(map, acc, combo, miss, n50, mods.ToArray(), score);

                double fcpp = calculator.Calculate(map, acc, calculator.GetMaxCombo(playableMap), 0, 0, mods.ToArray(), 1000000);
                double sspp = calculator.Calculate(map, 1, calculator.GetMaxCombo(playableMap), 0, 0, mods.ToArray(), 1000000);

                return JsonConvert.SerializeObject(new
                {
                    pp,
                    fcpp,
                    sspp,
                    progress = fail / playableMap.HitObjects.Last().GetEndTime(),
                    param = new
                    {
                        combo,
                        miss,
                        acc = acc * 100,
                        score
                    }
                });
            });

            server.AddEndpoint("/api/getBeatmap", async (req, res) => {
                var query = Helpers.ParseQueryString(req.QueryString);

                MapCache.APIBeatmap beatmap;

                if(query.ContainsKey("hash"))
                {
                    beatmap = await cache.GetAPIBeatmap(query["hash"]);
                } 
                else if(query.ContainsKey("id"))
                {
                    beatmap = await cache.GetAPIBeatmap(Int32.Parse(query["id"]));
                }
                else return JsonConvert.SerializeObject(new { error = "No ID or MD5 provided" });

                return JsonConvert.SerializeObject(beatmap);
            });

            server.AddEndpoint("/api/getBeatmapset", async (req, res) => {
                var query = Helpers.ParseQueryString(req.QueryString);

                if(!query.ContainsKey("id"))
                    return JsonConvert.SerializeObject(new { error = "No ID provided" });

                return JsonConvert.SerializeObject(await cache.GetAPIBeatmapset(Int32.Parse(query["id"])));
            });
        }

        private object getDifficulty(BeatmapDifficulty difficulty, DifficultyAttributes attributes)
        {
            double ar = difficulty.ApproachRate;
            double od = difficulty.OverallDifficulty;

            if(attributes is OsuDifficultyAttributes osuAttributes)
            {
                ar = osuAttributes.ApproachRate;
                od = osuAttributes.OverallDifficulty;
            }
            else if(attributes is CatchDifficultyAttributes catchAttributes)
            {
                ar = catchAttributes.ApproachRate;
            }

            return new
            {
                ar = Math.Round(ar * 100) / 100,
                cs = Math.Round(difficulty.CircleSize * 100) / 100,
                hp = Math.Round(difficulty.DrainRate * 100) / 100,
                od = Math.Round(od * 100) / 100,
                stars = Math.Round(attributes.StarRating * 100) / 100
            };
        }

        private Ruleset getRuleset(int rulesetID)
        {
            switch(rulesetID)
            {
                case 0:
                    return new OsuRuleset();
                case 1:
                    return new TaikoRuleset();
                case 2:
                    return new CatchRuleset();
                case 3:
                    return new ManiaRuleset();
                default:
                    throw new ArgumentOutOfRangeException("rulesetID");
            }
        }
    }
}