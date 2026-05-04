using System;
using System.Diagnostics;
using MediaBrowser.Model.Services;

namespace EmbyMemoryCleaner
{
    [Route("/Plugins/MemoryCleaner/Stats", "GET", Summary = "Get current and last-cleanup memory stats")]
    public class GetMemoryCleanerStats : IReturn<MemoryCleanerStatsResult>
    {
    }

    [Route("/Plugins/MemoryCleaner/CleanupNow", "POST", Summary = "Trigger an immediate cleanup")]
    public class PostCleanupNow : IReturn<CleanupNowResult>
    {
    }

    public class MemoryCleanerStatsResult
    {
        public long CurrentManagedMb { get; set; }
        public long CurrentRssMb { get; set; }
        public long LastFreedManagedMb { get; set; }
        public long LastFreedRssMb { get; set; }
        public string LastMethod { get; set; }
        public string LastCleanupTimeUtc { get; set; }
        public bool HasRun { get; set; }
    }

    public class CleanupNowResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public long ManagedFreedMb { get; set; }
        public long RssFreedMb { get; set; }
        public long ManagedNowMb { get; set; }
        public long RssNowMb { get; set; }
        public string Method { get; set; }
    }

    public class MemoryCleanerService : IService
    {
        public object Get(GetMemoryCleanerStats request)
        {
            long currentManagedMb = GC.GetTotalMemory(false) / 1024 / 1024;
            long currentRssMb = 0L;
            try
            {
                using var p = Process.GetCurrentProcess();
                currentRssMb = p.WorkingSet64 / 1024 / 1024;
            }
            catch
            {
            }

            return new MemoryCleanerStatsResult
            {
                CurrentManagedMb = currentManagedMb,
                CurrentRssMb = currentRssMb,
                LastFreedManagedMb = MemoryCleaner.LastManagedFreedMb,
                LastFreedRssMb = MemoryCleaner.LastRssFreedMb,
                LastMethod = MemoryCleaner.LastMethod,
                LastCleanupTimeUtc = MemoryCleaner.LastCleanupTimeUtc == default
                    ? null
                    : MemoryCleaner.LastCleanupTimeUtc.ToString("o"),
                HasRun = MemoryCleaner.LastCleanupTimeUtc != default
            };
        }

        public object Post(PostCleanupNow request)
        {
            var inst = MemoryCleaner.Instance;
            if (inst == null)
            {
                return new CleanupNowResult
                {
                    Success = false,
                    Message = "MemoryCleaner 未启用，请先在配置页勾选「启用周期内存清理」。"
                };
            }

            var r = inst.CleanupNowForce();
            if (r.Skipped)
            {
                return new CleanupNowResult
                {
                    Success = false,
                    Message = r.SkipReason ?? "已跳过"
                };
            }

            return new CleanupNowResult
            {
                Success = true,
                Message = $"已释放：托管 {r.ManagedFreedMb} MB / RSS {r.RssFreedMb} MB",
                ManagedFreedMb = r.ManagedFreedMb,
                RssFreedMb = r.RssFreedMb,
                ManagedNowMb = r.ManagedNowMb,
                RssNowMb = r.RssNowMb,
                Method = r.Method
            };
        }
    }
}
