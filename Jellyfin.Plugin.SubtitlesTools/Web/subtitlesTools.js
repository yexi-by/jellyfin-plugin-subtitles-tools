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
        EnableAutoVideoConvertToMkv: page.querySelector('#enableAutoVideoConvertToMkv').checked,
        VideoConvertConcurrency: Number.parseInt(page.querySelector('#videoConvertConcurrency').value, 10) || 1,
        FfmpegExecutablePath: page.querySelector('#ffmpegExecutablePath').value.trim(),
        QsvRenderDevicePath: page.querySelector('#qsvRenderDevicePath').value.trim()
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
            view.querySelector('#enableAutoVideoConvertToMkv').checked = config.EnableAutoVideoConvertToMkv !== false;
            view.querySelector('#videoConvertConcurrency').value = config.VideoConvertConcurrency || 1;
            view.querySelector('#ffmpegExecutablePath').value = config.FfmpegExecutablePath || '';
            view.querySelector('#qsvRenderDevicePath').value = config.QsvRenderDevicePath || '/dev/dri/renderD128';
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
        const payload = JSON.stringify(formValue);
        const url = ApiClient.getUrl('Jellyfin.Plugin.SubtitlesTools/TestConnection');

        setButtonsDisabled(view, true);
        Dashboard.showLoadingMsg();
        ApiClient.ajax({ type: 'POST', url, data: payload, contentType: 'application/json' }).then(async response => {
            const body = await response.json();
            Dashboard.hideLoadingMsg();
            setButtonsDisabled(view, false);

            if (response.ok) {
                const health = body.Health || body.health || {};
                const ffmpeg = body.Ffmpeg || body.ffmpeg || {};
                const video = body.Video || body.video || {};
                const version = readValue(health, ['Version', 'version']);
                const providerName = readValue(health, ['ProviderName', 'providerName', 'provider_name']);
                const ffmpegPath = readValue(ffmpeg, ['ffmpegPath', 'FfmpegPath'], '未找到');
                const ffprobePath = readValue(ffmpeg, ['ffprobePath', 'FfprobePath'], '未找到');
                const qsvRenderDevicePath = readValue(video, ['qsvRenderDevicePath', 'QsvRenderDevicePath'], '未配置');
                const supportsH264Qsv = video.supportsH264Qsv === true ? '支持' : '不支持';
                setMessage(
                    messageElement,
                    `连接成功。服务版本：${version || '-'}，字幕源：${providerName || '-'}，FFmpeg：${ffmpegPath}，FFprobe：${ffprobePath}，QSV 设备：${qsvRenderDevicePath}，h264_qsv：${supportsH264Qsv}。`,
                    false);
                return;
            }

            const errorMessage = readValue(body, ['Message', 'message'], '连接失败。');
            setMessage(messageElement, errorMessage, true);
            Dashboard.processErrorResponse({ statusText: errorMessage });
        }).catch(() => {
            Dashboard.hideLoadingMsg();
            setButtonsDisabled(view, false);
            const errorMessage = '连接失败，请检查服务地址、FFmpeg 路径和网络状态。';
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
            config.EnableAutoVideoConvertToMkv = formValue.EnableAutoVideoConvertToMkv;
            config.VideoConvertConcurrency = formValue.VideoConvertConcurrency;
            config.FfmpegExecutablePath = formValue.FfmpegExecutablePath;
            config.QsvRenderDevicePath = formValue.QsvRenderDevicePath;

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
