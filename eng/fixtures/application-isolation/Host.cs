// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using LibreWinForms.ApplicationIsolation;

internal static class Program
{
    private static string s_child = null!;
    private static string s_dotnet = null!;

    private static async Task<int> Main(string[] args)
    {
        // This typed reference forces Microsoft Drawing first in the HOST.
        // Only reference-bound metadata is inspected, not assembly resolution or reflection dispatch.
        var microsoft = typeof(System.Drawing.Font).Assembly;
        Require(Convert.ToHexString(microsoft.GetName().GetPublicKeyToken()!) == "CC7B13FFCD2DDD51", "Microsoft-first host identity");
        s_child = args[0];
        s_dotnet = args[1];
        const string text = "A🙂 \"quote\"; $HOME\nsecond line";
        PortableApplicationExit success = await Run("verify", text);
        Require(success.Succeeded && success.Reply is { Accepted: true } && success.Reply.Value == text, "actual child control and exact primitive round trip");
        PortableApplicationExit decision = await Run("decision");
        Require(decision.Succeeded && decision.Reply is { Accepted: false, Value: null }, "user cancellation is a valid decision");
        PortableApplicationExit nonzero = await Run("nonzero");
        Require(nonzero.ExitCode == 23 && !nonzero.Succeeded && nonzero.Reply is not null, "nonzero child exit retained");
        foreach (string invalid in new[] { "missing", "malformed", "oversized" })
        {
            PortableApplicationExit rejected = await Run(invalid);
            Require(rejected.ExitCode == 0 && !rejected.Succeeded && rejected.Reply is null && rejected.ProtocolError is not null, invalid);
        }

        foreach (string invalid in new[] { "version", "identity", "field", "unicode-key", "unicode-value" })
        {
            PortableApplicationExit rejected = await Run(invalid);
            Require(rejected.ExitCode == 29 && !rejected.Succeeded && rejected.Reply is null && rejected.ProtocolError is not null,
                $"{invalid} protocol failure must retain the real child exit code");
        }

        PortableApplicationExit duplicate = await Run("duplicate");
        Require(duplicate.Succeeded && duplicate.Reply?.Value == "first", "one-time result cannot overwrite");
        PortableApplicationExit limited = await Run("value-limit");
        Require(limited.Succeeded && limited.Reply?.Value == "bounded", "text limit before publication");
        await VerifyCanceledWait();
        await VerifyDisposeLeavesChildRunning();
        Require(ReferenceEquals(microsoft, typeof(System.Drawing.Font).Assembly), "host Drawing identity retained after every child");
        Require(Convert.ToHexString(microsoft.GetName().GetPublicKeyToken()!) == "CC7B13FFCD2DDD51", "host still uses Microsoft Drawing");
        Console.WriteLine("Application isolation contracts passed: 15 real child cases; Microsoft host / canonical child remain independent.");
        return 0;
    }

    private static PortableApplicationSession Start(params string[] args)
    {
        var info = new PortableApplicationStartInfo(s_child) { DotNetHostPath = s_dotnet };
        foreach (string argument in args)
            info.Arguments.Add(argument);
        PortableApplicationSession child = PortableApplication.Start(info);
        Require(child.ProcessId != Environment.ProcessId, "distinct actual process");
        Console.WriteLine($"Child {child.ProcessId}; retained result {child.ResultDirectory}");
        return child;
    }

    private static async Task<PortableApplicationExit> Run(params string[] args)
    {
        using PortableApplicationSession child = Start(args);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
        try
        {
            return await child.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            // The fixture explicitly owns this timeout/failure termination policy.
            if (!child.HasExited)
                await Terminate(child);
        }
    }

    private static async Task VerifyCanceledWait()
    {
        using PortableApplicationSession child = Start("wait");
        try
        {
            using CancellationTokenSource wait = new(TimeSpan.FromMilliseconds(200));
            try
            {
                await child.WaitForExitAsync(wait.Token);
                throw new InvalidOperationException("The bounded wait was not canceled.");
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested)
            {
                Require(!child.HasExited, "wait cancellation must not kill the child");
            }
        }
        finally
        {
            await Terminate(child);
        }
    }

    private static async Task VerifyDisposeLeavesChildRunning()
    {
        PortableApplicationSession child = Start("wait");
        using Process owned = Process.GetProcessById(child.ProcessId);
        child.Dispose();
        try
        {
            await Task.Delay(200);
            Require(!owned.HasExited, "disposing a session must not kill its GUI process");
        }
        finally
        {
            if (!owned.HasExited)
                owned.Kill(entireProcessTree: true);
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
            await owned.WaitForExitAsync(cleanup.Token);
        }
    }

    private static async Task Terminate(PortableApplicationSession child)
    {
        child.Terminate();
        using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
        await child.WaitForExitAsync(cleanup.Token);
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition)
            throw new InvalidOperationException($"Application isolation contract failed: {contract}");
    }
}
