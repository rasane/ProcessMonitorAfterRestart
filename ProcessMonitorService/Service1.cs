using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace ProcessMonitorService
{
    public static class ConfigReader
    {
        public static int GetMonitorDurationInSeconds()
        {
            return int.Parse(ConfigurationManager.AppSettings["MonitorDurationInSeconds"]);
        }

        public static string[] GetProcessNamesToMonitor()
        {
            return ConfigurationManager.AppSettings["ProcessNamesToMonitor"].Split(',');
        }

        public static int GetMaxRestarts()
        {
            return int.Parse(ConfigurationManager.AppSettings["MaxRestarts"]);
        }

        public static string GetLogFilePath()
        {
            return ConfigurationManager.AppSettings["LogFilePath"];
        }

        public static string GetRestartCountFilePath()
        {
            return ConfigurationManager.AppSettings["RestartCountFilePath"];
        }

        public static string GetRestartStatusFilePath()
        {
            return ConfigurationManager.AppSettings["RestartStatusFilePath"];
        }

        public static string GetExceptionFilePath()
        {
            return ConfigurationManager.AppSettings["ExceptionFilePath"];
        }
    }

    public partial class Service1 : ServiceBase
    {
        private int _currentRestartCount;
        private string _exceptionFilePath;
        private string _logFilePath;
        private int _maxRestarts;
        private int _monitorDurationInSeconds;
        private string[] _processNamesToMonitor;
        private string _restartCountFilePath;
        private string _restartStatusFilePath;
        private Timer _shutdownTimer;
        private Timer _timer;

        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                // Load configuration
                _monitorDurationInSeconds = ConfigReader.GetMonitorDurationInSeconds();
                _processNamesToMonitor = ConfigReader.GetProcessNamesToMonitor();
                _maxRestarts = ConfigReader.GetMaxRestarts();
                _logFilePath = ConfigReader.GetLogFilePath();
                _restartStatusFilePath = ConfigReader.GetRestartStatusFilePath();
                _restartCountFilePath = ConfigReader.GetRestartCountFilePath();
                _exceptionFilePath = ConfigReader.GetExceptionFilePath();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_exceptionFilePath) ?? @"C:\Logs");
                    Directory.CreateDirectory(Path.GetDirectoryName(_restartStatusFilePath) ?? @"C:\Logs");
                    Directory.CreateDirectory(Path.GetDirectoryName(_restartCountFilePath) ?? @"C:\Logs");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(_exceptionFilePath,
                        $"{DateTime.Now} exception creating directory. we may not have create permission or dir exists {ex} {Environment.NewLine}");
                }

                // Load current restart count
                if (File.Exists(_restartCountFilePath))
                    _currentRestartCount = int.Parse(File.ReadAllText(_restartCountFilePath));
                else
                    _currentRestartCount = 0;
                
                if (_currentRestartCount < _maxRestarts) // do nothing
                {
                    File.WriteAllText(_restartCountFilePath, $"{_currentRestartCount++}");

                    // Start monitoring
                    var times = 10;
                    _timer = new Timer(MonitorProcesses, null, 0, Math.Min(1, _monitorDurationInSeconds / times) * 1000);
                    _shutdownTimer = new Timer(state =>
                    {
                        File.AppendAllLines(_restartStatusFilePath,
                            new[] { $"{DateTime.Now} all processes did not stop" });
                        RestartMachine();
                    }, null, _monitorDurationInSeconds * 1000, _monitorDurationInSeconds * 1000);
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("C:\\Logs\\SystemControlLog.txt",
                    $"Error restarting machine: {ex.Message}{Environment.NewLine}");
            }
        }

        protected override void OnStop()
        {
            _shutdownTimer?.Dispose();
            _timer?.Dispose();
        }

        private void MonitorProcesses(object state)
        {
            try
            {
                var statuses = new List<bool>();
                foreach (var processName in _processNamesToMonitor)
                {
                    var isRunning = Process.GetProcessesByName(processName).Any();
                    LogProcessStatus(processName, isRunning);
                    statuses.Add(isRunning);
                }

                // if all monitored processes are running, we can restart system
                if (statuses.All(x => x))
                {
                    File.AppendAllLines(_restartStatusFilePath, new[] { $"{DateTime.Now} all processes stopped" });
                    RestartMachine();
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(_logFilePath, $"Error: {ex.Message}{Environment.NewLine}");
            }
        }

        private static void RestartMachine()
        {
            try
            {
                // Execute the shutdown command with the /r flag to restart
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 0", // /r: restart, /t 0: no delay
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                File.AppendAllText("C:\\Logs\\SystemControlLog.txt",
                    $"Error restarting machine: {ex.Message}{Environment.NewLine}");
            }
        }

        private void LogProcessStatus(string processName, bool isRunning)
        {
            var status = isRunning ? "Running" : "Not Running";
            var logEntry = $"{DateTime.Now}: Process '{processName}' is {status}{Environment.NewLine}";
            File.AppendAllText(_logFilePath, logEntry);
        }
    }
}