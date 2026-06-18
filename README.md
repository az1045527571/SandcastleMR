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

### 🔲 C8: 塌陷物理
- [ ] 连通域检测（flood fill）
- [ ] 断裂碎块提取 mesh
- [ ] Rigidbody 自由落体
- [ ] 落地融合回沙堆

### 🔲 C9: Quest 3 MR 移植
- [ ] 体积缩到 40cm 桌面（MR 桌面锚定）
- [ ] 手势交互替代鼠标
- [ ] Passthrough + 沙堡渲染融合
- [ ] 性能优化（Compute Shader MC / 降分辨率）

### 🔲 C10: 美术与氛围
- [ ] 水面 shader（折射/焦散）
- [ ] 天空盒 / 环境光
- [ ] 贝壳/浮木装饰物
- [ ] 音效（海浪/放置/崩塌）

---

## 操作说明

| 按键 | 功能 |
|------|------|
| 1 | 旧模式（高度场构件） |
| 2 | SDF 模式 |
| B | SDF 球 |
| M | SDF 烘焙模型 |
| 左键 | 放置 |
| X+左键 | 删除最近 |
| +/- 按住 | 调大小 |
| R 按住 | 旋转预览 |
| V 按住 | 浇水（湿沙抗侵蚀） |
| 右键拖 | 旋转视角 |
| 滚轮 | 缩放视角 |
| F1 | 显示/隐藏 Debug UI |

---

## 坐标系

| 元素 | 世界 Y |
|------|--------|
| 沙地基准 | -0.10 |
| 海平面（静止） | -0.08 |
| 小岛顶 | +0.12 |
| SDF 体积中心 | +0.50 |
| SDF 体积范围 | -0.25 ~ +1.25 |

---

## 工具脚本

- `pull.bat` — 双击拉取最新代码
- `push.bat` — 双击提交推送改动
- `Tools → Sandcastle → Bake Mesh SDF` — 烘焙模型为 SDF 资产

---

## 技术架构

```
SandcastleBootstrap (Awake)
├── SandTerrain (高度场 128×128)
├── SdfVolume (96×32×96 体素)
│   ├── SdfPiece[] (球/Box/BakedMesh)
│   ├── _sdfBase[] (地形+piece 基础 SDF)
│   ├── _erosion[] (侵蚀累积场)
│   └── Marching Cubes → Mesh
├── SimpleWave (海面视觉)
├── WaveSimulator (潮汐 + 侵蚀驱动)
├── SdfPiecePlacer (放置器)
└── OrbitCamera
```
