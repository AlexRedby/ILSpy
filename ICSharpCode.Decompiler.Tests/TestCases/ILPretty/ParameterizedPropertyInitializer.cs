using System.Runtime.InteropServices;

public class ParameterizedPropertyInitializer
{
	// C# has no syntax for parameterized property 'Foo'.
	public int get_Foo(int x)
	{
		return x;
	}

	public void set_Foo(int x, int value)
	{
	}

	// C# has no syntax for parameterized property 'Bar'.
	public int get_Bar(int x = 7)
	{
		return x;
	}

	public void set_Bar([Optional][DefaultParameterValue(7)] int x, int value)
	{
	}

	public static void Consume(ParameterizedPropertyInitializer p)
	{
	}

	public static void Use()
	{
		ParameterizedPropertyInitializer parameterizedPropertyInitializer = new ParameterizedPropertyInitializer();
		parameterizedPropertyInitializer.set_Foo(7, 5);
		Consume(parameterizedPropertyInitializer);
	}

	public static void UseOptional()
	{
		ParameterizedPropertyInitializer parameterizedPropertyInitializer = new ParameterizedPropertyInitializer();
		parameterizedPropertyInitializer.set_Bar(7, 5);
		Consume(parameterizedPropertyInitializer);
	}
}
