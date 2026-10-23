using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
namespace MalatangStory.Game
{
    public sealed class TitleBowlPreview : MonoBehaviour
    {
        private const int Layer = 29;
        private const uint LightLayer = 1u << 9;
        private GameObject studio, bowl;
        private Camera cameraView;
        private RenderTexture texture;
        private GameApplication app;
        private bool rendered;
        private int frame;
        public void Initialize(GameApplication application, RawImage image)
        {
            app = application;
            studio = new GameObject("Title Bowl Studio"); studio.transform.SetParent(app.World.transform, false); studio.transform.position = new Vector3(500, -500, 500);
            var order = new CustomerOrder { id = "title-preview", chosenSoup = "mala", chosenSpice = 2, stage = OrderStage.Served,
                selected = new List<string> { "beef_slice", "bok_choy", "enoki", "rice_noodle" }, quality = 96 };
            bowl = app.World.Kitchen.CreateServedBowl(order, Vector3.zero); bowl.transform.SetParent(studio.transform, false); bowl.transform.localPosition = Vector3.zero;
            foreach (Transform item in bowl.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = Layer;
            foreach (Renderer renderer in bowl.GetComponentsInChildren<Renderer>(true)) renderer.renderingLayerMask = LightLayer;
            var lightObject = new GameObject("Title Bowl Softbox"); lightObject.transform.SetParent(studio.transform, false); lightObject.transform.localPosition = new Vector3(-.4f, .8f, -.5f);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Point; light.intensity = 1.1f; light.range = 3; light.color = new Color(1, .94f, .84f); light.cullingMask = 1 << Layer; light.renderingLayerMask = (int)LightLayer;
            var cameraObject = new GameObject("Title Bowl Camera"); cameraObject.transform.SetParent(studio.transform, false);
            cameraView = cameraObject.AddComponent<Camera>(); cameraView.enabled = false; cameraView.cullingMask = 1 << Layer;
            cameraView.clearFlags = CameraClearFlags.SolidColor; cameraView.backgroundColor = Color.clear; cameraView.nearClipPlane = .01f; cameraView.farClipPlane = 3; cameraView.fieldOfView = 32; cameraView.aspect = 1;
            cameraView.transform.localPosition = new Vector3(0, .4f, -.6f); cameraView.transform.LookAt(studio.transform.position + Vector3.up * .1f);
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.renderShadows = false;
            texture = new RenderTexture(384, 384, 24, RenderTextureFormat.ARGB32); texture.Create(); image.texture = texture;
        }
        private void Update()
        {
            if (cameraView == null || texture == null || app == null || rendered && UIKit.ReduceMotion) return;
            int interval = app.Simulation.State.highQuality ? 4 : 8;
            if (rendered && ++frame % interval != 0) return;
            if (!UIKit.ReduceMotion) bowl.transform.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * .3f) * 5, Mathf.Sin(Time.time * .8f) * .7f);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
            if (RenderPipeline.SupportsRenderRequest(cameraView, request)) RenderPipeline.SubmitRenderRequest(cameraView, request);
            else { cameraView.targetTexture = texture; cameraView.Render(); }
            rendered = true;
        }
        private void OnDestroy()
        {
            if (studio != null) Destroy(studio);
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
