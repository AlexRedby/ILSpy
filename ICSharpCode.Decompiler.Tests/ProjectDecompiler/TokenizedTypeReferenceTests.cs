using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler.CSharp.Resolver;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;

using NUnit.Framework;

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
