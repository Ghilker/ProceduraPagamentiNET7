using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

public sealed class Logger : IDisposable
{
    private static readonly object instanceLock = new();
    private static Logger? instance;

    private readonly object queueLock = new();

    private readonly LogLevel logLevelThreshold;
    private readonly CircularBuffer<(LogLevel level, string message, Color? textColor)> logQueue;
    private readonly List<string> allLogEntries = new();

    private readonly Form mainForm;
    private readonly ProgressBar progressBar;
    private readonly RichTextBox logTextBox;

    private int logSequence;
    private readonly bool verbose;
    private bool isDisposed;
    private int uiFlushPending;

    private const int MaxTextLength = 500_000;

    private Logger(
        Form mainForm,
        ProgressBar progressBar,
        RichTextBox logTextBox,
        LogLevel logLevelThreshold,
        bool verbose)
    {
        this.mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
        this.progressBar = progressBar ?? throw new ArgumentNullException(nameof(progressBar));
        this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
        this.verbose = verbose;

        this.logLevelThreshold = verbose ? LogLevel.DEBUG : logLevelThreshold;

        logQueue = new CircularBuffer<(LogLevel level, string message, Color? textColor)>(2048);

        Application.ApplicationExit += Application_ApplicationExit;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    public static Logger GetInstance(
        Form mainForm,
        ProgressBar progressBar,
        RichTextBox logTextBox,
        LogLevel logLevelThreshold = LogLevel.INFO,
        bool verbose = false)
    {
        if (instance != null)
            return instance;

        lock (instanceLock)
        {
            instance ??= new Logger(mainForm, progressBar, logTextBox, logLevelThreshold, verbose);
            return instance;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    // ============================
    //   METODI STATICI DI LOG
    // ============================

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

    // ============================
    //   LOG INSTANCE
    // ============================

    public void LogInstance(string message, int? progress = null, LogLevel level = LogLevel.INFO, Color? textColor = null)
    {
        if (isDisposed || message == null)
            return;

        Interlocked.Increment(ref logSequence);
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logEntry = $"{timestamp} - {message}";

        lock (allLogEntries)
        {
            allLogEntries.Add(logEntry);
        }

        if (level < logLevelThreshold)
            return;

        lock (queueLock)
        {
            logQueue.Enqueue((level, logEntry, textColor));
        }

        if (progress.HasValue)
        {
            UpdateProgressSafe(progress.Value);
        }

        RequestFlush();
    }

    private void UpdateProgressSafe(int value)
    {
        if (isDisposed)
            return;

        if (mainForm.IsDisposed || !mainForm.IsHandleCreated)
            return;

        try
        {
            mainForm.BeginInvoke((MethodInvoker)delegate
            {
                if (progressBar.IsDisposed)
                    return;

                int clamped = Math.Max(progressBar.Minimum, Math.Min(value, progressBar.Maximum));
                if (clamped != progressBar.Value)
                    progressBar.Value = clamped;
            });
        }
        catch
        {
        }
    }

    private int QueueCount
    {
        get
        {
            lock (queueLock)
            {
                return logQueue.Count;
            }
        }
    }

    // ============================
    //   FLUSH SU UI (COALESCED)
    // ============================

    private void RequestFlush()
    {
        if (isDisposed)
            return;

        if (mainForm.IsDisposed || !mainForm.IsHandleCreated)
            return;

        if (Interlocked.Exchange(ref uiFlushPending, 1) == 1)
            return;

        try
        {
            mainForm.BeginInvoke((MethodInvoker)delegate
            {
                uiFlushPending = 0;
                FlushLogQueueToUI();
            });
        }
        catch
        {
            uiFlushPending = 0;
        }
    }

    private void FlushLogQueueToUI()
    {
        if (isDisposed || mainForm.IsDisposed || logTextBox.IsDisposed)
            return;

        var batch = DequeueAll();
        if (batch.Count == 0)
            return;

        logTextBox.SuspendLayout();
        try
        {
            TrimLogIfNeeded();

            foreach (var entry in batch)
            {
                ProcessLogEntry(entry);
            }
        }
        finally
        {
            logTextBox.ResumeLayout();
        }
    }

    private List<(LogLevel level, string message, Color? textColor)> DequeueAll()
    {
        var result = new List<(LogLevel level, string message, Color? textColor)>();

        lock (queueLock)
        {
            while (logQueue.TryDequeue(out var item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private void TrimLogIfNeeded()
    {
        if (logTextBox.TextLength <= MaxTextLength)
            return;

        int excess = logTextBox.TextLength - MaxTextLength;
        if (excess <= 0)
            return;

        try
        {
            logTextBox.Select(0, excess);
            logTextBox.SelectedText = string.Empty;
        }
        catch
        {
        }
    }

    private void ProcessLogEntry((LogLevel level, string message, Color? textColor) logEntry)
    {
        if (isDisposed || logTextBox.IsDisposed)
            return;

        string messageText = logEntry.message;
        if (messageText == null)
            return;

        Color color = logEntry.textColor ?? GetColorForLogLevel(logEntry.level);

        if (messageText.Contains("UPDATE:"))
        {
            string updatedMessage = messageText.Replace("UPDATE:", "").Trim();
            string[] lines = logTextBox.Lines;

            if (lines.Length > 0)
            {
                int lastLineStartIndex = logTextBox.GetFirstCharIndexFromLine(lines.Length - 1);
                int lastLineLength = lines[^1].Length;

                logTextBox.Select(lastLineStartIndex, lastLineLength);
                logTextBox.SelectedText = updatedMessage;
                logTextBox.Select(lastLineStartIndex, updatedMessage.Length);
                logTextBox.SelectionColor = color;
            }

            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();
        }
        else
        {
            if (logTextBox.TextLength > 0 && !logTextBox.Text.EndsWith(Environment.NewLine))
            {
                logTextBox.AppendText(Environment.NewLine);
            }

            int start = logTextBox.TextLength;
            logTextBox.AppendText(messageText);

            logTextBox.Select(start, messageText.Length);
            logTextBox.SelectionColor = color;

            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();
        }
    }

    public void ClearLogs()
    {
        if (isDisposed)
            return;

        if (!logTextBox.IsDisposed)
        {
            logTextBox.Clear();
        }

        lock (queueLock)
        {
            logQueue.Clear();
        }
    }

    private static Color GetColorForLogLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.DEBUG => Color.DarkCyan,
            LogLevel.INFO => Color.Black,
            LogLevel.WARN => Color.Orange,
            LogLevel.ERROR => Color.Red,
            _ => Color.Black
        };
    }

    // ============================
    //   STOP / EXIT / DUMP
    // ============================

    public void Stop()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        try
        {
            FlushLogQueueToUI();
        }
        catch
        {
        }

        WriteLogDump();

        Application.ApplicationExit -= Application_ApplicationExit;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
    }

    private void Application_ApplicationExit(object? sender, EventArgs e)
    {
        Stop();
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        WriteLogDump();
    }

    private void WriteLogDump()
    {
        List<string> snapshot;

        lock (allLogEntries)
        {
            if (allLogEntries.Count == 0)
                return;

            snapshot = new List<string>(allLogEntries);
        }

        try
        {
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string logFolderPath = Path.Combine(documentsPath, "procedure", "logs");
            Directory.CreateDirectory(logFolderPath);

            string logFileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string logFilePath = Path.Combine(logFolderPath, logFileName);

            File.WriteAllLines(logFilePath, snapshot);

            LimitLogFiles(logFolderPath, maxFiles: 10);
        }
        catch
        {
        }
    }

    private void LimitLogFiles(string logFolderPath, int maxFiles)
    {
        try
        {
            var logFiles = Directory.GetFiles(logFolderPath, "log_*.txt");
            if (logFiles.Length <= maxFiles)
                return;

            Array.Sort(logFiles, (a, b) =>
            {
                DateTime ta = File.GetCreationTime(a);
                DateTime tb = File.GetCreationTime(b);
                return ta.CompareTo(tb);
            });

            int toDelete = logFiles.Length - maxFiles;
            for (int i = 0; i < toDelete; i++)
            {
                File.Delete(logFiles[i]);
            }
        }
        catch
        {
        }
    }
}

// ============================
//   CIRCULAR BUFFER
// ============================

public class CircularBuffer<T>
{
    private readonly T[] buffer;
    private int head;
    private int tail;
    private int size;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

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
            head = (head + 1) % buffer.Length;
        }
    }

    public bool TryDequeue(out T item)
    {
        if (size == 0)
        {
            item = default!;
            return false;
        }

        item = buffer[head];
        head = (head + 1) % buffer.Length;
        size--;
        return true;
    }

    public void Clear()
    {
        head = 0;
        tail = 0;
        size = 0;
    }
}

public enum LogLevel
{
    DEBUG,
    INFO,
    WARN,
    ERROR
}
