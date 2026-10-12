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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.Loader;
using System.Threading.Tasks;

using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using NUnit.Framework;

using Cecil = Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace ICSharpCode.Decompiler.Tests.ProjectDecompiler;

[TestFixture]
public sealed class AccessorOverrideProjectionTests
{
	const int Getter = 1;
	const int Setter = 2;
	const string AccessorAttribute = nameof(System.Diagnostics.DebuggerNonUserCodeAttribute);

	[Test]
	public void DeepNormalPropertyOverrideChainRetainsPropertySyntax()
	{
		using var assembly = CreateAssembly(Getter | Setter, Getter | Setter, baseHasProperty: true);
		var previous = assembly.MainModule.Types.Single(t => t.Name == "Derived");
		for (int level = 2; level <= 24; level++)
		{
			var type = new Cecil.TypeDefinition("AccessorFixture", "Level" + level, Cecil.TypeAttributes.Public, previous);
			assembly.MainModule.Types.Add(type);
			var property = new Cecil.PropertyDefinition("Title", Cecil.PropertyAttributes.None, assembly.MainModule.TypeSystem.Int32);
			type.Properties.Add(property);
			foreach (var accessor in previous.Methods.Where(m => !m.IsConstructor))
			{
				var method = new Cecil.MethodDefinition(accessor.Name, accessor.Attributes, accessor.ReturnType);
				foreach (var parameter in accessor.Parameters)
					method.Parameters.Add(new Cecil.ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
				if (method.Name == "get_Title")
				{
					Emit(method, OpCodes.Ldc_I4_0);
					property.GetMethod = method;
				}
				else
				{
					property.SetMethod = method;
				}
				Emit(method, OpCodes.Ret);
				type.Methods.Add(method);
			}
			previous = type;
		}
		var timer = Stopwatch.StartNew();
		CheckPropertySyntaxTraversal(assembly, previous, cyclic: false);
		Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
	}

	[Test]
	public void CyclicPropertyOverrideHierarchyTerminates()
	{
		using var assembly = CreateAssembly(Getter | Setter, Getter | Setter, baseHasProperty: true);
		var baseType = assembly.MainModule.Types.Single(t => t.Name == "Base");
		var derived = assembly.MainModule.Types.Single(t => t.Name == "Derived");
		baseType.BaseType = derived;
		foreach (var accessor in baseType.Methods.Where(m => !m.IsConstructor))
			accessor.IsNewSlot = false;
		CheckPropertySyntaxTraversal(assembly, derived, cyclic: true);
	}

	static void CheckPropertySyntaxTraversal(Cecil.AssemblyDefinition assembly, Cecil.TypeDefinition type, bool cyclic)
	{
		using var input = new MemoryStream();
		assembly.Write(input);
		input.Position = 0;
		using var module = new PEFile("AccessorOverride.dll", input);
		var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
		var decompiler = new CSharpDecompiler(module, resolver, new DecompilerSettings());
		var property = decompiler.TypeSystem.MainModule.TypeDefinitions.Single(t => t.Name == type.Name).Properties.Single();
		Assert.That(MetadataTokens.GetToken(property.MetadataToken), Is.EqualTo(type.Properties.Single().MetadataToken.ToInt32()));
		Assert.That(property.Getter.IsOverride, Is.True);
		Assert.That(property.Setter.IsOverride, Is.True);
		var baseGetter = TypeSystemAstBuilder.FindBaseAccessorMethod(property.Getter);
		Assert.That(baseGetter, Is.Not.Null);
		Assert.That(baseGetter.DeclaringType.Name, Is.EqualTo(type.BaseType.Name));
		if (cyclic)
		{
			var cycleGetter = TypeSystemAstBuilder.FindBaseAccessorMethod(baseGetter);
			Assert.That(cycleGetter, Is.SameAs(property.Getter));
			Assert.DoesNotThrow(() => TypeSystemAstBuilder.CanUsePropertySyntax(property));
		}
		else
		{
			Assert.That(TypeSystemAstBuilder.CanUsePropertySyntax(property), Is.True);
		}
	}

	[TestCase(Getter, Getter, false)]
	[TestCase(Getter, Getter, true)]
	[TestCase(Setter, Setter, false)]
	[TestCase(Setter, Setter, true)]
	[TestCase(Getter | Setter, Getter | Setter, false)]
	[TestCase(Getter | Setter, Getter | Setter, true)]
	[TestCase(Getter | Setter, Getter, false)]
	[TestCase(Getter | Setter, Getter, true)]
	[TestCase(Getter | Setter, Setter, false)]
	[TestCase(Getter | Setter, Setter, true)]
	public void PropertyOverridingRegularMethodsProjectsAsAccessorMethods(int derivedAccessors, int baseAccessors, bool tokenize)
	{
		CheckProjection(derivedAccessors, baseAccessors, baseHasProperty: false, tokenize);
	}

	[TestCase(Getter, false)]
	[TestCase(Getter, true)]
	[TestCase(Setter, false)]
	[TestCase(Setter, true)]
	[TestCase(Getter | Setter, false)]
	[TestCase(Getter | Setter, true)]
	public void NormalPropertyOverrideRetainsPropertyProjection(int accessors, bool tokenize)
	{
		CheckProjection(accessors, accessors, baseHasProperty: true, tokenize);
	}

	[TestCase(false, false)]
	[TestCase(false, true)]
	[TestCase(true, false)]
	[TestCase(true, true)]
	public void ProjectionFollowsOverriddenSlotsRatherThanAccessorNames(bool descendant, bool tokenize)
	{
		using var assembly = CreateAssembly(Getter | Setter, Getter | Setter, baseHasProperty: false);
		var module = assembly.MainModule;
		var derived = module.Types.Single(t => t.Name == "Derived");
		var projected = derived;
		if (descendant)
		{
			projected = new Cecil.TypeDefinition("AccessorFixture", "Child", Cecil.TypeAttributes.Public, derived);
			module.Types.Add(projected);
			var property = new Cecil.PropertyDefinition("propertyTitle", Cecil.PropertyAttributes.None, module.TypeSystem.Int32);
			projected.Properties.Add(property);
			var callers = module.Types.Single(t => t.Name == "CallSites");
			foreach (var accessor in derived.Methods.Where(m => !m.IsConstructor))
			{
				var method = new Cecil.MethodDefinition(accessor.Name, accessor.Attributes, accessor.ReturnType);
				foreach (var parameter in accessor.Parameters)
					method.Parameters.Add(new Cecil.ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
				if (accessor.Name == "get_Title")
				{
					Emit(method, OpCodes.Ldc_I4_3);
					property.GetMethod = method;
				}
				else
				{
					property.SetMethod = method;
				}
				Emit(method, OpCodes.Ret);
				projected.Methods.Add(method);
				var caller = new Cecil.MethodDefinition("CallChild" + method.Name, Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, method.ReturnType);
				caller.Parameters.Add(new Cecil.ParameterDefinition(projected));
				Emit(caller, OpCodes.Ldarg_0);
				if (method.Name == "set_Title")
					Emit(caller, OpCodes.Ldc_I4_1);
				Call(caller, method);
				Emit(caller, OpCodes.Ret);
				callers.Methods.Add(caller);
			}
		}
		else
		{
			derived.Properties.Single().Name = "Title";
			foreach (var method in derived.Methods.Where(m => !m.IsConstructor))
				method.IsNewSlot = true;
		}
		using var input = new MemoryStream();
		assembly.Write(input);
		input.Position = 0;
		using var peFile = new PEFile("AccessorOverride.dll", input);
		var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
		var decompiler = new CSharpDecompiler(peFile, resolver, new DecompilerSettings { TokenizeNames = tokenize });
		var types = decompiler.TypeSystem.MainModule.TypeDefinitions.Where(t => t.Namespace == "AccessorFixture").ToArray();
		var projectedType = types.Single(t => t.Name == projected.Name);
		var tree = decompiler.DecompileTypes(types.Select(t => (System.Reflection.Metadata.TypeDefinitionHandle)t.MetadataToken));
		if (!tokenize)
			AssertCompileBack(tree.ToString(), assembly, baseHasProperty: !descendant);
		var declaration = tree.Descendants.OfType<TypeDeclaration>().Single(t => t.GetSymbol() == projectedType);
		Assert.That(declaration.Members.OfType<PropertyDeclaration>().Count(), Is.EqualTo(descendant ? 0 : 1), declaration.ToString());
		if (descendant)
		{
			foreach (var accessor in new[] { projectedType.Properties.Single().Getter, projectedType.Properties.Single().Setter })
			{
				var method = declaration.Members.OfType<MethodDeclaration>().Single(m => m.GetSymbol() == accessor);
				Assert.That(method.Name, Is.EqualTo(Name(accessor, "method", tokenize)));
				Assert.That(method.Modifiers.HasFlag(Modifiers.Override), Is.True);
				var caller = tree.Descendants.OfType<MethodDeclaration>().Single(m => m.GetSymbol()?.Name == "CallChild" + accessor.Name);
				var invocation = caller.Body.Descendants.OfType<InvocationExpression>().Single();
				Assert.That(((MemberResolveResult)invocation.GetResolveResult()).Member.MemberDefinition, Is.SameAs(accessor));
				Assert.That(((MemberReferenceExpression)invocation.Target).MemberName, Is.EqualTo(method.Name));
			}
		}
		else
		{
			var property = declaration.Members.OfType<PropertyDeclaration>().Single();
			Assert.That(property.GetSymbol(), Is.SameAs(projectedType.Properties.Single()));
			Assert.That(property.Name, Is.EqualTo(Name(projectedType.Properties.Single(), "prop", tokenize)));
			Assert.That(property.Modifiers.HasFlag(Modifiers.Virtual), Is.True);
			Assert.That(property.Modifiers.HasFlag(Modifiers.Override), Is.False);
		}
	}

	static void CheckProjection(int derivedAccessors, int baseAccessors, bool baseHasProperty, bool tokenize)
	{
		using var assembly = CreateAssembly(derivedAccessors, baseAccessors, baseHasProperty);
		using var input = new MemoryStream();
		assembly.Write(input);
		input.Position = 0;
		using var module = new PEFile("AccessorOverride.dll", input);
		var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
		var decompiler = new CSharpDecompiler(module, resolver, new DecompilerSettings { TokenizeNames = tokenize });
		var types = decompiler.TypeSystem.MainModule.TypeDefinitions.Where(t => t.Namespace == "AccessorFixture").ToArray();
		var baseType = types.Single(t => t.Name == "Base");
		var derivedType = types.Single(t => t.Name == "Derived");
		Assert.That(baseType.Properties.Count(), Is.EqualTo(baseHasProperty ? 1 : 0));
		var property = derivedType.Properties.Single();
		Assert.That(property.Name, Is.EqualTo(baseHasProperty ? "Title" : "propertyTitle"));
		var accessors = new[] { property.Getter, property.Setter }.Where(m => m != null).ToArray();
		Assert.That(accessors.Select(m => MetadataTokens.GetToken(m.MetadataToken)), Is.EquivalentTo(
			assembly.MainModule.Types.Single(t => t.Name == "Derived").Methods.Where(m => !m.IsConstructor).Select(m => m.MetadataToken.ToInt32())));
		var tree = decompiler.DecompileTypes(types.Select(t => (System.Reflection.Metadata.TypeDefinitionHandle)t.MetadataToken));
		string code = tree.ToString();

		if (!tokenize)
			AssertCompileBack(code, assembly, baseHasProperty);

		var declaration = tree.Descendants.OfType<TypeDeclaration>().Single(t => t.GetSymbol() == derivedType);
		if (baseHasProperty)
		{
			var propertyDeclaration = declaration.Members.OfType<PropertyDeclaration>().Single();
			Assert.That(propertyDeclaration.GetSymbol(), Is.SameAs(property));
			Assert.That(propertyDeclaration.Name, Is.EqualTo(Name(property, "prop", tokenize)));
			Assert.That(propertyDeclaration.Modifiers.HasFlag(Modifiers.Override), Is.True);
			Assert.That(declaration.Members.OfType<MethodDeclaration>(), Is.Empty);
		}
		else
		{
			Assert.That(declaration.Members.OfType<PropertyDeclaration>(), Is.Empty, code);
			foreach (var accessor in accessors)
			{
				var method = declaration.Members.OfType<MethodDeclaration>().Single(m => m.GetSymbol() == accessor);
				Assert.That(method.GetSymbol(), Is.SameAs(accessor));
				Assert.That(method.Name, Is.EqualTo(Name(accessor, "method", tokenize)));
				bool overrides = baseType.Methods.Any(m => m.Name == accessor.Name);
				Assert.That(method.Modifiers.HasFlag(Modifiers.Override), Is.EqualTo(overrides));
				Assert.That(method.Modifiers.HasFlag(Modifiers.Virtual), Is.EqualTo(!overrides));
				Assert.That(method.Attributes.SelectMany(a => a.Attributes)
					.Any(a => a.Type.GetSymbol()?.Name == AccessorAttribute), Is.True, method.ToString());
			}
		}

		foreach (var caller in tree.Descendants.OfType<MethodDeclaration>().Where(m => m.GetSymbol()?.Name.StartsWith("Call", StringComparison.Ordinal) == true))
		{
			var callerSymbol = (IMethod)caller.GetSymbol();
			bool callsBase = callerSymbol.Name.StartsWith("CallBase", StringComparison.Ordinal);
			bool callsGetter = callerSymbol.Name.EndsWith("Getter", StringComparison.Ordinal);
			var owner = callsBase ? baseType : derivedType;
			var ownerProperty = owner.Properties.SingleOrDefault();
			var accessor = ownerProperty != null ? (callsGetter ? ownerProperty.Getter : ownerProperty.Setter)
				: owner.Methods.Single(m => m.Name == (callsGetter ? "get_Title" : "set_Title"));
			var references = caller.Body.Descendants.OfType<MemberReferenceExpression>().ToArray();
			Assert.That(references, Has.Length.EqualTo(1), caller.ToString());
			var reference = references.Single();
			var resolveResult = (reference.Parent is InvocationExpression invocation ? invocation : (Expression)reference).GetResolveResult();
			Assert.That(resolveResult, Is.InstanceOf<MemberResolveResult>());
			var member = ((MemberResolveResult)resolveResult).Member;
			if (baseHasProperty)
			{
				Assert.That(member, Is.SameAs(owner.Properties.Single()));
				Assert.That(reference.MemberName, Is.EqualTo(Name(member, "prop", tokenize)));
				Assert.That(caller.Body.Descendants.OfType<InvocationExpression>(), Is.Empty, caller.ToString());
			}
			else
			{
				Assert.That(member.MemberDefinition, Is.SameAs(accessor));
				Assert.That(member.MetadataToken, Is.EqualTo(accessor.MetadataToken));
				Assert.That(reference.MemberName, Is.EqualTo(Name(accessor, "method", tokenize)));
				Assert.That(reference.Parent, Is.TypeOf<InvocationExpression>(), caller.ToString());
			}
		}
		Assert.That(tree.Descendants.OfType<MethodDeclaration>().Count(m => m.GetSymbol()?.Name.StartsWith("Call", StringComparison.Ordinal) == true),
			Is.EqualTo(accessors.Length + assembly.MainModule.Types.Single(t => t.Name == "Base").Methods.Count(m => !m.IsConstructor)));
		foreach (var probe in tree.Descendants.OfType<MethodDeclaration>().Where(m => m.GetSymbol()?.Name.StartsWith("Probe", StringComparison.Ordinal) == true))
		{
			var propertyReferences = probe.Body.Descendants
				.Where(n => n.GetResolveResult() is MemberResolveResult { Member: IProperty p } && p.DeclaringTypeDefinition == derivedType).ToArray();
			Assert.That(propertyReferences.Length, baseHasProperty ? Is.GreaterThan(0) : Is.EqualTo(0), probe.ToString());
			if (!baseHasProperty)
			{
				Assert.That(probe.Body.Descendants.OfType<RecursivePatternExpression>().SelectMany(p => p.SubPatterns).OfType<NamedArgumentExpression>(),
					Is.Empty, probe.ToString());
				Assert.That(probe.Body.Descendants.OfType<NamedExpression>(), Is.Empty, probe.ToString());
			}
		}
	}

	[TestCase(Getter, false, false)]
	[TestCase(Getter, false, true)]
	[TestCase(Setter, false, false)]
	[TestCase(Setter, false, true)]
	[TestCase(Getter | Setter, false, false)]
	[TestCase(Getter | Setter, false, true)]
	[TestCase(Getter, true, false)]
	[TestCase(Getter, true, true)]
	[TestCase(Setter, true, false)]
	[TestCase(Setter, true, true)]
	[TestCase(Getter | Setter, true, false)]
	[TestCase(Getter | Setter, true, true)]
	public void RegularOverridesProjectBothEndsOfPropertyFamily(int accessors, bool descendant, bool tokenize)
	{
		CheckPropertyFamily(accessors, descendant, newSlot: false, tokenize);
	}

	[TestCase(Getter, false)]
	[TestCase(Getter, true)]
	[TestCase(Setter, false)]
	[TestCase(Setter, true)]
	[TestCase(Getter | Setter, false)]
	[TestCase(Getter | Setter, true)]
	public void NewSlotRegularMethodsDoNotProjectBaseProperty(int accessors, bool tokenize)
	{
		CheckPropertyFamily(accessors, descendant: false, newSlot: true, tokenize);
	}

	[TestCase(false)]
	[TestCase(true)]
	public void GetterOnlyOverrideProjectsSetterAndNormalPropertySibling(bool tokenize)
	{
		CheckPropertyFamily(Getter, descendant: false, newSlot: false, tokenize, sibling: true);
	}

	[TestCase(false, false, false, false)]
	[TestCase(true, false, false, false)]
	[TestCase(false, true, false, false)]
	[TestCase(true, true, false, false)]
	[TestCase(false, false, false, true)]
	[TestCase(true, false, false, true)]
	[TestCase(false, false, true, true)]
	[TestCase(true, false, true, true)]
	public void ExternalPropertyGetterOverrideRetainsVirtualDispatch(bool tokenize, bool sealedOverride, bool abstractOverride, bool descendant)
	{
		CheckExternalPropertyBase(Getter, tokenize, sealedOverride: sealedOverride, abstractOverride: abstractOverride, descendant: descendant);
	}

	[TestCase(false)]
	[TestCase(true)]
	public void ExternalPropertyGetterCallUsesItsSlotWhenReceiverHidesProperty(bool tokenize)
	{
		CheckExternalPropertyBase(Getter, tokenize, shadowedReceiver: true);
	}

	[TestCase(false)]
	[TestCase(true)]
	public void OrphanSpecialNameGetterOverrideRetainsVirtualDispatch(bool tokenize)
	{
		CheckExternalPropertyBase(Getter, tokenize, specialName: true);
	}

	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(true, true)]
	public void ProjectedGetterPreservesNonVirtualBaseCall(bool tokenize, bool descendant)
	{
		CheckExternalPropertyBase(Getter, tokenize, descendant: descendant, callBase: true);
	}

	[TestCase(false)]
	[TestCase(true)]
	public void ProjectedGetterNameCollisionKeepsRegularMethodFallback(bool tokenize)
	{
		CheckExternalPropertyBase(Getter, tokenize, memberNameConflict: true);
	}

	[TestCase(Getter, false, true, false)]
	[TestCase(Getter, true, true, false)]
	[TestCase(Setter, false, false, false)]
	[TestCase(Setter, true, false, false)]
	[TestCase(Getter, false, false, true)]
	[TestCase(Getter, true, false, true)]
	public void UnsupportedExternalPropertyOverridesKeepRegularMethodFallback(int accessors, bool tokenize, bool newSlot, bool indexed)
	{
		CheckExternalPropertyBase(accessors, tokenize, newSlot: newSlot, indexed: indexed);
	}

	static void CheckExternalPropertyBase(int accessors, bool tokenize, bool newSlot = false, bool indexed = false,
		bool sealedOverride = false, bool abstractOverride = false, bool descendant = false, bool shadowedReceiver = false,
		bool specialName = false, bool callBase = false, bool memberNameConflict = false)
	{
		using var external = CreateAssembly(accessors, accessors, baseHasProperty: true);
		external.Name.Name = "ExternalAccessorBase";
		external.MainModule.Name = "ExternalAccessorBase.dll";
		foreach (var type in external.MainModule.Types.Where(t => t.Name != "Base" && t.Name != "<Module>").ToArray())
			external.MainModule.Types.Remove(type);
		var externalBase = external.MainModule.Types.Single(t => t.Name == "Base");
		if (indexed)
		{
			var defaultMember = new Cecil.CustomAttribute(external.MainModule.ImportReference(
				typeof(System.Reflection.DefaultMemberAttribute).GetConstructor(new[] { typeof(string) })));
			defaultMember.ConstructorArguments.Add(new Cecil.CustomAttributeArgument(external.MainModule.TypeSystem.String, "Title"));
			externalBase.CustomAttributes.Add(defaultMember);
			externalBase.Properties.Single().Parameters.Add(new Cecil.ParameterDefinition(external.MainModule.TypeSystem.Int32));
			externalBase.Properties.Single().GetMethod.Parameters.Add(new Cecil.ParameterDefinition("index", Cecil.ParameterAttributes.None, external.MainModule.TypeSystem.Int32));
		}
		using var assembly = CreateAssembly(accessors, accessors, baseHasProperty: true);
		var module = assembly.MainModule;
		var baseType = module.Types.Single(t => t.Name == "Base");
		var derived = module.Types.Single(t => t.Name == "Derived");
		derived.BaseType = module.ImportReference(externalBase);
		derived.Properties.Clear();
		if (memberNameConflict)
			derived.Fields.Add(new Cecil.FieldDefinition("Title", Cecil.FieldAttributes.Public, module.TypeSystem.Int32));
		var regularMethod = derived.Methods.Single(m => !m.IsConstructor);
		regularMethod.IsSpecialName = specialName;
		regularMethod.SemanticsAttributes = Cecil.MethodSemanticsAttributes.None;
		regularMethod.IsNewSlot = newSlot;
		regularMethod.IsFinal = sealedOverride;
		regularMethod.IsAbstract = abstractOverride;
		if (abstractOverride)
		{
			derived.IsAbstract = true;
			regularMethod.Body = null;
		}
		if (indexed)
			regularMethod.Parameters.Add(new Cecil.ParameterDefinition("index", Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
		if (callBase)
		{
			regularMethod.Body.Instructions.Clear();
			Emit(regularMethod, OpCodes.Ldarg_0);
			regularMethod.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Call, baseType.Methods.Single(m => m.Name == "get_Title")));
			Emit(regularMethod, OpCodes.Ldc_I4_1);
			Emit(regularMethod, OpCodes.Add);
			Emit(regularMethod, OpCodes.Ret);
		}
		foreach (var type in module.Types)
		{
			foreach (var method in type.Methods)
			{
				foreach (var parameter in method.Parameters.Where(p => p.ParameterType == baseType))
					parameter.ParameterType = derived.BaseType;
				if (!method.HasBody)
					continue;
				foreach (var instruction in method.Body.Instructions.ToArray())
				{
					if (instruction.Operand is Cecil.MethodReference target && target.DeclaringType == baseType)
						instruction.Operand = module.ImportReference(externalBase.Methods.Single(m => m.Name == target.Name));
					if (indexed && instruction.Operand is Cecil.MethodReference getter && getter.Name == "get_Title")
						method.Body.Instructions.Insert(method.Body.Instructions.IndexOf(instruction), Cecil.Cil.Instruction.Create(OpCodes.Ldc_I4_0));
				}
			}
		}
		module.Types.Remove(baseType);
		if (descendant || shadowedReceiver)
		{
			var leaf = new Cecil.TypeDefinition("AccessorFixture", "Leaf", Cecil.TypeAttributes.Public, derived);
			module.Types.Add(leaf);
			var constructor = new Cecil.MethodDefinition(".ctor",
				Cecil.MethodAttributes.Public | Cecil.MethodAttributes.SpecialName | Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
			Emit(constructor, OpCodes.Ldarg_0);
			constructor.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Call, derived.Methods.Single(m => m.IsConstructor)));
			Emit(constructor, OpCodes.Ret);
			leaf.Methods.Add(constructor);
			if (descendant)
			{
				var getter = new Cecil.MethodDefinition("get_Title", regularMethod.Attributes, module.TypeSystem.Int32);
				getter.IsAbstract = false;
				if (callBase)
				{
					Emit(getter, OpCodes.Ldarg_0);
					getter.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Call, regularMethod));
					Emit(getter, OpCodes.Ldc_I4_1);
					Emit(getter, OpCodes.Add);
				}
				else
				{
					Emit(getter, OpCodes.Ldc_I4_3);
				}
				Emit(getter, OpCodes.Ret);
				leaf.Methods.Add(getter);
			}
			else
			{
				leaf.Fields.Add(new Cecil.FieldDefinition("Title", Cecil.FieldAttributes.Public, module.TypeSystem.Int32));
				var caller = new Cecil.MethodDefinition("CallLeafGetter", Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, module.TypeSystem.Int32);
				caller.Parameters.Add(new Cecil.ParameterDefinition("instance", Cecil.ParameterAttributes.None, leaf));
				Emit(caller, OpCodes.Ldarg_0);
				Call(caller, regularMethod);
				Emit(caller, OpCodes.Ret);
				module.Types.Single(t => t.Name == "CallSites").Methods.Add(caller);
			}
		}
		var directory = Directory.CreateTempSubdirectory("AccessorExternal-");
		try
		{
			string externalPath = Path.Combine(directory.FullName, external.MainModule.Name);
			external.Write(externalPath);
			using var input = new MemoryStream();
			assembly.Write(input);
			input.Position = 0;
			using var peFile = new PEFile("AccessorOverride.dll", input);
			var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
			resolver.AddSearchDirectory(directory.FullName);
			resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
			var decompiler = new CSharpDecompiler(peFile, resolver, new DecompilerSettings { TokenizeNames = tokenize });
			var types = decompiler.TypeSystem.MainModule.TypeDefinitions.Where(t => t.Namespace == "AccessorFixture").ToArray();
			var derivedType = types.Single(t => t.Name == "Derived");
			var baseProperty = derivedType.DirectBaseTypes.Single().GetProperties().Single();
			using var externalFile = baseProperty.ParentModule.MetadataFile;
			Assert.That(baseProperty.ParentModule, Is.Not.SameAs(decompiler.TypeSystem.MainModule));
			Assert.That(TypeSystemAstBuilder.CanUsePropertySyntax(baseProperty), Is.True);
			var tree = decompiler.DecompileTypes(types.Select(t => (System.Reflection.Metadata.TypeDefinitionHandle)t.MetadataToken));
			bool projected = accessors == Getter && !newSlot && !indexed && !memberNameConflict;
			string[] callerNames = shadowedReceiver ? new[] { "CallBaseGetter", "CallDerivedGetter", "CallLeafGetter" }
				: new[] { "CallBaseGetter", "CallDerivedGetter" };
			foreach (var type in types.Where(t => t.Name == "Derived" || descendant && t.Name == "Leaf"))
			{
				var original = module.Types.Single(t => t.Name == type.Name).Methods.Single(m => !m.IsConstructor);
				var method = type.GetMethods(m => m.Name == original.Name, GetMemberOptions.IgnoreInheritedMembers).Single();
				Assert.That(type.Properties, Is.Empty, "Projection must not invent a PropertyDef.");
				Assert.That(method.IsAccessor, Is.False, "Projection must not mutate the type system.");
				Assert.That(MetadataTokens.GetToken(method.MetadataToken), Is.EqualTo(original.MetadataToken.ToInt32()));
				var declaration = tree.Descendants.OfType<TypeDeclaration>().Single(t => t.GetSymbol() == type);
				if (projected)
				{
					Assert.That(declaration.Members.OfType<PropertyDeclaration>().Count(), Is.EqualTo(1), tree.ToString());
					var property = declaration.Members.OfType<PropertyDeclaration>().Single();
					Assert.That(property.Name, Is.EqualTo("Title"));
					Assert.That(property.Modifiers.HasFlag(Modifiers.Override), Is.True);
					Assert.That(property.Modifiers.HasFlag(Modifiers.Virtual), Is.False);
					Assert.That(property.Modifiers.HasFlag(Modifiers.Sealed), Is.EqualTo(original.IsFinal));
					Assert.That(property.Modifiers.HasFlag(Modifiers.Abstract), Is.EqualTo(original.IsAbstract));
					AstNode accessor = property.Getter ?? (AstNode)property;
					Assert.That(accessor.GetSymbol(), Is.SameAs(method));
					if (callBase)
					{
						var reference = property.Descendants.OfType<MemberReferenceExpression>().Single();
						Assert.That(reference.MemberName, Is.EqualTo("Title"));
						Assert.That(reference.Target, Is.TypeOf<BaseReferenceExpression>(), property.ToString());
						Assert.That(property.Descendants.OfType<InvocationExpression>(), Is.Empty, property.ToString());
					}
					Assert.That(property.Setter, Is.Null);
					var attributes = property.Getter?.Attributes ?? property.Attributes;
					Assert.That(attributes.SelectMany(a => a.Attributes)
						.Any(a => a.Type.GetSymbol()?.Name == AccessorAttribute),
						Is.EqualTo(original.CustomAttributes.Any(a => a.AttributeType.Name == AccessorAttribute)));
					var marker = attributes.SelectMany(a => a.Attributes)
						.SingleOrDefault(a => a.Type is SimpleType { Identifier: "DecompiledName" });
					Assert.That(marker != null, Is.EqualTo(tokenize));
					if (tokenize)
					{
						Assert.That(marker.Arguments.OfType<PrimitiveExpression>().Single().Value, Is.EqualTo(original.Name));
						var token = marker.Arguments.OfType<NamedExpression>().Single(a => a.Name == "MetadataToken");
						Assert.That(((PrimitiveExpression)token.Expression).Value, Is.EqualTo(original.MetadataToken.ToInt32()));
					}
					Assert.That(declaration.Members.OfType<MethodDeclaration>(), Is.Empty);
				}
				else
				{
					Assert.That(declaration.Members.OfType<PropertyDeclaration>(), Is.Empty, tree.ToString());
					var node = declaration.Members.OfType<MethodDeclaration>().Single();
					Assert.That(node.GetSymbol(), Is.SameAs(method));
					Assert.That(node.Modifiers.HasFlag(Modifiers.Override), Is.False);
					Assert.That(node.Modifiers.HasFlag(Modifiers.Virtual), Is.True);
					Assert.That(node.Name, Is.EqualTo(Name(method, "method", tokenize)));
				}
			}
			if (accessors == Getter && !indexed)
			{
				foreach (string name in callerNames)
				{
					var caller = tree.Descendants.OfType<MethodDeclaration>().Single(m => m.GetSymbol()?.Name == name);
					var reference = caller.Body.Descendants.OfType<MemberReferenceExpression>().Single();
					bool propertyCall = name == "CallBaseGetter" || projected;
					Assert.That(reference.Parent is InvocationExpression, Is.EqualTo(!propertyCall), caller.ToString());
					Assert.That(reference.MemberName, Is.EqualTo(propertyCall ? "Title" : Name(derivedType.Methods.Single(m => m.Name == "get_Title"), "method", tokenize)));
					if (name == "CallBaseGetter")
						Assert.That(((MemberResolveResult)reference.GetResolveResult()).Member, Is.SameAs(baseProperty));
					else if (projected)
						Assert.That(((MemberResolveResult)reference.GetResolveResult()).Member.MemberDefinition,
							Is.SameAs(derivedType.Methods.Single(m => m.Name == "get_Title")));
				}
			}
			if (projected || memberNameConflict)
			{
				var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
					.Select(path => MetadataReference.CreateFromFile(path)).Append(MetadataReference.CreateFromFile(externalPath));
				var compilation = CSharpCompilation.Create("AccessorExternalRecompiled", new[] {
					CSharpSyntaxTree.ParseText(tree.ToString()),
					CSharpSyntaxTree.ParseText("sealed class DecompiledNameAttribute : System.Attribute { public DecompiledNameAttribute(string name) {} public int MetadataToken { get; set; } }")
				}, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
				using var output = new MemoryStream();
				var result = compilation.Emit(output);
				Assert.That(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty, tree.ToString());
				Assert.That(result.Success, Is.True);
				if (projected)
				{
					output.Position = 0;
					using var rebuilt = Cecil.AssemblyDefinition.ReadAssembly(output);
					foreach (var type in types.Where(t => t.Name == "Derived" || descendant && t.Name == "Leaf"))
					{
						var declaration = tree.Descendants.OfType<TypeDeclaration>().Single(t => t.GetSymbol() == type);
						var getter = rebuilt.MainModule.Types.Single(t => t.Name == declaration.Name).Properties.Single().GetMethod;
						var original = module.Types.Single(t => t.Name == type.Name).Methods.Single(m => m.Name == "get_Title");
						Assert.That(getter.IsVirtual, Is.True);
						Assert.That(getter.IsNewSlot, Is.False);
						Assert.That(getter.IsFinal, Is.EqualTo(original.IsFinal));
						Assert.That(getter.IsAbstract, Is.EqualTo(original.IsAbstract));
					}
					var context = new AssemblyLoadContext("AccessorExternal", isCollectible: true);
					try
					{
						var externalAssembly = context.LoadFromAssemblyPath(externalPath);
						input.Position = 0;
						var originalAssembly = context.LoadFromStream(input);
						var originalInstance = Activator.CreateInstance(originalAssembly.GetType("AccessorFixture." + (descendant || shadowedReceiver ? "Leaf" : "Derived")));
						var externalProperty = externalAssembly.GetType("AccessorFixture.Base").GetProperty("Title");
						int expected = descendant ? 3 : 2;
						Assert.That(externalProperty.GetValue(originalInstance), Is.EqualTo(expected));
						output.Position = 0;
						var rebuiltAssembly = context.LoadFromStream(output);
						var instanceType = types.Single(t => t.Name == (descendant || shadowedReceiver ? "Leaf" : "Derived"));
						var instanceDeclaration = tree.Descendants.OfType<TypeDeclaration>().Single(t => t.GetSymbol() == instanceType);
						var instance = Activator.CreateInstance(rebuiltAssembly.GetType("AccessorFixture." + instanceDeclaration.Name));
						Assert.That(externalProperty.GetValue(instance), Is.EqualTo(expected), "External virtual getter dispatch must survive recompilation.");
						foreach (string name in callerNames)
						{
							var caller = tree.Descendants.OfType<MethodDeclaration>().Single(m => m.GetSymbol()?.Name == name);
							var callers = rebuiltAssembly.GetType("AccessorFixture." + ((TypeDeclaration)caller.Parent).Name);
							Assert.That(callers.GetMethod(caller.Name).Invoke(null, new[] { instance }), Is.EqualTo(expected));
						}
					}
					finally
					{
						context.Unload();
					}
				}
			}
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	static void CheckPropertyFamily(int accessors, bool descendant, bool newSlot, bool tokenize, bool sibling = false)
	{
		using var assembly = CreateAssembly(accessors, sibling ? Getter | Setter : accessors, baseHasProperty: true);
		var module = assembly.MainModule;
		var baseType = module.Types.Single(t => t.Name == "Base");
		var derived = module.Types.Single(t => t.Name == "Derived");
		derived.Properties.Clear();
		foreach (var method in derived.Methods.Where(m => !m.IsConstructor))
		{
			method.IsSpecialName = false;
			method.SemanticsAttributes = Cecil.MethodSemanticsAttributes.None;
			method.IsNewSlot = newSlot;
			method.IsFinal = !descendant && !newSlot;
		}
		if (descendant)
			AddType("Leaf", derived, hasProperty: true, final: true);
		if (sibling)
			AddType("Sibling", baseType, hasProperty: true, final: false);
		AddType("UnrelatedProperty", module.TypeSystem.Object, hasProperty: true, final: false);
		AddType("UnrelatedMethod", module.TypeSystem.Object, hasProperty: false, final: false);
		using var input = new MemoryStream();
		assembly.Write(input);
		input.Position = 0;
		using var peFile = new PEFile("AccessorOverride.dll", input);
		var resolver = new UniversalAssemblyResolver(null, false, $".NETCoreApp,Version=v{Environment.Version.Major}.0");
		resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
		var decompiler = new CSharpDecompiler(peFile, resolver, new DecompilerSettings { TokenizeNames = tokenize });
		var types = decompiler.TypeSystem.MainModule.TypeDefinitions.Where(t => t.Namespace == "AccessorFixture").ToArray();
		if (sibling)
		{
			var properties = types.SelectMany(t => t.Properties).ToArray();
			Parallel.For(0, 64, i => {
				var property = properties[i % properties.Length];
				Assert.That(TypeSystemAstBuilder.CanUsePropertySyntax(property), Is.EqualTo(property.DeclaringTypeDefinition.Name == "UnrelatedProperty"));
			});
		}
		var tree = decompiler.DecompileTypes(types.Select(t => (System.Reflection.Metadata.TypeDefinitionHandle)t.MetadataToken));
		string code = tree.ToString();
		foreach (var type in types.Where(t => t.Name != "CallSites"))
		{
			var original = module.Types.Single(t => t.Name == type.Name);
			bool propertySyntax = type.Name == "UnrelatedProperty" || type.Name == "Base" && newSlot;
			var declaration = tree.Descendants.OfType<TypeDeclaration>().Single(t => t.GetSymbol() == type);
			Assert.That(type.Properties.Select(p => MetadataTokens.GetToken(p.MetadataToken)),
				Is.EqualTo(original.Properties.Select(p => p.MetadataToken.ToInt32())));
			Assert.That(declaration.Members.OfType<PropertyDeclaration>().Count(), Is.EqualTo(propertySyntax ? 1 : 0), code);
			if (propertySyntax)
			{
				var property = type.Properties.Single();
				Assert.That(TypeSystemAstBuilder.CanUsePropertySyntax(property), Is.True);
				var node = declaration.Members.OfType<PropertyDeclaration>().Single();
				Assert.That(node.GetSymbol(), Is.SameAs(property));
				Assert.That(node.Name, Is.EqualTo(Name(property, "prop", tokenize)));
				Assert.That(node.Modifiers.HasFlag(Modifiers.Virtual), Is.True);
				Assert.That(node.Modifiers.HasFlag(Modifiers.Override), Is.False);
				Assert.That(node.Modifiers.HasFlag(Modifiers.Sealed), Is.False);
				Assert.That(declaration.Members.OfType<MethodDeclaration>(), Is.Empty, code);
			}
			else
			{
				foreach (var property in type.Properties)
					Assert.That(TypeSystemAstBuilder.CanUsePropertySyntax(property), Is.False);
				Assert.That(declaration.Members.OfType<MethodDeclaration>().Count(), Is.EqualTo(original.Methods.Count(m => !m.IsConstructor)));
				foreach (var originalMethod in original.Methods.Where(m => !m.IsConstructor))
				{
					var method = type.GetMethods(m => m.Name == originalMethod.Name, GetMemberOptions.IgnoreInheritedMembers)
						.Concat(type.GetAccessors(m => m.Name == originalMethod.Name, GetMemberOptions.IgnoreInheritedMembers)).Single();
					Assert.That(method.IsAccessor, Is.EqualTo(original.Properties.Count != 0));
					var node = declaration.Members.OfType<MethodDeclaration>().Single(m => m.GetSymbol() == method);
					Assert.That(node.GetSymbol(), Is.SameAs(method));
					Assert.That(MetadataTokens.GetToken(method.MetadataToken), Is.EqualTo(originalMethod.MetadataToken.ToInt32()));
					Assert.That(node.Name, Is.EqualTo(Name(method, "method", tokenize)));
					Assert.That(node.Modifiers.HasFlag(Modifiers.Override), Is.EqualTo(!originalMethod.IsNewSlot));
					Assert.That(node.Modifiers.HasFlag(Modifiers.Virtual), Is.EqualTo(originalMethod.IsNewSlot));
					Assert.That(node.Modifiers.HasFlag(Modifiers.Sealed), Is.EqualTo(originalMethod.IsFinal));
				}
			}
			foreach (var originalMethod in original.Methods.Where(m => !m.IsConstructor))
			{
				bool getter = originalMethod.Name == "get_Title";
				var caller = tree.Descendants.OfType<MethodDeclaration>().Single(m => m.GetSymbol()?.Name == "Call" + type.Name + (getter ? "Getter" : "Setter"));
				var reference = caller.Body.Descendants.OfType<MemberReferenceExpression>().Single();
				var call = reference.Parent is InvocationExpression invocation ? invocation : (Expression)reference;
				var result = call.GetResolveResult() as MemberResolveResult;
				Assert.That(result, Is.Not.Null, caller.ToString());
				if (propertySyntax)
				{
					Assert.That(result.Member, Is.SameAs(type.Properties.Single()));
					Assert.That(reference.MemberName, Is.EqualTo(Name(result.Member, "prop", tokenize)));
					Assert.That(caller.Body.Descendants.OfType<InvocationExpression>(), Is.Empty, caller.ToString());
				}
				else
				{
					var method = declaration.Members.OfType<MethodDeclaration>().Single(m => m.GetSymbol()?.Name == originalMethod.Name);
					Assert.That(reference.Parent, Is.TypeOf<InvocationExpression>(), caller.ToString());
					Assert.That(result.Member.MemberDefinition, Is.SameAs(method.GetSymbol()));
					Assert.That(result.Member.MetadataToken, Is.EqualTo(((IMethod)method.GetSymbol()).MetadataToken));
					Assert.That(reference.MemberName, Is.EqualTo(method.Name));
				}
			}
		}
		if (!tokenize)
			AssertCompileBack(code, assembly, baseHasProperty: false, checkFamily: !newSlot);

		void AddType(string name, Cecil.TypeReference parent, bool hasProperty, bool final)
		{
			var type = new Cecil.TypeDefinition("AccessorFixture", name, Cecil.TypeAttributes.Public, parent);
			module.Types.Add(type);
			var property = hasProperty ? new Cecil.PropertyDefinition("Title", Cecil.PropertyAttributes.None, module.TypeSystem.Int32) : null;
			if (property != null)
				type.Properties.Add(property);
			foreach (var source in baseType.Methods.Where(m => !m.IsConstructor))
			{
				var method = new Cecil.MethodDefinition(source.Name, source.Attributes, source.ReturnType);
				method.IsNewSlot = parent == module.TypeSystem.Object;
				method.IsFinal = final;
				method.IsSpecialName = hasProperty;
				foreach (var parameter in source.Parameters)
					method.Parameters.Add(new Cecil.ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
				bool getter = method.Name == "get_Title";
				if (getter)
					Emit(method, OpCodes.Ldc_I4_3);
				Emit(method, OpCodes.Ret);
				type.Methods.Add(method);
				if (property != null)
				{
					if (getter)
						property.GetMethod = method;
					else
						property.SetMethod = method;
				}
				var caller = new Cecil.MethodDefinition("Call" + name + (getter ? "Getter" : "Setter"), Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, method.ReturnType);
				caller.Parameters.Add(new Cecil.ParameterDefinition("instance", Cecil.ParameterAttributes.None, type));
				Emit(caller, OpCodes.Ldarg_0);
				if (!getter)
					Emit(caller, OpCodes.Ldc_I4_1);
				Call(caller, method);
				Emit(caller, OpCodes.Ret);
				module.Types.Single(t => t.Name == "CallSites").Methods.Add(caller);
			}
		}
	}

	static string Name(IMember member, string prefix, bool tokenize)
		=> tokenize ? $"{prefix}_{MetadataTokens.GetToken(member.MetadataToken):X8}" : member.Name;

	static void AssertCompileBack(string code, Cecil.AssemblyDefinition original, bool baseHasProperty, bool checkFamily = false)
	{
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
			.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
		var compilation = CSharpCompilation.Create("AccessorOverrideRecompiled",
			new[] { CSharpSyntaxTree.ParseText(code) }, references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using var output = new MemoryStream();
		var result = compilation.Emit(output);
		Assert.That(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty,
			"Property/accessor overrides must compile without CS0115, CS0545 or CS0546.\n" + code);
		Assert.That(result.Success, Is.True);
		output.Position = 0;
		using var rebuilt = Cecil.AssemblyDefinition.ReadAssembly(output);
		foreach (var type in original.MainModule.Types.Where(t => t.Name == "Derived" || checkFamily && (t.Name == "Base" || t.Name == "Leaf" || t.Name == "Sibling")))
		{
			var recompiledType = rebuilt.MainModule.Types.Single(t => t.Name == type.Name);
			Assert.That(recompiledType.Properties.Count, Is.EqualTo(baseHasProperty ? 1 : 0));
			foreach (var method in type.Methods.Where(m => !m.IsConstructor))
			{
				var recompiled = recompiledType.Methods.Single(m => m.Name == method.Name);
				Assert.That(recompiled.IsVirtual, Is.True);
				Assert.That(recompiled.IsNewSlot, Is.EqualTo(method.IsNewSlot));
				Assert.That(recompiled.IsFinal, Is.EqualTo(method.IsFinal));
				Assert.That(recompiled.ReturnType.FullName, Is.EqualTo(method.ReturnType.FullName));
				Assert.That(recompiled.Parameters.Select(p => p.ParameterType.FullName), Is.EqualTo(method.Parameters.Select(p => p.ParameterType.FullName)));
			}
		}
	}

	static Cecil.AssemblyDefinition CreateAssembly(int derivedAccessors, int baseAccessors, bool baseHasProperty)
	{
		var assembly = Cecil.AssemblyDefinition.CreateAssembly(
			new Cecil.AssemblyNameDefinition("AccessorOverride", new Version(1, 0)), "AccessorOverride.dll", Cecil.ModuleKind.Dll);
		var module = assembly.MainModule;
		var core = (Cecil.AssemblyNameReference)module.TypeSystem.CoreLibrary;
		var runtime = typeof(object).Assembly.GetName();
		core.Name = runtime.Name;
		core.Version = runtime.Version;
		core.PublicKeyToken = runtime.GetPublicKeyToken();
		var baseType = new Cecil.TypeDefinition("AccessorFixture", "Base", Cecil.TypeAttributes.Public, module.TypeSystem.Object);
		var derivedType = new Cecil.TypeDefinition("AccessorFixture", "Derived", Cecil.TypeAttributes.Public, baseType);
		var callers = new Cecil.TypeDefinition("AccessorFixture", "CallSites", Cecil.TypeAttributes.Public, module.TypeSystem.Object);
		module.Types.Add(baseType);
		module.Types.Add(derivedType);
		module.Types.Add(callers);
		AddConstructor(baseType, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)));
		var constructor = AddConstructor(derivedType, baseType.Methods.Single(m => m.IsConstructor));
		AddAccessors(baseType, baseAccessors, 0, baseHasProperty ? "Title" : null);
		AddAccessors(derivedType, derivedAccessors, baseAccessors, baseHasProperty ? "Title" : "propertyTitle");
		var getter = derivedType.Methods.SingleOrDefault(m => m.Name == "get_Title");
		var setter = derivedType.Methods.SingleOrDefault(m => m.Name == "set_Title");
		if (getter != null)
		{
			var probe = AddProbe("ProbeRecursivePattern", module.TypeSystem.Int32, module.TypeSystem.Object);
			probe.Body.InitLocals = true;
			probe.Body.Variables.Add(new Cecil.Cil.VariableDefinition(derivedType));
			probe.Body.Variables.Add(new Cecil.Cil.VariableDefinition(module.TypeSystem.Int32));
			var notMatched = Cecil.Cil.Instruction.Create(OpCodes.Ldc_I4_M1);
			Emit(probe, OpCodes.Ldarg_0);
			probe.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Isinst, derivedType));
			Emit(probe, OpCodes.Stloc_0);
			Emit(probe, OpCodes.Ldloc_0);
			probe.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Brfalse, notMatched));
			Emit(probe, OpCodes.Ldloc_0);
			Call(probe, getter);
			Emit(probe, OpCodes.Stloc_1);
			Emit(probe, OpCodes.Ldloc_1);
			probe.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Call,
				module.ImportReference(typeof(Console).GetMethod(nameof(Console.WriteLine), new[] { typeof(int) }))));
			Emit(probe, OpCodes.Ldloc_1);
			Emit(probe, OpCodes.Ret);
			probe.Body.Instructions.Add(notMatched);
			Emit(probe, OpCodes.Ret);
		}
		if (setter != null)
		{
			var probe = AddProbe("ProbeObjectInitializer", derivedType, module.TypeSystem.Int32);
			probe.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Newobj, constructor));
			Emit(probe, OpCodes.Dup);
			Emit(probe, OpCodes.Ldarg_0);
			Call(probe, setter);
			Emit(probe, OpCodes.Ret);
		}
		if (getter != null && setter != null)
		{
			var compound = AddProbe("ProbeCompoundAssignment", module.TypeSystem.Void, derivedType, module.TypeSystem.Int32);
			Emit(compound, OpCodes.Ldarg_0);
			Emit(compound, OpCodes.Dup);
			Call(compound, getter);
			Emit(compound, OpCodes.Ldarg_1);
			Emit(compound, OpCodes.Add);
			Call(compound, setter);
			Emit(compound, OpCodes.Ret);
			var increment = AddProbe("ProbeIncrement", module.TypeSystem.Void, derivedType);
			Emit(increment, OpCodes.Ldarg_0);
			Emit(increment, OpCodes.Dup);
			Call(increment, getter);
			Emit(increment, OpCodes.Ldc_I4_1);
			Emit(increment, OpCodes.Add);
			Call(increment, setter);
			Emit(increment, OpCodes.Ret);
		}
		return assembly;

		Cecil.MethodDefinition AddConstructor(Cecil.TypeDefinition type, Cecil.MethodReference baseConstructor)
		{
			var constructor = new Cecil.MethodDefinition(".ctor",
				Cecil.MethodAttributes.Public | Cecil.MethodAttributes.SpecialName | Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
			Emit(constructor, OpCodes.Ldarg_0);
			constructor.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Call, baseConstructor));
			Emit(constructor, OpCodes.Ret);
			type.Methods.Add(constructor);
			return constructor;
		}

		Cecil.MethodDefinition AddProbe(string name, Cecil.TypeReference returnType, params Cecil.TypeReference[] parameterTypes)
		{
			var method = new Cecil.MethodDefinition(name, Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, returnType);
			foreach (var parameterType in parameterTypes)
				method.Parameters.Add(new Cecil.ParameterDefinition(parameterType));
			callers.Methods.Add(method);
			return method;
		}

		void AddAccessors(Cecil.TypeDefinition type, int available, int overridden, string propertyName)
		{
			var property = propertyName == null ? null : new Cecil.PropertyDefinition(propertyName, Cecil.PropertyAttributes.None, module.TypeSystem.Int32);
			if (property != null)
				type.Properties.Add(property);
			foreach (int kind in new[] { Getter, Setter }.Where(kind => (available & kind) != 0))
			{
				bool getter = kind == Getter;
				var attributes = Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Virtual | Cecil.MethodAttributes.SpecialName | Cecil.MethodAttributes.HideBySig;
				if ((overridden & kind) == 0)
					attributes |= Cecil.MethodAttributes.NewSlot;
				var method = new Cecil.MethodDefinition(getter ? "get_Title" : "set_Title", attributes, getter ? module.TypeSystem.Int32 : module.TypeSystem.Void);
				if (!getter)
					method.Parameters.Add(new Cecil.ParameterDefinition("value", Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
				var attribute = new Cecil.CustomAttribute(module.ImportReference(typeof(System.Diagnostics.DebuggerNonUserCodeAttribute).GetConstructor(Type.EmptyTypes)));
				method.CustomAttributes.Add(attribute);
				if (getter)
					method.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldc_I4, type == baseType ? 1 : 2));
				method.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ret));
				type.Methods.Add(method);
				if (property != null)
				{
					if (getter)
						property.GetMethod = method;
					else
						property.SetMethod = method;
				}
				var caller = new Cecil.MethodDefinition("Call" + type.Name + (getter ? "Getter" : "Setter"),
					Cecil.MethodAttributes.Public | Cecil.MethodAttributes.Static, method.ReturnType);
				caller.Parameters.Add(new Cecil.ParameterDefinition("instance", Cecil.ParameterAttributes.None, type));
				if (!getter)
					caller.Parameters.Add(new Cecil.ParameterDefinition("value", Cecil.ParameterAttributes.None, module.TypeSystem.Int32));
				caller.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldarg_0));
				if (!getter)
					caller.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ldarg_1));
				caller.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Callvirt, method));
				caller.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Ret));
				callers.Methods.Add(caller);
			}
		}
	}

	static void Emit(Cecil.MethodDefinition method, Cecil.Cil.OpCode opcode)
		=> method.Body.Instructions.Add(Cecil.Cil.Instruction.Create(opcode));

	static void Call(Cecil.MethodDefinition method, Cecil.MethodReference target)
		=> method.Body.Instructions.Add(Cecil.Cil.Instruction.Create(OpCodes.Callvirt, target));
}
