using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using UFG.AsyncMethodAnalysis;
using Xunit;
using VerifyCS = UFG.AsyncMethodAnalysis.Test.CSharpCodeFixVerifier<
   UFG.AsyncMethodAnalysis.AsyncAnalyzerAnalyzer,
   UFG.AsyncMethodAnalysis.CancellationTokenAsyncAnalyzerCodeFixProvider>;

namespace AsyncAnalyzer.Test;

public class CancellationTokenAnalyzerTests
{
   //No diagnostics expected to show up
   [Fact]
   public async Task NothingToDetect()
   {
      var test = @"";

      await VerifyCS.VerifyAnalyzerAsync(test);
   }

   //Diagnostic and CodeFix both triggered and checked for
   [Fact]
   public async Task OneParameter()
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
            public Task<int> {|#0:MethodName2Async|}(int a) { return Task.FromResult(a); }
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
            public Task<int> MethodName2Async(int a, CancellationToken cancellationToken) { return Task.FromResult(a); }
        }
    }";

      var expected = new[] { GetCSharpResultAt(0, AsyncAnalyzerAnalyzer.CancellationTokenRule, "MethodName2Async") };
      await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
   }

   [Fact]
   public async Task TwoParameters()
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
            public Task<int> {|#0:MethodName2Async|}(int a, string b) { return Task.FromResult(a); }
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
            public Task<int> MethodName2Async(int a, string b, CancellationToken cancellationToken) { return Task.FromResult(a); }
        }
    }";

      var expected = new[] { GetCSharpResultAt(0, AsyncAnalyzerAnalyzer.CancellationTokenRule, "MethodName2Async") };
      await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
   }

   [Fact]
   public async Task NoParameters()
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
            public Task<int> {|#0:MethodName2Async|}() { return Task.FromResult(123); }
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
            public Task<int> MethodName2Async(CancellationToken cancellationToken) { return Task.FromResult(123); }
        }
    }";

      var expected = new[] { GetCSharpResultAt(0, AsyncAnalyzerAnalyzer.CancellationTokenRule, "MethodName2Async") };
      await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
   }

   [Fact]
   public async Task MultipleMethods()
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
            public Task<int> {|#0:MethodName2Async|}(int a, string b) { return Task.FromResult(a); }

            public Task<string> {|#1:DoStuffAsync|}(DateTime dt) { return Task.FromResult(dt.ToString()); }
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
            public Task<int> MethodName2Async(int a, string b, CancellationToken cancellationToken) { return Task.FromResult(a); }

            public Task<string> DoStuffAsync(DateTime dt, CancellationToken cancellationToken) { return Task.FromResult(dt.ToString()); }
        }
    }";

      var expected = new[]
      {
         GetCSharpResultAt(0, AsyncAnalyzerAnalyzer.CancellationTokenRule, "MethodName2Async"),
         GetCSharpResultAt(1, AsyncAnalyzerAnalyzer.CancellationTokenRule, "DoStuffAsync")
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
            public virtual Task<int> MethodNameAsync(int a) { return Task.FromResult(a); }
        }

        class D : C
        {
            public override Task<int> MethodNameAsync(int a) { return Task.FromResult(a); }
        }
    }";

      await VerifyCS.VerifyAnalyzerAsync(test);
   }

   [Fact]
   public async Task DetectsMethodWithoutCancellationTokenInterface()
   {
      var test = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    namespace ConsoleApplication1
    {
        interface IA
        {
            Task<int> {|#0:MethodNameAsync|}(int a);
        }

        class C : IA
        {
            public Task<int> {|#1:MethodNameAsync|}(int a) { return Task.FromResult(a); }
        }
    }";

      var fixtest = @"
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    namespace ConsoleApplication1
    {
        interface IA
        {
            Task<int> MethodNameAsync(int a, CancellationToken cancellationToken);
        }

        class C : IA
        {
            public Task<int> MethodNameAsync(int a, CancellationToken cancellationToken) { return Task.FromResult(a); }
        }
    }";

      var expected = new[]
      {
         GetCSharpResultAt(0, AsyncAnalyzerAnalyzer.CancellationTokenRule, "MethodNameAsync"),
         GetCSharpResultAt(1, AsyncAnalyzerAnalyzer.CancellationTokenRule, "MethodNameAsync")
      };
      await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
   }

   [Fact]
   public async Task IdentifiesInvalidMethodsWhenParentIsIgnored()
   {
      var global = """
using System;
using System.Threading;
using System.Threading.Tasks;
using UFG.AsyncMethodAnalyzer.Attributes;

namespace ConsoleApplication1
{
    interface IGetStuff
    {
        Task<string> GetStrAsync();
    }

    class B
    {
        public virtual Task<int> MethodNameAsync(int a) { return Task.FromResult(a); }
    }

    class C : B, IGetStuff
    {
        public override Task<int> MethodNameAsync(int a) { return Task.FromResult(a + 1); }
        public Task<int> {|#0:MethodName2Async|}(int a) { return Task.FromResult(a + 1); }
        public Task<string> GetStrAsync() { return Task.FromResult("abc"); }
    }
}
""";
      var ignoreText = """
ConsoleApplication1.IGetStuff
ConsoleApplication1.B
""";

      var test = new VerifyCS.Test()
      {
         TestState =
         {
            Sources = { global, IgnoreAttribute }, OutputKind = OutputKind.DynamicallyLinkedLibrary,
         },
         ExpectedDiagnostics =
         {
            GetCSharpResultAt(0, AsyncAnalyzerAnalyzer.CancellationTokenRule, "MethodName2Async")
         }
      };
      test.TestState.AdditionalFiles.Add((AsyncAnalyzerAnalyzer.IgnoredFileName, ignoreText));

      await test.RunAsync();
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

   private static DiagnosticResult GetCSharpResultAt(
      int markupKey,
      DiagnosticDescriptor descriptor,
      string bannedMemberName)
      => VerifyCS.Diagnostic(descriptor)
         .WithLocation(markupKey)
         .WithArguments(bannedMemberName);
}
