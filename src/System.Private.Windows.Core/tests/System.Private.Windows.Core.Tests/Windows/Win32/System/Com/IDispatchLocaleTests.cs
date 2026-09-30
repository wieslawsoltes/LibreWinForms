// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Variant;

namespace Windows.Win32.System.Com.Tests;

public unsafe class IDispatchLocaleTests
{
    [Theory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("ja-JP", false)]
    [InlineData("ja-JP", true)]
    [InlineData("ar-SA", false)]
    [InlineData("ar-SA", true)]
    [InlineData("", false)]
    [InlineData("", true)]
    public void AutomationHelpersForwardTheCallingThreadLocale(string cultureName, bool setProperty)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            VerifyHelper(setProperty);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            VerifyHelper(setProperty);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    private static void VerifyHelper(bool setProperty)
    {
        // This is a test-owned ABI recorder, not an OS COM server. Invoke the
        // actual shipped helper methods and record their real vtable arguments.
        // No CoInitialize, native activation, GDI, or IUnknown lifetime is used.
        void** table = stackalloc void*[7];
        table[5] = (delegate* unmanaged[Stdcall]<IDispatch*, Guid*, PWSTR*, uint, uint, int*, HRESULT>)&GetNames;
        table[6] = (delegate* unmanaged[Stdcall]<IDispatch*, int, Guid*, uint, DISPATCH_FLAGS, DISPPARAMS*, VARIANT*, EXCEPINFO*, uint*, HRESULT>)&Invoke;
        Recorder recorder = new() { Vtable = table };
        IDispatch* dispatch = (IDispatch*)&recorder;
        uint expected = OperatingSystem.IsWindows()
            ? PInvokeCore.GetThreadLocale()
            : (uint)CultureInfo.CurrentCulture.LCID;

        if (setProperty)
        {
            Assert.Equal(HRESULT.S_FALSE, dispatch->SetPropertyValue(456, default, out string? error));
            Assert.Null(error);
            Assert.Equal(expected, recorder.InvokeLcid);
            Assert.Equal(1, recorder.InvokeCalls);
            Assert.Equal(456, recorder.DispatchId);
            Assert.Equal(DISPATCH_FLAGS.DISPATCH_PROPERTYPUT, recorder.Flags);
            Assert.Equal(PInvokeCore.DISPID_PROPERTYPUT, recorder.NamedArgument);
            Assert.Equal(1u, recorder.ArgumentCount);
            Assert.Equal(1u, recorder.NamedArgumentCount);
            Assert.Equal(0, recorder.NameCalls);
        }
        else
        {
            Assert.Equal(HRESULT.S_FALSE, dispatch->GetIDOfName("Value", out int id));
            Assert.Equal(123, id);
            Assert.Equal(expected, recorder.NameLcid);
            Assert.Equal(1, recorder.NameCalls);
            Assert.True(recorder.NameMatches);
            Assert.Equal(0, recorder.InvokeCalls);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Recorder
    {
        public void** Vtable;
        public uint NameLcid, InvokeLcid, ArgumentCount, NamedArgumentCount;
        public int NameCalls, InvokeCalls, DispatchId, NamedArgument;
        public DISPATCH_FLAGS Flags;
        public bool NameMatches;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT GetNames(IDispatch* self, Guid* iid, PWSTR* names, uint count, uint lcid, int* ids)
    {
        Recorder* recorder = (Recorder*)self;
        recorder->NameCalls++;
        recorder->NameLcid = lcid;
        recorder->NameMatches = count == 1 && new ReadOnlySpan<char>(names[0].Value, 5).SequenceEqual("Value");
        *ids = 123;
        return HRESULT.S_FALSE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT Invoke(IDispatch* self, int id, Guid* iid, uint lcid, DISPATCH_FLAGS flags,
        DISPPARAMS* args, VARIANT* result, EXCEPINFO* exception, uint* argumentError)
    {
        Recorder* recorder = (Recorder*)self;
        recorder->InvokeCalls++;
        recorder->InvokeLcid = lcid;
        recorder->DispatchId = id;
        recorder->Flags = flags;
        recorder->ArgumentCount = args->cArgs;
        recorder->NamedArgumentCount = args->cNamedArgs;
        recorder->NamedArgument = *args->rgdispidNamedArgs;
        return HRESULT.S_FALSE;
    }
}
#endif
