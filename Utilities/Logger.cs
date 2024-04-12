using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Forms; // Required for working with WinForms UI components
public enum LogLevel { DEBUG, INFO, WARN, ERROR }
public class Logger
{


    private LogLevel logLevelThreshold = LogLevel.INFO; // Default log level threshold

    private static Logger instance;
    private AutoResetEvent logSignal = new AutoResetEvent(false);
    private Thread logThread;
    private bool isRunning = true;
    private Form mainForm;
    private ProgressBar progressBar;
    private RichTextBox logTextBox; // Changed to RichTextBox for text color formatting
    private System.Timers.Timer uiUpdateTimer;
    private ConcurrentQueue<(int sequence, LogLevel level, string message)> logQueue = new ConcurrentQueue<(int sequence, LogLevel level, string message)>();
    private int logSequence = 0;

    private Logger(Form mainForm, ProgressBar progressBar, RichTextBox logTextBox, LogLevel logLevelThreshold)
    {
        this.mainForm = mainForm;
        this.progressBar = progressBar;
        this.logTextBox = logTextBox;
        this.logLevelThreshold = logLevelThreshold;

        uiUpdateTimer = new System.Timers.Timer(100); // Adjusted for more efficient batch processing
        uiUpdateTimer.Elapsed += (sender, e) => FlushLogQueueToUI();
        uiUpdateTimer.Start();

        logThread = new Thread(LoggingThreadMethod)
        {
            IsBackground = true
        };
        logThread.Start();
    }
    public static Logger GetInstance(Form mainForm, ProgressBar progressBar, RichTextBox logTextBox, LogLevel logLevelThreshold = LogLevel.INFO)
    {
        if (instance == null)
        {
            instance = new Logger(mainForm, progressBar, logTextBox, logLevelThreshold);
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
        while (isRunning || !logQueue.IsEmpty)
        {
            logSignal.WaitOne(); // Wait for a signal to check the queue again
        }
    }

    public void LogInstance(string message, int? progress = null, LogLevel level = LogLevel.INFO)
    {
        if (level < logLevelThreshold)
        {
            return; // Skip logging messages below the threshold
        }

        int currentSequence = Interlocked.Increment(ref logSequence);
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logEntry = $"{timestamp} - {message}";
        logQueue.Enqueue((currentSequence, level, logEntry));
        logSignal.Set();

        if (progress.HasValue)
        {
            mainForm.BeginInvoke((MethodInvoker)delegate
            {
                progressBar.Value = progress.Value;
            });
        }
    }

    private void FlushLogQueueToUI()
    {
        if (!mainForm.IsDisposed)
        {
            mainForm.BeginInvoke((MethodInvoker)delegate
            {
                try
                {
                    while (logQueue.TryDequeue(out var logEntry))
                    {
                        ProcessLogEntry(logEntry);
                    }
                }
                catch (Exception ex)
                {
                    // Log or handle the exception as needed
                }
            });
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
                int lastLineLength = lines[lines.Length - 1].Length;

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

    private Color GetColorForLogLevel(LogLevel level)
    {
        switch (level)
        {
            case LogLevel.DEBUG:
                return Color.DarkCyan;
            case LogLevel.INFO:
                return Color.Black;
            case LogLevel.WARN:
                return Color.Orange;
            case LogLevel.ERROR:
                return Color.Red;
            default:
                return Color.Black;
        }
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
