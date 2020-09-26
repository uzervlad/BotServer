using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Catch.Difficulty;
using BotServer.PPCalculator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;

namespace BotServer
{
    class App
    {
        private Server server;

        public MapCache cache = new MapCache();

        private Thread console;

        public App(int port)
        {
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

                return JsonConvert.SerializeObject(result);
            });

            server.AddEndpoint("/getBeatmap", (req, res) => {
                var query = Helpers.ParseQueryString(req.QueryString);

                if(!query.ContainsKey("id"))
                    return JsonConvert.SerializeObject(new { error = "No ID provided" });

                var map = cache.GetBeatmap(int.Parse(query["id"]));
                var metadata = map.Metadata;

                var calculator = PPCalculatorHelpers.GetPPCalculator(map.RulesetID);

                int mode = map.RulesetID;

                if (query.ContainsKey("mode") && map.RulesetID == 0)
                {
                    var playableMap = map.GetPlayableBeatmap(calculator.Ruleset.RulesetInfo);
                    mode = int.Parse(query["mode"]);
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

                var result = JObject.FromObject(new
                {
                    title = metadata.Title,
                    artist = metadata.Artist,
                    creator = metadata.AuthorString,
                    version = map.BeatmapInfo.Version,
                    beatmapsetID = map.BeatmapSetInfo.OnlineBeatmapSetID,
                    mode,
                    difficulty = getDifficulty(difficulty, attributes),
                    bpm = new
                    {
                        min = Math.Round(controlPointInfo.BPMMinimum * 100) / 100,
                        max = Math.Round(controlPointInfo.BPMMaximum * 100) / 100,
                        avg = Math.Round(controlPointInfo.BPMMode * 100) / 100
                    },
                    length = map.Beatmap.HitObjects.LastOrDefault().StartTime
                });

                return JsonConvert.SerializeObject(result);
            });

            server.AddEndpoint("/getScorePP", (req, res) => {
                // id=80&mods=HD,DT,HR,FL,SO,NF&combo=600&miss=2&acc=98.653452&score=900000&fail=3453
                var query = Helpers.ParseQueryString(req.QueryString);

                if(!query.ContainsKey("id"))
                    return JsonConvert.SerializeObject(new { error = "No ID provided" });

                var map = cache.GetBeatmap(int.Parse(query["id"]));

                var calculator = PPCalculatorHelpers.GetPPCalculator(map.RulesetID);
                var playableMap = map.GetPlayableBeatmap(calculator.Ruleset.RulesetInfo);

                if (query.ContainsKey("mode") && map.RulesetID == 0)
                {
                    int mode = int.Parse(query["mode"]);
                    Ruleset ruleset = getRuleset(mode);
                    calculator = PPCalculatorHelpers.GetPPCalculator(mode);
                    var converter = ruleset.CreateBeatmapConverter(playableMap);
                    playableMap = converter.Convert();
                    map = new PPCalculator.WorkingBeatmap(playableMap);
                }

                var mods = query.ContainsKey("mods") ? query["mods"].Split(",") : new string[] { };

                var failed = query.ContainsKey("fail");
                var fail = failed ? map.Beatmap.HitObjects.Last(o => o.StartTime < double.Parse(query["fail"])).StartTime : 0;

                int combo = !query.ContainsKey("combo") ? calculator.GetMaxCombo(playableMap) : int.Parse(query["combo"]);
                int miss = !query.ContainsKey("miss") ? 0 : int.Parse(query["miss"]);
                double acc = !query.ContainsKey("acc") ? 1 : double.Parse(query["acc"].Replace(".", ",")) / 100;
                int score = !query.ContainsKey("score") ? 1000000 : int.Parse(query["score"]);

                double pp = failed
                    ? calculator.Calculate(map, fail, acc, combo, miss, mods.ToArray(), score)
                    : calculator.Calculate(map, acc, combo, miss, mods.ToArray(), score);

                double fcpp = calculator.Calculate(map, acc, calculator.GetMaxCombo(playableMap), 0, mods.ToArray(), 1000000);
                double sspp = calculator.Calculate(map, 1, calculator.GetMaxCombo(playableMap), 0, mods.ToArray(), 1000000);

                return JsonConvert.SerializeObject(new
                {
                    pp,
                    fcpp,
                    sspp,
                    param = new
                    {
                        combo,
                        miss,
                        acc = acc * 100,
                        score
                    }
                });
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