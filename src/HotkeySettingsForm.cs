using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartCalendar;

internal readonly record struct Hotkey(uint Modifiers, Keys Key, string Label)
{
    public static Hotkey? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        string[] parts = value.ToUpperInvariant().Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            uint modifier = parts[i] switch
            {
                "ALT" => 0x0001,
                "CTRL" => 0x0002,
                "SHIFT" => 0x0004,
                _ => 0
            };
            if (modifier == 0 || (modifiers & modifier) != 0) throw InvalidHotkey();
            modifiers |= modifier;
        }

        string name = parts[^1];
        Keys key = name switch
        {
            [>= 'A' and <= 'Z'] => (Keys)name[0],
            [>= '0' and <= '9'] => (Keys)name[0],
            _ when name.StartsWith('F') && int.TryParse(name.AsSpan(1), out int number)
                && number is >= 1 and <= 24 && name == $"F{number}" => Keys.F1 + (number - 1),
            _ => Keys.None
        };
        if (modifiers == 0 || key == Keys.None) throw InvalidHotkey();

        string label = ((modifiers & 0x0002) != 0 ? "Ctrl + " : "")
            + ((modifiers & 0x0001) != 0 ? "Alt + " : "")
            + ((modifiers & 0x0004) != 0 ? "Shift + " : "") + name;
        return new Hotkey(modifiers, key, label);
    }

    private static FormatException InvalidHotkey()
    {
        return new FormatException("请至少使用一个 Ctrl、Alt 或 Shift，搭配字母、数字或 F1–F24，例如 Ctrl + Shift + K。");
    }
}

internal sealed class HotkeySettingsForm : Form
{
    public HotkeySettingsForm(string? currentHotkey, Action<Hotkey?> save)
    {
        Text = "智能入历 · 设置快捷键";
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var input = new TextBox
        {
            AccessibleName = "全局快捷键",
            Text = currentHotkey ?? "",
            Width = 360,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        var defaults = new Button { Text = "恢复默认", AutoSize = true };
        defaults.Click += (_, _) => input.Text = AppConfig.DefaultHotkey;
        var clear = new Button { Text = "清空快捷键", AutoSize = true };
        clear.Click += (_, _) => input.Clear();
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var apply = new Button { Text = "保存", AutoSize = true };
        var error = new Label { AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = Color.Firebrick };
        apply.Click += (_, _) =>
        {
            try
            {
                save(Hotkey.Parse(input.Text));
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                error.Text = ex.Message;
                input.Focus();
            }
        };
        input.TextChanged += (_, _) => error.Text = "";
        AcceptButton = apply;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.AddRange(new Control[] { defaults, clear, apply, cancel });

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(16),
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(new Label { Text = "全局快捷键（例如 Ctrl + Shift + K）", AutoSize = true });
        layout.Controls.Add(input);
        layout.Controls.Add(new Label { Text = "留空并保存后，复制文本时自动识别。", AutoSize = true });
        layout.Controls.Add(error);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        ActiveControl = input;
    }
}
