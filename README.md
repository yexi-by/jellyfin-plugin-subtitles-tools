# jellyfin-plugin-subtitles-tools

`jellyfin-plugin-subtitles-tools` 是 [`subtitles-tools`](https://github.com/yexi-by/subtitles-tools) 的 Jellyfin 客户端插件，负责在 Jellyfin 里计算本地视频 `CID/GCID`、调用 Python 服务端搜索字幕，并以 Jellyfin 原生远程字幕提供器的方式展示和下载字幕。

插件仓库清单地址：
<https://raw.githubusercontent.com/yexi-by/jellyfin-plugin-subtitles-tools/main/manifest/stable.json>

## 支持范围

- 仅支持 Jellyfin `10.11.6`
- 仅支持 `Movie` 和 `Episode`
- 仅支持本地可直接读取的视频文件
- 不支持 `.strm`
- 当前只对接 `subtitles-tools` Python 服务端

## 构建

```bash
dotnet restore
dotnet build
dotnet test
```

## 插件配置

插件配置页当前包含四个字段：

- `Python 服务地址`
- `请求超时（秒）`
- `新增媒体入库后自动预计算视频哈希`
- `视频哈希预计算并发数`

配置页提供“测试连接”按钮，会调用 Python 服务端的 `/health` 接口验证连通性，不需要先保存再测试。

自动预计算默认关闭。开启后，插件会在 Jellyfin 新增 `Movie` 和 `Episode` 入库时，后台异步预计算该文件的 `CID/GCID`，并写入插件自身缓存。`.strm` 和不可直接读取的文件会被自动跳过。

预计算并发数默认是 `1`。网络盘建议保持 `1`，本地 SSD 或更快的共享存储可按机器性能适当调高。

除了自动预计算，插件还会在 Jellyfin 的“计划任务”页面注册一个手动任务：`预计算缺失的视频哈希`。这个任务只扫描 Jellyfin 已入库的电影和剧集，并且只为当前缓存里还没有有效哈希的本地文件补算 `CID/GCID`。

## 安装方式

1. 先部署服务端项目：<https://github.com/yexi-by/subtitles-tools>
2. 在 Jellyfin 后台的插件仓库中添加上面的 manifest 地址
3. 安装 `Subtitles Tools`
4. 在插件配置页填入 Python 服务地址，例如 `http://127.0.0.1:8055`
5. 如需减少首次搜字幕等待时间，可开启自动预计算，或到计划任务页手动执行一次“预计算缺失的视频哈希”

## 发布文件

- 当前最新插件压缩包位于 [release-assets/Jellyfin.Plugin.SubtitlesTools_0.1.2.0.zip](./release-assets/Jellyfin.Plugin.SubtitlesTools_0.1.2.0.zip)
- GitHub Actions 已包含基于 tag 的 Release 上传工作流
- manifest 指向 GitHub Releases 的固定下载地址
