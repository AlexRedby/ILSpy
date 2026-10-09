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

namespace ICSharpCode.Decompiler.Tests.TestCases.Correctness
{
	public class GenericLocalFunctionNames
	{
		public static void Main()
		{
			Generic<Guid>.Run("captured");
			Generic<DateTime>.Run(23L);
		}

		class Generic<T1>
		{
			public static void Run<T2>(T2 value)
			{
				Console.WriteLine(Shadow<int, decimal>());
				Func<string> shadowDelegate = Shadow<double, bool>;
				Console.WriteLine(shadowDelegate());
				Console.WriteLine(Capture<DayOfWeek>());
				Console.WriteLine(Nested<int>());
				Func<string> nestedDelegate = Nested<bool>;
				Console.WriteLine(nestedDelegate());

#pragma warning disable CS8387
#if CS80
				static string Shadow<T2, T3>() where T2 : IConvertible where T3 : struct
#else
				string Shadow<T2, T3>() where T2 : IConvertible where T3 : struct
#endif
				{
					return typeof(T1).Name + "/" + typeof(T2).Name + "/" + typeof(T3).Name;
				}

				string Nested<T2>() where T2 : struct
				{
					return typeof(T2).Name + "/" + Capture<T2>();
				}
#pragma warning restore CS8387

				string Capture<T3>()
				{
					return typeof(T1).Name + "/" + typeof(T2).Name + "/" + typeof(T3).Name + "/" + value;
				}
			}
		}
	}
}
