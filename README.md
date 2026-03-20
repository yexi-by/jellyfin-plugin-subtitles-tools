# jellyfin-plugin-subtitles-tools

`jellyfin-plugin-subtitles-tools` 是 [`subtitles-tools`](https://github.com/yexi-by/subtitles-tools) 的 Jellyfin 客户端插件。  
它负责在 Jellyfin 中计算本地视频的 `CID/GCID`，调用 Python 服务搜索迅雷字幕，并提供一套插件自带的“分段字幕”管理页面。

插件仓库清单地址：  
<https://raw.githubusercontent.com/yexi-by/jellyfin-plugin-subtitles-tools/main/manifest/stable.json>

## 支持范围

- 仅支持 Jellyfin `10.11.6`
- 仅支持 `Movie` 和 `Episode`
- 仅支持本地可直接读取的视频文件
- 不支持 `.strm`
- 当前只对接 [`subtitles-tools`](https://github.com/yexi-by/subtitles-tools) Python 服务端

## 安装方式

1. 先部署服务端项目：<https://github.com/yexi-by/subtitles-tools>
2. 在 Jellyfin 后台的“插件仓库”中添加上面的 manifest 地址
3. 安装 `Subtitles Tools`
4. 在插件配置页填写 Python 服务地址，例如 `http://127.0.0.1:8055`
5. 使用“测试连接”确认插件与服务端连通

## 插件配置

当前配置项包括：

- `Python 服务地址`
- `请求超时（秒）`
- `新增媒体入库后自动预计算视频哈希`
- `视频哈希预计算并发数`

自动预计算默认关闭。开启后，插件会在新 `Movie` / `Episode` 入库时异步预计算 `CID/GCID` 并写入插件缓存。  
此外，Jellyfin 的“计划任务”页面还会新增一个手动任务：

- `预计算缺失的视频哈希`

这个任务只扫描已入库的电影和剧集，并只为当前缓存中还没有有效哈希的本地文件补算 `CID/GCID`。

## 使用方式

安装并配置完成后，在电影或剧集详情页会出现插件自带的 `分段字幕` 入口按钮。

这套页面统一支持单文件媒体和多分段媒体：

- 单文件媒体：只显示一个分段
- 多分段媒体：支持 `cd1/cd2/cd3`、`part1/part2`、`disc1/disc2`、`pt1/pt2` 这类常见命名

进入页面后可以：

- 查看当前媒体识别出的所有分段
- 查看每个分段旁边已存在的 sidecar 字幕
- 对当前分段手动搜索字幕候选
- 对当前分段手动选择并下载字幕
- 一键为全部分段执行“最佳匹配并下载”
- 删除某条已经保存到媒体目录的字幕

## 字幕命名规则

插件当前只负责把字幕下载到媒体目录，不再替播放器决定默认启用哪条字幕。  
为了同时满足 Jellyfin 识别规则和多字幕并存，下载后的字幕命名为：

- `视频名.清洗后的原字幕名.扩展名`

例如：

- `GHNU-31.网友上传.srt`
- `GHNU-31.官方中文字幕.srt`
- `movie-cd2.字幕组A.ass`

如果同名字幕已经存在，插件会自动追加序号，避免覆盖旧字幕，例如：

- `GHNU-31.网友上传.2.srt`

这样多个字幕文件可以并存，后续由 Jellyfin 播放器自己选择启用哪条字幕。

## 说明

- 当前版本不再依赖 Jellyfin 原生字幕搜索流程
- `Subtitles Tools` 不会出现在 Jellyfin 原生字幕提供器列表中
- 详情页的 `分段字幕` 页面是当前版本的唯一推荐入口
