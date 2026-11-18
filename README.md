# LAN Perf Mon · 局域网性能监控

一个给 Windows 用的单文件性能监控程序：电脑端运行 `LanPerfMon.exe`，手机/平板在同一 Wi-Fi 下用浏览器打开局域网地址，即可实时查看 CPU、GPU、内存、FPS 和系统音量。

**目标设备：** iPhone 4S / iOS 6.1.3 Safari（同时兼容现代手机和桌面浏览器）

![监控页面预览](preview.png)

横屏预览：

![横屏预览](preview-landscape.png)

## 目录

- **只想用起来** → [快速使用](#2-快速使用)：先拿 exe，再双击，手机开浏览器访问
- **想改代码** → [单文件与构建](#6-单文件与构建) 和 [CONTRIBUTING.md](CONTRIBUTING.md)
- **遇到问题** → [常见问题](#7-常见问题)；托盘图标消失、布局乱掉各有专节
- **想接自动化** → 见 [HTTP 接口](#9-http-接口)
- **想知道能不能用** → 见 [许可](#许可)：**专有许可，可自用、不可再分发或商用**

---

## 1. 功能

| 项目 | 内容 |
| --- | --- |
| CPU | 5 个开口朝下的 3/4 圆仪表：占用率、温度、功耗、频率、电压；数值在圆心 |
| GPU | 5 个开口朝下的 3/4 圆仪表：占用率、温度、功耗、频率、显存占用 |
| 内存 | “内存”后跟 SMBios 读取的内存频率（如 3200MHz）+ 大号百分比 + 横向占用条 + 区块内颜色填充（绿/橙/红随占用变化）+ 加粗放大的已用/总计 |
| FPS | 平均帧大号显示 + **1% low（小一号，位于平均帧右侧同一行）** + 平均帧滚动心电图 |
| 音量 | 页面最右侧竖直滑动条，带 0–100 数值；支持静音/取消静音 |
| 时间 | 顶栏时钟加大字号，方便 iPhone 4S 小屏查看 |
| 网络 | 绑定局域网，端口可改；自动添加 Windows 防火墙入站规则 |
| 托盘 | 关闭窗口后留在托盘；可复制地址、打开页面、开机自启、退出 |
| 单文件 | 所有托管依赖、网页、图标、PresentMon 均嵌入一个 exe |

当前验证环境（编写本 README 时实测）：

- Windows 11 专业版（10.0.26300）
- Intel Core i5-12600KF
- NVIDIA GeForce RTX 3080（驱动 32.0.16.1062）
- 4 × 16 GB DDR4-3200（Micron）
- .NET Framework 4.8（4.8.09221）
- PresentMon 2.6.0（FPS，随 exe 内嵌）
- **未安装 PawnIO** — 因此 CPU 温度/功耗/电压显示 `--`，其余项正常

> 历史验证环境（早期在另一台机器上验证过，本仓库当前测试范围不含该配置）：
> Windows 11 10.0.26100、Intel Arc B580 Graphics（驱动 32.0.101.8974）、PawnIO 2.2、
> Intel Graphics Software。
>
> 需要说明的是：**GPU 数据在各版本中均由 LibreHardwareMonitorLib 读取**，
> 代码中没有任何 IGCL / `ControlLib.dll` 直读实现（见第 4 节）。

---

## 2. 快速使用

### 先拿到 LanPerfMon.exe

本仓库**只放源码，不放编译好的 exe**（`dist/` 被 `.gitignore` 排除）。二选一：

**方式 A：直接用 Releases 里的 exe（推荐给使用者）**

到 **[Releases](https://github.com/lyxc6/Lan-Performance-Monitor/releases)** 页面下载最新一版的
`LanPerfMon.exe`，放到任意目录即可运行，不需要装 .NET SDK 或 Visual Studio。

> 如果没有可用的 Release，用下面的方式 B 自己构建。

**方式 B：自己构建（需要开发环境）**

```powershell
git clone https://github.com/lyxc6/Lan-Performance-Monitor.git
cd Lan-Performance-Monitor
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

产物在 `dist\LanPerfMon.exe`。构建只需要 Windows 自带的 .NET Framework 4.x，
不需要额外安装任何 SDK；详见 [第 6 节](#6-单文件与构建)。

### 运行

1. 双击 `LanPerfMon.exe`。
2. Windows 会弹出 UAC，请选择"是"。  
   **必须允许管理员权限**，否则 CPU 温度、功耗、电压和 FPS 会显示 `--`。
3. 主窗口会显示类似：
   `http://192.168.1.43:8686/`
4. 让手机连接**同一个 Wi-Fi**，在浏览器地址栏输入上面的地址。
5. 页面右侧的竖直滑条可以直接拖动调节电脑当前默认播放设备的音量；点"静音"切换静音。

关闭主窗口不会退出程序，而是缩到右下角托盘。要完全退出：右键托盘图标 → "退出"。

> 这个地址是**你本机**的局域网 IP，每台机器都不同，以主窗口显示为准。

### 想快速确认它能用？

浏览器打开 `http://<你的IP>:8686/api/health`，返回 `{"ok":true}` 即服务正常。

### 横屏 / 竖屏布局（真机识别失效时用）

主窗口底部有「布局:自动 / 布局:横屏 / 布局:竖屏」按钮，点一下循环切换：

- **自动**：网页按手机实际方向自动排布（默认）。
- **横屏**：强制手机页面使用横屏布局（CPU/GPU 标题居左竖排、仪表满高），即使手机竖着拿。
- **竖屏**：强制使用竖屏堆叠布局。

选择会立即下发给手机页面（网页每秒轮询 `/api/stats` 自动应用，无需刷新），并保存到 `LanPerfMon.ini` 的 `layout=`。

> iPhone 4S / iOS 6.1.3 的 Safari 对 `orientation` 媒体查询和旋转事件的识别不可靠，实测可能一直按横屏显示。若自动模式不对，直接用这个按钮强制成想要的方向即可。

### 修改端口

在主窗口“端口”输入框填入 1024–65535 之间的端口，点“应用端口”。程序会：

- 重启 HTTP 服务；
- 自动在 Windows 防火墙放行新端口；
- 保存到 exe 同目录的 `LanPerfMon.ini`（若目录不可写则保存到 `%APPDATA%\LanPerfMon\LanPerfMon.ini`）。

也可以命令行指定：

```bat
LanPerfMon.exe --port 9000
```

完整命令行参数：

| 参数 | 作用 |
| --- | --- |
| `--port 9000` / `--port=9000` | 指定监听端口（1–65535，越界会被忽略并回退到 ini 里的值） |
| `--minimized` / `-min` | 启动后直接缩到托盘，不显示主窗口 |
| `--local` | 只监听 `127.0.0.1`，不接受局域网访问（仅本机调试用） |
| `--no-fps` | 不启动 PresentMon（不需要 FPS 时可减少开销与权限依赖） |
| `--no-firewall` | 不尝试添加防火墙规则 |

### 开机自启

勾选主窗口或托盘菜单里的“开机自动启动”。程序会创建一个名为 `LanPerfMon` 的 Windows 计划任务，登录时以最高权限启动，避免每次登录都弹 UAC。

注意：复选框的勾选状态会同时写入 `LanPerfMon.ini` 的 `autostart=`。若计划任务被外部删除
（例如手动 `schtasks /delete`、系统还原、或换到新机器），ini 里可能仍是 `autostart=1`
但实际不会自启；此时取消勾选再重新勾选一次即可重建任务。
任务是否存在可用 `schtasks /query /tn LanPerfMon` 确认。

### 托盘图标为什么会“消失”，以及如何恢复

托盘图标依赖 Explorer 的通知区域。以下情况图标会丢失：

- **登录时程序比 Explorer 先启动**：`Shell_NotifyIcon(NIM_ADD)` 找不到通知区域而失败；
- **Explorer 崩溃或被重启**：所有托盘图标都被销毁，程序必须重新添加。

程序对这两条都做了处理：

1. 启动后 3 / 10 / 30 / 60 秒各重注册一次，覆盖“启动跑在 Explorer 前面”的情况；
2. 监听 Explorer 广播的 **`WM_TASKBARCREATED`** 消息，收到后立即重注册 —— Explorer 重启后
   图标会自动回来，**不需要重启本程序**。

若图标仍然不见了，先确认程序还在跑（任务管理器里应有 `LanPerfMon.exe`），
然后从托盘菜单或主窗口操作；实在找不到就重启程序。

### 诊断令牌（/api/debug）

`/api/debug` 会暴露日志绝对路径、令牌完整性级别、会话号等机器级信息，因此**需要令牌**才能读取完整内容。
令牌在每次进程启动时随机生成，只在本机可见：

- 主窗口的 **“复制诊断令牌”** 按钮；
- 托盘菜单的 **“复制诊断令牌”**。

点一下会复制一个带令牌的完整地址，直接粘到浏览器即可。也支持命令行方式：

```powershell
# 从主窗口拿到令牌后
curl.exe "http://127.0.0.1:8686/api/debug?token=<令牌>"
curl.exe -H "X-Debug-Token: <令牌>" "http://127.0.0.1:8686/api/debug"
```

不带令牌时返回 `"locked": true`，路径会被裁剪成最后一级（如 `…\LanPerfMon.log`），
且**不会真正执行**写文件/托盘探测；带令牌时返回完整字段。令牌不会出现在任何响应体里，
所以同一局域网内的其他人无法自己取得它。

---

## 3. iPhone 4S / iOS 6.1.3 兼容说明

页面是按 iOS 6 Safari 的能力集写的，不使用以下新特性：

- 不使用 ES6（`let` / `const` / 箭头函数 / Promise / fetch / 模板字符串）
- 不使用 CSS Flexbox / Grid / CSS 变量
- 不使用外部字体、外部 JS/CSS、CDN
- 使用 ES5 + `XMLHttpRequest` 每秒轮询
- 使用 `-webkit-linear-gradient`、`border-radius`、内联 SVG、`position:absolute/fixed`
- 自定义触摸滑条：`touchstart` / `touchmove` / `touchend`，不依赖 iOS 6 不支持的竖直 `input[type=range]`

布局适配：

- iPhone 4S 竖屏 320×480
- Safari 工具栏占用后的 320×372 可视区域会自动进入紧凑布局
- iPhone 4S 横屏 480×320：CPU/GPU 卡片的标题（标签+型号）移至**最左侧竖排**，不再占用顶部空间，仪表区占满整卡高度；FPS/RAM 重新分配高度，文字不重叠
- **横竖屏切换不再依赖 `@media (orientation)`**（iOS 6.1.3 Safari 对该特性识别不可靠，会错误地一直按横屏渲染）；改为 `<head>` 内联脚本根据视口宽高比 + `window.orientation` 计算并切换 `<html class="land">`，CSS 横屏规则全部挂在 `.land` 下
- **exe 主窗口提供「布局」按钮**（自动 → 横屏 → 竖屏 → 自动）：真机旋转识别失效时，点一下按钮即可强制手机页面切换布局。选择会写入 `LanPerfMon.ini` 的 `layout=`，并通过 `/api/stats` 的 `sys.layout` 下发给所有网页客户端，网页每秒轮询时自动应用
- 3/4 圆仪表使用内联 SVG 绘制，开口朝下，数值在圆心；描边为红→粉→紫→蓝渐变，进度弧带发光层，温度单位 ° 上标显示
- 页面最高只占一屏，不出现上下滚动
- 支持“添加到主屏幕”，已提供 `apple-touch-icon`

> 说明：开发环境无法连接真实 iPhone 4S，已使用 iOS 6.1.3 Safari User-Agent 在 320×480、320×372 和 480×320 视口下完成渲染验证；页面没有使用 iOS 6 不支持的前端 API。真机建议再实际打开确认一次。

---

## 4. 数据来源与限制

### CPU / GPU / 内存

通过 **LibreHardwareMonitorLib 0.9.6** 读取：

- CPU 占用：LHM `CPU Total`
- CPU 频率：LHM 核心 Clock；无权限时回退 `CallNtPowerInformation`
- CPU 电压/功耗/温度：LHM（需要管理员 + PawnIO/WinRing0 驱动）
- GPU：LHM 的 GPU 传感器（Intel / NVIDIA / AMD 通用），读取负载、频率、功耗、温度、显存
- 内存：LHM，失败时回退 `GlobalMemoryStatusEx`
- 内存频率：LibreHardwareMonitor 的 `SMBios.MemoryDevices`（优先 `ConfiguredSpeed`，否则额定 `Speed`）

> 早期文档曾写 GPU 走 Intel IGCL（`ControlLib.dll`）直读 Arc 显卡。
> 当前源码中不存在该实现（`IGCL` / `ControlLib` 在 `src/` 中零匹配），
> 所有 GPU 读数均来自 LibreHardwareMonitorLib，因此 NVIDIA / AMD 显卡同样可用。

如果 CPU 温度/功耗/电压显示 `--`：

1. 确认 exe 是以管理员身份运行；
2. 确认 PawnIO 已安装（默认安装于 `C:\Program Files\PawnIO`；不在该目录即视为未安装）；
3. 某些 Windows 11 版本会拦截 WinRing0 类驱动，PawnIO 是更稳妥的方案；
4. GPU 驱动更新后，LHM 的传感器通常也会恢复。

### FPS

内嵌 **PresentMon 2.6.0**（Intel 开源，MIT）：

- 程序把 `PresentMon-2.6.0.exe` 解压到 `%TEMP%\LanPerfMon\` 并以管理员权限运行；
- 通过 ETW 捕获 D3D/DXGI/Vulkan 的 Present 事件；
- 自动选择 FPS 数据源，依次尝试：
  1. 当前前台窗口进程（最近 5 秒内有帧）；
  2. 最近 5 秒内有帧、且窗口接近全屏的 3D 程序；
  3. 兜底：任何最近 5 秒内有帧且未被过滤的非系统进程；
- 浏览器、桌面、终端、Electron 壳等非游戏进程会被过滤；
- 没有游戏画面时显示“等待画面”，不会显示桌面动画的假 FPS；
- **1% low**：最近约 30 秒 / 最多 2400 帧中，取最慢 1% 帧时间的平均值换算成 FPS；
- **平均帧心电图**：页面保留最近 36 个采样点，用滚动折线绘制当前平均帧趋势。

如果 FPS 一直 `--`：

1. 确认游戏正在运行，且处于前台或全屏；
2. 确认程序以管理员身份运行（ETW 需要）；
3. 某些杀毒软件会阻止 ETW/PresentMon，加入白名单；
4. 同时运行 RTSS、Afterburner 等捕获工具可能冲突，先退出它们。
5. 若页面显示"需要管理员权限才能采集 FPS"，说明非管理员运行导致
   PresentMon 以退出码 6（ETW 权限不足）退出，右键以管理员身份运行即可。
   PresentMon 的英文原文仍记录在日志中。

### 音量

使用 Windows Core Audio 的 `IAudioEndpointVolume` 控制**当前默认播放设备**的主音量。切换耳机/音箱后，页面下一秒会自动读取新设备音量。

---

## 5. 局域网 / 防火墙 / 安全

- 默认监听 `0.0.0.0:8686`（所有网卡）。
- 首次启动会尝试执行 `netsh advfirewall firewall add rule`，规则名：
  `LanPerfMon TCP <端口>`，方向入站，协议 TCP，配置文件 Domain/Private/Public。
- HTTP 服务只接受**私有地址 / 回环地址**的客户端（10/8、172.16/12、192.168/16、127/8、169.254/16），公网来源会被直接关闭连接。
- 这是**无登录**的局域网工具：同一局域网内知道地址的人都能查看数据并调节音量。请只在可信家庭/办公室网络使用，**不要做端口映射到公网**。
- 如果防火墙规则添加失败，可手动放行：

```bat
netsh advfirewall firewall add rule name="LanPerfMon TCP 8686" dir=in action=allow protocol=TCP localport=8686 profile=any
```

- 如果手机打不开：
  1. 确认手机和电脑在同一 Wi-Fi（不是访客网络/AP 隔离）；
  2. 确认地址是主窗口显示的 `192.168.1.43:端口`；
  3. 关闭再打开 Windows 防火墙的入站规则；
  4. 路由器给电脑设置 DHCP 地址保留，避免 IP 变化。

---

## 6. 单文件与构建

发布文件：

```
dist\LanPerfMon.exe
```

单文件约 **4.18 MB**（4,182,016 字节），内含：

- C# WinForms 托盘程序
- 内嵌网页（HTML/CSS/ES5 JS）
- LibreHardwareMonitorLib 0.9.6 及依赖
- PresentMon 2.6.0
- 应用图标

运行要求：Windows 10/11 x64，.NET Framework 4.8（Windows 10/11 自带）。

源码结构：

```
src/                 C# 源码
web/index.html       单页监控界面（iOS 6 兼容）
assets/app.ico       应用图标
app.manifest         requireAdministrator 清单
build.ps1            csc 一键构建
tools/lhm096/        LHM 依赖（构建时嵌入）
tools/PresentMon.exe 构建时嵌入
tools/acorn.js       ES5 校验器（随仓库提供，离线可用）
tools/cdp_shot.py    按视口截图（Edge headless + CDP），用于渲染验证
tools/es5_check.py   校验 web/index.html 内联脚本为严格 ES5
dist/                输出目录（已在 .gitignore 中，不进版本库）
```

源码按职责分层，主要文件：

| 文件 | 职责 |
| --- | --- |
| `src/Program.cs` | 入口：单实例互斥、装配嵌入依赖解析器 |
| `src/MainForm.cs` | 主窗口与托盘（含 `WM_TASKBARCREATED` 重注册） |
| `src/AppHost.cs` | 装配各子系统、端口与防火墙、布局状态 |
| `src/MonitorService.cs` | LHM 传感器采样（CPU/GPU/内存） |
| `src/FpsMonitor.cs` | PresentMon 子进程管理、帧时间解析、1% low |
| `src/VolumeController.cs` | Core Audio 音量读写与静音 |
| `src/HttpServer.cs` | 手写 TCP HTTP 服务与各 `/api/*` 路由 |
| `src/StatsJson.cs` | 传感器快照的 JSON 序列化 |
| `src/ChildProcessJob.cs` | Job 对象，保证本进程被杀时 PresentMon 一并回收 |
| `src/ResourceLoader.cs` | 从 exe 内嵌资源加载托管程序集/工具 |
| `src/DebugAccess.cs` | `/api/debug` 的令牌校验与路径脱敏 |
| `src/Native.cs` | Win32 P/Invoke（私网判定、编码页、托盘消息等） |

版本库约定：

- 本仓库用 Git 管理，`dist/`、`LanPerfMon.ini`、`tools/edge-shot-profile/`、
  `*.log` 及 `es5_check.py` 的生成物均被忽略。
- 工作区文件统一 LF 换行（`.gitattributes` 钉住 `eol=lf`），避免 Windows 上
  `core.autocrlf` 造成整文件伪差异。
- `tools/es5_check.py` 需要 `node` 可执行文件与 `tools/acorn.js`（已随仓库提供，离线可用）；
  缺失时脚本会明确报错退出，不会静默跳过。
- 用 `acorn` 而非 `esprima` 校验 ES5：`esprima@4` 会忽略 `ecmaVersion: 5` 选项按 ES2017 解析，
  箭头函数与 `const` 都能混过去（假阳性）。

构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建脚本使用系统自带 .NET Framework 4.x 的 `csc.exe`，不需要安装 Visual Studio 或 .NET SDK。会同时生成：

- `dist\LanPerfMon.exe`：正式版，`requireAdministrator`
- `dist\LanPerfMonTest.exe`：测试版，`asInvoker`（仅开发调试用，功能不完整）

> 两个 exe 的 C# 源码完全相同，唯一区别是嵌入的清单（`app.manifest`）申请的执行级别。
> 测试版不需要 UAC，适合验证 HTTP/页面；但因为没有提权，传感器会缺温度/功耗/电压，
> PresentMon 也会以退出码 6 失败（FPS 显示"需要管理员权限才能采集 FPS"）。这是预期行为。

### 开发与自测

改完代码后，建议按下面三档验证，从便宜到贵：

**1. 构建 + ES5 校验（每次改动都跑，秒级）**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
python tools\es5_check.py
```

`es5_check.py` 会把 `web/index.html` 里**两个**内联 `<script>` 全部按严格 ES5 解析。
需要 `node` 与随仓库提供的 `tools/acorn.js`；两者缺失都会明确报错而不是静默跳过。

> 页面目标设备是 iOS 6.1.3 Safari，**不能用 ES6 语法、CSS 变量、flexbox、grid、`matchMedia`**。
> 这条约束靠上面这个脚本兜底，改前端务必跑。

**2. 起一个隔离实例做接口冒烟**

用测试版 + 独立端口，避免撞上你正在用的实例：

```powershell
.\dist\LanPerfMonTest.exe --port 18690 --local --minimized
```

然后逐项确认：

```powershell
curl.exe http://127.0.0.1:18690/api/health    # {"ok":true}
curl.exe http://127.0.0.1:18690/api/stats     # 传感器 JSON
curl.exe http://127.0.0.1:18690/api/config    # 端口与 IP 列表
```

`--local` 只监听回环地址，测完记得杀掉进程。若改了鉴权相关代码，注意
`/api/debug` 不带令牌时应返回 `"locked": true` 且路径被裁剪（见上文"诊断令牌"）。

**3. 页面渲染验证（改了 `web/index.html` 时必做）**

`tools/cdp_shot.py` 用 Edge headless + CDP 按指定视口截图：

```powershell
python tools\cdp_shot.py http://127.0.0.1:18690/ shot-320.png 320 480
python tools\cdp_shot.py http://127.0.0.1:18690/ shot-480.png 480 320
```

至少要覆盖 iPhone 4S 的 **320×480** 与 **480×320**；再各点一次主窗口的「布局」按钮
（自动 / 横屏 / 竖屏）确认三种模式都不出现文字压边或元素溢出卡片。

> 脚本会把临时的 Edge 配置写到 `tools/edge-shot-profile/`（约 39 MB，已加入 `.gitignore`），
> 用完可以直接删掉，下次运行会重建。

需要 `python`、Edge 与网络空闲端口；脚本自带 6 秒等待，用于让页面轮询填充真实数据。

---

## 7. 常见问题

**Q：双击没反应 / 只在托盘里？**  
A：关闭窗口会缩到托盘，这是正常行为。双击托盘图标恢复。

**Q：端口修改后手机还是旧地址？**  
A：手机页面还在轮询旧端口。用新地址重新打开；主窗口的“复制地址”会给出新 URL。

**Q：CPU 频率显示 3.7 GHz，但任务管理器显示更高？**  
A：这是所有核心的实时平均频率；满载/单核睿频时会变化。频率圆环的进度按本次运行观察到的最高频率缩放，圆心数值始终显示当前频率。

**Q：FPS 显示的是浏览器/桌面？**  
A：已过滤浏览器、桌面、终端、Electron 等进程；只显示前台或接近全屏的 3D 程序。若仍有误判，欢迎在 [Issues](https://github.com/lyxc6/Lan-Performance-Monitor/issues) 里附上进程名。

**Q：Windows 提示“未知发布者”/SmartScreen？**  
A：exe 没有购买代码签名证书。可以点“更多信息 → 仍要运行”。硬件传感器驱动也可能触发杀毒误报，需加入信任。

**Q：日志在哪里？**  
A：`%TEMP%\LanPerfMon\LanPerfMon.log`，包含启动、端口、PresentMon 状态，便于排查。

---

## 8. 第三方组件

详见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

- LibreHardwareMonitorLib 0.9.6 — MPL-2.0
- PresentMon 2.6.0 — MIT
- PawnIO — 系统驱动，未随 exe 分发
- HidSharp、System.Memory 等 — 各自开源许可证

## 9. HTTP 接口

网页本身就是调用这些接口工作的，所以你也可以拿来接自己的脚本、状态栏或键盘小屏。
默认端口 `8686`，地址用主窗口显示的那个局域网 IP。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/api/health` | 存活检查，返回 `{"ok":true}` |
| `GET` | `/api/stats` | 传感器快照（CPU/GPU/内存/FPS/音量/系统信息），网页每秒轮询的就是它 |
| `GET` | `/api/volume` | 当前音量与静音状态 |
| `POST` | `/api/volume` | 设置音量：`v=0..100`；切换静音：`mute=toggle` |
| `GET` | `/api/layout` | 当前布局模式 |
| `POST` | `/api/layout` | 设置布局：`mode=auto` / `landscape` / `portrait`（其它值返回 400） |
| `GET` | `/api/config` | 端口、版本、本机 IPv4 列表 |
| `GET` | `/api/debug` | 诊断信息，**需要令牌**（见上文"诊断令牌"） |

几点说明：

- 所有响应都是 `application/json`，`Cache-Control: no-store`（静态页面/图标除外）。
- 服务**只接受私有地址/回环地址**的客户端（10/8、172.16/12、192.168/16、127/8、169.254/16），
  公网来源直接断开连接。
- 这是**无登录**的局域网工具：同网段内知道地址的人都能读数据、调音量、切布局。
  **不要做端口映射到公网。**
- 示例：

```powershell
curl.exe http://192.168.1.43:8686/api/stats
curl.exe -X POST -d "v=30" http://192.168.1.43:8686/api/volume
```

---

## 许可

**本仓库不是开源项目，采用专有许可（保留所有权利）**，全文见 [LICENSE](LICENSE)。

简单说：

| 你可以 | 你不可以（需事先书面许可） |
| --- | --- |
| 查看源码、克隆到本地 | 再分发 exe 或源码（含免费转载、应用商店、硬件捆绑） |
| 自己构建、自己使用 | 分发修改后的版本 |
| 个人/家庭使用；非营利机构内部教学研究 | 任何公司/企业用途，及商业使用、销售、出租 |
| 为自己使用而修改（不对外分发） | 移除版权与第三方许可声明 |
| 写文章评测时引用并注明出处 | 用项目名称暗示官方授权或背书 |

一句话：**可以自用，不要散播，不要商用。**

> 允许每个人自行从本仓库获取和构建，但不允许把副本再分发出去。
> 需要额外授权（再分发、商用）请通过
> [Issues](https://github.com/lyxc6/Lan-Performance-Monitor/issues) 联系作者。

随仓库分发的第三方组件（LibreHardwareMonitorLib、PresentMon 等）遵循各自的许可证，
**独立于**本项目的专有许可，详见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。

本项目仅用于个人/家庭局域网监控，不提供任何硬件损坏或数据准确性担保。
