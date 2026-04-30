using SysBot.Base;

using System;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms;

/// <summary>
/// Forward logs to a TextBox.
/// </summary>
public sealed class TextBoxForwarder(TextBoxBase Box) : ILogForwarder
{
    /// <summary>
    /// Synchronize access to the TextBox. Only the GUI thread should be writing to it.
    /// </summary>
    private readonly object _logLock = new();

    public void Forward(string message, string identity)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] - {identity}: {message}{Environment.NewLine}";

        lock (_logLock)
        {
            if (Box.InvokeRequired)
                Box.BeginInvoke((System.Windows.Forms.MethodInvoker)(() => UpdateLog(line)));
            else
                UpdateLog(line);
        }
    }

    private void UpdateLog(string line)
    {
        // More aggressive trimming to prevent performance issues
        var text = Box.Text;
        var max = Box.MaxLength;

        // If we're approaching the limit (90% full), trim more aggressively
        if (text.Length > max * 0.9)
        {
            var lines = Box.Lines;
            // Remove the top half of lines when near limit
            var linesToKeep = lines.Length / 2;
            Box.Lines = lines[^linesToKeep..];
        }
        // If we exceed the MaxLength, remove the top 1/3 of the lines
        else if (text.Length + line.Length + 2 >= max)
        {
            var lines = Box.Lines;
            Box.Lines = lines[(lines.Length / 3)..];
        }

        Box.AppendText(line);
    }
}
