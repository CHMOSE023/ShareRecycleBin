using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace ShareRecycleBin
{
    /// <summary>
    /// 业务处理类：执行具体文件操作
    /// </summary>
    public class FileHandler
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool MoveFileW(string lpExistingFileName, string lpNewFileName);

        private readonly string _share, _shadow, _recycle;

        public FileHandler(string share, string shadow, string recycle)
        {
            _share = share; _shadow = shadow; _recycle = recycle;
        }

        public void HandleCreate(string sourcePath)
        {
            string shadowPath = GetShadowPath(sourcePath);
            if (File.Exists(PathHelper.ToLP(shadowPath))) return;

            for (int i = 0; i < 3; i++)
            {
                if (!File.Exists(PathHelper.ToLP(sourcePath))) return;
                PathHelper.EnsureParentDir(shadowPath);
                if (CreateHardLinkW(PathHelper.ToLP(shadowPath), PathHelper.ToLP(sourcePath), IntPtr.Zero)) return;
                Thread.Sleep(500);
            }
        }

        public void HandleDelete(string fullPath)
        {
            if (PathHelper.IsIgnored(fullPath))
            {
                string sp = PathHelper.ToLP(GetShadowPath(fullPath));
                try { if (File.Exists(sp)) File.Delete(sp); } catch { }
                return;
            }

            string shadowPath = GetShadowPath(fullPath);
            string baseTarget = Path.Combine(_recycle, PathHelper.GetRel(_share, fullPath));
            string finalPath = "";
            bool isDir = false;

            string lpShadow = PathHelper.ToLP(shadowPath);
            if (Directory.Exists(lpShadow)) { finalPath = PathHelper.GetUniquePath(baseTarget, true); isDir = true; }
            else if (File.Exists(lpShadow)) { finalPath = PathHelper.GetUniquePath(baseTarget, false); isDir = false; }

            if (!string.IsNullOrEmpty(finalPath))
            {
                PathHelper.EnsureParentDir(finalPath);
                if (MoveFileW(lpShadow, PathHelper.ToLP(finalPath)))
                {
                    Log.Information("[Recycled] {Path}", finalPath);
                    // 文件删除后, 权限不变
                    //ApplySecurity(finalPath, isDir);
                }
            }
        }

        public void HandleRename(string oldPath, string newPath)
        {
            string oldShadow = PathHelper.ToLP(GetShadowPath(oldPath));
            string newShadow = PathHelper.ToLP(GetShadowPath(newPath));
            if (File.Exists(oldShadow) || Directory.Exists(oldShadow))
            {
                PathHelper.EnsureParentDir(GetShadowPath(newPath));
                MoveFileW(oldShadow, newShadow);
            }
            else { HandleCreate(newPath); }
        }

        private void ApplySecurity(string path, bool isDir)
        {
            try
            {
                string lp = PathHelper.ToLP(path);
                FileSystemSecurity security = isDir ? (FileSystemSecurity)Directory.GetAccessControl(lp) : (FileSystemSecurity)File.GetAccessControl(lp);
                security.SetAccessRuleProtection(true, false);
                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                var inherit = isDir ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
                security.AddAccessRule(new FileSystemAccessRule(adminSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(everyoneSid, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
                if (isDir) Directory.SetAccessControl(lp, (DirectorySecurity)security);
                else { File.SetAccessControl(lp, (FileSecurity)security); File.SetAttributes(lp, FileAttributes.ReadOnly); }
            }
            catch { }
        }

        private string GetShadowPath(string path) => Path.Combine(_shadow, PathHelper.GetRel(_share, path));
    }
}
