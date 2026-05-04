using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace EmbyMemoryCleaner
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasThumbImage, IHasWebPages
    {
        public static Plugin Instance { get; private set; }

        /// <summary>插件配置变更事件（在 Emby 后台保存配置后触发）。</summary>
        public static event EventHandler<PluginConfiguration> ConfigurationUpdated;

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => "Memory Cleaner";

        public override string Description =>
            "Periodically forces full GC + LOH compaction + working set release to mitigate Emby long-running memory bloat. " +
            "Standalone port of the MemoryCleaner module from sjtuross/StrmAssistant. " +
            "Compatible with StrmAssistant / StrmAssistantPro (different plugin GUID, will not conflict).";

        // 与 StrmAssistant / StrmAssistantPro 完全不同的 GUID，避免 Emby 把它们认作同一插件
        public override Guid Id => new Guid("c1f20f3a-7d2c-4d5e-9b21-2a8f0e6e9c11");

        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            base.UpdateConfiguration(configuration);
            if (configuration is PluginConfiguration cfg)
            {
                ConfigurationUpdated?.Invoke(this, cfg);
            }
        }

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Images.thumb.png");
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "MemoryCleaner",
                    DisplayName = "Memory Cleaner",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                },
                new PluginPageInfo
                {
                    Name = "memorycleanerjs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.js"
                }
            };
        }
    }
}
