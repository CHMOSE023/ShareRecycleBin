# Windows  SMB 共享文件 回收站

## 1. 项目简介
ShareRecycleBin 是一个专为 Windows Server (特别是 SMB 共享环境) 设计的增量回收站服务。它通过硬链接 (Hard Link) 技术实现对文件的“即时备份”，并能在文件被删除时将其移动到指定的回收站目录，同时保留原始的目录结构和权限锁定。

## 2. 核心技术架构
项目采用解耦生产者-消费者模型，通过双优先级队列解决全量扫描与实时监控之间的 IO 冲突。

逻辑组件分工：
- FileMonitor (实时监视器)：捕捉文件系统的 Created, Deleted, Renamed 事件，享有高优先级。

- FileScanner (全量扫描器)：服务启动时扫描存量文件，享有低优先级，带 IO 冷却逻辑。

- FileHandler (业务处理器)：执行具体的 Win32 API 调用（硬链接创建、文件移动、ACL 修改）。

- PathHelper (路径助手)：处理 \\?\ 长路径转换及相对路径计算。

## 3. 文件列表说明
|文件名 |	类型	| 说明|
| :--  | :--: | :--  |
|App.config        | 	配置文件	| 存储共享路径、影子库路径、回收站路径及扩展名白名单。| 
|Program.cs	       | 入口类	| 负责服务的注册、启动逻辑以及控制台调试模式切换。| 
|RecycleBinService.cs| 	服务类	| 继承自 ServiceBase，协调监控、扫描与消费线程的生命周期。| 
|WatcherTask.cs	| 模型类	| 定义 WatcherAction 枚举和 WatcherTask 任务对象。| 
|FileMonitor.cs	| 逻辑类	| 封装 FileSystemWatcher，负责将实时事件压入高优先级队列。| 
|FileScanner.cs	| 逻辑类	| 负责背景全量扫描，将任务压入低优先级队列。| 
|FileHandler.cs	| 业务类	| 包含硬链接创建、文件移动及权限锁定等核心 IO 操作。| 
|PathHelper.cs	| 工具类	| 静态方法库，处理长路径支持、白名单过滤及路径转换。| 

## 4. 关键配置项 (App.config)
```xml
 <appSettings>
    <add key="ShareRoot" value="D:\协同文件" />
    <add key="ShadowRoot" value="D:\.ShadowIndex" />
    <add key="RecycleRoot" value="D:\回收站" />
    <add key="WhiteList" value="dwg,dxf,doc,docx,xls,xlsx,ppt,pptx,pdf,txt,zip,rar,7z,jpg,png" />
    <add key="WatcherBufferSizeKB" value="64" />
    <add key="EnableCleanup" value="false"/>
    <add key="RecycleDays" value="30"/>
  </appSettings>
```
## 5. 开发与部署
编译环境
- .NET Framework 4.7.2+ 或 .NET 6/8

- Visual Studio 2022

- NuGet 包：Serilog, Serilog.Sinks.File, Serilog.Sinks.Console

Git 常规操作流

**如果您在开发过程中需要同步代码，请参考以下操作：**

1. 拉取最新代码：git pull origin master
2. 查看修改状态：git status
3. 提交修改：

```bash
git add .
git commit -m "feat: 增加对硬链接创建失败的重试逻辑"
```

## 6. 注意事项

1. 分区限制：ShadowRoot 必须与 ShareRoot 位于同一磁盘分区，否则 CreateHardLink 将失败。
2. 性能优化：在处理超过 100 万个文件时，建议将 WatcherBufferSizeKB 调至 128。
3. 权限：服务运行账户（如 LocalSystem）必须对源目录和目标目录拥有完整的读写及修改权限的权限（Full Control）。

## 7. 服务安装与启动指南

1. 请以 管理员身份 运行 SMBRecycleBin.exe
2. 使用 sc.exe 指令安装（推荐）
 - 安装服务
 
    ```bash
    # 注意：binPath= 后面有一个空格，路径建议使用双引号包裹
    sc.exe create SMBRecycleBin binPath= "C:\RecycleBinService\ShareRecycleBin.exe" start= auto displayname= "SMB共享回收站增强服务"
    ```

 - 启动服务

   ```bash
    sc.exe start SMBRecycleBin
   ```

- 卸载服务

   ```bash
    sc.exe delete SMBRecycleBin
   ```
- 停止服务

   ```bash
    sc.exe stop SMBRecycleBin
   ```
