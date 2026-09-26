// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.ApplicationIsolation;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("Supply the isolated application and dotnet paths.");

        // Force the actual Microsoft reference before launching any portable UI.
        // Do not construct a Microsoft Font on operating systems where it is unsupported.
        var drawing = typeof(System.Drawing.Font).Assembly;
        Require(Convert.ToHexString(drawing.GetName().GetPublicKeyToken()!) == "CC7B13FFCD2DDD51",
            "The host must load Microsoft Drawing first.");
        const string text = "Isolated window: A🙂 \"quote\"";
        var start = new PortableApplicationStartInfo(args[0]) { DotNetHostPath = args[1] };
        start.Arguments.Add(text);
        using PortableApplicationSession child = PortableApplication.Start(start);
        Require(child.ProcessId != Environment.ProcessId, "The UI must belong to a separate process.");
        Console.WriteLine($"Window child {child.ProcessId}; retained result {child.ResultDirectory}");
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(150));
        try
        {
            PortableApplicationExit exit = await child.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            Require(exit.Succeeded && exit.Reply is { Accepted: true } && exit.Reply.Value == text,
                $"Child visible lifecycle failed: exit={exit.ExitCode}; protocol={exit.ProtocolError}");
            Require(ReferenceEquals(drawing, typeof(System.Drawing.Font).Assembly),
                "The host's Microsoft Drawing identity must remain unchanged.");
            Console.WriteLine("Isolated window contract passed: Microsoft-first host, child-owned system fonts/image, normal window closure.");
            return 0;
        }
        finally
        {
            // Failure/timeout cleanup only. Success requires normal Application.Run exit.
            if (!child.HasExited)
            {
                child.Terminate();
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
