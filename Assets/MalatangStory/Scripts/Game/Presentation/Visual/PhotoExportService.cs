using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
namespace MalatangStory.Game
{
    public sealed class PhotoExportService : MonoBehaviour
    {
        public static string OutputDirectoryOverride;
        private Action<string> completed;
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void MalatangSavePhoto(string path, string callbackObject);
#endif
        public static void Save(GameApplication app, int filter, Action hide, Action restore, Action<string> done)
        {
            var root = new GameObject("Photo Export " + Guid.NewGuid().ToString("N")); root.transform.SetParent(app.transform);
            var service = root.AddComponent<PhotoExportService>(); service.completed = done;
            service.StartCoroutine(service.Capture(app, filter, hide, restore));
        }
        private IEnumerator Capture(GameApplication app, int filter, Action hide, Action restore)
        {
            hide(); app.UI.SetPhotoOverlaysVisible(false);
            yield return null;
            Texture2D image = null; string path = null, error = null;
            try
            {
                image = RenderPhoto(app);
                var pixels = image.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var tone = PhotoFilter.Apply(pixels[i].r, pixels[i].g, pixels[i].b, filter);
                    pixels[i].r = tone.R; pixels[i].g = tone.G; pixels[i].b = tone.B;
                }
                image.SetPixels32(pixels); image.Apply();
                string directory = OutputDirectoryOverride ?? Path.Combine(Application.persistentDataPath, "Photos");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "Malatang-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            catch (Exception exception) { error = "写真を保存できませんでした：" + exception.Message; }
            finally { if (image != null) Destroy(image); app.UI.SetPhotoOverlaysVisible(true); restore(); }
            if (error != null) { Finish(error); yield break; }
#if UNITY_IOS && !UNITY_EDITOR
            MalatangSavePhoto(path, gameObject.name);
#else
            Finish("写真を保存しました：" + path);
#endif
        }
        private static Texture2D RenderPhoto(GameApplication app)
        {
            Camera camera = app.World.View;
            Canvas canvas = app.UI.GetComponentInChildren<Canvas>();
            RenderMode previousMode = canvas.renderMode; Camera previousCamera = canvas.worldCamera; float previousPlane = canvas.planeDistance;
            RenderTexture previousActive = RenderTexture.active, previousTarget = camera.targetTexture;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D image = null;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .5f; Canvas.ForceUpdateCanvases();
                var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
                else { camera.targetTexture = target; camera.Render(); }
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                var result = image; image = null; return result;
            }
            finally
            {
                if (image != null) Destroy(image);
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousPlane;
                Canvas.ForceUpdateCanvases(); RenderTexture.ReleaseTemporary(target);
            }
        }
        public void OnPhotoSaved(string status)
            => Finish(status == "ok" ? "写真ライブラリに保存しました。" : status == "denied" ? "写真への追加が許可されませんでした。PNGはアプリ内のPhotosフォルダに残しています。" : "写真ライブラリへの保存に失敗しました。PNGはアプリ内に残しています。");
        private void Finish(string message) { completed?.Invoke(message); Destroy(gameObject); }
    }
}
