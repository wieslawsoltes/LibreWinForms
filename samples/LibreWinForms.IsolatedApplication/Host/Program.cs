// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.ApplicationIsolation;

// The host deliberately loads Microsoft's Drawing before starting the portable UI.
// Only reference-bound assembly identity is inspected; nothing is dynamically loaded.
var drawing = typeof(System.Drawing.Font).Assembly;
if (Convert.ToHexString(drawing.GetName().GetPublicKeyToken()!) != "CC7B13FFCD2DDD51")
    throw new InvalidOperationException("The host must retain Microsoft System.Drawing.Common.");
if (args.Length < 2)
    throw new ArgumentException("Usage: Host <absolute PortableApp.dll> <absolute dotnet> [application arguments]");

var start = new PortableApplicationStartInfo(args[0]) { DotNetHostPath = args[1] };
foreach (string argument in args.Skip(2))
    start.Arguments.Add(argument);
using PortableApplicationSession child = PortableApplication.Start(start);
PortableApplicationExit exit = await child.WaitForExitAsync().ConfigureAwait(false);
if (!ReferenceEquals(drawing, typeof(System.Drawing.Font).Assembly))
    throw new InvalidOperationException("The host's Drawing identity changed.");
Console.WriteLine($"Child exit={exit.ExitCode}; accepted={exit.Reply?.Accepted}; value={exit.Reply?.Value}; error={exit.ProtocolError}");
Console.WriteLine($"Retained result: {child.ResultDirectory}");
return exit.Succeeded ? 0 : exit.ExitCode == 0 ? 1 : exit.ExitCode;
