using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class SystemFontCompatibilityBehaviorTests
{
    public static void Run()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string microsoftDrawingPath = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static attribute => attribute.Key == "MicrosoftSystemDrawingCommonPath")
            .Value ?? throw new InvalidOperationException("Microsoft System.Drawing.Common path is missing.");
        string formsPath = typeof(System.Windows.Forms.Control).Assembly.Location;

        using var context = new MicrosoftDrawingFirstLoadContext(microsoftDrawingPath, formsPath);
        Assembly drawingAssembly = context.LoadFromAssemblyPath(microsoftDrawingPath);
        AssertTrue(
            drawingAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company == "Microsoft Corporation",
            "The compatibility test did not load Microsoft System.Drawing.Common first.");

        Assembly formsAssembly = context.LoadFromAssemblyPath(formsPath);
        AssertNotNull(Create(formsAssembly, "System.Windows.Forms.Control"), "Control construction failed.");
        AssertNotNull(Create(formsAssembly, "System.Windows.Forms.Label"), "Label construction failed.");
        AssertNotNull(GetStaticProperty(formsAssembly, "System.Windows.Forms.Control", "DefaultFont"),
            "Control.DefaultFont returned null.");
        AssertNotNull(GetStaticProperty(formsAssembly, "System.Windows.Forms.SystemInformation", "MenuFont"),
            "SystemInformation.MenuFont returned null.");

        object fontDialog = Create(formsAssembly, "System.Windows.Forms.FontDialog");
        AssertNotNull(fontDialog.GetType().GetProperty("Font")?.GetValue(fontDialog),
            "FontDialog.Font returned null.");

        Console.WriteLine("LibreWinForms system-font compatibility tests passed: Microsoft System.Drawing.Common host=1.");
    }

    private static object Create(Assembly assembly, string typeName) =>
        Activator.CreateInstance(assembly.GetType(typeName, throwOnError: true)!)
        ?? throw new InvalidOperationException($"{typeName} construction returned null.");

    private static object? GetStaticProperty(Assembly assembly, string typeName, string propertyName) =>
        assembly.GetType(typeName, throwOnError: true)!.GetProperty(propertyName)?.GetValue(null);

    private static void AssertNotNull(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertTrue(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class MicrosoftDrawingFirstLoadContext : AssemblyLoadContext, IDisposable
    {
        private readonly string _microsoftDrawingPath;
        private readonly string _microsoftDrawingDirectory;
        private readonly string _formsDirectory;

        public MicrosoftDrawingFirstLoadContext(string microsoftDrawingPath, string formsPath)
            : base(nameof(MicrosoftDrawingFirstLoadContext), isCollectible: true)
        {
            _microsoftDrawingPath = microsoftDrawingPath;
            _microsoftDrawingDirectory = Path.GetDirectoryName(microsoftDrawingPath)
                ?? throw new InvalidOperationException("Microsoft System.Drawing.Common directory is missing.");
            _formsDirectory = Path.GetDirectoryName(formsPath)
                ?? throw new InvalidOperationException("LibreWinForms assembly directory is missing.");
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == "System.Drawing.Common")
            {
                return LoadFromAssemblyPath(_microsoftDrawingPath);
            }

            string candidate = Path.Combine(_microsoftDrawingDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(candidate))
            {
                return LoadFromAssemblyPath(candidate);
            }

            candidate = Path.Combine(_formsDirectory, $"{assemblyName.Name}.dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }

        public void Dispose() => Unload();
    }
}
