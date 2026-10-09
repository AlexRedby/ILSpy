// Copyright (c) 2026 AlexRedby
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
public sealed class ObjectReferenceConversionTests
{
	public enum ReferenceInput
	{
		StringLocal,
		ObjectParameter,
		MethodReturn,
		ReadonlyField,
		NullLiteral
	}

	public enum Conversion
	{
		NativeSigned,
		NativeUnsigned,
		Signed64,
		Unsigned64
	}

	[Test]
	public void ObjectReferencesCompileWithoutSyntheticPinning(
		[Values] ReferenceInput input, [Values] Conversion conversion, [Values] bool nativeIntegers)
	{
		using var assembly = BuildAssembly();
		AddConversion(assembly.MainModule, input, conversion);
		byte[] original = Serialize(assembly);
		string code = Decompile(original, nativeIntegers);

		Assert.That(code, Does.Not.Contain("fixed ("));
		if (input != ReferenceInput.NullLiteral)
			Assert.That(code, Does.Contain("Unsafe.As<"));
		byte[] recompiled = Compile(code);

		// Native-width conversions can be checked without dereferencing the untracked address.
		if (conversion is Conversion.NativeSigned or Conversion.NativeUnsigned)
		{
			object argument = input == ReferenceInput.ObjectParameter ? new object() : "abc";
			var expected = Invoke(original, "Convert", argument);
			var actual = Invoke(recompiled, "Convert", argument);
			Assert.That(IsZero(actual.Value), Is.EqualTo(IsZero(expected.Value)));
			Assert.That(IsZero(actual.Value), Is.EqualTo(input == ReferenceInput.NullLiteral));
			Assert.That(actual.Counter, Is.EqualTo(expected.Counter));
			Assert.That(actual.Counter, Is.EqualTo(input == ReferenceInput.MethodReturn ? 1 : 0));
		}
	}

	[Test]
	public void ClassThisReferenceConversionCompiles([Values] bool nativeIntegers)
	{
		using var assembly = BuildAssembly();
		var module = assembly.MainModule;
		var method = new Cecil.MethodDefinition("ConvertThis", Cecil.MethodAttributes.Public, module.TypeSystem.UIntPtr);
		module.GetType("ConversionFixture").Methods.Add(method);
		var il = method.Body.GetILProcessor();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Conv_U);
		il.Emit(OpCodes.Ret);
		string code = Decompile(Serialize(assembly), nativeIntegers);

		Assert.That(code, Does.Contain("Unsafe.As<"));
		Assert.That(code, Does.Not.Contain("fixed ("));
		Compile(code);
	}

	[Test]
	public void ReadonlyStringParameterConversionCompiles([Values] bool nativeIntegers)
	{
		using var assembly = BuildAssembly();
		var module = assembly.MainModule;
		var method = new Cecil.MethodDefinition("ConvertReadonly", Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, module.TypeSystem.UIntPtr);
		var parameter = new Cecil.ParameterDefinition("text", Cecil.ParameterAttributes.In, new Cecil.ByReferenceType(module.TypeSystem.String));
		parameter.CustomAttributes.Add(new Cecil.CustomAttribute(module.ImportReference(typeof(IsReadOnlyAttribute).GetConstructor(Type.EmptyTypes))));
		method.Parameters.Add(parameter);
		module.GetType("ConversionFixture").Methods.Add(method);
		var il = method.Body.GetILProcessor();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ldind_Ref);
		il.Emit(OpCodes.Conv_U);
		il.Emit(OpCodes.Ret);
		string code = Decompile(Serialize(assembly), nativeIntegers);

		Assert.That(code, Does.Contain("in string text"));
		Assert.That(code, Does.Contain("Unsafe.As<"));
		Assert.That(code, Does.Not.Contain("fixed ("));
		Compile(code);
	}

	[Test]
	public void OrdinaryStringOffsetConversionKeepsOriginalLifetime([Values] bool nativeIntegers)
	{
		using var assembly = BuildAssembly();
		AddStringRead(assembly.MainModule, pinned: false);
		byte[] original = Serialize(assembly);
		string code = Decompile(original, nativeIntegers);

		Assert.That(code, Does.Contain("Unsafe.As<"));
		Assert.That(code, Does.Contain("OffsetToStringData"));
		Assert.That(code, Does.Not.Contain("fixed ("));
		byte[] recompiled = Compile(code);
		AssertStringReads(original, recompiled);
	}

	[Test]
	public void GenuinePinnedStringStillUsesFixed([Values] bool nativeIntegers)
	{
		using var assembly = BuildAssembly();
		AddStringRead(assembly.MainModule, pinned: true);
		byte[] original = Serialize(assembly);
		string code = Decompile(original, nativeIntegers);

		Assert.That(code, Does.Contain("fixed (char*"));
		Assert.That(code, Does.Not.Contain("Unsafe.As<"));
		byte[] recompiled = Compile(code);
		AssertStringReads(original, recompiled);
	}

	static void AssertStringReads(byte[] original, byte[] recompiled)
	{
		// Interned strings remain rooted; neither fixture allocates after converting the reference.
		foreach (string text in new string[] { null, "", "abc" })
		{
			object expected = Invoke(original, "Read", text).Value;
			object actual = Invoke(recompiled, "Read", text).Value;
			Assert.That(actual, Is.EqualTo(expected));
			Assert.That(actual, Is.EqualTo(string.IsNullOrEmpty(text) ? 0 : 'a'));
		}
	}

	static bool IsZero(object value)
	{
		return value switch {
			IntPtr pointer => pointer == IntPtr.Zero,
			UIntPtr pointer => pointer == UIntPtr.Zero,
			long number => number == 0,
			ulong number => number == 0,
			_ => throw new InvalidOperationException("Unexpected reference conversion result.")
		};
	}

	static (object Value, int Counter) Invoke(byte[] image, string method, object argument)
	{
		var context = new AssemblyLoadContext("ReferenceConversion", isCollectible: true);
		try
		{
			using var stream = new MemoryStream(image);
			var assembly = context.LoadFromStream(stream);
			var type = assembly.GetType("ConversionFixture");
			object value = type.GetMethod(method).Invoke(null, new[] { argument });
			return (value, (int)type.GetField("Counter").GetValue(null));
		}
		finally
		{
			context.Unload();
		}
	}

	static byte[] Serialize(Cecil.AssemblyDefinition assembly)
	{
		using var stream = new MemoryStream();
		assembly.Write(stream);
		return stream.ToArray();
	}

	static string Decompile(byte[] image, bool nativeIntegers)
	{
		using var stream = new MemoryStream(image);
		using var peFile = new PEFile("ReferenceConversion.dll", stream);
		var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
		var decompiler = new CSharpDecompiler(peFile, resolver, new DecompilerSettings { NativeIntegers = nativeIntegers });
		return decompiler.DecompileTypeAsString(new FullTypeName("ConversionFixture"));
	}

	static byte[] Compile(string code)
	{
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
			.Select(path => MetadataReference.CreateFromFile(path));
		var compilation = CSharpCompilation.Create("ReferenceRecompiled", new[] { CSharpSyntaxTree.ParseText(code) }, references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
		using var stream = new MemoryStream();
		var result = compilation.Emit(stream);
		Assert.That(result.Success, Is.True, () => string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine + code);
		return stream.ToArray();
	}

	static Cecil.AssemblyDefinition BuildAssembly()
	{
		var assembly = Cecil.AssemblyDefinition.CreateAssembly(
			new Cecil.AssemblyNameDefinition("ReferenceConversion", new Version(1, 0)),
			"ReferenceConversion.dll", Cecil.ModuleKind.Dll);
		var module = assembly.MainModule;
		var core = (Cecil.AssemblyNameReference)module.TypeSystem.CoreLibrary;
		var runtime = typeof(object).Assembly.GetName();
		core.Name = runtime.Name;
		core.Version = runtime.Version;
		core.PublicKeyToken = runtime.GetPublicKeyToken();
		var type = new Cecil.TypeDefinition("", "ConversionFixture", Cecil.TypeAttributes.Public | Cecil.TypeAttributes.Class, module.TypeSystem.Object);
		module.Types.Add(type);
		type.Fields.Add(new Cecil.FieldDefinition("Counter", Cecil.FieldAttributes.Public | Cecil.FieldAttributes.Static, module.TypeSystem.Int32));
		var text = new Cecil.FieldDefinition("Text", Cecil.FieldAttributes.Public | Cecil.FieldAttributes.Static | Cecil.FieldAttributes.InitOnly, module.TypeSystem.String);
		type.Fields.Add(text);
		var initializer = new Cecil.MethodDefinition(".cctor",
			Cecil.MethodAttributes.Private | Cecil.MethodAttributes.Static | Cecil.MethodAttributes.SpecialName | Cecil.MethodAttributes.RTSpecialName,
			module.TypeSystem.Void);
		type.Methods.Add(initializer);
		var il = initializer.Body.GetILProcessor();
		il.Emit(OpCodes.Ldstr, "abc");
		il.Emit(OpCodes.Stsfld, text);
		il.Emit(OpCodes.Ret);
		return assembly;
	}

	static void AddConversion(Cecil.ModuleDefinition module, ReferenceInput input, Conversion conversion)
	{
		var type = module.GetType("ConversionFixture");
		Cecil.TypeReference returnType = conversion switch {
			Conversion.NativeSigned => module.TypeSystem.IntPtr,
			Conversion.NativeUnsigned => module.TypeSystem.UIntPtr,
			Conversion.Signed64 => module.TypeSystem.Int64,
			Conversion.Unsigned64 => module.TypeSystem.UInt64,
			_ => throw new ArgumentOutOfRangeException(nameof(conversion))
		};
		var method = new Cecil.MethodDefinition("Convert", Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, returnType);
		method.Parameters.Add(new Cecil.ParameterDefinition("input", Cecil.ParameterAttributes.None,
			input == ReferenceInput.ObjectParameter ? module.TypeSystem.Object : module.TypeSystem.String));
		method.Body.InitLocals = true;
		type.Methods.Add(method);
		var il = method.Body.GetILProcessor();
		switch (input)
		{
			case ReferenceInput.StringLocal:
				var local = new Cecil.Cil.VariableDefinition(module.TypeSystem.String);
				method.Body.Variables.Add(local);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Stloc, local);
				il.Emit(OpCodes.Ldloc, local);
				break;
			case ReferenceInput.ObjectParameter:
				il.Emit(OpCodes.Ldarg_0);
				break;
			case ReferenceInput.MethodReturn:
				var getter = new Cecil.MethodDefinition("Get", Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, module.TypeSystem.String);
				getter.Parameters.Add(new Cecil.ParameterDefinition("input", Cecil.ParameterAttributes.None, module.TypeSystem.String));
				type.Methods.Add(getter);
				var getIL = getter.Body.GetILProcessor();
				getIL.Emit(OpCodes.Ldsfld, type.Fields.Single(field => field.Name == "Counter"));
				getIL.Emit(OpCodes.Ldc_I4_1);
				getIL.Emit(OpCodes.Add);
				getIL.Emit(OpCodes.Stsfld, type.Fields.Single(field => field.Name == "Counter"));
				getIL.Emit(OpCodes.Ldarg_0);
				getIL.Emit(OpCodes.Ret);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Call, getter);
				break;
			case ReferenceInput.ReadonlyField:
				il.Emit(OpCodes.Ldsfld, type.Fields.Single(field => field.Name == "Text"));
				break;
			case ReferenceInput.NullLiteral:
				il.Emit(OpCodes.Ldnull);
				break;
		}
		il.Emit(conversion switch {
			Conversion.NativeSigned => OpCodes.Conv_I,
			Conversion.NativeUnsigned => OpCodes.Conv_U,
			Conversion.Signed64 => OpCodes.Conv_I8,
			Conversion.Unsigned64 => OpCodes.Conv_U8,
			_ => throw new ArgumentOutOfRangeException(nameof(conversion))
		});
		il.Emit(OpCodes.Ret);
	}

	static void AddStringRead(Cecil.ModuleDefinition module, bool pinned)
	{
		var type = module.GetType("ConversionFixture");
		var method = new Cecil.MethodDefinition("Read", Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, module.TypeSystem.Int32);
		method.Parameters.Add(new Cecil.ParameterDefinition("text", Cecil.ParameterAttributes.None, module.TypeSystem.String));
		method.Body.InitLocals = true;
		type.Methods.Add(method);
		var text = new Cecil.Cil.VariableDefinition(pinned ? new Cecil.PinnedType(module.TypeSystem.String) : module.TypeSystem.String);
		var pointer = new Cecil.Cil.VariableDefinition(new Cecil.PointerType(module.TypeSystem.Char));
		var result = new Cecil.Cil.VariableDefinition(module.TypeSystem.Int32);
		method.Body.Variables.Add(text);
		method.Body.Variables.Add(pointer);
		method.Body.Variables.Add(result);
		var il = method.Body.GetILProcessor();
		var read = il.Create(OpCodes.Ldloc, pointer);
		var nullResult = il.Create(OpCodes.Ldc_I4_0);
		var end = pinned ? il.Create(OpCodes.Ldnull) : il.Create(OpCodes.Ldloc, result);
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Stloc, text);
		il.Emit(OpCodes.Ldloc, text);
		il.Emit(OpCodes.Conv_U);
		il.Emit(OpCodes.Stloc, pointer);
		il.Emit(OpCodes.Ldloc, pointer);
		il.Emit(OpCodes.Brfalse, read);
		il.Emit(OpCodes.Ldloc, pointer);
		il.Emit(OpCodes.Call, module.ImportReference(typeof(RuntimeHelpers).GetProperty("OffsetToStringData").GetMethod));
		il.Emit(OpCodes.Conv_I);
		il.Emit(OpCodes.Add);
		il.Emit(OpCodes.Stloc, pointer);
		il.Append(read);
		il.Emit(OpCodes.Brfalse, nullResult);
		il.Emit(OpCodes.Ldloc, pointer);
		il.Emit(OpCodes.Ldind_U2);
		il.Emit(OpCodes.Stloc, result);
		il.Emit(OpCodes.Br, end);
		il.Append(nullResult);
		il.Emit(OpCodes.Stloc, result);
		il.Append(end);
		if (pinned)
		{
			il.Emit(OpCodes.Stloc, text);
			il.Emit(OpCodes.Ldloc, result);
		}
		il.Emit(OpCodes.Ret);
	}
}
