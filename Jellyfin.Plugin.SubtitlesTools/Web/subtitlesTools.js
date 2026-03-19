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
        RequestTimeoutSeconds: Number.parseInt(page.querySelector('#requestTimeoutSeconds').value, 10) || 10
    };
}

function setButtonsDisabled(page, disabled) {
    page.querySelector('#testConnectionButton').disabled = disabled;
    page.querySelector('#saveButton').disabled = disabled;
}

export default function (view) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(SubtitlesToolsConfig.pluginUniqueId).then(config => {
            view.querySelector('#serviceBaseUrl').value = config.ServiceBaseUrl || 'http://127.0.0.1:8055';
            view.querySelector('#requestTimeoutSeconds').value = config.RequestTimeoutSeconds || 10;
            setMessage(view.querySelector('#testConnectionMessage'), '', false);
            Dashboard.hideLoadingMsg();
        }).catch(() => {
            Dashboard.hideLoadingMsg();
            Dashboard.processErrorResponse({ statusText: '加载插件配置失败。' });
        });
    });

    view.querySelector('#testConnectionButton').addEventListener('click', function () {
        const messageElement = view.querySelector('#testConnectionMessage');
        const payload = JSON.stringify(readForm(view));
        const url = ApiClient.getUrl('Jellyfin.Plugin.SubtitlesTools/TestConnection');

        setButtonsDisabled(view, true);
        Dashboard.showLoadingMsg();
        ApiClient.ajax({ type: 'POST', url, data: payload, contentType: 'application/json' }).then(async response => {
            const body = await response.json();
            Dashboard.hideLoadingMsg();
            setButtonsDisabled(view, false);

            if (response.ok) {
                const health = body.Health || {};
                setMessage(
                    messageElement,
                    `连接成功。版本：${health.Version || '-'}，字幕源：${health.ProviderName || '-'}。`,
                    false);
                return;
            }

            setMessage(messageElement, body.Message || '连接失败。', true);
            Dashboard.processErrorResponse({ statusText: body.Message || '连接失败。' });
        }).catch(() => {
            Dashboard.hideLoadingMsg();
            setButtonsDisabled(view, false);
            setMessage(messageElement, '连接失败，请检查地址、网络或服务状态。', true);
            Dashboard.processErrorResponse({ statusText: '连接失败，请检查地址、网络或服务状态。' });
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
