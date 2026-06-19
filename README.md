# SandcastleMR

Quest 3 MR 沙堡模拟器原型 — SDF 体积融合 + 海浪侵蚀

---

## 项目里程碑

### ✅ C1: 基础场景
- Unity URP 工程
- SandTerrain 高度场（128×128，20m）
- 程序化沙色 shader（噪声 + 闪光 + 三面投影）
- 简单海浪视觉（Gerstner 正弦波）
- OrbitCamera 控制

### ✅ C2: 沙地物理
- 高度场堆沙/挖沙 (Pile/Carve)
- 安息角塌陷模拟 (Slump)
- 湿度系统（顶点色 R 通道）
- 湿度蒸发 + 视觉表现

### ✅ C3: 城堡构件放置
- PiecePlacer（旧模式，按 1）
- CastlePiece shader（三面投影 + 法线融合）

### ✅ C4: SDF 体积系统
- 96×32×96 体素 SDF 体积
- CPU Marching Cubes 提取 mesh
- SDF 梯度法线（平滑着色）
- Smooth Union 融合（球 + 地形一体化）
- SdfPiece: Sphere / Box / BakedMesh
- MeshSdfBaker Editor 工具（烘焙 FBX → 3D SDF 纹理）
- Resources 自动加载 SDF 资产
- 放置吸附地形高度
- SDF 区域边界可视化（青色线框）
- 模式切换: 1=旧模式, 2=SDF, B=球, M=烘焙模型
- +/- 调大小, R 旋转, X+左键 删除

### ✅ C5: 地形-SDF 融合
- SandTerrain 高度场注入 SDF（terrain SDF = worldY - height）
- Sand shader 全局水位潮湿线（_GlobalWaterY）
- SDF mesh 与 SandTerrain 同 shader 同光照
- 边缘 2 格淡出避免 MC 断面
- Z-fighting 处理（SDF mesh 微抬 1mm）

### ✅ C6: 海浪侵蚀（基础）
- WaveSimulator: 周期性涨退潮
- 均匀表面侵蚀: 每次浪冲刷 → 表面后退
- 持久侵蚀场 _erosion[]（累积，不丢失）
- 基础 SDF 缓存（piece 增删时才重算，侵蚀重建只做加法+MC）
- 潮湿着色: shader 按 _GlobalWaterY 自动显示湿沙

### ✅ C7: 侵蚀效果优化
- [x] 均匀表面后退：SDF 整体偏移式形态学腐蚀，逐层化开、整体变小变圆
- [x] 湿沙抗侵蚀：被浇过水的区域（_wetness）按 wetResistance 侵蚀更慢
- [x] 侵蚀粒子特效：ErosionParticles 在被冲掉的表面体素喷沙色碎屑
- [x] 侵蚀过程可视：浪头期间阶段性重建 mesh（rebuildInterval），化开连贯
- [x] 玩家浇水：按住 V 给鼠标指向的沙加湿（护城河/加固）

### ✅ C7.5: 重构为 40cm 全局 SDF 沙箱
- [x] 场景从 20m 重建为 40×40cm 桌面真实尺寸
- [x] 废除高度场 SandTerrain（无法表达厚度/掘空/悬挑）
- [x] 沙地改为全局有厚度 SDF（8cm 实心沙层 box），一体无接缝
- [x] SDF 分辨率 100×60×100 = 4mm/格（CPU MC 可交互）
- [x] 删除旧模式 PiecePlacer/CastlePiece/PlacerModeSwitcher，SDF 为唯一系统
- [x] 放置/浇水/相机/海浪参数全部对齐 cm 尺度

### 🔲 C8: 塌陷物理
- [x] 连通域检测（flood fill），无支撑残块立即移除 + 掉渣粒子
- [ ] 断裂碎块提取独立 mesh
- [ ] Rigidbody 自由落体
- [ ] 落地融合回沙堆
- [ ] 3D 安息角塌陷（替代旧高度场 slump）

### ✅ C8.5: GPU 渲染管线
- [x] GPU Marching Cubes 渲染（compute shader，DrawProcedural）
- [x] **职责划分**：CPU 算 SDF（base+侵蚀+塌陷，唯一真相），GPU 只做 MC 渲染
  - base（含球/盒/样条/bakedmesh）仅 piece 增删时 CPU 算一次，上传 GPU
  - 侵蚀/塌陷/collider 全读 CPU `_sdf`，保证正确；每帧 MC 在 GPU
- [x] GPU 顶点回读重建 MeshCollider（碰撞与 GPU 显示一致；Quest 端待改 AsyncGPUReadback）
- [x] **法线平滑**：SDF 梯度采样半径可调（`normalSmooth`），抹除侵蚀态边缘锯齿
- [x] F2 运行时切换 GPU/CPU 路径（调试对照 + GPU 不兼容时降级）

### 🔲 C9: Quest 3 MR 移植
- [x] 体积已是 40cm 桌面尺寸（C7.5 提前完成）
- [x] GPU MC 渲染管线就绪（C8.5）
- [ ] RebuildCollider 同步回读改 AsyncGPUReadback（避免 Quest 掉帧）
- [ ] 手势交互替代鼠标
- [ ] Passthrough + 沙堡渲染融合
- [ ] MR 桌面锚定

### 🔲 C10: 美术与氛围
- [ ] 水面 shader（折射/焦散）
- [ ] 天空盒 / 环境光
- [ ] 贝壳/浮木装饰物
- [ ] 音效（海浪/放置/崩塌）

---

## 操作说明

| 按键 | 功能 |
|------|------|
| B | SDF 球 |
| M | SDF 烘焙模型 |
| 左键 | 放置 |
| X+左键 | 删除最近 |
| +/- 按住 | 调大小 |
| R 按住 | 旋转预览 |
| V 按住 | 浇水（湿沙抗侵蚀） |
| G | 样条沙堤模式（左键加点, 回车/右键完成, Backspace撤销, Esc取消） |
| F2 | 切换 GPU/CPU 渲染路径（对照/降级用） |
| 右键拖 | 旋转视角 |
| 滚轮 | 缩放视角 |
| F1 | 显示/隐藏 Debug UI |

---

## 坐标系（40cm 桌面沙箱）

| 元素 | 世界 Y |
|------|--------|
| 沙箱底 | 0.00 |
| 沙层表面（初始 8cm） | +0.08 |
| 静止海面 | +0.04 |
| SDF 体积中心 | +0.12 |
| SDF 体积范围 | 0.00 ~ +0.24 |

沙箱：40×40cm，SDF 分辨率 100×60×100 体素 = **4mm/格**。
Unity 单位仍 1=1m，整个场景按真实桌面尺寸建。

---

## 工具脚本

- `pull.bat` — 双击拉取最新代码
- `push.bat` — 双击提交推送改动
- `Tools → Sandcastle → Bake Mesh SDF` — 烘焙模型为 SDF 资产

---

## 技术架构

```
SandcastleBootstrap (Awake)
├── SdfVolume 【CPU 核心: 唯一 SDF 真相】
│   ├── 初始沙层 (实心 box SDF)
│   ├── SdfPiece[] (球/Box/Spline/BakedMesh)
│   ├── _sdfBase[] (沙层+piece 基础 SDF, 仅 piece 增删时重算)
│   ├── _erosion[] (侵蚀累积场)
│   ├── _wetness[] (湿度场, 湿沙抗侵蚀)
│   ├── _sdf[] = base + erosion (最终场, 供塌陷/查询)
│   ├── RemoveUnsupported (连通域检测, 无支撑立即移除)
│   └── ExtractMesh (CPU MC — 仅 CPU 渲染路径/降级用)
├── GpuSandRenderer 【GPU 渲染路径, 默认开】
│   ├── 上传 _sdfBase/_erosion → GPU
│   ├── SandMarchingCubes.compute (每帧 GPU MC → 顶点 buffer)
│   ├── SdfGradient 法线 (normalSmooth 可调平滑)
│   ├── DrawProcedural 渲染 (Sand_GPU.shader)
│   └── RebuildCollider (回读顶点重建 MeshCollider)
├── SimpleWave (海面视觉)
├── WaveSimulator (潮汐 + 侵蚀驱动 + 塑塌)
├── ErosionParticles (碎屑粒子)
├── SdfPiecePlacer (唯一放置器)
└── OrbitCamera
```

## 精度与渲染

- **体素精度**由 SdfVolume.resolution 决定(当前代码默认 96×32×96 @ size 5×1.5×5m ≈ 52mm/格)。
  几何精度上限在体素场, GPU 只是把这份场渲染出来——提 GPU 采样密度不会凭空增加几何细节。
- **边缘硜齿**主要来自法线(SDF 梯度)贴着体素阶梯跳变, 侵蚀逐格累加时阶梯更陡。
  解法是加大梯度采样半径 `normalSmooth`(GpuSandRenderer 上可调, 1.5~2.5 推荐), 不动几何/精度。
- **更高视觉细节**应走法线贴图/细节纹理(在 Sand_GPU.shader 的 frag), 而非堆体素。
  全局 0.5mm 均匀体素 ≈ 1.5 亿个, 内存/算力 Quest 3 扣不住, 不可行。

> ⚠ 分辨率不一致: 上图及坐标系章节的 "100×60×100 @ 4mm / 40cm" 是 C7.5 重构目标, 但
> 当前代码 SandcastleBootstrap 未覆写 SdfVolume 默认值(5m / 96×32×96)。迁移未落地。

> 高度场 SandTerrain / PiecePlacer / CastlePiece 已于 C7.5 重构退场，
> 沙地改为全局有厚度 SDF。高度场无法表达厚度/掘空/悬挑，不适合本需求。
