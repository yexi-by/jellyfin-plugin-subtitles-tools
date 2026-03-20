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
        playbackPollMs: 1500,
        autoApplyRetryWindowMs: 8000,
        apiRoot: 'Jellyfin.Plugin.SubtitlesTools'
    };

    const state = {
        itemId: null,
        itemData: null,
        activePartId: null,
        searchResults: new Map(),
        lastBatchItems: [],
        lastDownloadedSubtitles: new Map(),
        busy: false,
        lastLocation: '',
        playbackKey: '',
        autoApplyWindowStartedAt: 0,
        autoApplyHandledPlaybackKey: '',
        autoApplyBusy: false
    };

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

            .subtitles-tools-fab:hover {
                filter: brightness(1.06);
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
                width: min(1100px, 100%);
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

            .subtitles-tools-button.is-warning {
                background: #70463a;
            }

            .subtitles-tools-button.is-success {
                background: #216b49;
            }

            .subtitles-tools-button.is-small {
                padding: 8px 12px;
                font-size: 13px;
            }

            .subtitles-tools-button:disabled {
                opacity: 0.55;
                cursor: not-allowed;
            }

            .subtitles-tools-body {
                display: grid;
                grid-template-columns: minmax(220px, 280px) minmax(0, 1fr);
                gap: 0;
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

            .subtitles-tools-chip-list {
                display: flex;
                flex-wrap: wrap;
                gap: 8px;
            }

            .subtitles-tools-chip {
                padding: 6px 10px;
                border-radius: 999px;
                background: rgba(255, 255, 255, 0.08);
                font-size: 12px;
                color: rgba(255, 255, 255, 0.86);
            }

            .subtitles-tools-chip.is-accent {
                background: rgba(79, 166, 255, 0.22);
                color: #d8ecff;
            }

            .subtitles-tools-chip.is-warning {
                background: rgba(213, 136, 85, 0.22);
                color: #ffd7bf;
            }

            .subtitles-tools-note {
                font-size: 13px;
                color: rgba(245, 247, 250, 0.72);
                line-height: 1.6;
            }

            .subtitles-tools-remembered-card,
            .subtitles-tools-existing-card,
            .subtitles-tools-result-card,
            .subtitles-tools-summary-item {
                border-radius: 18px;
                padding: 16px;
                background: rgba(255, 255, 255, 0.05);
            }

            .subtitles-tools-remembered-card,
            .subtitles-tools-existing-card {
                display: grid;
                gap: 10px;
            }

            .subtitles-tools-existing-list,
            .subtitles-tools-results,
            .subtitles-tools-summary-list {
                display: grid;
                gap: 12px;
            }

            .subtitles-tools-existing-header,
            .subtitles-tools-result-name {
                font-size: 15px;
                font-weight: 700;
            }

            .subtitles-tools-existing-meta,
            .subtitles-tools-result-meta {
                display: flex;
                flex-wrap: wrap;
                gap: 8px 14px;
                font-size: 12px;
                color: rgba(255, 255, 255, 0.72);
            }

            .subtitles-tools-actions {
                display: flex;
                flex-wrap: wrap;
                gap: 8px;
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
        root.innerHTML = '<button class="subtitles-tools-fab" type="button">分段字幕</button>';
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

    async function fetchParts(itemId, forceReload) {
        if (!forceReload && state.itemData && state.itemId === itemId) {
            return state.itemData;
        }

        const payload = await apiRequest(`${CONFIG.apiRoot}/Items/${itemId}/parts`, 'GET');
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
            state.lastDownloadedSubtitles = new Map();
        }

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
            state.lastDownloadedSubtitles = new Map();
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
            state.lastDownloadedSubtitles = new Map();
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

    function renderRememberedSection(part) {
        if (!part) {
            return '<div class="subtitles-tools-remembered-card">未找到当前分段。</div>';
        }

        const remembered = part.RememberedSubtitle || { Status: 'none' };
        const lastDownloadedFileName = state.lastDownloadedSubtitles.get(part.Id);
        const actions = [];
        let chips = '';
        let description = '当前分段还没有记住字幕。';

        if (remembered.Status === 'active') {
            chips = '<span class="subtitles-tools-chip is-accent">已记住</span>';
            description = `当前会优先尝试自动切换到 ${escapeHtml(remembered.FileName)}。`;
            actions.push('<button class="subtitles-tools-button is-secondary is-small" type="button" data-action="clear-remembered">取消记住</button>');
        } else if (remembered.Status === 'missing') {
            chips = '<span class="subtitles-tools-chip is-warning">记忆已失效</span>';
            description = `已记住的字幕 ${escapeHtml(remembered.FileName)} 不再存在，需要重新选择。`;
            actions.push('<button class="subtitles-tools-button is-warning is-small" type="button" data-action="clear-remembered">清除失效记忆</button>');
        }

        if (lastDownloadedFileName && lastDownloadedFileName !== remembered.FileName) {
            actions.push('<button class="subtitles-tools-button is-success is-small" type="button" data-action="remember-last-downloaded">记住刚下载的字幕</button>');
        }

        const meta = remembered.Status === 'none'
            ? '<div class="subtitles-tools-note">只有在你明确点击“记住这条字幕”后，插件才会在下次播放时尝试自动切换。</div>'
            : `
                <div class="subtitles-tools-existing-meta">
                    <span>文件：${escapeHtml(remembered.FileName)}</span>
                    <span>语言：${escapeHtml(remembered.Language || 'und')}</span>
                    <span>格式：${escapeHtml(remembered.Format || 'srt')}</span>
                </div>
            `;

        return `
            <div class="subtitles-tools-remembered-card">
                <div class="subtitles-tools-existing-header">当前字幕记忆 ${chips}</div>
                <div class="subtitles-tools-note">${description}</div>
                ${meta}
                ${actions.length > 0 ? `<div class="subtitles-tools-actions">${actions.join('')}</div>` : ''}
            </div>
        `;
    }

    function renderExistingSubtitles(part) {
        if (!part || !Array.isArray(part.ExistingSubtitles) || part.ExistingSubtitles.length === 0) {
            return '<div class="subtitles-tools-remembered-card">当前没有已保存的外部字幕。</div>';
        }

        return part.ExistingSubtitles.map(item => `
            <div class="subtitles-tools-existing-card">
                <div class="subtitles-tools-existing-header">
                    ${escapeHtml(item.FileName)}
                    ${item.IsRemembered ? '<span class="subtitles-tools-chip is-accent">已记住</span>' : ''}
                </div>
                <div class="subtitles-tools-existing-meta">
                    <span>语言：${escapeHtml(item.Language)}</span>
                    <span>格式：${escapeHtml(item.Format)}</span>
                </div>
                <div class="subtitles-tools-actions">
                    ${item.IsRemembered
                        ? '<button class="subtitles-tools-button is-secondary is-small" type="button" data-action="clear-remembered">取消记住</button>'
                        : `<button class="subtitles-tools-button is-small" type="button" data-action="remember-existing" data-subtitle-file="${escapeHtml(item.FileName)}">记住这条</button>`}
                </div>
            </div>
        `).join('');
    }

    function renderResults(partId) {
        const results = state.searchResults.get(partId) || [];
        if (results.length === 0) {
            return '<div class="subtitles-tools-summary-item">当前还没有搜索结果。你可以先点击“搜索当前分段”。</div>';
        }

        return results.map(item => `
            <div class="subtitles-tools-result-card">
                <div class="subtitles-tools-result-name">${escapeHtml(item.DisplayName)}</div>
                <div class="subtitles-tools-result-meta">
                    <span>语言：${escapeHtml(item.Language)}</span>
                    <span>格式：${escapeHtml(item.Format)}</span>
                    <span>分数：${escapeHtml(item.Score)}</span>
                    <span>目标文件：${escapeHtml(item.TargetFileName)}</span>
                </div>
                <button
                    class="subtitles-tools-button"
                    type="button"
                    data-action="download-candidate"
                    data-subtitle-id="${escapeHtml(item.Id)}">
                    下载到当前分段
                </button>
            </div>
        `).join('');
    }

    function renderSummary(items) {
        if (!Array.isArray(items) || items.length === 0) {
            return '';
        }

        return `
            <div class="subtitles-tools-section">
                <h3>最近一次批量执行结果</h3>
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

    function renderOverlay() {
        const overlay = ensureOverlay();
        if (!state.itemData) {
            overlay.innerHTML = '';
            return;
        }

        const activePart = getActivePart();
        const searchResultsHtml = activePart ? renderResults(activePart.Id) : '<div class="subtitles-tools-summary-item">未找到当前分段。</div>';
        const existingSubtitlesHtml = activePart ? renderExistingSubtitles(activePart) : '';
        const rememberedHtml = activePart ? renderRememberedSection(activePart) : '';

        overlay.innerHTML = `
            <div class="subtitles-tools-panel">
                <div class="subtitles-tools-header">
                    <div>
                        <h2 class="subtitles-tools-title">${escapeHtml(state.itemData.Name)}</h2>
                        <div class="subtitles-tools-subtitle">
                            ${escapeHtml(state.itemData.ItemType)} · ${state.itemData.IsMultipart ? '多分段媒体' : '单文件媒体'}
                        </div>
                    </div>
                    <button class="subtitles-tools-close" type="button" data-action="close">×</button>
                </div>
                <div class="subtitles-tools-toolbar">
                    <button class="subtitles-tools-button is-secondary" type="button" data-action="refresh">刷新分段状态</button>
                    <button class="subtitles-tools-button" type="button" data-action="search-part">搜索当前分段</button>
                    <button class="subtitles-tools-button" type="button" data-action="download-best">一键全段最佳匹配下载</button>
                </div>
                <div class="subtitles-tools-toolbar">
                    <div class="subtitles-tools-status"></div>
                </div>
                <div class="subtitles-tools-body">
                    <div class="subtitles-tools-parts">
                        ${state.itemData.Parts.map(part => `
                            <button
                                class="subtitles-tools-part-button ${part.Id === state.activePartId ? 'is-active' : ''}"
                                type="button"
                                data-action="select-part"
                                data-part-id="${escapeHtml(part.Id)}">
                                <div>${escapeHtml(part.Label)}</div>
                                <div class="subtitles-tools-part-meta">${escapeHtml(part.FileName)}</div>
                            </button>
                        `).join('')}
                    </div>
                    <div class="subtitles-tools-main">
                        <div class="subtitles-tools-section">
                            <h3>当前分段</h3>
                            <div class="subtitles-tools-summary-item">
                                ${activePart ? `${escapeHtml(activePart.Label)} · ${escapeHtml(activePart.FileName)}` : '未找到当前分段。'}
                            </div>
                        </div>
                        <div class="subtitles-tools-section">
                            <h3>自动切换记忆</h3>
                            ${rememberedHtml}
                        </div>
                        <div class="subtitles-tools-section">
                            <h3>已保存字幕</h3>
                            <div class="subtitles-tools-existing-list">${existingSubtitlesHtml}</div>
                        </div>
                        <div class="subtitles-tools-section">
                            <h3>搜索结果</h3>
                            <div class="subtitles-tools-results">${searchResultsHtml}</div>
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
            throw new Error('当前未选中有效分段。');
        }

        setStatus(`正在搜索 ${activePart.Label} 的字幕……`, '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/parts/${activePart.Id}/search`,
            'POST',
            {});
        state.searchResults.set(activePart.Id, payload.Items || []);
        renderOverlay();
        setStatus(`已返回 ${payload.Items.length} 条字幕候选。`, 'success');
    }

    function findCandidate(partId, subtitleId) {
        const candidates = state.searchResults.get(partId) || [];
        return candidates.find(item => item.Id === subtitleId) || null;
    }

    function formatConflictMessage(conflict) {
        const existingFiles = Array.isArray(conflict.ExistingFiles) ? conflict.ExistingFiles.join('\n') : '';
        return [
            `${conflict.PartLabel} 已存在 ${conflict.Language} 字幕。`,
            `目标文件：${conflict.TargetFileName}`,
            existingFiles ? `现有文件：\n${existingFiles}` : '',
            '',
            '确认后将覆盖或替换这些文件。'
        ].filter(Boolean).join('\n');
    }

    async function downloadCandidate(activePart, candidate, overwriteExisting) {
        return apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/parts/${activePart.Id}/download`,
            'POST',
            {
                SubtitleId: candidate.Id,
                Name: candidate.Name,
                Ext: candidate.Ext,
                Languages: candidate.Languages,
                Language: candidate.Language,
                OverwriteExisting: overwriteExisting
            });
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

        setStatus(`正在下载 ${candidate.DisplayName}……`, '');
        let payload = await downloadCandidate(activePart, candidate, false);
        if (payload.Status === 'confirmation_required' && payload.Conflict) {
            const confirmed = window.confirm(formatConflictMessage(payload.Conflict));
            if (!confirmed) {
                setStatus('已取消下载。', '');
                return;
            }

            payload = await downloadCandidate(activePart, candidate, true);
        }

        if (payload.Status !== 'downloaded') {
            throw new Error(payload.Message || '字幕下载失败。');
        }

        state.lastDownloadedSubtitles.set(activePart.Id, payload.WrittenSubtitle.FileName);
        state.lastBatchItems = [];
        await refreshOverlayData();
        setStatus(`字幕已保存为 ${payload.WrittenSubtitle.FileName}。`, 'success');
    }

    async function downloadBest(overwriteExisting) {
        return apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/download-best`,
            'POST',
            { OverwriteExisting: overwriteExisting });
    }

    async function runDownloadBest() {
        setStatus('正在为所有分段选择最佳字幕……', '');
        let payload = await downloadBest(false);
        if (payload.Status === 'confirmation_required' && Array.isArray(payload.Conflicts) && payload.Conflicts.length > 0) {
            const message = payload.Conflicts.map(formatConflictMessage).join('\n\n');
            const confirmed = window.confirm(message);
            if (!confirmed) {
                setStatus('已取消一键下载。', '');
                renderOverlay();
                return;
            }

            payload = await downloadBest(true);
        }

        state.lastBatchItems = Array.isArray(payload.Items) ? payload.Items : [];
        await refreshOverlayData();
        setStatus(payload.Message || '批量处理已完成。', payload.Status === 'completed' ? 'success' : '');
    }

    async function rememberExistingSubtitle(subtitleFileName) {
        const activePart = getActivePart();
        if (!activePart) {
            throw new Error('当前未选中有效分段。');
        }

        setStatus(`正在记住 ${subtitleFileName}……`, '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/parts/${activePart.Id}/remembered-subtitle`,
            'POST',
            { SubtitleFileName: subtitleFileName });
        await refreshOverlayData();
        setStatus(payload.Message || '已记住这条字幕。', 'success');
    }

    async function clearRememberedSubtitle() {
        const activePart = getActivePart();
        if (!activePart) {
            throw new Error('当前未选中有效分段。');
        }

        setStatus(`正在清除 ${activePart.Label} 的字幕记忆……`, '');
        const payload = await apiRequest(
            `${CONFIG.apiRoot}/Items/${state.itemId}/parts/${activePart.Id}/remembered-subtitle`,
            'DELETE');
        await refreshOverlayData();
        setStatus(payload.Message || '已清除字幕记忆。', 'success');
    }

    async function rememberLastDownloadedSubtitle() {
        const activePart = getActivePart();
        if (!activePart) {
            throw new Error('当前未选中有效分段。');
        }

        const subtitleFileName = state.lastDownloadedSubtitles.get(activePart.Id);
        if (!subtitleFileName) {
            throw new Error('当前分段没有可记住的最近下载字幕。');
        }

        await rememberExistingSubtitle(subtitleFileName);
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

            if (action === 'download-best') {
                await runDownloadBest();
                return;
            }

            if (action === 'download-candidate') {
                const subtitleId = event.currentTarget.getAttribute('data-subtitle-id');
                await downloadSelectedCandidate(subtitleId);
                return;
            }

            if (action === 'remember-existing') {
                const subtitleFileName = event.currentTarget.getAttribute('data-subtitle-file');
                await rememberExistingSubtitle(subtitleFileName);
                return;
            }

            if (action === 'clear-remembered') {
                await clearRememberedSubtitle();
                return;
            }

            if (action === 'remember-last-downloaded') {
                await rememberLastDownloadedSubtitle();
            }
        } catch (error) {
            setStatus(error.message || '请求失败。', 'error');
        } finally {
            state.busy = false;
        }
    }

    function resetAutoApplyState() {
        state.playbackKey = '';
        state.autoApplyWindowStartedAt = 0;
        state.autoApplyHandledPlaybackKey = '';
    }

    function observePlaybackKey(playbackKey) {
        if (!playbackKey) {
            resetAutoApplyState();
            return;
        }

        if (state.playbackKey !== playbackKey) {
            state.playbackKey = playbackKey;
            state.autoApplyWindowStartedAt = Date.now();
            state.autoApplyHandledPlaybackKey = '';
        }
    }

    function shouldStopAutoApplyRetry(payload) {
        const playbackKey = payload.PlaybackKey || '';
        if (!playbackKey) {
            return true;
        }

        if (payload.Status === 'already_selected') {
            return true;
        }

        if (payload.Status === 'not_found_in_streams') {
            return Date.now() - state.autoApplyWindowStartedAt > CONFIG.autoApplyRetryWindowMs;
        }

        return payload.Status !== 'ready';
    }

    async function applyRememberedSubtitle(payload) {
        if (!payload || !payload.SessionId || payload.TargetSubtitleStreamIndex === null || payload.TargetSubtitleStreamIndex === undefined) {
            return;
        }

        await apiRequest(
            `Sessions/${payload.SessionId}/Command`,
            'POST',
            {
                Name: 'SetSubtitleStreamIndex',
                Arguments: {
                    Index: String(payload.TargetSubtitleStreamIndex)
                }
            });
    }

    async function pollRememberedSubtitleAutoApply() {
        if (state.autoApplyBusy) {
            return;
        }

        if (!window.ApiClient || typeof window.ApiClient.ajax !== 'function' || typeof window.ApiClient.getUrl !== 'function') {
            return;
        }

        state.autoApplyBusy = true;
        try {
            const payload = await apiRequest(`${CONFIG.apiRoot}/Playback/remembered-subtitle`, 'GET');
            const playbackKey = payload.PlaybackKey || '';
            observePlaybackKey(playbackKey);

            if (!playbackKey) {
                return;
            }

            if (state.autoApplyHandledPlaybackKey === playbackKey) {
                return;
            }

            if (payload.Status === 'ready') {
                if (Date.now() - state.autoApplyWindowStartedAt > CONFIG.autoApplyRetryWindowMs) {
                    state.autoApplyHandledPlaybackKey = playbackKey;
                    return;
                }

                await applyRememberedSubtitle(payload);
                state.autoApplyHandledPlaybackKey = playbackKey;
                return;
            }

            if (shouldStopAutoApplyRetry(payload)) {
                state.autoApplyHandledPlaybackKey = playbackKey;
            }
        } catch (error) {
            // 播放期自动切换属于尽力而为的能力，单次失败不应打断页面或播放器。
        } finally {
            state.autoApplyBusy = false;
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
    window.setInterval(function () { pollRememberedSubtitleAutoApply().catch(function () {}); }, CONFIG.playbackPollMs);
})();
