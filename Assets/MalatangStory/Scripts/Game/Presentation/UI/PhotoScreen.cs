using UnityEngine;
using UnityEngine.UI;
namespace MalatangStory.Game
{
    public sealed class PhotoScreen : UIScreen
    {
        public override bool PausesGame => true;
        public override bool Modal => false;
        public override bool ShowHud => false;
        private RectTransform controls;
        private Text stamp, filterText, status;
        private int filter, stampIndex;
        private bool saving;
        protected override void Build(RectTransform root)
        {
            controls = UIKit.Panel(root, UIKit.Paper, 18, true, "Photo Controls").rectTransform;
            UIKit.Place(controls, new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(1080, 156));
            var row = UIKit.Rect("Photo Actions", controls); UIKit.Place(row, new Vector2(0, 1), new Vector2(12, -10), new Vector2(1056, 52));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childForceExpandWidth = true;
            void Button(string text, System.Action action) => UIKit.Button(row, text, action, ButtonStyle.Secondary, 18);
            Button("← 回す", () => App.World.PhotoYaw -= 15); Button("回す →", () => App.World.PhotoYaw += 15);
            Button("上から", () => App.World.PhotoPitch = Mathf.Min(75, App.World.PhotoPitch + 10));
            Button("低く", () => App.World.PhotoPitch = Mathf.Max(15, App.World.PhotoPitch - 10));
            Button("寄る", () => App.World.PhotoDistance = Mathf.Max(.8f, App.World.PhotoDistance * .8f));
            Button("引く", () => App.World.PhotoDistance = Mathf.Min(30, App.World.PhotoDistance * 1.25f));
            Button("戻る", App.EndPhoto);
            var options = UIKit.Rect("Photo Options", controls); UIKit.Place(options, new Vector2(0, 1), new Vector2(12, -70), new Vector2(1056, 48));
            var buttons = options.gameObject.AddComponent<HorizontalLayoutGroup>(); buttons.spacing = 10; buttons.childForceExpandWidth = true;
            var change = UIKit.Button(options, "フィルター：なし", () => { filter = (filter + 1) % 3; filterText.text = "フィルター：" + new[] { "なし", "あたたかい", "モノクロ" }[filter] + "（保存時）"; }, ButtonStyle.Secondary, 18);
            filterText = change.GetComponentInChildren<Text>();
            UIKit.Button(options, "スタンプ切替", () => { stampIndex = (stampIndex + 1) % 3; stamp.text = stampIndex == 0 ? "" : stampIndex == 1 ? "手づくりの一杯" : S.storeName; }, ButtonStyle.Gold, 18);
            UIKit.Button(options, Application.platform == RuntimePlatform.IPhonePlayer ? "写真に保存" : "PNGに保存", () => {
                if (saving) return; saving = true;
                PhotoExportService.Save(App, filter, () => { if (controls != null) controls.gameObject.SetActive(false); }, () => { if (controls != null) controls.gameObject.SetActive(true); }, message => { saving = false; if (status != null) status.text = message; });
            }, ButtonStyle.Primary, 18, IconKind.Check);
            status = UIKit.Label(controls, "ドラッグなしで角度・距離を選べます。UIは保存する写真に写りません。", 14, UIKit.InkSoft, TextAnchor.LowerLeft);
            UIKit.Place(status.rectTransform, new Vector2(0, 0), new Vector2(14, 5), new Vector2(1040, 26));
            stamp = UIKit.Outlined(UIKit.Label(root, "", 32, UIKit.Paper, TextAnchor.UpperRight, true), UIKit.Lacquer, 2);
            UIKit.Place(stamp.rectTransform, new Vector2(1, 1), new Vector2(-28, -28), new Vector2(680, 70));
        }
    }
}
