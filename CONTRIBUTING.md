# 参与开发 / Contributing

感谢你愿意改进这个项目。本文说明本仓库的硬性约束、验证方式和提交习惯。

## 先读这两条

1. **目标设备是 iPhone 4S / iOS 6.1.3 Safari。** 页面不是"现代 Web"，是刻意的 ES5 兼容层。
2. **本仓库不含编译产物。** `dist/`、`*.ini`、日志和各种缓存都被忽略，提交时不要强行加进去。

## 环境要求

| 用途 | 需要什么 |
| --- | --- |
| 构建 exe | Windows + 自带 .NET Framework 4.x（无需 VS / .NET SDK） |
| ES5 语法校验 | `node`（+ 随仓库提供的 `tools/acorn.js`） |
| 渲染截图 | `python` + Microsoft Edge |

## 前端硬性约束

`web/index.html` 面向 iOS 6.1.3，**禁止**使用：

- ES6：`let` / `const` / 箭头函数 / 模板字符串 / `class` / `Promise` / `fetch` / `for...of`
- CSS：flexbox、grid、CSS 变量（`var(--x)`）、`matchMedia`
- 外部资源：任何 CDN、网络字体、外部 JS/CSS

必须使用：ES5 + `XMLHttpRequest` 每秒轮询、`-webkit-linear-gradient`、内联 SVG、绝对定位、
`touchstart/touchmove/touchend`。

**这些不是风格偏好，是为了在真机上真的能跑。** 违反后页面会白屏或错位，而你在桌面浏览器里
可能完全看不出问题。

改完前端**必须**跑：

```powershell
python tools\es5_check.py
```

该脚本用 `acorn` 以 `ecmaVersion: 5` 严格解析 `web/index.html` 中全部内联 `<script>`
（目前两个块）。输出 `ES5 parse OK` 才算过。

> 为什么不用 esprima：`esprima@4` 会忽略 `ecmaVersion: 5` 而按 ES2017 解析，
> 箭头函数和 `const` 都能通过，属于假阳性。所以本项目改用 `acorn`。

## 布局改动的验证方法

`layoutGauges()` 是按**实测尺寸**动态算仪表圆环的，不是固定 CSS 尺寸，所以改布局不能只靠肉眼。

1. 起隔离实例：

```powershell
.\dist\LanPerfMonTest.exe --port 18690 --local --minimized
```

2. 截图覆盖 iPhone 4S 两个方向：

```powershell
python tools\cdp_shot.py http://127.0.0.1:18690/ shot-320.png 320 480
python tools\cdp_shot.py http://127.0.0.1:18690/ shot-480.png 480 320
```

3. 主窗口「布局」按钮三种模式（自动 / 横屏 / 竖屏）各截一次。

**验收标准**：卡片内元素不得溢出卡片边界；数值不得溢出圆环；顶栏主机名与方向按钮不得重叠。

历史教训：曾出现「480×320 强制竖屏时卡片仅 59px，仪表标签溢出 3px」和
「宽屏下方向按钮压住主机名 8px」。两者都只能靠量出来的尺寸发现，截图对比不够。

## 后端改动注意事项

- 传感器一律走 `LibreHardwareMonitorLib`，**不要**为某一家 GPU 直接引 IGCL/DLL
  （历史上 README 声称有 IGCL 直读，实际代码从未实现，已更正）。
- PresentMon 子进程必须挂进 Job 对象（`ChildProcessJob`），否则主进程被强杀会留下孤儿进程。
- 调 `netsh` / `schtasks` 这类控制台工具时，输出编码用 `Native.GetOEMCP()` 的系统代码页。
  早先按 UTF-8 解码导致中文/英文关键字都匹配不上、逻辑静默走错分支。
- 新增或修改 `/api/*` 路由时，不要扩大无鉴权的暴露面；`/api/debug` 已用进程级令牌保护。

## 提交前检查清单

- [ ] `build.ps1` 退出码 0，`dist\LanPerfMon.exe` 与 `dist\LanPerfMonTest.exe` 均生成
- [ ] `python tools\es5_check.py` 输出 `ES5 parse OK`
- [ ] 起测试版实例，`/api/health`、`/api/stats` 返回 200
- [ ] 改了前端：320×480 与 480×320 截图无压边/溢出
- [ ] `git status` 里没有 `dist/`、`*.ini`、`tools/edge-shot-profile/`、`*.log`
- [ ] 提交信息说明**改了什么、为什么**（不只是"修复 bug"）

## 提交信息风格

参考现有历史，使用 `type: 一句话概括` + 正文说明根因与验证方式：

```
fix: 托盘图标在 Explorer 重启后自动恢复；/api/debug 加诊断令牌；修复布局溢出与顶栏重叠

托盘：新增 WM_TASKBARCREATED 处理。此前完全没有该消息处理，Explorer 重启后
图标永久消失……
```

`type` 取值：`feat` / `fix` / `chore` / `docs` / `refactor`。

## 换行符

工作区统一 **LF**，由 `.gitattributes` 钉住 `eol=lf`。不要改成 CRLF，否则整个文件都会
显示为伪差异。

## 报告问题

请到 [Issues](https://github.com/lyxc6/Lan-Performance-Monitor/issues) 提交，尽量附上：

- Windows 版本、CPU/GPU 型号、是否安装 PawnIO
- 现象与预期
- `%TEMP%\LanPerfMon\LanPerfMon.log` 的相关片段（可用主窗口「复制诊断令牌」拿到完整
  `/api/debug` 输出）

## 许可

本仓库目前**尚未声明开源许可证**（默认保留所有权利）。在你提交较大改动前，建议先开一个
Issue 说明意图，避免贡献的代码因授权不明而无法合入。

仓库内使用的第三方组件（LibreHardwareMonitorLib、PresentMon 等）的许可与归属见
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)，不受本项目自身许可证影响。
