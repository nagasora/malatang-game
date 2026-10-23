using System;
using System.IO;
namespace MalatangStory.Game
{
    public sealed partial class GameApplication
    {
        public bool StartFranchise()
        {
            if (InChallenge || !Simulation.CanManage || Simulation.State.chapter < Chapters.All.Length) return false;
            var previous = Simulation.State;
            var next = FranchiseRules.Create(Catalog, previous, "franchise-" + Guid.NewGuid().ToString("N"));
            CopyPreferences(previous, next);
            string archivePath = campaignPath + ".archive-" + Guid.NewGuid().ToString("N") + ".json";
            try
            {
                var archive = new SaveRepository(archivePath); archive.Save(previous);
                var restored = archive.Load();
                if (restored == null || restored.storeId != previous.storeId || restored.stats.served != previous.stats.served)
                    throw new IOException("前の店舗のアーカイブを検証できませんでした。");
                campaignSaves.Save(next);
            }
            catch (Exception error) { SaveMessage = "のれん分けを完了できませんでした：" + error.Message; UI.Refresh(); return false; }
            Saves = campaignSaves; ResetSimulation(next); Started = true; SelectedOrderId = null;
            SaveMessage = "前の店舗を保存しました：" + Path.GetFileName(archivePath);
            Open(GameScreen.Morning); return true;
        }
    }
}
