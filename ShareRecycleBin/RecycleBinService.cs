using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace ShareRecycleBin
{
    /// <summary>
    /// 核心服务类：调度一切
    /// </summary>
    public class RecycleBinService : ServiceBase
    {
        private string ShareRoot, ShadowRoot, RecycleRoot;
        private HashSet<string> WhiteList;
        private int BufferSize, RecycleDays;
        private bool EnableCleanup;

        private BlockingCollection<WatcherTask> _priorityQueue = new BlockingCollection<WatcherTask>(20000);
        private BlockingCollection<WatcherTask> _syncQueue = new BlockingCollection<WatcherTask>(100000);
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private FileMonitor _monitor;
        private FileScanner _scanner;

        public RecycleBinService() { this.ServiceName = "SMBRecycleBinPro"; }

        protected override void OnStart(string[] args)
        {
            Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "log-.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                LoadConfiguration();
                PathHelper.EnsureDirectories(ShareRoot, ShadowRoot, RecycleRoot);

                // 分工启动
                _monitor = new FileMonitor(ShareRoot, BufferSize, WhiteList, _priorityQueue);
                _monitor.Start();

                _scanner = new FileScanner(ShareRoot, ShadowRoot,WhiteList, _syncQueue);

                Task.Run(() => _scanner.Start(_cts.Token));

                // 启动 4 个处理线程
                for (int i = 0; i < 4; i++)
                {
                    StartWorkerThread();
                }

                if (EnableCleanup)
                {
                    Task.Run(() => CleanupLoop(_cts.Token));
                }

                Log.Information("SMBRecycleBinPro 服务已就绪。");
            }
            catch (Exception ex) { Log.Fatal(ex, "服务无法启动"); Stop(); }
        }

        private void StartWorkerThread()
        {
            Task.Run(() => {
                var handler = new FileHandler(ShareRoot, ShadowRoot, RecycleRoot);
                while (!_cts.Token.IsCancellationRequested)
                {
                    WatcherTask task = null;
                    string tag = "";

                    if (_priorityQueue.TryTake(out task)) tag = "实时";
                    else if (_syncQueue.TryTake(out task, 50)) tag = "同步";

                    if (task != null)
                    {
                        try
                        {
                            switch (task.Action)
                            {
                                case WatcherAction.Created: handler.HandleCreate(task.FullPath); break;
                                case WatcherAction.Deleted: handler.HandleDelete(task.FullPath); break;
                                case WatcherAction.Renamed: handler.HandleRename(task.OldFullPath, task.FullPath); break;
                            }
                        }
                        catch (Exception ex) { Log.Error("[{Tag}] 处理异常 {Path}: {Msg}", tag, task.FullPath, ex.Message); }
                    }
                    else Thread.Sleep(20);
                }
            });
        }

        private void LoadConfiguration()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. 统一读取所有配置
            string sharePath = ConfigurationManager.AppSettings["ShareRoot"];
            string shadowPath = ConfigurationManager.AppSettings["ShadowRoot"];
            string recyclePath = ConfigurationManager.AppSettings["RecycleRoot"];

            // 2. 基础校验：如果没有读到 App.config，AppSettings 会是 null
            if (string.IsNullOrEmpty(sharePath))
            {
                Log.Fatal("无法读取配置！配置文件可能丢失或格式错误。预期路径：{Path}",
                          Path.Combine(baseDir, AppDomain.CurrentDomain.FriendlyName + ".config"));
                throw new Exception("Configuration error: ShareRoot is missing.");
            }

            // 3. 处理路径逻辑 (如果是相对路径则转为基于 .exe 的绝对路径)
            ShareRoot = Path.IsPathRooted(sharePath) ? sharePath : Path.GetFullPath(Path.Combine(baseDir, sharePath));
            ShadowRoot = Path.IsPathRooted(shadowPath) ? shadowPath : Path.GetFullPath(Path.Combine(baseDir, shadowPath));
            RecycleRoot = Path.IsPathRooted(recyclePath) ? recyclePath : Path.GetFullPath(Path.Combine(baseDir, recyclePath));

            // 4. 读取其他数值型配置
            EnableCleanup = bool.Parse(ConfigurationManager.AppSettings["EnableCleanup"] ?? "true");
            RecycleDays = int.Parse(ConfigurationManager.AppSettings["RecycleDays"] ?? "3");
            BufferSize = int.Parse(ConfigurationManager.AppSettings["WatcherBufferSizeKB"] ?? "64");
            WhiteList = new HashSet<string>((ConfigurationManager.AppSettings["WhiteList"] ?? "dwg,dxf,doc,docx,xls,xlsx,ppt,pptx")
                        .Split(',')
                        .Select(x => x.Trim().ToLower()));

            // 5. 最终验证
            if (string.IsNullOrEmpty(ShareRoot) || string.IsNullOrEmpty(ShadowRoot))
                throw new Exception("配置路径不能为空");

            Log.Information("配置信息: ShareRoot={Share}, ShadowRoot={Shadow}, RecycleRoot={Recycle}, EnableCleanup={EnableCleanup}, RecycleDays={RecycleDays}",  ShareRoot, ShadowRoot, RecycleRoot, EnableCleanup, RecycleDays);
            Log.Information("配置信息: WhiteList={WhiteList} ,BufferSize={BufferSize}", WhiteList, BufferSize);
        }

        private async Task CleanupLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!Directory.Exists(RecycleRoot))
                        continue;

                    // 删除所有子目录
                    foreach (var dir in Directory.GetDirectories(RecycleRoot))
                    {
                        try
                        {
                            Directory.Delete(dir, recursive: true);
                            Log.Information($"删除目录: {dir}");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, $"删除目录失败: {dir}");
                        }
                    }

                    // 删除根目录下的零散文件
                    foreach (var file in Directory.GetFiles(RecycleRoot))
                    {
                        try
                        {
                            File.Delete(file);
                            Log.Information($"删除文件: {file}");
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, $"删除文件失败: {file}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "清空回收站错误");
                }

                // 每 3 天执行一次“整仓清空”
                await Task.Delay(TimeSpan.FromDays(RecycleDays), token);
            }
        }

        protected override void OnStop()
        {
            _cts.Cancel();
            _monitor?.Stop();
            Log.Information("服务停止中...");
            Log.CloseAndFlush();
        }

    }
}
