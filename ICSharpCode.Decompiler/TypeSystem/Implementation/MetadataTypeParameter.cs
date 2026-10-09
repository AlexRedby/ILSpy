// Copyright (c) 2018 Daniel Grunwald
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	sealed class MetadataTypeParameter : AbstractTypeParameter
	{
		readonly MetadataModule module;
		readonly GenericParameterHandle handle;

		readonly GenericParameterAttributes attr;

		// lazy-loaded:
		IReadOnlyList<TypeConstraint> constraints;
		byte unmanagedConstraint = ThreeState.Unknown;
		const byte nullabilityNotYetLoaded = 255;
		byte nullabilityConstraint = nullabilityNotYetLoaded;

		public static ITypeParameter[] Create(MetadataModule module, ITypeDefinition copyFromOuter, IEntity owner, GenericParameterHandleCollection handles)
		{
			return Create(module, owner, handles, copyFromOuter.TypeParameters, copyFromOuter.TypeParameterCount);
		}

		public static ITypeParameter[] Create(MetadataModule module, IEntity owner, GenericParameterHandleCollection handles)
		{
			if (handles.Count == 0)
				return Empty<ITypeParameter>.Array;
			var outerTps = owner is IMethod method ? method.DeclaringTypeDefinition.TypeParameters : Empty<ITypeParameter>.Array;
			return Create(module, owner, handles, outerTps, 0);
		}

		static ITypeParameter[] Create(MetadataModule module, IEntity owner, GenericParameterHandleCollection handles,
			IReadOnlyList<ITypeParameter> outerTps, int inheritedCount)
		{
			if (handles.Count == 0)
				return Empty<ITypeParameter>.Array;
			var usedNames = new HashSet<string>(outerTps.Select(p => p.Name), StringComparer.Ordinal);
			var reservedNames = new HashSet<string>(usedNames, StringComparer.Ordinal);
			// Reserve original names before allocating aliases so a suffix never steals another slot's name.
			int i = 0;
			foreach (var handle in handles)
			{
				if (i++ >= inheritedCount)
					reservedNames.Add(module.GetString(module.metadata.GetGenericParameter(handle).Name));
			}
			var tps = new ITypeParameter[handles.Count];
			i = 0;
			foreach (var handle in handles)
			{
				if (i < inheritedCount)
				{
					tps[i] = outerTps[i];
				}
				else
				{
					var gp = module.metadata.GetGenericParameter(handle);
					Debug.Assert(gp.Index == i);
					string name = module.GetString(gp.Name);
					if (!usedNames.Add(name))
					{
						string baseName = name;
						int suffix = 1;
						do
						{
							name = baseName + (suffix++).ToString(CultureInfo.InvariantCulture);
						} while (reservedNames.Contains(name) || !usedNames.Add(name));
					}
					tps[i] = new MetadataTypeParameter(module, owner, i, name, handle, gp.Attributes);
				}
				i++;
			}
			return tps;
		}

		private MetadataTypeParameter(MetadataModule module, IEntity owner, int index, string name,
			GenericParameterHandle handle, GenericParameterAttributes attr)
			: base(owner, index, name, GetVariance(attr))
		{
			this.module = module;
			this.handle = handle;
			this.attr = attr;
		}

		private static VarianceModifier GetVariance(GenericParameterAttributes attr)
		{
			switch (attr & GenericParameterAttributes.VarianceMask)
			{
				case GenericParameterAttributes.Contravariant:
					return VarianceModifier.Contravariant;
				case GenericParameterAttributes.Covariant:
					return VarianceModifier.Covariant;
				default:
					return VarianceModifier.Invariant;
			}
		}

		public GenericParameterHandle MetadataToken => handle;

		public override IEnumerable<IAttribute> GetAttributes()
		{
			var metadata = module.metadata;
			var gp = metadata.GetGenericParameter(handle);

			var attributes = gp.GetCustomAttributes();
			var b = new AttributeListBuilder(module, attributes.Count);
			b.Add(attributes, SymbolKind.TypeParameter);
			return b.Build();
		}

		public override bool HasDefaultConstructorConstraint => (attr & GenericParameterAttributes.DefaultConstructorConstraint) != 0;
		public override bool HasReferenceTypeConstraint => (attr & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
		public override bool HasValueTypeConstraint => (attr & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
		public override bool AllowsRefLikeType => (attr & SRMExtensions.AllowByRefLike) != 0;

		public override bool HasUnmanagedConstraint {
			get {
				if (unmanagedConstraint == ThreeState.Unknown)
				{
					unmanagedConstraint = ThreeState.From(LoadUnmanagedConstraint());
				}
				return unmanagedConstraint == ThreeState.True;
			}
		}

		private bool LoadUnmanagedConstraint()
		{
			if ((module.TypeSystemOptions & TypeSystemOptions.UnmanagedConstraints) == 0)
				return false;
			var metadata = module.metadata;
			var gp = metadata.GetGenericParameter(handle);
			return gp.GetCustomAttributes().HasKnownAttribute(metadata, KnownAttribute.IsUnmanaged);
		}

		public override Nullability NullabilityConstraint {
			get {
				if (nullabilityConstraint == nullabilityNotYetLoaded)
				{
					nullabilityConstraint = (byte)LoadNullabilityConstraint();
				}
				return (Nullability)nullabilityConstraint;
			}
		}

		Nullability LoadNullabilityConstraint()
		{
			if (!module.ShouldDecodeNullableAttributes(Owner))
				return Nullability.Oblivious;

			var metadata = module.metadata;
			var gp = metadata.GetGenericParameter(handle);

			foreach (var handle in gp.GetCustomAttributes())
			{
				var customAttribute = metadata.GetCustomAttribute(handle);
				if (customAttribute.IsKnownAttribute(metadata, KnownAttribute.Nullable))
				{
					var attrVal = customAttribute.DecodeValue(module.TypeProvider);
					if (attrVal.FixedArguments.Length == 1)
					{
						if (attrVal.FixedArguments[0].Value is byte b && b <= 2)
						{
							return (Nullability)b;
						}
					}
				}
			}
			if (Owner is MetadataMethod method)
			{
				return method.NullableContext;
			}
			else if (Owner is ITypeDefinition td)
			{
				return td.NullableContext;
			}
			else
			{
				return Nullability.Oblivious;
			}
		}

		public override IReadOnlyList<TypeConstraint> TypeConstraints {
			get {
				var constraints = LazyInit.VolatileRead(ref this.constraints);
				if (constraints == null)
				{
					constraints = LazyInit.GetOrSet(ref this.constraints, DecodeConstraints());
				}
				return constraints;
			}
		}

		private IReadOnlyList<TypeConstraint> DecodeConstraints()
		{
			var metadata = module.metadata;
			var gp = metadata.GetGenericParameter(handle);
			Nullability nullableContext;
			if (Owner is ITypeDefinition typeDef)
			{
				nullableContext = typeDef.NullableContext;
			}
			else if (Owner is MetadataMethod method)
			{
				nullableContext = method.NullableContext;
			}
			else
			{
				nullableContext = Nullability.Oblivious;
			}

			var constraintHandleCollection = gp.GetConstraints();
			var result = new List<TypeConstraint>(constraintHandleCollection.Count + 1);
			bool hasNonInterfaceConstraint = false;
			foreach (var constraintHandle in constraintHandleCollection)
			{
				var constraint = metadata.GetGenericParameterConstraint(constraintHandle);
				var attrs = constraint.GetCustomAttributes();
				var ty = module.ResolveType(constraint.Type, new GenericContext(Owner), attrs, nullableContext);
				if (attrs.Count == 0)
				{
					result.Add(new TypeConstraint(ty));
				}
				else
				{
					AttributeListBuilder b = new AttributeListBuilder(module);
					b.Add(attrs, SymbolKind.Constraint);
					result.Add(new TypeConstraint(ty, b.Build()));
				}
				hasNonInterfaceConstraint |= (ty.Kind != TypeKind.Interface);
			}
			if (this.HasValueTypeConstraint)
			{
				result.Add(new TypeConstraint(Compilation.FindType(KnownTypeCode.ValueType)));
			}
			else if (!hasNonInterfaceConstraint)
			{
				result.Add(new TypeConstraint(Compilation.FindType(KnownTypeCode.Object)));
			}
			return result;
		}

		public override int GetHashCode()
		{
			return 0x51fc5b83 ^ module.MetadataFile.GetHashCode() ^ handle.GetHashCode();
		}

		public override bool Equals(IType other)
		{
			return other is MetadataTypeParameter tp && handle == tp.handle && module.MetadataFile == tp.module.MetadataFile;
		}

		public override string ToString()
		{
			return $"{MetadataTokens.GetToken(handle):X8} {ReflectionName}";
		}
	}
}
