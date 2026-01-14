using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShareRecycleBin
{
    // ==========================================
    // 1. 模型层：定义任务载体
    // ==========================================
    public enum WatcherAction { Created, Deleted, Renamed }
    public class WatcherTask
    {
        public WatcherAction Action;
        public string FullPath;
        public string OldFullPath;
    }
}
