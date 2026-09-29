# DSH 一键诊断

给 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 用的 Windows 自检工具 —— **DSH 起不来、报错看不懂、怀疑插件冲突的时候，跑它。**

单个 exe，纯 .NET Framework，无第三方依赖。与「DSH 启动器」是两个**互相独立**的项目：不引用启动器任何代码，只读取同一个安装目录和 `DSH_HOME`，启动器不在也能用。

![诊断界面](docs/doctor.png)

发现问题后点「一键修复」，它会自己跑完闭环 —— 下面是同一个窗口复检后的样子（警告 1 → 0）：

![修复后](docs/doctor-fixed.png)

## 检查项

### 一、运行环境（是否兼容 DSH 运行）

| 检查 | 判定标准 |
|---|---|
| 操作系统 | 需要 Windows 10 1809+（build 17763），推荐 Win11 |
| CPU 架构 | x64 通过；ARM64 提示原生模块可能缺预编译包 |
| .NET Framework | 需要 4.8 及以上 |
| Node 运行时 | 需要 20+，推荐 22+（PTC 运行时用到的 `stripTypeScriptTypes` 要求 22） |
| WebView2 运行时 | 启动器的独立页面窗口需要 |
| PowerShell | DSH 的终端 / pwsh 工具需要 |
| 物理内存 | 推荐 8 GB 以上 |
| 磁盘空间 | 建议 5 GB 以上 |
| 文件系统 | 必须 NTFS（npm 安装 node_modules 依赖硬链接/符号链接） |
| 长路径支持 | `LongPathsEnabled`，未开启时深层路径会撞 260 字符限制 |
| 系统编码 | 报告当前 ANSI 代码页，中文路径乱码时看这里 |
| 系统代理 | 报告代理设置，提示本地回环可能被拦 |

### 二、安装完整性

DSH 主程序（`bin.js`）、核心依赖包（`dsh` / `dsh-app-boot` / `dsh-base` / `cordis`）、`dsh --version` 启动自检、安装目录与 `DSH_HOME` 可写性、`DSH_HOME` 环境变量是否与启动器一致、npm 脚本策略（npm 12 默认拦截未授权的 install script）

### 三、配置与插件

profile 清单（`package.json`）、用户 patch（`cordis.patch.yml`）完整性、兼容性豁免文件、**插件兼容性 —— 真的跑一次 `dsh --profile web --dump-config`，抓出被跳过的 bundle**、配置解析、启动器 WebView2 组件

### 四、运行状态

3080 端口被谁占用、node 进程数、**多实例检测**（3081~3090 有没有第二个 DSH）、残留的 `runtime.json`、**启动日志错误特征分析**（EADDRINUSE / EPIPE / MODULE_NOT_FOUND / YAMLException / 401 / 堆内存不足 …）、缓存体积

## 能自动修的

界面上点「一键修复」批量处理，或双击某一行单独查看：

- 清掉指向已死进程的残留 `runtime.json`
- 清空超大的启动日志（共享读取 + 截断，不会破坏 DSH 正在持有的写入句柄）
- 清理过大的页面窗口缓存 / npm 缓存
- profile 清单或 patch 损坏 → 从同名 `.bak*` 里挑最新的恢复
- 插件因版本不兼容被跳过 → 在 `compatibility.json` 里写**精确版本豁免**（只动豁免文件，不改 profile，删掉那行即撤销）
- 核心包缺失或 `dsh --version` 失败 → **跑一次 `npm install --prefix dsh`** 按 `package.json` 的精确版本补齐依赖
- `DSH_HOME` 与启动器用的路径不一致 → 改为启动器使用的路径（用户级环境变量）
- 启动器缺 WebView2 组件 → 从 `launcher-src/lib` 补齐
- 长路径未启用 → 尝试写注册表启用（需要管理员权限）

有些问题工具**修不了但能指路**，比如端口被别的程序占了、Node 版本过低、DSH_HOME 指向了别处 —— 这些会明确告诉你是哪一项、当前值是什么、该怎么做。

### 修复的保守原则

- 动任何配置文件之前先备份（会留 `.bak-diag-时间戳`）
- 优先用**可逆**手段：插件不兼容写豁免而不是删插件；日志过大用截断而不是删除
- 不碰系统级设置（唯一例外是「长路径支持」开关，且需要管理员权限才会成功）
- 修完**立刻复检**，把「问题数：修复前 → 修复后」写进结果，不用你自己猜有没有修好

## 用法

### 闭环

「一键修复」不是只执行动作，而是跑完整闭环：

**诊断 → 执行所有可自动修复项 → 重新诊断 → 给出前后对比**

命令行 `--auto --fix` 同理，完全无人值守（退出码即为复检后的结论）。有些问题工具不修（端口被别的程序占、Node 版本过低、文件被占用等），但会明确告诉你是哪一项、当前值是什么、该怎么做。

### 界面

深色自绘窗口（无系统标题栏、圆角、Win11 原生窗口动画），检查结果是卡片式列表：彩色状态点 + 状态胶囊 + 说明，点一行看详情。

双击 `DSH一键诊断.exe` —— 打开即自动诊断。列表里颜色区分：绿色通过、灰色信息、黄色警告、红色错误。双击某行看详情，右上角「导出报告」把结果写成 txt（落在桌面），可以直接贴进 issue。

### 命令行

| 命令 | 行为 |
|---|---|
| `DSH一键诊断.exe` | 打开界面并自动诊断 |
| `DSH一键诊断.exe --autofix` | 打开界面，直接跑「诊断 → 修复 → 复检」闭环 |
| `--root <路径>` | 指定 DSH 安装目录（跳过自动探测）|
| `--home <路径>` | 指定 DSH_HOME |
| `--node <路径>` | 指定 node.exe |
| `--dsh <路径>` | 指定 dsh 的 bin.js |
| `DSH一键诊断.exe --auto` | 无界面诊断，报告写到 exe 同目录的 `DSH诊断报告.txt` |
| `DSH一键诊断.exe --auto --fix` | 无界面诊断 + 自动修复 |

退出码：`0` 全通过 / `1` 有警告 / `2` 有错误 —— 方便在批处理里判断。

## 编译

需要 .NET Framework 自带的 `csc.exe`：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

`build.ps1` 会编译到 `build\`、部署到 `E:\DeepSeekHarness\DSH一键诊断.exe`，再跑一次 `--auto` 自检确认可用。

## 通用性：路径全靠自动探测

这是个通用工具，**不假设你装在哪儿**。每次启动按下面的顺序探测，并把结果作为第一项检查显示出来：

**安装目录**（含 `dsh` / `node` / `home` 的那一层）

1. 命令行 `--root <路径>`
2. 程序自身所在目录，以及它的上级目录
3. 环境变量 `DSH_ROOT` / `DSH_INSTALL` / `DSH_DIR`
4. 由 `DSH_HOME` 反推
5. 常见位置：`?:DeepSeekHarness`、`?:DSH`、`?:Program FilesDeepSeekHarness`、`%LOCALAPPDATA%DeepSeekHarness`、`%USERPROFILE%DeepSeekHarness`（盘符 C~G 都扫）
6. 扫各盘根目录，找名字里含 `dsh` / `harness` 且结构像安装目录的

**数据目录 DSH_HOME**：环境变量 `DSH_HOME` → `<安装目录>\home` → `%USERPROFILE%\.dsh`

**Node**：`--node` → `<安装目录>\node\node.exe` → `PATH` 里的 node → `%ProgramFiles%\nodejs\node.exe`

**DSH 主程序**：`--dsh` → `<安装目录>\dsh\node_modules\@deepseek-ai\dsh\lib\bin.js` → npm 全局安装目录 → npx 缓存

界面上点 **「更改目录」** 可以随时手动指定，程序会重新探测并立刻重跑一次诊断。探测来源也会写进报告，方便排查"为什么没找到"。

## 目录结构

```
doctor-src/
├─ DshDoctor.cs     诊断工具全部逻辑（检查 + 修复 + 界面 + 报告）
├─ app.manifest     DPI 感知 / 通用控件 v6
├─ app.ico          程序图标
├─ build.ps1        一键编译 + 部署 + 自检
└─ docs/            README 配图
```

## License

MIT
