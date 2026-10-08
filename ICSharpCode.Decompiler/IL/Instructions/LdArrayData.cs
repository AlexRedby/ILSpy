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

#nullable enable

using System;
using System.Collections.Immutable;

using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.IL
{
	partial class LdArrayData
	{
		public LdArrayData(IType type, ImmutableArray<byte> data, int length) : base(OpCode.LdArrayData)
		{
			this.type = type ?? throw new ArgumentNullException(nameof(type));
			if (data.IsDefault)
				throw new ArgumentException("Array data must be initialized.", nameof(data));
			if (length <= 0)
				throw new ArgumentOutOfRangeException(nameof(length));
			Data = data;
			Length = length;
		}

		public ImmutableArray<byte> Data { get; }
		public int Length { get; }

		protected override void WriteToCore(ITextOutput output, ILAstWritingOptions options)
		{
			WriteILRange(output, options);
			output.Write(OpCode);
			output.Write(' ');
			Type.WriteTo(output);
			output.Write($" ({Length} elements, {Data.Length} bytes)");
		}
	}
}
