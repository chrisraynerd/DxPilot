using System.Runtime.InteropServices;

namespace JtdxAutoResume.V3.Services;

public sealed class ScreenClicker
{
    private const int VkSpace = 0x20;
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;

    public void MoveClickRestore(int x, int y)
    {
        GetCursorPos(out var original);
        SetCursorPos(x, y);
        mouse_event(MouseEventFlags.LeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(MouseEventFlags.LeftUp, 0, 0, 0, UIntPtr.Zero);
        SetCursorPos(original.X, original.Y);
    }

    public void MoveDoubleClickRestore(int x, int y)
    {
        GetCursorPos(out var original);
        SetCursorPos(x, y);
        for (var i = 0; i < 2; i++)
        {
            mouse_event(MouseEventFlags.LeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(35);
            mouse_event(MouseEventFlags.LeftUp, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(85);
        }
        SetCursorPos(original.X, original.Y);
    }

    public (int x, int y) GetCursorPosition()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public async Task<(int x, int y)?> PickPointAsync(CancellationToken cancellationToken = default)
    {
        await WaitForHotkeysReleasedAsync(cancellationToken);
        await WaitForHotkeyAsync(cancellationToken);
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public void KeepAwake(bool enabled)
    {
        SetThreadExecutionState(enabled
            ? ExecutionState.EsContinuous | ExecutionState.EsSystemRequired | ExecutionState.EsDisplayRequired
            : ExecutionState.EsContinuous);
    }

    private static async Task WaitForHotkeyAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if ((GetAsyncKeyState(VkSpace) & 0x8000) != 0)
                return;
            if ((GetAsyncKeyState(VkReturn) & 0x8000) != 0)
                return;
            if ((GetAsyncKeyState(VkEscape) & 0x8000) != 0)
                throw new OperationCanceledException("Pick cancelled.");

            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WaitForHotkeysReleasedAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var spaceDown = (GetAsyncKeyState(VkSpace) & 0x8000) != 0;
            var enterDown = (GetAsyncKeyState(VkReturn) & 0x8000) != 0;
            if (!spaceDown && !enterDown)
                return;

            await Task.Delay(10, cancellationToken);
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(MouseEventFlags flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(ExecutionState flags);

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004
    }

    [Flags]
    private enum ExecutionState : uint
    {
        EsSystemRequired = 0x00000001,
        EsDisplayRequired = 0x00000002,
        EsContinuous = 0x80000000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
