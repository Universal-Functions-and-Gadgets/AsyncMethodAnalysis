using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;

namespace UFG.AsyncMethodAnalysis;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EndInAsyncAnalyzerCodeFixProvider)), Shared]
public class EndInAsyncAnalyzerCodeFixProvider : CodeFixProvider
{
   public sealed override ImmutableArray<string> FixableDiagnosticIds =>
      ImmutableArray.Create(AsyncAnalyzerAnalyzer.EndsInAsyncRule.Id);

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
            title: CodeFixResources.AsyncCodeFixTitle,
            createChangedSolution: c => RenameAsyncMethodAsync(context.Document, declaration, c),
            equivalenceKey: nameof(CodeFixResources.AsyncCodeFixTitle)),
         diagnostic);
   }

   private static async Task<Solution> RenameAsyncMethodAsync(
      Document document,
      MethodDeclarationSyntax methodDecl,
      CancellationToken cancellationToken)
   {
      // Compute new uppercase name.
      var identifierToken = methodDecl.Identifier;
      var newName = $"{identifierToken.Text}Async";

      // Get the symbol representing the type to be renamed.
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
      var typeSymbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);

      // Produce a new solution that has all references to that type renamed, including the declaration.
      var newSolution = await Renamer
         .RenameSymbolAsync(
            document.Project.Solution,
            typeSymbol!,
            new SymbolRenameOptions(true, true, true),
            newName,
            cancellationToken)
         .ConfigureAwait(false);

      // Return the new solution with the now-uppercase type name.
      return newSolution;
   }
}
