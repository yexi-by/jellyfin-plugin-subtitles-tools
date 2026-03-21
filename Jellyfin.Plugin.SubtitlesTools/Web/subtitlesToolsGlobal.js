(function () {
    'use strict';

    if (window.__subtitlesToolsGlobalLoaded) {
        return;
    }

    window.__subtitlesToolsGlobalLoaded = true;

    const CONFIG = {
        rootId: 'subtitlesToolsFloatingRoot',
        overlayId: 'subtitlesToolsOverlay',
        styleId: 'subtitlesToolsGlobalStyle',
        routePollMs: 1200,
        apiRoot: 'Jellyfin.Plugin.SubtitlesTools'
    };

    const state = {
        itemId: null,
        itemData: null,
        activePartId: null,
        searchResults: new Map(),
        lastBatchItems: [],
        busy: false,
        lastLocation: ''
    };

    const REFRESH_RETRY_DELAY_MS = 1200;
    const REFRESH_RETRY_ATTEMPTS = 6;

    function injectStyles() {
        if (document.getElementById(CONFIG.styleId)) {
            return;
        }

        const style = document.createElement('style');
        style.id = CONFIG.styleId;
        style.textContent = `
            #${CONFIG.rootId} {
                position: fixed;
                right: 24px;
                bottom: 24px;
                z-index: 99999;
                display: none;
            }

            #${CONFIG.rootId}.is-visible {
                display: block;
            }

            .subtitles-tools-fab {
                border: 0;
                border-radius: 999px;
                padding: 14px 18px;
                font-size: 14px;
                font-weight: 700;
                color: #fff;
                background: linear-gradient(135deg, #e05263 0%, #a32339 100%);
                box-shadow: 0 16px 32px rgba(0, 0, 0, 0.35);
                cursor: pointer;
            }

            .subtitles-tools-overlay {
                position: fixed;
                inset: 0;
                z-index: 100000;
                display: none;
                background: rgba(12, 14, 18, 0.78);
                backdrop-filter: blur(8px);
            }

            .subtitles-tools-overlay.is-open {
                display: flex;
                align-items: center;
                justify-content: center;
                padding: 24px;
            }

            .subtitles-tools-panel {
                width: min(1180px, 100%);
                max-height: calc(100vh - 48px);
                overflow: hidden;
                display: flex;
                flex-direction: column;
                border-radius: 24px;
                background: #101419;
                color: #f5f7fa;
                box-shadow: 0 24px 60px rgba(0, 0, 0, 0.45);
            }

            .subtitles-tools-header {
                display: flex;
                align-items: flex-start;
                justify-content: space-between;
                gap: 16px;
                padding: 24px 28px 20px;
                border-bottom: 1px solid rgba(255, 255, 255, 0.08);
            }

            .subtitles-tools-title {
                margin: 0;
                font-size: 24px;
                line-height: 1.2;
            }

            .subtitles-tools-subtitle {
                margin-top: 6px;
                color: rgba(245, 247, 250, 0.72);
                font-size: 14px;
            }

            .subtitles-tools-close {
                border: 0;
                border-radius: 999px;
                background: rgba(255, 255, 255, 0.08);
                color: #fff;
                width: 40px;
                height: 40px;
                font-size: 24px;
                line-height: 1;
                cursor: pointer;
            }

            .subtitles-tools-toolbar {
                display: flex;
                flex-wrap: wrap;
                gap: 12px;
                padding: 18px 28px 0;
            }

            .subtitles-tools-button {
                border: 0;
                border-radius: 14px;
                padding: 10px 14px;
                font-size: 14px;
                font-weight: 600;
                color: #fff;
                background: #24496b;
                cursor: pointer;
            }

            .subtitles-tools-button.is-secondary {
                background: rgba(255, 255, 255, 0.12);
            }

            .subtitles-tools-button.is-danger {
                background: #7a2e38;
            }

            .subtitles-tools-button:disabled {
                opacity: 0.55;
                cursor: not-allowed;
            }

            .subtitles-tools-body {
                display: grid;
                grid-template-columns: minmax(240px, 300px) minmax(0, 1fr);
                min-height: 0;
                flex: 1 1 auto;
                overflow: hidden;
            }

            .subtitles-tools-parts {
                padding: 20px;
                border-right: 1px solid rgba(255, 255, 255, 0.08);
                overflow-y: auto;
                background: rgba(255, 255, 255, 0.02);
            }

            .subtitles-tools-part-button {
                width: 100%;
                text-align: left;
                margin-bottom: 10px;
                border: 0;
                border-radius: 16px;
                padding: 14px 16px;
                background: rgba(255, 255, 255, 0.06);
                color: #fff;
                cursor: pointer;
            }

            .subtitles-tools-part-button.is-active {
                background: linear-gradient(135deg, #163957 0%, #2f7196 100%);
            }

            .subtitles-tools-part-meta {
                margin-top: 6px;
                color: rgba(255, 255, 255, 0.68);
                font-size: 12px;
                display: flex;
                flex-wrap: wrap;
                gap: 8px;
            }

            .subtitles-tools-main {
                padding: 20px 24px 24px;
                overflow-y: auto;
            }

            .subtitles-tools-section {
                margin-bottom: 22px;
            }

            .subtitles-tools-section h3 {
                margin: 0 0 10px;
                font-size: 18px;
            }

            .subtitles-tools-status {
                min-height: 22px;
                margin-top: 10px;
                color: #d6e2ee;
                font-size: 13px;
            }

            .subtitles-tools-status.is-error {
                color: #ff8c8c;
            }

            .subtitles-tools-status.is-success {
                color: #95f3b0;
            }

            .subtitles-tools-note {
                font-size: 13px;
                color: rgba(245, 247, 250, 0.72);
                line-height: 1.6;
            }

            .subtitles-tools-card,
            .subtitles-tools-summary-item {
                border-radius: 18px;
                padding: 16px;
                background: rgba(255, 255, 255, 0.05);
            }

            .subtitles-tools-card-list,
            .subtitles-tools-summary-list {
                display: grid;
                gap: 12px;
            }

            .subtitles-tools-card-title {
                font-size: 15px;
                font-weight: 700;
            }

            .subtitles-tools-card-meta {
                display: flex;
                flex-wrap: wrap;
                gap: 8px 14px;
                font-size: 12px;
                color: rgba(255, 255, 255, 0.72);
                margin-top: 8px;
            }

            .subtitles-tools-actions {
                display: flex;
                flex-wrap: wrap;
                gap: 8px;
                margin-top: 12px;
            }

            .subtitles-tools-pill {
                display: inline-flex;
                align-items: center;
                border-radius: 999px;
                padding: 4px 10px;
                font-size: 12px;
                font-weight: 600;
                background: rgba(255, 255, 255, 0.08);
                color: rgba(255, 255, 255, 0.82);
            }

            @media (max-width: 900px) {
                .subtitles-tools-overlay.is-open {
                    padding: 0;
                }

                .subtitles-tools-panel {
                    height: 100vh;
                    max-height: 100vh;
                    border-radius: 0;
                }

                .subtitles-tools-body {
                    grid-template-columns: 1fr;
                }

                .subtitles-tools-parts {
                    border-right: 0;
                    border-bottom: 1px solid rgba(255, 255, 255, 0.08);
                }

                #${CONFIG.rootId} {
                    right: 16px;
                    bottom: 16px;
                }
            }
        `;

        document.head.appendChild(style);
    }

    function ensureRoot() {
        let root = document.getElementById(CONFIG.rootId);
        if (root) {
            return root;
        }

        root = document.createElement('div');
        root.id = CONFIG.rootId;
        root.innerHTML = '<button class="subtitles-tools-fab" type="button">内封字幕</button>';
        root.querySelector('button').addEventListener('click', openOverlay);
        document.body.appendChild(root);
        return root;
    }

    function ensureOverlay() {
        let overlay = document.getElementById(CONFIG.overlayId);
        if (overlay) {
            return overlay;
        }

        overlay = document.createElement('div');
        overlay.id = CONFIG.overlayId;
        overlay.className = 'subtitles-tools-overlay';
        overlay.addEventListener('click', function (event) {
            if (event.target === overlay) {
                closeOverlay();
            }
        });
        document.body.appendChild(overlay);
        return overlay;
    }

    function showButton() {
        ensureRoot().classList.add('is-visible');
    }

    function hideButton() {
        const root = document.getElementById(CONFIG.rootId);
        if (root) {
            root.classList.remove('is-visible');
        }
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function sleep(ms) {
        return new Promise(resolve => window.setTimeout(resolve, ms));
    }

    function extractErrorMessage(error) {
        if (!error) {
            return '请求失败。';
        }

        if (typeof error === 'string' && error.trim()) {
            return error.trim();
        }

        if (error.message && String(error.message).trim()) {
            return String(error.message).trim();
        }

        if (error.payload) {
            const payload = error.payload;
            if (payload.Message && String(payload.Message).trim()) {
                return String(payload.Message).trim();
            }

            if (payload.message && String(payload.message).trim()) {
                return String(payload.message).trim();
            }
        }

        if (Number.isInteger(error.status)) {
            return `请求失败：${error.status}`;
        }

        return '请求失败。';
    }

    function getFileNameFromPath(path) {
        if (!path) {
            return '';
        }

        const segments = String(path).split(/[\\/]/);
        return segments[segments.length - 1] || '';
    }

    function extractGuid(source) {
        if (!source) {
            return null;
        }

        const patterns = [
            /(?:^|[?&#])id=([0-9a-fA-F-]{32,36})/i,
            /\/details\/([0-9a-fA-F-]{32,36})/i,
            /\/items\/([0-9a-fA-F-]{32,36})/i
        ];

        for (const pattern of patterns) {
            const match = source.match(pattern);
            if (match && match[1]) {
                return match[1];
            }
        }

        return null;
    }

    function getCurrentItemId() {
        return extractGuid(window.location.hash)
            || extractGuid(window.location.search)
            || extractGuid(window.location.pathname)
            || extractGuid(window.location.href);
    }

    async function apiRequest(path, method, body) {
        if (!window.ApiClient || typeof window.ApiClient.ajax !== 'function' || typeof window.ApiClient.getUrl !== 'function') {
            throw new Error('Jellyfin ApiClient 尚未准备好。');
        }

        const response = await window.ApiClient.ajax({
            type: method,
            url: window.ApiClient.getUrl(path),
            data: body ? JSON.stringify(body) : undefined,
            contentType: body ? 'application/json' : undefined
        });

        let payload = null;
        try {
            payload = await response.json();
        } catch (error) {
            payload = null;
        }

        if (!response.ok) {
            const message = payload && (payload.Message || payload.message)
                ? payload.Message || payload.message
                : `请求失败：${response.status}`;
            const requestError = new Error(message);
            requestError.payload = payload;
            requestError.status = response.status;
            throw requestError;
        }

        return payload;
    }

    async function fetchPartsPayload(itemId) {
        return apiRequest(`${CONFIG.apiRoot}/Items/${itemId}/parts`, 'GET');
    }

    function applyPartsPayload(itemId, payload) {
        const isSameItem = state.itemId === itemId;
        const previousActivePartId = state.activePartId;
        state.itemId = itemId;
        state.itemData = payload;
        state.activePartId = isSameItem && previousActivePartId && payload.Parts.some(part => part.Id === previousActivePartId)
            ? previousActivePartId
            : payload.CurrentPartId || (payload.Parts[0] && payload.Parts[0].Id) || null;

        if (!isSameItem) {
            state.searchResults = new Map();
            state.lastBatchItems = [];
        }
    }

    async function fetchParts(itemId, forceReload) {
        if (!forceReload && state.itemData && state.itemId === itemId) {
            return state.itemData;
        }

        const payload = await fetchPartsPayload(itemId);
        applyPartsPayload(itemId, payload);
        return payload;
    }

    async function refreshCurrentPageState(forceReload) {
        const locationSignature = `${window.location.pathname}|${window.location.search}|${window.location.hash}`;
        if (!forceReload && state.lastLocation === locationSignature) {
            return;
        }

        state.lastLocation = locationSignature;
        const itemId = getCurrentItemId();
        if (!itemId) {
            state.itemId = null;
            state.itemData = null;
            state.activePartId = null;
            state.searchResults = new Map();
            state.lastBatchItems = [];
            hideButton();
            return;
        }

        try {
            await fetchParts(itemId, forceReload);
            showButton();
        } catch (error) {
            state.itemId = null;
            state.itemData = null;
            state.activePartId = null;
            state.searchResults = new Map();
            state.lastBatchItems = [];
            hideButton();
        }
    }

    function getActivePart() {
        if (!state.itemData || !state.activePartId) {
            return null;
        }

        return state.itemData.Parts.find(part => part.Id === state.activePartId) || null;
    }

    function setStatus(message, status) {
        const statusElement = document.querySelector('.subtitles-tools-status');
        if (!statusElement) {
            return;
        }

        statusElement.className = 'subtitles-tools-status';
        if (status === 'error') {
            statusElement.classList.add('is-error');
        }

        if (status === 'success') {
            statusElement.classList.add('is-success');
        }

        statusElement.textContent = message || '';
    }

    function updateManagedPart(partId, updater) {
        if (!state.itemData || !Array.isArray(state.itemData.Parts)) {
            return;
        }

        const targetPart = state.itemData.Parts.find(part => part.Id === partId);
        if (!targetPart) {
            return;
        }

        updater(targetPart);
    }

    function applyOperationResultToPart(partId, result) {
        if (!result) {
            return;
        }

        updateManagedPart(partId, part => {
            if (result.MediaPath) {
                part.MediaPath = result.MediaPath;
                part.FileName = getFileNameFromPath(result.MediaPath);
            }

            if (result.Container) {
                part.Container = result.Container;
            }

            if (result.IsManaged === true) {
                part.IsManaged = true;
                if (part.Container === 'mkv') {
                    part.ReadIdentityFromMetadata = true;
                }
            }

            if (result.EmbeddedSubtitle) {
                const nonPluginTracks = Array.isArray(part.EmbeddedSubtitles)
                    ? part.EmbeddedSubtitles.filter(track => !track.IsPluginManaged)
                    : [];
                part.EmbeddedSubtitles = [...nonPluginTracks, result.EmbeddedSubtitle];
            }
        });
    }

    function applyDeleteResultToPart(partId, deletedStreamIndex) {
        updateManagedPart(partId, part => {
            if (!Array.isArray(part.EmbeddedSubtitles)) {
                part.EmbeddedSubtitles = [];
                return;
            }

            part.EmbeddedSubtitles = part.EmbeddedSubtitles.filter(track => track.StreamIndex !== deletedStreamIndex);
        });
    }

    function applyBatchResults(items) {
        if (!Array.isArray(items)) {
            return;
        }

        items.forEach(item => {
            const shouldApply = item.IsManaged === true
                || item.Status === 'converted'
                || item.Status === 'embedded';
            if (shouldApply) {
                applyOperationResultToPart(item.PartId, item);
            }
        });
    }

    function createBatchRefreshValidator(items) {
        return payload => {
            if (!payload || !Array.isArray(payload.Parts)) {
                return false;
            }

            const successfulItems = items.filter(item => item.IsManaged === true || item.Status === 'converted' || item.Status === 'embedded');
            if (successfulItems.length === 0) {
                return true;
            }

            return successfulItems.every(item => {
                const part = payload.Parts.find(payloadPart => payloadPart.Id === item.PartId);
                if (!part) {
                    return false;
                }

                if (item.IsManaged === true && part.IsManaged !== true) {
                    return false;
                }

                if (item.MediaPath && part.MediaPath !== item.MediaPath) {
                    return false;
                }

                if (item.Container && part.Container !== item.Container) {
                    return false;
                }

                if (item.Status === 'embedded' && item.EmbeddedSubtitle) {
                    return Array.isArray(part.EmbeddedSubtitles)
                        && part.EmbeddedSubtitles.some(track =>
                            track.Title === item.EmbeddedSubtitle.Title
                            && track.Language === item.EmbeddedSubtitle.Language);
                }

                return true;
            });
        };
    }

    function createSinglePartRefreshValidator(partId, expectedResult) {
        return payload => {
            if (!payload || !Array.isArray(payload.Parts)) {
                return false;
            }

            const part = payload.Parts.find(payloadPart => payloadPart.Id === partId);
            if (!part) {
                return false;
            }

            if (expectedResult.IsManaged === true && part.IsManaged !== true) {
                return false;
            }

            if (expectedResult.MediaPath && part.MediaPath !== expectedResult.MediaPath) {
                return false;
            }

            if (expectedResult.Container && part.Container !== expectedResult.Container) {
                return false;
            }

            if (expectedResult.EmbeddedSubtitle) {
                return Array.isArray(part.EmbeddedSubtitles)
                    && part.EmbeddedSubtitles.some(track =>
                        track.Title === expectedResult.EmbeddedSubtitle.Title
                        && track.Language === expectedResult.EmbeddedSubtitle.Language);
            }

            return true;
        };
    }

    async function refreshOverlayDataWithRetry(validatePayload) {
        const currentItemId = state.itemId || getCurrentItemId();
        if (!currentItemId) {
            throw new Error('当前页面没有可管理的媒体项。');
        }

        let lastError = null;
        for (let attempt = 0; attempt < REFRESH_RETRY_ATTEMPTS; attempt += 1) {
            try {
                const payload = await fetchPartsPayload(currentItemId);
                if (!validatePayload || validatePayload(payload)) {
                    applyPartsPayload(currentItemId, payload);
                    renderOverlay();
                    return;
                }
            } catch (error) {
                lastError = error;
            }

            if (attempt < REFRESH_RETRY_ATTEMPTS - 1) {
                await sleep(REFRESH_RETRY_DELAY_MS);
            }
        }

        if (lastError) {
            throw lastError;
        }
    }

    function renderEmbeddedSubtitles(part) {
        if (!part || !Array.isArray(part.EmbeddedSubtitles) || part.EmbeddedSubtitles.length === 0) {
            return '<div class="subtitles-tools-summary-item">当前还没有内封字幕流。</div>';
        }

        return part.EmbeddedSubtitles.map(item => `
            <div class="subtitles-tools-card">
                <div class="subtitles-tools-card-title">${escapeHtml(item.Title || `字幕流 #${item.StreamIndex}`)}</div>
                <div class="subtitles-tools-card-meta">
                    <span>绝对流索引：${escapeHtml(item.StreamIndex)}</span>
                    <span>字幕流序号：${escapeHtml(item.SubtitleStreamIndex)}</span>
                    <span>语言：${escapeHtml(item.Language)}</span>
                    <span>格式：${escapeHtml(item.Format)}</span>
                    <span>${item.IsPluginManaged ? '插件写入' : '非插件写入'}</span>
                </div>
                <div class="subtitles-tools-actions">
                    ${item.IsPluginManaged ? `
                        <button
                            class="subtitles-tools-button is-danger"
                            type="button"
                            data-action="delete-embedded"
                            data-stream-index="${escapeHtml(item.StreamIndex)}">
                            删除这条内封字幕
                        </button>
                    ` : ''}
                </div>
            </div>
        `).join('');
    }

    function renderResults(partId) {
        const results = state.searchResults.get(partId) || [];
        if (results.length === 0) {
            return '<div class="subtitles-tools-summary-item">当前还没有搜索结果。你可以先点“搜索当前分段”。</div>';
        }

        return results.map(item => `
            <div class="subtitles-tools-card">
                <div class="subtitles-tools-card-title">${escapeHtml(item.DisplayName)}</div>
                <div class="subtitles-tools-card-meta">
                    <span>语言：${escapeHtml(item.Language)}</span>
                    <span>格式：${escapeHtml(item.Format)}</span>
                    <span>分数：${escapeHtml(item.Score)}</span>
                    <span>临时文件：${escapeHtml(item.TemporarySrtFileName)}</span>
                </div>
                <div class="subtitles-tools-actions">
                    <button
                        class="subtitles-tools-button"
                        type="button"
                        data-action="download-candidate"
                        data-subtitle-id="${escapeHtml(item.Id)}">
                        下载并内封到当前分段
                    </button>
                </div>
            </div>
        `).join('');
    }

    function renderSummary(items) {
        if (!Array.isArray(items) || items.length === 0) {
            return '';
        }

        return `
            <div class="subtitles-tools-section">
                <h3>最近一次批量任务结果</h3>
                <div class="subtitles-tools-summary-list">
                    ${items.map(item => `
                        <div class="subtitles-tools-summary-item">
                            <strong>${escapeHtml(item.Label)}</strong> · ${escapeHtml(item.Status)}<br />
                            ${escapeHtml(item.Message || '')}
                        </div>
                    `).join('')}
                </div>
            </div>
        `;
    }

    function getManagedStatusText(part) {
        if (!part || part.IsManaged !== true) {
            return '\u672a\u7eb3\u7ba1';
        }

        return part.ReadIdentityFromMetadata ? '\u5df2\u7eb3\u7ba1\uff08\u8bfb\u53d6 MKV \u5143\u6570\u636e\uff09' : '\u5df2\u7eb3\u7ba1';
    }

    function renderCurrentPart(part) {
        if (!part) {
            return '<div class="subtitles-tools-summary-item">\u5f53\u524d\u6ca1\u6709\u53ef\u5c55\u793a\u7684\u5206\u6bb5\u4fe1\u606f\u3002</div>';
        }

        const managedText = getManagedStatusText(part);

        return `
            <div class="subtitles-tools-card">
                <div class="subtitles-tools-card-title">${escapeHtml(part.Label)} \u00b7 ${escapeHtml(part.FileName)}</div>
                <div class="subtitles-tools-card-meta">
                    <span>\u5bb9\u5668\uff1a${escapeHtml(part.Container || 'unknown')}</span>
                    <span>${managedText}</span>
                    <span>\u8def\u5f84\uff1a${escapeHtml(part.MediaPath)}</span>
                </div>
            </div>
        `;
    }

    function renderOverlay() {
        const overlay = ensureOverlay();
        if (!state.itemData) {
            overlay.innerHTML = '';
            return;
        }

        const activePart = getActivePart();
        const embeddedSubtitlesHtml = activePart ? renderEmbeddedSubtitles(activePart) : '';
        const searchResultsHtml = activePart
            ? renderResults(activePart.Id)
            : '<div class="subtitles-tools-summary-item">\u5f53\u524d\u6ca1\u6709\u53ef\u5c55\u793a\u7684\u5206\u6bb5\u3002</div>';

        overlay.innerHTML = `
            <div class="subtitles-tools-panel">
                <div class="subtitles-tools-header">
                    <div>
                        <h2 class="subtitles-tools-title">${escapeHtml(state.itemData.Name)}</h2>
                        <div class="subtitles-tools-subtitle">
                            ${escapeHtml(state.itemData.ItemType)} \u00b7 ${state.itemData.IsMultipart ? '\u591a\u5206\u6bb5\u5a92\u4f53' : '\u5355\u6587\u4ef6\u5a92\u4f53'}
                        </div>
                    </div>
                    <button class="subtitles-tools-close" type="button" data-action="close">\u00d7</button>
                </div>
                <div class="subtitles-tools-toolbar">
                    <button class="subtitles-tools-button is-secondary" type="button" data-action="refresh">\u5237\u65b0\u72b6\u6001</button>
                    <button class="subtitles-tools-button" type="button" data-action="search-part">\u641c\u7d22\u5f53\u524d\u5206\u6bb5</button>
                    <button class="subtitles-tools-button" type="button" data-action="convert-part">\u8f6c\u6362\u5f53\u524d\u5206\u6bb5\u4e3a MKV</button>
                    <button class="subtitles-tools-button" type="button" data-action="convert-group">\u4e00\u952e\u6574\u7ec4\u8f6c\u6362\u4e3a MKV</button>
                    <button class="subtitles-tools-button" type="button" data-action="download-best">\u4e00\u952e\u6574\u7ec4\u6700\u4f73\u5339\u914d\u5e76\u5185\u5c01</button>
                </div>
                <div class="subtitles-tools-toolbar">
                    <div class="subtitles-tools-status"></div>
                </div>
                <div class="subtitles-tools-body">
                    <div class="subtitles-tools-parts">
                        ${state.itemData.Parts.map(part => {
                            const managedText = getManagedStatusText(part);
                            return `
                                <button
                                    class="subtitles-tools-part-button ${part.Id === state.activePartId ? 'is-active' : ''}"
                                    type="button"
                                    data-action="select-part"
                                    data-part-id="${escapeHtml(part.Id)}">
                                    <div>${escapeHtml(part.Label)}</div>
                                    <div class="subtitles-tools-part-meta">
                                        <span>${escapeHtml(part.FileName)}</span>
                                        <span>${escapeHtml(part.Container || 'unknown')}</span>
                                        <span>${managedText}</span>
                                    </div>
                                </button>
                            `;
                        }).join('')}
                    </div>
                    <div class="subtitles-tools-main">
                        <div class="subtitles-tools-section">
                            <h3>\u5f53\u524d\u5206\u6bb5</h3>
                            ${renderCurrentPart(activePart)}
                            <div class="subtitles-tools-note">
                                \u63d2\u4ef6\u73b0\u5728\u53ea\u8ba4\u89c6\u9891\u81ea\u8eab\u7684 MKV \u5143\u6570\u636e\u6765\u5224\u65ad\u662f\u5426\u5df2\u7eb3\u7ba1\u3002\u641c\u7d22\u3001\u8f6c\u6362\u548c\u4e0b\u8f7d\u5b57\u5e55\u524d\uff0c\u90fd\u4f1a\u5148\u786e\u4fdd\u5f53\u524d\u5206\u6bb5\u5df2\u7ecf\u7eb3\u7ba1\uff1b\u4e0b\u8f7d\u5b57\u5e55\u540e\u4f1a\u5148\u8f6c\u6210\u4e34\u65f6 UTF-8 SRT\uff0c\u518d\u5199\u5165\u5f53\u524d\u5206\u6bb5\u7684 MKV \u5bb9\u5668\uff0c\u6210\u529f\u540e\u81ea\u52a8\u5220\u9664\u4e34\u65f6 SRT\u3002
                            </div>
                        </div>
                        <div class="subtitles-tools-section">
                            <h3>\u5f53\u524d\u5df2\u5185\u5c01\u5b57\u5e55\u6d41</h3>
                            <div class="subtitles-tools-card-list">${embeddedSubtitlesHtml}</div>
                        </div>
                        <div class="subtitles-tools-section">
                            <h3>\u641c\u7d22\u7ed3\u679c</h3>
                            <div class="subtitles-tools-card-list">${searchResultsHtml}</div>
                        </div>
                        ${renderSummary(state.lastBatchItems)}
                    </div>
                </div>
            </div>
        `;

        overlay.querySelectorAll('[data-action]').forEach(element => {
            element.addEventListener('click', handleOverlayAction);
        });
    }

    function openOverlay() {
        const overlay = ensureOverlay();
        overlay.classList.add('is-open');
        renderOverlay();
        setStatus('', '');
    }

    function closeOverlay() {
        const overlay = document.getElementById(CONFIG.overlayId);
        if (overlay) {
            overlay.classList.remove('is-open');
        }
    }

    async function refreshOverlayData() {
        const currentItemId = state.itemId || getCurrentItemId();
        if (!currentItemId) {
            throw new Error('当前页面没有可管理的媒体项。');
        }

        await fetchParts(currentItemId, true);
        renderOverlay();
    }

    async function searchActivePart() {
        const activePart = getActivePart();
        if (!activePart) {
            throw new Error('\u5f53\u524d\u6ca1\u6709\u9009\u4e2d\u7684\u6709\u6548\u5206\u6bb5\u3002');
        }

        setStatus(`\u6b63\u5728\u4e3a ${activePart.Label} \u641c\u7d22\u5b57\u5e55\u2026\u2026`, '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/parts/${activePart.Id}/search`,
            'POST',
            {});
        state.searchResults.set(activePart.Id, payload.Items || []);
        state.lastBatchItems = [];
        applyOperationResultToPart(activePart.Id, payload);
        renderOverlay();
        await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, payload));
        setStatus(`\u5df2\u627e\u5230 ${(payload.Items || []).length} \u6761\u5b57\u5e55\u5019\u9009\u3002`, 'success');
    }

    function findCandidate(partId, subtitleId) {
        const candidates = state.searchResults.get(partId) || [];
        return candidates.find(item => item.Id === subtitleId) || null;
    }

    async function convertCurrentPart() {
        const activePart = getActivePart();
        if (!activePart) {
            throw new Error('当前未选中有效分段。');
        }

        setStatus(`正在把 ${activePart.Label} 转换为 MKV……`, '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/parts/${activePart.Id}/convert`,
            'POST',
            {});
        state.lastBatchItems = [];
        applyOperationResultToPart(activePart.Id, payload);
        renderOverlay();
        await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, payload));
        setStatus(payload.Message || '当前分段转换完成。', 'success');
    }

    async function convertGroup() {
        setStatus('正在按顺序转换整组分段为 MKV……', '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/convert-group`,
            'POST',
            {});
        state.lastBatchItems = Array.isArray(payload.Items) ? payload.Items : [];
        applyBatchResults(state.lastBatchItems);
        renderOverlay();
        await refreshOverlayDataWithRetry(createBatchRefreshValidator(state.lastBatchItems));
        setStatus(payload.Message || '整组转换完成。', payload.Status === 'completed' ? 'success' : '');
    }

    async function downloadSelectedCandidate(subtitleId) {
        const activePart = getActivePart();
        if (!activePart) {
            throw new Error('当前未选中有效分段。');
        }

        const candidate = findCandidate(activePart.Id, subtitleId);
        if (!candidate) {
            throw new Error('未找到对应的字幕候选。');
        }

        setStatus(`正在下载并内封 ${candidate.DisplayName}……`, '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/parts/${activePart.Id}/download`,
            'POST',
            {
                SubtitleId: candidate.Id,
                Name: candidate.Name,
                Ext: candidate.Ext,
                Languages: candidate.Languages,
                Language: candidate.Language
            });

        if (payload.Status !== 'embedded') {
            throw new Error(payload.Message || '字幕内封失败。');
        }

        state.lastBatchItems = [];
        applyOperationResultToPart(activePart.Id, payload);
        renderOverlay();
        await refreshOverlayDataWithRetry(createSinglePartRefreshValidator(activePart.Id, payload));
        setStatus(payload.Message || '字幕已内封到当前分段。', 'success');
    }

    async function deleteEmbeddedSubtitle(streamIndexText) {
        const activePart = getActivePart();
        if (!activePart) {
            throw new Error('当前未选中有效分段。');
        }

        const streamIndex = Number.parseInt(streamIndexText, 10);
        if (!Number.isInteger(streamIndex)) {
            throw new Error('字幕流索引无效。');
        }

        const confirmed = window.confirm(`确认删除内封字幕流 #${streamIndex} 吗？当前仅允许删除插件写入的字幕流。`);
        if (!confirmed) {
            setStatus('已取消删除。', '');
            return;
        }

        setStatus(`正在删除内封字幕流 #${streamIndex}……`, '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/parts/${activePart.Id}/delete-embedded-subtitle`,
            'POST',
            { StreamIndex: streamIndex });
        applyDeleteResultToPart(activePart.Id, streamIndex);
        renderOverlay();
        await refreshOverlayDataWithRetry(payloadResult => {
            if (!payloadResult || !Array.isArray(payloadResult.Parts)) {
                return false;
            }

            const refreshedPart = payloadResult.Parts.find(part => part.Id === activePart.Id);
            return refreshedPart && Array.isArray(refreshedPart.EmbeddedSubtitles)
                ? !refreshedPart.EmbeddedSubtitles.some(track => track.StreamIndex === streamIndex)
                : false;
        });
        setStatus(payload.Message || `已删除内封字幕流 #${streamIndex}。`, 'success');
    }

    async function runDownloadBest() {
        setStatus('正在为整组分段搜索最佳字幕并内封……', '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/download-best`,
            'POST',
            {});

        state.lastBatchItems = Array.isArray(payload.Items) ? payload.Items : [];
        applyBatchResults(state.lastBatchItems);
        renderOverlay();
        await refreshOverlayDataWithRetry(createBatchRefreshValidator(state.lastBatchItems));
        setStatus(payload.Message || '整组字幕内封完成。', payload.Status === 'completed' ? 'success' : '');
    }

    async function handleOverlayAction(event) {
        const action = event.currentTarget.getAttribute('data-action');
        if (!action || state.busy) {
            return;
        }

        try {
            state.busy = true;
            if (action === 'close') {
                closeOverlay();
                return;
            }

            if (action === 'refresh') {
                setStatus('正在刷新分段状态……', '');
                await refreshOverlayData();
                setStatus('分段状态已刷新。', 'success');
                return;
            }

            if (action === 'select-part') {
                state.activePartId = event.currentTarget.getAttribute('data-part-id');
                renderOverlay();
                setStatus('', '');
                return;
            }

            if (action === 'search-part') {
                await searchActivePart();
                return;
            }

            if (action === 'convert-part') {
                await convertCurrentPart();
                return;
            }

            if (action === 'convert-group') {
                await convertGroup();
                return;
            }

            if (action === 'download-best') {
                await runDownloadBest();
                return;
            }

            if (action === 'download-candidate') {
                const subtitleId = event.currentTarget.getAttribute('data-subtitle-id');
                await downloadSelectedCandidate(subtitleId);
                return;
            }

            if (action === 'delete-embedded') {
                const streamIndex = event.currentTarget.getAttribute('data-stream-index');
                await deleteEmbeddedSubtitle(streamIndex);
            }
        } catch (error) {
            setStatus(extractErrorMessage(error), 'error');
        } finally {
            state.busy = false;
        }
    }

    function scheduleRefresh(forceReload) {
        window.setTimeout(function () {
            refreshCurrentPageState(forceReload).catch(function () {
                hideButton();
            });
        }, 200);
    }

    injectStyles();
    ensureRoot();
    ensureOverlay();
    scheduleRefresh(true);
    window.addEventListener('hashchange', function () { scheduleRefresh(true); });
    window.addEventListener('popstate', function () { scheduleRefresh(true); });
    window.setInterval(function () { scheduleRefresh(false); }, CONFIG.routePollMs);
})();
