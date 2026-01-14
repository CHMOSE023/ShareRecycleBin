using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
namespace ShareRecycleBin
{
    /// <summary>
    /// 监视逻辑类：负责实时增量变动
    /// </summary>
    public class FileMonitor : IDisposable
    {
        private FileSystemWatcher _watcher;
        private readonly string _path;
        private readonly int _bufferSize;
        private readonly HashSet<string> _whiteList;
        private readonly BlockingCollection<WatcherTask> _priorityQueue;

        public FileMonitor(string path, int bufferSize, HashSet<string> whiteList, BlockingCollection<WatcherTask> priorityQueue)
        {
            _path = path;
            _bufferSize = bufferSize;
            _whiteList = whiteList;
            _priorityQueue = priorityQueue;
        }

        public void Start()
        {
            if (_watcher != null) Stop();

            _watcher = new FileSystemWatcher(_path)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = _bufferSize * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };

            _watcher.Created += (s, e) => Enqueue(WatcherAction.Created, e.FullPath);
            _watcher.Deleted += (s, e) => Enqueue(WatcherAction.Deleted, e.FullPath);
            _watcher.Renamed += (s, e) => Enqueue(WatcherAction.Renamed, e.FullPath, e.OldFullPath);

            _watcher.Error += (s, e) => {
                Log.Error("[Monitor] Watcher 异常失效: {Msg}。尝试重启...", e.GetException().Message);
                Thread.Sleep(3000);
                Start();
            };

            _watcher.EnableRaisingEvents = true;
            Log.Information("[Monitor] 实时监视器已在路径 {Path} 启动。", _path);
        }

        private void Enqueue(WatcherAction action, string path, string oldPath = null)
        {
            // 删除操作不查白名单（文件已不在），其他必须符合白名单且不是忽略文件
            if (action != WatcherAction.Deleted)
            {
                if (!PathHelper.IsWhiteListed(path, _whiteList) || PathHelper.IsIgnored(path)) return;
            }

            // 放入高优先级实时队列
            if (!_priorityQueue.TryAdd(new WatcherTask { Action = action, FullPath = path, OldFullPath = oldPath }, 500))
            {
                Log.Warning("[Monitor] 实时队列满，丢失事件: {Path}", path);
            }
        }

        public void Stop() { _watcher?.Dispose(); }
        public void Dispose() => Stop();
    }
}
