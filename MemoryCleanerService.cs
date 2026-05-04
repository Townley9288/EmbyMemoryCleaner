using System;
using System.Diagnostics;
using MediaBrowser.Model.Services;

namespace EmbyMemoryCleaner
{
    [Route("/Plugins/MemoryCleaner/Stats", "GET", Summary = "Get current and last-cleanup memory stats")]
    public class GetMemoryCleanerStats : IReturn<MemoryCleanerStatsResult>
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
    }
}
