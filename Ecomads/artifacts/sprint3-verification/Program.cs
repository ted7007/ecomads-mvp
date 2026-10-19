using System.Reflection;
using System.Runtime.Loader;
using Ecomads.WebApplication.Tests.Wb;

// Direct, local execution of pure test cases; no testhost IPC, DB, network or credentials.
var testDirectory = Path.GetFullPath("Ecomads.WebApplication.Tests/bin/Debug/net10.0");
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(testDirectory, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(testDirectory, name.Name + ".dll")) : null;
return await Run();
static async Task<int> Run()
{
Type[] types = [typeof(RecommendationEngineTests), typeof(RecommendationActionsTests), typeof(RecommendationDiagnosticsTests),
    typeof(OperationalRecommendationTests), typeof(OperationalEvaluatorTests)];
var passed = 0; var failed = 0;
foreach (var type in types)
foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
{
    var attributes = method.GetCustomAttributesData();
    var fact = attributes.Any(a => a.AttributeType.Name == "FactAttribute");
    var cases = attributes.Where(a => a.AttributeType.Name == "InlineDataAttribute")
        .Select(a => a.ConstructorArguments[0].Value is IEnumerable<CustomAttributeTypedArgument> arguments ? arguments.Select(x => x.Value).ToArray() : new object?[] { null }).ToList();
    if (fact) cases.Add([]);
    foreach (var values in cases)
    {
        try
        {
            var parameters = method.GetParameters();
            var argsForCall = values.Select((v, i) => v == null ? null : Convert.ChangeType(v, Nullable.GetUnderlyingType(parameters[i].ParameterType) ?? parameters[i].ParameterType)).ToArray();
            var result = method.Invoke(Activator.CreateInstance(type), argsForCall);
            if (result is Task task) await task;
            passed++;
        }
        catch (Exception error)
        { failed++; Console.WriteLine($"FAIL {type.Name}.{method.Name}: {(error.InnerException ?? error).Message}"); }
    }
}
Console.WriteLine($"Pure cases: {passed} passed, {failed} failed. Integration tests are not included.");
return failed == 0 && passed > 0 ? 0 : 1;
}
