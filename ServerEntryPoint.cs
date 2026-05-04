using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;

namespace EmbyMemoryCleaner
{
    /// <summary>
    /// Emby 服务器启动入口。负责按当前配置初始化 MemoryCleaner，
    /// 并在配置变更时重新应用（通过 Plugin.UpdateConfiguration 间接触发）。
    /// </summary>
    public class ServerEntryPoint : IServerEntryPoint
    {
        private readonly ILogger _logger;

        public ServerEntryPoint(ILogManager logManager)
        {
            _logger = logManager.GetLogger("MemoryCleaner");
        }

        public void Run()
        {
            try
            {
                var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
                MemoryCleaner.ApplySettings(_logger, cfg.EnableMemoryCleanup, cfg.MemoryCleanupIntervalMinutes);

                Plugin.ConfigurationUpdated += OnConfigurationChanged;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("MemoryCleaner initialization failed", ex);
            }
        }

        private void OnConfigurationChanged(object sender, PluginConfiguration cfg)
        {
            try
            {
                MemoryCleaner.ApplySettings(_logger, cfg.EnableMemoryCleanup, cfg.MemoryCleanupIntervalMinutes);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("MemoryCleaner ApplySettings failed", ex);
            }
        }

        public void Dispose()
        {
            try
            {
                Plugin.ConfigurationUpdated -= OnConfigurationChanged;
                MemoryCleaner.DisposeInstance();
            }
            catch
            {
                // ignore
            }
        }
    }
}
