# VSCN Vintage Story 汉化包

这是一个面向 Vintage Story 游戏本体与模组汉化的聚合语言包仓库。

模组翻译资源按模组名、目标模组版本和真实 `modid` 组织；游戏本体翻译按游戏版本独立保存。最终由本地打包器生成一个可直接放入 Vintage Story `Mods` 目录的聚合包 zip。

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
projects/assets/<mod-name>/<mod-version>/<modid>/README.txt
projects/game/index.json
projects/game/<game-version>/assets/game/lang/zh-cn.json
projects/game/<game-version>/assets/game/lang/en.json
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
- 每个汉化目录的 `README.txt` 只需一行汉化作者，与 `lang` 文件夹同级。
- `projects/game/index.json` 用于维护游戏本体翻译的版本化贡献者信息，键名为 `<game-version>`。
- `projects/game` 存放游戏本体翻译；它与模组翻译分开管理，不参与模组数量统计。
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

4. 在 `lang` 同级新建 UTF-8 编码的 `README.txt`，只写一行汉化作者，不需要字段名或其他信息。例如：

   ```text
   projects/assets/hideracks/1.0.1/hideracks/README.txt
   ```

   内容：

   ```text
   HansJack
   ```

   多名汉化作者可在同一行用 `、` 分隔，例如 `Alice、Bob`。读取第一行非空内容，忽略首尾空白和重复名字。署名按实际选中的版本和资源目录读取，并随打包记录保存。文件缺失或为空时仍可打包，汉化作者显示“未记录”。

   请按实际译文来源填写汉化作者，不要直接使用模组作者或聚合汉化包作者。引用 BetterChinese 译文时，可参考其对应模组目录中版本 `.txt` 文件的“汉化人员”记录，并保留本仓库已有的翻译贡献者；其他版本需核对译文来源，无法确认时留空。

   模组 ID 和目标版本从目录获取，其他信息无需填写。打包和生成发布说明均不读取模组索引、不请求模组站 API。`README.txt` 本身不作为游戏资源打包，作者名单保存在 ZIP 的 `assets/vscnlangpack/config/package-record.json` 中。

当同一个真实 `modid` 同时存在多个目标模组版本时，打包器会默认选择最高版本；如果版本无法比较、同一归一化版本重复，或最终输出路径发生冲突，打包会直接失败并列出冲突来源。

如果最高版本目录中存在 `lang/builtin`、`lang/source` 或 `lang/decompiled` 标记，打包器会跳过该模组，不会回退打包旧版本社区翻译。

## 游戏本体翻译

游戏本体翻译使用独立目录，并与最终包中的资源路径保持一致：

```text
projects/game/<game-version>/assets/game/lang/zh-cn.json
```

可选地，在同一目录中保留官方英文源文件：

```text
projects/game/<game-version>/assets/game/lang/en.json
```

`en.json` 不会进入语言包，用于版本比对和翻译校对。`zh-cn.json` 应为该游戏版本的完整翻译快照；更新游戏时新增版本目录，不要覆盖已发布版本的源文件。

在 `projects/game/index.json` 中按游戏版本登记本体翻译贡献者：

```json
{
  "1.22.3": {
    "contributors": [
      {
        "name": "HansJack",
        "url": "https://vintagestory.top/u/HansJack",
        "role": "游戏本体翻译"
      }
    ]
  }
}
```

`contributors` 的 `name` 必填，`url` 和 `role` 可选。该文件仅用于游戏本体，独立于模组目录中的 `README.txt`。发布时会按 `targetVersion` 读取本体贡献者，并写入 Release 说明、Release README 和贡献者统计。

默认打包配置已包含 `1.23.0-pre.1` 本体翻译。可在 `config/packer/default.json` 的 `gameTranslation` 中选择版本，设为 `null` 则只打包模组翻译：

```json
"gameTranslation": {
  "contentRoot": "projects/game",
  "targetVersion": "1.23.0-pre.1"
}
```

`targetVersion` 是精确选择，不会自动改用目录中的更高版本。打包器会读取：

```text
projects/game/1.23.0-pre.1/assets/game/lang/zh-cn.json
```

并输出为：

```text
assets/game/lang/zh-cn.json
```

同时，生成的 `modinfo.json` 会声明 `game: 1.23.0-pre.1` 依赖。本体翻译不计入模组翻译数量，在发布变更表中单独标记为“游戏本体”。`assets/game/lang/zh-cn.json` 由本体翻译独占；若某个模组项目也试图输出该路径，打包会失败并指出冲突来源。

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
assets/vscnlangpack/config/package-record.json  （本次实际入包记录）
assets/game/lang/zh-cn.json             （启用游戏本体翻译时）
assets/<真实modid>/lang/zh-cn.json
```

打包记录包含每份入包译文的资源路径、目标版本、SHA-256 和汉化作者，与译文一起写入 ZIP。保留不同版本的 ZIP，即可比较前后打包结果：

```powershell
dotnet run --project src/Packer -- pack --config config/packer/default.json --package-version 26.11.2
dotnet run --project src/Packer -- describe-release --config config/packer/default.json --package-version 26.11.2 --release-kind release --previous-package build/VintageStory-Chinese-Language-Package-26.11.1.zip
```

`describe-release` 读取已经生成的当前版本 ZIP，不会重新扫描源文件。首次打包时省略 `--previous-package`，全部内容记为新增；指定的旧包不存在、损坏或记录格式不支持时会报错，不会按首次发布继续。旧 ZIP 没有打包记录时，按其中译文的实际内容比较，无法恢复的旧模组版本和汉化署名会注明未知。

变更表仅列新增、更新和移除项；目标版本、译文内容或汉化署名改变均算更新，未变更项目不列出。游戏本体翻译也参与比较。没有变化时只显示“本次没有翻译变更”。`describe-package` 仍可用于本地查看当前目录的完整打包预览，不用于 Release 表格。

## 运行测试

```powershell
dotnet test
python -m unittest discover -s tests/ReleaseWorkflow
```

测试会覆盖路径扫描、版本选择、冲突处理、JSON 校验、zip 输出结构以及 CLI 参数契约。

## GitHub Actions

Release 通过 `.github/workflows/release.yml` 手动触发。`game_version` 默认选择 `1.23.0-pre.1`，`include_game_translation` 默认勾选，将该版本的本体翻译加入语言包；取消勾选可只发布模组翻译。`updater_api_version` 独立指定更新器的构建 API，默认保留 `1.22.3`，不影响汉化包的目标版本。1.23.0-pre.1 的公开服务端 API 下载地址当前不可用，更新器暂沿用已有稳定版构建基准；API 版本含预发布后缀时会从 `unstable` 目录下载，否则从 `stable` 下载。版本号会按 UTC+8 当前年月和已有标签自动生成，格式为 `YY.M.N`：例如 2026 年 7 月第一次修订为 `26.7.1`；如果当前月份已有最新标签 `v26.7.8`，下一次发布为 `26.7.9`；如果进入 8 月且没有 `v26.8.*` 标签，则从 `26.8.1` 开始。

发布时会下载最近一次已发布的语言包 ZIP（包括预发布，按发布时间选择，跳过草稿），与本次实际打包记录对比，生成仅包含新增、更新和移除翻译的 Release 表格。第一次发布没有旧包时全部列为新增。下载或 API 查询失败会停止工作流，避免误生成全量变更。

ZIP 内的 `README.md` 使用同一份变更说明。Release 不再生成全量模组清单，也不查询模组站最新版本。模组作者、中文名和主页不再需要手工维护。

同一个 Release 会额外上传 `vscnlangpackupdater-<version>.zip`，用于发布自动更新客户端模组。

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
  "https://gh-proxy.org/{url}",
  "https://v4.gh-proxy.org/{url}",
  "https://v6.gh-proxy.org/{url}",
  "https://axisnow.gh-proxy.org/{url}",
  "https://v6.gh-proxy.com/{url}",
  "https://hk.gh-proxy.com/{url}",
  "https://cdn.gh-proxy.com/{url}",
  "https://edgeone.gh-proxy.com/{url}",
  "{url}"
]
```

`{url}` 表示原始 GitHub 地址，`{escapedUrl}` 表示 URL 编码后的原始地址。候选地址仅接受 HTTPS，重定向也必须继续指向受信任的 GitHub、GitHub CDN 或已知代理主机。玩家可以按自己的网络情况调整或移除已列出的加速源，建议保留 `{url}` 作为最后兜底通道；未知主机会被安全检查拒绝。如果 GitHub API 超时或返回错误，更新器会依次请求 `https://github.com/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest` 和 `https://vscn.studio/VintageStory-Chinese-Language-Package/releases/latest`；两者都支持 Release JSON 或页面跳转解析，随后用 `releaseAssetDownloadUrlTemplate` 拼出语言包下载地址。默认通道包含 `ghproxy.net`、`gh-proxy.org`、`v4.gh-proxy.org`、`v6.gh-proxy.org`、`axisnow.gh-proxy.org` 以及现有的 `gh-proxy.com` 子线路。下载结果还会限制大小、校验 Release 提供的 SHA-256（如果有），并在安装前检查 ZIP 路径、条目数量、解压大小、modid 和版本。

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
