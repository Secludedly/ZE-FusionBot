using System;
using System.Windows.Forms;
using PKHeX.Core; // or wherever ILogForwarder is defined

namespace SysBot.Base
{
    public class LogTextBoxForwarder : ILogForwarder
    {
        private readonly RichTextBox _rtb;

        public LogTextBoxForwarder(RichTextBox rtb)
        {
            _rtb = rtb;
        }

        // Implement the interface method exactly
        public void Forward(string logText, string logSource)
        {
            if (_rtb.InvokeRequired)
            {
                _rtb.Invoke(new Action(() => AppendText(logText, logSource)));
            }
            else
            {
                AppendText(logText, logSource);
            }
        }

        private void AppendText(string text, string source)
        {
            if (_rtb.TextLength > _rtb.MaxLength)
                _rtb.Clear();

            // Format your log string, for example: [Source] LogText
            string formatted = $"[{source}] {text}{Environment.NewLine}";
            _rtb.AppendText(formatted);
            _rtb.SelectionStart = _rtb.Text.Length;
            _rtb.ScrollToCaret();
        }
    }
}
