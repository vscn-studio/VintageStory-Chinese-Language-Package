文明模拟测试整合包的客户端本地化适配。仅为 Crawl & Dive 和 Progressive Bleeding 中人工审核过的界面文本调用 `Lang.GetUnformatted`，译文保存在各模组的 `zh-cn.json`。

不替换原模组程序集。配置键、生物族代码、网络动作、数值格式和武器内部标识不进入替换表。武器显示名称在界面文本拼接完成后翻译；动态伤害预览按文字片段翻译；服务器返回的状态在客户端读取后翻译。非简体中文环境返回原文。缺失语言条目时保留原文。

构建：`dotnet build src/ModpackLocalizationBridge -c Release -p:VintageStoryDir=<游戏目录>`。随专用整合包汉化包分发，不加入仓库默认资源包。Harmony 使用游戏自带版本。

程序集集成检查需要当前安装版本的两个模组 DLL，可从原模组 ZIP 解压到本地 `build` 目录后运行：

```powershell
dotnet run --project tests/ModpackLocalizationBridge.Checks -c Release -p:VintageStoryDir="<游戏目录>" -- "<游戏目录>" "<仓库目录>" "<CrawlandDive.dll 路径>" "<ProgressiveBleeding.dll 路径>"
```

检查直接读取 `projects/assets/crawlanddive` 和 `projects/assets/progressivebleeding` 的译文；各目录需保留一个待检查版本。游戏及原模组 DLL 不纳入 Git。
