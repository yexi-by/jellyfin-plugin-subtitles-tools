const SubtitlesToolsConfig = {
    pluginUniqueId: 'b812b763-a31d-4d19-bfcc-15b9eac432cb'
};

function setMessage(element, text, isError) {
    element.innerText = text || '';
    element.style.color = isError ? '#d32f2f' : '';
}

function readForm(page) {
    return {
        ServiceBaseUrl: page.querySelector('#serviceBaseUrl').value.trim(),
        RequestTimeoutSeconds: Number.parseInt(page.querySelector('#requestTimeoutSeconds').value, 10) || 10,
        EnableAutoHashPrecompute: page.querySelector('#enableAutoHashPrecompute').checked,
        HashPrecomputeConcurrency: Number.parseInt(page.querySelector('#hashPrecomputeConcurrency').value, 10) || 1
    };
}

function setButtonsDisabled(page, disabled) {
    page.querySelector('#testConnectionButton').disabled = disabled;
    page.querySelector('#saveButton').disabled = disabled;
}

function readValue(source, keys, fallback = '') {
    if (!source) {
        return fallback;
    }

    for (const key of keys) {
        const value = source[key];
        if (value !== undefined && value !== null && value !== '') {
            return value;
        }
    }

    return fallback;
}

export default function (view) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(SubtitlesToolsConfig.pluginUniqueId).then(config => {
            view.querySelector('#serviceBaseUrl').value = config.ServiceBaseUrl || 'http://127.0.0.1:8055';
            view.querySelector('#requestTimeoutSeconds').value = config.RequestTimeoutSeconds || 10;
            view.querySelector('#enableAutoHashPrecompute').checked = !!config.EnableAutoHashPrecompute;
            view.querySelector('#hashPrecomputeConcurrency').value = config.HashPrecomputeConcurrency || 1;
            setMessage(view.querySelector('#testConnectionMessage'), '', false);
            Dashboard.hideLoadingMsg();
        }).catch(() => {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: '加载插件配置失败。' });
        });
    });

    view.querySelector('#testConnectionButton').addEventListener('click', function () {
        const messageElement = view.querySelector('#testConnectionMessage');
        const formValue = readForm(view);
        const payload = JSON.stringify({
            ServiceBaseUrl: formValue.ServiceBaseUrl,
            RequestTimeoutSeconds: formValue.RequestTimeoutSeconds
        });
        const url = ApiClient.getUrl('Jellyfin.Plugin.SubtitlesTools/TestConnection');

        setButtonsDisabled(view, true);
        Dashboard.showLoadingMsg();
        ApiClient.ajax({ type: 'POST', url, data: payload, contentType: 'application/json' }).then(async response => {
            const body = await response.json();
            Dashboard.hideLoadingMsg();
            setButtonsDisabled(view, false);

            if (response.ok) {
                const health = body.Health || body.health || {};
                const version = readValue(health, ['Version', 'version']);
                const providerName = readValue(health, ['ProviderName', 'providerName', 'provider_name']);
                setMessage(
                    messageElement,
                    `连接成功。版本：${version || '-'}，字幕源：${providerName || '-'}。`,
                    false);
                return;
            }

            const errorMessage = readValue(body, ['Message', 'message'], '连接失败。');
            setMessage(messageElement, errorMessage, true);
            Dashboard.processErrorResponse({ statusText: errorMessage });
        }).catch(() => {
            Dashboard.hideLoadingMsg();
            setButtonsDisabled(view, false);
            const errorMessage = '连接失败，请检查地址、网络或服务状态。';
            setMessage(messageElement, errorMessage, true);
            Dashboard.processErrorResponse({ statusText: errorMessage });
        });
    });

    view.querySelector('#SubtitlesToolsConfigForm').addEventListener('submit', function (event) {
        event.preventDefault();
        Dashboard.showLoadingMsg();
        setButtonsDisabled(view, true);

        ApiClient.getPluginConfiguration(SubtitlesToolsConfig.pluginUniqueId).then(config => {
            const formValue = readForm(view);
            config.ServiceBaseUrl = formValue.ServiceBaseUrl;
            config.RequestTimeoutSeconds = formValue.RequestTimeoutSeconds;
            config.EnableAutoHashPrecompute = formValue.EnableAutoHashPrecompute;
            config.HashPrecomputeConcurrency = formValue.HashPrecomputeConcurrency;

            return ApiClient.updatePluginConfiguration(SubtitlesToolsConfig.pluginUniqueId, config);
        }).then(result => {
            Dashboard.hideLoadingMsg();
            setButtonsDisabled(view, false);
            Dashboard.processPluginConfigurationUpdateResult(result);
        }).catch(() => {
            Dashboard.hideLoadingMsg();
            setButtonsDisabled(view, false);
            Dashboard.processErrorResponse({ statusText: '保存插件配置失败。' });
        });

        return false;
    });
}
