# 美术资产目录

## 子目录用途

- `Models/` — 3D 模型（fbx, glb, blend）
  - `Models/Castle/` — 城堡构件（墙、塔、城垛）
  - `Models/Decor/` — 装饰物（贝壳、浮木、海星、卵石）
  - `Models/Environment/` — 环境物（椰子树、礁石、海鸥）
- `Textures/` — 贴图（沙子、海水、装饰物纹理）
- `HDRI/` — 环境贴图，用于天空盒和环境光（.hdr 或 .exr）
- `Materials/` — 材质球（拖到模型上用的）
- `Audio/` — 音效（海浪、风声、UI 反馈）
- `Animations/` — 动画文件（如海鸥飞行）

## 命名约定

- 模型：`类型_名字.fbx`，例如 `Decor_Shell_01.fbx`
- HDRI：`HDRI_场景名.hdr`，例如 `HDRI_TropicalSunset.hdr`
- 贴图：`Type_Name_Channel.png`，例如 `Sand_Coral_Albedo.png`、`Sand_Coral_Normal.png`

## 使用流程

1. 把资产丢到对应文件夹
2. push 到 git 后告诉 AI 文件名
3. AI 写挂载脚本和材质配置
