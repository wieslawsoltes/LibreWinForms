// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Runtime.ExceptionServices;

namespace System.Windows.Forms;

public sealed partial class Application
{
    internal abstract unsafe partial class ThreadContext
    {
        private PortableModalFrame? _portableModalFrame;
        private bool _drainingPortableModalFrames;

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
