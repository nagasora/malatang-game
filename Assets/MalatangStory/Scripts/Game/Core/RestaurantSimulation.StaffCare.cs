using System;
using System.Linq;
namespace MalatangStory.Game
{
    public sealed partial class RestaurantSimulation
    {
        public bool RestStaff(string id, bool rest)
        {
            var member = State.staff.FirstOrDefault(x => x.id == id);
            if (member == null) return false;
            member.resting = rest;
            if (rest) { member.busySeconds = 0; member.task = ""; member.taskTarget = ""; }
            Save(); Changed?.Invoke(); return true;
        }
        public bool PraiseStaff(string id)
        {
            var member = State.staff.FirstOrDefault(x => x.id == id);
            if (member == null || member.praisedDay == State.day) return false;
            member.praisedDay = State.day; member.motivation = Math.Min(100, member.motivation + 10);
            Notify(member.name + "「今日もがんばります！」"); Save(); return true;
        }
        public bool StaffMeal(string id, string ingredientId)
        {
            if (!CanManage) return false;
            var member = State.staff.FirstOrDefault(x => x.id == id);
            var stock = State.stock.FirstOrDefault(x => x.ingredientId == ingredientId && x.quantity > 0);
            if (member == null || stock == null || member.mealDay == State.day) return false;
            stock.quantity--; member.mealDay = State.day;
            State.report.foodCostFen += stock.unitCostFen > 0 ? stock.unitCostFen : Cost(Catalog.Get(ingredientId));
            member.motivation = Math.Min(100, member.motivation + 15); member.fatigue = Math.Max(0, member.fatigue - 15);
            Notify(member.name + "にまかないを出しました。疲れが回復しました。"); Save(); return true;
        }
    }
}
