// ============================================================
// Windows 更新开关 - UpdateToggle.exe
//   停用更新 = 暂停 Windows 更新 34 天 + 部署每日自动续期计划任务（无限暂停）
//   启用更新 = 立即恢复接收更新 + 移除自动续期任务
//   --renew  = 隐藏续期模式（由计划任务调用，检查标记后自动续期）
// 原理：读写 HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings 暂停键值，
//       与设置界面手动暂停等价；不禁用服务、不改 ACL，无 SCM 拉锯。
// ============================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UpdateToggle
{
    internal static class Program
    {
        internal const string RegPath = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        internal static readonly string[] PauseValues = new string[] {
            "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
            "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
            "PauseUpdatesExpiryTime" };
        internal const string FlagKey = @"SOFTWARE\WindowsUpdateToggle";
        internal const string TaskDaily = "WindowsUpdatePauseRenew";
        internal const string TaskBoot = "WindowsUpdatePauseRenewBoot";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--renew")
            {
                Renew();
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        // 计划任务每日/开机调用：仅当用户开启过"无限暂停"才续期
        private static void Renew()
        {
            try
            {
                using (RegistryKey flag = Registry.LocalMachine.OpenSubKey(FlagKey))
                {
                    if (flag == null) return;
                    object v = flag.GetValue("InfinitePause");
                    if (v == null || (int)v != 1) return;
                }
                WritePause();
            }
            catch { }
        }

        internal static void WritePause()
        {
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(RegPath))
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string end = DateTime.UtcNow.AddDays(34).ToString("yyyy-MM-ddTHH:mm:ssZ");
                key.SetValue("PauseFeatureUpdatesStartTime", now);
                key.SetValue("PauseFeatureUpdatesEndTime", end);
                key.SetValue("PauseQualityUpdatesStartTime", now);
                key.SetValue("PauseQualityUpdatesEndTime", end);
                key.SetValue("PauseUpdatesExpiryTime", end);
            }
        }

        internal static void ClearPause()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegPath, true))
            {
                if (key == null) return;
                foreach (string v in PauseValues) key.DeleteValue(v, false);
            }
        }

        internal static bool IsPaused(out DateTime endUtc)
        {
            endUtc = DateTime.MinValue;
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegPath))
            {
                if (key == null) return false;
                object v = key.GetValue("PauseFeatureUpdatesEndTime");
                string s = v as string;
                if (string.IsNullOrEmpty(s)) return false;
                DateTime dt;
                if (!DateTime.TryParse(s, null,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out dt)) return false;
                endUtc = dt;
                return dt > DateTime.UtcNow;
            }
        }

        internal static bool TaskExists(string name)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks", "/Query /TN \"" + name + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process p = Process.Start(psi);
            p.WaitForExit();
            return p.ExitCode == 0;
        }

        private static void RunSchtasks(string args, bool tolerateError)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks", args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardError = true;
            Process p = Process.Start(psi);
            p.WaitForExit();
            if (p.ExitCode != 0 && !tolerateError)
                throw new Exception("schtasks 执行失败：" + p.StandardError.ReadToEnd());
        }

        internal static void EnableInfinitePause(string exePath)
        {
            using (RegistryKey flag = Registry.LocalMachine.CreateSubKey(FlagKey))
                flag.SetValue("InfinitePause", 1, RegistryValueKind.DWord);

            string tr = "\\\"" + exePath + "\\\" --renew";
            RunSchtasks("/Create /F /TN \"" + TaskDaily + "\" /SC DAILY /ST 12:00 /RU SYSTEM /TR \"" + tr + "\"", false);
            RunSchtasks("/Create /F /TN \"" + TaskBoot + "\" /SC ONSTART /RU SYSTEM /TR \"" + tr + "\"", false);
        }

        internal static void DisableInfinitePause()
        {
            try { Registry.LocalMachine.DeleteSubKeyTree(FlagKey, false); } catch { }
            RunSchtasks("/Delete /F /TN \"" + TaskDaily + "\"", true);
            RunSchtasks("/Delete /F /TN \"" + TaskBoot + "\"", true);
        }
    }

    internal class MainForm : Form
    {
        private Label statusLabel;

        public MainForm()
        {
            Text = "Windows 更新开关";
            Width = 476; Height = 315;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Microsoft YaHei UI", 10f);

            statusLabel = new Label();
            statusLabel.Left = 15; statusLabel.Top = 12;
            statusLabel.Width = 430; statusLabel.Height = 62;
            statusLabel.Text = BuildStatusText();
            Controls.Add(statusLabel);

            Button btnOff = new Button();
            btnOff.Text = "停用更新\r\n（无限暂停）";
            btnOff.Left = 15; btnOff.Top = 84; btnOff.Width = 205; btnOff.Height = 62;
            btnOff.BackColor = Color.FromArgb(255, 205, 205);
            btnOff.Click += delegate
            {
                try
                {
                    Program.WritePause();
                    Program.EnableInfinitePause(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    MessageBox.Show("已暂停更新，并开启每日自动续期（无限暂停）。\r\n计划任务 " + Program.TaskDaily + " 每天 12:00 / 开机时自动续期 34 天。", "完成");
                }
                catch (Exception ex) { MessageBox.Show("操作失败：" + ex.Message, "错误"); }
                statusLabel.Text = BuildStatusText();
            };
            Controls.Add(btnOff);

            Button btnOn = new Button();
            btnOn.Text = "启用更新\r\n（恢复正常）";
            btnOn.Left = 240; btnOn.Top = 84; btnOn.Width = 205; btnOn.Height = 62;
            btnOn.BackColor = Color.FromArgb(200, 255, 200);
            btnOn.Click += delegate
            {
                try
                {
                    Program.ClearPause();
                    Program.DisableInfinitePause();
                    MessageBox.Show("已恢复 Windows 更新，并移除自动续期任务。", "完成");
                }
                catch (Exception ex) { MessageBox.Show("操作失败：" + ex.Message, "错误"); }
                statusLabel.Text = BuildStatusText();
            };
            Controls.Add(btnOn);

            Label tip = new Label();
            tip.Left = 15; tip.Top = 160; tip.Width = 430; tip.Height = 85;
            tip.ForeColor = Color.DimGray;
            tip.Text = "说明：停用 = 暂停更新并部署每日自动续期（永久不到期）；启用 = 立即恢复接收更新并撤掉续期任务。本工具不禁用服务、不修改 ACL，可放心使用。";
            Controls.Add(tip);
        }

        private static string BuildStatusText()
        {
            DateTime endUtc;
            bool paused = Program.IsPaused(out endUtc);
            bool auto = Program.TaskExists(Program.TaskDaily);

            string text;
            if (paused && auto)
                text = "当前状态：更新已暂停（无限暂停模式，每天自动续期，当前暂停至 " + endUtc.ToString("yyyy-MM-dd") + "）";
            else if (paused)
                text = "当前状态：更新已暂停至 " + endUtc.ToString("yyyy-MM-dd") + "（无自动续期）";
            else
                text = "当前状态：更新已启用（正常接收更新）";

            StringBuilder warn = new StringBuilder();
            foreach (string s in new string[] { "wuauserv", "UsoSvc", "WaaSMedicSvc", "DoSvc" })
            {
                try
                {
                    ServiceController sc = new ServiceController(s);
                    if (sc.StartType == ServiceStartMode.Disabled)
                    {
                        if (warn.Length > 0) warn.Append(", ");
                        warn.Append(s);
                    }
                }
                catch { }
            }
            if (warn.Length > 0)
                text += "\r\n警告：以下更新服务被禁用：" + warn.ToString() + "，可能是禁更新工具残留。";
            return text;
        }
    }
}
