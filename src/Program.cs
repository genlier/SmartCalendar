using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SmartCalendar;

static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main()
    {
        try
        {
            _singleInstanceMutex = new Mutex(true, "Local\\SmartClipboardToOutlookCalendar_Mutex", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("智能入历已经在后台运行中了！\n可查看系统右下角托盘图标。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new ClipboardAppContext());

            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (Exception ex)
        {
            string crashLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(crashLog, ex.ToString(), Encoding.UTF8);
        }
    }
}

public class AppConfig
{
    [JsonPropertyName("_help")]
    public string Help { get; set; } = "api_key: 在 https://open.bigmodel.cn 免费获取";

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("api_url")]
    public string ApiUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4/chat/completions";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "glm-4-flash";

    [JsonPropertyName("calendar_mode")]
    public string CalendarMode { get; set; } = "classic_outlook"; // "classic_outlook" 或 "new_outlook"

    [JsonPropertyName("reminder_minutes")]
    public int ReminderMinutes { get; set; } = 15;

    [JsonPropertyName("min_text_length")]
    public int MinTextLength { get; set; } = 4;

    [JsonPropertyName("max_text_length")]
    public int MaxTextLength { get; set; } = 2000;

    [JsonPropertyName("keywords")]
    public string[] Keywords { get; set; } = new[]
    {
        "会议", "约", "面试", "航班", "火车", "高铁",
        "上午", "下午", "晚上", "点半", "点钟",
        "月", "号", "周", "星期", "明天", "后天", "下周", "下个月",
        "开会", "聚餐", "预约", "挂号", "考试", "答辩",
        "汇报", "培训", "出发", "到达", "截止", "deadline",
        "meeting", "appointment", "flight", "schedule",
        "tomorrow", "next week", "remind"
    };

    [JsonPropertyName("ignore_keywords")]
    public string[] IgnoreKeywords { get; set; } = new[]
    {
        "验证码", "密码", "已取消", "取消会议", "账单", "扣款", "历史消息", "退款"
    };
}

public class EventResult
{
    [JsonPropertyName("is_event")]
    public bool IsEvent { get; set; }

    [JsonPropertyName("is_all_day")]
    public bool IsAllDay { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class ClipboardAppContext : ApplicationContext
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "SmartCalendar";

    private readonly string _baseDir;
    private readonly string _configPath;
    private readonly string _logPath;
    private readonly HttpClient _httpClient;
    private readonly Regex[] _datePatterns;
    private readonly MessageListenerForm _msgForm;

    private AppConfig _config = new();
    private NotifyIcon _notifyIcon = null!;
    private ToolStripMenuItem _toggleMenuItem = null!;
    private ToolStripMenuItem _autoStartMenuItem = null!;
    private bool _isListening = true;
    private string _lastProcessedText = "";

    public ClipboardAppContext()
    {
        _baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _configPath = Path.Combine(_baseDir, "config.json");
        _logPath = Path.Combine(_baseDir, "smart_calendar.log");

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        _datePatterns = new[]
        {
            new Regex(@"\d{1,2}月\d{1,2}[日号]", RegexOptions.Compiled),
            new Regex(@"\d{4}[-/\.]\d{1,2}[-/\.]\d{1,2}", RegexOptions.Compiled),
            new Regex(@"\d{1,2}:\d{2}", RegexOptions.Compiled),
            new Regex(@"[周星期][一二三四五六日天]", RegexOptions.Compiled),
        };

        InitConfig();
        InitTrayIcon();

        _msgForm = new MessageListenerForm(OnClipboardChanged);
        AddClipboardFormatListener(_msgForm.Handle);

        Log("🚀 智能入历已启动 (v1.4 - 支持全天日程与查重防重复)");

        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _notifyIcon.ShowBalloonTip(5000, "⚠️ 需要配置 API Key", "检测到 config.json 中未填写智谱 API Key。\n右键托盘图标点击【打开配置文件】填入即可免费使用。", ToolTipIcon.Warning);
        }
        else
        {
            _notifyIcon.ShowBalloonTip(3000, "📅 智能入历 (全静默运行中)", "复制含日程的文本将自动识别并100%静默存入 Outlook！", ToolTipIcon.Info);
        }
    }

    private class MessageListenerForm : Form
    {
        private readonly Action _onClipboardChanged;

        public MessageListenerForm(Action onClipboardChanged)
        {
            _onClipboardChanged = onClipboardChanged;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Size = new Size(1, 1);
            var _ = Handle;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                _onClipboardChanged();
            }
            base.WndProc(ref m);
        }
    }

    private void InitConfig()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, options), Encoding.UTF8);
            }
            else
            {
                string json = File.ReadAllText(_configPath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null)
                {
                    _config = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"读取配置失败: {ex.Message}");
        }
    }

    private void InitTrayIcon()
    {
        var contextMenu = new ContextMenuStrip();

        var titleItem = new ToolStripMenuItem("📅 智能入历 (v1.4)") { Enabled = false };
        titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);

        _toggleMenuItem = new ToolStripMenuItem("⏸️ 暂停监听", null, (s, e) => ToggleListening());
        
        bool isAutoStart = IsAutoStartEnabled();
        _autoStartMenuItem = new ToolStripMenuItem(isAutoStart ? "✓ 开机自启动" : "  开机自启动", null, (s, e) => ToggleAutoStart())
        {
            Checked = isAutoStart
        };

        var openConfigItem = new ToolStripMenuItem("⚙️ 打开配置文件", null, (s, e) => OpenConfig());
        var reloadConfigItem = new ToolStripMenuItem("🔄 重新加载配置", null, (s, e) => ReloadConfig());
        var openLogItem = new ToolStripMenuItem("📝 查看运行日志", null, (s, e) => OpenLog());
        var exitItem = new ToolStripMenuItem("❌ 退出程序", null, (s, e) => ExitApp());

        contextMenu.Items.Add(titleItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_toggleMenuItem);
        contextMenu.Items.Add(_autoStartMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(openConfigItem);
        contextMenu.Items.Add(reloadConfigItem);
        contextMenu.Items.Add(openLogItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateCalendarIcon(),
            Text = "📅 智能入历 - 复制即静默入历",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => OpenConfig();
    }

    private bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppRegistryName) != null;
        }
        catch
        {
            return false;
        }
    }

    private void ToggleAutoStart()
    {
        try
        {
            bool current = IsAutoStartEnabled();
            bool target = !current;

            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key != null)
            {
                if (target)
                {
                    key.SetValue(AppRegistryName, $"\"{Application.ExecutablePath}\"");
                    _autoStartMenuItem.Text = "✓ 开机自启动";
                    _autoStartMenuItem.Checked = true;
                    _notifyIcon.ShowBalloonTip(2000, "智能入历", "✅ 已开启开机自启动", ToolTipIcon.Info);
                    Log("开机自启动: 已启用");
                }
                else
                {
                    key.DeleteValue(AppRegistryName, false);
                    _autoStartMenuItem.Text = "  开机自启动";
                    _autoStartMenuItem.Checked = false;
                    _notifyIcon.ShowBalloonTip(2000, "智能入历", "❌ 已关闭开机自启动", ToolTipIcon.Info);
                    Log("开机自启动: 已关闭");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置开机自启动失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Icon CreateCalendarIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);

            using var bgBrush = new SolidBrush(Color.White);
            g.FillRectangle(bgBrush, 3, 5, 26, 23);
            using var borderPen = new Pen(Color.FromArgb(90, 90, 90), 2);
            g.DrawRectangle(borderPen, 3, 5, 26, 23);

            using var headerBrush = new SolidBrush(Color.FromArgb(231, 76, 60));
            g.FillRectangle(headerBrush, 3, 5, 26, 8);

            string day = DateTime.Now.Day.ToString();
            using var font = new Font("Arial", 9, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
            var sz = g.MeasureString(day, font);
            g.DrawString(day, font, textBrush, (32 - sz.Width) / 2, 13);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void ToggleListening()
    {
        _isListening = !_isListening;
        if (_isListening)
        {
            _toggleMenuItem.Text = "⏸️ 暂停监听";
            _notifyIcon.Text = "📅 智能入历 - 运行中";
            _notifyIcon.ShowBalloonTip(2000, "智能入历", "已恢复剪贴板监听 ▶", ToolTipIcon.Info);
            Log("已恢复剪贴板监听");
        }
        else
        {
            _toggleMenuItem.Text = "▶️ 恢复监听";
            _notifyIcon.Text = "📅 智能入历 - 已暂停";
            _notifyIcon.ShowBalloonTip(2000, "智能入历", "已暂停剪贴板监听 ⏸", ToolTipIcon.Warning);
            Log("已暂停剪贴板监听");
        }
    }

    private void OpenConfig()
    {
        try
        {
            if (!File.Exists(_configPath)) InitConfig();
            Process.Start(new ProcessStartInfo("notepad.exe", _configPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开配置文件失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLog()
    {
        try
        {
            if (!File.Exists(_logPath)) File.WriteAllText(_logPath, "--- 智能入历 日志开始 ---\r\n", Encoding.UTF8);
            Process.Start(new ProcessStartInfo("notepad.exe", _logPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开日志失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReloadConfig()
    {
        InitConfig();
        _notifyIcon.ShowBalloonTip(2000, "智能入历", "配置已重新加载 ✅", ToolTipIcon.Info);
        Log("用户手动重新加载配置");
    }

    private void ExitApp()
    {
        try
        {
            RemoveClipboardFormatListener(_msgForm.Handle);
            _msgForm.Close();
        }
        catch { }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    private void OnClipboardChanged()
    {
        if (!_isListening) return;

        string? text = GetClipboardTextSafe();
        if (string.IsNullOrWhiteSpace(text)) return;

        text = text.Trim();
        if (text == _lastProcessedText) return;
        _lastProcessedText = text;

        if (text.Length < _config.MinTextLength || text.Length > _config.MaxTextLength)
            return;

        if (ShouldIgnore(text))
        {
            Log($"🚫 命中黑名单关键词，已跳过: {text.Replace("\r", " ").Replace("\n", " ").Substring(0, Math.Min(text.Length, 40))}...");
            return;
        }

        if (!MightContainEvent(text))
            return;

        Task.Run(() => ProcessEventAsync(text));
    }

    private string? GetClipboardTextSafe()
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    return Clipboard.GetText();
                }
                return null;
            }
            catch (ExternalException)
            {
                Thread.Sleep(60);
            }
            catch
            {
                break;
            }
        }
        return null;
    }

    private bool ShouldIgnore(string text)
    {
        if (_config.IgnoreKeywords != null)
        {
            foreach (var ikw in _config.IgnoreKeywords)
            {
                if (!string.IsNullOrWhiteSpace(ikw) && text.IndexOf(ikw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        return false;
    }

    private bool MightContainEvent(string text)
    {
        foreach (var kw in _config.Keywords)
        {
            if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        foreach (var pattern in _datePatterns)
        {
            if (pattern.IsMatch(text))
                return true;
        }

        return false;
    }

    private async Task ProcessEventAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            Log("未配置 api_key，跳过解析");
            return;
        }

        Log($"🔍 检测到疑似日程文本: {text.Replace("\r", " ").Replace("\n", " ").Substring(0, Math.Min(text.Length, 60))}...");

        try
        {
            var now = DateTime.Now;
            string weekdayCn = now.DayOfWeek switch
            {
                DayOfWeek.Monday => "周一",
                DayOfWeek.Tuesday => "周二",
                DayOfWeek.Wednesday => "周三",
                DayOfWeek.Thursday => "周四",
                DayOfWeek.Friday => "周五",
                DayOfWeek.Saturday => "周六",
                _ => "周日"
            };

            string systemPrompt = $@"你是一个日程提取助手。根据用户文本提取日程信息，只返回严格合法的纯JSON对象（禁止markdown代码块或多余文字）：
{{""is_event"":true,""is_all_day"":true/false,""title"":""简洁标题"",""start_date"":""YYYY-MM-DD"",""start_time"":""HH:mm"",""end_time"":""HH:mm"",""location"":""地点或空字符串"",""notes"":""备注或空字符串""}}
若不含日程返回：{{""is_event"":false}}

规则：
1. 当前真实基准时间是：{now:yyyy-MM-dd HH:mm}（{weekdayCn}）
2. 全天判断：若仅提到日期无具体几点几分（如“明天出差”、“周五生日”、“截止日”），设 ""is_all_day"": true，start_time和end_time填空字符串；若有具体时间（如“下午2点”），设 ""is_all_day"": false。
3. 持续时间：有时段但无明确结束时间的，默认持续 1 小时。
4. 纯时间无日期，取最近将来的那一刻。
5. 无地点或备注填空字符串。";

            var requestBody = new
            {
                model = _config.Model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = text }
                },
                temperature = 0.0
            };

            var request = new HttpRequestMessage(HttpMethod.Post, _config.ApiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string resJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resJson);
            string rawContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?.Trim() ?? "";

            if (rawContent.StartsWith("```"))
            {
                rawContent = Regex.Replace(rawContent, @"^```[a-zA-Z]*\r?\n?", "");
                rawContent = Regex.Replace(rawContent, @"\r?\n?```$", "");
                rawContent = rawContent.Trim();
            }

            var eventData = JsonSerializer.Deserialize<EventResult>(rawContent);
            if (eventData == null || !eventData.IsEvent || string.IsNullOrWhiteSpace(eventData.Title))
            {
                Log("   → AI 判定: 非日程信息");
                return;
            }

            string modeDesc = eventData.IsAllDay ? "【全天】" : $"{eventData.StartTime}-{eventData.EndTime}";
            Log($"📅 成功解析日程: 【{eventData.Title}】 {eventData.StartDate} {modeDesc} @ {eventData.Location}");

            bool added;
            bool alreadyExists;
            string errorMsg;

            if (_config.CalendarMode.Equals("new_outlook", StringComparison.OrdinalIgnoreCase))
            {
                added = AddToNewOutlook(eventData, _config.ReminderMinutes, out errorMsg);
                alreadyExists = false;
            }
            else
            {
                added = AddToClassicOutlook(eventData, _config.ReminderMinutes, out alreadyExists, out errorMsg);
            }

            if (alreadyExists)
            {
                _notifyIcon.ShowBalloonTip(3000, "⚠️ 日程已存在", $"【{eventData.Title}】已在日历中，无需重复添加", ToolTipIcon.Info);
                Log($"⚠️ 日历中已存在该日程，已跳过: {eventData.Title}");
            }
            else if (added)
            {
                string timeDesc = eventData.IsAllDay ? "全天日程" : eventData.StartTime ?? "";
                string summary = $"{eventData.Title}\n{eventData.StartDate} {timeDesc}" +
                                 (!string.IsNullOrEmpty(eventData.Location) ? $" @ {eventData.Location}" : "");
                _notifyIcon.ShowBalloonTip(4000, "✅ 日程已静默添加", $"{summary}\n已直接存入 Outlook 日历", ToolTipIcon.Info);
                Log($"✅ 成功静默存入 Outlook: {eventData.Title}");
            }
            else
            {
                _notifyIcon.ShowBalloonTip(4000, "❌ 日程写入失败", errorMsg, ToolTipIcon.Error);
                Log($"❌ 日程写入失败: {errorMsg}");
            }
        }
        catch (Exception ex)
        {
            Log($"调用 AI 发生异常: {ex.Message}");
        }
    }

    private static bool EventExistsInOutlook(dynamic outlook, string title, DateTime startDt)
    {
        try
        {
            dynamic ns = outlook.GetNamespace("MAPI");
            dynamic calendar = ns.GetDefaultFolder(9); // 9 = olFolderCalendar
            dynamic items = calendar.Items;

            DateTime dayStart = startDt.Date;
            DateTime dayEnd = dayStart.AddDays(1);
            string filter = $"[Start] >= '{dayStart:yyyy-MM-dd 00:00}' AND [Start] < '{dayEnd:yyyy-MM-dd 00:00}'";
            dynamic restricted = items.Restrict(filter);

            string targetTitle = title.Trim();
            foreach (var item in restricted)
            {
                try
                {
                    string subj = item.Subject ?? "";
                    if (string.Equals(subj.Trim(), targetTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    private static bool AddToClassicOutlook(EventResult ev, int reminderMinutes, out bool alreadyExists, out string error)
    {
        alreadyExists = false;
        error = string.Empty;

        DateTime startDt;
        if (!string.IsNullOrEmpty(ev.StartTime) && DateTime.TryParse($"{ev.StartDate} {ev.StartTime}", out startDt)) { }
        else if (DateTime.TryParse(ev.StartDate, out startDt)) { }
        else { startDt = DateTime.Now.AddHours(1); }

        DateTime endDt;
        if (!string.IsNullOrEmpty(ev.EndTime) && DateTime.TryParse($"{ev.StartDate} {ev.EndTime}", out endDt))
        {
            if (endDt <= startDt) endDt = startDt.AddHours(1);
        }
        else
        {
            endDt = ev.IsAllDay ? startDt : startDt.AddHours(1);
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Type? outlookType = Type.GetTypeFromProgID("Outlook.Application");
                if (outlookType == null)
                {
                    error = "未检测到经典版 Microsoft Outlook 客户端。";
                    return false;
                }

                dynamic outlook = Activator.CreateInstance(outlookType)!;

                // 查重检测
                if (EventExistsInOutlook(outlook, ev.Title ?? "", startDt))
                {
                    alreadyExists = true;
                    return true;
                }

                dynamic appt = outlook.CreateItem(1);
                appt.Subject = ev.Title ?? "新日程";
                appt.AllDayEvent = ev.IsAllDay;

                if (ev.IsAllDay)
                {
                    appt.Start = startDt.Date;
                    appt.End = endDt.Date.AddDays(1);
                }
                else
                {
                    appt.Start = startDt;
                    appt.End = endDt;
                }

                if (!string.IsNullOrEmpty(ev.Location)) appt.Location = ev.Location;
                if (!string.IsNullOrEmpty(ev.Notes)) appt.Body = ev.Notes;

                appt.ReminderSet = true;
                appt.ReminderMinutesBeforeStart = reminderMinutes;

                appt.Save();
                return true;
            }
            catch (COMException) when (attempt == 0)
            {
                Thread.Sleep(800);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        return false;
    }

    private static bool AddToNewOutlook(EventResult ev, int reminderMinutes, out string error)
    {
        error = string.Empty;
        try
        {
            DateTime startDt;
            if (!string.IsNullOrEmpty(ev.StartTime) && DateTime.TryParse($"{ev.StartDate} {ev.StartTime}", out startDt)) { }
            else if (DateTime.TryParse(ev.StartDate, out startDt)) { }
            else { startDt = DateTime.Now.AddHours(1); }

            DateTime endDt;
            if (!string.IsNullOrEmpty(ev.EndTime) && DateTime.TryParse($"{ev.StartDate} {ev.EndTime}", out endDt))
            {
                if (endDt <= startDt) endDt = startDt.AddHours(1);
            }
            else
            {
                endDt = ev.IsAllDay ? startDt : startDt.AddHours(1);
            }

            string uid = Guid.NewGuid().ToString("N");
            string nowUtc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");

            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//SmartCalendar//CN");
            sb.AppendLine("CALSCALE:GREGORIAN");
            sb.AppendLine("METHOD:PUBLISH");
            sb.AppendLine("BEGIN:VEVENT");
            sb.AppendLine($"UID:{uid}");
            sb.AppendLine($"DTSTAMP:{nowUtc}");

            if (ev.IsAllDay)
            {
                sb.AppendLine($"DTSTART;VALUE=DATE:{startDt:yyyyMMdd}");
                sb.AppendLine($"DTEND;VALUE=DATE:{endDt.AddDays(1):yyyyMMdd}");
            }
            else
            {
                sb.AppendLine($"DTSTART:{startDt:yyyyMMddTHHmmss}");
                sb.AppendLine($"DTEND:{endDt:yyyyMMddTHHmmss}");
            }

            sb.AppendLine($"SUMMARY:{EscapeIcs(ev.Title ?? "新日程")}");
            if (!string.IsNullOrEmpty(ev.Location))
                sb.AppendLine($"LOCATION:{EscapeIcs(ev.Location)}");
            if (!string.IsNullOrEmpty(ev.Notes))
                sb.AppendLine($"DESCRIPTION:{EscapeIcs(ev.Notes)}");
            sb.AppendLine("STATUS:CONFIRMED");
            sb.AppendLine("BEGIN:VALARM");
            sb.AppendLine($"TRIGGER:-PT{reminderMinutes}M");
            sb.AppendLine("ACTION:DISPLAY");
            sb.AppendLine("DESCRIPTION:Reminder");
            sb.AppendLine("END:VALARM");
            sb.AppendLine("END:VEVENT");
            sb.AppendLine("END:VCALENDAR");

            string tempDir = Path.Combine(Path.GetTempPath(), "SmartCalendarEvents");
            Directory.CreateDirectory(tempDir);
            string tempIcs = Path.Combine(tempDir, $"event_{DateTime.Now:yyyyMMdd_HHmmss}_{uid.Substring(0, 6)}.ics");
            File.WriteAllText(tempIcs, sb.ToString(), Encoding.UTF8);

            Process.Start(new ProcessStartInfo(tempIcs) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string EscapeIcs(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n");
    }

    private void Log(string message)
    {
        try
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\r\n";
            File.AppendAllText(_logPath, line, Encoding.UTF8);
        }
        catch { }
    }
}
