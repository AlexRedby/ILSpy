using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Resolver;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.Tests.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;

using NUnit.Framework;

using Cecil = Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace ICSharpCode.Decompiler.Tests.ProjectDecompiler;

[TestFixture]
public sealed class TokenizedTypeReferenceTests
{
	[TestCase("Assembly-CSharp", "Assembly-CSharp-firstpass", true, true)]
	[TestCase("Assembly-CSharp", "Assembly-CSharp-firstpass", false, true)]
	[TestCase("Assembly-CSharp", "Assembly-CSharp", true, true)]
	[TestCase("Assembly-CSharp", "Assembly-CSharp", false, true)]
	[TestCase("OtherTarget", "Assembly-CSharp-firstpass", true, true)]
	[TestCase("OtherTarget", "Assembly-CSharp-firstpass", false, true)]
	[TestCase("Assembly-CSharp", "Assembly-CSharp-firstpass", true, false)]
	[TestCase("Assembly-CSharp", "Assembly-CSharp-firstpass", false, false)]
	public void OnlyExactTargetModuleTypesAreTokenized(
		string targetName, string referenceName, bool withResolver, bool tokenize)
	{
		using var target = CreateModule(targetName, "GameType");
		using var reference = CreateModule(referenceName, "SdkType");
		var compilation = new SimpleCompilation(target, reference);
		var ownType = compilation.MainModule.TypeDefinitions.Single(t => t.Name == "GameType");
		var externalType = compilation.ReferencedModules.Single().TypeDefinitions.Single(t => t.Name == "SdkType");
		// Both modules deliberately allocate the same RID. Tokens are module-scoped.
		Assert.That(externalType.MetadataToken, Is.EqualTo(ownType.MetadataToken));
		var builder = withResolver
			? new TypeSystemAstBuilder(new CSharpResolver(compilation))
			: new TypeSystemAstBuilder { TargetModule = compilation.MainModule };
		builder.TokenizeNames = tokenize;
		builder.UseAliases = false;
		string ownName = tokenize ? $"type_{MetadataTokens.GetToken(ownType.MetadataToken):X8}" : "GameType";
		Assert.That(builder.ConvertType(ownType).ToString(), Is.EqualTo(ownName).Or.EqualTo("global::" + ownName));
		Assert.That(builder.ConvertType(externalType).ToString(), Is.EqualTo("SdkType").Or.EqualTo("global::SdkType"));
		Assert.That(builder.ConvertType(new ArrayType(compilation, externalType)).ToString(), Is.EqualTo("SdkType[]").Or.EqualTo("global::SdkType[]"));
	}

	[TestCase(false)]
	[TestCase(true)]
	public void MemberReferencesRetainIdentityAndHonorTokenization(bool tokenize)
	{
		var module = TypeSystemLoaderTests.TestAssembly;
		var settings = new DecompilerSettings { TokenizeNames = tokenize };
		var decompiler = new CSharpDecompiler(module,
			new UniversalAssemblyResolver(module.FileName, false, module.Metadata.DetectTargetFrameworkId()), settings);
		var tree = decompiler.DecompileType(new FullTypeName(typeof(TokenizedMemberSample).FullName));

		AssertReferenceName(nameof(TokenizedMemberSample.Value), "field", true);
		AssertReferenceName(nameof(TokenizedMemberSample.Helper), "method", true);
		AssertReferenceName(nameof(TokenizedMemberExtensions.Extend), "method", true);
		AssertReferenceName(nameof(Math.Abs), "method", false);
		AssertReferenceName(nameof(TokenizedMemberSample.Changed), "event", true);
		Assert.That(tree.Descendants.OfType<CSharp.Syntax.Attribute>()
			.Any(a => a.Type is SimpleType { Identifier: "DecompiledName" }
				&& a.Arguments.OfType<PrimitiveExpression>().Any(p => Equals(p.Value, "Changed"))), Is.EqualTo(tokenize));

		void AssertReferenceName(string name, string prefix, bool own)
		{
			var references = tree.Descendants.OfType<Expression>()
				.Where(e => e.GetResolveResult() is MemberResolveResult rr && rr.Member.Name == name)
				.Select(e => (Expression: e is InvocationExpression invocation ? invocation.Target : e,
					Member: ((MemberResolveResult)e.GetResolveResult()).Member))
				.Where(r => r.Expression is IdentifierExpression or MemberReferenceExpression)
				.ToArray();
			Assert.That(references, Is.Not.Empty, name);
			foreach (var (expression, member) in references)
			{
				Assert.That(member.ParentModule == decompiler.TypeSystem.MainModule, Is.EqualTo(own), name);
				string actual = expression is IdentifierExpression identifier
					? identifier.Identifier : ((MemberReferenceExpression)expression).MemberName;
				string expected = tokenize && own ? $"{prefix}_{MetadataTokens.GetToken(member.MetadataToken):X8}" : name;
				Assert.That(actual, Is.EqualTo(expected), name);
			}
		}
	}

	[TestCase(false)]
	[TestCase(true)]
	public void MemberTokensUseOwnTokenNamesAndRetainExternalNames(bool tokenize)
	{
		using var assembly = Cecil.AssemblyDefinition.CreateAssembly(
			new Cecil.AssemblyNameDefinition("TokenSample", new Version(1, 0)), "TokenSample.dll", Cecil.ModuleKind.Dll);
		var module = assembly.MainModule;
		var core = (Cecil.AssemblyNameReference)module.TypeSystem.CoreLibrary;
		var runtime = typeof(object).Assembly.GetName();
		core.Name = runtime.Name;
		core.Version = runtime.Version;
		core.PublicKeyToken = runtime.GetPublicKeyToken();
		var type = new Cecil.TypeDefinition("", "TokenSample", Cecil.TypeAttributes.Public, module.TypeSystem.Object);
		module.Types.Add(type);
		var field = new Cecil.FieldDefinition("Value", Cecil.FieldAttributes.Public | Cecil.FieldAttributes.Static, module.TypeSystem.Int32);
		type.Fields.Add(field);
		var helper = new Cecil.MethodDefinition("Helper", Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, module.TypeSystem.Int32);
		helper.Parameters.Add(new Cecil.ParameterDefinition(module.TypeSystem.Int32));
		helper.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldarg_0));
		helper.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ret));
		type.Methods.Add(helper);
		AddTokenMethod("OwnField", field, typeof(RuntimeFieldHandle));
		AddTokenMethod("OwnMethod", helper, typeof(RuntimeMethodHandle));
		AddTokenMethod("ExternalField", module.ImportReference(typeof(DateTime).GetField(nameof(DateTime.MinValue))), typeof(RuntimeFieldHandle));
		AddTokenMethod("ExternalMethod", module.ImportReference(typeof(Math).GetMethod(nameof(Math.Abs), new[] { typeof(int) })), typeof(RuntimeMethodHandle));
		using var stream = new MemoryStream();
		assembly.Write(stream);
		stream.Position = 0;
		using var peFile = new PEFile("TokenSample.dll", stream);
		var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
		string code = new CSharpDecompiler(peFile, resolver, new DecompilerSettings { TokenizeNames = tokenize })
			.DecompileTypeAsString(new FullTypeName("TokenSample"));
		string typeName = tokenize ? $"type_{type.MetadataToken.ToInt32():X8}" : type.Name;
		string fieldName = tokenize ? $"field_{field.MetadataToken.ToInt32():X8}" : field.Name;
		string methodName = tokenize ? $"method_{helper.MetadataToken.ToInt32():X8}" : helper.Name;
		Assert.That(code, Does.Contain($"__ldtoken({typeName}.{fieldName})"));
		Assert.That(code, Does.Contain($"__ldtoken({typeName}.{methodName}(int))"));
		Assert.That(code, Does.Contain("__ldtoken(DateTime.MinValue)"));
		Assert.That(code, Does.Contain("__ldtoken(Math.Abs(int))"));

		void AddTokenMethod(string name, Cecil.MemberReference member, Type handleType)
		{
			var method = new Cecil.MethodDefinition(name, Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static,
				module.ImportReference(handleType));
			method.Body.Instructions.Add(member is Cecil.FieldReference fieldReference
				? Cecil.Cil.Instruction.Create(OpCodes.Ldtoken, fieldReference)
				: Cecil.Cil.Instruction.Create(OpCodes.Ldtoken, (Cecil.MethodReference)member));
			method.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ret));
			type.Methods.Add(method);
		}
	}

	static PEFile CreateModule(string assemblyName, string typeName)
	{
		using var assembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(
			new Mono.Cecil.AssemblyNameDefinition(assemblyName, new Version(1, 0)),
			assemblyName + ".dll", Mono.Cecil.ModuleKind.Dll);
		assembly.MainModule.Types.Add(new Mono.Cecil.TypeDefinition(
			"", typeName, Mono.Cecil.TypeAttributes.Public, assembly.MainModule.TypeSystem.Object));
		var stream = new MemoryStream();
		assembly.Write(stream);
		stream.Position = 0;
		return new PEFile(assemblyName + ".dll", stream);
	}
}

public class TokenizedMemberSample
{
	public static int Value = 1;
	public static int Helper() => 2;
	public static int Read() => Math.Abs(Value) + Helper();
	public int ReadExtension() => this.Extend();
	public event Action Changed;
	public void Raise() => Changed?.Invoke();
}

public static class TokenizedMemberExtensions
{
	public static int Extend(this TokenizedMemberSample sample) => sample.GetHashCode();
}
