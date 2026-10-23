using System;
using System.Collections.Generic;
using System.Linq;

namespace MalatangStory.Game
{
    [Serializable] public sealed class PreparationState
    {
        public bool started, completed;
        public string soup = "mala";
        public int bones = 2, spice = 2, oil = 1;
        public float seconds, quality = 50;
        public int cuts, accurateCuts, lastCutBeat = -1;
    }

    [Serializable] public sealed class HouseSoup
    {
        public string id, name, soup;
        public int bones, spice, oil, served;
        public float quality;
    }

    /// <summary>仕込みのタイミングと季節・配合による味の相性。</summary>
    public static class PreparationRules
    {
        public const float IdealMinimumSeconds = 8, IdealMaximumSeconds = 12;
        public const int MaximumSoups = 6;
        public static bool AccurateCut(float seconds)
        {
            if (!(seconds >= 0) || float.IsInfinity(seconds)) return false;
            float beat = seconds - (float)Math.Floor(seconds);
            return beat >= .35f && beat <= .65f;
        }
        public static float Quality(PreparationState preparation)
        {
            float timing = preparation.seconds >= IdealMinimumSeconds && preparation.seconds <= IdealMaximumSeconds ? 1
                : Math.Max(0, 1 - Math.Abs(preparation.seconds - 10) / 10);
            float cuts = preparation.cuts == 0 ? 0 : preparation.accurateCuts / (float)preparation.cuts;
            float balance = 1 - Math.Abs(preparation.bones + preparation.spice + preparation.oil - 6) / 8f;
            return Math.Max(0, Math.Min(100, 20 + 30 * timing + 30 * cuts + 20 * balance));
        }
        public static float Bonus(GameState state, string soup)
        {
            float result = state.preparation.completed ? (state.preparation.quality - 50) * .04f : 0;
            HouseSoup house = state.houseSoups.FirstOrDefault(x => x.id == state.activeHouseSoup);
            if (house != null && house.soup == soup)
            {
                string seasonal = Seasons.Of(state.day).Id;
                bool fits = seasonal == "winter" ? house.bones >= 3 && house.spice >= 2
                    : seasonal == "summer" ? house.oil <= 1 : seasonal == "autumn" ? house.bones >= 2 : house.spice <= 2;
                result += fits ? 1.5f : .5f;
            }
            return result;
        }
    }

    public sealed partial class RestaurantSimulation
    {
        public bool BeginPreparation(string soup, int bones, int spice, int oil)
        {
            if (State.phase != DayPhase.Preparation || !State.tutorialComplete || State.preparation.started || State.preparation.completed) return false;
            if (!Customers.AvailableSoups.Any(x => x.Id == soup) || bones < 0 || bones > 5 || spice < 0 || spice > 5 || oil < 0 || oil > 5) return false;
            if (bones + spice + oil < 3) return Fail("材料は合計3つ以上にしましょう。");
            long cost = (bones + spice + oil) * 40;
            if (State.moneyFen < cost) return Fail("仕込みの材料費が足りません。");
            State.moneyFen -= cost; State.report.foodCostFen += cost;
            State.preparation = new PreparationState { started = true, soup = soup, bones = bones, spice = spice, oil = oil };
            Save(); return true;
        }
        public void TickPreparation(float seconds)
        {
            if (State.phase != DayPhase.Preparation || !State.preparation.started || State.preparation.completed || !(seconds > 0) || float.IsInfinity(seconds)) return;
            State.preparation.seconds = Math.Min(30, State.preparation.seconds + seconds);
        }
        public bool ChopPreparation()
        {
            var preparation = State.preparation;
            if (!preparation.started || preparation.completed || State.phase != DayPhase.Preparation) return false;
            int beat = (int)preparation.seconds;
            if (beat == preparation.lastCutBeat) return false;
            preparation.lastCutBeat = beat;
            preparation.cuts++;
            if (PreparationRules.AccurateCut(preparation.seconds)) preparation.accurateCuts++;
            return true;
        }
        public bool FinishPreparation(bool byStaff = false)
        {
            if (State.phase != DayPhase.Preparation || !State.tutorialComplete || State.preparation.completed) return false;
            if (byStaff) State.preparation = new PreparationState { completed = true, quality = 50 };
            else
            {
                if (!State.preparation.started) return false;
                State.preparation.quality = PreparationRules.Quality(State.preparation);
                State.preparation.completed = true;
            }
            Notify(byStaff ? "仕込みをスタッフに任せました。標準の品質で開店できます。" : "仕込み完了！本日のスープ品質 " + State.preparation.quality.ToString("0"));
            Save(); return true;
        }
        public bool RecordHouseSoup(string name)
        {
            var preparation = State.preparation;
            string cleaned = PlayerText.Clean(name);
            if (!CanManage || !preparation.completed || !preparation.started || cleaned.Length == 0) return false;
            var soup = State.houseSoups.FirstOrDefault(x => x.soup == preparation.soup && x.bones == preparation.bones && x.spice == preparation.spice && x.oil == preparation.oil);
            if (soup == null)
            {
                if (State.houseSoups.Count >= PreparationRules.MaximumSoups) return Fail("オリジナルスープは6品まで登録できます。");
                soup = new HouseSoup { id = "house-" + State.nextHouseSoup++, soup = preparation.soup, bones = preparation.bones, spice = preparation.spice, oil = preparation.oil };
                State.houseSoups.Add(soup);
            }
            soup.name = cleaned; soup.quality = preparation.quality; State.activeHouseSoup = soup.id;
            Notify("「" + cleaned + "」を本日のオリジナルスープにしました。"); Save(); return true;
        }
        public bool ChooseHouseSoup(string id)
        {
            if (!CanManage || !State.houseSoups.Any(x => x.id == id)) return false;
            State.activeHouseSoup = id; Save(); Changed?.Invoke(); return true;
        }
    }
}
