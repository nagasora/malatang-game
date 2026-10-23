using System;
using System.IO;

namespace MalatangStory.Game
{
    /// <summary>開発用の撮影先を、明示した既存ディレクトリに限定する。</summary>
    public static class SmokeOutputPath
    {
        public static bool TryResolve(string value, out string resolved)
        {
            resolved = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (string segment in value.Replace('\\', '/').Split('/')) if (segment == "..") return false;
            try
            {
                string full = Path.GetFullPath(value);
                if (!Directory.Exists(full)) return false;
                resolved = full;
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is NotSupportedException || e is UnauthorizedAccessException) { return false; }
        }
    }
}
