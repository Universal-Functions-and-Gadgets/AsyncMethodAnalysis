using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UFG.AsyncMethodAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncMethodAnalyzer : DiagnosticAnalyzer
{
   public const string EndInAsyncDiagnosticId = "AM0001";
   public const string CancellationTokenDiagnosticId = "AM0002";

   internal const string CommentLineStart = "#";
   internal const string Extension = ".txt";
   internal const string IgnoredFileNamePrefix = "AsyncMethodAnalysis.Ignored";
   internal const string IgnoredFileName = IgnoredFileNamePrefix + Extension;

   private static readonly Type s_resourcesType = typeof(Resources);
   private static readonly ImmutableArray<string> s_mainVariants = ImmutableArray.Create("Main", "<Main>$");

   private static LocalizableResourceString CreateLocalizableResourceString(string resourceName) =>
      new(resourceName, Resources.ResourceManager, s_resourcesType);

   private const string Category = "Naming";

   public static readonly DiagnosticDescriptor EndsInAsyncRule =
      new(EndInAsyncDiagnosticId,
         CreateLocalizableResourceString(nameof(Resources.MethodNameTitle)),
         CreateLocalizableResourceString(nameof(Resources.MethodNameMessageFormat)),
         Category,
         DiagnosticSeverity.Warning,
         isEnabledByDefault: true,
         description: CreateLocalizableResourceString(nameof(Resources.MethodNameDescription)));

   public static readonly DiagnosticDescriptor CancellationTokenRule =
      new(CancellationTokenDiagnosticId,
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
         var taskType = compilationContext.Compilation.GetTypeByMetadataName(typeof(Task).FullName!);
         var cancellationTokenType = compilationContext
            .Compilation
            .GetTypeByMetadataName(typeof(CancellationToken).FullName!);

         if (taskType is null || cancellationTokenType is null)
         {
            return;
         }

         var ignoredTypes = ExtractIgnoredTypesFromAdditionalFiles(compilationContext);

         var analyzer = new CompilationAnalyzer(taskType, cancellationTokenType, ignoredTypes);

         compilationContext.RegisterSymbolAction(analyzer.AnalyzeSymbol, SymbolKind.Method);
      });
   }

   private static ImmutableArray<INamedTypeSymbol> ExtractIgnoredTypesFromAdditionalFiles(
#pragma warning disable RS1012
      CompilationStartAnalysisContext analysisContext)
#pragma warning restore RS1012
   {
      var additionalFiles = analysisContext
         .Options
         .AdditionalFiles
         .Where(x =>
         {
            var fileName = Path.GetFileName(x.Path);
            return string.Equals(fileName, IgnoredFileName, StringComparison.Ordinal);
         })
         .ToList();

      var texts = additionalFiles
         .Select(x => x.GetText())
         .Where(x => x is not null)
         .ToList();

      if (texts.Count == 0)
      {
         return ImmutableArray.Create<INamedTypeSymbol>();
      }

      var filtered = texts
         .SelectMany(x => x!.Lines)
         .Select(x => x.ToString().Trim())
         .Where(x => !string.IsNullOrWhiteSpace(x))
         .Where(x => !x.StartsWith(CommentLineStart))
         .ToList();

      return filtered
         .Select(line => analysisContext.Compilation.GetTypeByMetadataName(line))
         .Where(type => type is not null)
         .Aggregate(ImmutableArray.Create<INamedTypeSymbol>(), (current, type) => current.Add(type!));
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
      private readonly INamedTypeSymbol _taskType;
      private readonly INamedTypeSymbol _cancellationTokenType;

      /// <summary>
      /// List of async methods to validate
      /// </summary>
      private readonly ConcurrentBag<IMethodSymbol> _asyncMethods = new();

      /// <summary>
      /// List of types to ignore async methods from as specified by the ignore additional file
      /// </summary>
      private readonly ConcurrentBag<INamedTypeSymbol> _ignoredTypes = new();

      private readonly ImmutableArray<(INamedTypeSymbol type, IMethodSymbol method)> _ignoredTypesMethods;
      private readonly ImmutableArray<(INamedTypeSymbol type, IMethodSymbol method)> _interfaceTypesMethods;
      private readonly Dictionary<INamedTypeSymbol, ImmutableHashSet<IMethodSymbol>> _genericInterfaceTypesMethods;
      private readonly Dictionary<IMethodSymbol, INamedTypeSymbol> _concreteTypesMethods;
      private readonly Dictionary<INamedTypeSymbol, ImmutableHashSet<IMethodSymbol>> _genericConcreteTypesMethods;

      public CompilationAnalyzer(
         INamedTypeSymbol taskType,
         INamedTypeSymbol cancellationTokenType,
         ImmutableArray<INamedTypeSymbol> ignoredTypes)
      {
         _taskType = taskType;
         _cancellationTokenType = cancellationTokenType;
         _ignoredTypes = new ConcurrentBag<INamedTypeSymbol>(ignoredTypes);

         //pre-build out supporting structures
         _ignoredTypesMethods = ignoredTypes
            .SelectMany(
               x => x.GetMembers().OfType<IMethodSymbol>().Where(y => y.MethodKind == MethodKind.Ordinary),
               (ignored, method) => (ignored, method))
            .ToImmutableArray();

         _interfaceTypesMethods = _ignoredTypesMethods
            .Where(x => x.type is { TypeKind: TypeKind.Interface, IsGenericType: false })
            .ToImmutableArray();

         _genericInterfaceTypesMethods = _ignoredTypesMethods
            .Where(x => x.type is { TypeKind: TypeKind.Interface, IsGenericType: true })
            .GroupBy<(INamedTypeSymbol type, IMethodSymbol method), INamedTypeSymbol>(x => x.type, SymbolEqualityComparer.Default)
            .ToDictionary<IGrouping<INamedTypeSymbol, (INamedTypeSymbol type, IMethodSymbol method)>, INamedTypeSymbol, ImmutableHashSet<IMethodSymbol>>(
               x => x.Key,
               x => x.Select(y => y.method).ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
               SymbolEqualityComparer.Default);

         _concreteTypesMethods = _ignoredTypesMethods
            .Where(x => x.type.TypeKind is TypeKind.Class or TypeKind.Struct)
            .ToDictionary<(INamedTypeSymbol type, IMethodSymbol method), IMethodSymbol, INamedTypeSymbol>(
               x => x.method,
               x => x.type,
               SymbolEqualityComparer.Default);

         _genericConcreteTypesMethods = _ignoredTypesMethods
            .Where(x => x.type.TypeKind is TypeKind.Class or TypeKind.Struct && x.type.IsGenericType)
            .GroupBy<(INamedTypeSymbol type, IMethodSymbol method), INamedTypeSymbol>(
               x => x.type, SymbolEqualityComparer.Default)
            .ToDictionary<IGrouping<INamedTypeSymbol, (INamedTypeSymbol type, IMethodSymbol method)>, INamedTypeSymbol, ImmutableHashSet<IMethodSymbol>>(
               x => x.Key,
               x => x.Select(x => x.method).ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
               SymbolEqualityComparer.Default);
      }

      public void AnalyzeSymbol(SymbolAnalysisContext context)
      {
         switch (context.Symbol.Kind)
         {
            case SymbolKind.Method:
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
                  if (!IsMethodFilteredOut(methodSymbol))
                  {
                     VerifyMethodName(context.ReportDiagnostic, methodSymbol);
                     VerifyHasCancellationToken(context.ReportDiagnostic, _cancellationTokenType, methodSymbol);
                  }
               }

               break;
            default:
               throw new ArgumentOutOfRangeException(nameof(context), @"Unknown symbol kind");
         }
      }

      private bool IsMethodFilteredOut(IMethodSymbol method)
      {
         //remove methods within an ignored type
         if (_ignoredTypes.Contains(method.ContainingType, SymbolEqualityComparer.Default))
         {
            return true;
         }

         //remove methods where containing type has interface in chain and method is one of interface method
         if (_interfaceTypesMethods
             .Select(x => method.ContainingType.FindImplementationForInterfaceMember(x.method))
             .Any(x => SymbolEqualityComparer.Default.Equals(x, method)))
         {
            return true;
         }

         //remove methods that are implementations of generic types
         if (method.ContainingType
             .AllInterfaces
             .Where(x => _genericInterfaceTypesMethods.ContainsKey(x.ConstructedFrom))
             .SelectMany(x => x.GetMembers().OfType<IMethodSymbol>())
             .Any(x =>
                SymbolEqualityComparer.Default.Equals(
                   method.ContainingType.FindImplementationForInterfaceMember(x),
                   method)))
         {
            return true;
         }

         //remove method where method overrides containing type's base type method
         var baseTypes = GetBaseTypes(method.ContainingType);
         if (method.IsOverride
             && _concreteTypesMethods.TryGetValue(method.OverriddenMethod!, out var conType)
             && baseTypes.Contains(conType))
         {
            return true;
         }

         if (baseTypes
            .Where(x => _genericConcreteTypesMethods.ContainsKey(x.ConstructedFrom))
            .SelectMany(x => x.GetMembers().OfType<IMethodSymbol>().Where(y => y.MethodKind != MethodKind.Constructor))
            .Any(x => SymbolEqualityComparer.Default.Equals(x, method.OverriddenMethod)))
         {
            return true;
         }

         return false;
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
