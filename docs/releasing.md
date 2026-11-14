# Windows 8.1 AppX 发布

采用本地旧版 SDK 构建和签名、云端验证、GitHub Releases 发布。当前没有 self-hosted Runner，GitHub Actions 不编译完整 WinRT 客户端。

## 一次性准备

- 本地安装已验证的 MSBuild 14、Windows 8.1 SDK、Player Framework 2.0 和项目 NuGet 依赖。Python 3.13+、PowerShell 7、Git 和已登录的 GitHub CLI 用于发布工具。
- 使用专用代码签名证书，其 Subject 必须匹配 `Client/Package.appxmanifest` 的 Publisher。私钥放在本机 `Cert:\CurrentUser\My`，不要提交到 Git，不要使用项目默认的临时 PFX。自签名证书需在本机构建验证时已受信任；安装者也需自行确认并信任同一个公开证书。
- 导出 DER 格式的公开 `.cer`，计算 SHA-256，并将确认的指纹配置为 GitHub 仓库变量 `RELEASE_CERTIFICATE_SHA256`。这不是私钥或 secret；更换证书需显式更新变量并说明迁移。**当前故意未设置正式证书指纹，配置前 CD 会拒绝发布。**

```powershell
$pin = (Get-FileHash -LiteralPath '<公开证书路径.cer>' -Algorithm SHA256).Hash.ToLowerInvariant()
gh variable set RELEASE_CERTIFICATE_SHA256 --body $pin
```

## 每次发布

1. 在短期分支将清单版本更新为四位 AppX 版本，记录 walkthrough、提交并通过 PR 合并。标签统一为 `v<Major.Minor.Build.Revision>`，须与清单完全相同；同一个版本不可重打或替换安装包。
2. 在干净、已提交的目标源码上补做必要的原生 UI、播放和服务器验收，检查 Release 构建要求。云端 CI 成功并不能证明 AppX 与源码一致；本地构建者负责源码和原生验收的真实性。
3. 使用旧版工具构建并签名，不部署、不启动或停止开发客户端：

```powershell
./tools/release/build.ps1 -CertificateThumbprint '<CurrentUser/My 中的正式证书指纹>' -Architecture AnyCPU
# 可指定 -MSBuildPath、-SignToolPath，或对应 SUBSONIC8_MSBUILD/SUBSONIC8_SIGNTOOL。
# 有可用 RFC 3161 服务时可通过 -TimestampUrl 指定时间戳地址。
```

脚本执行全新 Release 构建、关闭自动递增和诊断探针、关闭默认 PFX 签名，再用指定证书签名。输出在 `.tmp/releases/v<版本>-<架构>`，包含安装归档和 `release-manifest.json`；归档只包含主 AppX、公开证书和所需架构的依赖 AppX。构建日志位于 `.tmp/release-build/<唯一目录>/build.log`。已存在的输出目录不会覆盖；再次构建时先自行保存或移动旧结果。

4. 核对输出、原生验收及指纹后，创建标签和草稿并触发 CD：

```powershell
./tools/release/publish-draft.ps1 -Directory '.tmp/releases/v<版本>-AnyCPU'
```

此命令会推送对应标签、上传 draft Release，并调用 `Publish release` 工作流；调用它即授权本次版本发布。已有 Release 不会覆盖。重新运行失败的 CD：

```powershell
gh workflow run release.yml --ref main -f tag='v<版本>'
```

CD 只从 `main` 运行，在 `github-releases` 环境中依次验证：目标标签不可变化且源码已合并 main；该提交的 push CI 成功；标签、源码清单和安装包版本/Publisher/Name 相同；全部文件清单及 SHA-256 正确；公开证书与仓库指纹匹配；主 AppX 确由该证书签名；主包与全部依赖通过 SignTool 完整性/签名验证。自签名证书只在临时 CI Runner 上按已配置指纹暂时加入 CurrentUser Root，验证后移除。最后重查草稿和资产未变化，才将 draft 发布。失败时保留草稿及日志，不发布、不覆盖已发布版本。

此流程校验分发包和发布来源记录，不提供编译产物的可复现构建证明。目前每个 Release 上传一种主包架构，AnyCPU 为 neutral；需要多架构 bundle 时先扩展构建和依赖验证，再改变流程。

云端临时证书信任使用 Windows `CertOpenStore` 的 `CERT_SYSTEM_STORE_UNPROTECTED_FLAG`，避免默认 CurrentUser Root 写入时弹出的交互确认框。仅在显式指定 `-TrustPinnedCertificate`、证书指纹及 AppX 签名者匹配后启用，成功或失败均撤销本次添加的信任；已有信任不删除。普通本地构建验证仍使用已有证书信任。

## 安装者

从 Release 下载归档及 `release-manifest.json`，核对归档的 SHA-256；公开证书的 SHA-256 应与维护者公布的指纹一致。确认信任后导入公开证书，将归档内适用架构的依赖路径传入系统 `Add-AppxPackage -Path <主包.appx> -DependencyPath <依赖路径数组>`。归档不含会自动修改证书信任或执行其他操作的安装脚本。

## 仓库规则

`main` 通过 PR 合并，必需检查为 GitHub Actions 的 `CI required`，要求分支最新、讨论解决和线性历史；禁止直接推送、强推和删除。`v*` 标签禁止更新和删除，但允许创建新版本。工作流使用固定 action commit、最小 token 权限，回归检查不接触发布凭据。发布环境仅允许 main。

GitHub 配置的版本化来源为 `.github/*-ruleset.json` 和 `.github/repository-settings.json`。维护者在已推送 main 且首次 CI 成功后，可用 `./tools/ci/configure-repository.ps1` 幂等应用这些规则和发布环境策略；配置变更同样应经 PR 审查。
