# jellyfin-plugin-subtitles-tools

`jellyfin-plugin-subtitles-tools` 是 `subtitles-tools` 的 Jellyfin 客户端插件，实现了本地视频 `CID/GCID` 计算、调用 Python 服务端搜索字幕，以及把远程字幕结果接入 Jellyfin 原生字幕流程。

服务端项目：
<https://github.com/yexi-by/subtitles-tools>

插件仓库清单地址：
<https://raw.githubusercontent.com/yexi-by/jellyfin-plugin-subtitles-tools/main/manifest/stable.json>

## 支持范围

- 仅支持 Jellyfin `10.11.6`
- 仅支持 `Movie` 和 `Episode`
- 仅支持本地可直接读取的视频文件
- 不支持 `.strm`
- 不做多字幕源聚合，当前只对接 `subtitles-tools` Python 服务端

## 构建

```bash
dotnet restore
dotnet build
dotnet test
```

## 插件配置

插件配置页当前只暴露两个字段：

- `Python 服务地址`
- `请求超时（秒）`

配置页提供“测试连接”按钮，会调用 Python 服务端的 `/health` 接口验证连通性，不需要先保存再测试。

## 安装方式

1. 先部署服务端项目：<https://github.com/yexi-by/subtitles-tools>
2. 在 Jellyfin 后台的插件仓库中添加上面的 manifest 地址
3. 安装 `Subtitles Tools`
4. 在插件配置页填入 Python 服务地址，例如 `http://127.0.0.1:8055`

## 发布文件

- 当前首版插件压缩包位于 [release-assets/Jellyfin.Plugin.SubtitlesTools_0.1.0.0.zip](./release-assets/Jellyfin.Plugin.SubtitlesTools_0.1.0.0.zip)
- GitHub Actions 已包含基于 tag 的 Release 上传工作流
- manifest 指向 GitHub Releases 的固定下载地址
