using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace EmbyMemoryCleaner
{
    /// <summary>
    /// 计划任务：检查 GitHub Releases 是否有新版本，自动下载并替换插件 DLL。
    /// 默认每天 03:00 触发，也可以在 Emby 后台手动运行。
    /// 替换后需要重启 Emby Server 才能生效（Emby 自身机制：DLL 在运行时被锁定）。
    /// </summary>
    public class UpdatePluginTask : IScheduledTask, IConfigurableScheduledTask
    {
        // 直接读 manifest.json，避免 GitHub API 的未认证速率限制（60/h）
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/Townley9288/EmbyMemoryCleaner/main/manifest.json";

        private readonly ILogger _logger;

        public UpdatePluginTask(ILogManager logManager)
        {
            _logger = logManager.GetLogger("MemoryCleaner.UpdatePluginTask");
        }

        public string Name => "Update Memory Cleaner";
        public string Key => "MemoryCleanerUpdatePlugin";
        public string Description => "检查 GitHub 上的最新版本，自动下载并替换插件 DLL（重启 Emby 后生效）";
        public string Category => "Memory Cleaner";

        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // 每天凌晨 3 点
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress?.Report(0);

            try
            {
                var current = Plugin.Instance?.Version;
                if (current == null)
                {
                    _logger.Warn("Plugin.Instance is null, skip update check.");
                    return;
                }

                _logger.Info("Checking for update. Current version: " + current);
                progress?.Report(10);

                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    http.DefaultRequestHeaders.UserAgent.Add(
                        new ProductInfoHeaderValue("EmbyMemoryCleaner", current.ToString()));

                    string json;
                    using (var resp = await http.GetAsync(ManifestUrl, cancellationToken).ConfigureAwait(false))
                    {
                        resp.EnsureSuccessStatusCode();
                        json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    progress?.Report(40);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                    {
                        _logger.Warn("manifest.json root is not a non-empty array.");
                        return;
                    }
                    var pluginEntry = root[0];
                    if (!pluginEntry.TryGetProperty("versions", out var versions) ||
                        versions.ValueKind != JsonValueKind.Array || versions.GetArrayLength() == 0)
                    {
                        _logger.Warn("manifest.json has no versions array.");
                        return;
                    }
                    // versions[0] 约定是最新版
                    var top = versions[0];
                    var versionStr = top.TryGetProperty("version", out var vEl) ? vEl.GetString() : null;
                    var dllUrl = top.TryGetProperty("sourceUrl", out var sEl) ? sEl.GetString() : null;
                    if (string.IsNullOrEmpty(versionStr) || string.IsNullOrEmpty(dllUrl))
                    {
                        _logger.Warn("manifest.json top version missing version or sourceUrl.");
                        return;
                    }

                    var latest = ParseVersion(versionStr);
                    if (latest == null)
                    {
                        _logger.Warn("Cannot parse latest version: " + versionStr);
                        return;
                    }

                    if (latest <= current)
                    {
                        _logger.Info($"Already up-to-date (latest {latest} <= current {current}).");
                        progress?.Report(100);
                        return;
                    }

                    _logger.Info($"New version detected: {latest} (current {current}). Downloading from {dllUrl}");

                    progress?.Report(60);

                    var targetPath = Plugin.Instance.AssemblyFilePath;
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        _logger.Warn("Plugin AssemblyFilePath is empty.");
                        return;
                    }

                    var tempPath = targetPath + ".new";

                    using (var dlResp = await http.GetAsync(dllUrl, cancellationToken).ConfigureAwait(false))
                    {
                        dlResp.EnsureSuccessStatusCode();
                        using var fs = File.Create(tempPath);
                        await dlResp.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
                    }

                    progress?.Report(90);

                    // 直接覆盖 DLL。Linux: rename 可替换被占用文件；Windows 退路：先改名再写。
                    try
                    {
                        File.Move(tempPath, targetPath, overwrite: true);
                    }
                    catch (IOException)
                    {
                        var backupPath = targetPath + ".old";
                        try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                        File.Move(targetPath, backupPath);
                        File.Move(tempPath, targetPath);
                    }

                    _logger.Info($"Plugin updated to {latest}. RESTART Emby Server to load the new version.");
                    progress?.Report(100);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("UpdatePluginTask failed", ex);
                throw;
            }
        }

        private static Version ParseVersion(string tag)
        {
            // tag like "v1.0.2.0" or "1.0.2.0"
            var m = Regex.Match(tag, @"(\d+(?:\.\d+){1,3})");
            if (!m.Success) return null;
            return Version.TryParse(m.Groups[1].Value, out var v) ? v : null;
        }
    }
}
