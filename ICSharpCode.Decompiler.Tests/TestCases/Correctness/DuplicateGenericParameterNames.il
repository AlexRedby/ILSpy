.assembly extern mscorlib
{
	.publickeytoken = (B7 7A 5C 56 19 34 E0 89)
	.ver 4:0:0:0
}
.assembly DuplicateGenericParameterNames { }
.module DuplicateGenericParameterNames.exe

.class public auto ansi abstract sealed Program extends [mscorlib]System.Object
{
	.method public static void Main() cil managed
	{
		.entrypoint
		.maxstack 8
		call void class Duplicate`2<int32, string>::Print()
		ldc.i4.7
		ldstr "second"
		call !1 class Duplicate`2<int32, string>::Select(!0, !1)
		call void [mscorlib]System.Console::WriteLine(string)
		call void Program::Method<float64, bool>()
		call void class Reserved`4<int32, string, bool, float64>::Print()
		call void class ReverseReserved`3<bool, int32, string>::Print()
		call void class Outer`1/Inner`1<int32, string>::Print()
		call void class Outer`1/Inner`1/Leaf`1<int32, string, bool>::Print()
		call void class Outer`1/Inherited<int32>::Print()
		call void class Outer`1<int32>::Method<string, bool>()
		call void class Constrained`2<object, string>::Print()
		call void Program::ConstrainedMethod<object, string>()
		call void class Valid`2<int32, string>::Method<bool, float64>()
		ret
	}

	.method public static void Method<A, A>() cil managed
	{
		.maxstack 8
		ldtoken !!0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !!1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ret
	}

	.method public static void ConstrainedMethod<class A, (!!0) A>() cil managed
	{
		.maxstack 8
		ldtoken !!0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !!1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ret
	}
}

.class public auto ansi abstract sealed Duplicate`2<A, A> extends [mscorlib]System.Object
{
	.field public static !0 First
	.field public static !1 Second

	.method public static !1 Select(!0 first, !1 second) cil managed
	{
		ldarg.1
		ret
	}

	.method public static void Print() cil managed
	{
		.maxstack 8
		ldtoken !0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ret
	}
}

.class public auto ansi abstract sealed Reserved`4<A, A, A1, A> extends [mscorlib]System.Object
{
	.method public static void Print() cil managed
	{
		.maxstack 8
		ldtoken !0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !2
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !3
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ret
	}
}

.class public auto ansi abstract sealed ReverseReserved`3<A1, A, A> extends [mscorlib]System.Object
{
	.method public static void Print() cil managed
	{
		.maxstack 8
		ldtoken !0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !2
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ret
	}
}

.class public auto ansi abstract sealed Outer`1<A> extends [mscorlib]System.Object
{
	.method public static void Method<A, A1>() cil managed
	{
		.maxstack 8
		ldtoken !0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !!0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !!1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ret
	}

	// The inherited metadata row has a different name but denotes the outer slot.
	.class nested public auto ansi abstract sealed Inner`1<B, A> extends [mscorlib]System.Object
	{
		.method public static void Print() cil managed
		{
			.maxstack 8
			ldtoken !0
			call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
			call void [mscorlib]System.Console::WriteLine(object)
			ldtoken !1
			call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
			call void [mscorlib]System.Console::WriteLine(object)
			ret
		}

		.class nested public auto ansi abstract sealed Leaf`1<B, C, A> extends [mscorlib]System.Object
		{
			.method public static void Print() cil managed
			{
				.maxstack 8
				ldtoken !0
				call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
				call void [mscorlib]System.Console::WriteLine(object)
				ldtoken !1
				call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
				call void [mscorlib]System.Console::WriteLine(object)
				ldtoken !2
				call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
				call void [mscorlib]System.Console::WriteLine(object)
				ret
			}
		}
	}

	.class nested public auto ansi abstract sealed Inherited<B> extends [mscorlib]System.Object
	{
		.method public static void Print() cil managed
		{
			.maxstack 8
			ldtoken !0
			call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
			call void [mscorlib]System.Console::WriteLine(object)
			ret
		}
	}
}

.class public auto ansi abstract sealed Constrained`2<class A, (!0) A> extends [mscorlib]System.Object
{
	.method public static void Print() cil managed
	{
		.maxstack 8
		ldtoken !0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ret
	}
}

.class public auto ansi abstract sealed Valid`2<T, A1> extends [mscorlib]System.Object
{
	.method public static void Method<U, V>() cil managed
	{
		.maxstack 8
		ldtoken !0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !!0
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ldtoken !!1
		call class [mscorlib]System.Type [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
		call void [mscorlib]System.Console::WriteLine(object)
		ret
	}
}
