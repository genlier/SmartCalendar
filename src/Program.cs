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
using System.Text.Json.Nodes;
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
    public const string DefaultHotkey = "Ctrl + Alt + F11";

    [JsonPropertyName("hotkey")]
    public string? Hotkey { get; set; } = DefaultHotkey;

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
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private const int WM_HOTKEY = 0x0312;
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const uint ModNoRepeat = 0x4000;
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
    private ToolStripMenuItem _titleItem = null!;
    private ToolStripMenuItem _toggleMenuItem = null!;
    private ToolStripMenuItem _autoStartMenuItem = null!;
    private bool _paused;
    private bool _triggerActive;
    private Hotkey? _activeHotkey;
    private int _activeTriggerId;
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

        InitTrayIcon();
        _msgForm = new MessageListenerForm(OnTriggered);

        Log("🚀 智能入历已启动 (v1.6)");
        try
        {
            _config = ReadConfig();
            SetTrigger(Hotkey.Parse(_config.Hotkey), enabled: true);
        }
        catch (Exception ex)
        {
            UpdateTriggerUi();
            ReportTriggerError(ex);
            return;
        }
        UpdateTriggerUi();

        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _notifyIcon.ShowBalloonTip(5000, "⚠️ 需要配置 API Key", "检测到 config.json 中未填写智谱 API Key。\n右键托盘图标点击【打开配置文件】填入即可免费使用。", ToolTipIcon.Warning);
        }
        else
        {
            string instructions = _activeHotkey is { } hotkey
                ? $"复制日程文本后，按 {hotkey.Label} 添加到 Outlook。"
                : "复制日程文本后，将自动识别并添加到 Outlook。";
            _notifyIcon.ShowBalloonTip(3000, "📅 智能入历", instructions, ToolTipIcon.Info);
        }
    }

    private class MessageListenerForm : Form
    {
        private readonly Action<int> _onTriggered;

        public MessageListenerForm(Action<int> onTriggered)
        {
            _onTriggered = onTriggered;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Size = new Size(1, 1);
            var _ = Handle;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                _onTriggered(m.WParam.ToInt32());
                return;
            }
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                _onTriggered(0);
                return;
            }
            base.WndProc(ref m);
        }
    }

    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private AppConfig ReadConfig()
    {
        if (!File.Exists(_configPath)) WriteNewConfig();
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath, Encoding.UTF8))
            ?? throw new InvalidDataException("配置文件应包含一个 JSON 对象。");
    }

    private void WriteNewConfig()
    {
        File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, ConfigJsonOptions), Encoding.UTF8);
    }

    private void InitTrayIcon()
    {
        var contextMenu = new ContextMenuStrip();

        _titleItem = new ToolStripMenuItem("📅 智能入历") { Enabled = false };
        _titleItem.Font = new Font(_titleItem.Font, FontStyle.Bold);

        _toggleMenuItem = new ToolStripMenuItem("▶️ 启用触发", null, (s, e) => ToggleTrigger());
        var shortcutItem = new ToolStripMenuItem("设置快捷键…", null, (s, e) => EditHotkey());
        
        bool isAutoStart = IsAutoStartEnabled();
        _autoStartMenuItem = new ToolStripMenuItem(isAutoStart ? "✓ 开机自启动" : "  开机自启动", null, (s, e) => ToggleAutoStart())
        {
            Checked = isAutoStart
        };

        var openConfigItem = new ToolStripMenuItem("⚙️ 打开配置文件", null, (s, e) => OpenConfig());
        var reloadConfigItem = new ToolStripMenuItem("🔄 重新加载配置", null, (s, e) => ReloadConfig());
        var openLogItem = new ToolStripMenuItem("📝 查看运行日志", null, (s, e) => OpenLog());
        var exitItem = new ToolStripMenuItem("❌ 退出程序", null, (s, e) => ExitApp());

        contextMenu.Items.Add(_titleItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_toggleMenuItem);
        contextMenu.Items.Add(shortcutItem);
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
            Text = "📅 智能入历",
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

    private void SetTrigger(Hotkey? hotkey, bool enabled)
    {
        if (_triggerActive == enabled && (!enabled || _activeHotkey == hotkey)) return;

        IntPtr handle = _msgForm.Handle;
        int nextId = 0;
        if (enabled)
        {
            if (hotkey is { } shortcut)
            {
                nextId = _activeTriggerId == 1 ? 2 : 1;
                if (!RegisterHotKey(handle, nextId, shortcut.Modifiers | ModNoRepeat, (uint)shortcut.Key))
                {
                    int error = Marshal.GetLastWin32Error();
                    string message = error == 1409 ? "已被占用，请换一组按键。" : $"注册失败（Windows 错误 {error}）。";
                    throw new InvalidOperationException($"{shortcut.Label} {message}");
                }
            }
            else if (!AddClipboardFormatListener(handle))
            {
                throw new InvalidOperationException($"无法启用复制触发（Windows 错误 {Marshal.GetLastWin32Error()}）。");
            }
        }

        if (_triggerActive && !RemoveTrigger(handle, _activeTriggerId))
        {
            int error = Marshal.GetLastWin32Error();
            if (enabled) RemoveTrigger(handle, nextId);
            throw new InvalidOperationException($"无法停用当前触发（Windows 错误 {error}）。");
        }

        _triggerActive = enabled;
        _activeHotkey = enabled ? hotkey : null;
        _activeTriggerId = nextId;
        Log(!enabled ? "触发已暂停" : hotkey is { } active ? $"快捷键已启用: {active.Label}" : "复制时自动识别已启用");
    }

    private static bool RemoveTrigger(IntPtr handle, int id)
    {
        return id == 0 ? RemoveClipboardFormatListener(handle) : UnregisterHotKey(handle, id);
    }

    private void UpdateTriggerUi()
    {
        string mode = _triggerActive
            ? _activeHotkey?.Label ?? "复制时自动识别"
            : _paused ? "触发已暂停" : "触发未启用";
        _titleItem.Text = $"📅 智能入历 · {mode}";
        _notifyIcon.Text = $"📅 智能入历 - {mode}";
        _toggleMenuItem.Text = _triggerActive ? "⏸️ 暂停触发" : "▶️ 启用触发";
    }

    private void ReportTriggerError(Exception ex)
    {
        Log($"触发设置失败: {ex.Message}");
        _notifyIcon.ShowBalloonTip(5000, "触发设置失败", ex.Message, ToolTipIcon.Error);
    }

    private void ToggleTrigger()
    {
        try
        {
            bool enabled = !_triggerActive;
            SetTrigger(enabled ? Hotkey.Parse(_config.Hotkey) : null, enabled);
            _paused = !enabled;
            UpdateTriggerUi();
        }
        catch (Exception ex)
        {
            ReportTriggerError(ex);
        }
    }

    private void EditHotkey()
    {
        using var dialog = new HotkeySettingsForm(_config.Hotkey, SaveHotkey);
        dialog.ShowDialog();
    }

    private void SaveHotkey(Hotkey? hotkey)
    {
        if (!File.Exists(_configPath)) WriteNewConfig();
        var current = JsonNode.Parse(File.ReadAllText(_configPath, Encoding.UTF8))?.AsObject()
            ?? throw new InvalidDataException("配置文件应包含一个 JSON 对象。");
        current["hotkey"] = hotkey?.Label;

        string temporary = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool wasActive = _triggerActive;
        Hotkey? previousHotkey = _activeHotkey;
        try
        {
            File.WriteAllText(temporary, current.ToJsonString(ConfigJsonOptions), Encoding.UTF8);
            SetTrigger(hotkey, !_paused);
            try
            {
                File.Replace(temporary, _configPath, null);
            }
            catch
            {
                SetTrigger(previousHotkey, wasActive);
                throw;
            }
            _config.Hotkey = hotkey?.Label;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            UpdateTriggerUi();
        }
    }

    private void OpenConfig()
    {
        try
        {
            if (!File.Exists(_configPath)) WriteNewConfig();
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
        try
        {
            AppConfig updated = ReadConfig();
            SetTrigger(Hotkey.Parse(updated.Hotkey), !_paused);
            _config = updated;
            UpdateTriggerUi();
            _notifyIcon.ShowBalloonTip(2000, "智能入历", "配置已重新加载 ✅", ToolTipIcon.Info);
            Log("用户手动重新加载配置");
        }
        catch (Exception ex)
        {
            ReportTriggerError(ex);
        }
    }

    private void ExitApp()
    {
        try
        {
            if (_triggerActive) RemoveTrigger(_msgForm.Handle, _activeTriggerId);
            _msgForm.Dispose();
        }
        catch { }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    private void OnTriggered(int id)
    {
        if (!_triggerActive || id != _activeTriggerId) return;

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
