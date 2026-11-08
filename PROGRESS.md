# 朋友的酒 — 进度与恢复说明

## 星球纹理裁切修复（2026-11-08，已完成）

当前状态：正式 1.0.3 已构建、安装并核验。游戏 Mods 中 DLL 与 dist 构建一致，SHA-256 为 `9958644FAFC4E3DD82278784E083FF81852346D822B06A3A96286BAF0731D7B9`。原 settings.json 完整保留，SHA-256 为 `56242A0C6B34E7FC71C6F6226A1F3E0EA85E20514F352A516F0DB2B49F4855B9`。正式 DLL 不含测试类，无待完成的构建或安装步骤。重新启动游戏后按 F8 使用。

- 用户确认：拉伸时表情星球顶部被切平、两侧出现紫色碎片；正确行为是整个球及其纹理一起变为椭圆。
- 已确认 Unity 在更新 Position 时重算 UV。NativeSpriteEffect 现缓存原始 UV，并在每次写位置后恢复 TexCoord0；切换精灵和释放效果时正确管理缓存。
- 新增条件编译入口 `--friendswine-sprite-texture-test`，用真实游戏表情资源，将模组结果与完整 Transform 缩放的像素对照，覆盖横向、纵向及翻转/旋转。
- 检查开始时用户游戏正在运行，使用独立测试副本完成验证，未强制关闭用户游戏。验证结束时原游戏已自行退出，可以正常安装正式版。
- `sprite-texture-baseline` 的 12 组真实表情全部复现旧问题；`sprite-texture-verified` 的 24 组画面与完整缩放参照逐像素一致，122 项断言通过，覆盖真实星球材质、连续变形、切换贴图和偏心图集。
- `sprite-fix-culling` 的 17 项检查、`sprite-fix-track-verified` 的 41 项检查通过。隐藏窗口不逐帧自动绘制，验证组件已补充显式相机渲染；早期相关失败不代表生产回归。
- `sprite-fix-regression-verified` 的 28 项基础检查通过，含 12 个实际渲染帧、每字原点、音乐、暂停、循环、启停、重开、配置和恢复。68 项核心检查通过。
- 发行包为 `dist/FriendsWine-1.0.3.zip`。已清理约 2.6GB 的独立游戏副本，先移除所有 Junction 再删除副本，未修改链接指向的游戏资源。截图和验证记录保留在 artifacts 中，本轮 1.0.2 对照测试包移至 `artifacts/sprite-texture-baseline/FriendsWine-1.0.2-test.zip`。

以下为 1.0.2 及更早版本的历史记录。

## 视野裁剪与原生轨道动画（2026-11-01，已完成）

最终交付：游戏 Mods 中已安装正式 1.0.2；发行包 dist/FriendsWine-1.0.2.zip。安装 DLL 与构建 DLL 一致，SHA-256 为 FB5AC5AA23057205B63EC854AAF5EDE4478E3324DE0AF261F494683867684728。settings.json 安装前后哈希完全相同。没有未完成的构建或安装步骤。以下保留排查过程，早期“下一步”均已完成。

- 新要求：即使拉伸后也完全在视野外的砖块停止拉伸计算，缓解巨大谱面卡顿；同时修复仍然常显/缺失的出现和消失动画。
- 已确认目前每台相机都会遍历所有渲染副本并执行材质同步、层级修改和恢复，且每 0.2 秒全场景扫描；没有视锥裁剪或懒创建，开销随整个谱面增长。
- 正在追踪游戏轨道动画的原生渲染器依赖。1.0.1 只桥接 scrFloor 的可见性通知，尚未保留完整原生渲染生命周期。
- 初查时游戏没有运行。接下来需完成代码、边界/性能/真实动画验证，最后构建安装正式版并保留 settings.json。
- 已写入 1.0.2 首版并编译通过：普通 SpriteRenderer 使用原渲染器的位置缓冲，MeshRenderer 使用临时变形网格且保留原渲染器，不再隐藏原件或依赖 scrFloor 回调桥接。旧桥接仅保留在条件测试构建用于对照。
- 渲染器注册改为轻量记录，只有通过整个拉伸周期的保守视锥判断后才创建变形资源/副本。每台相机仅恢复本次真正应用的对象，不再对全谱面逐个恢复。视野外 TMP/UI 也跳过网格重建，相机视锥按帧缓存。
- 当前新源码已编译，原有 68 项纯数学检查通过；尚未实际安装验证新精灵缓冲和新的恢复方式。下一步先运行可见性/基础画面验证，再加真实出现/消失动画和大规模视野外对象测试。
- 实测发现 Unity 6 的蒙皮变形缓冲不影响普通无骨骼精灵，已经改成独立 Sprite 的 Position 顶点通道，保留 UV/图集及原渲染器；原 Sprite 资产不修改。`artifacts/native-sprite-geometry` 基础回归 27 项通过，实际截图可见拉伸。旧音频断言对帧卡顿过于敏感，已改为核对真实经过时间及播放位置回绕；未修改音频功能。
- `artifacts/culling-fixed`：一万个测试对象外加一个边缘对象中，实际只处理 9 个、跳过 9992 个，仅创建 9 份变形资源；边缘拉伸进入视野仍有像素，移入/移出即时更新，原 Sprite 资产未变化，所有检查通过。首轮暖态处理约 5.5ms；统计显示的 candidates 在 yield 后被其他相机覆盖，已修正统计快照。
- 又把包围盒判断移到启用/图层等引擎属性读取之前，进一步减少离屏对象开销，待新一轮性能对照。
- 新增可选 `--friendswine-track-tests`：VisibilityVerification 会直接调用游戏 8 类出现、6 类消失效果，在开始/中间/结束三个时点对比模组开关时的实际像素和动画状态。该套新测试尚未运行。
- 动画首轮真实对照中 12/14 类匹配；Scatter、Shrink_Spin 的失败来自测试只重置 Transform、未重置游戏用于旋转 Tween 的 tweenRot，现已修正夹具。首次反射 StartEffect 选到重载的异常也已修正，不是生产代码错误。
- 原生渲染包围盒改为整个节拍的局部范围，避免屏幕边缘因周期性压缩使游戏动画脚本反复被停用；真正绘制的顶点仍按当前相位变形。测试改为检查真实 Sprite 顶点，而非用保守包围盒当成几何尺寸。
- 已加入原 Renderer 的 OnWillRenderObject/isVisible 探针，以及回调里更改可见性的像素检查，以覆盖旧副本路径遗漏的完整渲染生命周期；该加强版裁剪测试尚待运行。
- `artifacts/track-animations-final`：41 项全通过，含全部 8 类出现和 6 类消失动画；开始/中间/结束的像素与位置、缩放、旋转、透明度与关闭模组时完全匹配。
- `artifacts/native-final-regression`：新增偏心原点、X/Y 翻转及原生世界包围盒包含实际几何的检查后，基础回归全通过；所有原有 BGM/启停/重开检查正常。
- `artifacts/culling-final`：加强版裁剪全通过，包括原 renderer.isVisible 与 OnWillRenderObject、在渲染回调中隐藏图像、多摄像机切换。该轮处理 9 个可见对象，仅创建 9 份资源。
- 离屏包围盒检查移到图层/Renderer.enabled 之后、材质/几何等重操作之前：避免给不属于当前相机的粒子/蒙皮组件查询包围盒而同步引擎任务。接下来需要最终新旧同场景性能对照，再更新文档并安装正式版。当前仍为开发验证构建。
- 最终裁剪验证：artifacts/culling-release-verified，17 项通过。10001 个测试对象只处理/分配 9 个，裁剪 9992 个；暖态相机应用阶段中位 6.954ms。旧路径对照 artifacts/culling-legacy-comparison 处理/分配全部 10001 个，中位 196.058ms。
- 旧路径同时复现原 Renderer 不收到原生回调/isVisible、渲染回调里隐藏仍可见；新路径已通过对应检查。这确认 1.0.1 专用可见性桥接不足以保留完整渲染生命周期。
- 所有 14 类原生出现/消失动画对照及透明度检查 41 项通过；偏心/旋转/翻转、原生包围盒、BGM 等基础回归通过。README/VERIFICATION.md 已更新；当前只剩正式构建安装和最终文件核验。

## 隐形砖块修复记录（已完成）

最终结果：1.0.1 正式构建、安装和核验均已完成，现有 settings.json 完整保留。没有需要继续执行的安装步骤。以下保留排查过程，早期“下一步”条目均已完成。

- 用户报告：隐形砖块没有正确隐形，总是可见。需要保持游戏本身的隐藏/透明度规则，拉伸不应使它们重新可见。
- 初查发现游戏目前正在运行（PID 26252）；不要擅自关闭用户正在玩的游戏。先完成修复和验证准备，再解决更新安装所需的重启。
- 正在检查 src/RendererProxies.cs 的可见性和 MaterialPropertyBlock 复制，以及游戏 scrFloor / FloorMeshRenderer / FloorSpriteRenderer 的隐藏逻辑。
- 先前的基础/菜单验证没有覆盖隐形砖块，不能以旧测试通过证明本问题已解决。
- 已检查游戏代码：scrFloor.LateUpdate 把 opacity 写入材质 _Alpha；OnBecameInvisible 会关闭该脚本。原渲染器被副本替代时，这条更新链会停掉。
- 已新增 src/FloorVisibility.cs：通过副本真实 OnWillRenderObject 向游戏原脚本转交可见性，在副本仍可见时阻止原渲染器造成的错误不可见通知，离开所有摄像机后照常交回不可见状态。监测砖块本体及其 topGlow，停用时恢复原状态。
- 已新增 verification/VisibilityVerification.cs：进入内置 1-1，用真实 scrFloor 和游戏材质，检查 0/0.25/1 透明度、实际像素、离开/重新进入视野、反复启停、Renderer.enabled 和 forceRenderingOff。
- 版本已改为 1.0.1；构建脚本的 ZIP 名称改为读取 Info.json 版本。初次构建和 68 项核心检查通过。
- 游戏已退出，可以进行安装和独立测试。下一步先运行带 --friendswine-visibility-baseline 的测试证明旧路径失败，再运行修复路径对比；最终必须重新构建并安装不含测试代码的正式版本。
- 首次 baseline 无效：r150 的 1-1 使用内置关卡资源，不能直接 SceneManager/loader.LoadScene("1-1")。测试已改为原生 scrController.EnterLevel("1-1", false)，等待 scnGame 加载完成并核对 levelName，再继续检查；无效的早期结果在 artifacts/visibility-baseline，不能当作复现证据。
- 有效复现：artifacts/visibility-baseline-gameplay/visibility-results.txt，5 项失败。真实 1-1 的 scrFloor 在效果启用后 enabled=false，设置 opacity=0 后材质 alpha 仍为 1，截图仍有 11890 个非背景像素；0.25 透明度及重新进入视野也未同步。这证实原渲染器可见性被替代导致更新冻结。下一步运行同一构建、移除 --friendswine-visibility-baseline 来验证修复。
- 修复验证已通过：artifacts/visibility-fixed/visibility-results.txt，17 项 PASS / 0 项 FAIL。透明度 0 对应材质 alpha=0，隐藏截图非背景像素为 0；0.25 对应 alpha=0.25。离开视野仍正常停用脚本，返回视野后隐藏状态正确；停用/重新启用、Renderer.enabled=false 和 forceRenderingOff=true 均保持隐藏。
- 下一步运行原有 --friendswine-selftest 到 artifacts/visibility-regression，核对无模组异常，再更新文档并构建/安装不含两种测试入口的正式 1.0.1。
- 原有回归已通过：artifacts/visibility-regression，27 项 PASS、0 项 FAIL、12 个实际渲染帧；Floor visibility 异常、FriendsWine 错误及失效 Tween 警告均为 0。全透明/半透明/旧路径三张截图已实际查看。接下来只需安装并核验正式包。
恢复入口：先看顶部完成状态和“最终交付与核验”，不要把早期调试日志当成当前问题。

## 用户已确认的规格

用户强调需求有歧义必须先询问。以下经过两轮澄清并得到“没错”的最终确认，无需再询问。

- 游戏目录：`C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice`，已装 UnityModManager。
- 工作目录：`D:\A Dance of Fire and Ice\Tools\朋友的酒`。
- 模组名称：朋友的酒；内部 ID 与 DLL 名称：FriendsWine。
- 尽可能覆盖所有游戏图像，含主菜单、选关、角色、轨道、装饰、背景、UI、文字、粒子。每个字和每颗粒子分别处理。
- 每个图像以自身底边中心为固定原点，原点和坐标轴跟随对象旋转。
- 固定 130 BPM，一拍 60/130 秒；原状→纵向极值→原状→横向极值→原状，四段等长、线性、无停顿。
- 四个独立配置：VerticalX=0.75、VerticalY=1.35、HorizontalX=1.35、HorizontalY=0.75。
- 默认 F8 切换启用/停用；启动游戏时效果停用；快捷键可配置。
- 启用时 BGM 从头播放且动画从第一轮开始；停用时停止 BGM 并恢复画面；再启用重新从头。
- BGM 自动循环，与原游戏音乐混音，不改变原游戏音乐。默认音量 0.5，可配置。
- 游戏暂停、切场景、重开关卡不停止 BGM/动画，不重置动画时钟。无需对齐关卡音乐。
- 所有六项配置使用独立 settings.json，UMM 面板提供修改、保存与重载入口。
- 用户已授权直接安装到游戏 Mods；源码和发行产物都保留在工作目录。
- 原始 MP3 随包发布，也保留在可提交到 GitHub 的源码中。用户要求写好 .gitignore。
- 用户最新特别要求：持续记录进度，确保额度耗尽或任务被打断后可以恢复；没有要求暂停工作。

## 当前完成项

- 完整 C# 源码在 src/；Info.json、config.default.json、README.md、build.ps1、install.ps1、.gitignore、.gitattributes 已建立。
- 构建脚本直接调用本机 .NET SDK 的 Roslyn，并引用本机游戏的 Mono/Unity/UMM/DOTween DLL，不下载或分发游戏依赖。
- BeatMath：纯数学四段线性动画，使用 unscaled 实时时钟，无累计缩放。倍数允许任意有限正数。
- RuntimeController：持久独立音源，支持 MP3 预载、音频初始化/设备变化后的重新解码、暂停下继续播放、循环、同步启停。
- Main/Settings：独立 JSON 配置、校验、原子保存及 .bak、快捷键（含 Ctrl/Alt/Shift 组合）、完整 UMM 设置入口。
- Sprite/Mesh/Skinned/Line/Trail/Particle 渲染副本；SpriteMask 适配；uGUI 网格效果；TMP 每字顶点；旧 TextMesh；IMGUI 字符及直接 DrawTexture/DrawMesh 调用拦截。
- 渲染副本只在相机绘制期间临时接入原层级，其余时间停用并置于 Runtime 下；不缩放原游戏 Transform，不让普通游戏层级扫描将副本纳入淡出动画。
- 动态对象每 0.2 秒补充发现。当前主菜单实际遇到的全部 Renderer 类型都有适配：SpriteRenderer、MeshRenderer、ParticleSystemRenderer、LineRenderer、TrailRenderer。
- 停用会还原原渲染器、UI 和文字，移除绘制拦截，并只清理绑定到副本自身的 DOTween 动画。
- 原始 BGM：`活跃黑江乐 - 「邦友的酒(DJ版)」梦的结唱halo.mp3`，8,279,082 字节；打包为 Audio/bgm.mp3。

## 最终验证结果

1.0.1 权威结果目录：`artifacts/visibility-fixed/`（17 项针对性检查）和 `artifacts/visibility-regression/`（27 项回归）。`artifacts/verified-lifecycle/` 是 1.0.0 历史结果。所有本地诊断资产均被 .gitignore 排除。

- 构建成功，无编译错误。
- 68 项核心检查通过，含 100000 帧无累计漂移、独立四轴倍数、线性中点、边界连续、长时间循环和配置范围检查。
- 游戏内 27 项集成断言全部通过：`TOTAL failures=0, renderFrames=12`。
- 真实摄像机的 12 帧检查通过：旋转、翻转及父子嵌套图像底边中心不动。
- 每个 TMP 字符的独立原点和当前比例正确；停用后的顶点与原始值误差为 0。
- BGM 与效果共同启停；timeScale=0 且 AudioListener.pause=true 时仍继续；三次再次启用都从音频起点开始。
- 将播放位置移到结尾后验证了自动循环。
- 通过游戏原生 ADOBase.RestartScene 重开，确认运行对象和动画起始时钟不变，BGM 继续播放。
- 六个配置字段分别往返一致；非法音量被拒绝。
- 最新日志没有 FriendsWine 异常，也没有失效 DOTween 目标警告。
- 实际查看了 `01b-game-active.png` 和 `03-fixtures-stretch.png`；精灵纹理正确，粒子没有异常放大覆盖画面，拉伸可见。
- GUI 输入工具不能运行，所以没有通过模拟物理 F8 按键做端到端检查；实际游戏验证调用了相同的运行时切换方法，正常 Update 中的快捷键读取没有异常。

验证环境：ADOFAI r150（f446792，2026/10/29），Unity 6000.3.25f1，UnityModManager 0.33.3.0，Windows 10，.NET SDK 10.0.402。

## 已解决的真实问题

这些是调试历史，均已修复，不是待办：

1. 游戏启动音频初始化会使早期下载的 MP3 进入 Unloaded。已监听音频配置变化并重新解码；效果启用时按现有时钟恢复音频位置，不重置动画。
2. 将空的 per-material MaterialPropertyBlock 设置给副本会遮蔽精灵的原生纹理数据，造成串图。空块现用 null 清除。
3. 粒子默认烘焙模式遇到不同模拟/缩放空间会重复应用父变换，产生巨大的亮色覆盖。现在显式烘焙世界坐标，再转换回源局部坐标。
4. 游戏扫描原对象的子层级时会给副本绑定淡出 Tween，停用销毁副本后出现失效目标。现在隔离副本层级、标记组件 DontSave，并定向清理自身 Tween。
5. 测试最初直接使用 SceneManager 重载，绕过了游戏正常清理。现使用游戏原生 RestartScene。
6. 隐藏游戏窗口导致没有实际摄像机渲染。后续验证使用了真实可见游戏窗口，并自动退出。
7. 测试物体最初随异步菜单场景切换被销毁，造成假失败；现在测试物体持久化，测试结束统一退出清理。

## 已知范围与限制

- 没有遍历所有关卡或第三方模组。不能声称任意自定义 GPU 间接绘制、特殊渲染管线、UI Toolkit 或着色器内部生成图像都能逐对象处理。
- 复杂多 Mesh 粒子的回退按 UV 方向下的连通块处理；不连通的多部件单颗粒子可能拆成多个原点。单一可读 Mesh 与 Billboard 使用各自独立粒子原点。
- 新对象最多约 0.2 秒后加入。大量对象、文字和粒子会增加渲染开销。
- 源码含旧 TextMesh、Skinned/Mesh/Line/Trail/SpriteMask 路径；这些边缘类型没有全部做专用视觉夹具验证。
- 不把整张合成画面再次整体缩放；这会破坏“每个图像自身原点”的要求。

## 最终交付与核验

- README 与 VERIFICATION.md 已更新。正式构建、核心验证和安装均成功。
- 安装位置：`C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\Mods\FriendsWine`。
- 当前发行包：`dist/FriendsWine-1.0.2.zip`；可直接安装目录：`dist/FriendsWine/`。旧版本包仍保留。
- 已反查正式 DLL 的类型列表，不包含 IntegrationVerification、VisibilityVerification、CullingVerification、RenderCallbackProbe 或旧 FloorVisibility 桥接。开发诊断开关由条件编译排除。
- 1.0.2 构建 DLL 与安装 DLL 的 SHA-256 相同：`FB5AC5AA23057205B63EC854AAF5EDE4478E3324DE0AF261F494683867684728`。
- 源 MP3 与包内 Audio/bgm.mp3 的 SHA-256 相同。
- ZIP 仅包含 FriendsWine.dll、FriendsWine.pdb、Info.json、README.md、settings.json、VERIFICATION.md、Audio/bgm.mp3；没有游戏 DLL。
- 安装配置已核验：0.75 / 1.35 / 1.35 / 0.75，音量 0.5，快捷键 F8。
- 已初始化本地 Git 仓库，分支 main；未创建 commit、未连接远程或发布 GitHub。
- 已实际检查 .gitignore：build/dist/artifacts/obj 和运行时 settings.json 被排除，原始 MP3、源码和 config.default.json 会保留。
- 测试游戏进程已自动退出。正常启动后效果默认停用，按 F8 开始。
- 当前没有必须继续完成的开发或安装步骤。若恢复聊天，先向用户报告交付位置；后续按用户反馈调节效果或扩展边缘渲染覆盖。

## 恢复时的工具与权限

- 普通 exec_command 和 node_repl 因 Windows 沙箱错误无法启动：helper_sandbox_lock_failed / SetNamedSecurityInfoW ... failed: 5。
- 已按权限流程使用 require_escalated 进行本任务的构建、安装、只读检查与游戏验证；自动审批允许。不要绕过权限机制。
- 已读 computer-use SKILL.md、guidance.md、confirmations.md、api.md；node_repl 重试/重置后仍不能初始化，尚未使用任何 Windows UI 输入自动化。
- 游戏验证是项目自己的条件编译入口，通过已授权 shell 启动游戏进程，使用 Unity API 检查、保存游戏截图，然后自动退出。
- 开发验证构建：`./build.ps1 -IntegrationTest`。仅命令行带 `--friendswine-selftest` 才运行；`--friendswine-test-output <目录>` 指定结果目录。正式构建不包含此入口或隔离诊断开关。
- 测试源码在 verification/IntegrationVerification.cs；旧 artifacts 子目录保存了排查证据，避免与最终 verified-lifecycle 结果混淆。
- 没有使用子代理。用户未要求提交 Git commit 或发布 GitHub，不要擅自发布。
