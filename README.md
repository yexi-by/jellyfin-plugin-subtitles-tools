# jellyfin-plugin-subtitles-tools

`jellyfin-plugin-subtitles-tools` 是 [`subtitles-tools`](https://github.com/yexi-by/subtitles-tools) 的 Jellyfin 插件。

插件现在只处理本地 `Movie` 和 `Episode` 文件，并把流程收敛为：

- 识别并纳管当前视频到 `MKV`
- 在 `MKV` 元数据中保存插件使用的 `CID/GCID`
- 调用 `subtitles-tools` Python 服务搜索迅雷字幕
- 把字幕统一转成 UTF-8 `SRT`
- 软字幕内封进当前 `MKV`
- 删除临时字幕文件，不再保留外挂字幕

插件仓库清单地址：
<https://raw.githubusercontent.com/yexi-by/jellyfin-plugin-subtitles-tools/main/manifest/stable.json>

## 支持范围

- 仅支持 Jellyfin `10.11.6`
- 仅支持本地可直接读取的 `Movie` 和 `Episode`
- 不支持 `.strm`
- 仅对接 [`subtitles-tools`](https://github.com/yexi-by/subtitles-tools) Python 服务
- 仅支持能稳定转成文字型 `SRT` 的字幕；图片字幕 / OCR 不在当前范围内

## 核心能力

- 新视频入库后可自动纳管为 `MKV`
- 已是 `MKV` 但没有插件元数据的视频，也可在首次操作时自动补算并写入 `CID/GCID`
- 手动把当前分段或整组分段转换为 `MKV`
- 对当前分段搜索迅雷字幕
- 下载字幕后自动转成临时 `SRT`，再内封到当前 `MKV`
- 一键为整组分段执行“最佳匹配并内封”
- 删除或替换插件写入的内封字幕流

## 安装方式

1. 先部署服务端项目：<https://github.com/yexi-by/subtitles-tools>
2. 在 Jellyfin 后台“插件仓库”中添加上面的 `manifest` 地址
3. 安装 `Subtitles Tools`
4. 在插件配置页填写 Python 服务地址，例如 `http://127.0.0.1:8055`
5. 如有需要，填写 `FFmpeg` 可执行文件路径
6. 使用“测试连接”确认 Python 服务和 `FFmpeg` 都可用

## 插件配置

当前配置页包括：

- `Python 服务地址`
- `请求超时（秒）`
- `新视频入库后自动纳管为 MKV`
- `视频转换并发数`
- `FFmpeg 可执行文件路径（可选）`

默认行为：

- 自动纳管为 `MKV`：开启
- 转换并发：`1`

自动纳管开启后：

- 新入库文件若已是 `MKV`，插件会补算 `CID/GCID` 并写入 `MKV` 元数据
- 新入库文件若不是 `MKV`，插件会先计算 `CID/GCID`，再转成 `MKV`，并把这些值写入新文件的 `MKV` 元数据
- 后续搜索字幕时，插件优先读取当前 `MKV` 元数据中的 `CID/GCID`

## 使用方式

安装并配置完成后，在电影或剧集详情页会出现插件自带的 `内封字幕` 入口按钮。

进入页面后可以：

- 查看当前媒体识别出的所有分段
- 查看每个分段当前是否已纳管、当前容器格式以及已内封字幕流
- 手动把当前分段转换为 `MKV`
- 一键把整组分段转换为 `MKV`
- 对当前分段搜索字幕候选
- 下载并内封到当前分段
- 一键为整组分段执行“最佳匹配并内封”
- 删除插件写入的内封字幕流

## 转换与内封规则

- 目标容器固定为 `MKV`
- 转换时优先尝试 `remux`
- 若 `remux` 失败，则自动回退到转码流程后再输出 `MKV`
- 字幕下载后会先转换成临时 UTF-8 `SRT`
- 临时字幕文件名按当前分段视频名生成，例如：
  - `GIGP-123.srt`
  - `GIGP-54-cd1.srt`
- 字幕成功内封后，临时 `.srt` 会立即删除
- 插件不再保留任何外挂字幕文件

## 说明

- 插件不再依赖 Jellyfin 原生字幕搜索提供器
- `Subtitles Tools` 不会出现在 Jellyfin 原生字幕提供器列表中
- 详情页的 `内封字幕` 页面是当前版本的唯一推荐入口
- 插件现在只认当前视频自身的 `MKV` 元数据，不再依赖旧版外部哈希归档文件
- 启动时会自动清理插件数据目录里的旧归档、旧缓存和残留临时文件
