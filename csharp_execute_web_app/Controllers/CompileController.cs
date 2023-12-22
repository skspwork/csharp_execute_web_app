using System.Reflection;
using System.Runtime.Loader;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace csharp_execute_web_app.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class CompileController : Controller
    {
        [HttpPost]
        public async Task<string> Execute()
        {
            var reader = new StreamReader(Request.Body);
            var sourceCodeString = await reader.ReadToEndAsync();


            var compiledAssembly = Compile(sourceCodeString);


            var (program, context) = CreateInstance(compiledAssembly);
            var type = program.GetType();
            var methodInfo = type.GetMethod("Main")!;

            using var stringWriter = new StringWriter();
            
            Console.SetOut(stringWriter);
            try
            {
                methodInfo.Invoke(program, null);
            }
            finally
            {
                context.Unload();
            }
            return stringWriter.ToString();
        }

        private byte[] Compile(string sourceCodeString)
        {
            // ソースコードの準備
            var sourceCode = SourceText.From(sourceCodeString);
            var sourceCodeOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
            var parsedSyntaxTree = SyntaxFactory.ParseSyntaxTree(sourceCode, sourceCodeOptions);

            // コード内で参照するDLLの準備
            var dllDirectoryPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var references = new List<string>()
            {
                "netstandard",
                "System",
                "System.Runtime",
                "System.Private.CoreLib",
                "System.Console"
            }
            .Select(x => $"{Path.Combine(dllDirectoryPath, x)}.dll")
            .Select(x => MetadataReference.CreateFromFile(x))
            .ToArray();

            // コンパイル設定
            var options = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);

            // コンパイル
            var compilation = CSharpCompilation.Create("test",
                new[] { parsedSyntaxTree },
                references: references,
                options: options);

            // 発行処理
            using var stream = new MemoryStream();
            var compileResult = compilation.Emit(stream);
            if (!compileResult.Success)
                throw new Exception(string.Join("\n", compileResult.Diagnostics));

            stream.Seek(0, SeekOrigin.Begin);
            var compiledAssembly = stream.ToArray();
            return compiledAssembly;
        }



        private (object, AssemblyLoadContext) CreateInstance(byte[] compiledAssembly, object[]? args = null)
        {
            using var stream = new MemoryStream(compiledAssembly);
            var context = new AssemblyLoadContext(null, true);
            Assembly assembly = context.LoadFromStream(stream);
            var type = assembly.GetTypes().FirstOrDefault(x => x.Name == "Program");    
            if (type == null)
                throw new Exception();

            var instance = Activator.CreateInstance(type, args);
            if (instance == null)
                throw new Exception();

            return (instance, context);
        }
    }
}
