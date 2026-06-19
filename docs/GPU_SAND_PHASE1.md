# GPU 沙子迁移 — 阶段一技术设计

> 目标：把 CPU 端 SDF 体素沙子（96×32×96，CPU Marching Cubes 30~50ms/帧 → 30fps 瓶颈）
> 迁移到 GPU。渲染走 **GPU Marching Cubes + DrawProceduralIndirect**，
> 碰撞走 **GPU SDF Raymarching**。彻底脱离 CPU mesh 生成和 MeshCollider。
>
> 环境：Unity 2022.3.55f1 LTS + URP。PC(3080Ti) 优先跑爽，MR 优化后置。

---

## 0. 现状基线（勘察结论）

- 数据：3 个 `float[]`（`_sdfBase`/`_erosion`/`_wetness`），长度 Nx*Ny*Nz = 97*33*97 ≈ 31 万。
  `_sdf = _sdfBase + _erosion`，`Index(x,y,z)=x+y*Nx+z*Nx*Ny`，`<0`=实心沙。
- `_sdfBase` = 地形基底 + 所有 SdfPiece 用 SmoothMin 合成（仅 piece 增删时重算）。
- CPU MC：标准查表法（MarchingCubesTables），每帧全量扫 96*32*96 cube，
  每输出顶点再做 6 次三线性采样算梯度法线 → 热点。
- 写操作（都改 _erosion/_wetness，然后 RebuildMesh）：
  BoxBrush（铲子）、SurfaceErode（浪蚀）、WetVolume（浇水）、DecayWetness、
  EraseVoxels、RemoveUnsupported（flood fill 连通域塌陷）。
- 碰撞：铲子/放置器靠 `Physics.Raycast` 打 SDF mesh 的 MeshCollider 拿世界命中点。
- 外部调用者：ShovelTool、WaveSimulator、SdfPiecePlacer、SplineWallPlacer、
  SandcastleBootstrap、SandcastleDebugUI、SdfVolumeBoundsVisualizer。

---

## 1. GPU 数据布局

全部用 `StructuredBuffer<float>`（不用 3D RenderTexture，便于 append 和随机写）：

| Buffer | 类型 | 长度 | 语义 | 谁写 |
|--------|------|------|------|------|
| `_SdfBaseBuf` | float | Nx*Ny*Nz | 地形+piece 基础场 | EvaluateBase kernel |
| `_ErosionBuf` | float | Nx*Ny*Nz | 挖/填/侵蚀累加场 | 写操作 kernels |
| `_WetnessBuf` | float | Nx*Ny*Nz | 湿度 0~1 | WetVolume/SurfaceErode/Decay |
| `_VertBuf` | AppendStructuredBuffer<Vert> | 上限(动态) | MC 输出顶点 | MC kernel |
| `_IndirectArgs` | uint[4] | 1 | DrawProceduralIndirect 参数 | CopyCount |
| `_PieceBuf` | StructuredBuffer<Piece> | piece 数 | 上传的形状参数 | CPU 上传 |

`_Sdf`（最终场）不单独存，MC kernel 内现算 `base+erosion`（省一遍带宽）。

`Vert` struct：`float3 pos; float3 normal; float wet;`（28 字节，对齐到 32）。
顶点上限：经验值 cube 数 × 平均 ~3 顶点，先开 `cubeCount*5` 容量，CopyCount 取实际。

---

## 2. Kernel 清单（ShallowSand.compute 或拆多个 .compute）

| Kernel | 线程组 | 职责 |
|--------|--------|------|
| `EvaluateBase` | 3D over 体素 | 地形基底 + 遍历 piece buffer 做 SmoothMin → 写 _SdfBaseBuf |
| `MarchingCubes` | 3D over cube | 查表 MC，算梯度法线，采湿度，Append 顶点到 _VertBuf |
| `BoxBrush` | 3D | 世界 box 内 _ErosionBuf += / -= amount |
| `WetVolume` | 3D | 球内实心体素 _WetnessBuf += falloff |
| `SurfaceErode` | 3D | 水位带内按 wetResistance 加 erosion + 置湿 |
| `DecayWetness` | 1D | _WetnessBuf 全体衰减 |

查找表（EdgeTable[256]、TriTable[256,16]、VertexOffset、EdgeVertexIndex）
上传成 `StructuredBuffer<int>`，MC kernel 索引。

每帧流程：
1. （仅 piece 脏时）`EvaluateBase`
2. 写操作 kernel（按输入触发，铲子/浪等）
3. `_VertBuf.SetCounterValue(0)` → `MarchingCubes` dispatch → `CopyCount` 到 `_IndirectArgs`
4. 渲染：`Graphics.DrawProceduralIndirect`（或 RenderMeshIndirect），材质 = 改造版 Sand.shader，
   顶点着色器从 `_VertBuf` 用 `SV_VertexID` 取顶点。

> 优化注意：MC 不必每帧重扫全量。若该帧无写操作且 piece 未变，沙子静止 → 跳过 MC，
> 复用上帧 _VertBuf。只在 dirty 时重建。这点和现在"事件驱动 RebuildMesh"一致。

---

## 3. 渲染：DrawProceduralIndirect

- 不再有 Mesh / MeshFilter / MeshRenderer 生成沙子几何。
- `Graphics.DrawProceduralIndirect(material, bounds, MeshTopology.Triangles, _IndirectArgs)`
  在 SdfVolume.Update 或 camera 回调里每帧提交。
- Sand.shader 改造：
  - 顶点阶段：`StructuredBuffer<Vert> _VertBuf; v2f vert(uint id:SV_VertexID){ Vert v=_VertBuf[id]; ...}`
  - 片元阶段：现有三面投影噪声 + 湿度 + _GlobalWaterY 逻辑**基本不变**（用 vert 带来的 worldPos/normal/wet）。
  - 保留 `#pragma target 4.5`（buffer in vertex shader 需要）。
- bounds 用体积包围盒（固定），不用每帧算。

---

## 4. 碰撞：GPU SDF Raymarching（替代 MeshCollider）

铲子/放置器不再 `Physics.Raycast`。新增 `SdfRaycast(Ray) → (bool hit, float3 point)`：

**方案**：CPU 发起，GPU 算，异步回读。但鼠标交互要求**当帧拿结果**，异步回读有 1~2 帧延迟。
两个选项：

- **4a（推荐先用）**：保留一份**低频 AsyncGPUReadback 的高度图**（每列沙顶 Y，从 sdf 扫，
  ~128×128 RFloat，每 N 帧回读一次）。CPU 端用这张高度图做射线-高度场求交（步进），
  拿世界命中点。延迟可接受（沙子变化不剧烈），无 GPU 往返卡顿。铲子落点精度足够。
- **4b（后置）**：真 GPU raymarch + 单点 readback。精度高但有延迟，留作精修。

阶段一用 **4a**：`SdfVolume` 维护 `_heightField`（CPU float[128*128]，AsyncGPUReadback 填充），
暴露 `bool RaycastSurface(Ray ray, out Vector3 hit)`。ShovelTool/SdfPiecePlacer/SplineWallPlacer/
FootprintManager 的 `Physics.Raycast(...SdfFloor...)` 全部改调这个。

> SdfFloor 那个平面 BoxCollider 可保留作兜底（射线没命中高度场时）。

---

## 5. 写操作 API 兼容（外部调用者不改逻辑）

SdfVolume 保留同名 public 方法签名，内部改成 dispatch kernel + 标记 dirty：
- `BoxBrush(center,half,rotY,dig,amount)` → 设 kernel 参数 dispatch，标记 sandDirty。
  返回值（受影响实心数）→ 阶段一可先返回估计值或 0，铲子不依赖精确值。
- `WetVolume` / `SurfaceErode` / `DecayWetness` → dispatch。
- `RebuildMesh()` → 改成"标记 dirty"，实际 MC 在 Update 里按 dirty 执行。
- `SandSurfaceWorldY` → 用高度图中心采样，或保持现在的解析值。

**RemoveUnsupported（flood fill）**：阶段一**保留 CPU 实现**，但需要 _erosion 数据。
方案：它低频触发（浪蚀塌陷时），触发时 AsyncGPUReadback 把 _ErosionBuf/_SdfBaseBuf 拉回 CPU，
跑现有 flood fill，结果（要擦的体素）再上传回 _ErosionBuf。有一次往返开销但低频可接受。
→ 标记为阶段三再优化成 GPU 标签传播。

---

## 6. 分阶段落地（每步可跑可回退）

- **Step 1**：数据上 GPU + EvaluateBase kernel + MC kernel + DrawProceduralIndirect 渲染。
  先只读不写，验证"静态沙层能正确显示"。CPU mesh 路径暂时保留做对照，确认无误再删。
- **Step 2**：BoxBrush/WetVolume/SurfaceErode/DecayWetness kernel 化。验证铲子挖填、浇水、浪蚀视觉。
- **Step 3**：高度图回读 + RaycastSurface，切换铲子/放置器射线。验证交互定位。
- **Step 4**：RemoveUnsupported 走回读往返。删 CPU MC 旧路径、删 MeshCollider。
- **Step 5**：清理，性能测（目标 PC 远超 60fps），提交。

每步独立提交，出问题能回退到上一步。

---

## 7. 风险点（提前标记）

1. **顶点 buffer 上限**：MC 输出动态，开太小会截断、太大浪费显存。先按 cubeCount*5 给，
   监控实际 CopyCount，必要时调。
2. **DrawProceduralIndirect + URP**：URP 下 procedural draw 要正确进 forward pass、受光照。
   可能要自定义 pass 或用 `RenderMeshIndirect`（2022.3 有）。Step1 重点验证这个。
3. **SV_VertexID 顶点 buffer 在 URP shader**：需 `#pragma target 4.5`，MR 的 GLES 需确认支持
   （Vulkan 没问题）。MR 阶段再验。
4. **射线延迟**：4a 高度图低频回读，剧烈挖填时落点可能滞后 1~2 帧，交互手感需实测。
5. **piece SmoothMin 顺序**：GPU 并行下 piece 合成顺序无关（SmoothMin 可交换），安全。

---

## 8. 不在阶段一范围

- 水系统（阶段二，沙在 GPU 后无缝耦合）
- flood fill 纯 GPU 化（阶段三）
- 4b 真 raymarch 碰撞（精修）
- MR 性能优化（沙水都成熟后统一做）
