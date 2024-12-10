using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms; // Required for working with WinForms UI components

public class Logger : IDisposable
{
    private readonly LogLevel logLevelThreshold;
    private static readonly object lockObject = new();
    private static Logger? instance;
    private readonly AutoResetEvent logSignal = new(false);
    private readonly Thread logThread;
    private bool isRunning = true;
    private readonly Form mainForm;
    private readonly ProgressBar progressBar;
    private readonly RichTextBox logTextBox;
    private readonly System.Timers.Timer uiUpdateTimer;
    private readonly CircularBuffer<(int sequence, LogLevel level, string message, Color? textColor)> logQueue;
    private int logSequence = 0;
    private readonly bool verbose;

    // List to store all log entries for the log dump
    private readonly List<string> allLogEntries = new List<string>();

    private Logger(Form mainForm, ProgressBar progressBar, RichTextBox logTextBox, LogLevel logLevelThreshold, bool verbose)
    {
        this.mainForm = mainForm;
        this.progressBar = progressBar;
        this.logTextBox = logTextBox;
        this.verbose = verbose;

        // Set log level threshold based on verbose option
        this.logLevelThreshold = verbose ? LogLevel.DEBUG : logLevelThreshold;

        // Initialize the circular buffer with a size of 1024 (can be adjusted based on performance needs)
        logQueue = new CircularBuffer<(int sequence, LogLevel level, string message, Color? textColor)>(1024);

        uiUpdateTimer = new System.Timers.Timer(100);
        uiUpdateTimer.Elapsed += (sender, e) => FlushLogQueueToUI();
        uiUpdateTimer.Start();

        logThread = new Thread(LoggingThreadMethod) { IsBackground = true };
        logThread.Start();

        // Subscribe to application exit and unhandled exception events
        Application.ApplicationExit += Application_ApplicationExit;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    public void Dispose()
    {
        Stop();
        WriteLogDump();
    }

    public static Logger GetInstance(Form mainForm, ProgressBar progressBar, RichTextBox logTextBox, LogLevel logLevelThreshold = LogLevel.INFO, bool verbose = false)
    {
        if (instance == null)
        {
            lock (lockObject)
            {
                instance ??= new Logger(mainForm, progressBar, logTextBox, logLevelThreshold, verbose);
            }
        }
        return instance;
    }

    // Static method to log messages
    public static void Log(int? progress, string message, LogLevel level = LogLevel.INFO, Color? textColor = null)
    {
        instance?.LogInstance(message, progress, level, textColor);
    }

    public static void LogDebug(int? progress, string message)
    {
        Log(progress, message, LogLevel.DEBUG);
    }

    public static void LogInfo(int? progress, string message, Color? textColor = null)
    {
        Log(progress, message, LogLevel.INFO, textColor);
    }

    public static void LogWarning(int? progress, string message)
    {
        Log(progress, message, LogLevel.WARN);
    }

    public static void LogError(int? progress, string message)
    {
        Log(progress, message, LogLevel.ERROR);
    }

    private void LoggingThreadMethod()
    {
        while (isRunning)
        {
            if (logSignal.WaitOne(1000) || logQueue.Count > 0)
            {
                FlushLogQueue();
            }
        }
    }

    public void LogInstance(string message, int? progress = null, LogLevel level = LogLevel.INFO, Color? textColor = null)
    {
        int currentSequence = Interlocked.Increment(ref logSequence);
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logEntry = $"{timestamp} - {message}";

        // Store all log entries for the log dump
        lock (allLogEntries)
        {
            allLogEntries.Add(logEntry);
        }

        // Only process log entries above the threshold for UI display
        if (level < logLevelThreshold) return;

        logQueue.Enqueue((currentSequence, level, logEntry, textColor));

        if (progress.HasValue)
        {
            mainForm.BeginInvoke((MethodInvoker)delegate { progressBar.Value = progress.Value; });
        }

        logSignal.Set();
        AdjustTimerInterval();
    }

    private void AdjustTimerInterval()
    {
        int queueCount = logQueue.Count;
        uiUpdateTimer.Interval = queueCount <= 100 ? 100 : 100 + (queueCount - 100) / 10;
    }

    private void FlushLogQueueToUI()
    {
        if (!mainForm.IsDisposed)
        {
            mainForm.BeginInvoke((MethodInvoker)delegate
            {
                // Dequeue messages in a thread-safe manner and ensure they're processed sequentially
                var sortedLogs = new SortedList<int, (int sequence, LogLevel level, string message, Color? textColor)>();

                while (sortedLogs.Count < CalculateDynamicBatchSize() && logQueue.TryDequeue(out var logEntry))
                {
                    sortedLogs.Add(logEntry.sequence, logEntry);
                }

                foreach (var logEntry in sortedLogs.Values)
                {
                    ProcessLogEntry(logEntry);
                }
            });
        }
    }

    private int CalculateDynamicBatchSize()
    {
        int queueCount = logQueue.Count;
        return queueCount <= 100 ? 1 : 10 + (queueCount - 100) / 100 * 10;
    }

    private void FlushLogQueue()
    {
        while (logQueue.TryDequeue(out var logEntry))
        {
            ProcessLogEntry(logEntry);
        }
    }

    private void ProcessLogEntry((int sequence, LogLevel level, string message, Color? textColor) logEntry)
    {
        // Ensure any UI updates are done on the UI thread
        if (mainForm.InvokeRequired)
        {
            mainForm.Invoke((MethodInvoker)delegate { ProcessLogEntry(logEntry); });
            return;
        }

        string messageText = logEntry.message;

        if (messageText == null)
        {
            return;
        }

        Color textColor = logEntry.textColor ?? GetColorForLogLevel(logEntry.level);

        if (messageText.Contains("UPDATE:"))
        {
            // For "UPDATE:", modify the last line without appending a new line
            string updatedMessage = messageText.Replace("UPDATE:", "").Trim();
            string[] lines = logTextBox.Lines;

            if (lines.Length > 0)
            {
                int lastLineStartIndex = logTextBox.GetFirstCharIndexFromLine(lines.Length - 1);
                int lastLineLength = lines[^1].Length;

                // Select the last line and replace it with the updated message
                logTextBox.Select(lastLineStartIndex, lastLineLength);
                logTextBox.SelectedText = updatedMessage;

                // Set the color for the updated line
                logTextBox.SelectionColor = textColor;
            }

            // Scroll to the end to ensure the updated line is visible
            logTextBox.SelectionStart = logTextBox.Text.Length;
            logTextBox.ScrollToCaret();
        }
        else
        {
            // Append a new line before the message if the last character isn't a newline (and the textbox isn't empty)
            if (logTextBox.TextLength > 0 && !logTextBox.Text.EndsWith(Environment.NewLine))
            {
                logTextBox.AppendText(Environment.NewLine); // Add a new line before appending the message
            }

            int start = logTextBox.TextLength;
            logTextBox.AppendText(messageText);

            // Select the appended text to set its color
            logTextBox.Select(start, messageText.Length);
            logTextBox.SelectionColor = textColor;

            // Ensure the caret is at the end and the last line is visible
            logTextBox.SelectionStart = logTextBox.Text.Length;
            logTextBox.ScrollToCaret();
        }
    }

    public void ClearLogs()
    {
        logTextBox.Clear();
    }

    private static Color GetColorForLogLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.DEBUG => Color.DarkCyan,
            LogLevel.INFO => Color.Black,
            LogLevel.WARN => Color.Orange,
            LogLevel.ERROR => Color.Red,
            _ => Color.Black,
        };
    }

    public void Stop()
    {
        isRunning = false;
        logSignal.Set(); // Ensure the logging thread can exit
        logThread.Join();
        uiUpdateTimer.Stop();
        uiUpdateTimer.Dispose();
        FlushLogQueueToUI(); // Flush remaining messages
        logSignal.Dispose();
    }

    // Event handler for application exit
    private void Application_ApplicationExit(object sender, EventArgs e)
    {
        WriteLogDump();
    }

    // Event handler for unhandled exceptions
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteLogDump();
    }

    // Method to write the log dump to a file
    private void WriteLogDump()
    {
        try
        {
            // Determine the log file path
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string logFolderPath = Path.Combine(documentsPath, "procedure", "logs");
            Directory.CreateDirectory(logFolderPath);

            string logFileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string logFilePath = Path.Combine(logFolderPath, logFileName);

            // Write all log entries to the file
            lock (allLogEntries)
            {
                File.WriteAllLines(logFilePath, allLogEntries);
            }

            // Limit the number of log files to a maximum of 10
            LimitLogFiles(logFolderPath, maxFiles: 10);
        }
        catch (Exception ex)
        {
            // Handle exceptions if necessary (e.g., log to Event Viewer)
        }
    }

    // Helper method to limit the number of log files
    private void LimitLogFiles(string logFolderPath, int maxFiles)
    {
        try
        {
            var logFiles = Directory.GetFiles(logFolderPath, "log_*.txt");

            if (logFiles.Length > maxFiles)
            {
                // Order files by the timestamp in the filename
                var orderedFiles = logFiles.OrderBy(f =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(f); // e.g., "log_20231017_124500"
                    string timestampPart = fileName.Substring(4); // Remove "log_"
                    if (DateTime.TryParseExact(timestampPart, "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out DateTime timestamp))
                    {
                        return timestamp;
                    }
                    else
                    {
                        // If parsing fails, use file creation time
                        return File.GetCreationTime(f);
                    }
                }).ToList();

                int filesToDelete = orderedFiles.Count - maxFiles;

                // Delete the oldest files
                for (int i = 0; i < filesToDelete; i++)
                {
                    File.Delete(orderedFiles[i]);
                }
            }
        }
        catch (Exception ex)
        {
            // Handle exceptions if necessary
        }
    }
}

// Circular buffer implementation
public class CircularBuffer<T>
{
    private readonly T[] buffer;
    private int head;
    private int tail;
    private int size;

    public CircularBuffer(int capacity)
    {
        buffer = new T[capacity];
        head = 0;
        tail = 0;
        size = 0;
    }

    public int Count => size;

    public void Enqueue(T item)
    {
        buffer[tail] = item;
        tail = (tail + 1) % buffer.Length;
        if (size < buffer.Length)
        {
            size++;
        }
        else
        {
            head = (head + 1) % buffer.Length; // Overwrite the oldest item if full
        }
    }

    public bool TryDequeue(out T item)
    {
        if (size == 0)
        {
            item = default;
            return false;
        }

        item = buffer[head];
        head = (head + 1) % buffer.Length;
        size--;
        return true;
    }
}

public enum LogLevel { DEBUG, INFO, WARN, ERROR }
