# Unity3D 风景项目（水体 Shader 复刻）

> 本立项目标：复刻抖音作者「雨鱼鱼雨雨雨」的 Unity 水体 Shader 视频效果，
> 产出可复现的学习路径、知识库与项目工程。

## 一、项目信息

| 项目 | 内容 |
| ---- | ---- |
| 项目名 | Unity3D 风景项目立项（水体场景） |
| Unity 版本 | 2022.3.57f1c2（LTS；与 MCP测试 项目同为 2022.3 系列） |
| 渲染管线 | URP 14.0.11（Universal Render Pipeline） |
| 协作方式 | Unity 官方 MCP 服务器（gamedevtoi.gdt-unity-mcp-server v0.1.13）+ UnityBridge 桥（ws://127.0.0.1:6400） |
| 版本管理 | git + GitHub（大版本 v0/V1/V2 推送远端；小版本 v0.x 本地提交） |

## 二、技术要点（视频复刻清单）

1. 自适应曲面细分（DX11 Tessellation，近处加密网格）
2. 格斯特纳波（Gerstner Wave，波浪位移 + 法线）
3. 屏幕空间折射（Screen Space Refraction，采样 URP `_CameraOpaqueTexture`）
4. 折射深度保护（按深度/菲涅尔收敛折射偏移，防穿帮）
5. 动态水体交互（Interaction：落点写入交互 RT，Shader 采样产生涟漪）
6. RNW 法线混合（RNW Normal Blending，方向波法线重建与混合）
7. 程序化 Ripple（Procedural Ripple，无贴图程序化涟漪）
8. 格斯特纳波驱动的浪尖效果（浪尖白沫/卷曲亮度增强）

参考：地平线：零之曙光（Horizon Zero Dawn）水体渲染思路。

## 三、目录规范

```
Assets/
  Art/       美术资源（Models / Textures / Materials / Shaders / StylePacks）
  Audio/     音乐与音效资源
  Prefabs/   可复用预制体
  Animation/ 动画资源
  Scripts/   脚本（Presentation 表现层 / Logic 逻辑层 / Data 数据层）
  Scenes/    场景
  Editor/    编辑器扩展（UnityBridge MCP 桥等）
  Settings/  URP 管线资产等
Docs/        MD 文档（版本计划、素材库、外接组件、学习路径与知识库）
```

## 四、MCP 连接方式

1. 打开本项目（Unity 2022.3.57f1c2），`Assets/Editor/UnityBridge/` 会自动启动
   WebSocket 桥，监听 `ws://127.0.0.1:6400`。
2. MCP 服务器：`node "C:\Users\18689\.vscode\extensions\gamedevtoi.gdt-unity-mcp-server-0.1.13\dist\unity-mcp.js"`
3. MCP 工具（unity_ 系列）把 JSON 命令转发给桥，桥在主线程执行后回传结果。

## 五、快速开始

- 打开场景：`Assets/Scenes/Main.unity`
- 水体材质：`Assets/Art/Materials/`（Water_*）
- 水体 Shader：`Assets/Art/Shaders/`（Water_Advanced.shader 等）
- 交互演示：场景内放置「Interaction 发生器」即可在水面产生涟漪与浪尖反馈

详细流程见 `Docs/学习路径与知识库/` 下的系列文档。