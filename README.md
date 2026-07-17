# VSCN Vintage Story 汉化包

这是一个面向 Vintage Story 模组汉化的聚合语言包仓库。

仓库中的翻译资源按模组名、目标模组版本和真实 `modid` 组织，最终由本地打包器生成一个可直接放入 Vintage Story `Mods` 目录的聚合包 zip。

## 相关页面

| 名称 | 地址 |
| --- | --- |
| VSCN-Studio 网站 | <https://vscn.studio> |
| 译名标准化页面 | <https://vscn-studio.github.io/VintageStory-Chinese-Language-Package/docs/terminology/> |
| 游戏模组库 | <https://mods.vintagestory.at/vscnlangpack> |

## 环境要求

- .NET SDK 8 或更高，用于构建打包器和运行测试。
- .NET SDK 10，用于构建 `src/Updater` 中面向 Vintage Story 1.22.x API 的自动更新模组。

## 仓库结构

```text
config/packer/default.json
projects/assets/<mod-name>/<mod-version>/<modid>/lang/zh-cn.json
projects/assets/<mod-name>/<mod-version>/<modid>/lang/builtin
projects/assets/<mod-name>/<mod-version>/<modid>/lang/source
projects/assets/<mod-name>/<mod-version>/<modid>/lang/decompiled
projects/assets/index.json
projects/translation-terminology/<language>/*.json
projects/dictionaries/vs-wiki/zh-cn.dictionary.json
src/Packer
src/Updater
tests/Packer.Tests
```

其中：

- `<mod-name>` 仅用于仓库内组织，不进入最终产物。
- `<mod-version>` 表示目标模组版本，不表示游戏版本。
- `<modid>` 必须使用被汉化模组的真实 `modid`。
- `projects/assets/index.json` 用于维护模组展示元数据，键名为 `<mod-name>`。
- `projects/translation-terminology/<language>/*.json` 是按主题拆分的译名标准化术语表，用于 Weblate 术语协作。
- `projects/dictionaries/vs-wiki/zh-cn.dictionary.json` 是从 Vintage Story Wiki 抽取的来源字典，供生成或校对术语表时参考。
- `src/Updater` 是独立的客户端代码模组 `vscnlangpackupdater`，游戏内名称为“VSCN 汉化包自动更新器”，用于通过 GitHub 加速通道自动检查并下载最新版汉化包。

可选地，你也可以在同目录保留源语言文件，例如：

```text
projects/assets/<mod-name>/<mod-version>/<modid>/lang/en.json
```

打包时只会输出 `zh-cn.json`。如果某个版本不应进入语言包主包，可以在对应 `lang` 目录放置一个空的标记文件：

- `builtin`：作者内置汉化。
- `source`：基于源码翻译。
- `decompiled`：基于反编译翻译。

这三种标记在打包器中的行为完全一致：对应版本会参与版本判断，但不会被打包输出。

如果某个模组整体应按上述特殊状态处理，可以在 `projects/assets/index.json` 中将该模组的 `latestVersion` 直接写为 `builtin`、`source` 或 `decompiled`。例如：

```json
{
  "chiseltools": {
    "name": "QP's Chisel Tools",
    "translation": "QP 的凿刻工具",
    "authors": [
      "QPTech"
    ],
    "homepage": "https://mods.vintagestory.at/chiseltools",
    "latestVersion": "builtin"
  }
}
```

这种写法会让打包器和版本检查工作流完全跳过该模组。

## 新增翻译

1. 在 `projects/assets` 下创建目录：

   ```text
   projects/assets/<mod-name>/<mod-version>/<modid>/lang/
   ```

2. 放入简体中文语言文件：

   ```text
   projects/assets/<mod-name>/<mod-version>/<modid>/lang/zh-cn.json
   ```

3. 保证 `zh-cn.json` 是合法 JSON，且根节点是对象。

4. 按需在 `projects/assets/index.json` 补充展示信息：

   ```json
   {
     "betterloot": {
       "name": "Better Loot",
       "translation": "更好的战利品",
       "authors": [
         "DejFidOFF"
       ],
       "contributors": [
         {
           "name": "HansJack",
           "url": "https://vintagestory.top/u/HansJack",
           "role": "Chinese Translator"
         }
       ],
       "homepage": "https://mods.vintagestory.at/betterloot",
       "latestVersion": "2.0.3"
     }
   }
   ```

   其中 `betterloot` 对应仓库目录里的 `<mod-name>`。`authors` 是原模组作者；`contributors` 是汉化贡献者，支持贡献者名称、链接和角色。发布 Release 时会通过 `mods.vintagestory.at/api` 获取英文名、主页、作者和最新版本；`translation` 和 `contributors` 这类人工维护信息仍以 `index.json` 为准。

当同一个真实 `modid` 同时存在多个目标模组版本时，打包器会默认选择最高版本；如果版本无法比较、同一归一化版本重复，或最终输出路径发生冲突，打包会直接失败并列出冲突来源。

如果最高版本目录中存在 `lang/builtin`、`lang/source` 或 `lang/decompiled` 标记，打包器会跳过该模组，不会回退打包旧版本社区翻译。

## 译名标准化术语表

术语表网页展示入口：

```text
docs/terminology/
```

网页直接读取 `projects/translation-terminology/zh-cn/*.json` 作为数据源。

译名标准化文件位于：

```text
projects/translation-terminology/zh-cn/
```

每个分类文件都是一个普通 JSON 对象，扁平键值结构：

```json
{
  "temporal gear": "时空齿轮",
  "translocator": "传送器"
}
```

键名为英文术语，值为标准中文译名。

当前简体中文术语文件如下：

```text
overview.json                     标准化概况
blocks.json                       方块
items.json                        物品
creatures-and-entities.json       生物与实体
rocks-minerals-and-metals.json    岩石、矿物与金属
food-farming-and-cooking.json     食物、农业与烹饪
mushroom.json                     蘑菇
crafting-and-processing.json      工艺与加工
mechanical-power.json             机械动力
environment-and-worldgen.json     环境与世界生成
biomes.json                       生物群系
status-stats-and-damage.json      状态、属性与伤害
ui-commands-and-server.json       界面、命令与服务器
temporal-lore-and-story.json      时空、传说与剧情
mod-common-terms.json             模组通用术语
technical-content.json            技术性内容
other.json                        其他
```

Weblate 的文件掩码必须包含 `*` 来表示语言代码，因此每个分类组件应使用如下形式：

```text
projects/translation-terminology/*/blocks.json
projects/translation-terminology/*/items.json
projects/translation-terminology/*/temporal-lore-and-story.json
```

其中 `*` 会匹配当前的 `zh-cn` 目录。新增其他语言时，应放在同一层级，例如 `projects/translation-terminology/en/blocks.json` 或 Weblate 采用的对应语言代码目录。

Weblate 接入时建议将这些文件分别作为 JSON 组件导入，并在组件设置中启用 `Use as a glossary`。不要把 `projects/translation-terminology/*/*.json` 当作单个术语组件的文件掩码使用，因为这些文件名表示分类，不表示语言代码。

## 本地打包

在仓库根目录运行：

```powershell
dotnet run --project src/Packer -- pack --config config/packer/default.json
```

默认输出文件：

```text
build/VintageStory-Chinese-Language-Package-<version>.zip
```

产物根目录固定为：

```text
modinfo.json
assets/<真实modid>/lang/zh-cn.json
```

## 运行测试

```powershell
dotnet test
```

测试会覆盖路径扫描、版本选择、冲突处理、JSON 校验、zip 输出结构以及 CLI 参数契约。

## GitHub Actions

Release 通过 `.github/workflows/release.yml` 手动触发，只需要选择 `release_kind`。版本号会按 UTC+8 当前年月和已有标签自动生成，格式为 `YY.M.N`：例如 2026 年 7 月第一次修订为 `26.7.1`；如果当前月份已有最新标签 `v26.7.8`，下一次发布为 `26.7.9`；如果进入 8 月且没有 `v26.8.*` 标签，则从 `26.8.1` 开始。

发布说明会显示全部入包模组列表，包含模组中文名称、模组英文名称、模组 ID、模组最新版本和翻译贡献者，并生成贡献者翻译数量统计表。

Release 还会额外附带 `README.md` 文件，里面包含完整入包模组清单和贡献者链接。发布说明和 Release README 会通过 `mods.vintagestory.at/api` 获取模组站元数据，并结合 `projects/assets/index.json` 中的人工中文名和覆盖信息生成。

同一个 Release 会额外上传 `vscnlangpackupdater-<version>.zip`，用于发布自动更新客户端模组。

模组最新版本检查会生成待更新模组表和完整模组版本表。完整版本表包含“状态”列：`仓库维护` 表示仍由本仓库提供社区翻译，`作者内置`、`源码翻译`、`反编译翻译` 分别表示该版本通过 `lang/builtin`、`lang/source`、`lang/decompiled` 标记为特殊维护方式。`index.json` 中 `latestVersion` 为 `builtin`、`source` 或 `decompiled` 的模组会被版本检查直接跳过。

## 自动更新模组

`src/Updater` 提供 `vscnlangpackupdater` 客户端代码模组，游戏内名称为“VSCN 汉化包自动更新器”。它会在客户端启动后请求 GitHub Releases 的最新发布版本：

```text
https://api.github.com/repos/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest
```

默认配置会先通过 GitHub 加速通道访问 Release API 和下载地址，全部失败后再回退到原始 GitHub 地址。如果最新 Release 版本高于当前已加载的 `vscnlangpack`，它会下载对应的 `VintageStory-Chinese-Language-Package-<version>.zip` 到玩家的 `Mods` 目录，并提示玩家重启游戏生效。它不会自动打包语言包，也不依赖额外云服务器。

默认加速通道配置位于 `ModConfig/vscnlangpackupdater.json`：

```json
"releasesLatestUrl": "https://github.com/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest",
"releaseAssetDownloadUrlTemplate": "https://github.com/vscn-studio/VintageStory-Chinese-Language-Package/releases/download/{tag}/{asset}",
"githubUrlTemplates": [
  "https://ghproxy.net/{url}",
  "https://v6.gh-proxy.com/{url}",
  "https://hk.gh-proxy.com/{url}",
  "https://cdn.gh-proxy.com/{url}",
  "https://edgeone.gh-proxy.com/{url}",
  "{url}"
]
```

`{url}` 表示原始 GitHub 地址，`{escapedUrl}` 表示 URL 编码后的原始地址。玩家可以按自己的网络情况替换或增删加速源，建议保留 `{url}` 作为最后兜底通道。如果 GitHub API 无法访问，更新器会退回到 `releasesLatestUrl` 的跳转地址解析最新标签，再用 `releaseAssetDownloadUrlTemplate` 拼出语言包下载地址。默认通道优先使用 `ghproxy.net`，并参考 MVL 增加了 `v6`、`hk`、`cdn`、`edgeone` 四条 `gh-proxy.com` 子线路作为下载备用。

构建 updater 时需要将 `VINTAGE_STORY` 指向包含 `VintagestoryAPI.dll` 的游戏安装目录：

```powershell
dotnet build src/Updater/Updater.csproj -c Release
```

构建产物位于：

```text
src/Updater/bin/Release/mod/
```

## 安装到 Vintage Story

1. 运行打包命令生成 zip。
2. 将生成的 zip 放入 Vintage Story 的 `Mods` 目录。
3. 启动游戏后，语言包会以 `vscnlangpack` 这个模组 ID 被识别，并覆盖你已安装且受支持模组的对应简体中文语言文件。
4. 如需启动时自动检查 GitHub Release 最新版，可额外安装 `vscnlangpackupdater`。
