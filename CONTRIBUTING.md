# 分支与工作流程

`main` 是默认稳定分支。`Navidrome-support` 保留迁移前的历史；新工作从最新的 `origin/main` 开始。

1. `git fetch origin`，从 `origin/main` 建立短期分支。自动化开发默认使用 `codex/<描述>`，人工分支可使用 `feature/`、`fix/` 或 `chore/`。
2. 完成一个独立改动后，将改动、思路和验证结果追加到根目录 `walkthrough.md`，时间戳必须带 UTC 偏移，例如 `2026-11-15 03:00:00 +08:00`。用维护者自己的 Git 身份提交，不能署名 Codex。
3. 推送并向 `main` 提交 PR。PR 说明应包含实际问题、最终行为和验证结果；使用资源及协议定义，不用硬编码或临时 hack 掩盖问题。
4. `CI required` 必须成功，所有审查讨论必须解决。主分支要求最新基线通过 CI，禁止直接推送、强制推送和删除。
5. 推荐 squash merge；也允许 rebase merge。合并后删除短期分支。单维护者仓库不强制另一位维护者审批；有协作者时仍应请求审查。

CI 会在所有分支 push、`main` 的 PR、`v*` 标签和手动触发时运行，不按文件路径跳过必需检查。云端运行桌面及手机版生产源码 CLR 回归、XAML/资源引用检查、工具自测，以及锁定 Navidrome 源码的功能映射审计。它不构建 WinRT AppX，也不认证真实窗口、手势、后台音频或服务器集成行为；这些变更应补做现有原生/集成验收，并在 PR 中说明结果。

本地回归（PowerShell 7、Python 3.13+，先恢复 `Caliburn.Micro.Core` 3.2.0）：

```powershell
./tools/ci/run-desktop.ps1
python tools/ci/check.py
# 已有干净且符合 source-lock.json 的 Navidrome 源码时：
python tools/ci/check.py --source .tmp/navidrome
```

`MIXDROME_CSC` 可指定桌面 C# 编译器。云端使用 Windows 2022 上的 Roslyn，本地兼容 MSBuild 14；完整客户端仍需旧版 SDK。发布规则及完整流程见 [releasing.md](docs/releasing.md)。
