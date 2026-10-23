using System;
using System.IO;
using System.Linq;
namespace MalatangStory.Game
{
    public sealed partial class GameApplication
    {
        private string campaignPath;
        private SaveRepository campaignSaves;
        public bool InChallenge => !string.IsNullOrEmpty(Simulation.State.challengeMode);
        public System.Collections.Generic.IReadOnlyList<ChallengeRecord> ChallengeRecords => (campaignSaves.Load()?.challengeRecords ?? Simulation.State.challengeRecords);
        public bool HasChallenge => File.Exists(campaignPath + ".challenge") || File.Exists(campaignPath + ".challenge.bak");
        public void StartChallenge(string mode, DateTime utc)
        {
            if (mode != ChallengeRules.Rush && mode != ChallengeRules.Weekly) return;
            if (!InChallenge && Started)
            {
                try { campaignSaves.Save(Simulation.State); }
                catch (Exception error) { SaveMessage = error.Message; UI.Refresh(); return; }
            }
            if (campaignSaves.Exists && campaignSaves.Load() == null)
            { SaveMessage = campaignSaves.LastError; UI.Refresh(); return; }
            int seed = mode == ChallengeRules.Rush ? ChallengeRules.DailySeed(utc) : ChallengeRules.WeeklySeed(utc);
            var state = ChallengeRules.Create(Catalog, mode, seed);
            CopyPreferences(Simulation.State, state);
            Saves = new SaveRepository(campaignPath + ".challenge");
            ResetSimulation(state); Started = true; Paused = false; SelectedOrderId = null;
            Save(); Open(GameScreen.Morning);
        }
        public void ResumeChallenge()
        {
            var repository = new SaveRepository(campaignPath + ".challenge");
            var state = repository.Load();
            if (state == null || string.IsNullOrEmpty(state.challengeMode))
            { SaveMessage = repository.LastError ?? "再開できる挑戦がありません。"; UI.Refresh(); return; }
            Saves = repository; ResetSimulation(state); Started = true; Paused = false;
            Open(state.phase == DayPhase.Report ? GameScreen.Report : state.phase == DayPhase.Preparation ? GameScreen.Morning : GameScreen.Store);
        }
        public void LeaveChallenge()
        {
            if (!InChallenge) return;
            Save();
            var campaign = campaignSaves.Load();
            if (campaignSaves.Exists && campaign == null) { SaveMessage = campaignSaves.LastError; UI.Refresh(); return; }
            if (campaign == null) campaign = new RestaurantSimulation(Catalog).State;
            if (Simulation.State.phase == DayPhase.Report)
            {
                ChallengeRules.Record(campaign, Simulation.State);
                try { campaignSaves.Save(campaign); }
                catch (Exception error) { SaveMessage = error.Message; UI.Refresh(); return; }
            }
            Saves = campaignSaves; ResetSimulation(campaign); Started = false; SelectedOrderId = null; Open(GameScreen.Title);
        }
        private static void CopyPreferences(GameState source, GameState target)
        {
            target.musicVolume = source.musicVolume; target.effectsVolume = source.effectsVolume;
            target.textSize = source.textSize; target.highQuality = source.highQuality;
            target.reducedMotion = source.reducedMotion; target.snapCamera = source.snapCamera;
            target.leftHanded = source.leftHanded; target.gaugeMode = source.gaugeMode;
            target.interactKey = source.interactKey; target.buildRotateKey = source.buildRotateKey;
        }
    }
}
