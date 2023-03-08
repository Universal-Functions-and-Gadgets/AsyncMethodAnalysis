using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using UFG.AsyncMethodAnalysis;
using Xunit;
using VerifyCS = UFG.AsyncMethodAnalysis.Test.CSharpCodeFixVerifier<
   UFG.AsyncMethodAnalysis.AsyncMethodAnalyzer,
   UFG.AsyncMethodAnalysis.EndInAsyncAnalyzerCodeFixProvider>;

namespace AsyncAnalyzer.Test
{
   public class EndsInAsyncAnalyzerTests
   {
      //No diagnostics expected to show up
      [Fact]
      public async Task NothingToDetect()
      {
         var test = @"";

         await VerifyCS.VerifyAnalyzerAsync(test);
      }

      private static string IgnoreAttribute => """
namespace UFG.AsyncMethodAnalyzer.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
    public class IgnoreAsyncMethodAnalysisForAttribute : System.Attribute
    {
        public string FullTypeName { get; }

        public IgnoreAsyncMethodAnalysisForAttribute(string fullTypeName)
        {
            FullTypeName = fullTypeName;
        }
    }
}
""";

      //Diagnostic and CodeFix both triggered and checked for
      [Fact]
      public async Task DetectsMethodWithoutAsync()
      {
         var test = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Diagnostics;

    namespace ConsoleApplication1
    {
        class C
        {
            public Task<int> {|#0:MethodName|}(int a, CancellationToken ct) { return Task.FromResult(a); }

            public Task<int> MethodName2Async(int a, CancellationToken ct) { return Task.FromResult(a); }
        }
    }";

         var fixtest = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Diagnostics;

    namespace ConsoleApplication1
    {
        class C
        {
            public Task<int> MethodNameAsync(int a, CancellationToken ct) { return Task.FromResult(a); }

            public Task<int> MethodName2Async(int a, CancellationToken ct) { return Task.FromResult(a); }
        }
    }";

         var expected = new[] { GetCSharpResultAt(0, AsyncMethodAnalyzer.EndsInAsyncRule, "MethodName") };
         await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
      }

      [Fact]
      public async Task DetectsMethodWithoutAsyncInterface()
      {
         var test = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Diagnostics;

    namespace ConsoleApplication1
    {
        interface IA
        {
            Task<int> {|#0:MethodName|}(int a, CancellationToken ct);
        }

        class C : IA
        {
            public Task<int> {|#1:MethodName|}(int a, CancellationToken ct) { return Task.FromResult(a); }
        }
    }";

         var fixtest = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Diagnostics;

    namespace ConsoleApplication1
    {
        interface IA
        {
            Task<int> MethodNameAsync(int a, CancellationToken ct);
        }

        class C : IA
        {
            public Task<int> MethodNameAsync(int a, CancellationToken ct) { return Task.FromResult(a); }
        }
    }";

         var expected = new[]
         {
            GetCSharpResultAt(0, AsyncMethodAnalyzer.EndsInAsyncRule, "MethodName"),
            GetCSharpResultAt(1, AsyncMethodAnalyzer.EndsInAsyncRule, "MethodName")
         };
         await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
      }

      [Fact]
      public async Task IgnoresGeneratedCode()
      {
         var test = @"
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            using System.Diagnostics;

            namespace ConsoleApplication1
            {
                class C
                {
                    [global::System.CodeDom.Compiler.GeneratedCode(""test_gen"", null)]
                    public virtual Task<int> MethodName(int a, CancellationToken ct) { return Task.FromResult(a); }
                }

                class D : C
                {
                    public override Task<int> MethodName(int a, CancellationToken ct) { return Task.FromResult(a); }
                }
            }";

         await VerifyCS.VerifyAnalyzerAsync(test);
      }

      [Fact]
      public async Task IgnoresInvalidMethodsOnIgnoredInterfaces()
      {
         var global = """
using System;
using System.Threading;
using System.Threading.Tasks;
using UFG.AsyncMethodAnalyzer.Attributes;

[assembly: IgnoreAsyncMethodAnalysisFor("ConsoleApplication1.IMyInter")];
Console.WriteLine("Hello");

namespace ConsoleApplication1
{
    interface IMyInter
    {
        Task<int> MethodName(int a, CancellationToken ct);
    }

    class C : IMyInter
    {
        public Task<int> MethodName(int a, CancellationToken ct) { return Task.FromResult(a); }
    }
}
""";

         var test = new VerifyCS.Test()
         {
            TestState = { Sources = { global, IgnoreAttribute }, OutputKind = OutputKind.ConsoleApplication }
         };

         await test.RunAsync();
      }

      [Fact]
      public async Task IgnoresInvalidMethodsOnIgnoredClasses1()
      {
         var global = """
using System;
using System.Threading;
using System.Threading.Tasks;
using UFG.AsyncMethodAnalyzer.Attributes;

[assembly: IgnoreAsyncMethodAnalysisFor("ConsoleApplication1.C")];
Console.WriteLine("Hello");

namespace ConsoleApplication1
{
    class C
    {
        public Task<int> MethodName(int a, CancellationToken ct) { return Task.FromResult(a); }
    }
}
""";

         var test = new VerifyCS.Test()
         {
            TestState = { Sources = { global, IgnoreAttribute }, OutputKind = OutputKind.ConsoleApplication }
         };

         await test.RunAsync();
      }

      [Fact]
      public async Task IgnoresInvalidMethodsOnIgnoredClasses2()
      {
         var global = """
using System;
using System.Threading;
using System.Threading.Tasks;
using UFG.AsyncMethodAnalyzer.Attributes;

[assembly: IgnoreAsyncMethodAnalysisFor("ConsoleApplication1.B")];
Console.WriteLine("Hello");

namespace ConsoleApplication1
{
    class B
    {
        public virtual Task<int> MethodName(int a, CancellationToken ct) { return Task.FromResult(a); }
    }

    class C : B
    {
        public override Task<int> MethodName(int a, CancellationToken ct) { return Task.FromResult(a + 1); }
    }
}
""";

         var test = new VerifyCS.Test()
         {
            TestState = { Sources = { global, IgnoreAttribute }, OutputKind = OutputKind.ConsoleApplication }
         };

         await test.RunAsync();
      }

      [Fact]
      public async Task IdentifiesInvalidMethodsWhenParentIsIgnored()
      {
         var global = """
using System;
using System.Threading;
using System.Threading.Tasks;
using UFG.AsyncMethodAnalyzer.Attributes;

[assembly: IgnoreAsyncMethodAnalysisFor("ConsoleApplication1.B"), IgnoreAsyncMethodAnalysisFor("ConsoleApplication1.IGetStuff")];
Console.WriteLine("Hello");

namespace ConsoleApplication1
{
    interface IGetStuff
    {
        Task<string> GetStr(CancellationToken ct);
    }

    class B
    {
        public virtual Task<int> MethodName(int a, CancellationToken ct) { return Task.FromResult(a); }
    }

    class C : B, IGetStuff
    {
        public override Task<int> MethodName(int a, CancellationToken ct) { return Task.FromResult(a + 1); }
        public Task<int> {|#0:MethodName2|}(int a, CancellationToken ct) { return Task.FromResult(a + 1); }
        public Task<string> GetStr(CancellationToken ct) { return Task.FromResult("abc"); }
    }
}
""";
         var test = new VerifyCS.Test()
         {
            TestState = { Sources = { global, IgnoreAttribute }, OutputKind = OutputKind.ConsoleApplication },
            ExpectedDiagnostics = { GetCSharpResultAt(0, AsyncMethodAnalyzer.EndsInAsyncRule, "MethodName2") }
         };

         await test.RunAsync();
      }

      private static DiagnosticResult GetCSharpResultAt(
         int markupKey,
         DiagnosticDescriptor descriptor,
         string bannedMemberName)
         => VerifyCS.Diagnostic(descriptor)
            .WithLocation(markupKey)
            .WithArguments(bannedMemberName);
   }
}
