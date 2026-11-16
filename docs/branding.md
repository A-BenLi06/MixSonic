# Mixdrome 品牌与应用身份

应用名称为 **Mixdrome**，维护者为 **A-BenLi06 <ben_li06@outlook.com>**。解决方案为 `Mixdrome.sln`；桌面、手机与后台音频项目分别为 `Client/Mixdrome.csproj`、`Client.Phone/Mixdrome.Phone.csproj` 和 `Client.Phone.BackgroundAudioTask/Mixdrome.Phone.BackgroundAudioTask.csproj`。

| 项目 | 桌面 | Windows Phone |
| --- | --- | --- |
| 包名称 | `A-BenLi06.Mixdrome` | `A-BenLi06.Mixdrome.Phone` |
| 应用入口 | `Mixdrome.App` | `Mixdrome.Phone.App` |
| 发布者显示名 | `A-BenLi06` | `A-BenLi06` |
| 签名证书主体 | `CN=benli06` | `CN=benli06` |

签名主体沿用维护者已有证书。桌面默认生成未签名开发包，正式发布通过 [发布流程](releasing.md) 显式选择证书。项目不使用旧的入库临时 PFX。PhonePublisherId 保留侧载开发所用的空 GUID；商店发布须使用实际商店分配的身份，不伪造发布者 ID。

包身份改变后，系统创建独立的安装与数据目录，已有 Subsonic8/MixRadio 安装、账户、凭据、下载及队列不会自动迁移。新应用须重新配置账户；本次不删除原安装或数据。Subsonic 协议字段、XML 命名空间和接口名称保持协议规定的拼写，服务器请求的客户端标识统一为 `Mixdrome`。

图形采用参考设计的白色耳机笑脸与音符、洋红色主体、粉红/橙/黄色角落点缀。字标使用 Segoe UI，`Mix` 为粗体，`drome` 为细体。磁贴是静态应用品牌资源；播放时的动态磁贴仍显示实际歌曲与封面，不写入参考图的示例曲名或虚假控制按钮。静态磁贴已包含字标，因此关闭系统重复显示的名称。

`tools/branding/brand.json` 集中定义字体、配色、平台尺寸和比例；`generate.py` 从可缩放几何重建 PNG，包内仍使用平台支持的 PNG。桌面资源覆盖 100/140/180%，手机覆盖 100/140/240%。向用户展示的预览为压缩 WebP。

```powershell
python tools/branding/generate.py
```

生成器需要 Pillow 与 Windows 的 Segoe UI 字体，也可用 `--font-directory` 指定字体目录。33 个资源全部使用明确的 scale 限定符；清单通过逻辑文件名选择对应比例，避免无比例副本与 scale-100 同时进入包。预览输出到忽略目录 `.tmp/branding/mixdrome-preview.webp`。界面字标通过现有 PRI 资源解析，两端复用相同的品牌字符串。

构建与回归工具的环境覆盖项改为 `MIXDROME_CSC`、`MIXDROME_MSBUILD`、`MIXDROME_SIGNTOOL`，也可继续使用各脚本的显式路径参数。`docs/mixradio`、`tools/mixradio`、原版图片与恢复的样式键记录历史参考来源，保留原名；许可证与原作者记录同样保留。
