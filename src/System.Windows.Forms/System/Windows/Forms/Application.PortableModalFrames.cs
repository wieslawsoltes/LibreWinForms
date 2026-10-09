// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Runtime.ExceptionServices;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public sealed partial class Application
{
    internal static LibreMessageBoxResult ShowPortableModalMessageBox(
        ILibreModalMessageBoxService service, in LibreMessageBoxRequest request)
    {
        ThreadContext.PortableMessageBoxModalScope scope = ThreadContext.FromCurrent().BeginPortableMessageBoxModalLoop();
        bool failed = false;
        try
        {
            return service.Show(request, scope);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            try { scope.Release(); }
            catch when (failed) { } // Preserve the original service/provider failure.
        }
    }

    internal abstract unsafe partial class ThreadContext
    {
        private PortableModalFrame? _portableModalFrame;
        private bool _drainingPortableModalFrames;

        internal PortableMessageBoxModalScope BeginPortableMessageBoxModalLoop()
        {
            BeginModalMessageLoop(null);
            PortableModalFrame? frame = null;
            try
            {
                frame = PushPortableModalFrame(null);
                PortableModalFrame retained = frame;
                return new(frame.Completion, () => ReleasePortableModalFrame(retained, null));
            }
            catch
            {
                try
                {
                    if (frame is null) EndModalMessageLoop(null);
                    else ReleasePortableModalFrame(frame, null);
                }
                catch { }
                throw;
            }
        }

        internal sealed class PortableMessageBoxModalScope(
            PortableModalCompletion completion, Action release) : ILibreMessageBoxModalLifecycle
        {
            private readonly int _threadId = Environment.CurrentManagedThreadId;
            private bool _begun;
            private bool _releaseRequested;

            public void Begin(ILibreModalWindow? exactWindow)
            {
                VerifyAccess();
                if (_begun || _releaseRequested)
                    throw new InvalidOperationException("A message-box modal generation cannot begin twice or after release.");
                _begun = true;
                completion.Begin(exactWindow);
            }

            public void AfterSourceRelease(Action cleanup)
            {
                VerifyAccess();
                ArgumentNullException.ThrowIfNull(cleanup);
                if (_releaseRequested)
                    throw new InvalidOperationException("Message-box cleanup must be owned before release.");
                completion.AfterSourceRelease(cleanup);
            }

            internal void Release()
            {
                VerifyAccess();
                if (_releaseRequested) return;
                _releaseRequested = true; // Native End can reenter; never retry an uncertain token.
                release();
            }

            private void VerifyAccess()
            {
                if (_threadId != Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException("Message-box modal ownership belongs to its creating thread.");
            }
        }

        private PortableModalFrame PushPortableModalFrame(Form? form)
        {
            var frame = new PortableModalFrame(form?.AttachPortableModalCompletion() ?? new(),
                _threadWindows, _portableModalFrame);
            _portableModalFrame = frame;
            return frame;
        }

        private void ReleasePortableModalFrame(PortableModalFrame frame, ApplicationContext? context)
        {
            frame.Context = context;
            frame.Completion.ReleaseNative(() =>
            {
                frame.NativeReleased = true;
                DrainPortableModalFrames();
            });
        }

        private void DrainPortableModalFrames()
        {
            if (_drainingPortableModalFrames) return;
            _drainingPortableModalFrames = true;
            ExceptionDispatchInfo? first = null;
            try
            {
                while (_portableModalFrame is { NativeReleased: true } frame)
                {
                    if (!ReferenceEquals(_threadWindows, frame.Windows))
                        throw new InvalidOperationException("The source dialog lost its original modal window frame.");
                    // Detach before re-enable/activation callbacks can enter a
                    // new source frame. Its native proof is independent.
                    _portableModalFrame = frame.Previous;
                    try { EndModalMessageLoop(frame.Context); }
                    catch (Exception failure) { first ??= ExceptionDispatchInfo.Capture(failure); }
                    try { frame.Completion.ReleaseSource(); }
                    catch (Exception failure) { first ??= ExceptionDispatchInfo.Capture(failure); }
                }
            }
            finally { _drainingPortableModalFrames = false; }
            first?.Throw();
        }

        private sealed class PortableModalFrame(
            PortableModalCompletion completion, ThreadWindows? windows, PortableModalFrame? previous)
        {
            internal PortableModalCompletion Completion { get; } = completion;
            internal ThreadWindows? Windows { get; } = windows;
            internal PortableModalFrame? Previous { get; } = previous;
            internal ApplicationContext? Context { get; set; }
            internal bool NativeReleased { get; set; }
        }
    }
}
#endif
