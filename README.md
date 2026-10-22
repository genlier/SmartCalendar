# 📅 SmartCalendar (智能入历)

<p align="center">
  <strong>复制日程文本，自动分析并静默添加至 Outlook 日历</strong><br>
  原生轻量 Windows 后台工具 · 永久免费 AI 驱动 · 待机 0% CPU 占用 · 自动查重防重复
</p>

<p align="center">
  <a href="https://github.com/genlier/SmartCalendar/releases"><img src="https://img.shields.io/github/v/release/genlier/SmartCalendar?color=blue&label=最新版本" alt="Release" /></a>
  <img src="https://img.shields.io/badge/平台-Windows%2010%20%2F%2011-blue?logo=windows" alt="Windows" />
  <img src="https://img.shields.io/badge/AI%20模型-智谱%20GLM--4--Flash%20(免费)-green" alt="GLM-4-Flash" />
  <img src="https://img.shields.io/badge/许可-MIT-orange" alt="License" />
</p>

---

## 💡 它是用来解决什么的？

工作中经常在**微信、飞书、钉钉、邮件或网页**里收到各种日程信息：
> *“明天下午2点在星巴克和张总讨论预算”*  
> *“周五妈妈生日”*  
> *“下周一到周三去北京出差”*  
> *“您的航班 CA1234 将于 10月28日 08:30 起飞...”*

以前你需要：打开日历 → 点新建 → 逐字输入标题 → 选日期 → 设时间 → 填地点。

现在你只需要：**鼠标划选文本按 `Ctrl + C` 复制，搞定。**  
SmartCalendar 会在后台自动唤起 AI 理解文本含义，并将日程**静默存入 Outlook 日历**，并在屏幕右下角弹出轻量气泡通知。

---

## ✨ 核心特性

- 🚀 **100% 后台全静默**：直接存入本地日历，没有多余弹窗，不需要二次确认。
- 🔍 **智能日历查重（防重复录入）**：录入前自动检索日历，若同一天已存在同名日程，自动跳过并轻量提示，即使误按多次复制也不会堆积重复事件。
- 📆 **全天与跨天自适应**：自动识别具体时段（14:00-15:00）或全天事件（生日、出差、节假日、截稿日），全天日程自动显示在顶部横幅，不干扰时间块。
- 🕒 **精准相对时间推算**：以当前真实时分为基准，无论是“2小时后”、“半小时后”还是“明天下午”，都能毫秒级精准对齐。
- ⚡ **真正的 0% CPU 待机**：基于 Windows 原生剪贴板事件驱动，只有在按下复制时才会响应，完全没有后台死循环轮询。
- 🚫 **黑白名单双重过滤**：
  - **白名单关键词**：命中日期或日程关键词才进行 AI 解析；
  - **黑名单关键词**（`ignore_keywords`）：命中如“已取消/账单/密码/退款”等词汇直接前置忽略，杜绝误触。
- 🆓 **永久免费使用**：默认对接智谱 **GLM-4-Flash** 模型，永久免费且响应速度极快（1~2 秒内完成）。
- 🔄 **开机自启动**：托盘右键直接勾选开机自启，开机即用，无感守护。

---

## 🚀 3 步开始使用

### 第 1 步：下载解压

前往 👉 **[Releases 页面](https://github.com/genlier/SmartCalendar/releases/latest)** 下载最新的压缩包：
- **`SmartCalendar-...-standalone.zip`（推荐）**：免安装独立版，任何 Win10/Win11 电脑解压即可运行。

### 第 2 步：填入免费 API Key

1. 访问 [智谱 AI 开放平台](https://open.bigmodel.cn) 免费注册账号。
2. 进入控制台「API Keys」页面复制你的密钥（新老用户调用 GLM-4-Flash 均永久免费）。
3. 打开解压目录下的 `config.json`，把密钥粘贴进去：
   ```json
   {
     "api_key": "这里填入你的API密钥",
     "api_url": "https://open.bigmodel.cn/api/paas/v4/chat/completions",
     "model": "glm-4-flash",
     "calendar_mode": "classic_outlook",
     "reminder_minutes": 15
   }
   ```

### 第 3 步：双击启动

双击运行 **`SmartCalendar.exe`**。
* 屏幕右下角任务栏会出现一个 📅 **日历小图标**；
* 随便复制一句包含日程的话（例如：`明天上午10点开会` 或 `周五妈妈生日`）；
* 屏幕右下角弹出通知：`✅ 日程已静默添加`，日历里已经有该事件了！

---

## ⚙️ 配置文件说明 (`config.json`)

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `api_key` | `""` | 你的大模型 API 密钥（推荐智谱开放平台免费申请） |
| `api_url` | `https://open.bigmodel.cn/...` | OpenAI 格式的 Chat Completions 接口地址 |
| `model` | `"glm-4-flash"` | 模型名称（推荐 `glm-4-flash`） |
| `calendar_mode` | `"classic_outlook"` | 日历写入模式：`classic_outlook` (全静默写入) / `new_outlook` (卡片唤起) |
| `reminder_minutes`| `15` | 日历提前提醒时间（分钟） |
| `min_text_length` | `4` | 过滤剪贴板最小字数 |
| `max_text_length` | `2000` | 过滤剪贴板最大字数 |
| `keywords` | `[...]` | **白名单关键词**：包含这些词或日期模式才触发解析 |
| `ignore_keywords` | `["验证码", "已取消", "账单"...]` | **黑名单关键词**：只要命中其中任意词汇，立即跳过绝不解析 |

---

## 🖱️ 快捷功能与操作

右键点击右下角的 📅 托盘图标：
- **✓ 开机自启动**：勾选后电脑开机自动在后台静默运行。
- **⏸️ 暂停监听 / ▶️ 恢复监听**：需要频繁复制大量文本时可临时暂停。
- **⚙️ 打开配置文件**：快速调出 `config.json` 修改设置。
- **🔄 重新加载配置**：改完配置点此刷新，无需重启软件。
- **📝 查看运行日志**：查看历史识别记录与录入结果。

---

## ❓ 常见问题

<details>
<summary><strong>Q: 它是怎么做到不泄露隐私的？</strong></summary>
程序内置了本地黑白名单双重预过滤：只有当复制的文本同时满足「包含时间/日期/日程词汇」且「不含黑名单词汇」时才会触发 AI 解析；平常复制的代码、密码、长篇文章或普通对话全部在本地直接静默忽略，绝不发起任何网络请求。
</details>

<details>
<summary><strong>Q: 经典 Outlook 和新版 Outlook 都能用吗？</strong></summary>
都可以。默认使用的是 <code>classic_outlook</code> 模式（经典版完全静默写入）。如果你电脑上只用微软最新的 New Outlook，可以在 <code>config.json</code> 中把 <code>calendar_mode</code> 改为 <code>new_outlook</code>，识别后会自动调起新版 Outlook 日程卡片一键保存。
</details>

<details>
<summary><strong>Q: 能换成 DeepSeek 或其他大模型吗？</strong></summary>
可以。程序接口完全遵循标准 OpenAI 协议，只需在 <code>config.json</code> 中将 <code>api_url</code> 和 <code>model</code> 改为你所用平台的地址与模型名称即可。
</details>

---

## 📄 开源许可

本项目基于 [MIT License](LICENSE) 开源，欢迎提交 Issue 或 Pull Request！
