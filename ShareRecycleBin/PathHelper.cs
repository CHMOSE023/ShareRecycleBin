using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShareRecycleBin
{
    /// <summary>
    ///  辅助工具类：静态路径处理
    /// </summary>
    public static class PathHelper
    {
        public static string ToLP(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith(@"\\?\")) return path;
            string full = Path.GetFullPath(path);
            return full.StartsWith(@"\\") ? @"\\?\UNC\" + full.Substring(2) : @"\\?\" + full;
        }

        public static string GetRel(string baseDir, string fullPath)
        {
            string cb = baseDir.Replace(@"\\?\", "").Replace(@"UNC\", "").TrimEnd('\\') + "\\";
            string cf = fullPath.Replace(@"\\?\", "").Replace(@"UNC\", "");
            return cf.StartsWith(cb, StringComparison.OrdinalIgnoreCase) ? cf.Substring(cb.Length) : cf;
        }

        public static bool IsIgnored(string p)
        {
            string name = Path.GetFileName(p);
            if (string.IsNullOrEmpty(name)) return true;
            return name.StartsWith("~$") || name.StartsWith("~WRL") || (new[] { ".tmp", ".bak", ".lnk" }).Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsWhiteListed(string p, HashSet<string> whiteList)
        {
            try { return whiteList.Contains(Path.GetExtension(p).TrimStart('.').ToLower()); }
            catch { return false; }
        }

        public static void EnsureDirectories(params string[] paths)
        {
            foreach (var p in paths) if (!Directory.Exists(ToLP(p))) Directory.CreateDirectory(ToLP(p));
        }

        public static void EnsureParentDir(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(ToLP(dir))) Directory.CreateDirectory(ToLP(dir));
        }

        public static string GetUniquePath(string path, bool isDir)
        {
            if (isDir ? !Directory.Exists(ToLP(path)) : !File.Exists(ToLP(path))) return path;
            string ts = DateTime.Now.ToString("_yyyyMMdd_HHmmss");
            return isDir ? path + ts : Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ts + Path.GetExtension(path));
        }
    }
}
