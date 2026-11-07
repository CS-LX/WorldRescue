# 世界修复助手 / World Rescue

独立的 Survivalcraft 2.4 模组，面向 API 1.9.3.1（开发时也对照了 1.9.4 宿主加载流程）。只依赖游戏 API，无其他模组依赖；整个目录可独立复制、构建、打包。

[下载 v0.2.3 模组包](https://github.com/CS-LX/WorldRescue/releases/download/v0.2.3/WorldRescue-0.2.3.scmod) · [发布说明](https://github.com/CS-LX/WorldRescue/releases/tag/v0.2.3) · [问题反馈](https://github.com/CS-LX/WorldRescue/issues)

## 玩家使用

1. 从 Releases 下载 `WorldRescue-0.2.3.scmod` 并安装，重启游戏。保留世界原本需要的模组。
2. 正常点击进入世界。没有重叠时直接继续加载；检测到重叠时打开修复界面。
3. 左侧显示存档中的实际周围方块：环境半透明，冲突位置不透明并带红框。拖动旋转；下方图标依次为缩小、放大、旋转、复位、显示周围。鼠标悬停或按住时显示操作说明，右上角问号提供完整帮助。
4. 右侧点开记录页签比较内容，再点“保留记录”。下划线表示正在查看，勾号表示选择保留，浏览不会自动选择。物品以可点开的图标格子显示。其余记录的内容**不会合并**。多处冲突使用左右箭头逐处检查。
5. “选择相同记录”仅处理内容完全相同的记录，不会替你猜测不同内容哪份正确。选择和取消都不写入世界。
6. 点击“备份并修复”并确认。成功后可以进入世界，也可立即“撤销修复”。

左侧展示的是**现场地形**，右侧展示的是**各份保存内容**；同一位置的不同实体记录未必有不同的外观。内置展示只识别宿主物品存储约定，名称和模型来自已安装游戏内容。其他内容由可选适配提供说明；没有适配时显示“内容尚无说明”，不将其视为零或空白。未说明的内容仍随所选记录完整保留。模型绘制失败时显示线框，地形读取失败时明确说明只显示位置。

界面使用原版字体、文字按钮和物品格子，以及统一的 Remix Icon 线性图标。纯图标按钮没有倒角底框，点击区域至少为 48 像素；按下变色，所查看记录有下划线。未修改原版或其他模组的控件。

弹窗自身按屏幕中心对齐。物品图标与现场预览在各自的裁切区内重置深度，避免加载界面保留的旧帧深度在弹出动画、滚动和切换记录后遮掉模型；不清空全屏深度，也不关闭模型内部的深度测试。现场目标在半透明环境之后独立重置深度绘制，仍保持清晰可见。

模组列表图标位于 `Mod/icon.png`：抽象错位立方体、红色重叠框与绿色修复勾号，采用平滑斜线和较细描边，并向左上调整视觉重心；透明背景，不使用游戏方块纹理。通过内置 image_gen 生成，最终提示词保存在 `Art/ModIcon.prompt.txt`。可用 `Preview/Run.ps1 -HostBin '游戏宿主输出目录' -IconOnly` 检查原版模组列表中的 64 像素显示效果。

## 面向所有模组的展示接口

修复核心只处理整份记录的保留与删除，不推测任意自定义字段的含义，也不根据展示结果自动选择。内置物品适配通过宿主模板的实际组件类型确认 `ComponentInventoryBase`，再读取其 `Slots` 存储格式；名称相似的任意字段不会被猜成物品。缺少、损坏或重复的物品槽信息不会被显示成空容器。

第三方可通过 `RecordPresenters.Default.Register(id, provider)` 添加说明，独立提供本地化名称、图标、物品格子、任意数值和空状态文案。例如修行类模组可以显示灵气和点数，其他玩法可以展示各自的内容，所有展示使用相同布局。

- 每次调用收到独立的 `ComponentSnapshot.Data` 副本，包含模板名、组件名和语言。不得加载或修改玩家世界；回调应快速完成。
- 返回 `null` 表示不负责该内容；不要用缺失值冒充零。返回 `ComponentDescription` 表示提供一个或多个 `DetailSection`。
- `ExplainedChildren` 只列出真正解释的当前组件直接子节点的 `Name`。没列出的内容继续显示“尚无说明”，可由其他适配补充。
- `RecordFact` 接收玩家可理解的名称、格式化值、图标路径；内部字段名只用于读取，不直接展示。图标应为普通透明 PNG，运行时按非预乘方式混合。
- `DetailSection.EmptyText` 由提供方定义。通用界面不会把任意空分区解释成“没有物品”。
- 回调异常被隔离，原始记录不受影响；不接受无分区的“已解释”声明。重复注册 ID 会报错，不覆盖已有适配，可用 `Unregister` 注销。

推荐将适配放在单独的可选插件中，由插件依赖目标模组和 World Rescue；本模组本身不依赖任何玩法模组。示例位于 `Preview/Program.cs` 的 `RenderExtensionExamples`，仅在演示程序中注册，**不会打进发布模组**。该演示同时渲染适配前“尚无说明”和适配后“灵气／点数”，验证两条路径。

## 备份与范围

只修改 `Project.xml` 的冲突实体列表，保留所选实体的全部未知字段，地形文件和 `Project.bak` 保持原样。

修复前在该世界目录的 `WorldRescueBackups/<时间-唯一编号>/` 保存原始 `Project.xml`、已有的 `Project.bak` 和 `repair.json` 记录。备份成功并校验后，才用同目录临时文件替换主文件。若原文件在检查后发生变化则停止。立即撤销也检查世界未被再次修改。

需要日后手动恢复时，**先退出该世界并另备份当前进度**，再将对应备份中的 `Project.xml` 复制回世界目录。回退会同时回退修复之后的进度。备份是世界状态文件的备份，并非完整世界导出包；地形从未被本工具修改。

当前针对静态 `BlockEntity` 同坐标重复导致的加载失败。移动方块集、负高度占位记录不视为此类冲突。发现已知实体引用指向待删除记录时会停止，避免猜测引用的含义。第三方模组自定义的隐式引用无法全面识别，因此不自动合并数据。

首版预览支持 2.3/2.4 的 `Regions/Region *.dat`（RGN1）地形。它不修复缺失模组、损坏地形、无效实体模板、任意 XML 损坏或模组持续制造重复实体的根因。安装修复助手不替代原模组的错误修复。直接调用 `GameManager.LoadProject`、自定义加载 Screen 或联机服务器的第三方流程可能绕过本模组的标准加载入口。

## 开发与验证

需要 .NET 10 SDK。以下命令在仓库根目录执行；构建输出位于 `dist/`。

```powershell
dotnet test Tests/WorldRescue.Tests.csproj -c Test
./Pack.ps1
./Preview/Run.ps1 -HostBin '游戏宿主输出目录' -WorldArchive '待验证世界.scworld'
```

`Core/` 是不依赖游戏的扫描、决策、事务及只读地形解码；`Mod/` 是加载拦截和新建界面；`Tests/` 是行为测试；`Preview/` 使用实际游戏控件与渲染器进行隔离验证。不会自动部署或修改玩家安装目录。

测试覆盖多重冲突、未知状态保留、移动方块排除、完整选择、引用检查、原始备份、并发修改保护、撤销、备份失败、宿主实体索引重载、地形解码、展示适配、异常隔离和未知内容提示。预览验证中文/英文、1024×768/900×600、按钮文字、可视区、只查看不选择、选择操作、环境切换，以及加载拦截、确认返回、修复和立即撤销。提供真实世界时仅在输出目录的新副本上运行修复/撤销，并读取实际周围地形。

`Preview/DialogRenderingChecks.cs` 通过真实 `DialogsManager` 播放弹出动画，并比较连续帧、滚动、切换记录与旋转现场后的 GPU 输出是否与干净深度的输出一致；检查 1024×768 和带实际 UI 缩放的 1920×1080 居中位置，以及绘图区外的深度未受影响。预览入口不再额外替弹窗设置居中属性。

图标使用普通透明 PNG、`NonPremultiplied` 混合、线性采样和连续透明度；不使用裁切抗锯齿的 alpha-test。转换时将透明像素的 RGB 延展为白色，保留覆盖率 alpha，避免缩放采样引入暗边。预览程序会读取 GPU 实际输出，检查 16/24/32/48 像素、深浅背景、正常与半透明图标的暗边、亮边和抗锯齿，而非只检查控件属性。

`Art/RemixIcon/` 保存原始 SVG；`Mod/Assets/Textures/WorldRescue/Remix/` 保存已生成 PNG；`Tools/BuildIcons.cjs` 可重建。日常构建和游戏运行不需要 Node，也不需要下载图标。授权与转换说明随包附于 `ThirdParty/RemixIcon/`。

`Playtest/prepare_worlds.py` 可在指定的现有游戏目录中新建 1 处、32 处、256 处冲突的原版方块测试世界，并导出 `.scworld`，不会覆盖已有同名世界；使用 `--help` 查看参数。测试世界、玩家存档、游戏文件和本机验证缓存不包含在源码仓库中。

预览是实际引擎绘制与受控交互验证，不等同于完整玩家世界进游戏验收；Android 触屏、复杂第三方方块、快照恢复和与其他加载拦截模组共存仍需实机回归。API 的传递依赖 ImageSharp 3.1.12 目前产生 NuGet 安全公告警告，未擅自替换游戏宿主程序集；发布时应配合宿主升级验证。

## 许可证

本项目采用 [MIT 许可证](LICENSE)，包括 v0.2.3 版本。版权归属为 `Copyright (c) 2026 Linxium`。

第三方 Remix Icon v4.6.0 图标保留原有 [Apache License 2.0](Mod/ThirdParty/RemixIcon/LICENSE.txt) 授权及 [NOTICE](Mod/ThirdParty/RemixIcon/NOTICE.txt)，不适用本项目的 MIT 许可证。

## 参考

- 宿主 `BuildFurnitureDialog` / `FurnitureDesignWidget`：旋转观察与三维预览坐标变换；未修改这些控件。
- 宿主 `ChestWidget` / `InventorySlotWidget`：物品格子、计数与默认字体。
- [Kaalus：New inventory layout](https://kaalus.wordpress.com/2016/03/09/new-inventory-layout/) 与 [New creative inventory](https://kaalus.wordpress.com/2015/03/18/new-creative-inventory/)：紧凑工具区、选择入口和触屏空间安排。
- [Remix Icon v4.6.0](https://github.com/Remix-Design/RemixIcon/tree/v4.6.0)：统一线性图标，遵循该版本的 Apache 2.0 授权。
- RecipaediaEX 的 Overlay 图标操作条：参考无底框按钮和非预乘混合方式；没有复制其控件，也没有运行时依赖。
- [MCA Selector](https://github.com/Querz/mcaselector)：可视化检查与选择操作。
- [Minecraft Region Fixer](https://github.com/Fenixin/Minecraft-Region-Fixer)：检测问题、保留备份后修复。

除注明授权的 Remix Icon 图标素材外，参考仅用于交互与流程设计，没有复制这些项目的实现代码。
