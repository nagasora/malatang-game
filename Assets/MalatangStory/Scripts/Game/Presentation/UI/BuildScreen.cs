using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MalatangStory.Game
{
    /// <summary>
    /// 内装画面。真上からの視点で家具を選んで置く・動かす・回す・売る。
    /// 置ける場所は緑、置けない場所は赤のゴーストで示す。
    /// </summary>
    public sealed class BuildScreen : UIScreen
    {
        private string placingDef;
        private int movingUid = -1, selectedUid = -1, rotation;
        private RectTransform catalog, selectionPanel;
        private Text info, selectionText;
        private Vector2 pointerDown;
        private bool pressed;

        public override bool ShowHud => true;

        protected override void Build(RectTransform root)
        {
            var bottom = UIKit.Panel(root, new Color(.99f, .96f, .89f, .97f), 22, true, "Catalog Panel").rectTransform;
            bottom.anchorMin = new Vector2(0, 0); bottom.anchorMax = new Vector2(1, 0); bottom.pivot = new Vector2(.5f, 0);
            bottom.offsetMin = new Vector2(12, 12); bottom.offsetMax = new Vector2(-12, 196);
            info = UIKit.Label(bottom, "", 17, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place(info.rectTransform, new Vector2(0, 1), new Vector2(18, -6), new Vector2(760, 30));
            var done = UIKit.Button(bottom, "完了", () => { App.World.ClearBuildGhost(); Back(); }, ButtonStyle.Primary, 18, IconKind.Check);
            UIKit.Place(done.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-12, -6), new Vector2(130, 40));
            var rotate = UIKit.Button(bottom, "回転", () => { rotation = 1 - rotation; }, ButtonStyle.Secondary, 16, IconKind.Rotate);
            UIKit.Place(rotate.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(-152, -6), new Vector2(110, 40));
            var area = UIKit.Rect("Area", bottom);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = new Vector2(10, 8); area.offsetMax = new Vector2(-10, -50);
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            area.gameObject.AddComponent<RectMask2D>();
            scroll.vertical = false; scroll.horizontal = true;
            catalog = UIKit.Rect("Content", area);
            catalog.anchorMin = new Vector2(0, 0); catalog.anchorMax = new Vector2(0, 1); catalog.pivot = new Vector2(0, .5f);
            catalog.offsetMin = Vector2.zero; catalog.offsetMax = Vector2.zero;
            var layout = catalog.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10; layout.childControlWidth = false; layout.childControlHeight = true; layout.childForceExpandHeight = true;
            catalog.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = catalog; scroll.viewport = area;
            RebuildCatalog();

            selectionPanel = UIKit.Panel(root, UIKit.Paper, 18, true, "Selection").rectTransform;
            UIKit.Place(selectionPanel, new Vector2(1, 1), new Vector2(-14, -90), new Vector2(320, 160));
            selectionText = UIKit.Label(selectionPanel, "", 18, UIKit.Ink, TextAnchor.UpperLeft, true);
            UIKit.Fill(selectionText.rectTransform, 16, 16, 12, 70);
            var move = UIKit.Button(selectionPanel, "移動", () => { movingUid = selectedUid; rotation = S.furniture.First(f => f.uid == selectedUid).rotation; selectedUid = -1; }, ButtonStyle.Secondary, 17, IconKind.Hammer);
            UIKit.Place(move.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(14, 12), new Vector2(140, 46));
            var sell = UIKit.Button(selectionPanel, "売却", () => { if (Sim.SellFurniture(selectedUid)) selectedUid = -1; }, ButtonStyle.Danger, 17, IconKind.Coin);
            UIKit.Place(sell.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(-14, 12), new Vector2(140, 46));
            if (!Sim.CanManage)
            {
                var warn = UIKit.Label(root, "内装は営業前か閉店後に変更できます。", 22, UIKit.Bad, TextAnchor.MiddleCenter, true);
                UIKit.Place(UIKit.Outlined(warn, Color.white).rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(700, 60));
            }
        }

        private void Back()
        {
            if (S.phase == DayPhase.Preparation) App.Open(GameScreen.Morning); else App.BackToStore();
        }

        private void RebuildCatalog()
        {
            Clear(catalog);
            foreach (var theme in Content.Themes) ThemeCard(theme);
            foreach (var def in Content.Furniture) CatalogCard(def);
        }

        /// <summary>内装テーマのカード。買うとすぐに模様替えされ、購入済みなら無料で切り替えられる。</summary>
        private void ThemeCard(ThemeDefinition theme)
        {
            bool owned = S.ownedThemes.Contains(theme.Id), applied = S.theme == theme.Id, unlocked = S.chapter >= theme.ChapterRequired;
            var card = UIKit.Panel(catalog, applied ? new Color(.86f, .95f, .88f) : unlocked ? new Color(1, .97f, .9f) : new Color(.88f, .85f, .8f), 14, true, "Theme " + theme.Id);
            card.rectTransform.sizeDelta = new Vector2(210, 0);
            var button = card.gameObject.AddComponent<Button>();
            button.interactable = unlocked && !applied && Sim.CanManage && S.tutorialComplete;
            string id = theme.Id;
            button.onClick.AddListener(() => { UIKit.ClickSound?.Invoke(); if (Sim.BuyTheme(id)) RebuildCatalog(); });
            card.gameObject.AddComponent<PressFeedback>();
            var stripe = UIKit.Panel(card.transform, UIKit.Jade, 14, false, "Stripe");
            UIKit.Place(stripe.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(210, 24));
            var tag = UIKit.Label(stripe.rectTransform, "内装テーマ", 13, UIKit.Paper, TextAnchor.MiddleCenter, true);
            UIKit.Fill(tag.rectTransform);
            var name = UIKit.Label(card.transform, theme.Name, 16, UIKit.Ink, TextAnchor.UpperLeft, true);
            UIKit.Fill(name.rectTransform, 10, 8, 28, 64);
            string status = applied ? "使用中　雰囲気+" + theme.Ambience
                : owned ? "購入済み・タップで模様替え"
                : unlocked ? RestaurantSimulation.Money(theme.PriceFen) + "　雰囲気+" + theme.Ambience
                : "物語の第" + theme.ChapterRequired + "章で解禁";
            var detail = UIKit.Label(card.transform, status, 14, unlocked ? UIKit.InkSoft : UIKit.Bad, TextAnchor.LowerLeft);
            UIKit.Fill(detail.rectTransform, 10, 8, 50, 8);
        }

        private void CatalogCard(FurnitureDefinition def)
        {
            bool unlocked = S.shopLevel >= def.UnlockLevel;
            var card = UIKit.Panel(catalog, unlocked ? Color.white : new Color(.88f, .85f, .8f), 14, true, def.Name);
            card.rectTransform.sizeDelta = new Vector2(190, 0);
            var button = card.gameObject.AddComponent<Button>();
            button.interactable = unlocked && Sim.CanManage;
            string id = def.Id;
            button.onClick.AddListener(() => { UIKit.ClickSound?.Invoke(); placingDef = id; movingUid = -1; selectedUid = -1; rotation = 0; });
            card.gameObject.AddComponent<PressFeedback>();
            var name = UIKit.Label(card.transform, def.Name, 16, UIKit.Ink, TextAnchor.UpperLeft, true);
            UIKit.Fill(name.rectTransform, 10, 8, 6, 64);
            string kind = def.Kind == FurnitureKind.Table ? def.Seats + "席" : def.Kind == FurnitureKind.Wall ? "壁飾り" : def.Width + "×" + def.Depth;
            var detail = UIKit.Label(card.transform, unlocked ? RestaurantSimulation.Money(def.PriceFen) + "\n" + kind + "　雰囲気+" + def.Ambience : "店舗Lv." + def.UnlockLevel + "で解禁", 14, unlocked ? UIKit.InkSoft : UIKit.Bad, TextAnchor.LowerLeft);
            UIKit.Fill(detail.rectTransform, 10, 8, 50, 8);
        }

        public override void Tick(float deltaTime)
        {
            int seats = DiningLayout.SeatCount(S);
            info.text = (placingDef != null ? "置く場所をタップ：" + Content.FurnitureById(placingDef).Name : movingUid >= 0 ? "移動先をタップしてください" : "家具を選んで置く／置いた家具をタップで選択")
                + "　客席 " + seats + "　雰囲気 " + DiningLayout.Ambience(S) + "　所持金 " + RestaurantSimulation.Money(S.moneyFen);
            var selected = S.furniture.FirstOrDefault(f => f.uid == selectedUid);
            selectionPanel.gameObject.SetActive(selected != null);
            if (selected != null)
            {
                var def = Content.FurnitureById(selected.defId);
                selectionText.text = def.Name + "\n売却で " + RestaurantSimulation.Money(def.PriceFen / 2) + " 戻ります";
            }
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) rotation = 1 - rotation;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { placingDef = null; movingUid = -1; }
            var pointer = Pointer.current;
            if (pointer == null) return;
            Vector2 position = pointer.position.ReadValue();
            string ghostDef = placingDef ?? (movingUid >= 0 ? S.furniture.FirstOrDefault(f => f.uid == movingUid)?.defId : null);
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointer.deviceId);
            var pick = App.World.PickBuild(position);
            if (ghostDef != null && pick.valid)
            {
                var def = Content.FurnitureById(ghostDef);
                int x = def.Kind == FurnitureKind.Wall ? pick.wallSlot : pick.column, z = pick.row;
                bool ok = (def.Kind != FurnitureKind.Wall || pick.wallSlot >= 0) && DiningLayout.CanPlace(S, ghostDef, x, z, rotation, movingUid, out _);
                App.World.SetBuildGhost(ghostDef, x, z, rotation, ok);
            }
            else App.World.ClearBuildGhost();
            if (pointer.press.wasPressedThisFrame && !overUI) { pressed = true; pointerDown = position; }
            if (!pressed || !pointer.press.wasReleasedThisFrame) return;
            pressed = false;
            if (Vector2.Distance(position, pointerDown) > 18 || !Sim.CanManage) return;
            if (ghostDef != null && pick.valid)
            {
                var def = Content.FurnitureById(ghostDef);
                int x = def.Kind == FurnitureKind.Wall ? pick.wallSlot : pick.column;
                bool success = movingUid >= 0 ? Sim.MoveFurniture(movingUid, x, pick.row, rotation) : Sim.BuyFurniture(ghostDef, x, pick.row, rotation);
                if (success && movingUid >= 0) movingUid = -1;
                if (success && placingDef != null && S.moneyFen < def.PriceFen) placingDef = null;
                return;
            }
            if (pick.furnitureUid >= 0) selectedUid = pick.furnitureUid;
            else selectedUid = -1;
        }

        public override void Close()
        {
            App.World.ClearBuildGhost();
            base.Close();
        }
    }
}
