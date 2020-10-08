using System;
using System.Linq;
using System.Collections.Generic;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;

namespace BotServer.PPCalculator
{
    public class OsuCalculator : PPCalculator
    {
        public override Ruleset Ruleset { get; } = new OsuRuleset();

        protected override int GetMaxCombo(IReadOnlyList<HitObject> hitObjects) =>
            hitObjects.Count + hitObjects.OfType<Slider>().Sum(s => s.NestedHitObjects.Count - 1);

        protected override double GetTimeAtHits(IReadOnlyList<HitObject> hitObjects, int hits)
        {
            return hits > hitObjects.Count()
                ? hitObjects.Last().GetEndTime()
                : hitObjects[hits - 1].GetEndTime();
        }

        protected override Dictionary<HitResult, int> GenerateHitResults(double accuracy, IReadOnlyList<HitObject> hitObjects, int countMiss)
        {
            var totalResultCount = hitObjects.Count;

            var targetTotal = (int)Math.Round(accuracy * totalResultCount * 6);

            var delta = targetTotal - (totalResultCount - countMiss);

            var great = delta / 5;
            var good = delta % 5;
            var meh = totalResultCount - great - good - countMiss;

            return new Dictionary<HitResult, int>
            {
                { HitResult.Great, great },
                { HitResult.Good, good },
                { HitResult.Meh, meh },
                { HitResult.Miss, countMiss }
            };
        }

        protected override double GetAccuracy(Dictionary<HitResult, int> statistics)
        {
            var countGreat = statistics[HitResult.Great];
            var countGood = statistics[HitResult.Good];
            var countMeh = statistics[HitResult.Meh];
            var countMiss = statistics[HitResult.Miss];
            var total = countGreat + countGood + countMeh + countMiss;

            return (double)((6 * countGreat) + (2 * countGood) + countMeh) / (6 * total);
        }
    }
}