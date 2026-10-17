using System;
#if EXPECTED_OUTPUT
using System.Runtime.CompilerServices;
#endif
namespace Issue3465
{
	internal class Program
	{
		private static Program programNull;

		private static Program GetProgram()
		{
			return null;
		}

		private static bool Test3465()
		{
			Program program = GetProgram();
#if EXPECTED_OUTPUT
			return (nuint)System.Runtime.CompilerServices.Unsafe.As<Program, UIntPtr>(ref program) > (nuint)System.Runtime.CompilerServices.Unsafe.As<Program, UIntPtr>(ref System.Runtime.CompilerServices.Unsafe.AsRef<Program>(programNull));
#else
			return System.Runtime.CompilerServices.Unsafe.As<Program, UIntPtr>(ref program) > System.Runtime.CompilerServices.Unsafe.As<Program, UIntPtr>(ref programNull);
#endif
		}
	}
}
