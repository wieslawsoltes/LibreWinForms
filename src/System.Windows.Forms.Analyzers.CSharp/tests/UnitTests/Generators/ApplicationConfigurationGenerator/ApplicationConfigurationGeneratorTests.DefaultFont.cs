// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms.Analyzers.Diagnostics;
using System.Windows.Forms.CSharp.Generators.ApplicationConfiguration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace System.Windows.Forms.Analyzers.Tests;

public partial class ApplicationConfigurationGeneratorTests
{
    private const string SdkConfiguration = """
        internal static partial class ApplicationConfiguration
        {
            internal static void Initialize()
            {
                global::System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                ConfigureDefaultFont();
            }
            static partial void ConfigureDefaultFont();
        }
        """;

    [Theory]
    [InlineData("ApplicationConfiguration.Initialize();")]
    [InlineData("namespace Example { internal static class Program { private static void Main() { ApplicationConfiguration.Initialize(); } } }")]
    [InlineData("namespace Example; internal static class Program { private static void Main() { ApplicationConfiguration.Initialize(); } }")]
    public async Task CS_ApplicationConfigurationGenerator_sdk_font_uses_global_owner(string source)
    {
        var test = CreateSdkFontTest(source, "Arial, 14.25px, style=Bold, Italic");
        ExpectSdkFont(test, "Arial", "14.25", style: 3, unit: 2);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS_ApplicationConfigurationGenerator_sdk_font_retains_canonical_name_sanitization()
    {
        var test = CreateSdkFontTest("ApplicationConfiguration.Initialize();", "A\"B\\C<&D>, 12pt");
        ExpectSdkFont(test, "ABCD", "12", style: 0, unit: 3);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CS_ApplicationConfigurationGenerator_sdk_without_font_has_no_supplement(string? font)
    {
        var test = CreateSdkFontTest("ApplicationConfiguration.Initialize();", font);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("false")]
    public async Task CS_ApplicationConfigurationGenerator_caller_owned_font_is_not_parsed(string generates)
    {
        var test = CreateSdkFontTest("ApplicationConfiguration.Initialize();", "Arial, 12bogus", generates,
            "internal static class ApplicationConfiguration { internal static void Initialize() { } }");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS_ApplicationConfigurationGenerator_sdk_invalid_font_reports_original_diagnostic()
    {
        var test = CreateSdkFontTest("ApplicationConfiguration.Initialize();", "Arial, 12bogus");
        test.TestState.ExpectedDiagnostics.Add(DiagnosticResult.CompilerError(DiagnosticIDs.PropertyCantBeSetToValue));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS_ApplicationConfigurationGenerator_sdk_explicit_forms_library_retains_its_owner()
    {
        var test = CreateSdkFontTest("internal static class Library { }", "Arial, 11pt");
        test.TestState.OutputKind = OutputKind.DynamicallyLinkedLibrary;
        ExpectSdkFont(test, "Arial", "11", style: 0, unit: 3);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS_ApplicationConfigurationGenerator_sdk_generation_flag_alone_does_not_claim_upstream_owner()
    {
        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
                Sources = { SourceCompilable },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", """
                    is_global = true

                    build_property.LibreWinFormsSdkGeneratesApplicationConfiguration = true
                    """),
                },
                GeneratedSources =
                {
                    (typeof(ApplicationConfigurationGenerator), "ApplicationConfiguration.g.cs",
                        await LoadFileContentAsync("GenerateInitialize_default_boilerplate")),
                },
            },
        };
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test CreateSdkFontTest(
        string source, string? font, string generates = "true", string configuration = SdkConfiguration)
    {
        string fontProperty = font is null ? string.Empty : $"build_property.ApplicationDefaultFont = {font}";
        return new()
        {
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
                Sources = { source, configuration },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", $"""
                    is_global = true

                    build_property.LibreWinFormsSdkOwnsApplicationConfiguration = true
                    build_property.LibreWinFormsSdkGeneratesApplicationConfiguration = {generates}
                    build_property.ApplicationHighDpiMode = NotAnUpstreamSetting
                    build_property.ApplicationVisualStyles = NotAnUpstreamSetting
                    build_property.ApplicationUseCompatibleTextRendering = NotAnUpstreamSetting
                    {fontProperty}
                    """),
                },
            },
        };
    }

    private static void ExpectSdkFont(
        Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test test,
        string name, string size, int style, int unit)
    {
        // Independent expected output; do not use the product formatter to
        // assert its own output or normalize source/namespace/escaping bytes.
        // The original verifier uses .NET Framework 4.7.2 references. As in its
        // existing font fixtures, mark that framework's missing SetDefaultFont;
        // real modern Project/Package consumers separately must compile and run.
        test.TestState.GeneratedSources.Add((typeof(ApplicationConfigurationGenerator),
            "LibreWinForms.ApplicationDefaultFont.g.cs", $$"""
            // <auto-generated />
            internal static partial class ApplicationConfiguration
            {
                static partial void ConfigureDefaultFont()
                {
                    global::System.Windows.Forms.Application.{|CS0117:SetDefaultFont|}(new global::System.Drawing.Font(new global::System.Drawing.FontFamily("{{name}}"), {{size}}f, (global::System.Drawing.FontStyle){{style}}, (global::System.Drawing.GraphicsUnit){{unit}}));
                }
            }
            """));
    }
}
