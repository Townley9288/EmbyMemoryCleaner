using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using MediaBrowser.Model.Logging;

namespace EmbyMemoryCleaner
{
    /// <summary>
    /// 周期性触发 GC + LOH 压缩 + 进程工作集释放，缓解 Emby 长期运行内存膨胀。
    /// 移植自 sjtuross/StrmAssistant 的 MemoryCleaner 模块。
    /// </summary>
    public sealed class MemoryCleaner : IDisposable
    {
        private static MemoryCleaner _instance;
        private static readonly object _lock = new object();

        private readonly ILogger _logger;
        private int _intervalMinutes;
        private Timer _timer;
        private int _running;
        private bool _disposed;

        private static readonly TimeSpan MinManualInterval = TimeSpan.FromSeconds(60);
        private long _lastTickTicks;
        private int _manualPending;
        private static int _mallocTrimSupported = -1;

        // 上一次清理统计（供 UI 读取）
        public static long LastManagedNowMb;
        public static long LastRssNowMb;
        public static long LastManagedFreedMb;
        public static long LastRssFreedMb;
        public static string LastMethod = "(未运行)";
        public static DateTime LastCleanupTimeUtc;

        public static MemoryCleaner Instance => _instance;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("libc", EntryPoint = "malloc_trim", SetLastError = true)]
        private static extern int malloc_trim_libc(UIntPtr pad);

        private MemoryCleaner(ILogger logger, int intervalMinutes)
        {
            _logger = logger;
            _intervalMinutes = intervalMinutes;
        }

        public static void ApplySettings(ILogger logger, bool enabled, int intervalMinutes)
        {
            if (intervalMinutes < 1) intervalMinutes = 1;
            if (intervalMinutes > 120) intervalMinutes = 120;

            lock (_lock)
            {
                if (!enabled)
                {
                    if (_instance != null)
                    {
                        _instance.Dispose();
                        _instance = null;
                    }
                }
                else if (_instance == null)
                {
                    _instance = new MemoryCleaner(logger, intervalMinutes);
                    _instance.Start();
                }
                else if (_instance._intervalMinutes != intervalMinutes)
                {
                    _instance.Reschedule(intervalMinutes);
                }
            }
        }

        public static void DisposeInstance()
        {
            lock (_lock)
            {
                _instance?.Dispose();
                _instance = null;
            }
        }

        public static void RequestCleanup(string reason = null)
        {
            var inst = _instance;
            if (inst == null || inst._disposed || inst._timer == null) return;

            long lastTicks = Interlocked.Read(ref inst._lastTickTicks);
            if (lastTicks != 0)
            {
                var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastTicks);
                if (elapsed < MinManualInterval) return;
            }

            if (Interlocked.CompareExchange(ref inst._manualPending, 1, 0) != 0) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(reason))
                        inst._logger.Debug("MemoryCleaner: post-batch cleanup requested (" + reason + ")", Array.Empty<object>());
                    inst.OnTick(null);
                }
                finally
                {
                    Interlocked.Exchange(ref inst._manualPending, 0);
                }
            });
        }

        private void Reschedule(int intervalMinutes)
        {
            _intervalMinutes = intervalMinutes;
            var period = TimeSpan.FromMinutes(intervalMinutes);
            _timer?.Change(period, period);
            _logger.Info($"MemoryCleaner rescheduled - Cleanup every {_intervalMinutes} minutes", Array.Empty<object>());
        }

        public void Start()
        {
            if (_timer != null)
            {
                _logger.Warn("MemoryCleaner already started", Array.Empty<object>());
                return;
            }
            var period = TimeSpan.FromMinutes(_intervalMinutes);
            _timer = new Timer(OnTick, null, TimeSpan.FromMinutes(1), period);
            _logger.Info($"MemoryCleaner started - Cleanup every {_intervalMinutes} minutes", Array.Empty<object>());
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
                _logger.Info("MemoryCleaner stopped", Array.Empty<object>());
            }
        }

        public void CleanupNow() => OnTick(null);

        private void OnTick(object state)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;

            try
            {
                long managedBefore = GC.GetTotalMemory(forceFullCollection: false);
                long rssBefore = 0L;
                try
                {
                    using var process = Process.GetCurrentProcess();
                    rssBefore = process.WorkingSet64;
                }
                catch { }

                try
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                }
                catch { }

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

                long rssAfter = rssBefore;
                string method = "none";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        if (SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)))
                            method = "SetProcessWorkingSetSize";
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("SetProcessWorkingSetSize failed: " + ex.Message, Array.Empty<object>());
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && TryMallocTrim())
                {
                    method = "malloc_trim";
                }

                long managedAfter = GC.GetTotalMemory(forceFullCollection: false);
                try
                {
                    using var process2 = Process.GetCurrentProcess();
                    rssAfter = process2.WorkingSet64;
                }
                catch { }

                long managedFreedMb = (managedBefore - managedAfter) / 1024 / 1024;
                long rssFreedMb = (rssBefore - rssAfter) / 1024 / 1024;
                long managedNowMb = managedAfter / 1024 / 1024;
                long rssNowMb = rssAfter / 1024 / 1024;

                LastManagedNowMb = managedNowMb;
                LastRssNowMb = rssNowMb;
                LastManagedFreedMb = managedFreedMb;
                LastRssFreedMb = rssFreedMb;
                LastMethod = method;
                LastCleanupTimeUtc = DateTime.UtcNow;

                _logger.Info(
                    $"MemoryCleaner [{method}]: Managed {managedNowMb} MB (freed {managedFreedMb} MB), RSS {rssNowMb} MB (freed {rssFreedMb} MB)",
                    Array.Empty<object>());
            }
            catch (Exception ex)
            {
                _logger.Warn("MemoryCleaner tick failed: " + ex.Message, Array.Empty<object>());
            }
            finally
            {
                Interlocked.Exchange(ref _lastTickTicks, DateTime.UtcNow.Ticks);
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private bool TryMallocTrim()
        {
            if (_mallocTrimSupported == 0) return false;

            try
            {
                malloc_trim_libc(UIntPtr.Zero);
                if (_mallocTrimSupported == -1)
                {
                    _mallocTrimSupported = 1;
                    _logger.Info("MemoryCleaner: glibc malloc_trim is available; will release freed memory back to OS each cycle.", Array.Empty<object>());
                }
                return true;
            }
            catch (DllNotFoundException ex)
            {
                _mallocTrimSupported = 0;
                _logger.Info("MemoryCleaner: libc not found (" + ex.Message + "); skipping malloc_trim. Likely musl libc (Alpine) - GC alone will be used.", Array.Empty<object>());
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                _mallocTrimSupported = 0;
                _logger.Info("MemoryCleaner: malloc_trim not exported by libc (" + ex.Message + "); likely musl libc - GC alone will be used.", Array.Empty<object>());
                return false;
            }
            catch (Exception ex)
            {
                _mallocTrimSupported = 0;
                _logger.Debug("MemoryCleaner: malloc_trim invocation failed: " + ex.Message, Array.Empty<object>());
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
