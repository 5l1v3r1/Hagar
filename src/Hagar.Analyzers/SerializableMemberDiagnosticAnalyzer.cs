using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Orleans.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SerializableMemberDiagnosticAnalyzer : DiagnosticAnalyzer
    {
        private const string AlwaysInterleaveAttributeName = "Orleans.Concurrency.AlwaysInterleaveAttribute";

        public const string DiagnosticId = "HAGAR0001";
        public const string Title = "[AlwaysInterleave] must only be used on the grain interface method and not the grain class method.";
        public const string MessageFormat = Title;
        public const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                AnalyzeSyntax,
                SyntaxKind.MethodDeclaration);
        }

        private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
        {
            var alwaysInterleaveAttribute = context.Compilation.GetTypeByMetadataName(AlwaysInterleaveAttributeName);

            var syntax = (MethodDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(syntax);

            if (symbol.ContainingType.TypeKind == TypeKind.Interface)
            {
                // TODO: Check that interface inherits from IGrain
                return;
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (!attribute.AttributeClass.Equals(alwaysInterleaveAttribute))
                {
                    return;
                }

                var syntaxReference = attribute.ApplicationSyntaxReference;
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span)));
            }
        }
    }

    [ExportCodeFixProvider(LanguageNames.CSharp)]
    public class AddIdAttributesCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray<string>.Empty;

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(CodeAction.Create("Add [Id(x)] attribute", AddIdAttribute, "add-id-attribute"), );
        }

        private Task<Document> AddIdAttribute(CancellationToken cancellationToken)
        {

        }
    }
}
