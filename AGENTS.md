# 开发约定

- 平台为 Windows 10 22H2 专业工作站版，使用 PowerShell 7。
- 每个独立改动完成后提交一次，并在项目根目录 `walkthrough.md` 记录改动、思路、验证结果及带 UTC 偏移的时间戳。
- 提交身份使用 `A-BenLi06 <ben_li06@outlook.com>`，不要使用 Codex。
- 避免硬编码语言和字段，优先沿用项目资源、协议及既有架构。不要把开发要求写进产品界面，不要用 hack 掩盖逻辑问题。
- 从 `origin/main` 建立短期分支，自动化分支默认 `codex/`。遵循 `CONTRIBUTING.md`，通过 PR 和必需 CI 合并。
- 完整 Windows 8.1 XAML 构建依赖旧版 SDK；云端 CLR 回归不代表原生运行验证。CI 不调用会停止、部署、启动客户端的 `tools/desktop/run.ps1`。
- 正式发布遵循 `docs/releasing.md`，不得使用已入库的临时私钥。没有明确发布版本的请求时，只搭建或验证发布流程，不创建公开 Release。
