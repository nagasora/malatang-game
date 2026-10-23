#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
namespace MalatangStory.Editor
{
    public static class PhotoLibraryBuild
    {
        [PostProcessBuild(100)] public static void Configure(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(path, "Info.plist"); var plist = new PlistDocument(); plist.ReadFromFile(plistPath);
            plist.root.SetString("NSPhotoLibraryAddUsageDescription", "撮影した麻辣湯やお店の写真を写真ライブラリへ保存します。"); plist.WriteToFile(plistPath);
            string projectPath = PBXProject.GetPBXProjectPath(path); var project = new PBXProject(); project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Photos.framework", false); project.WriteToFile(projectPath);
        }
    }
}
#endif
