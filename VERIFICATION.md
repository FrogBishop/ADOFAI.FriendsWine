# 验证记录

## 1.0.3：星球及表情纹理裁切（2026-11-08）

在独立游戏副本中运行测试，未关闭用户原游戏。环境为 Unity 6000.3.25f1 / UMM 0.33.3.0，使用实际 GPU 渲染和像素读回。

- 根因：Unity 的 `Sprite.SetVertexAttribute(Position, ...)` 会重新计算精灵 UV。原实现只写位置，导致纹理取样范围跟着网格变化，出现顶部裁切和两侧重复。现改为缓存原图 UV，并在每次写入位置之后恢复 `TexCoord0`；切换 Sprite 时更换缓存，销毁效果时释放缓存。
- `artifacts/sprite-texture-baseline/`：四张真实游戏表情贴图 × 横向/纵向/旋转翻转，共 12 组旧路径全部复现像素错误，UV 最大偏差约 0.19～0.36。诊断中显式恢复 UV 后，同一批画面全部与完整图像缩放参照一致。
- `artifacts/sprite-texture-verified/`：24 组画面、122 项断言全部通过。四张真实表情分别覆盖原状、横向、纵向、旋转翻转和重复横向，复用同一效果对象以覆盖连续帧及切换贴图；另含偏心图集子区域和真实 `ADOFAI/Frames` 星球材质的横纵变形。
- 像素参照使用原始 Sprite 的完整 Transform 缩放，与模组修改顶点的实现独立。24 组全部为 `mismatchedPixels=0`、`meanChannelError=0`；并断言画面有可见像素、原 UV 保持一致、源 Sprite/Transform 恢复及共享资产未修改。
- 早期 `sprite-texture-fixed` 的 4 项位置差异来自测试读到切换 Sprite 前的缓存 Renderer 边界。参照改用未修改的源 Sprite 边界后通过；未通过放宽误差阈值来消除差异。
- `artifacts/sprite-fix-culling/`：17 项通过。一万个离屏/可见对象加一个边缘对象，仍只处理和创建 9 份资源，跳过 9992 份；原生可见性回调、屏幕边缘、多摄像机与恢复均正常。
- `artifacts/sprite-fix-track-verified/`：41 项轨道出现/消失/透明度检查通过。隐藏测试窗口会跳过自动绘制，因此验证组件改为每帧显式调用实际相机渲染，再检查游戏的原生更新；没有模拟可见性回调或放宽断言。此前两轮轨道测试中的 4 项失败来自没有逐帧绘制的测试环境。
- `artifacts/sprite-fix-regression-verified/`：28 项基础回归通过，12 个实际渲染帧验证旋转/翻转/嵌套原点和文字缩放；BGM、暂停、启停、循环、原生场景重开、配置及恢复均通过。该验证同样显式绘制相机，并安排在运行时 UI 更新后执行。68 项核心检查通过。
- 条件编译入口：`build.ps1 -IntegrationTest` 后带 `--friendswine-sprite-texture-test --friendswine-test-output <目录>` 启动游戏，测试完成自动退出。正式 DLL 不包含该入口。

## 1.0.2：视野裁剪与原渲染器生命周期

环境：2026-11-01，ADOFAI r150（e0229c0，2026/10/30 构建），Unity 6000.3.25f1，UMM 0.33.3.0，Windows 10。

- `artifacts/track-animations-final/`：41 项通过。实际调用游戏的 Extend、Grow、Grow_Spin、Fade、Drop、Rise、Assemble、Assemble_Far 出现动画，以及 Retract、Scatter、Scatter_Far、Shrink、Shrink_Spin、Fade 消失动画；在开始、中间、结束比对关闭模组时的像素、位置、缩放、旋转、opacity 和材质 alpha，全部一致。用于精确对照的这部分设为单位拉伸，另外 17 项透明度测试使用正常拉伸倍数。
- `artifacts/native-final-regression/`：原有音乐/启停/重开/原点检查通过，增加了偏心 Sprite 原点、X/Y 翻转，以及原生世界包围盒必须包含实际变形顶点的检查。
- `artifacts/culling-final/`：一万个测试对象加一个边缘对象时，只对 9 个对象创建和应用变形资源。验证了拉伸后才进入屏幕的边缘像素、共享源 Sprite 不被修改、移动进出视野、原 Renderer 的 isVisible/OnWillRenderObject、渲染回调里的隐藏、多摄像机切换及透视裁剪。
- 新路径保留原渲染器，并且不再用旧 scrFloor 可见性桥接替代游戏自身的渲染生命周期。旧路径只在条件测试构建中保留用于对照，正式构建不包含它。
- 最终同场景对照：`artifacts/culling-legacy-comparison/` 旧路径处理 10001 个、分配 10001 份，暖态中位 196.058ms；`artifacts/culling-release-verified/` 新路径处理 9 个、分配 9 份、裁剪 9992 个，暖态中位 6.954ms，17 项全部通过。两个版本均保留相同测试场景与相机；结果是模组相机应用阶段的计时。
- 旧路径可复现原 Renderer 不收到 OnWillRenderObject/isVisible，以及渲染回调中隐藏对象仍然可见；新路径同样的检查均通过。旧路径的裁剪相关失败是预期对照，不是新版本遗留错误。
- 大场景仍有一次轻量的包围范围遍历；完全离屏对象不创建副本/网格、不计算拉伸顶点、不复制材质。测试中的处理耗时是模组自身的相机应用阶段，不等于整个游戏帧率。

早期 `native-rendering-initial` 使用的蒙皮缓冲不适用于普通无骨骼精灵，已改为独立 Sprite 顶点数据。早期轨道动画对照夹具未重置 tweenRot，已修正后重跑；这些早期失败不代表最终结果。

## 1.0.1：隐形砖块修复

通过原生 scrController.EnterLevel 进入真实 1-1 关卡，选择实际 scrFloor、材质与网格，用独立摄像机渲染到纹理并读取像素。

- 对照旧路径：5 项失败。开启效果后 scrFloor 被停用，opacity=0 时材质 alpha 仍为 1，仍有 11890 个非背景像素。
- 修复路径：17 项检查全部通过。opacity=0 时材质 alpha=0，实际砖块像素为 0；opacity=0.25 时材质 alpha=0.25。
- 检查了渐隐后恢复、移出/移入视野、停用与重新启用、Renderer.enabled=false、forceRenderingOff=true，以及最终恢复正常渲染。
- 已查看全透明、半透明与旧版本对照截图。
- 证据目录：`artifacts/visibility-baseline-gameplay/`（旧路径）与 `artifacts/visibility-fixed/`（修复后）。最早的 `artifacts/visibility-baseline/` 未正确加载关卡，不用于结论。
- 原有 27 项游戏内回归检查也全部通过（`artifacts/visibility-regression/`），12 个实际渲染帧通过；可见性桥接异常、模组错误和失效 Tween 警告均为 0。68 项核心检查继续通过。

以下是 1.0.0 的原始完整验证记录；1.0.1 的既有功能回归结果另记于进度文件。

日期：2026-10-31。环境：Windows 10、ADOFAI r150（f446792）、Unity 6000.3.25f1、UnityModManager 0.33.3.0、.NET SDK 10.0.402。

## 已通过

- 编译及 68 项核心检查，包括 100000 帧无累计漂移。
- MP3 解码，默认音乐/效果关闭，共同启停和三次从头重播。
- `timeScale=0` 与 `AudioListener.pause=true` 同时成立时，音频样本和拉伸动画仍继续。
- 12 个真实渲染帧中，旋转、翻转与嵌套精灵的底边中心固定。
- 每个 TMP 字符底边中心固定，顶点高度符合当前拉伸比例。
- 停用后原 SpriteRenderer 与 Transform 恢复；TMP 顶点恢复误差为 0。
- 材质属性块的“空/存在”状态与源对象保持一致。
- BGM 从末尾自动循环。
- 通过游戏原生 `ADOBase.RestartScene` 重开，运行对象和时钟不重置，BGM 继续。
- 六项配置独立序列化/读取一致，非法音量被拒绝。
- 最终日志没有 FriendsWine 异常，也没有失效 DOTween 目标警告。
- 已查看真实菜单与测试图案截图，确认没有串图或异常粒子放大覆盖。

最终本地证据：`artifacts/verified-lifecycle/integration-results.txt`，结果为 `TOTAL failures=0, renderFrames=12`。该目录的 `Player.log`、`01b-game-active.png` 和 `03-fixtures-stretch.png` 为日志和视觉证据；本地诊断资产不提交到 Git。

## 边界

物理键盘 F8 的端到端输入未由 GUI 工具验证（该工具被 Windows 沙箱初始化错误阻断）；测试调用了同一运行时切换入口，常规 Update 的快捷键读取没有异常。未测试所有关卡或任意第三方自定义渲染路径。复杂多网格粒子按连通块处理；部分边缘 Renderer 仅有实现和编译验证，具体范围见 README。

早期版本的音频重载、精灵材质块、粒子坐标及副本生命周期问题均已在最终版本修复；不要将 `artifacts/` 下的早期失败记录当成当前结果。
