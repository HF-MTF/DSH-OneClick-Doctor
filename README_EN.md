# DSH Doctor

[简体中文](README.md) ｜ **English**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/HF-MTF/DSH-Doctor?label=release&color=green)](https://github.com/HF-MTF/DSH-Doctor/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)](https://github.com/HF-MTF/DSH-Doctor)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=dotnet&logoColor=white)](https://github.com/HF-MTF/DSH-Doctor)

> When DeepSeek Harness will not start, plugins fail to load, or the web UI will not open — double-click this. It works out what is wrong, fixes what it safely can, then re-checks and tells you the result.

**[⬇ Download the latest release](https://github.com/HF-MTF/DSH-Doctor/releases/latest)** · Unzip and double-click `DSH一键诊断.exe`. No installation, no configuration, runs from any folder.

A one-click environment diagnostics and auto-repair tool for local **DeepSeek Harness** installations on Windows. Single file, zero third-party dependencies (.NET Framework 4.8 ships with Windows).

## Features

- **~30 checks** across four groups: runtime environment / installation integrity / config & plugins / running state
- **Diagnose → repair → re-check** closed loop — one click in the GUI, or fully unattended from the command line
- **Automatic path detection** — nothing hard-coded; manual override available when detection fails
- **Every repair is verified** (is the file really gone? is the directory really empty? did the registry write stick?), and failures are reported honestly — never faked as success
- **Path-boundary guard on every destructive action** — the target must be strictly inside the expected directory
- Custom dark UI, card-style result list, exportable plain-text report
- Proper exit codes (0 / 1 / 2) for scripting

## Requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 / 11 (64-bit) |
| Runtime | .NET Framework 4.8 (built into Win10 1809+ and Win11) |
| Privileges | Standard user; only the long-path-support repair needs administrator |
| Disk | ~1 MB |

## Quick start

1. Download the zip from [Releases](https://github.com/HF-MTF/DSH-Doctor/releases/latest)
2. Unzip anywhere and double-click `DSH一键诊断.exe`
3. A diagnosis starts automatically. If problems are found, click the 一键修复 (Fix All) button — it runs diagnose → fix → re-check and shows you the before/after problem count

### Build from source

Requires .NET Framework 4.8 and its bundled `csc.exe` — no NuGet, no project file, no solution:

```bat
双击  一键编译.bat
```

This compiles `DSH一键诊断.exe`, auto-detects your DSH install directory and copies itself there, then runs a self-check.

## Command line

| Command | Description |
|---|---|
| `DSH一键诊断.exe` | Open the GUI and diagnose immediately |
| `DSH一键诊断.exe --autofix` | Open the GUI and run the full diagnose → fix → re-check loop |
| `DSH一键诊断.exe --auto` | Headless; writes `DSH诊断报告.txt` next to the exe |
| `DSH一键诊断.exe --auto --fix` | Headless diagnose + fix + re-check |
| `--root <path>` | Specify the DSH install directory (skips auto-detection) |
| `--home <path>` | Specify DSH_HOME |
| `--node <path>` | Specify node.exe |
| `--dsh <path>` | Specify the dsh bin.js |
| `DSH一键诊断.exe --deploy` | Auto-detect the DSH directory and copy itself there |

Exit codes: `0` all pass · `1` warnings · `2` errors.

## What it checks

Four groups — runtime environment, installation integrity, config & plugins, running state. The full list, plus every automatic repair it can perform, is documented in the [Chinese README](README.md).

## Notes

- **The GUI and the generated report are in Chinese.** The command line and exit codes work regardless of system locale.
- It needs the DSH install directory — the one containing `dsh`, `node` and `home`. If auto-detection misses yours, pass `--root "D:\YourPath"`.
- **Antivirus false positives are expected** for an unsigned single-file .NET executable. Allow-list it, or skip the binary and build from source.
- The tool makes real changes to your system when you use the repair function. The exact list of destructive operations is in [SECURITY.md](SECURITY.md) — please read it before running a fix.

## Contributing

Issues and pull requests are welcome — please read [CONTRIBUTING.md](CONTRIBUTING.md) first. Two things worth knowing up front: the codebase is limited to **C# 5** syntax (it is built with the .NET Framework `csc.exe`), and all user-facing text is Chinese.

## License

[MIT](LICENSE) © 2026 [HF-MTF](https://github.com/HF-MTF) (HFRin)
