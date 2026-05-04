define([], function () {
    'use strict';

    return function (view, params) {
        var PluginId = "c1f20f3a-7d2c-4d5e-9b21-2a8f0e6e9c11";

        function loadConfig() {
            var chk = view.querySelector('#chkEnableMemoryCleanup');
            var txt = view.querySelector('#txtIntervalMinutes');
            var chkSkip = view.querySelector('#chkSkipWhenPlaying');
            if (!chk || !txt) { return; }
            ApiClient.getPluginConfiguration(PluginId).then(function (config) {
                chk.checked = !!config.EnableMemoryCleanup;
                txt.value = config.MemoryCleanupIntervalMinutes || 30;
                if (chkSkip) { chkSkip.checked = config.SkipWhenPlaying !== false; }
            });
        }

        function saveConfig(e) {
            ApiClient.getPluginConfiguration(PluginId).then(function (config) {
                config.EnableMemoryCleanup = view.querySelector('#chkEnableMemoryCleanup').checked;
                var v = parseInt(view.querySelector('#txtIntervalMinutes').value, 10);
                if (isNaN(v) || v < 1) v = 1;
                if (v > 120) v = 120;
                config.MemoryCleanupIntervalMinutes = v;
                var chkSkip = view.querySelector('#chkSkipWhenPlaying');
                config.SkipWhenPlaying = chkSkip ? !!chkSkip.checked : true;
                ApiClient.updatePluginConfiguration(PluginId, config).then(function (r) {
                    Dashboard.processPluginConfigurationUpdateResult(r);
                });
            });
            if (e) { e.preventDefault(); }
            return false;
        }

        function setText(sel, val) {
            var el = view.querySelector(sel);
            if (el) { el.textContent = val; }
        }

        function formatTime(iso) {
            if (!iso) { return '尚未运行'; }
            try {
                var d = new Date(iso);
                if (isNaN(d.getTime())) { return iso; }
                return d.toLocaleString();
            } catch (e) { return iso; }
        }

        function loadStats() {
            var url = ApiClient.getUrl('Plugins/MemoryCleaner/Stats');
            ApiClient.getJSON(url).then(function (s) {
                setText('.mc-current-managed', s.CurrentManagedMb);
                setText('.mc-current-rss', s.CurrentRssMb);
                setText('.mc-last-time', s.HasRun ? formatTime(s.LastCleanupTimeUtc) : '尚未运行');
                setText('.mc-last-managed-freed', s.HasRun ? s.LastFreedManagedMb : '--');
                setText('.mc-last-rss-freed', s.HasRun ? s.LastFreedRssMb : '--');
                setText('.mc-last-method', s.LastMethod || '--');
            }, function () {
                setText('.mc-last-method', '获取失败');
            });
        }

        view.addEventListener('viewshow', function () {
            loadConfig();
            loadStats();
        });

        var form = view.querySelector('.memoryCleanerConfigForm');
        if (form) {
            form.addEventListener('submit', saveConfig);
        }

        var btnRefresh = view.querySelector('.mc-btn-refresh');
        if (btnRefresh) {
            btnRefresh.addEventListener('click', loadStats);
        }

        var btnCleanupNow = view.querySelector('.mc-btn-cleanup-now');
        var msgEl = view.querySelector('.mc-cleanup-msg');
        if (btnCleanupNow) {
            btnCleanupNow.addEventListener('click', function () {
                if (msgEl) { msgEl.textContent = '正在清理...'; }
                btnCleanupNow.disabled = true;
                var url = ApiClient.getUrl('Plugins/MemoryCleaner/CleanupNow');
                ApiClient.ajax({ type: 'POST', url: url, dataType: 'json' }).then(function (r) {
                    if (msgEl) {
                        msgEl.textContent = (r && r.Message) ? r.Message : '完成';
                        msgEl.style.color = (r && r.Success) ? '' : 'orange';
                    }
                    loadStats();
                }, function () {
                    if (msgEl) {
                        msgEl.textContent = '请求失败';
                        msgEl.style.color = 'orange';
                    }
                }).then(function () {
                    btnCleanupNow.disabled = false;
                }, function () {
                    btnCleanupNow.disabled = false;
                });
            });
        }
    };
});
