using System;
using System.ServiceProcess;
using System.Threading;

namespace ShareRecycleBin
{ 
    static class Program
    {
        static void Main(string[] args)
        {
            RecycleBinService service = new RecycleBinService();
            if (Environment.UserInteractive)
            {
                Console.WriteLine("正在以控制台方式启动调试...");
                typeof(RecycleBinService)
                    .GetMethod("OnStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(service, new object[] { args });
                Thread.Sleep(Timeout.Infinite);
            }
            else
            {
                ServiceBase.Run(new ServiceBase[] { service });
            }
        }
    }
}