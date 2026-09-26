// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms.Analyzers.Diagnostics;
using System.Windows.Forms.CSharp.Generators.ApplicationConfiguration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using static System.Windows.Forms.Analyzers.ApplicationConfig;

namespace System.Windows.Forms.Analyzers.Tests;

[ForceGC]
[SkipOnArchitecture(TestArchitectures.X86, "Analyzer tests hit OutOfMemoryException on x86 due to memory-mapped NuGet package extraction")]
public partial class ApplicationConfigurationGeneratorTests
{
    private const string SourceCompilable = """
        namespace MyProject
        {
            class Program
            {
                static void Main()
                {
                     ApplicationConfiguration.Initialize();
                }
            }
        }

        """;

    private const string SourceCompilationFailed = """
        namespace MyProject
        {
            class Program
            {
                static void Main()
                {
                     {|CS0103:ApplicationConfiguration|}.Initialize();
                }
            }
        }

        """;

    public static TheoryData<OutputKind> UnsupportedProjectTypes_TestData()
    {
        TheoryData<OutputKind> testData = new();

        foreach (OutputKind projectType in Enum.GetValues(typeof(OutputKind)))
        {
            if (projectType is not OutputKind.ConsoleApplication
                and not OutputKind.WindowsApplication)
            {
                testData.Add(projectType);
            }
        }

        return testData;
    }

    [Theory]
    [MemberData(nameof(UnsupportedProjectTypes_TestData))]
    public async Task CS_ApplicationConfigurationGenerator_GenerateInitialize_fails_if_project_type_unsupported(OutputKind projectType)
    {
        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = projectType,
                Sources = { SourceCompilationFailed },
                ExpectedDiagnostics =
                {
                    DiagnosticResult.CompilerError(DiagnosticIDs.UnsupportedProjectType).WithArguments("WindowsApplication"),
                }
            },
        };

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(OutputKind.ConsoleApplication)]
    [InlineData(OutputKind.WindowsApplication)]
    public async Task CS_ApplicationConfigurationGenerator_GenerateInitialize_pass_if_supported_project_type(OutputKind projectType)
    {
        SourceText generatedCode = await LoadFileContentAsync("GenerateInitialize_default_boilerplate");

        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = projectType,
                Sources = { SourceCompilable },
                GeneratedSources =
                {
                    (typeof(ApplicationConfigurationGenerator), "ApplicationConfiguration.g.cs", generatedCode),
                },
            },
        };

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS_ApplicationConfigurationGenerator_GenerateInitialize_default_boilerplate()
    {
        SourceText generatedCode = await LoadFileContentAsync("GenerateInitialize_default_boilerplate");

        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
                Sources = { SourceCompilable },
                GeneratedSources =
                {
                    (typeof(ApplicationConfigurationGenerator), "ApplicationConfiguration.g.cs", generatedCode),
                },
            },
        };

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS_ApplicationConfigurationGenerator_GenerateInitialize_user_settings_boilerplate()
    {
        SourceText generatedCode = await LoadFileContentAsync("GenerateInitialize_user_settings_boilerplate");

        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
                Sources = { SourceCompilable },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig",
                    $"""
                    is_global = true

                    build_property.{PropertyNameCSharp.DefaultFont} = Microsoft Sans Serif, 8.25px
                    build_property.{PropertyNameCSharp.EnableVisualStyles} =
                    build_property.{PropertyNameCSharp.HighDpiMode} = {HighDpiMode.DpiUnawareGdiScaled}
                    build_property.{PropertyNameCSharp.UseCompatibleTextRendering} = true
                    """),
                },
                GeneratedSources =
                {
                    (typeof(ApplicationConfigurationGenerator), "ApplicationConfiguration.g.cs", generatedCode),
                },
            },
        };

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS_ApplicationConfigurationGenerator_GenerateInitialize_default_top_level()
    {
        const string source =
            """
            ApplicationConfiguration.Initialize();
            """;

        SourceText generatedCode = await LoadFileContentAsync("GenerateInitialize_default_top_level");

        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
                Sources = { source },
                GeneratedSources =
                {
                    (typeof(ApplicationConfigurationGenerator), "ApplicationConfiguration.g.cs", generatedCode),
                },
            },
        };

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CS_ApplicationConfigurationGenerator_GenerateInitialize_user_settings_top_level()
    {
        const string source =
            """
            ApplicationConfiguration.Initialize();
            """;

        SourceText generatedCode = await LoadFileContentAsync("GenerateInitialize_user_top_level");

        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
                Sources = { source },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig",
                    $"""
                    is_global = true

                    build_property.{PropertyNameCSharp.DefaultFont} = Microsoft Sans Serif, 8.25px
                    build_property.{PropertyNameCSharp.EnableVisualStyles} =
                    build_property.{PropertyNameCSharp.HighDpiMode} = {HighDpiMode.DpiUnawareGdiScaled}
                    build_property.{PropertyNameCSharp.UseCompatibleTextRendering} = true
                    """),
                },
                GeneratedSources =
                {
                    (typeof(ApplicationConfigurationGenerator), "ApplicationConfiguration.g.cs", generatedCode),
                },
            },
        };

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    private async Task<SourceText> LoadFileContentAsync(string testName)
    {
        string input = await TestFileLoader.GetGeneratorTestCodeAsync($"{GetType().Name}.{testName}.cs").ConfigureAwait(false);
        return SourceText.From(RestoreCanonicalStatementNewlines(input), Encoding.UTF8);
    }

    private static string RestoreCanonicalStatementNewlines(string input)
    {
        // GenerateCode explicitly joins consecutive configuration statements
        // with CRLF, independently of checkout EOLs in its surrounding template.
        // Reconstruct only those joins in the expected fixture. The actual
        // generated source and every other expected byte remain unnormalized.
        return CanonicalStatementJoin().Replace(input, "$1\r\n");
    }

    [GeneratedRegex(@"(^[ \t]*(?:///[ \t]+)?global::System\.Windows\.Forms\.Application\.[^\r\n]+;)\r?\n(?=[ \t]*(?:///[ \t]+)?global::System\.Windows\.Forms\.Application\.)", RegexOptions.Multiline)]
    private static partial Regex CanonicalStatementJoin();

    [Fact]
    public void Generator_fixture_reconstructs_only_canonical_statement_joins()
    {
        const string input = "header\n  global::System.Windows.Forms.Application.EnableVisualStyles();\n"
            + "  global::System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);\nfooter\r\n";
        const string expected = "header\n  global::System.Windows.Forms.Application.EnableVisualStyles();\r\n"
            + "  global::System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);\nfooter\r\n";
        Assert.Equal(expected, RestoreCanonicalStatementNewlines(input));
        Assert.Equal(expected, RestoreCanonicalStatementNewlines(expected));
    }

    [Theory]
    [InlineData("ApplicationConfiguration.Initialize();")]
    [InlineData("namespace Example { internal static class Program { private static void Main() { ApplicationConfiguration.Initialize(); } } }")]
    [InlineData("namespace Example; internal static class Program { private static void Main() { ApplicationConfiguration.Initialize(); } }")]
    public async Task CS_ApplicationConfigurationGenerator_preserves_explicit_sdk_configuration_owner(string source)
    {
        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
                Sources =
                {
                    source,
                    "internal static class ApplicationConfiguration { internal static void Initialize() { } }",
                },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", """
                    is_global = true

                    build_property.LibreWinFormsSdkOwnsApplicationConfiguration = true
                    build_property.ApplicationHighDpiMode = NotAnUpstreamSetting
                    """),
                },
            },
        };

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("false")]
    public async Task CS_ApplicationConfigurationGenerator_retains_upstream_generation_without_sdk_ownership(string value)
    {
        SourceText generatedCode = await LoadFileContentAsync("GenerateInitialize_default_boilerplate");
        var test = new Verifiers.CSharpIncrementalSourceGeneratorVerifier<ApplicationConfigurationGenerator>.Test
        {
            TestState =
            {
                OutputKind = OutputKind.WindowsApplication,
                Sources = { SourceCompilable },
                AnalyzerConfigFiles =
                {
                    ("/.globalconfig", $"""
                    is_global = true

                    build_property.LibreWinFormsSdkOwnsApplicationConfiguration = {value}
                    """),
                },
                GeneratedSources =
                {
                    (typeof(ApplicationConfigurationGenerator), "ApplicationConfiguration.g.cs", generatedCode),
                },
            },
        };

        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
