using SysBot.Pokemon.WinForms;
using System.Collections.Generic;
using System.Drawing;

public static class ThemeManager
{
    public static Dictionary<string, ThemeColors> ThemePresets { get; } = new()
    {
        ["Classic"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(31, 30, 68),
            Shadow = Color.FromArgb(20, 19, 57),
            Hover = Color.FromArgb(31, 30, 68)
        },
        ["Diet Classic"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(41, 40, 78),
            Shadow = Color.FromArgb(30, 29, 67),
            Hover = Color.FromArgb(41, 40, 78)
        },
        ["In the Shadows"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(20, 19, 57),
            Shadow = Color.FromArgb(15, 15, 45),
            Hover = Color.FromArgb(20, 19, 57)
        },
        ["Darkest Nights"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(15, 15, 45),
            Shadow = Color.FromArgb(11, 11, 41),
            Hover = Color.FromArgb(15, 15, 45)
        },
        ["Singularity I"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(31, 30, 68),
            Shadow = Color.FromArgb(31, 30, 68),
            Hover = Color.FromArgb(31, 30, 68)
        },
        ["Singularity II"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(20, 19, 57),
            Shadow = Color.FromArgb(20, 19, 57),
            Hover = Color.FromArgb(20, 19, 57)
        },
        ["Singularity III"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(15, 15, 45),
            Shadow = Color.FromArgb(15, 15, 45),
            Hover = Color.FromArgb(15, 15, 45)
        },
        ["Deep Purple Vibe"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(25, 0, 77),
            Shadow = Color.FromArgb(9, 0, 51),
            Hover = Color.FromArgb(25, 0, 77)
        },
        ["Indigo Shroud"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(31, 30, 68),
            Shadow = Color.FromArgb(42, 0, 128),
            Hover = Color.FromArgb(31, 30, 68)
        },
        ["Blackout Borders"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(31, 30, 68),
            Shadow = Color.FromArgb(0, 0, 0),
            Hover = Color.FromArgb(31, 30, 68)
        },
        ["Blackout"] = new ThemeColors
        {
            PanelBase = Color.FromArgb(0, 0, 0),
            Shadow = Color.FromArgb(7, 7, 7),
            Hover = Color.FromArgb(0, 0, 0)
        },
    };

    public static string CurrentThemeName { get; private set; } = "Classic";

    // 🆕 Easy access to current theme colors
    public static ThemeColors CurrentColors => ThemePresets[CurrentThemeName];

    public static void ApplyTheme(Main form, string themeName)
    {
        if (!ThemePresets.TryGetValue(themeName, out var colors))
            return;

        CurrentThemeName = themeName;

        // PANEL: Main stays untouched
        form.panelMain.BackColor = Color.FromArgb(10, 10, 40);

        // PANEL: Primary base
        form.panelLeftSide.BackColor = colors.PanelBase;
        form.panelTitleBar.BackColor = colors.PanelBase;

        // SHADOW PANELS + Borders
        form.shadowPanelTop.BackColor = colors.Shadow;
        form.shadowPanelLeft.BackColor = colors.Shadow;
        form.panel1.BackColor = colors.Shadow;
        form.panel2.BackColor = colors.Shadow;
        form.panel3.BackColor = colors.Shadow;
        form.panel4.BackColor = colors.Shadow;
        form.panel5.BackColor = colors.Shadow;
        form.panel6.BackColor = colors.Shadow;

        // BUTTONS — start with PanelBase color
        form.btnBots.BackColor = colors.PanelBase;
        form.btnHub.BackColor = colors.PanelBase;
        form.btnLogs.BackColor = colors.PanelBase;

        // Reapply hover animations using new theme colors
        form.SetupThemeAwareButtons();
    }

    public static ThemeColors? GetCurrentColors()
        => ThemePresets.TryGetValue(CurrentThemeName, out var colors) ? colors : null;
}

// Add Hover support to ThemeColors
public class ThemeColors
{
    public Color PanelBase { get; set; }
    public Color Shadow { get; set; }

    // This is the hover/fade color used on animated buttons
    public Color Hover { get; set; }
}
