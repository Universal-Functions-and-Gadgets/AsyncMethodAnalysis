using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using UFG.AsyncMethodAnalysis.Attributes;

namespace UFG.AsyncMethodAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncAnalyzerAnalyzer : DiagnosticAnalyzer
{
   // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
   // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Localizing%20Analyzers.md for more on localization

   private static readonly Type s_resourcesType = typeof(Resources);
   private static readonly ImmutableArray<string> s_mainVariants = ImmutableArray.Create("Main", "<Main>$");

   private static LocalizableResourceString CreateLocalizableResourceString(string resourceName) =>
      new(resourceName, Resources.ResourceManager, s_resourcesType);

   private const string Category = "Naming";

   public static readonly DiagnosticDescriptor EndsInAsyncRule =
      new("AM0001",
         CreateLocalizableResourceString(nameof(Resources.MethodNameTitle)),
         CreateLocalizableResourceString(nameof(Resources.MethodNameMessageFormat)),
         Category,
         DiagnosticSeverity.Warning,
         isEnabledByDefault: true,
         description: CreateLocalizableResourceString(nameof(Resources.MethodNameDescription)));

   public static readonly DiagnosticDescriptor CancellationTokenRule =
      new("AM0002",
         CreateLocalizableResourceString(nameof(Resources.CancellationTokenTitle)),
         CreateLocalizableResourceString(nameof(Resources.CancellationTokenMessageFormat)),
         Category,
         DiagnosticSeverity.Warning,
         isEnabledByDefault: true,
         description: CreateLocalizableResourceString(nameof(Resources.CancellationTokenDescription)));

   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
      ImmutableArray.Create(EndsInAsyncRule, CancellationTokenRule);

   public override void Initialize(AnalysisContext context)
   {
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
      context.EnableConcurrentExecution();

      // Consider registering other actions that act on syntax instead of or in addition to symbols
      // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information

      context.RegisterCompilationStartAction(compilationContext =>
      {
         var ignoreAttrType = compilationContext
            .Compilation
            .GetTypeByMetadataName(typeof(IgnoreAsyncMethodAnalysisForAttribute).FullName!);
         var taskType = compilationContext.Compilation.GetTypeByMetadataName(typeof(Task).FullName!);
         var cancellationTokenType = compilationContext
            .Compilation
            .GetTypeByMetadataName(typeof(CancellationToken).FullName!);

         if (taskType is null || cancellationTokenType is null)
         {
            return;
         }

         var analyzer = new CompilationAnalyzer(ignoreAttrType, taskType, cancellationTokenType);

         compilationContext.RegisterSymbolAction(analyzer.AnalyzeSymbol, SymbolKind.Method);

         compilationContext.RegisterOperationBlockAction(analyzer.AnalyzeBlock);

         compilationContext.RegisterCompilationEndAction(analyzer.CompilationEndAction);
      });
   }

   private static bool IsParentGenerated(IMethodSymbol methodSymbol)
   {
      var parent = methodSymbol.OverriddenMethod;

      while (parent is { })
      {
         var attrs = parent.GetAttributes();
         if (attrs.Any(x => string.Equals(x.AttributeClass?.Name, nameof(GeneratedCodeAttribute))))
         {
            return true;
         }

         parent = parent.OverriddenMethod;
      }

      return false;
   }

   private static void VerifyMethodName(Action<Diagnostic> reportDiagnostic, IMethodSymbol methodSymbol)
   {
      if (methodSymbol.Name.EndsWith("Async", StringComparison.Ordinal))
      {
         return;
      }

      var diagnostic = Diagnostic.Create(EndsInAsyncRule, methodSymbol.Locations[0], methodSymbol.Name);
      reportDiagnostic(diagnostic);
   }

   private static void VerifyHasCancellationToken(
      Action<Diagnostic> reportDiagnostic,
      INamedTypeSymbol cancellationTokenType,
      IMethodSymbol methodSymbol)
   {
      if (!methodSymbol
             .Parameters
             .Any(x => SymbolEqualityComparer.Default.Equals(x.Type, cancellationTokenType)))
      {
         var diagnostic = Diagnostic.Create(
            CancellationTokenRule,
            methodSymbol.Locations[0],
            methodSymbol.Name);
         reportDiagnostic(diagnostic);
      }
   }

   private sealed class CompilationAnalyzer
   {
      private readonly INamedTypeSymbol? _ignoreAttributeType;
      private readonly INamedTypeSymbol _taskType;
      private readonly INamedTypeSymbol _cancellationTokenType;

      /// <summary>
      /// List of async methods to validate
      /// </summary>
      private readonly ConcurrentBag<IMethodSymbol> _asyncMethods = new();

      /// <summary>
      /// List of types to ignore async methods from as specified by the <see cref="IgnoreAsyncMethodAnalysisForAttribute"/>
      /// </summary>
      private readonly ConcurrentBag<INamedTypeSymbol> _ignoredTypes = new();

      public CompilationAnalyzer(
         INamedTypeSymbol? ignoreAttributeType,
         INamedTypeSymbol taskType,
         INamedTypeSymbol cancellationTokenType)
      {
         _ignoreAttributeType = ignoreAttributeType;
         _taskType = taskType;
         _cancellationTokenType = cancellationTokenType;
      }

      public void AnalyzeSymbol(SymbolAnalysisContext context)
      {
         switch (context.Symbol.Kind)
         {
            case SymbolKind.Method:
               // Check if this is an interface method with "_unsecureMethodAttributeType" attribute.
               var methodSymbol = (IMethodSymbol)context.Symbol;
               if (methodSymbol.IsOverride && IsParentGenerated(methodSymbol))
               {
                  return;
               }

               //standard method definition
               if (methodSymbol is { MethodKind: MethodKind.Ordinary }
                   && !s_mainVariants.Contains(methodSymbol.Name)
                   && (methodSymbol.IsAsync
                       || SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, _taskType)
                       || SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType.BaseType, _taskType)))
               {
                  _asyncMethods.Add(methodSymbol);
               }

               break;
            default:
               throw new ArgumentOutOfRangeException(nameof(context), @"Unknown symbol kind");
         }
      }

      public void AnalyzeBlock(OperationBlockAnalysisContext context)
      {
         if (_ignoreAttributeType is null
             || context.OwningSymbol.Kind != SymbolKind.Namespace
             || !context.OperationBlocks.All(x => x.Kind == OperationKind.Attribute))
         {
            return;
         }

         //pull out the type name arguments from the attribute ctor and get the type
         var ignores = context
            .OperationBlocks
            .OfType<IAttributeOperation>()
            .SelectMany(x => x
               .ChildOperations
               .OfType<IObjectCreationOperation>()
               .Where(y => SymbolEqualityComparer.Default.Equals(y.Type, _ignoreAttributeType)))
            .Select(x => x.Arguments.Single().Value.ConstantValue)
            .Where(x => x.HasValue)
            .Select(x => x.Value!.ToString())
            .Select(x => context.Compilation.GetTypeByMetadataName(x))
            .Where(x => x is { })
            .Select(x => x!)
            .ToList();

         foreach (var ig in ignores)
         {
            _ignoredTypes.Add(ig);
         }
      }

      public void CompilationEndAction(CompilationAnalysisContext context)
      {
         if (_asyncMethods.Count == 0)
         {
            // No async methods to check
            return;
         }

         var minusIgnored = _ignoredTypes.Count == 0
            ? _asyncMethods
            : FilterOutIgnored(_asyncMethods, _ignoredTypes);

         // Report diagnostic for violating methods
         foreach (var asyncMethod in minusIgnored)
         {
            VerifyMethodName(context.ReportDiagnostic, asyncMethod);
            VerifyHasCancellationToken(context.ReportDiagnostic, _cancellationTokenType, asyncMethod);
         }
      }

      private static IEnumerable<IMethodSymbol> FilterOutIgnored(
         IEnumerable<IMethodSymbol> asyncMethods,
         ConcurrentBag<INamedTypeSymbol> ignoredTypes)
      {
         var ignoredTypesMethods = ignoredTypes
            .SelectMany(
               x => x.GetMembers().OfType<IMethodSymbol>().Where(y => y.MethodKind == MethodKind.Ordinary),
               (ignored, method) => new { ignored, method })
            .ToList();

         var interfaceTypesMethods = ignoredTypesMethods
            .Where(x => x.ignored.TypeKind is TypeKind.Interface)
            .Select(x => x.method)
            .ToImmutableArray();

         var structureTypesMethods = ignoredTypesMethods
            .Where(x => x.ignored.TypeKind is TypeKind.Class or TypeKind.Struct)
            .ToDictionary(x => x.method, x => x.ignored, SymbolEqualityComparer.Default);

         //remove methods within an ignored type
         asyncMethods = asyncMethods
            .Where(x => !ignoredTypes.Contains(x.ContainingType, SymbolEqualityComparer.Default))
            .ToList();

         //remove methods where containing type has interface in chain and method is one of interface method
         asyncMethods = asyncMethods
            .Where(am =>
            {
               return !interfaceTypesMethods
                  .Select(x => am.ContainingType.FindImplementationForInterfaceMember(x!))
                  .Any(x => SymbolEqualityComparer.Default.Equals(x, am));
            })
            .ToList();

         //remove method where method overrides containing type's base type method
         asyncMethods = asyncMethods
            .Where(am =>
            {
               if (am.IsOverride && structureTypesMethods.TryGetValue(am.OverriddenMethod, out var structType))
               {
                  return !GetBaseTypes(am.ContainingType).Contains(structType);
               }

               return true;
            })
            .ToList();

         return asyncMethods;
      }

      private static ImmutableArray<INamedTypeSymbol> GetBaseTypes(INamedTypeSymbol symbol)
      {
         var ia = ImmutableArray.Create<INamedTypeSymbol>();

         var parent = symbol.BaseType;

         while (parent is not null)
         {
            ia = ia.Add(parent);
            parent = parent.BaseType;
         }

         return ia;
      }
   }
}
