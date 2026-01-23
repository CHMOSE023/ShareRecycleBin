using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ShareRecycleBin
{
    public class FileScanner
    {
        private readonly string _rootPath;
        private readonly string _shadowPath; // 增加影子库路径对比
        private readonly HashSet<string> _whiteList;
        private readonly BlockingCollection<WatcherTask> _syncQueue;

        public FileScanner(string rootPath, string shadowPath, HashSet<string> whiteList, BlockingCollection<WatcherTask> syncQueue)
        {
            _rootPath = rootPath;
            _shadowPath = shadowPath;
            _whiteList = whiteList;
            _syncQueue = syncQueue;
        }

        public void Start(CancellationToken token)
        {
            Log.Information("[Scanner] 开始增量同步扫描 (跳过已存在的占位符)...");
            int scanCount = 0;
            int addCount = 0;

            try
            {
                // 使用递归方式手动处理目录，以便捕获并跳过“拒绝访问”的文件夹
                ScanDirectoryRecursive(new DirectoryInfo(PathHelper.ToLP(_rootPath)), token, ref scanCount, ref addCount);

                Log.Information("[Scanner] 扫描任务结束。共检查 {Scan} 个文件，新增排队 {Add} 个任务。", scanCount, addCount);
            }
            catch (Exception ex)
            {
                Log.Error("[Scanner] 扫描进程发生非预期中断: {Msg}", ex.Message);
            }
        }

        private void ScanDirectoryRecursive(DirectoryInfo dir, CancellationToken token, ref int scanCount, ref int addCount)
        {
            if (token.IsCancellationRequested) return;

            // 1. 处理当前目录下的文件
            try
            {
                foreach (var f in dir.GetFiles())
                {
                    if (token.IsCancellationRequested) return;
                    scanCount++;

                    // 检查是否在白名单
                    if (PathHelper.IsWhiteListed(f.FullName, _whiteList))
                    {
                        // 【核心改进】：检查影子库中是否已经存在该文件的“占位符”
                        string shadowFilePath = GetShadowPath(f.FullName);
                        if (!File.Exists(PathHelper.ToLP(shadowFilePath)))
                        {
                            if (_syncQueue.TryAdd(new WatcherTask { Action = WatcherAction.Created, FullPath = f.FullName }, 100))
                            {
                                addCount++;
                            }
                        }
                    }

                    // 性能调优：每扫描 100 个文件喘口气，降低对生产环境磁盘的压力
                    if (scanCount % 100 == 0) Thread.Sleep(100);
                }
            }
            catch (UnauthorizedAccessException)
            {
                Log.Warning("[Scanner] 权限不足，跳过文件扫描: {Path}", dir.FullName);
            }

            // 2. 递归处理子目录
            try
            {
                foreach (var subDir in dir.GetDirectories())
                {
                    ScanDirectoryRecursive(subDir, token, ref scanCount, ref addCount);
                }
            }
            catch (UnauthorizedAccessException)
            {
                Log.Warning("[Scanner] 权限不足，跳过文件夹: {Path}", dir.FullName);
            }
            catch (Exception ex)
            {
                Log.Debug("[Scanner] 读取目录出错 {Path}: {Msg}", dir.FullName, ex.Message);
            }
        }

        private string GetShadowPath(string fullPath)
        {
            // 复用之前的相对路径计算逻辑
            string relPath = PathHelper.GetRel(_rootPath, fullPath);
            return Path.Combine(_shadowPath, relPath);
        }
    }
}