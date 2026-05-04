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

        /// <summary>「播放时跳过」仅在转码会话时生效（直出/直传不跳过）。默认 false（任何播放都跳过）。</summary>
        public bool SkipOnlyWhenTranscoding { get; set; } = false;

        /// <summary>仅当进程 RSS 超过此阈值（MB）时才执行清理；&lt;=0 表示不启用阈值。默认 0。</summary>
        public int RssThresholdMb { get; set; } = 0;
    }
}
