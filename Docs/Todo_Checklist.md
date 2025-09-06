# 准备阶段 Checklist（创建空 3D 项目之后）

## 0. 版本控制
- 初始化 Git；把根目录的 `.gitignore` 用上；必要时为大文件启用 Git LFS（Textures/Audio/FBX）。
- 在 `Docs/` 写下里程碑与证据点截图清单（课程验收需要）。

## 1. 渲染与项目设置
- `Edit > Project Settings > Player > Color Space` 设为 **Linear**。
- 使用 **URP**：创建 `UniversalRenderPipelineAsset` 与 `Renderer`，在 `Graphics` 指向，勾选 **SRP Batcher**。
- 关闭 VSync（开发期便于看真实 FPS）；目标帧率 60。
- 导入 **Post-processing(URP Volume)**，加一个全局 Volume（Bloom/Vignette/Color Adjustments）。

## 2. 输入 & UI
- 安装 **Input System**，`Active Input Handling` 设为 `Both` 或 `Input System Package`.
- 导入 **TextMeshPro** Essentials。
- 在 `Assets/_Project/UI/Prefabs` 放入 `HUD_Debug`（FPS/RTT/Entities 计数的简单 Text）。

## 3. Packages（Package Manager）
- Core：Burst、Jobs、Collections、Mathematics。
- DOTS：Entities、Entities Graphics（Hybrid Renderer）、Unity Transport、**Netcode for Entities**。
- 工具：Cinemachine、Timeline、ProBuilder、Addressables(可选)。
- 物理：Unity Physics（如需 Ragdoll 可后加 Articulation/PhysX 方案）。

## 4. 物理 & 分层
- 定义 Layers：`Player`、`Enemy`、`Projectile`、`Trigger`、`IgnoreRaycast`。
- 配好 **Layer Collision Matrix**：例如 `Projectile` 只与 `Enemy`/`Environment` 相撞；探索段物体多用 **Trigger**。
- `Time.fixedDeltaTime` 建议 0.02；后续根据网络步进再微调。

## 5. 目录命名规范
- 资源前缀：`SM_`(静态网格)、`SK_`(骨骼)、`M_`(材质)、`FX_`(粒子)、`S_`(音频)。
- 预制体：`P_`，变体用 `P_Xxx_VariantName`。
- 场景：`A_` 开头为探索、`B_` 开头为尸潮；`_Dev` 为沙盒。

## 6. 初始场景与引导
- 建 `Boot` 场景：只挂一个 `Bootstrap` 物体（DontDestroyOnLoad），加载 AudioMixer、设置 Volume、初始化网络 HUD。
- 建 `_Dev/Sandbox` 场景：用 ProBuilder 白盒一条直道，方便做性能与网络烟囱测试。

## 7. ECS/Netcode 基线
- 在 `Scripts/Runtime/ECS` 新建 `ZombieCountSystem`（空 System + 统计器）与 `Authoring/Baker`（把 Inspector 参数烤进 BlobAsset）。
- 在 `Scripts/Runtime/Netcode` 新建最小连线样例（Host/Client 按键、本地回环），先跑通连接与 HUD 的 RTT。

## 8. AudioMixer
- 创建 `Master` 下四个组：`BGM`、`Weapons`、`Environment`、`Zombies`；做一个 `Combat_Snapshot`（限幅/压缩），战斗切换展示“音频系统”。

## 9. asmdef（加快编译）
- 为 `Scripts/Runtime` 与 `Scripts/Editor` 各建一个 Assembly Definition。Editor 版勾选 `Include Platforms: Editor`。

## 10. 证据点准备
- 在 `Docs/EvidenceShots.md` 记录需要拍的截图：URP 勾选、Animator 层/BlendTree、Physics 矩阵、Profiler 与 Network HUD 等。

完成以上，再进入“第 1 周里程碑”：连上局域网、玩家预测移动、协作门触发，并录到 20 秒片段。
