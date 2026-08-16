# VSCN 汉化包自动更新器

`vscnlangpackupdater` 是一个客户端代码模组，用于在游戏启动后自动检查并下载最新版 VSCN Vintage Story 汉化包。

启动后它会请求最新 Release 信息：

```text
https://api.github.com/repos/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest
```

默认会先通过 GitHub 加速通道访问 Release API 和下载地址，所有加速通道失败后再回退到原始 GitHub 地址。如果最新 Release 版本高于当前已加载的 `vscnlangpack` 版本，它会把匹配的 `VintageStory-Chinese-Language-Package-<version>.zip` 下载到玩家的 `Mods` 目录，并提示玩家重启游戏生效。

## 构建

将 `VINTAGE_STORY` 设置为包含 `VintagestoryAPI.dll` 的 Vintage Story 安装目录，然后运行。面向 Vintage Story 1.22.x 的代码模组目前需要 .NET 10 SDK。

```powershell
dotnet build src/Updater/Updater.csproj -c Release
```

模组文件会复制到：

```text
src/Updater/bin/Release/mod/
```

发布自动更新器时，将该 `mod` 目录中的内容打包为 zip。

## 配置

模组首次启动时会写入 `ModConfig/vscnlangpackupdater.json`。

```json
{
  "enabled": true,
  "checkDelayMilliseconds": 3000,
  "httpTimeoutSeconds": 30,
  "notifyWhenUpToDate": false,
  "notifyOnFailure": true,
  "deleteOldPackages": true,
  "releasesApiUrl": "https://api.github.com/repos/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest",
  "releasesLatestUrl": "https://github.com/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest",
  "releaseAssetDownloadUrlTemplate": "https://github.com/vscn-studio/VintageStory-Chinese-Language-Package/releases/download/{tag}/{asset}",
  "githubUrlTemplates": [
    "https://ghproxy.net/{url}",
    "https://gh-proxy.org/{url}",
    "https://v4.gh-proxy.org/{url}",
    "https://v6.gh-proxy.org/{url}",
    "https://axisnow.gh-proxy.org/{url}",
    "https://v6.gh-proxy.com/{url}",
    "https://hk.gh-proxy.com/{url}",
    "https://cdn.gh-proxy.com/{url}",
    "https://edgeone.gh-proxy.com/{url}",
    "{url}"
  ],
  "assetFilePrefix": "VintageStory-Chinese-Language-Package-",
  "assetFileSuffix": ".zip"
}
```

`githubUrlTemplates` 同时用于 Release API、最新版跳转页和语言包 zip 下载。`{url}` 会替换为原始 GitHub 地址，`{escapedUrl}` 会替换为 URL 编码后的原始地址；如果某个模板不包含占位符，会直接把原始地址拼到模板末尾。候选地址和重定向仅允许 HTTPS 及受信任的 GitHub/CDN/代理主机。建议保留 `{url}` 作为最后兜底通道；未知主机会被安全检查拒绝。如果 GitHub API 超时或返回错误，更新器会依次请求 `https://github.com/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest` 和 `https://vscn.studio/VintageStory-Chinese-Language-Package/releases/latest`；两者都支持 Release JSON 或页面跳转解析，随后用 `releaseAssetDownloadUrlTemplate` 拼出语言包下载地址。下载会限制响应大小；API 提供 digest 时会校验 SHA-256，并在安装前检查 ZIP 路径、条目数量、解压大小、modid 和版本。
