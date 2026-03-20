# jellyfin-plugin-subtitles-tools

`jellyfin-plugin-subtitles-tools` 是 [`subtitles-tools`](https://github.com/yexi-by/subtitles-tools) 的 Jellyfin 插件。  
它负责在 Jellyfin 中为本地视频保存原始 `CID/GCID`，调用 Python 服务搜索迅雷字幕，并把字幕统一转成 `SRT` 后软封装进 `MKV` 容器。

插件仓库清单地址：
<https://raw.githubusercontent.com/yexi-by/jellyfin-plugin-subtitles-tools/main/manifest/stable.json>

## 支持范围

- 仅支持 Jellyfin `10.11.6`
- 仅支持 `Movie` 和 `Episode`
- 仅支持本地可直接读取的视频文件
- 不支持 `.strm`
- 当前只对接 [`subtitles-tools`](https://github.com/yexi-by/subtitles-tools) Python 服务端
- 当前只支持可稳定转成文字型 `SRT` 的字幕；图片字幕 / OCR 不在本期范围内

## 核心能力

- 新视频入库后可自动计算原始 `CID/GCID`
- 新视频入库后可自动转换为 `MKV`
- 手动把当前分段或整组分段转换为 `MKV`
- 对当前分段搜索迅雷字幕
- 下载字幕后自动转成临时 `SRT`，再软封装进 `MKV`
- 一键为整组分段执行“最佳匹配并内封”
- 删除或替换插件写入的内封字幕流

## 安装方式

1. 先部署服务端项目：<https://github.com/yexi-by/subtitles-tools>
2. 在 Jellyfin 后台的“插件仓库”中添加上面的 `manifest` 地址
3. 安装 `Subtitles Tools`
4. 在插件配置页填写 Python 服务地址，例如 `http://127.0.0.1:8055`
5. 使用“测试连接”确认 Python 服务和 FFmpeg 都可用

## 插件配置

当前配置页包括：

- `Python 服务地址`
- `请求超时（秒）`
- `新视频入库后自动计算原始 CID/GCID`
- `哈希后台并发数`
- `新视频入库后自动转换为 MKV`
- `视频转换并发数`
- `FFmpeg 可执行文件路径（可选）`

默认行为：

- 自动计算原始 `CID/GCID`：开启
- 自动转换为 `MKV`：开启
- 哈希并发：`1`
- 转换并发：`1`

当自动转换开启时，插件会先确保旧 `CID/GCID` 已被持久化，再执行 `MKV` 转换。  
后续即使文件已经改成 `MKV`，字幕检索仍然优先使用原始哈希，而不是转换后新文件的 `GCID`。

## 使用方式

安装并配置完成后，在电影或剧集详情页会出现插件自带的 `内封字幕` 入口按钮。

进入页面后可以：

- 查看当前媒体识别出的所有分段
- 查看每个分段当前容器格式和原始哈希归档状态
- 手动把当前分段转换为 `MKV`
- 一键把整组分段转换为 `MKV`
- 对当前分段搜索字幕候选
- 下载并内封到当前分段
- 一键为整组分段执行“最佳匹配并内封”
- 删除插件写入的内封字幕流

## 转换与内封规则

- 视频目标容器固定为 `MKV`
- 转换时优先尝试 `remux`
- 若 `remux` 失败，则自动回退到转码流程后再输出 `MKV`
- 字幕下载后会先转换成临时 UTF-8 `SRT`
- 临时字幕文件名按当前分段视频名生成，例如：
  - `GIGP-123.srt`
  - `GIGP-54-cd1.srt`
- 字幕成功内封后，临时 `.srt` 会立刻删除
- 插件不再保留任何外挂字幕文件

## 说明

- 插件现在不再依赖 Jellyfin 原生字幕搜索提供器
- `Subtitles Tools` 不会出现在 Jellyfin 原生字幕提供器列表中
- 详情页的 `内封字幕` 页面是当前版本的唯一推荐入口
- 当前版本不提供“恢复原始旧视频文件”的能力；若要撤销，仅支持删除插件写入的内封字幕流
