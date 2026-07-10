using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfWindow = System.Windows.Window;

namespace LibreWinForms.SdkSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--run-form", StringComparer.Ordinal))
        {
            return RunMainFormSmoke();
        }

        if (args.Contains("--run-dialog", StringComparer.Ordinal))
        {
            return RunOwnedDialogSmoke();
        }

        if (args.Contains("--run-designer", StringComparer.Ordinal))
        {
            return RunDesignerSmoke();
        }

        Console.WriteLine("LibreWinForms SDK smoke build loaded.");
        return 0;
    }

    private static int RunMainFormSmoke()
    {
        bool shown = false;
        bool closed = false;

        var form = new Forms.Form
        {
            Name = "LibreWinFormsSdkSmoke",
            Text = "LibreWinForms SDK Smoke",
            Width = 320,
            Height = 180,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        using var closeTimer = new Timer(_ => form.Close(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        form.Shown += (_, _) =>
        {
            shown = true;
            closeTimer.Change(TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
        };

        form.FormClosed += (_, _) => closed = true;

        Forms.Application.Run(form);

        if (!shown || !closed)
        {
            Console.Error.WriteLine($"LibreWinForms SDK smoke failed shown={shown} closed={closed}");
            return 2;
        }

        Console.WriteLine("LibreWinForms SDK smoke result=Success host=WPF formShown=True formClosed=True");
        return 0;
    }

    private static int RunOwnedDialogSmoke()
    {
        bool ownerLoaded = false;
        bool dialogShown = false;
        bool dialogClosed = false;
        bool ownerLinked = false;
        Forms.DialogResult dialogResult = Forms.DialogResult.None;

        var application = new WpfApplication();
        var ownerWindow = new WpfWindow
        {
            Title = "LibreWinForms SDK Dialog Owner",
            Width = 480,
            Height = 300
        };

        ownerWindow.Loaded += (_, _) =>
        {
            ownerLoaded = true;
            ownerWindow.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var dialog = new Forms.Form
                    {
                        Name = "LibreWinFormsSdkOwnedDialog",
                        Text = "LibreWinForms SDK Owned Dialog",
                        Width = 340,
                        Height = 200,
                        StartPosition = Forms.FormStartPosition.CenterParent
                    };

                    var closeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    closeTimer.Tick += (_, _) =>
                    {
                        closeTimer.Stop();
                        dialog.DialogResult = Forms.DialogResult.OK;
                        dialog.Close();
                    };

                    dialog.Shown += (_, _) =>
                    {
                        dialogShown = true;
                        ownerLinked = WpfApplication.Current.Windows
                            .Cast<WpfWindow>()
                            .Any(window => !ReferenceEquals(window, ownerWindow)
                                && ReferenceEquals(window.Owner, ownerWindow));
                        closeTimer.Start();
                    };
                    dialog.FormClosed += (_, _) => dialogClosed = true;

                    dialogResult = dialog.ShowDialog(new WpfWindowOwner(ownerWindow));
                    closeTimer.Stop();
                    ownerWindow.Close();
                }),
                DispatcherPriority.ApplicationIdle);
        };

        application.Run(ownerWindow);

        if (!ownerLoaded || !dialogShown || !dialogClosed || !ownerLinked || dialogResult != Forms.DialogResult.OK)
        {
            Console.Error.WriteLine(
                $"LibreWinForms SDK owned dialog smoke failed ownerLoaded={ownerLoaded} dialogShown={dialogShown} " +
                $"dialogClosed={dialogClosed} ownerLinked={ownerLinked} result={dialogResult}");
            return 3;
        }

        Console.WriteLine(
            "LibreWinForms SDK owned dialog smoke result=Success host=WPF ownerLoaded=True " +
            "dialogShown=True dialogClosed=True ownerLinked=True result=OK");
        return 0;
    }

    private static int RunDesignerSmoke()
    {
        const string originalName = "toolStripContainer1";
        const string renamedName = "designerContainer";
        const string updatedText = "LibreWinForms designer smoke";

        using var surface = new DesignSurface();
        var loader = new DesignerSmokeLoader();
        surface.BeginLoad(loader);

        var host = surface.GetService(typeof(IDesignerHost)) as IDesignerHost;
        var component = host?.Container.Components[originalName] as Forms.ToolStripContainer;
        var changeService = component?.Site?.GetService(typeof(IComponentChangeService)) as IComponentChangeService;
        var selectionService = host?.GetService(typeof(ISelectionService)) as ISelectionService;
        var serializationManager = host?.GetService(typeof(IDesignerSerializationManager)) as IDesignerSerializationManager;
        PropertyDescriptor? textProperty = component is null ? null : TypeDescriptor.GetProperties(component)[nameof(Forms.Control.Text)];

        bool siteHasChangeService = changeService is not null;
        bool siteHasHost = ReferenceEquals(component?.Site?.GetService(typeof(IDesignerHost)), host);
        bool siteHasContainer = ReferenceEquals(component?.Site?.GetService(typeof(IContainer)), host?.Container);

        var localService = new DesignerSmokeService();
        if (component?.Site is IServiceContainer siteServices)
        {
            siteServices.AddService(typeof(DesignerSmokeService), localService);
        }

        bool siteLocalService = ReferenceEquals(component?.Site?.GetService(typeof(DesignerSmokeService)), localService);

        if (component?.Site?.GetService(typeof(IDictionaryService)) is IDictionaryService dictionary)
        {
            dictionary.SetValue("smoke", updatedText);
        }

        bool siteDictionary = string.Equals(
            (component?.Site?.GetService(typeof(IDictionaryService)) as IDictionaryService)?.GetValue("smoke") as string,
            updatedText,
            StringComparison.Ordinal);

        if (component is not null && textProperty is not null)
        {
            changeService?.OnComponentChanging(component, textProperty);
            textProperty.SetValue(component, updatedText);
            changeService?.OnComponentChanged(component, textProperty, string.Empty, updatedText);
            selectionService?.SetSelectedComponents(new object[] { component }, SelectionTypes.Replace);
        }

        bool selected = component is not null && selectionService?.GetComponentSelected(component) == true;
        surface.Flush();
        bool persisted = loader.ContainsPropertyAssignment(originalName, nameof(Forms.Control.Text), updatedText);

        if (component?.Site is not null)
        {
            component.Site.Name = renamedName;
        }

        bool renamed = component is not null
            && ReferenceEquals(host?.Container.Components[renamedName], component)
            && host?.Container.Components[originalName] is null
            && ReferenceEquals(serializationManager?.GetInstance(renamedName), component)
            && serializationManager?.GetInstance(originalName) is null;

        bool success = surface.IsLoaded
            && component is not null
            && siteHasChangeService
            && siteHasHost
            && siteHasContainer
            && siteLocalService
            && siteDictionary
            && selected
            && persisted
            && renamed;

        Console.WriteLine(
            "LibreWinForms SDK designer smoke result=" + (success ? "Success" : "Partial")
            + $" loaded={surface.IsLoaded} component={component is not null}"
            + $" siteHasChangeService={siteHasChangeService} siteHasHost={siteHasHost} siteHasContainer={siteHasContainer}"
            + $" siteLocalService={siteLocalService} siteDictionary={siteDictionary} selected={selected}"
            + $" persisted={persisted} renamed={renamed}");
        return success ? 0 : 4;
    }

    private sealed class WpfWindowOwner : Forms.IWin32Window
    {
        private readonly WpfWindow _window;

        public WpfWindowOwner(WpfWindow window)
        {
            _window = window;
        }

        public IntPtr Handle => new WindowInteropHelper(_window).Handle;
    }

    private sealed class DesignerSmokeService
    {
    }

    private sealed class DesignerSmokeLoader : CodeDomDesignerLoader
    {
        private CodeCompileUnit? _writtenUnit;

        protected override CodeDomProvider? CodeDomProvider => null;

        protected override ITypeResolutionService? TypeResolutionService => null;

        protected override CodeCompileUnit Parse()
        {
            var unit = new CodeCompileUnit();
            var codeNamespace = new CodeNamespace("LibreWinForms.SdkSmoke");
            var codeClass = new CodeTypeDeclaration("DesignerSmokeForm");
            codeClass.BaseTypes.Add(typeof(Forms.Form));
            codeClass.Members.Add(new CodeMemberField(typeof(Forms.ToolStripContainer), "toolStripContainer1"));

            var initializeComponent = new CodeMemberMethod
            {
                Name = "InitializeComponent"
            };
            var component = new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "toolStripContainer1");
            initializeComponent.Statements.Add(new CodeAssignStatement(
                component,
                new CodeObjectCreateExpression(typeof(Forms.ToolStripContainer))));
            initializeComponent.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(component, nameof(Forms.Control.Name)),
                new CodePrimitiveExpression("toolStripContainer1")));
            initializeComponent.Statements.Add(new CodeExpressionStatement(
                new CodeMethodInvokeExpression(
                    new CodePropertyReferenceExpression(new CodeThisReferenceExpression(), nameof(Forms.Control.Controls)),
                    "Add",
                    component)));
            codeClass.Members.Add(initializeComponent);
            codeNamespace.Types.Add(codeClass);
            unit.Namespaces.Add(codeNamespace);
            return unit;
        }

        protected override void Write(CodeCompileUnit unit)
        {
            _writtenUnit = unit;
        }

        public bool ContainsPropertyAssignment(string componentName, string propertyName, object expectedValue)
        {
            if (_writtenUnit is null)
                return false;

            foreach (CodeNamespace codeNamespace in _writtenUnit.Namespaces)
            {
                foreach (CodeTypeDeclaration codeClass in codeNamespace.Types)
                {
                    foreach (CodeMemberMethod method in codeClass.Members.OfType<CodeMemberMethod>())
                    {
                        foreach (CodeAssignStatement assignment in method.Statements.OfType<CodeAssignStatement>())
                        {
                            if (assignment.Left is CodePropertyReferenceExpression property
                                && string.Equals(property.PropertyName, propertyName, StringComparison.Ordinal)
                                && property.TargetObject is CodeFieldReferenceExpression field
                                && field.TargetObject is CodeThisReferenceExpression
                                && string.Equals(field.FieldName, componentName, StringComparison.Ordinal)
                                && assignment.Right is CodePrimitiveExpression value
                                && Equals(value.Value, expectedValue))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }
    }
}
