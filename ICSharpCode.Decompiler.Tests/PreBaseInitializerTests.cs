// Copyright (c) 2026 Alex Redby
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.Loader;

using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp.Transforms;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.Tests.Helpers;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NUnit.Framework;

using Cecil = Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using SyntaxTree = ICSharpCode.Decompiler.CSharp.Syntax.SyntaxTree;

namespace ICSharpCode.Decompiler.Tests;

[TestFixture]
public sealed class PreBaseInitializerTests
{
	[TestCase("Simple", false)]
	[TestCase("Simple", true)]
	[TestCase("Shared", true)]
	[TestCase("Chained", true)]
	[TestCase("Throwing", true)]
	[TestCase("Collision", true)]
	[TestCase("Generic", true)]
	[TestCase("GenericElement", true)]
	[TestCase("SecondArray", true)]
	[TestCase("Wrapped", false)]
	[TestCase("Wrapped", true)]
	[TestCase("WrappedShared", true)]
	[TestCase("WrappedChained", true)]
	[TestCase("WrappedGenericElement", true)]
	[TestCase("WrappedThrowing", true)]
	[TestCase("WrappedObjectThrowing", true)]
	public void RetainedFactoryInitializerRoundtrips(string shape, bool optimize)
	{
		byte[] original = BuildFixture(shape, optimize);
		var tree = Decompile(original);
		string code = tree.ToString();
		Assert.That(code, Does.Not.Contain("_002Ector"), code);
		var helpers = tree.Descendants.OfType<MethodDeclaration>()
			.Where(m => m.Name.StartsWith("__InitializeField", StringComparison.Ordinal)).ToArray();
		Assert.That(helpers.Count(m => m.Body.Descendants.OfType<InvocationExpression>()
			.Any(i => i.ToString().Contains("Factory.Create"))), Is.EqualTo(shape == "SecondArray" ? 2 : 1), code);
		Assert.That(helpers.Where(m => m.GetSymbol() == null).All(m => m.Annotation<ILFunction>() == null), Is.True);
		byte[] recompiled = Compile(code, optimize);
		Assert.That(Run(recompiled), Is.EqualTo(Run(original)));
		Assert.That(Run(original), Does.StartWith(shape is "Throwing" or "WrappedThrowing" ? "1CF23|InvalidOperationException"
			: shape == "WrappedObjectThrowing" ? "1CF23W|InvalidOperationException"
			: shape.StartsWith("Wrapped", StringComparison.Ordinal) ? "1CF23W4B|1,2,3,4"
			: shape == "SecondArray" ? "1CF23F454B|1,2,3,4" : "1CF234B|1,2,3,4"));
	}

	[TestCase("Simple")]
	[TestCase("Wrapped")]
	public void TokenizedInitializerKeepsRealMetadataIdentities(string shape)
	{
		byte[] original = BuildFixture(shape, true);
		using var pe = new PEFile("PreBase.dll", new MemoryStream(original));
		var decompiler = CreateDecompiler(pe, tokenize: true);
		var tree = decompiler.DecompileWholeModuleAsSingleFile();
		var helper = tree.Descendants.OfType<MethodDeclaration>().Single(m => m.GetSymbol() == null);
		Assert.That(helper.Name, Does.StartWith("__InitializeField"));
		Assert.That(helper.Annotation<ILFunction>(), Is.Null);
		Assert.That(helper.ReturnType.GetResolveResult().Type.Equals(
			tree.Descendants.OfType<FieldDeclaration>().Single(f => f.GetSymbol() is IField { Name: "Points" }).ReturnType.GetResolveResult().Type), Is.True);
		Assert.That(helper.ReturnType.ToString(), Does.StartWith("type_"));
		var factory = helper.Body.Descendants.OfType<InvocationExpression>()
			.Single(i => i.GetSymbol() is IMethod { Name: "Create" });
		Assert.That(((IMethod)factory.GetSymbol()).MetadataToken.IsNil, Is.False);
		Assert.That(tree.Descendants.OfType<InvocationExpression>()
			.Any(i => i.GetSymbol() is IMethod { IsConstructor: true }), Is.False);
	}

	[Test]
	public void MovedLocalCannotShadowFactoryType()
	{
		using var pe = new PEFile("PreBase.dll", new MemoryStream(BuildFixture("Simple", true)));
		var decompiler = CreateDecompiler(pe);
		int index = decompiler.AstTransforms.Select((transform, i) => (transform, i))
			.Single(p => p.transform is TransformFieldAndConstructorInitializers).i;
		decompiler.AstTransforms.Insert(index, new FactoryNameLocal());
		var tree = decompiler.DecompileWholeModuleAsSingleFile();
		Assert.That(tree.Descendants.OfType<MethodDeclaration>().Any(m => m.GetSymbol() == null), Is.False, tree.ToString());
	}

	sealed class FactoryNameLocal : IAstTransform
	{
		public void Run(AstNode node, TransformContext context)
		{
			foreach (var ctor in node.Descendants.OfType<ConstructorDeclaration>())
			{
				foreach (var declaration in ctor.Body.Statements.OfType<VariableDeclarationStatement>())
				{
					var variable = declaration.Variables.Single();
					var local = variable.Annotation<ILVariableResolveResult>().Variable;
					variable.Name = local.Name = "Factory";
					foreach (var reference in ctor.Body.Descendants.OfType<IdentifierExpression>()
						.Where(e => e.Annotation<ILVariableResolveResult>()?.Variable == local))
						reference.Identifier = "Factory";
				}
			}
		}
	}

	[TestCase("Simple")]
	[TestCase("Shared")]
	[TestCase("WrappedShared")]
	public void ExtractedArrayRangesMapToInitializerInEveryConstructor(string shape)
	{
		byte[] bytes = BuildFixture(shape, true);
		using var pe = new PEFile("PreBase.dll", new MemoryStream(bytes));
		var decompiler = CreateDecompiler(pe);
		var tree = decompiler.DecompileWholeModuleAsSingleFile();
		var initializer = tree.Descendants.OfType<FieldDeclaration>()
			.Single(f => f.GetSymbol() is IField { Name: "Points" }).Variables.Single().Initializer;
		using var output = new StringWriter();
		var writer = TokenWriter.WrapInWriterThatSetsLocationsInAST(new TextWriterTokenWriter(output));
		tree.AcceptVisitor(new CSharpOutputVisitor(writer, new DecompilerSettings().CSharpFormattingOptions));
		var points = decompiler.CreateSequencePoints(tree);
		using var original = Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
		foreach (var ctor in original.MainModule.GetType("Derived").Methods.Where(m => m.IsConstructor))
		{
			var ranges = points.Single(p => MetadataTokens.GetToken(p.Key.Method.MetadataToken) == ctor.MetadataToken.ToInt32()).Value;
			int offset = ctor.Body.Instructions.Single(i => i.OpCode == OpCodes.Call
				&& i.Operand is Cecil.MethodReference m && m.DeclaringType.Name == "Factory" && m.Name == "Create").Offset;
			Assert.That(ranges.Any(p => p.StartLine == initializer.StartLocation.Line && p.Offset <= offset && p.EndOffset > offset),
				Is.True, "The factory work must remain mapped to this constructor's field initializer.");
		}
	}

	[TestCase("Parameter")]
	[TestCase("Instance")]
	[TestCase("Escaping")]
	[TestCase("Different")]
	[TestCase("FieldOrder")]
	[TestCase("Branch")]
	[TestCase("AfterBase")]
	[TestCase("DelayedThis")]
	[TestCase("ParameterElement")]
	[TestCase("ParameterIndex")]
	[TestCase("Duplicate")]
	[TestCase("ExceptionRegion")]
	[TestCase("WrappedParameter")]
	[TestCase("WrappedInstance")]
	[TestCase("WrappedEscaping")]
	[TestCase("WrappedDifferent")]
	[TestCase("WrappedFieldOrder")]
	[TestCase("WrappedParameterElement")]
	[TestCase("WrappedParameterIndex")]
	[TestCase("WrappedParameterArgument")]
	[TestCase("WrappedInstanceArgument")]
	[TestCase("WrappedLambdaArgument")]
	[TestCase("WrappedRefArgument")]
	public void UnsafeOrUnnecessaryExtractionDoesNotAddHelper(string shape)
	{
		var tree = Decompile(BuildFixture(shape, true));
		Assert.That(tree.Descendants.OfType<MethodDeclaration>()
			.Any(m => m.Name.StartsWith("__InitializeField", StringComparison.Ordinal)), Is.False, tree.ToString());
	}

	[Test]
	public void ConstructorOnlyDecompilationDoesNotAddHelper()
	{
		byte[] bytes = BuildFixture("Simple", true);
		using var pe = new PEFile("PreBase.dll", new MemoryStream(bytes));
		var decompiler = CreateDecompiler(pe);
		var type = decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.Name == "Derived");
		var tree = decompiler.Decompile(type.Methods.Single(m => m.IsConstructor).MetadataToken);
		Assert.That(tree.Descendants.OfType<MethodDeclaration>(), Is.Empty, tree.ToString());
	}

	static SyntaxTree Decompile(byte[] bytes)
	{
		using var pe = new PEFile("PreBase.dll", new MemoryStream(bytes));
		return CreateDecompiler(pe).DecompileWholeModuleAsSingleFile();
	}

	static CSharpDecompiler CreateDecompiler(PEFile pe, bool tokenize = false)
	{
		var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
		var decompiler = new CSharpDecompiler(pe, resolver, new DecompilerSettings {
			UsePrimaryConstructorSyntaxForNonRecordTypes = false, TokenizeNames = tokenize
		});
		decompiler.AstTransforms.Insert(0, new RemoveCompilerAttribute());
		decompiler.AstTransforms.Add(new EscapeInvalidIdentifiers());
		return decompiler;
	}

	static byte[] Compile(string code, bool optimize)
	{
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
			.Select(path => MetadataReference.CreateFromFile(path));
		var compilation = CSharpCompilation.Create("PreBase", new[] { CSharpSyntaxTree.ParseText(code) }, references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
				optimizationLevel: optimize ? OptimizationLevel.Release : OptimizationLevel.Debug));
		using var stream = new MemoryStream();
		var result = compilation.Emit(stream);
		Assert.That(result.Success, Is.True, () => string.Join(Environment.NewLine, result.Diagnostics) + "\n" + code);
		return stream.ToArray();
	}

	static string Run(byte[] bytes, string type = "Entry", string method = "Run")
	{
		var context = new AssemblyLoadContext("PreBase", isCollectible: true);
		try
		{
			using var stream = new MemoryStream(bytes);
			var assembly = context.LoadFromStream(stream);
			return (string)assembly.GetType(type).GetMethod(method).Invoke(null, null);
		}
		finally
		{
			context.Unload();
		}
	}

	static byte[] BuildFixture(string shape, bool optimize)
	{
		bool wrapped = shape.StartsWith("Wrapped", StringComparison.Ordinal);
		if (wrapped)
			shape = shape.Length == "Wrapped".Length ? "Simple" : shape.Substring("Wrapped".Length);
		string body = "Scalar = Trace.Mark(1); var array = Factory.Create(2); "
			+ "array[0] = new Item(Trace.Mark(2)); array[1] = new Item(Trace.Mark(3)); Points = array; Tail = Trace.Mark(4);";
		string parameter = shape.StartsWith("Parameter", StringComparison.Ordinal) ? "int size" : "";
		if (shape == "Parameter")
			body = body.Replace("Factory.Create(2)", "Factory.Create(size)");
		if (shape == "Instance")
			body = body.Replace("Trace.Mark(2)", "ReadInstance()");
		if (shape == "ParameterElement")
			body = body.Replace("Trace.Mark(2)", "Trace.Mark(size)");
		if (shape == "ParameterIndex")
			body = body.Replace("array[0]", "array[size - 2]");
		if (shape == "Duplicate")
			body += "Tail = Trace.Mark(5);";
		if (shape == "ExceptionRegion")
			body = "try { " + body + " } catch (Exception) { throw; }";
		if (shape == "Escaping")
			body += "Factory.Escaped = array;";
		if (shape == "Branch")
			body = body.Replace("array[0] = new Item(Trace.Mark(2));", "if (Trace.Value.Length > 0) array[0] = new Item(Trace.Mark(2));");
		if (shape == "SecondArray")
			body = body.Replace("Tail =", "var second = Factory.Create(2); second[0] = new Item(Trace.Mark(4)); second[1] = new Item(Trace.Mark(5)); Extra = second; Tail =");
		if (shape == "GenericElement")
			body = body.Replace("Factory.Create(2)", "Factory.Create<T>(2)").Replace("new Item(", "(T)(object)new Item(");
		string wrapperArgument = shape switch {
			"ParameterArgument" => ", size",
			"InstanceArgument" => ", ReadInstance()",
			"LambdaArgument" => ", () => Scalar",
			"RefArgument" => ", ref Scalar",
			_ => ""
		};
		string itemType = shape == "GenericElement" ? "T" : "Item";
		if (wrapped)
			body = body.Replace("Points = array;", $"Points = new Wrapper<{itemType}>(array{wrapperArgument});");
		string generic = shape is "Generic" or "GenericElement" ? "<T>" : "";
		string type = shape == "Generic" ? "Derived<string>" : shape == "GenericElement" ? "Derived<Item>" : "Derived";
		string other = shape is "Shared" or "Different" ? "public Derived(bool unused) { "
			+ (shape == "Different" ? body.Replace("Trace.Mark(3)", "Trace.Mark(9)") : body) + " }" : "";
		if (shape is "Chained" or "DelayedThis")
			other = "public Derived(int unused) : this() { Trace.Mark(5); }";
		string collision = shape == "Collision" ? "private static Item[] __InitializeField0() => null;" : "";
		string arguments = parameter.Length > 0 ? "2" : "";
		string repeat = shape is "Shared" or "Different" ? $"new {type}(true);" : shape == "Chained" ? $"new {type}(0);" : "";
		string code = $$"""
			using System;
			using System.Runtime.CompilerServices;
			public static class Trace {
				public static string Value = "";
				public static int Mark(int value) { Value += value; if (value == 3 && {{(shape == "Throwing" ? "true" : "false")}}) throw new InvalidOperationException(); return value; }
			}
			public struct Item { public int Value; public Item(int value) { Value = value; } }
			public class Wrapper<T> {
				public T[] Items;
				public Wrapper(T[] items) { Trace.Value += "W"; if ({{(shape == "ObjectThrowing" ? "true" : "false")}}) throw new InvalidOperationException(); Items = items; }
				public Wrapper(T[] items, int unused) : this(items) { }
				public Wrapper(T[] items, Func<int> unused) : this(items) { }
				public Wrapper(T[] items, ref int unused) : this(items) { }
			}
			public static class Factory {
				public static Item[] Escaped;
				static Factory() { Trace.Value += "C"; }
				[MethodImpl(MethodImplOptions.Synchronized)]
				public static Item[] Create(int size) { Trace.Value += "F"; return new Item[size]; }
				public static T[] Create<T>(int size) { Trace.Value += "F"; return new T[size]; }
			}
			public class Observer {
				public string Observed;
				public Observer() { Trace.Value += "B"; Observed = Snapshot(); }
				public virtual string Snapshot() => "";
			}
			public class Derived{{generic}} : Observer {
				public int Scalar;
				public {{(wrapped ? $"Wrapper<{itemType}>" : itemType + "[]")}} Points;
				{{(shape == "SecondArray" ? "public Item[] Extra;" : "")}}
				public int Tail;
				public Derived({{parameter}}) { {{body}} }
				{{other}}
				{{collision}}
				public int ReadInstance() => Scalar;
				public override string Snapshot() => Scalar + "," + ((Item)(object)Points{{(wrapped ? ".Items" : "")}}[0]).Value + "," + ((Item)(object)Points{{(wrapped ? ".Items" : "")}}[1]).Value + "," + Tail;
			}
			public static class Entry {
				public static string Run() {
					Trace.Value = "";
					try { var value = new {{type}}({{arguments}}); string first = Trace.Value + "|" + value.Observed; {{repeat}} return first + ";" + Trace.Value; }
					catch (Exception ex) { return Trace.Value + "|" + ex.GetType().Name; }
				}
			}
			""";
		using var assembly = Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(Compile(code, optimize)));
		var derived = assembly.MainModule.Types.Single(t => t.Name.StartsWith("Derived", StringComparison.Ordinal));
		foreach (var ctor in derived.Methods.Where(m => m.IsConstructor && !m.IsStatic))
		{
			var instructions = ctor.Body.Instructions;
			var call = instructions.Single(i => i.OpCode == OpCodes.Call && i.Operand is Cecil.MethodReference m && m.Name == ".ctor");
			var target = (Cecil.MethodReference)call.Operand;
			if (shape == "AfterBase" || target.DeclaringType.Name == derived.Name && shape != "DelayedThis")
				continue;
			Assert.That(call.Previous.OpCode, Is.EqualTo(OpCodes.Ldarg_0));
			var loadThis = call.Previous;
			instructions.Remove(loadThis);
			instructions.Remove(call);
			int insertion = shape == "Escaping" ? instructions.IndexOf(instructions.Single(i => i.OpCode == OpCodes.Stsfld
				&& i.Operand is Cecil.FieldReference f && f.Name == "Escaped").Previous) : instructions.Count - 1;
			instructions.Insert(insertion, loadThis);
			instructions.Insert(insertion + 1, call);
		}
		if (shape == "FieldOrder")
		{
			var field = derived.Fields.Single(f => f.Name == "Points");
			derived.Fields.Remove(field);
			derived.Fields.Insert(0, field);
		}
		using var output = new MemoryStream();
		assembly.Write(output);
		return output.ToArray();
	}
}
