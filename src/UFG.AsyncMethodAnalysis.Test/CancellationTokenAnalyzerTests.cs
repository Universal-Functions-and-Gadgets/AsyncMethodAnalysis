using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using UFG.AsyncMethodAnalysis;
using Xunit;
using VerifyCS = UFG.AsyncMethodAnalysis.Test.CSharpCodeFixVerifier<
   UFG.AsyncMethodAnalysis.AsyncMethodAnalyzer,
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

      var expected = new[] { GetCSharpResultAt(0, AsyncMethodAnalyzer.CancellationTokenRule, "MethodName2Async") };
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

      var expected = new[] { GetCSharpResultAt(0, AsyncMethodAnalyzer.CancellationTokenRule, "MethodName2Async") };
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

      var expected = new[] { GetCSharpResultAt(0, AsyncMethodAnalyzer.CancellationTokenRule, "MethodName2Async") };
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
         GetCSharpResultAt(0, AsyncMethodAnalyzer.CancellationTokenRule, "MethodName2Async"),
         GetCSharpResultAt(1, AsyncMethodAnalyzer.CancellationTokenRule, "DoStuffAsync")
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
         GetCSharpResultAt(0, AsyncMethodAnalyzer.CancellationTokenRule, "MethodNameAsync"),
         GetCSharpResultAt(1, AsyncMethodAnalyzer.CancellationTokenRule, "MethodNameAsync")
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
# fun comment here
ConsoleApplication1.IGetStuff
# another for safe measure
ConsoleApplication1.B
""";

      var test = new VerifyCS.Test()
      {
         TestState =
         {
            Sources = { global }
         },
         ExpectedDiagnostics =
         {
            GetCSharpResultAt(0, AsyncMethodAnalyzer.CancellationTokenRule, "MethodName2Async")
         }
      };
      test.TestState.AdditionalFiles.Add((AsyncMethodAnalyzer.IgnoredFileName, ignoreText));

      await test.RunAsync();
   }

   [Fact]
   public async Task IgnoresGenericInterfaces()
   {
      var global = """
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApplication1
{
   interface IOtherStuff<T>
   {
      Task<T> RepeatMeAsync(T a);
   }

   class C : IOtherStuff<int>
   {
      public Task<int> {|#0:MethodName2Async|}(int a) { return Task.FromResult(a + 1); }
      public Task<int> RepeatMeAsync(int a) { return Task.FromResult(a); }
   }
}
""";
      var ignoreText = """
# Comment describing something or another
ConsoleApplication1.IOtherStuff`1
""";

      var test = new VerifyCS.Test()
      {
         TestState =
         {
            Sources = { global }, OutputKind = OutputKind.DynamicallyLinkedLibrary,
         },
         ExpectedDiagnostics =
         {
            GetCSharpResultAt(0, AsyncMethodAnalyzer.CancellationTokenRule, "MethodName2Async")
         }
      };
      test.TestState.AdditionalFiles.Add((AsyncMethodAnalyzer.IgnoredFileName, ignoreText));

      await test.RunAsync();
   }

   [Fact]
   public async Task IgnoresGenericClasses()
   {
      var global = """
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApplication1
{
   abstract class OtherStuff<T>
   {
      public abstract Task<T> RepeatMeAsync(T a);
      public abstract Task<T> MyGen<TValue>(T a, TValue value);
   }

   class C : OtherStuff<int>
   {
      public Task<int> {|#0:MethodName2Async|}(int a) { return Task.FromResult(a + 1); }
      public override Task<int> RepeatMeAsync(int a) { return Task.FromResult(a); }
      public override Task<int> MyGen<TValue>(int a, TValue value) { return Task.FromResult(a); }
   }
}
""";
      var ignoreText = """
# Comment describing something or another
ConsoleApplication1.OtherStuff`1
""";

      var test = new VerifyCS.Test()
      {
         TestState =
         {
            Sources = { global }, OutputKind = OutputKind.DynamicallyLinkedLibrary,
         },
         ExpectedDiagnostics =
         {
            GetCSharpResultAt(0, AsyncMethodAnalyzer.CancellationTokenRule, "MethodName2Async")
         }
      };
      test.TestState.AdditionalFiles.Add((AsyncMethodAnalyzer.IgnoredFileName, ignoreText));

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
