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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NUnit.Framework;

using Cecil = Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace ICSharpCode.Decompiler.Tests;

[TestFixture]
public sealed class ArrayInitializationTests
{
	[Test]
	public void PureArrayFactoryRecoversPrimitiveInitializer()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9)));

		Assert.That(code, Does.Contain("ArrayFactory.Create(3)"));
		Assert.That(code, Does.Contain("array[0] = 1"));
		Assert.That(code, Does.Contain("array[1] = 5"));
		Assert.That(code, Does.Contain("array[2] = 9"));
		Assert.That(code, Does.Not.Contain("RuntimeHelpers.InitializeArray"));
	}

	[TestCase(ArrayKind.UInt16, "array[0] = 1")]
	[TestCase(ArrayKind.Byte, "array[0] = 1")]
	[TestCase(ArrayKind.Boolean, "array[0] = true")]
	[TestCase(ArrayKind.Char, "array[0] = 'A'")]
	[TestCase(ArrayKind.Single, "array[0] = 1.5f")]
	[TestCase(ArrayKind.Double, "array[0] = 1.5")]
	public void PureArrayFactoryRecoversPrimitiveElementKinds(ArrayKind kind, string expectedStore)
	{
		string code = Decompile(BuildAssembly(kind, BlobFor(kind)));

		Assert.That(code, Does.Contain("ArrayFactory.Create(3)"));
		Assert.That(code, Does.Contain(expectedStore));
		Assert.That(code, Does.Not.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void DynamicLengthKeepsInitializeArray()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9), dynamicLength: true));

		Assert.That(code, Does.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void ShortRvaKeepsInitializeArray()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, new byte[] { 1, 0, 0, 0 }));

		Assert.That(code, Does.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void ArbitraryFactoryKeepsInitializeArray()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9), FactoryKind.Cached));

		Assert.That(code, Does.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void SideEffectingFactoryKeepsInitializeArray()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9), FactoryKind.SideEffecting));

		Assert.That(code, Does.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void CctorAndSynchronizedFactoryStillRecoversInitializer()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9), synchronizedFactory: true, factoryCctor: true));

		Assert.That(code, Does.Contain("ArrayFactory.Create(3)"));
		Assert.That(code, Does.Contain("array[0] = 1"));
		Assert.That(code, Does.Not.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void EnumArrayRecoversUnderlyingValues()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Enum, Blob(1, 5, 9)));

		Assert.That(code, Does.Contain("ArrayFactory.Create(3)"));
		Assert.That(code, Does.Not.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void GenericFactoryRecoversInitializer()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9), genericFactory: true));

		Assert.That(code, Does.Contain("ArrayFactory.Create<int>(3)"));
		Assert.That(code, Does.Contain("array[2] = 9"));
		Assert.That(code, Does.Not.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void InlineFactoryAllocationRecoversInitializer()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9), inlineInitialization: true));

		Assert.That(code, Does.Contain("ArrayFactory.Create(3)"));
		Assert.That(code, Does.Contain("[2] = 9"));
		Assert.That(code, Does.Not.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void DirectNewArrayPathRemainsSupported()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9), directAllocation: true));

		Assert.That(code, Does.Contain("1, 5, 9"));
		Assert.That(code, Does.Not.Contain("RuntimeHelpers.InitializeArray"));
	}

	[TestCase(0)]
	[TestCase(-1)]
	[TestCase(int.MaxValue)]
	public void InvalidFactoryLengthKeepsInitializeArray(int length)
	{
		string code = Decompile(BuildAssembly(ArrayKind.Int32, Blob(1, 5, 9), requestedLength: length));

		Assert.That(code, Does.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void NonCanonicalBooleanBytesKeepInitializeArray()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Boolean, new byte[] { 1, 2, 1 }));

		Assert.That(code, Does.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void CustomNaNPayloadKeepsInitializeArray()
	{
		string code = Decompile(BuildAssembly(ArrayKind.Single, Blob(unchecked((int)0x7FC01234), 0, 0)));

		Assert.That(code, Does.Contain("RuntimeHelpers.InitializeArray"));
	}

	[Test]
	public void SpecialFloatingPointBitsRoundtrip()
	{
		byte[] bits = Blob(
			unchecked((int)0x80000000),
			unchecked((int)0x7F800000),
			unchecked((int)0xFF800000));
		AssertRoundtrip(ArrayKind.Single, bits);
	}

	[TestCase(ArrayKind.Int32)]
	[TestCase(ArrayKind.UInt16)]
	[TestCase(ArrayKind.Byte)]
	[TestCase(ArrayKind.Boolean)]
	[TestCase(ArrayKind.Char)]
	[TestCase(ArrayKind.Single)]
	[TestCase(ArrayKind.Double)]
	[TestCase(ArrayKind.Enum)]
	public void RecoveredContentsRoundtrip(ArrayKind kind)
	{
		AssertRoundtrip(kind, kind is ArrayKind.Int32 or ArrayKind.Enum ? Blob(1, 5, 9) : BlobFor(kind));
	}

	static void AssertRoundtrip(ArrayKind kind, byte[] initialValue)
	{
		using var fixture = BuildAssembly(kind, initialValue, synchronizedFactory: true, factoryCctor: true);
		using var original = new MemoryStream();
		fixture.Write(original);
		original.Position = 0;
		string code = Decompile(new MemoryStream(original.ToArray()), wholeModule: true);
		Assert.That(code, Does.Not.Contain("RuntimeHelpers.InitializeArray"));
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
			.Select(path => MetadataReference.CreateFromFile(path));
		var compilation = CSharpCompilation.Create("ArrayRecompiled", new[] { CSharpSyntaxTree.ParseText(code) }, references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
		using var recompiled = new MemoryStream();
		var result = compilation.Emit(recompiled);
		Assert.That(result.Success, Is.True, () => string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine + code);
		var originalContext = new AssemblyLoadContext("ArrayOriginal", isCollectible: true);
		var recompiledContext = new AssemblyLoadContext("ArrayRecompiled", isCollectible: true);
		try
		{
			original.Position = 0;
			recompiled.Position = 0;
			var originalAssembly = originalContext.LoadFromStream(original);
			var recompiledAssembly = recompiledContext.LoadFromStream(recompiled);
			var expected = (Array)originalAssembly.GetType("ArrayFixture").GetMethod("Read").Invoke(null, null);
			var read = recompiledAssembly.GetType("ArrayFixture").GetMethod("Read");
			var actual = (Array)read.Invoke(null, null);
			Assert.That(actual.Length, Is.EqualTo(expected.Length));
			for (int i = 0; i < expected.Length; i++)
			{
				object expectedValue = expected.GetValue(i), actualValue = actual.GetValue(i);
				if (kind == ArrayKind.Single)
					Assert.That(BitConverter.SingleToInt32Bits((float)actualValue), Is.EqualTo(BitConverter.SingleToInt32Bits((float)expectedValue)));
				else if (kind == ArrayKind.Double)
					Assert.That(BitConverter.DoubleToInt64Bits((double)actualValue), Is.EqualTo(BitConverter.DoubleToInt64Bits((double)expectedValue)));
				else if (kind == ArrayKind.Enum)
					Assert.That(Convert.ToInt32(actualValue), Is.EqualTo(Convert.ToInt32(expectedValue)));
				else
					Assert.That(actualValue, Is.EqualTo(expectedValue));
			}
			Assert.That(read.Invoke(null, null), Is.Not.SameAs(actual));
			var factory = recompiledAssembly.GetType("ArrayFactory");
			Assert.That(factory.GetField("Counter", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), Is.EqualTo(1));
			Assert.That(factory.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static).GetMethodImplementationFlags()
				.HasFlag(MethodImplAttributes.Synchronized), Is.True);
		}
		finally
		{
			originalContext.Unload();
			recompiledContext.Unload();
		}
	}

	static byte[] Blob(params int[] values)
	{
		return values.SelectMany(BitConverter.GetBytes).ToArray();
	}

	static byte[] BlobFor(ArrayKind kind)
	{
		return kind switch {
			ArrayKind.UInt16 => new byte[] { 1, 0, 5, 0, 9, 0 },
			ArrayKind.Byte => new byte[] { 1, 5, 9 },
			ArrayKind.Boolean => new byte[] { 1, 0, 1 },
			ArrayKind.Char => new byte[] { 65, 0, 66, 0, 67, 0 },
			ArrayKind.Single => Blob(BitConverter.SingleToInt32Bits(1.5f), BitConverter.SingleToInt32Bits(2.5f), BitConverter.SingleToInt32Bits(3.5f)),
			ArrayKind.Double => BitConverter.GetBytes(1.5d).Concat(BitConverter.GetBytes(2.5d)).Concat(BitConverter.GetBytes(3.5d)).ToArray(),
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};
	}

	static string Decompile(Cecil.AssemblyDefinition assembly)
	{
		using (assembly)
		{
			using var stream = new MemoryStream();
			assembly.Write(stream);
			stream.Position = 0;
			return Decompile(stream);
		}
	}

	static string Decompile(Stream stream, bool wholeModule = false)
	{
		using var peFile = new PEFile("ArrayInitialization.dll", stream);
		var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
		var decompiler = new CSharpDecompiler(peFile, resolver, new DecompilerSettings());
		return wholeModule ? decompiler.DecompileWholeModuleAsString() : decompiler.DecompileTypeAsString(new FullTypeName("ArrayFixture"));
	}

	static Cecil.AssemblyDefinition BuildAssembly(ArrayKind kind, byte[] initialValue, FactoryKind factoryKind = FactoryKind.Pure,
		bool dynamicLength = false, bool synchronizedFactory = false, bool factoryCctor = false, int requestedLength = 3,
		bool directAllocation = false, bool genericFactory = false, bool inlineInitialization = false)
	{
		var assembly = Cecil.AssemblyDefinition.CreateAssembly(
			new Cecil.AssemblyNameDefinition("ArrayInitialization", new Version(1, 0)),
			"ArrayInitialization.dll", Cecil.ModuleKind.Dll);
		var module = assembly.MainModule;
		var core = (Cecil.AssemblyNameReference)module.TypeSystem.CoreLibrary;
		var runtime = typeof(object).Assembly.GetName();
		core.Name = runtime.Name;
		core.Version = runtime.Version;
		core.PublicKeyToken = runtime.GetPublicKeyToken();
		var type = new Cecil.TypeDefinition("", "ArrayFixture", Cecil.TypeAttributes.Public | Cecil.TypeAttributes.Class, module.TypeSystem.Object);
		module.Types.Add(type);

		Cecil.TypeReference elementType = kind switch {
			ArrayKind.Int32 => module.TypeSystem.Int32,
			ArrayKind.UInt16 => module.TypeSystem.UInt16,
			ArrayKind.Byte => module.TypeSystem.Byte,
			ArrayKind.Boolean => module.TypeSystem.Boolean,
			ArrayKind.Char => module.TypeSystem.Char,
			ArrayKind.Single => module.TypeSystem.Single,
			ArrayKind.Double => module.TypeSystem.Double,
			ArrayKind.Enum => CreateEnum(module),
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};
		var arrayType = new Cecil.ArrayType(elementType);
		var blobType = new Cecil.TypeDefinition("", "BlobData", Cecil.TypeAttributes.NotPublic | Cecil.TypeAttributes.Sealed | Cecil.TypeAttributes.ExplicitLayout, module.ImportReference(typeof(ValueType))) {
			ClassSize = initialValue.Length,
			PackingSize = 1
		};
		module.Types.Add(blobType);
		var blobField = new Cecil.FieldDefinition("Blob", Cecil.FieldAttributes.Private | Cecil.FieldAttributes.Static | Cecil.FieldAttributes.HasFieldRVA, blobType) {
			InitialValue = initialValue
		};
		type.Fields.Add(blobField);

		var factory = new Cecil.TypeDefinition("", "ArrayFactory", Cecil.TypeAttributes.NotPublic | Cecil.TypeAttributes.Class, module.TypeSystem.Object);
		module.Types.Add(factory);
		var cachedField = new Cecil.FieldDefinition("Cached", Cecil.FieldAttributes.Private | Cecil.FieldAttributes.Static, arrayType);
		var counterField = new Cecil.FieldDefinition("Counter", Cecil.FieldAttributes.Private | Cecil.FieldAttributes.Static, module.TypeSystem.Int32);
		factory.Fields.Add(cachedField);
		factory.Fields.Add(counterField);
		var create = new Cecil.MethodDefinition("Create", Cecil.MethodAttributes.Assembly | Cecil.MethodAttributes.Static, arrayType);
		Cecil.TypeReference factoryElementType = elementType;
		Cecil.MethodReference factoryCall = create;
		if (genericFactory)
		{
			var parameter = new Cecil.GenericParameter("T", create);
			create.GenericParameters.Add(parameter);
			factoryElementType = parameter;
			create.ReturnType = new Cecil.ArrayType(parameter);
			var specialized = new Cecil.GenericInstanceMethod(create);
			specialized.GenericArguments.Add(elementType);
			factoryCall = specialized;
		}
		if (synchronizedFactory)
			create.ImplAttributes |= Cecil.MethodImplAttributes.Synchronized;
		create.Parameters.Add(new Cecil.ParameterDefinition("length", Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
		create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldarg_0));
		create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Newarr, factoryElementType));
		create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ret));
		factory.Methods.Add(create);
		if (factoryCctor)
		{
			var cctor = new Cecil.MethodDefinition(".cctor", Cecil.MethodAttributes.Private | Cecil.MethodAttributes.Static | Cecil.MethodAttributes.SpecialName | Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
			cctor.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldc_I4_1));
			cctor.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Stsfld, counterField));
			cctor.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ret));
			factory.Methods.Add(cctor);
		}

		if (factoryKind != FactoryKind.Pure)
		{
			if (factoryKind == FactoryKind.Cached)
			{
				create.Body.Instructions.Clear();
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldsfld, cachedField));
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ret));
			}
			else
			{
				create.Body.Instructions.Clear();
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldsfld, counterField));
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldc_I4_1));
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Add));
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Stsfld, counterField));
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldarg_0));
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Newarr, elementType));
				create.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ret));
			}
		}

		var read = new Cecil.MethodDefinition("Read", Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static,
			inlineInitialization ? module.TypeSystem.Void : arrayType);
		if (dynamicLength)
			read.Parameters.Add(new Cecil.ParameterDefinition("length", Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
		var il = read.Body.GetILProcessor();
		if (dynamicLength)
			il.Append(il.Create(OpCodes.Ldarg_0));
		else
			il.Append(il.Create(OpCodes.Ldc_I4, requestedLength));
		if (directAllocation)
			il.Append(il.Create(OpCodes.Newarr, elementType));
		else
			il.Append(il.Create(OpCodes.Call, factoryCall));
		var local = new Cecil.Cil.VariableDefinition(arrayType);
		read.Body.Variables.Add(local);
		if (!inlineInitialization)
		{
			il.Append(il.Create(OpCodes.Stloc, local));
			il.Append(il.Create(OpCodes.Ldloc, local));
		}
		il.Append(il.Create(OpCodes.Ldtoken, blobField));
		il.Append(il.Create(OpCodes.Call, module.ImportReference(typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.InitializeArray)))));
		if (!inlineInitialization)
			il.Append(il.Create(OpCodes.Ldloc, local));
		il.Append(il.Create(OpCodes.Ret));
		type.Methods.Add(read);
		return assembly;
	}

	static Cecil.TypeDefinition CreateEnum(Cecil.ModuleDefinition module)
	{
		var type = new Cecil.TypeDefinition("", "FixtureEnum", Cecil.TypeAttributes.Public | Cecil.TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
		type.Fields.Add(new Cecil.FieldDefinition("value__", Cecil.FieldAttributes.Public | Cecil.FieldAttributes.SpecialName | Cecil.FieldAttributes.RTSpecialName, module.TypeSystem.Int32));
		module.Types.Add(type);
		return type;
	}

	public enum ArrayKind
	{
		Int32,
		UInt16,
		Byte,
		Boolean,
		Char,
		Single,
		Double,
		Enum
	}

	public enum FactoryKind
	{
		Pure,
		Cached,
		SideEffecting
	}
}
