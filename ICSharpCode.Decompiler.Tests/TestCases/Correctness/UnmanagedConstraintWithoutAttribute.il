.assembly extern mscorlib
{
	.publickeytoken = (B7 7A 5C 56 19 34 E0 89)
	.ver 4:0:0:0
}
.assembly UnmanagedConstraintWithoutAttribute { }
.module UnmanagedConstraintWithoutAttribute.exe

.class public auto ansi abstract sealed Program extends [mscorlib]System.Object
{
	.method public static void Main() cil managed
	{
		.entrypoint
		.maxstack 1
		call int32 Program::WithoutAttribute<int32>()
		call void [mscorlib]System.Console::WriteLine(int32)
		call int32 class WithoutAttribute`1<float64>::Size()
		call void [mscorlib]System.Console::WriteLine(int32)
		call int32 class RenamedAttribute`1<int64>::Size()
		call void [mscorlib]System.Console::WriteLine(int32)
		ret
	}

	.method public static int32 CheckedSize<valuetype .ctor (class [mscorlib]System.ValueType modreq([mscorlib]System.Runtime.InteropServices.UnmanagedType)) T>() cil managed
	{
		.param type T
		.custom instance void System.Runtime.CompilerServices.IsUnmanagedAttribute::.ctor() = (01 00 00 00)
		.maxstack 1
		sizeof !!0
		ret
	}

	.method public static int32 WithoutAttribute<valuetype .ctor (class [mscorlib]System.ValueType modreq([mscorlib]System.Runtime.InteropServices.UnmanagedType)) T>() cil managed
	{
		.maxstack 1
		call int32 Program::CheckedSize<!!0>()
		ret
	}
}

.class public auto ansi abstract sealed WithoutAttribute`1<valuetype .ctor (class [mscorlib]System.ValueType modreq([mscorlib]System.Runtime.InteropServices.UnmanagedType)) T> extends [mscorlib]System.Object
{
	.method public static int32 Size() cil managed
	{
		.maxstack 1
		call int32 Program::CheckedSize<!0>()
		ret
	}
}

.class public auto ansi abstract sealed RenamedAttribute`1<valuetype .ctor (class [mscorlib]System.ValueType modreq([mscorlib]System.Runtime.InteropServices.UnmanagedType)) T> extends [mscorlib]System.Object
{
	.param type T
	.custom instance void MarkerAttribute::.ctor() = (01 00 00 00)
	.method public static int32 Size() cil managed
	{
		.maxstack 1
		call int32 Program::CheckedSize<!0>()
		ret
	}
}

.class public auto ansi abstract sealed CanonicalAttribute`1<valuetype .ctor (class [mscorlib]System.ValueType modreq([mscorlib]System.Runtime.InteropServices.UnmanagedType)) T> extends [mscorlib]System.Object
{
	.param type T
	.custom instance void System.Runtime.CompilerServices.IsUnmanagedAttribute::.ctor() = (01 00 00 00)
}

.class public auto ansi abstract sealed StructOnly`1<valuetype .ctor T> extends [mscorlib]System.Object { }
.class public auto ansi abstract sealed CanonicalAttributeOnly`1<valuetype .ctor T> extends [mscorlib]System.Object
{
	.param type T
	.custom instance void System.Runtime.CompilerServices.IsUnmanagedAttribute::.ctor() = (01 00 00 00)
}
.class public auto ansi abstract sealed OptionalModifier`1<valuetype .ctor (class [mscorlib]System.ValueType modopt([mscorlib]System.Runtime.InteropServices.UnmanagedType)) T> extends [mscorlib]System.Object { }
.class public auto ansi abstract sealed WrongModifier`1<valuetype .ctor (class [mscorlib]System.ValueType modreq([mscorlib]System.Runtime.CompilerServices.IsVolatile)) T> extends [mscorlib]System.Object { }
.class public auto ansi abstract sealed WrongNamespace`1<valuetype .ctor (class [mscorlib]System.ValueType modreq(Other.UnmanagedType)) T> extends [mscorlib]System.Object { }
.class public auto ansi abstract sealed EnumElement`1<valuetype .ctor (class [mscorlib]System.Enum modreq([mscorlib]System.Runtime.InteropServices.UnmanagedType)) T> extends [mscorlib]System.Object { }
.class public auto ansi abstract sealed MissingValueFlag`1<(class [mscorlib]System.ValueType modreq([mscorlib]System.Runtime.InteropServices.UnmanagedType)) T> extends [mscorlib]System.Object { }
.class public auto ansi abstract sealed RenamedAttributeOnly`1<valuetype .ctor T> extends [mscorlib]System.Object
{
	.param type T
	.custom instance void MarkerAttribute::.ctor() = (01 00 00 00)
}

.class public auto ansi sealed MarkerAttribute extends [mscorlib]System.Attribute
{
	.method public specialname rtspecialname instance void .ctor() cil managed
	{
		ldarg.0
		call instance void [mscorlib]System.Attribute::.ctor()
		ret
	}
}

.class public auto ansi sealed System.Runtime.CompilerServices.IsUnmanagedAttribute extends [mscorlib]System.Attribute
{
	.method public specialname rtspecialname instance void .ctor() cil managed
	{
		ldarg.0
		call instance void [mscorlib]System.Attribute::.ctor()
		ret
	}
}

.class public auto ansi sealed Other.UnmanagedType extends [mscorlib]System.Object { }
