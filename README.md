# 📅 SmartCalendar (智能入历)

<p align="center">
  <strong>Windows 轻量后台工具：复制文本即自动识别日程并存入 Outlook 日历</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-blue?logo=windows" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-9.0-purple?logo=dotnet" alt=".NET 9.0" />
  <img src="https://img.shields.io/badge/AI%20Model-GLM--4--Flash%20(Free)-green" alt="GLM-4-Flash" />
  <img src="https://img.shields.io/badge/License-MIT-orange" alt="License" />
</p>

---

## ✨ 核心特性

- 📋 **复制即识别**：在任意聊天软件（微信/钉钉/飞书）、网页或邮件中按 `Ctrl + C` 复制日程，自动提取事件标题、日期、时间、地点与备注。
- ⚡ **真正的 0% CPU 占用**：采用 Windows 原生 Win32 `AddClipboardFormatListener` 事件消息机制，**完全摒弃死循环轮询**，平常待机不吃 CPU 与内存。
- 🤖 **完全免费的大模型支持**：内置对接智谱 **GLM-4-Flash**（永久免费），每次识别耗时仅 1~2 秒，零使用成本；同时兼容 DeepSeek、MiMo、OpenAI 等标准接口。
- 📅 **两种日历模式自由切换**：
  - **经典版 Outlook 模式**：通过系统底层 COM 自动化，**100% 后台全静默写入**，无弹窗、无需二次点击！
  - **新版 Outlook (New Outlook) 模式**：通过标准 `.ics` 协议自动唤起日程卡片，一键确认保存。
- 🛡️ **智能预过滤与隐私保护**：内置正则表达式与日程关键词前置拦截。复制代码、密码、网址或普通文本时**绝对不会发送给 AI**，零冗余网络请求。
- 🚀 **系统托盘与开机自启**：右键托盘图标一键勾选开机自启，支持随时暂停/恢复监听、一键修改配置与查看日志。

---

## 🚀 快速上手

### 1. 下载即用

前往 [Releases](../../releases) 页面下载压缩包：
- **`SmartCalendar.exe`**：仅几百 KB，原生单文件执行档。
- **`config.example.json`**：复制并重命名为 `config.json`。

### 2. 获取免费 API Key

1. 访问 [智谱 AI 开放平台](https://open.bigmodel.cn) 注册账号（免费）。
2. 在控制台进入「API Keys」页面复制你的密钥。
3. 打开 `config.json` 填入 API Key：
   ```json
   {
     "api_key": "你的API密钥填在这里",
     "api_url": "https://open.bigmodel.cn/api/paas/v4/chat/completions",
     "model": "glm-4-flash",
     "calendar_mode": "classic_outlook",
     "reminder_minutes": 15
   }
   ```

### 3. 双击运行

双击 `SmartCalendar.exe`，右下角任务栏托盘会浮现 📅 **日历小图标**。

现在复制一段文本试试：
> *“明天下午2点在星巴克和张总讨论Q4预算”*

右下角将弹出系统气泡：
`✅ 日程已静默添加：【讨论Q4预算】明天 14:00 @ 星巴克`

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
| `keywords` | `[...]` | 预过滤关键词列表，命中后才会调用 AI 分析 |

---

## 🛠️ 本地编译构建

本项目基于 **.NET 9.0** 与 Windows Forms 构建：

```powershell
# 1. 克隆代码仓库
git clone https://github.com/your-username/SmartCalendar.git
cd SmartCalendar

# 2. 发布单个 EXE 文件 (依赖本机 .NET 运行时，体积仅 ~180KB)
dotnet publish src/SmartCalendar.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o dist

# 3. 或者发布独立全打包 EXE (免安装运行时，可在任何 Win10/11 电脑上运行)
dotnet publish src/SmartCalendar.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --self-contained true -o dist-standalone
```

---

## 🛡️ 安全与脱敏规范

- 本开源仓库严格遵守安全合规原则，所有配置文件与代码均已剔除私有密钥与个人路径。
- 用户实际使用的 `config.json` 与运行日志 `smart_calendar.log` 已被 `.gitignore` 默认排除，不会被意外提交至公共仓库。

---

## 📄 开源许可证

本项目基于 [MIT License](LICENSE) 开源。欢迎提 Issue 与 PR！
