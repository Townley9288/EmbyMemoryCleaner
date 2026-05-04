using MediaBrowser.Model.Plugins;

namespace EmbyMemoryCleaner
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>是否启用周期性内存清理。</summary>
        public bool EnableMemoryCleanup { get; set; } = true;

        /// <summary>清理间隔（分钟）。范围 1-120，默认 30。</summary>
        public int MemoryCleanupIntervalMinutes { get; set; } = 30;

        /// <summary>有用户正在播放时跳过本次清理（避免 GC STW 引起卡顿）。默认 true。</summary>
        public bool SkipWhenPlaying { get; set; } = true;
    }
}
