# 游戏本体翻译

每个 Vintage Story 游戏版本使用独立目录，并按最终资源路径存放语言文件：

```text
<game-version>/assets/game/lang/zh-cn.json
<game-version>/assets/game/lang/en.json
```

例如，游戏版本 `1.22.3` 的简体中文翻译文件路径为：

```text
projects/game/1.22.3/assets/game/lang/zh-cn.json
```

在 `config/packer/default.json` 的 `gameTranslation` 中指定该版本后，Packer 会将它写入发布包的 `assets/game/lang/zh-cn.json`，并在 `modinfo.json` 中声明对应的 `game` 依赖。

不要将本体翻译放到 `projects/assets`。那个目录仅用于模组翻译，且会被模组元数据和版本检查流程扫描。
