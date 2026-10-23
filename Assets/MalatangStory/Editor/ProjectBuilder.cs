using System;
using System.IO;
using MalatangStory.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MalatangStory.Editor
{
    public static class ProjectBuilder
    {
        [MenuItem("Malatang Story/Setup Project")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/MalatangStory/Settings");
            Directory.CreateDirectory("Assets/MalatangStory/Scenes");
            string pipelinePath="Assets/MalatangStory/Settings/RestaurantURP.asset";
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if(pipeline==null)
            {
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer,"Assets/MalatangStory/Settings/RestaurantRenderer.asset");
                pipeline=UniversalRenderPipelineAsset.Create(renderer);
                pipeline.renderScale=1;pipeline.msaaSampleCount=2;pipeline.shadowDistance=35;
                AssetDatabase.CreateAsset(pipeline,pipelinePath);
            }
            // iOS（Metal）では MSAA の標本数が最終合成と UI オーバーレイで食い違い描画パスが壊れるため、MSAA は使わずカメラの FXAA で補う。
            pipeline.msaaSampleCount=1;
            EditorUtility.SetDirty(pipeline);
            Directory.CreateDirectory("Assets/MalatangStory/Resources/Materials");
            CreateMaterialTemplates();
            const string focusPath="Assets/MalatangStory/Resources/RestaurantFoodFocus.asset";
            if(AssetDatabase.LoadAssetAtPath<VolumeProfile>(focusPath)==null)
            {
                var focusProfile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(focusProfile,focusPath);
                var focus=focusProfile.Add<DepthOfField>(true); focus.mode.Override(DepthOfFieldMode.Gaussian); focus.hideFlags=HideFlags.None;
                AssetDatabase.AddObjectToAsset(focus,focusProfile); EditorUtility.SetDirty(focusProfile);
            }
            var rendererData=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/MalatangStory/Settings/RestaurantRenderer.asset");
            if(rendererData!=null && rendererData.postProcessData==null)
            {
                // URP のポストエフェクト用シェーダー群。未設定だとブルーム等が描画されない。
                rendererData.postProcessData=AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                EditorUtility.SetDirty(rendererData);
            }
            if(rendererData!=null)
            {
                const string aoPath="Assets/MalatangStory/Resources/RestaurantSSAO.asset";
                var ao=AssetDatabase.LoadAssetAtPath<ScreenSpaceAmbientOcclusion>(aoPath);
                if(ao==null)
                {
                    ao=ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                    ao.name="Restaurant Ambient Occlusion";
                    AssetDatabase.CreateAsset(ao,aoPath);
                    var settings=new SerializedObject(ao);
                    settings.FindProperty("m_Settings.Downsample").boolValue=true;
                    settings.FindProperty("m_Settings.Intensity").floatValue=.65f;
                    settings.FindProperty("m_Settings.Radius").floatValue=.12f;
                    settings.FindProperty("m_Settings.Samples").intValue=2;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                if(!rendererData.rendererFeatures.Contains(ao))
                {
                    rendererData.rendererFeatures.Add(ao);
                    EditorUtility.SetDirty(rendererData);
                }
            }
            GraphicsSettings.defaultRenderPipeline=pipeline;
            QualitySettings.renderPipeline=pipeline;
            PlayerSettings.companyName="MalatangStory";PlayerSettings.productName="Malatang Story";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone,"com.malatangstory.game");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS,"com.malatangstory.game");
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft=true;PlayerSettings.allowedAutorotateToLandscapeRight=true;
            PlayerSettings.allowedAutorotateToPortrait=false;PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;
            PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.runInBackground=false;
            PlayerSettings.iOS.targetOSVersionString="15.0";
            PlayerSettings.bundleVersion="1.8.0";
            PlayerSettings.enableFrameTimingStats=true;
            var serialized=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input=serialized.FindProperty("activeInputHandler");if(input!=null)input.intValue=1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            const string scenePath="Assets/MalatangStory/Scenes/Restaurant.unity";
            if(!File.Exists(scenePath))
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                new GameObject("Malatang Story").AddComponent<GameApplication>();
                EditorSceneManager.SaveScene(scene,scenePath);
            }
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/MalatangStory/Scenes/Restaurant.unity",true)};
            AssetDatabase.SaveAssets();
            Debug.Log("MALATANG_SETUP_SUCCESS");
        }
        /// <summary>
        /// 実行時に複製して使うマテリアルの雛形を Resources に作る。
        /// プレイヤービルドでは参照されないシェーダー・キーワードの組み合わせが除去されるため、使う組み合わせを資産として残す。
        /// </summary>
        private static void CreateMaterialTemplates()
        {
            const string folder="Assets/MalatangStory/Resources/Materials/";
            Material Ensure(string name,string shader)
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(folder+name+".mat");
                if(material!=null) return material;
                material=new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material,folder+name+".mat");
                return material;
            }
            Ensure("RestaurantLit","Universal Render Pipeline/Lit");
            var transparent=Ensure("RestaurantLitTransparent","Universal Render Pipeline/Lit");
            ConfigureTransparent(transparent);
            var emissive=Ensure("RestaurantLitEmissive","Universal Render Pipeline/Lit");
            emissive.EnableKeyword("_EMISSION");emissive.SetColor("_EmissionColor",Color.white);
            emissive.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;
            var particles=Ensure("RestaurantParticles","Universal Render Pipeline/Particles/Unlit");
            ConfigureTransparent(particles);
            Ensure("RestaurantUnlit","Universal Render Pipeline/Unlit");
            foreach(var name in new[]{"RestaurantLit","RestaurantLitTransparent","RestaurantLitEmissive","RestaurantParticles","RestaurantUnlit"})
                EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Material>(folder+name+".mat"));
        }

        private static void ConfigureTransparent(Material material)
        {
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);
            material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite",0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType","Transparent");
            material.renderQueue=(int)RenderQueue.Transparent;
        }

        [MenuItem("Malatang Story/Build Mac")]
        public static void BuildMac()
        {
            Setup();Directory.CreateDirectory("Builds/Mac");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=new[]{"Assets/MalatangStory/Scenes/Restaurant.unity"},locationPathName="Builds/Mac/Malatang Story.app",
                target=BuildTarget.StandaloneOSX,options=BuildOptions.Development });
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Mac build failed: "+result.summary.result);
            Debug.Log("MALATANG_MAC_BUILD_SUCCESS");
        }
        [MenuItem("Malatang Story/Build Mac (Release)")]
        public static void BuildMacRelease()
        {
            Setup();Directory.CreateDirectory("Builds/MacRelease");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=new[]{"Assets/MalatangStory/Scenes/Restaurant.unity"},locationPathName="Builds/MacRelease/Malatang Story.app",
                target=BuildTarget.StandaloneOSX,options=BuildOptions.None });
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Mac release build failed: "+result.summary.result);
            Debug.Log("MALATANG_MAC_RELEASE_BUILD_SUCCESS");
        }
        /// <summary>iOS シミュレーター向けの開発ビルドを書き出す（自動プレイでの表示確認用）。実機向けの設定は元に戻す。</summary>
        [MenuItem("Malatang Story/Export iOS Simulator")]
        public static void BuildIOSSimulator()
        {
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS,BuildTarget.iOS))
                throw new InvalidOperationException("Unity HubでiOS Build Supportを追加してください。");
            Setup();
            var previous=PlayerSettings.iOS.sdkVersion;
            try
            {
                PlayerSettings.iOS.sdkVersion=iOSSdkVersion.SimulatorSDK;
                PlayerSettings.iOS.simulatorSdkArchitecture=AppleMobileArchitectureSimulator.ARM64;
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                    scenes=new[]{"Assets/MalatangStory/Scenes/Restaurant.unity"},locationPathName="Builds/iOSSimulator",
                    target=BuildTarget.iOS,options=BuildOptions.Development });
                if(result.summary.result!=BuildResult.Succeeded)throw new Exception("iOS simulator export failed.");
                Debug.Log("MALATANG_IOS_SIMULATOR_EXPORT_SUCCESS");
            }
            finally { PlayerSettings.iOS.sdkVersion=previous; AssetDatabase.SaveAssets(); }
        }
        [MenuItem("Malatang Story/Export iOS")]
        public static void BuildIOS()
        {
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS,BuildTarget.iOS))
                throw new InvalidOperationException("Unity HubでiOS Build Supportを追加してください。");
            Setup();
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=new[]{"Assets/MalatangStory/Scenes/Restaurant.unity"},locationPathName="Builds/iOS",
                target=BuildTarget.iOS,options=BuildOptions.None });
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("iOS export failed.");
        }
    }
}
