namespace System.Windows.Forms;

public interface IWinFormsApplicationHost
{
    void Run(Form mainForm);

    DialogResult ShowDialog(Form form, IWin32Window? owner);

    void ExitThread();
}

/// <summary>
/// Optional typed application-loop capability for hosts that can isolate nested
/// or secondary WinForms UI threads from their process-wide native application.
/// </summary>
/// <remarks>
/// <see cref="Application"/> registers the returned context against the calling
/// managed thread for the duration of <see cref="Application.Run()"/>. Controls
/// created on that thread then marshal through the context instead of the host's
/// process-wide dispatcher, and <see cref="Application.ExitThread()"/> terminates
/// only that context.
/// </remarks>
public interface IWinFormsThreadApplicationHost
{
    IWinFormsApplicationThreadContext CreateThreadContext(Form? mainForm);
}

/// <summary>
/// Owns one typed portable WinForms application loop.
/// </summary>
public interface IWinFormsApplicationThreadContext : IWinFormsDispatcherHost, IDisposable
{
    void Run();

    void ExitThread();
}

public interface IWinFormsTimerHost
{
    IDisposable RegisterTimer(int intervalMilliseconds, Action callback);
}

/// <summary>
/// Optional typed idle-dispatch capability implemented by application hosts with
/// a native or portable UI message loop.
/// </summary>
/// <remarks>
/// Implementations enqueue the callback supplied to <see cref="TryBeginInvokeIdle"/>
/// once at their idle priority and return <see langword="false"/> when their
/// dispatcher can no longer accept work. The portable Forms layer owns the
/// <see cref="Application.Idle"/> event and coalesces outstanding requests.
/// </remarks>
public interface IWinFormsIdleHost
{
    bool TryBeginInvokeIdle(Action callback);
}

/// <summary>
/// Optional typed UI-thread dispatcher capability implemented by application hosts.
/// </summary>
/// <remarks>
/// The portable Forms layer owns delegate invocation and asynchronous-result state;
/// hosts only marshal strongly typed callbacks to their UI thread.
/// </remarks>
public interface IWinFormsDispatcherHost
{
    bool CheckAccess();

    void BeginInvoke(Action callback);

    void Invoke(Action callback);
}

/// <summary>
/// Optional typed drag/drop capability implemented by application hosts that can
/// run a native or portable UI drag session for a hosted Forms control.
/// </summary>
public interface IWinFormsDragDropHost
{
    DragDropEffects DoDragDrop(
        Control source,
        IDataObject data,
        DragDropEffects allowedEffects);
}

/// <summary>
/// Optional typed coordinate conversion capability for controls hosted inside a
/// native or portable top-level surface.
/// </summary>
public interface IWinFormsCoordinateHost
{
    bool TryPointToScreen(
        Control control,
        System.Drawing.Point point,
        out System.Drawing.Point screenPoint);

    bool TryPointToClient(
        Control control,
        System.Drawing.Point point,
        out System.Drawing.Point clientPoint);
}

/// <summary>
/// Optional typed drawing capability for controls hosted by a portable
/// presentation surface.
/// </summary>
/// <remarks>
/// The returned graphics object targets host-owned presentation state for the
/// supplied control. Hosts that do not own the control must return
/// <see langword="false"/> without creating a disconnected drawing surface.
/// </remarks>
public interface IWinFormsGraphicsHost
{
    bool TryCreateGraphics(
        Control control,
        out System.Drawing.Graphics graphics);
}

/// <summary>
/// Optional typed extension implemented by application hosts that can end a
/// modal loop after a form publishes a non-none <see cref="Form.DialogResult"/>.
/// </summary>
public interface IWinFormsModalDialogHost
{
    void RequestDialogCompletion(Form form);
}

/// <summary>
/// Optional typed modeless-window capability implemented by application hosts.
/// </summary>
/// <remarks>
/// The portable Forms layer owns form state and lifetime. Hosts associate that
/// state with their native or portable top-level without inspecting Win32 handles.
/// </remarks>
public interface IWinFormsWindowHost
{
    bool TryShow(Form form, IWin32Window owner);

    bool TrySetTopMost(Form form, bool topMost);
}

/// <summary>
/// Typed dialog-key entry point used by portable input hosts.
/// </summary>
public interface IWinFormsDialogKeyProcessor
{
    bool TryProcessDialogKey(Keys keyData, Control? focusedControl);
}
