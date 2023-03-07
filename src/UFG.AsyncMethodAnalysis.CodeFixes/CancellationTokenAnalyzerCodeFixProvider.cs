using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UFG.AsyncMethodAnalysis;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CancellationTokenAsyncAnalyzerCodeFixProvider)), Shared]
public class CancellationTokenAsyncAnalyzerCodeFixProvider : CodeFixProvider
{
   public sealed override ImmutableArray<string> FixableDiagnosticIds =>
      ImmutableArray.Create(AsyncAnalyzerAnalyzer.CancellationTokenRule.Id);

   public sealed override FixAllProvider GetFixAllProvider()
   {
      // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
      return WellKnownFixAllProviders.BatchFixer;
   }

   public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
   {
      var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

      if (root is null)
      {
         return;
      }

      var diagnostic = context.Diagnostics.First();
      var diagnosticSpan = diagnostic.Location.SourceSpan;

      // Find the type declaration identified by the diagnostic.
      var declaration = root
         .FindToken(diagnosticSpan.Start)
         .Parent!
         .AncestorsAndSelf()
         .OfType<MethodDeclarationSyntax>().First();

      // Register a code action that will invoke the fix.
      context.RegisterCodeFix(
         CodeAction.Create(
            title: CodeFixResources.CancellationCodeFixTitle,
            createChangedDocument: c => AddCancellationTokenParameterAsync(context.Document, declaration, c),
            equivalenceKey: nameof(CodeFixResources.CancellationCodeFixTitle)),
         diagnostic);
   }

   private static async Task<Document> AddCancellationTokenParameterAsync(
      Document document,
      MethodDeclarationSyntax methodDecl,
      CancellationToken cancellationToken)
   {
      var ctParam = SyntaxFactory
         .Parameter(SyntaxFactory.Identifier("cancellationToken"))
         .WithType(SyntaxFactory.ParseTypeName(nameof(CancellationToken)));

      var updatedMethod = methodDecl.AddParameterListParameters(ctParam);

      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);

      var updatedTree = (await syntaxTree!.GetRootAsync(cancellationToken))
         .ReplaceNode(methodDecl, updatedMethod);

      // Return the new solution with the now-uppercase type name.
      return document.WithSyntaxRoot(updatedTree);
   }
}
