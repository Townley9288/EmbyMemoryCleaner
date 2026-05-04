# EmbyMemoryCleaner

[![Version](https://img.shields.io/badge/version-1.0.1.0-blue.svg)](#)
[![.NET](https://img.shields.io/badge/.NET-6.0-512BD4.svg)](#)
[![Emby](https://img.shields.io/badge/Emby-4.9%2B-52B54B.svg)](#)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](#)

一个轻量级 Emby Server 插件，**专门解决 Emby 长期运行内存膨胀问题**。

| | |
|---|---|
| **DLL 大小** | ~ 460 KB（含嵌入资源） |
| **Plugin GUID** | `c1f20f3a-7d2c-4d5e-9b21-2a8f0e6e9c11` |
| **目标 Emby 版本** | 4.9+ |
| **兼容平台** | Windows / Linux (glibc & musl) / Docker |

---

## ✨ 它做了什么

每隔 N 分钟（默认 30）执行一轮：

| 步骤 | 作用 |
|---|---|
| ① 强制 Full GC（Gen 0/1/2） | 回收所有可达的垃圾对象 |
| ② LOH 压缩（`CompactOnce`） | 解决大对象堆碎片化（**.NET 内存膨胀头号元凶**） |
| ③ 释放工作集回 OS | `SetProcessWorkingSetSize` (Win) / `malloc_trim` (Linux glibc) |

> ⚠️ Alpine / musl libc 容器：自动检测，跳过 `malloc_trim`，仅依赖 GC。

### 配置页面截图

包含 **运行状态面板**：当前托管内存、当前进程 RSS、上次清理时间、上次释放量、使用方法。

---

## 📦 安装

### 方式 A：直接复制 DLL（最简单）

1. 从 [Releases](#) 下载最新版 `EmbyMemoryCleaner.dll`
2. 放到 Emby 的 plugins 目录：

   | 部署方式 | plugins 目录 |
   |---|---|
   | Windows 桌面版 | `%AppData%\Emby-Server\plugins\` |
   | Docker (`emby/embyserver`) | 容器内 `/config/plugins/` |
   | Linux deb/rpm | `/var/lib/emby/plugins/` 或 `~/.config/emby-server/plugins/` |
   | Synology | `/var/packages/EmbyServer/var/plugins/` |

3. **重启 Emby Server**
4. 进入 仪表板 → 插件 → Memory Cleaner 配置

### 方式 B：通过 Emby 插件源（如果作者发布了 manifest）

仪表板 → 高级 → 插件 → 插件源 → 添加：

```
https://raw.githubusercontent.com/<owner>/<repo>/main/manifest.json
```

之后在插件商店直接搜索 `Memory Cleaner` 安装。

---

## ⚙️ 配置项

进入 仪表板 → 插件 → Memory Cleaner，可配置：

| 配置 | 默认值 | 范围 | 说明 |
|---|---|---|---|
| 启用周期内存清理 | ✅ 开 | - | 关闭后不会自动清理 |
| 清理间隔（分钟） | 30 | 1 – 120 | 建议 30-60 |

### 推荐配置

| 你的场景 | 建议间隔 |
|---|---|
| 小库（< 1 万条目）、内存稳定几百 MB | 60 分钟，或不装 |
| 大库（10 万+）+ 经常完整扫库 | 30 分钟 |
| 7×24 多人共享 + 内存吃紧 | 30 分钟，避免设太短 |

---

## 🔍 验证生效

启动后 **1 分钟** 首次执行，之后每 N 分钟执行一次。

### 方法 1：配置页"运行状态"面板

刷新后能直接看到上次清理释放了多少 MB。

### 方法 2：Emby 日志

搜索 `MemoryCleaner` 关键字：

```
[INFO] MemoryCleaner started - Cleanup every 30 minutes
[INFO] MemoryCleaner [SetProcessWorkingSetSize]: Managed 124 MB (freed 87 MB), RSS 412 MB (freed 234 MB)
[INFO] MemoryCleaner [malloc_trim]: Managed 98 MB (freed 45 MB), RSS 380 MB (freed 156 MB)
```

| 字段 | 含义 |
|---|---|
| `[XXX]` | 使用的工作集释放方法（`SetProcessWorkingSetSize` / `malloc_trim` / `none`） |
| `Managed N MB (freed M MB)` | .NET 托管堆当前用量 / 本次释放量 |
| `RSS N MB (freed M MB)` | 进程物理内存 / 本次还给 OS 的量 |

---

## ⚠️ 副作用与注意事项

### 1. STW 暂停（最主要）

完整 GC + LOH 压缩是**阻塞式**的，Emby 进程会卡住几十到几百毫秒（堆越大停顿越久）：

| 堆大小 | 估计停顿 |
|---|---|
| 500 MB | ~50 ms |
| 2 GB | ~200 ms |
| 5 GB+ | 500 ms ~ 1 s |

**影响**：
- ✅ 无人使用时清理 → 完全无感
- ⚠️ 单人播放时 → 可能微小卡顿（一两帧）
- ❌ 多人转码 / 大库扫描中 → 短暂缓冲

> 建议把间隔设为 30+ 分钟，避免影响体验。

### 2. CPU 短时尖峰

完整 GC 会跑满一个核心几百毫秒，CPU 监控图会出现毛刺。NAS 上一般无所谓。

### 3. 不会有的副作用

- ❌ 不会丢数据（GC 只回收无引用对象）
- ❌ 不会破坏播放（流由 OS 缓冲）
- ❌ 不会影响数据库（SQLite 不在托管堆）
- ❌ 不会内存泄漏（仅 1 个 Timer + 几个静态字段）

---

## 🛠️ 自行编译

### 准备引用 DLL

从 **目标 Emby 服务器** 复制 3 个 DLL 到 `refs/`：

| 部署方式 | DLL 位置 |
|---|---|
| Windows | `%AppData%\Emby-Server\system\` |
| Docker | `docker cp <container>:/system/MediaBrowser.Common.dll ./refs/`（其余 2 个同样操作） |
| Linux deb | `/opt/emby-server/system/` |

需要的文件：
- `MediaBrowser.Common.dll`
- `MediaBrowser.Controller.dll`
- `MediaBrowser.Model.dll`

> 💡 **重要**：用最低支持版本的 Emby DLL 编译。例如想兼容 Emby 4.8+，就拿 4.8.x 的 DLL build。

### 编译

需要 .NET 6 SDK：

```powershell
cd EmbyMemoryCleaner
dotnet build -c Release
```

输出：`bin/Release/net6.0/EmbyMemoryCleaner.dll`（**不会**复制 refs 中的 DLL，因为 csproj 设置了 `<Private>false</Private>`）。

### 项目结构

```
EmbyMemoryCleaner/
├── Plugin.cs                    # 插件注册 + GetPages()
├── ServerEntryPoint.cs          # IServerEntryPoint，启动钩子
├── PluginConfiguration.cs       # 持久化配置
├── MemoryCleaner.cs             # 核心清理逻辑（GC + WSS/malloc_trim）
├── MemoryCleanerService.cs      # IService - REST API /Plugins/MemoryCleaner/Stats
├── Configuration/
│   ├── configPage.html          # 配置页 UI（嵌入资源）
│   └── configPage.js            # AMD 控制器（嵌入资源）
├── Images/
│   └── thumb.png                # 插件缩略图（嵌入资源）
└── refs/                        # 编译时引用的 Emby DLL（不入库，自己放）
```

---

## 🌐 与其它插件兼容

Plugin GUID 与 [StrmAssistant](https://github.com/sjtuross/StrmAssistant) (`f6c40a3e-...`) 和 StrmAssistantPro 完全不同，**可以同时安装**互不冲突。

如果你只想要内存清理而不需要 StrmAssistant 的其它功能，本插件是更轻量的选择。

---

## 📜 来源声明

`MemoryCleaner.cs` 核心逻辑移植自 [sjtuross/StrmAssistant](https://github.com/sjtuross/StrmAssistant)（MIT 协议），保留原作思路，剥离了所有外部依赖以做成独立插件。

感谢原作者 @sjtuross 的优秀实现。

---

## 🐛 问题反馈

- 配置页面打不开 / 文字浮在外面 → 强制刷新 (Ctrl+F5) 清除浏览器缓存
- 日志里完全看不到 `MemoryCleaner` → 检查插件是否成功加载（仪表板 → 插件 列表里有没有）
- `MissingMethodException` → 你的 Emby 版本太老，请用对应版本的 DLL 重新编译

---

## 📄 License

MIT
