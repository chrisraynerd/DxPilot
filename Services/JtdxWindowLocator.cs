using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace JtdxAutoResume.V3.Services;

public sealed class JtdxWindowLocator
{
    public JtdxWindowInfo? FindMainWindow(string titleMatch)
    {
        titleMatch = string.IsNullOrWhiteSpace(titleMatch) ? "JTDX" : titleMatch.Trim();
        JtdxWindowInfo? best = null;
        var bestScore = 0;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var title = GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title) || !title.Contains(titleMatch, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!GetWindowRect(hwnd, out var rect))
                return true;

            GetWindowThreadProcessId(hwnd, out var processId);
            var className = GetClassNameText(hwnd);
            var processName = GetProcessName(processId);
            var score = ScoreCandidate(title, className, processName, titleMatch);
            if (score > bestScore)
            {
                bestScore = score;
                best = new JtdxWindowInfo(hwnd, title, processId, rect.Left, rect.Top, rect.Right, rect.Bottom);
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";

        var sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassNameText(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static int ScoreCandidate(string title, string className, string processName, string titleMatch)
    {
        var score = 0;
        if (title.StartsWith("JTDX", StringComparison.OrdinalIgnoreCase))
            score += 100;
        if (title.Contains("by HF community", StringComparison.OrdinalIgnoreCase))
            score += 50;
        if (title.Contains("WSJT-X", StringComparison.OrdinalIgnoreCase))
            score += 25;
        if (title.Contains(titleMatch, StringComparison.OrdinalIgnoreCase))
            score += 10;
        if (processName.Contains("jtdx", StringComparison.OrdinalIgnoreCase))
            score += 100;
        if (processName.Contains("wsjt", StringComparison.OrdinalIgnoreCase))
            score += 40;

        if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            || className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase)
            || title.Contains("File Explorer", StringComparison.OrdinalIgnoreCase)
            || title.Contains("JtdxAutoResume", StringComparison.OrdinalIgnoreCase)
            || title.Contains("JTDX Auto TX", StringComparison.OrdinalIgnoreCase))
        {
            score -= 250;
        }

        return score;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

public sealed record JtdxWindowInfo(IntPtr Handle, string Title, uint ProcessId, int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
