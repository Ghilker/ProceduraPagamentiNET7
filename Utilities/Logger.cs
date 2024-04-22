using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Forms; // Required for working with WinForms UI components
public class Logger : IDisposable
{
    private readonly LogLevel logLevelThreshold = LogLevel.INFO;
    private static readonly object lockObject = new();
    private static Logger? instance;
    private readonly AutoResetEvent logSignal = new(false);
    private readonly Thread logThread;
    private bool isRunning = true;
    private readonly Form mainForm;
    private readonly ProgressBar progressBar;
    private readonly RichTextBox logTextBox;
    private readonly System.Timers.Timer uiUpdateTimer;
    private readonly ConcurrentQueue<(int sequence, LogLevel level, string message)> logQueue = new();
    private int logSequence = 0;

    private Logger(Form mainForm, ProgressBar progressBar, RichTextBox logTextBox, LogLevel logLevelThreshold)
    {
        this.mainForm = mainForm;
        this.progressBar = progressBar;
        this.logTextBox = logTextBox;
        this.logLevelThreshold = logLevelThreshold;

        uiUpdateTimer = new System.Timers.Timer(100);
        uiUpdateTimer.Elapsed += (sender, e) => FlushLogQueueToUI();
        uiUpdateTimer.Start();

        logThread = new Thread(LoggingThreadMethod) { IsBackground = true };
        logThread.Start();
    }

    public void Dispose()
    {
        Stop();
    }

    public static Logger GetInstance(Form mainForm, ProgressBar progressBar, RichTextBox logTextBox, LogLevel logLevelThreshold = LogLevel.INFO)
    {
        if (instance == null)
        {
            lock (lockObject)
            {
                instance ??= new Logger(mainForm, progressBar, logTextBox, logLevelThreshold);
            }
        }
        return instance;
    }

    // Static method to log messages
    public static void Log(int? progress, string message, LogLevel level = LogLevel.INFO)
    {
        instance?.LogInstance(message, progress, level);
    }

    public static void LogDebug(int? progress, string message)
    {
        Log(progress, message, LogLevel.DEBUG);
    }

    public static void LogInfo(int? progress, string message)
    {
        Log(progress, message, LogLevel.INFO);
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
            if (logSignal.WaitOne(1000) || !logQueue.IsEmpty)
            {
                FlushLogQueue();
            }
        }
    }

    public void LogInstance(string message, int? progress = null, LogLevel level = LogLevel.INFO)
    {
        if (level < logLevelThreshold) return;

        int currentSequence = Interlocked.Increment(ref logSequence);
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logEntry = $"{timestamp} - {message}";
        logQueue.Enqueue((currentSequence, level, logEntry));

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
        if (queueCount <= 100)
        {
            uiUpdateTimer.Interval = 100; // Fast interval for small or moderate queues
        }
        else
        {
            // As the queue size increases, the timer interval increases exponentially
            uiUpdateTimer.Interval = 100 + (queueCount - 100) / 10;
        }
    }

    private void FlushLogQueueToUI()
    {
        int batchCount = CalculateDynamicBatchSize(); // Increase dump size if queue is large

        if (!mainForm.IsDisposed)
        {
            mainForm.BeginInvoke((MethodInvoker)delegate
            {
                for (int i = 0; i < batchCount && logQueue.TryDequeue(out var logEntry); i++)
                {
                    ProcessLogEntry(logEntry);
                }
            });
        }
    }
    private int CalculateDynamicBatchSize()
    {
        int queueCount = logQueue.Count;
        if (queueCount <= 100)
        {
            return 1; // No batching, process one at a time
        }
        else
        {
            // Increase batch size as the queue grows larger
            // Example: for every additional 100 entries, increase batch size by 10
            return 10 + (queueCount - 100) / 100 * 10;
        }
    }

    private void FlushLogQueue()
    {
        while (logQueue.TryDequeue(out var logEntry))
        {
            ProcessLogEntry(logEntry);
        }
    }

    private void ProcessLogEntry((int sequence, LogLevel level, string message) logEntry)
    {
        // Ensure any UI updates are done on the UI thread
        if (mainForm.InvokeRequired)
        {
            mainForm.Invoke((MethodInvoker)delegate { ProcessLogEntry(logEntry); });
            return;
        }

        string messageText = logEntry.message;
        Color textColor = GetColorForLogLevel(logEntry.level);

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
}
public enum LogLevel { DEBUG, INFO, WARN, ERROR }